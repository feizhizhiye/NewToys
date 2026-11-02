using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Readers;
using NewToys.UI.Viewers;
using NewToys.Viewer.Services;

namespace NewToys.Viewer.ViewModels;

/// <summary>
/// 查看器页 VM：只管「打开哪个文件 / 最近打开 / 排版参数 / 状态文案」。
/// <para>
/// 🔴 真正的装载与渲染在 <c>NewToys.UI.Viewers.ViewerHost</c>（View 持有它），VM 通过
/// <see cref="OpenRequested"/> 请求 View 打开 —— VM ⛔ 不 new 控件、⛔ 不碰 StorageProvider，
/// 与项目既有约定一致（选择器 / 控件操作归 View）。
/// </para>
/// <para>
/// 🔴 章节切分 / 压缩包遍历 / 音频标签 / 播放后端都走注入的服务；VM 只做状态与命令编排。
/// </para>
/// </summary>
public partial class ViewerViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    private readonly ViewerSettingsService _settings;
    private readonly TxtBookService _txt;
    private readonly ArchiveImageService _archive;
    private readonly AudioLibraryService _audio;
    private readonly IAudioPlayback _playback;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _currentTitle = "未打开文件";
    [ObservableProperty] private string _currentInfo = "";
    [ObservableProperty] private bool _isText;
    [ObservableProperty] private bool _isImage;
    [ObservableProperty] private bool _isReader;
    [ObservableProperty] private bool _isAudio;
    [ObservableProperty] private bool _isArchive;
    [ObservableProperty] private bool _hasContent;
    [ObservableProperty] private string _archiveInfo = "";

    /// <summary>设计期用。</summary>
    public ViewerViewModel()
    {
        _settings = null!;
        _txt = null!;
        _archive = null!;
        _audio = null!;
        _playback = null!;
    }

    public ViewerViewModel(ViewerSettingsService settings, TxtBookService txt,
        ArchiveImageService archive, AudioLibraryService audio, IAudioPlayback playback)
    {
        _settings = settings;
        _txt = txt;
        _archive = archive;
        _audio = audio;
        _playback = playback;

        RefreshRecent();
        RefreshCustomRules();
        StatusText = "拖入文件或点「📂 打开」开始。";
    }

    /// <summary>请求 View 打开文件；参数 <c>null</c> = 由 View 弹选择器。</summary>
    public event EventHandler<string?>? OpenRequested;

    /// <summary>请求 View 关闭当前内容。</summary>
    public event EventHandler? CloseRequested;

    public event EventHandler? ZoomInRequested;
    public event EventHandler? ZoomOutRequested;
    public event EventHandler? FitRequested;

    /// <summary>压缩包内翻页（参数 = +1 / -1）。</summary>
    public event EventHandler<int>? ArchiveNavigateRequested;

    /// <summary>播放 / 暂停（音频）。</summary>
    public event EventHandler? PlayToggleRequested;

    /// <summary>打开章节规则浮层。</summary>
    public event EventHandler? RulesEditRequested;

    /// <summary>长文本排版参数变化（View 重新下发）。</summary>
    public event EventHandler<ReaderTypography>? ReaderOptionsChanged;

    public event EventHandler? StatusChanged;
    public event EventHandler? ToolbarChanged;

    // ---------- 服务暴露（View 用） ----------

    /// <summary>章节切分服务。</summary>
    public TxtBookService Txt => _txt;

    /// <summary>压缩包图片服务。</summary>
    public ArchiveImageService Archive => _archive;

    /// <summary>音频标签服务。</summary>
    public AudioLibraryService AudioLibrary => _audio;

    /// <summary>播放后端。</summary>
    public IAudioPlayback Playback => _playback;

    // ---------- 最近打开 ----------

    public ObservableCollection<string> RecentItems { get; } = new();

    public bool HasRecent => RecentItems.Count > 0;

    private void RefreshRecent()
    {
        RecentItems.Clear();
        foreach (var p in _settings.Recent) RecentItems.Add(p);
        OnPropertyChanged(nameof(HasRecent));
    }

    // ---------- 压缩包条目 ----------

    public ObservableCollection<string> ArchiveEntries { get; } = new();

    /// <summary>压缩包内当前张数信息（「第 3 / 128 张」）。</summary>
    public void SetArchiveEntries(IEnumerable<string> names, int index)
    {
        ArchiveEntries.Clear();
        foreach (var name in names) ArchiveEntries.Add(name);
        IsArchive = ArchiveEntries.Count > 0;
        ArchiveInfo = ArchiveEntries.Count == 0 ? "" : $"第 {index + 1} / {ArchiveEntries.Count} 张";
    }

    /// <summary>更新压缩包内序号显示。</summary>
    public void UpdateArchiveIndex(int index)
        => ArchiveInfo = ArchiveEntries.Count == 0 ? "" : $"第 {index + 1} / {ArchiveEntries.Count} 张";

    /// <summary>清空压缩包状态。</summary>
    public void ClearArchive()
    {
        ArchiveEntries.Clear();
        IsArchive = false;
        ArchiveInfo = "";
    }

    // ---------- 排版参数 ----------

    public double FontSize => _settings.FontSize;

    public double LineSpacing => _settings.LineSpacing;

    public double PagePadding => _settings.PagePadding;

    /// <summary>长文本阅读器排版参数。</summary>
    public ReaderTypography ReaderOptions => _settings.ReaderOptions;

    /// <summary>字号加减：长文本走阅读器排版，其它走短文本排版。</summary>
    public void StepFontSize(double delta)
    {
        try
        {
            if (IsReader)
            {
                var current = _settings.ReaderOptions;
                var next = CopyReader(current, current.FontSize + delta);
                _settings.SetReaderOptions(next);
                OnPropertyChanged(nameof(ReaderOptions));
                ReaderOptionsChanged?.Invoke(this, next);
                StatusText = "阅读字号 " + next.FontSize.ToString("0");
                return;
            }

            _settings.FontSize = FontSize + delta;
            OnPropertyChanged(nameof(FontSize));
            StatusText = "字号 " + FontSize.ToString("0");
            TypographyChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "⚠ 保存字号失败：" + ex.Message; }
    }

    /// <summary>保存阅读器排版（ReaderShell 的排版浮层改完就回写）。</summary>
    public void SaveReaderOptions(ReaderTypography options)
    {
        try { _settings.SetReaderOptions(options); }
        catch (Exception ex) { StatusText = "⚠ 保存排版失败：" + ex.Message; }
        OnPropertyChanged(nameof(ReaderOptions));
    }

    /// <summary>排版参数变化（短文本：View 据此重新下发到渲染器）。</summary>
    public event EventHandler? TypographyChanged;

    /// <summary>取某文件的阅读进度（0~1）；没有记录返回 0。</summary>
    public double GetProgress(string path) => _settings.GetProgress(path);

    /// <summary>存某文件的阅读进度（关闭 / 切文件 / 离开页面时调）。</summary>
    public void SaveProgress(string path, double progress) => _settings.SetProgress(path, progress);

    /// <summary>取长文本阅读位置（章 + 章内比例）。</summary>
    public ViewerReaderState? GetReaderPosition(string path) => _settings.GetReaderPosition(path);

    /// <summary>存长文本阅读位置。</summary>
    public void SaveReaderPosition(string path, ViewerReaderState state) => _settings.SetReaderPosition(path, state);

    // ---------- 书签 ----------

    /// <summary>读某本书的书签。</summary>
    public IReadOnlyList<ReaderBookmark> LoadBookmarks(string path) => _settings.GetBookmarks(path);

    /// <summary>存某本书的书签。</summary>
    public void SaveBookmarks(string path, IEnumerable<ReaderBookmark> bookmarks)
        => _settings.SetBookmarks(path, bookmarks);

    // ---------- 章节规则 ----------

    /// <summary>自定义章节规则（排在内置规则之前）。</summary>
    public ObservableCollection<TxtChapterRule> CustomRules { get; } = new();

    private void RefreshCustomRules()
    {
        CustomRules.Clear();
        foreach (var rule in _settings.CustomTxtRules) CustomRules.Add(rule);
    }

    /// <summary>加一条规则并落盘。</summary>
    public bool AddRule(string name, string pattern, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(pattern)) { error = "正则不能为空。"; return false; }

        var probe = _txt.TestRule("第一章 测试\nChapter 1\n1、测试", pattern, 3);
        if (probe.Count > 0 && probe[0].StartsWith("⚠", StringComparison.Ordinal))
        {
            error = probe[0];
            return false;
        }

        CustomRules.Add(new TxtChapterRule
        {
            Name = string.IsNullOrWhiteSpace(name) ? "自定义规则 " + (CustomRules.Count + 1) : name.Trim(),
            Pattern = pattern.Trim(),
        });

        SaveRules();
        return true;
    }

    /// <summary>删除一条规则并落盘。</summary>
    public void RemoveRule(TxtChapterRule? rule)
    {
        if (rule is null) return;
        CustomRules.Remove(rule);
        SaveRules();
    }

    private void SaveRules()
    {
        try { _settings.SetCustomTxtRules(CustomRules); }
        catch (Exception ex) { StatusText = "⚠ 保存规则失败：" + ex.Message; }
    }

    /// <summary>规则试跑（设置界面预览用）。</summary>
    public IReadOnlyList<string> TestRule(string sample, string pattern) => _txt.TestRule(sample, pattern, 30);

    /// <summary>当前生效的规则文本（内置 + 自定义），给规则浮层展示。</summary>
    public IReadOnlyList<(string Name, string Pattern, bool Custom)> AllRules
    {
        get
        {
            var list = CustomRules.Select(r => (r.Name, r.Pattern, true)).ToList();
            list.AddRange(TxtBookService.BuiltinRules.Select(r => (r.Name, r.Pattern, false)));
            return list;
        }
    }

    // ---------- 音频 ----------

    /// <summary>音量 0~100（持久化）。</summary>
    public int AudioVolume
    {
        get => _settings.AudioVolume;
        set
        {
            _settings.AudioVolume = value;
            OnPropertyChanged(nameof(AudioVolume));
        }
    }

    // ---------- View 回调 ----------

    /// <summary>打开成功。</summary>
    public void OnOpened(string path, ViewerKind kind, string info = "")
    {
        try { _settings.PushRecent(path); }
        catch (Exception ex) { StatusText = "⚠ 记录最近打开失败：" + ex.Message; }

        CurrentTitle = Path.GetFileName(path);
        CurrentInfo = string.IsNullOrWhiteSpace(info) ? path : info;
        IsText = kind == ViewerKind.Text;
        IsImage = kind == ViewerKind.Image;
        IsReader = kind == ViewerKind.Reader;
        IsAudio = kind == ViewerKind.Audio;
        HasContent = true;
        Hint = "";

        StatusText = kind switch
        {
            ViewerKind.Reader => "已打开（长文本阅读器）",
            ViewerKind.Text => "已打开",
            ViewerKind.Image => "已打开",
            ViewerKind.Audio => "已打开（播放列表就绪）",
            _ => "已打开",
        };

        RefreshRecent();
        NotifyToolbarChanged();
    }

    /// <summary>打开失败：保持空态并显示原因。</summary>
    public void OnOpenFailed(string error)
    {
        HasContent = false;
        IsText = false;
        IsImage = false;
        IsReader = false;
        IsAudio = false;
        IsArchive = false;
        CurrentTitle = "未打开文件";
        CurrentInfo = "";
        Hint = error;
        StatusText = "⚠ " + error;
        NotifyToolbarChanged();
    }

    /// <summary>关闭后回到空态。</summary>
    public void OnClosed()
    {
        HasContent = false;
        IsText = false;
        IsImage = false;
        IsReader = false;
        IsAudio = false;
        ClearArchive();
        CurrentTitle = "未打开文件";
        CurrentInfo = "";
        StatusText = "已关闭。拖入文件或点「📂 打开」。";
        NotifyToolbarChanged();
    }

    /// <summary>状态栏右侧附带信息（当前缩放 / 进度 / 曲目）。</summary>
    public void SetInfo(string info) => CurrentInfo = info ?? "";

    /// <summary>状态栏一句话（阅读器 / 播放器的内部事件上报）。</summary>
    public void SetStatus(string text) => StatusText = text ?? "";

    // ---------- 命令 ----------

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(this, null);

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        OpenRequested?.Invoke(this, path);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearRecent()
    {
        try
        {
            _settings.ClearRecent();
            RefreshRecent();
            StatusText = "已清空最近打开。";
        }
        catch (Exception ex) { StatusText = "⚠ 清空失败：" + ex.Message; }
    }

    [RelayCommand]
    private void ZoomIn() => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomOut() => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Fit() => FitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FontLarger() => StepFontSize(1);

    [RelayCommand]
    private void FontSmaller() => StepFontSize(-1);

    [RelayCommand]
    private void PrevImage() => ArchiveNavigateRequested?.Invoke(this, -1);

    [RelayCommand]
    private void NextImage() => ArchiveNavigateRequested?.Invoke(this, 1);

    [RelayCommand]
    private void TogglePlay() => PlayToggleRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditRules() => RulesEditRequested?.Invoke(this, EventArgs.Empty);

    // ---------- 工具条 ----------

    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "📂 打开",
            Classes = "primary",
            CommandPath = nameof(OpenCommand),
            ToolTip = "选择要查看的文件（也可直接把文件拖进窗口）",
        },
        new()
        {
            Text = "A-",
            CommandPath = nameof(FontSmallerCommand),
            IsEnabledPath = nameof(CanStepFont),
            ToolTip = "字号减小",
        },
        new()
        {
            Text = "A+",
            CommandPath = nameof(FontLargerCommand),
            IsEnabledPath = nameof(CanStepFont),
            ToolTip = "字号增大",
        },
        new()
        {
            Text = "⊞ 适应窗口",
            CommandPath = nameof(FitCommand),
            IsEnabledPath = nameof(IsImage),
            ToolTip = "图片缩放到适应窗口（也可双击图片切换 1:1 / 适应）",
        },
        new()
        {
            Text = "－",
            CommandPath = nameof(ZoomOutCommand),
            IsEnabledPath = nameof(IsImage),
            ToolTip = "缩小（Ctrl + 滚轮同样有效）",
        },
        new()
        {
            Text = "＋",
            CommandPath = nameof(ZoomInCommand),
            IsEnabledPath = nameof(IsImage),
            ToolTip = "放大（Ctrl + 滚轮同样有效）",
        },
        new()
        {
            Text = "◀ 上一张",
            CommandPath = nameof(PrevImageCommand),
            IsEnabledPath = nameof(IsArchive),
            ToolTip = "压缩包里的上一张（←）",
        },
        new()
        {
            Text = "下一张 ▶",
            CommandPath = nameof(NextImageCommand),
            IsEnabledPath = nameof(IsArchive),
            ToolTip = "压缩包里的下一张（→）",
        },
        new()
        {
            Text = "⏯ 播放/暂停",
            CommandPath = nameof(TogglePlayCommand),
            IsEnabledPath = nameof(IsAudio),
            ToolTip = "播放或暂停当前曲目",
        },
        new()
        {
            Text = "✕ 关闭",
            CommandPath = nameof(CloseCommand),
            IsEnabledPath = nameof(HasContent),
            ToolTip = "关闭当前文件（回到空态）",
        },
        new()
        {
            Text = "⚙ 章节规则",
            CommandPath = nameof(EditRulesCommand),
            ToolTip = "自定义 txt 章节识别正则（带试跑）",
        },
    };

    /// <summary>字号按钮可用：短文本或长文本阅读器。</summary>
    public bool CanStepFont => IsText || IsReader;

    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsTextChanged(bool value) => OnPropertyChanged(nameof(CanStepFont));

    partial void OnIsReaderChanged(bool value) => OnPropertyChanged(nameof(CanStepFont));

    private static ReaderTypography CopyReader(ReaderTypography source, double fontSize) => new()
    {
        FontSize = Math.Clamp(fontSize, 12, 34),
        LineSpacing = source.LineSpacing,
        Padding = source.Padding,
        ParagraphSpacing = source.ParagraphSpacing,
        Indent = source.Indent,
        FontFamily = source.FontFamily,
        Foreground = source.Foreground,
        Background = source.Background,
        Theme = source.Theme,
    };
}
