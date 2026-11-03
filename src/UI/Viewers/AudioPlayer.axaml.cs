using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NewToys.Shared.Services;

namespace NewToys.UI.Viewers;

/// <summary>
/// 查看器内的本地音乐播放器：播放列表 + 控制条 + LRC 歌词。
/// <para>
/// 🔴 播放能力由宿主注入 <see cref="IAudioPlayback"/>（UI 库 ⛔ 不引 NAudio / 平台库）；
/// 本控件只做界面、轮询位置、解析歌词。
/// </para>
/// <para>🔴 歌词是「行内高亮 + 自动滚动」，⛔ 不做逐字卡拉OK（需要逐字时间轴，LRC 不提供）。</para>
/// </summary>
public partial class AudioPlayer : UserControl
{
    private static readonly Regex TimeTag = new(@"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly List<(TimeSpan At, string Text, string Trans)> _lyrics = new();
    private readonly List<StackPanel> _lyricViews = new();

    private List<AudioTrackItem> _tracks = new();
    private int _current = -1;
    private bool _suppress;
    private bool _updating;
    private int _lyricIndex = -1;
    private PlayMode _mode = PlayMode.Order;
    private Bitmap? _cover;

    public AudioPlayer()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        // 频谱单独一张表（33 ms ≈ 30 fps）：⛔ 塞进 250 ms 的进度轮询里，柱高会一跳一跳
        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _spectrumTimer.Tick += (_, _) => Spectrum.Pump();

        // 离开视觉树（切页）⇒ 只停轮询，⛔ 不停播：音乐要能继续放（切回来时重新起表）。
        // 关闭标签页由宿主决定是否 Stop（本控件不揣测，避免切页与销毁分不清）。
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            _spectrumTimer.Stop();
            Spectrum.Hide();
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (Playback?.State == AudioPlayState.Playing) _timer.Start();
        };
    }

    private enum PlayMode
    {
        Order = 0,
        Single = 1,
        Shuffle = 2,
    }

    /// <summary>播放后端（宿主注入）。</summary>
    public IAudioPlayback? Playback { get; set; }

    /// <summary>
    /// 译文歌词取用器（宿主注入：拿到音频路径，返回译文 LRC 文本或 <c>null</c>）。
    /// <para>🔴 UI 库不认识「.zh.lrc」这类约定，读文件归宿主；本控件只负责按时间戳对齐与显示。</para>
    /// </summary>
    public Func<string, string?>? TranslationLoader { get; set; }

    /// <summary>
    /// 歌词取用器（宿主注入：返回「文本 + 是否带时间轴」）。
    /// <para>🔴 内嵌歌词（USLT）没有时间轴 ⇒ 静态整段显示，不高亮不滚动；<c>null</c> 文本 = 没有歌词。</para>
    /// </summary>
    public Func<string, (string? Text, bool Timed)>? LyricsLoader { get; set; }

    /// <summary>频谱数据源（宿主注入；后端不实现 <see cref="IAudioSpectrum"/> 时为 <c>null</c> ⇒ 不显示频谱）。</summary>
    public IAudioSpectrum? SpectrumSource
    {
        get => Spectrum.Source;
        set => Spectrum.Source = value;
    }

    /// <summary>播放模式（0 顺序 / 1 单曲 / 2 随机），供宿主存取续播快照。</summary>
    public int ModeValue
    {
        get => (int)_mode;
        set
        {
            _mode = (PlayMode)Math.Clamp(value, 0, 2);
            UpdateModeButton();
        }
    }

    /// <summary>当前位置（秒），供宿主写续播快照。</summary>
    public double PositionSeconds => Playback?.Position.TotalSeconds ?? 0;

    /// <summary>当前曲目在列表里的下标（-1 = 还没播）。</summary>
    public int CurrentIndex => _current;

    /// <summary>当前曲目变化（宿主记「最近打开」用）。</summary>
    public event Action<AudioTrackItem?>? TrackChanged;

    /// <summary>一句话状态（失败 / 播完 / 切歌）。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>当前曲目。</summary>
    public AudioTrackItem? Current => _current >= 0 && _current < _tracks.Count ? _tracks[_current] : null;

    /// <summary>播放列表里全部曲目（宿主拖入新文件时合并用）。</summary>
    public IReadOnlyList<AudioTrackItem> Tracks => _tracks;

    // ==================== 装载 ====================

    /// <summary>设置播放列表并立刻播第 <paramref name="startIndex"/> 首。</summary>
    public void SetPlaylist(IEnumerable<AudioTrackItem> tracks, int startIndex = 0, bool autoPlay = true)
    {
        _tracks = tracks.ToList();
        _suppress = true;
        try
        {
            TrackList.ItemsSource = _tracks;
            TrackList.SelectedIndex = -1;
        }
        finally { _suppress = false; }

        if (_tracks.Count == 0)
        {
            StatusChanged?.Invoke("播放列表是空的。");
            return;
        }

        PlayIndex(Math.Clamp(startIndex, 0, _tracks.Count - 1), autoPlay);
    }

    /// <summary>把追加的曲目并进当前列表（拖入更多文件时用）。</summary>
    public void AppendTracks(IEnumerable<AudioTrackItem> tracks)
    {
        foreach (var track in tracks)
        {
            if (_tracks.Any(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase))) continue;
            _tracks.Add(track);
        }

        _suppress = true;
        try { TrackList.ItemsSource = null; TrackList.ItemsSource = _tracks; }
        finally { _suppress = false; }
    }

    /// <summary>播放指定曲目。</summary>
    public void PlayIndex(int index, bool autoPlay = true)
    {
        if (Playback is null) { StatusChanged?.Invoke("⚠ 播放服务未就绪。"); return; }
        if (index < 0 || index >= _tracks.Count) return;

        var track = _tracks[index];
        if (!Playback.Open(track.Path, out var error))
        {
            StatusChanged?.Invoke("⚠ 打不开《" + track.Title + "》：" + error);
            return;
        }

        _current = index;
        _lyricIndex = -1;

        _suppress = true;
        try { TrackList.SelectedIndex = index; }
        finally { _suppress = false; }

        UpdateHeader(track);
        LoadLyrics(track.Path);

        if (autoPlay) Playback.Play();
        PlayButton.Content = Playback.State == AudioPlayState.Playing ? "⏸ 暂停" : "▶ 播放";

        _timer.Start();
        TrackChanged?.Invoke(track);
        StatusChanged?.Invoke("正在播放：" + track.Display);
        Tick();
    }

    /// <summary>开 / 关播放（供宿主工具条调用）。</summary>
    public void TogglePlay()
    {
        if (Playback is null) return;
        if (Playback.State == AudioPlayState.Playing) Playback.Pause();
        else Playback.Play();
        PlayButton.Content = Playback.State == AudioPlayState.Playing ? "⏸ 暂停" : "▶ 播放";
    }

    /// <summary>下一首（<paramref name="auto"/> = 播完自动切，响应「单曲循环」）。</summary>
    public void Next(bool auto = false)
    {
        if (_tracks.Count == 0) return;

        if (auto && _mode == PlayMode.Single) { PlayIndex(_current, true); return; }
        if (auto && _mode == PlayMode.Shuffle) { PlayIndex(Random.Shared.Next(_tracks.Count), true); return; }

        if (_current >= _tracks.Count - 1)
        {
            if (auto) { Stop(); StatusChanged?.Invoke("播放列表已播完。"); }
            else PlayIndex(0);
            return;
        }

        PlayIndex(_current + 1);
    }

    /// <summary>上一首。</summary>
    public void Previous()
    {
        if (_tracks.Count == 0) return;
        PlayIndex(_current <= 0 ? _tracks.Count - 1 : _current - 1);
    }

    /// <summary>停止并复位（换文件 / 关闭页面时调）。</summary>
    public void Stop()
    {
        _timer.Stop();
        _spectrumTimer.Stop();
        Spectrum.Hide();
        Playback?.Stop();
        PlayButton.Content = "▶ 播放";
        _updating = true;
        try { ProgressSlider.Value = 0; }
        finally { _updating = false; }
        PositionText.Text = "00:00";
    }

    /// <summary>音量 0~100（宿主从配置读出来后下发）。</summary>
    public void SetVolume(int volume)
    {
        var value = Math.Clamp(volume, 0, 100);
        _suppress = true;
        try { VolumeSlider.Value = value; }
        finally { _suppress = false; }
        if (Playback is not null) Playback.Volume = (float)(value / 100.0);
    }

    /// <summary>当前音量 0~100。</summary>
    public int Volume => (int)Math.Round(VolumeSlider.Value);

    /// <summary>
    /// 载入歌词。
    /// <para><paramref name="raw"/> = 原文 LRC；<paramref name="translation"/> = 译文 LRC（时间戳对齐，可空）。</para>
    /// <para>双行歌词（同一时间戳重复写原文 + 译文）也会被识别：第 2 行起当作译文。</para>
    /// <para><paramref name="staticText"/> = 无时间轴的整段歌词（内嵌 USLT）：逐行平铺，⛔ 不高亮不滚动。</para>
    /// </summary>
    public void SetLyrics(string? raw, string? translation = null, bool staticText = false)
    {
        _lyrics.Clear();
        _lyricViews.Clear();
        LyricPanel.Children.Clear();
        _lyricIndex = -1;

        if (string.IsNullOrWhiteSpace(raw)) return;

        if (staticText)
        {
            foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var text = line.Trim();
                if (text.Length == 0) continue;

                LyricPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 14,
                    Opacity = 0.85,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                });
            }
            return;
        }

        var originals = ParseLrc(raw);
        if (originals.Count == 0) return;

        // 译文：外置译文文件按时间戳对齐；同一时间戳的第 2 行起（双行写法）优先用文件自带的
        var transMap = new Dictionary<TimeSpan, string>();
        if (!string.IsNullOrWhiteSpace(translation))
        {
            foreach (var item in ParseLrc(translation))
                transMap.TryAdd(item.At, item.Text);
        }

        foreach (var group in originals.GroupBy(x => x.At).OrderBy(g => g.Key))
        {
            var lines = group.Select(x => x.Text).Where(t => t.Length > 0).ToList();
            if (lines.Count == 0) continue;

            var text = lines[0];
            var trans = lines.Count > 1
                ? string.Join(" ", lines.Skip(1))
                : (transMap.TryGetValue(group.Key, out var t) ? t : "");
            _lyrics.Add((group.Key, text, trans));
        }

        if (_lyrics.Count == 0) return;

        foreach (var item in _lyrics)
        {
            var panel = new StackPanel { Spacing = 2, Opacity = 0.55, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            panel.Children.Add(new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                FontSize = 14,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            });

            if (!string.IsNullOrWhiteSpace(item.Trans))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Trans,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Opacity = 0.7,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                });
            }

            _lyricViews.Add(panel);
            LyricPanel.Children.Add(panel);
        }
    }

    /// <summary>解析 LRC 文本为前提的（时间, 文本）序列（保留出现顺序，⛔ 不去重 —— 双行歌词要靠它配对）。</summary>
    private static List<(TimeSpan At, string Text)> ParseLrc(string raw)
    {
        var list = new List<(TimeSpan At, string Text)>();

        foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var matches = TimeTag.Matches(line);
            if (matches.Count == 0) continue;

            var text = TimeTag.Replace(line, "").Trim();
            if (text.Length == 0) continue;

            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = 0;
                if (match.Groups[3].Success)
                {
                    var digits = match.Groups[3].Value.PadRight(3, '0')[..3];
                    fraction = int.Parse(digits, CultureInfo.InvariantCulture);
                }
                list.Add((new TimeSpan(0, 0, minutes, seconds, fraction), text));
            }
        }

        return list;
    }

    // ==================== 轮询 ====================

    private void Tick()
    {
        if (Playback is null) return;

        var duration = Playback.Duration;
        var position = Playback.Position;

        var ratio = duration.TotalSeconds > 0.5
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
            : 0;

        _updating = true;
        try { ProgressSlider.Value = ratio; }
        finally { _updating = false; }

        PositionText.Text = Format(position);
        DurationText.Text = duration.TotalSeconds > 0.5 ? Format(duration) : "--:--";
        PlayButton.Content = Playback.State == AudioPlayState.Playing ? "⏸ 暂停" : "▶ 播放";

        UpdateLyric(position);
        SyncSpectrumTimer();
    }

    /// <summary>频谱表跟着播放状态走（暂停 / 停止 ⇒ 停表并收起，⛔ 留着柱子空转）。</summary>
    private void SyncSpectrumTimer()
    {
        var running = Playback?.State == AudioPlayState.Playing && SpectrumSource is not null;

        if (running)
        {
            if (!_spectrumTimer.IsEnabled) _spectrumTimer.Start();
            return;
        }

        _spectrumTimer.Stop();
        Spectrum.Hide();
    }

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private void UpdateLyric(TimeSpan position)
    {
        if (_lyrics.Count == 0) return;

        var index = -1;
        for (var i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].At > position) break;
            index = i;
        }

        if (index == _lyricIndex) return;
        _lyricIndex = index;

        for (var i = 0; i < _lyricViews.Count; i++)
        {
            var active = i == index;
            _lyricViews[i].Opacity = active ? 1 : 0.55;
            if (_lyricViews[i].Children.Count > 0 && _lyricViews[i].Children[0] is TextBlock original)
                original.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
            if (!active) continue;
            try { _lyricViews[i].BringIntoView(); }
            catch { /* 尚未布局 */ }
        }
    }

    private void UpdateHeader(AudioTrackItem track)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileName(track.Path) : track.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(track.Album)
            ? track.Artist
            : track.Artist + " · " + track.Album;
        TechText.Text = track.TechText;

        _cover?.Dispose();
        _cover = null;
        Cover.Source = null;
        CoverPlaceholder.IsVisible = true;

        if (track.Cover is not { Length: > 0 }) return;
        try
        {
            _cover = new Bitmap(new MemoryStream(track.Cover));
            Cover.Source = _cover;
            CoverPlaceholder.IsVisible = false;
        }
        catch { /* 封面坏了不影响播放 */ }
    }

    private void LoadLyrics(string path)
    {
        SetLyrics(null);
        try
        {
            var (text, timed) = LyricsLoader?.Invoke(path) ?? (null, true);
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowLyricPlaceholder();
                return;
            }

            SetLyrics(text, timed ? TranslationLoader?.Invoke(path) : null, staticText: !timed);
        }
        catch { /* 歌词是附属产物 */ }
    }

    /// <summary>没歌词时的空态（⛔ 留空白面板 —— 用户会以为是加载卡住）。</summary>
    private void ShowLyricPlaceholder()
    {
        LyricPanel.Children.Clear();
        LyricPanel.Children.Add(new TextBlock
        {
            Text = "这首歌没有歌词",
            Classes = { "hint-text" },
            Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
    }

    // ==================== 交互 ====================

    private void OnPlayPause(object? sender, RoutedEventArgs e) => TogglePlay();

    private void OnNext(object? sender, RoutedEventArgs e) => Next();

    /// <summary>播放列表抽屉：默认收起（截图那种「只有封面 + 歌词」的形态），需要时再展开。</summary>
    private void OnToggleList(object? sender, RoutedEventArgs e)
    {
        PlaylistPanel.IsVisible = !PlaylistPanel.IsVisible;
        ListButton.Content = PlaylistPanel.IsVisible ? "☰ 收起" : "☰ 列表";
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => Previous();

    private void OnProgressChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppress || _updating || Playback is null) return;
        var duration = Playback.Duration;
        if (duration.TotalSeconds <= 0.5) return;
        Playback.Seek(TimeSpan.FromSeconds(duration.TotalSeconds * ProgressSlider.Value));
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppress || Playback is null) return;
        Playback.Volume = (float)(VolumeSlider.Value / 100.0);
    }

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var index = TrackList.SelectedIndex;
        if (index < 0 || index == _current) return;
        PlayIndex(index);
    }

    private void OnTrackDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        var index = TrackList.SelectedIndex;
        if (index >= 0) PlayIndex(index);
    }

    private void OnSwitchMode(object? sender, RoutedEventArgs e)
    {
        _mode = (PlayMode)(((int)_mode + 1) % 3);
        UpdateModeButton();
        StatusChanged?.Invoke("播放模式：" + ModeButton.Content);
    }

    private void UpdateModeButton()
    {
        ModeButton.Content = _mode switch
        {
            PlayMode.Single => "单曲",
            PlayMode.Shuffle => "随机",
            _ => "顺序",
        };
    }

    /// <summary>
    /// 跳到指定位置（续播恢复用）：只改进度，⛔ 顺手开播 —— 恢复后保持暂停态。
    /// </summary>
    public void SeekTo(TimeSpan position)
    {
        if (Playback is null) return;

        Playback.Seek(position);

        var duration = Playback.Duration;
        _updating = true;
        try
        {
            ProgressSlider.Value = duration.TotalSeconds > 0.5
                ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1)
                : 0;
        }
        finally { _updating = false; }

        PositionText.Text = Format(Playback.Position);
    }

    /// <summary>宿主要求播完自动下一首时挂这个（在宿主里订阅 <see cref="IAudioPlayback.PlaybackEnded"/> 调用 <see cref="Next"/>）。</summary>
    public void HandlePlaybackEnded() => Next(true);
}
