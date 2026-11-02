using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    private readonly List<(TimeSpan At, string Text)> _lyrics = new();
    private readonly List<TextBlock> _lyricViews = new();

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

        // 离开视觉树（切页 / 关闭）⇒ 停表并停播，⛔ 不留后台声音
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            try { Playback?.Stop(); } catch { }
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
        PlayButton.Content = Playback.State == AudioPlayState.Playing ? "暂停" : "播放";

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
        PlayButton.Content = Playback.State == AudioPlayState.Playing ? "暂停" : "播放";
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
        Playback?.Stop();
        PlayButton.Content = "播放";
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

    /// <summary>载入歌词文本（宿主读同名 .lrc 后传入；<c>null</c> = 没有歌词）。</summary>
    public void SetLyrics(string? raw)
    {
        _lyrics.Clear();
        _lyricViews.Clear();
        LyricPanel.Children.Clear();
        _lyricIndex = -1;

        if (string.IsNullOrWhiteSpace(raw)) return;

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
                _lyrics.Add((new TimeSpan(0, 0, minutes, seconds, fraction), text));
            }
        }

        if (_lyrics.Count == 0) return;

        _lyrics.Sort((a, b) => a.At.CompareTo(b.At));
        foreach (var (_, text) in _lyrics)
        {
            var view = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Opacity = 0.55,
            };
            _lyricViews.Add(view);
            LyricPanel.Children.Add(view);
        }
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
        PlayButton.Content = Playback.State == AudioPlayState.Playing ? "暂停" : "播放";

        UpdateLyric(position);
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
            _lyricViews[i].FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
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

        _cover?.Dispose();
        _cover = null;
        Cover.Source = null;

        if (track.Cover is not { Length: > 0 }) return;
        try
        {
            _cover = new Bitmap(new MemoryStream(track.Cover));
            Cover.Source = _cover;
        }
        catch { /* 封面坏了不影响播放 */ }
    }

    private void LoadLyrics(string path)
    {
        SetLyrics(null);
        try
        {
            var lrc = Path.ChangeExtension(path, ".lrc");
            if (File.Exists(lrc)) SetLyrics(File.ReadAllText(lrc, System.Text.Encoding.UTF8));
        }
        catch { /* 歌词是附属产物 */ }
    }

    // ==================== 交互 ====================

    private void OnPlayPause(object? sender, RoutedEventArgs e) => TogglePlay();

    private void OnNext(object? sender, RoutedEventArgs e) => Next();

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
        ModeButton.Content = _mode switch
        {
            PlayMode.Single => "单曲",
            PlayMode.Shuffle => "随机",
            _ => "顺序",
        };
        StatusChanged?.Invoke("播放模式：" + ModeButton.Content);
    }

    /// <summary>宿主要求播完自动下一首时挂这个（在宿主里订阅 <see cref="IAudioPlayback.PlaybackEnded"/> 调用 <see cref="Next"/>）。</summary>
    public void HandlePlaybackEnded() => Next(true);
}
