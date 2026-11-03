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
/// <para>
/// 🔴 <see cref="Scope"/> 决定本页收哪类文件：不匹配时 <see cref="TryRouteElsewhere"/> 会投递给
/// 目标页并切过去（<c>viewer-text</c> / <c>viewer-image</c> / <c>viewer-audio</c>），本页不装载。
/// </para>
/// </summary>
public partial class ViewerViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    private readonly ViewerSettingsService _settings;
    private readonly TxtBookService _txt;
    private readonly ArchiveImageService _archive;
    private readonly AudioLibraryService _audio;
    private readonly IAudioPlayback _playback;
    private readonly IModuleBus? _bus;
    private readonly INavigationService? _nav;

    private readonly List<string> _pending = new();

    /// <summary>「媒体资产 → ▶ 播放」投递过来、但 View 还没挂上树的曲目（挂树后自动开播）。</summary>
    private readonly List<string> _pendingAudio = new();
    private bool _attached;

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

    /// <summary>分屏网格是否开启（一屏看多张，翻页一次换一屏）。</summary>
    [ObservableProperty] private bool _isSplit;

    /// <summary>分屏每屏列数（左右分屏 = 2，上下分屏 = 1，2×2 = 2）。</summary>
    [ObservableProperty] private int _splitColumns = 2;

    /// <summary>分屏每屏行数（左右分屏 = 1，上下分屏 = 2，2×2 = 2）。</summary>
    [ObservableProperty] private int _splitRows = 1;

    /// <summary>
    /// 工具条分屏按钮的文案（点按钮展开四档菜单）。
    /// <para>绑定在工具条上（<c>TextPath</c>）⇒ 改模式后由绑定自动刷新，⛔ 不必重建工具条。</para>
    /// </summary>
    public string SplitModeText => !IsSplit ? "▭ 单张"
        : SplitRows <= 1 ? "◫ 左右"
        : SplitColumns <= 1 ? "⊟ 上下"
        : "▦ 2×2";

    /// <summary>分屏菜单的四个选项（每项一个独立命令，避免菜单项带参数）。</summary>
    private IReadOnlyList<ToolbarMenuItemDescriptor> SplitMenuItems => new[]
    {
        new ToolbarMenuItemDescriptor
        {
            Text = "▭ 单张（不分屏）",
            CommandPath = nameof(SplitOneCommand),
            ToolTip = "一屏一张，恢复缩放 / 旋转",
        },
        new ToolbarMenuItemDescriptor
        {
            Text = "◫ 左右分屏（一屏 2 张）",
            CommandPath = nameof(SplitSideBySideCommand),
            ToolTip = "左右并排看两张，翻页一次换一屏",
        },
        new ToolbarMenuItemDescriptor
        {
            Text = "⊟ 上下分屏（一屏 2 张）",
            CommandPath = nameof(SplitStackedCommand),
            ToolTip = "上下排列看两张，翻页一次换一屏",
        },
        new ToolbarMenuItemDescriptor
        {
            Text = "▦ 2 × 2 四格（一屏 4 张）",
            CommandPath = nameof(SplitQuadCommand),
            ToolTip = "四格看四张，双击任意一格 = 单图看这张",
        },
    };

    /// <summary>工具条「历史」下拉是否展开。</summary>
    [ObservableProperty] private bool _isHistoryOpen;

    /// <summary>本页只处理哪一类内容（<see cref="ViewerScope.All"/> = 设计期不限）。</summary>
    public ViewerScope Scope { get; private set; } = ViewerScope.All;

    /// <summary>空态提示（按范围区分）。</summary>
    public string ScopeHint => Scope switch
    {
        ViewerScope.Text => "拖入文本（txt / md / 源码）或点「📂 打开」：txt / md 自动切章，带目录、书签、全书搜索。",
        ViewerScope.Image => "拖入图片（png / jpg / webp …）或压缩包（zip / rar / 7z），也可点「📂 打开」。",
        ViewerScope.Audio => "拖入音频（mp3 / flac / m4a …，可一次多个）或播放列表（m3u / m3u8），也可点「📂 打开」；按列表顺序播放。",
        _ => "拖入文件或点「📂 打开」开始。",
    };

    /// <summary>设计期用。</summary>
    public ViewerViewModel()
    {
        _settings = null!;
        _txt = null!;
        _archive = null!;
        _audio = null!;
        _playback = null!;
        _bus = null;
        _nav = null;
    }

    protected ViewerViewModel(ViewerScope scope, ViewerSettingsService settings, TxtBookService txt,
        ArchiveImageService archive, AudioLibraryService audio, IAudioPlayback playback,
        IModuleBus? bus, INavigationService? nav)
    {
        Scope = scope;
        _settings = settings;
        _txt = txt;
        _archive = archive;
        _audio = audio;
        _playback = playback;
        _bus = bus;
        _nav = nav;

        RefreshRecent();
        RefreshCustomRules();
        StatusText = ScopeHint;

        // 跨页投递：先取存量（本页刚被创建时投递还在信箱里），再订阅（本页已开着时后续投递即时落地）。
        _pending.AddRange(Flatten(_bus?.TakeAll(ViewerTopics.OpenFiles)));
        _bus?.Subscribe(ViewerTopics.OpenFiles, this, OnOpenFilesPosted);

        // 「媒体资产 → ▶ 播放」：载荷是路径列表，与 OpenFiles 不同（不是 ViewerOpenRequest），单独一条通道。
        if (Scope is ViewerScope.Audio or ViewerScope.All)
        {
            foreach (var payload in _bus?.TakeAll(ModuleTopics.AudioPlay) ?? Array.Empty<object?>())
                OnAudioPlayPosted(payload);
            _bus?.Subscribe(ModuleTopics.AudioPlay, this, OnAudioPlayPosted);
        }
    }

    /// <summary>「媒体资产 → ▶ 播放」投递到达：View 已就绪就立刻开播，否则先攒着等挂树。</summary>
    private void OnAudioPlayPosted(object? payload)
    {
        if (payload is not IReadOnlyList<string> paths || paths.Count == 0) return;

        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) return;

        if (_attached) AudioPlayRequested?.Invoke(this, list);
        else _pendingAudio.AddRange(list);
    }

    /// <summary>取走还没开播的投递曲目（本页创建时投递先到）。</summary>
    public IReadOnlyList<string> TakePendingAudio()
    {
        if (_pendingAudio.Count == 0) return Array.Empty<string>();

        var paths = _pendingAudio.ToList();
        _pendingAudio.Clear();
        return paths;
    }

    /// <summary>请求 View 打开文件；参数 <c>null</c> = 由 View 弹选择器。</summary>
    public event EventHandler<IReadOnlyList<string>?>? OpenRequested;

    /// <summary>请求 View 关闭当前内容。</summary>
    public event EventHandler? CloseRequested;

    public event EventHandler? ZoomInRequested;
    public event EventHandler? ZoomOutRequested;
    public event EventHandler? FitRequested;

    /// <summary>顺时针旋转 90°（图片）。</summary>
    public event EventHandler? RotateRequested;

    /// <summary>压缩包内翻页（参数 = +1 / -1）。</summary>
    public event EventHandler<int>? ArchiveNavigateRequested;

    /// <summary>分屏开关或每屏张数变了（View 据此装载 / 卸载网格）。</summary>
    public event EventHandler? SplitChanged;

    /// <summary>播放 / 暂停（音频）。</summary>
    public event EventHandler? PlayToggleRequested;

    /// <summary>「媒体资产 → ▶ 播放」要求开播一批曲目（参数 = 按顺序排好的绝对路径）。</summary>
    public event EventHandler<IReadOnlyList<string>>? AudioPlayRequested;

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

    // ---------- 页面范围 ----------

    /// <summary>查看器页 Key（按范围）。</summary>
    public static string KeyOf(ViewerScope scope) => scope switch
    {
        ViewerScope.Text => "viewer-text",
        ViewerScope.Image => "viewer-image",
        ViewerScope.Audio => "viewer-audio",
        _ => "viewer-text",
    };

    /// <summary>页面标题（按范围）。</summary>
    public static string TitleOf(ViewerScope scope) => scope switch
    {
        ViewerScope.Text => "文本阅读",
        ViewerScope.Image => "图片查看",
        ViewerScope.Audio => "音乐播放",
        _ => "查看器",
    };

    /// <summary>某文件应落到哪一页；识别不出返回 <see cref="ViewerScope.All"/>。</summary>
    public static ViewerScope TargetScopeOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ViewerScope.All;
        if (ViewerHost.IsArchive(path)) return ViewerScope.Image;

        return ViewerHost.KindOf(path) switch
        {
            ViewerKind.Text or ViewerKind.Reader => ViewerScope.Text,
            ViewerKind.Image => ViewerScope.Image,
            ViewerKind.Audio => ViewerScope.Audio,
            _ => ViewerScope.All,
        };
    }

    /// <summary>本页是否接收该文件（设计期 / 识别不出的一律放行，由 View 报错）。</summary>
    public bool Accepts(string path)
        => Scope == ViewerScope.All || TargetScopeOf(path) is ViewerScope.All || TargetScopeOf(path) == Scope;

    /// <summary>
    /// 文件不属于本页时：投递给目标页并切过去。返回 <c>true</c> = 已转走，本页 ⛔ 不再装载。
    /// </summary>
    public bool TryRouteElsewhere(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return false;

        var target = paths.All(ViewerHost.IsAudio) ? ViewerScope.Audio : TargetScopeOf(paths[0]);
        if (target == ViewerScope.All || target == Scope) return false;

        var request = new ViewerOpenRequest(target, paths);
        _bus?.Post(ViewerTopics.OpenFiles, request);
        _nav?.NavigateTo(KeyOf(target));

        StatusText = $"⚠ 这个文件属于「{TitleOf(target)}」页，已转过去打开。";
        return true;
    }

    /// <summary>View 设好 DataContext 后调一次：此后投递立即落地，不再压信箱。</summary>
    public void AttachView() => _attached = true;

    /// <summary>取走还没打开的文件（本页创建时投递先到）。</summary>
    public IReadOnlyList<string> TakePending()
    {
        if (_pending.Count == 0) return Array.Empty<string>();

        var paths = _pending.ToList();
        _pending.Clear();
        return paths;
    }

    private void OnOpenFilesPosted(object? payload)
    {
        var paths = Flatten(new[] { payload });
        if (paths.Count == 0) return;

        if (_attached) OpenRequested?.Invoke(this, paths);
        else _pending.AddRange(paths);
    }

    /// <summary>从信箱 / 订阅载荷里挑出属于本页的请求。</summary>
    private IReadOnlyList<string> Flatten(IReadOnlyList<object?>? payloads)
    {
        var paths = new List<string>();
        if (payloads == null) return paths;

        foreach (var p in payloads)
        {
            if (p is not ViewerOpenRequest request || request.Scope != Scope) continue;
            paths.AddRange(request.Paths.Where(path => !string.IsNullOrWhiteSpace(path)));
        }

        return paths;
    }

    // ---------- 最近打开 ----------

    public ObservableCollection<string> RecentItems { get; } = new();

    public bool HasRecent => RecentItems.Count > 0;

    private void RefreshRecent()
    {
        RecentItems.Clear();
        foreach (var p in _settings.Recent.Where(Accepts)) RecentItems.Add(p);
        OnPropertyChanged(nameof(HasRecent));
    }

    // ---------- 图片条目（压缩包 / 同目录） ----------

    public ObservableCollection<ViewerEntryItem> ArchiveEntries { get; } = new();

    /// <summary>节下拉的可选项（如 <c>1–100</c>）；只有一般目录且够长时才有内容。</summary>
    public ObservableCollection<string> SectionItems { get; } = new();

    /// <summary>每节多少张；0 = 不分节。</summary>
    public int SectionSize { get; private set; }

    /// <summary>是否显示节下拉（≥2 节才值得显示）。</summary>
    public bool HasSections => SectionSize > 0 && SectionItems.Count > 1;

    /// <summary>压缩包内当前张数信息（「第 3 / 128 张」）。</summary>
    /// <remarks><see cref="IsArchive"/> 语义 = 「有可翻页的条目」（压缩包内图片或同目录图片）。</remarks>
    public void SetArchiveEntries(IReadOnlyList<string> names, int index, int sectionSize)
    {
        ArchiveEntries.Clear();
        SectionItems.Clear();
        SectionSize = sectionSize;

        for (var i = 0; i < names.Count; i++)
        {
            var isSectionStart = sectionSize > 0 && i % sectionSize == 0;
            ArchiveEntries.Add(new ViewerEntryItem
            {
                Name = names[i],
                IsSectionStart = isSectionStart,
                SectionLabel = isSectionStart ? $"{i + 1}–{Math.Min(i + sectionSize, names.Count)}" : "",
            });

            if (isSectionStart) SectionItems.Add($"{i + 1}–{Math.Min(i + sectionSize, names.Count)}");
        }

        IsArchive = ArchiveEntries.Count > 0;
        ArchiveInfo = ArchiveEntries.Count == 0 ? "" : $"第 {index + 1} / {ArchiveEntries.Count} 张";
        OnPropertyChanged(nameof(HasSections));
    }

    /// <summary>更新压缩包内序号显示。</summary>
    public void UpdateArchiveIndex(int index)
        => ArchiveInfo = ArchiveEntries.Count == 0 ? "" : $"第 {index + 1} / {ArchiveEntries.Count} 张";

    /// <summary>分屏时显示整屏范围（「第 5–8 / 120 张」）。</summary>
    public void SetArchiveRange(int start, int count)
    {
        if (ArchiveEntries.Count == 0) { ArchiveInfo = ""; return; }

        var first = Math.Clamp(start, 0, ArchiveEntries.Count - 1);
        var last = Math.Min(first + Math.Max(count, 1), ArchiveEntries.Count) - 1;
        ArchiveInfo = last <= first
            ? $"第 {first + 1} / {ArchiveEntries.Count} 张"
            : $"第 {first + 1}–{last + 1} / {ArchiveEntries.Count} 张";
    }

    /// <summary>翻到同一批图片里的另一张（同目录 / 压缩包内）：只刷新标题，⛔ 不写「最近打开」。</summary>
    public void OnImageNavigated(string path)
    {
        CurrentTitle = Path.GetFileName(path);
        CurrentInfo = path;
        IsImage = true;
        HasContent = true;
        Hint = "";
        StatusText = "已打开";
    }

    /// <summary>清空压缩包状态。</summary>
    public void ClearArchive()
    {
        ArchiveEntries.Clear();
        SectionItems.Clear();
        SectionSize = 0;
        IsArchive = false;
        ArchiveInfo = "";
        OnPropertyChanged(nameof(HasSections));
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

    /// <summary>上次的音乐播放列表（关掉再开能续上；没有返回空列表 + 索引 0）。</summary>
    public (IReadOnlyList<string> Paths, int Index) LoadAudioPlaylist() => _settings.AudioPlaylist;

    /// <summary>存音乐播放列表（切歌 / 换列表 / 关页面时调）。</summary>
    public void SaveAudioPlaylist(IReadOnlyList<string> paths, int index)
    {
        try { _settings.AudioPlaylist = (paths ?? Array.Empty<string>(), index); }
        catch (Exception ex) { StatusText = "⚠ 保存播放列表失败：" + ex.Message; }
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
        IsHistoryOpen = false;
        if (kind != ViewerKind.Image) IsSplit = false;

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
        IsSplit = false;
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
        StatusText = "已关闭。" + ScopeHint;
        IsSplit = false;
        IsHistoryOpen = false;
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
        OpenRequested?.Invoke(this, new[] { path });
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

    // ---------- 分屏 / 历史 ----------

    /// <summary>分屏菜单「单张」：退出分屏。</summary>
    [RelayCommand]
    private void SplitOne() => ApplySplitMode(1, 1);

    /// <summary>分屏菜单「左右」：两列一行。</summary>
    [RelayCommand]
    private void SplitSideBySide() => ApplySplitMode(1, 2);

    /// <summary>分屏菜单「上下」：一列两行。</summary>
    [RelayCommand]
    private void SplitStacked() => ApplySplitMode(2, 1);

    /// <summary>分屏菜单「2 × 2」：两列两行。</summary>
    [RelayCommand]
    private void SplitQuad() => ApplySplitMode(2, 2);

    private void ApplySplitMode(int rows, int cols)
    {
        if (rows <= 1 && cols <= 1)
        {
            IsSplit = false;
            StatusText = "已回到单图。";
            return;
        }

        if (!IsImage) return;

        SplitRows = rows;
        SplitColumns = cols;
        IsSplit = true;
        IsHistoryOpen = false;
        StatusText = $"分屏：一屏 {rows * cols} 张，翻页一次换一屏";
    }

    private void SyncSplitMode() => OnPropertyChanged(nameof(SplitModeText));

    /// <summary>退出分屏（网格里双击某张 → 单图看这张）。</summary>
    public void ExitSplit()
    {
        if (!IsSplit) return;
        IsSplit = false;
        StatusText = "已回到单图。";
    }

    /// <summary>展开 / 收起工具条的历史下拉。</summary>
    [RelayCommand]
    private void ToggleHistory() => IsHistoryOpen = !IsHistoryOpen;

    /// <summary>顺时针旋转 90°（单图有效）。</summary>
    [RelayCommand]
    private void Rotate() => RotateRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void TogglePlay() => PlayToggleRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EditRules() => RulesEditRequested?.Invoke(this, EventArgs.Empty);

    // ---------- 工具条 ----------

    /// <summary>工具条按页面范围裁剪：⛔ 不给文本页放「缩放」，⛔ 不给音频页放「章节规则」。</summary>
    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar
    {
        get
        {
            var items = new List<ToolbarItemDescriptor>
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
                    Text = "🕘 历史",
                    CommandPath = nameof(ToggleHistoryCommand),
                    IsEnabledPath = nameof(HasRecent),
                    ToolTip = "最近打开：点一下展开，选一项重新打开",
                },
            };

            if (Scope is ViewerScope.Text or ViewerScope.All)
            {
                items.Add(new()
                {
                    Text = "A-",
                    CommandPath = nameof(FontSmallerCommand),
                    IsEnabledPath = nameof(CanStepFont),
                    ToolTip = "字号减小",
                });
                items.Add(new()
                {
                    Text = "A+",
                    CommandPath = nameof(FontLargerCommand),
                    IsEnabledPath = nameof(CanStepFont),
                    ToolTip = "字号增大",
                });
            }

            if (Scope is ViewerScope.Image or ViewerScope.All)
            {
                items.Add(new()
                {
                    Text = "适应窗口",
                    CommandPath = nameof(FitCommand),
                    IsEnabledPath = nameof(CanZoom),
                    ToolTip = "图片缩放到适应窗口（也可双击图片切换 1:1 / 适应）",
                });
                items.Add(new()
                {
                    Text = "－",
                    Width = 34,
                    GlyphSize = 14,
                    CommandPath = nameof(ZoomOutCommand),
                    IsEnabledPath = nameof(CanZoom),
                    ToolTip = "缩小（Ctrl + 滚轮同样有效）",
                });
                items.Add(new()
                {
                    Text = "＋",
                    Width = 34,
                    GlyphSize = 14,
                    CommandPath = nameof(ZoomInCommand),
                    IsEnabledPath = nameof(CanZoom),
                    ToolTip = "放大（Ctrl + 滚轮同样有效）",
                });
                items.Add(new()
                {
                    Text = "↻",
                    Width = 34,
                    GlyphSize = 14,
                    CommandPath = nameof(RotateCommand),
                    IsEnabledPath = nameof(CanZoom),
                    ShowToolTipOnDisabled = true,
                    ToolTip = "顺时针旋转 90°（单图有效）",
                });
                items.Add(new()
                {
                    Text = "←",
                    Width = 34,
                    GlyphSize = 14,
                    CommandPath = nameof(PrevImageCommand),
                    IsEnabledPath = nameof(IsArchive),
                    ShowToolTipOnDisabled = true,
                    ToolTip = "上一张（同目录图片 / 压缩包内，←）",
                });
                items.Add(new()
                {
                    Text = "→",
                    Width = 34,
                    GlyphSize = 14,
                    CommandPath = nameof(NextImageCommand),
                    IsEnabledPath = nameof(IsArchive),
                    ShowToolTipOnDisabled = true,
                    ToolTip = "下一张（同目录图片 / 压缩包内，→）",
                });
                items.Add(new()
                {
                    // 点击展开四档分屏菜单（⛔ 不再是一排四个图标按钮：占位宽、还跟播放键撞脸）
                    TextPath = nameof(SplitModeText),
                    Items = SplitMenuItems,
                    IsEnabledPath = nameof(IsImage),
                    ToolTip = "点击展开分屏方式：单张 / 左右 / 上下 / 2×2；分屏时翻页一次换一屏",
                });
            }

            if (Scope is ViewerScope.Audio or ViewerScope.All)
            {
                items.Add(new()
                {
                    Text = "⏯ 播放/暂停",
                    CommandPath = nameof(TogglePlayCommand),
                    IsEnabledPath = nameof(IsAudio),
                    ToolTip = "播放或暂停当前曲目",
                });
            }

            items.Add(new()
            {
                Text = "✕ 关闭",
                CommandPath = nameof(CloseCommand),
                IsEnabledPath = nameof(HasContent),
                ToolTip = "关闭当前文件（回到空态）",
            });

            if (Scope is ViewerScope.Text or ViewerScope.All)
            {
                items.Add(new()
                {
                    Text = "⚙ 章节规则",
                    CommandPath = nameof(EditRulesCommand),
                    ToolTip = "自定义 txt 章节识别正则（带试跑）",
                });
            }

            return items;
        }
    }

    /// <summary>字号按钮可用：短文本或长文本阅读器。</summary>
    public bool CanStepFont => IsText || IsReader;

    /// <summary>缩放按钮可用：图片单图模式（分屏网格按格自适应，缩放无意义）。</summary>
    public bool CanZoom => IsImage && !IsSplit;

    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsTextChanged(bool value) => OnPropertyChanged(nameof(CanStepFont));

    partial void OnIsReaderChanged(bool value) => OnPropertyChanged(nameof(CanStepFont));

    partial void OnIsImageChanged(bool value) => OnPropertyChanged(nameof(CanZoom));

    partial void OnIsSplitChanged(bool value)
    {
        OnPropertyChanged(nameof(CanZoom));
        SyncSplitMode();
        SplitChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSplitColumnsChanged(int value)
    {
        SyncSplitMode();
        SplitChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSplitRowsChanged(int value)
    {
        SyncSplitMode();
        SplitChanged?.Invoke(this, EventArgs.Empty);
    }

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
