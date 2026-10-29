using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 视频列表页 —— 一个「比资源管理器显示更多信息的视频查看器」，用来快速判断哪些片子该剪辑 / 该转码。
/// <para>
/// 布局遵循规范 <b>3.6 列表页</b>（与「PC 工具箱 → 软件包管理」同款）：左侧可排序表头 + 列表、
/// 右侧选中项详情、列表挂右键菜单；表头最右侧还有一个无文字小按钮，点一下展开详情（并自动选中第一项）。
/// 列显隐 / 详情开合 / 排序 / 多项筛选全部继承自 <see cref="ListPageViewModelBase"/>（v1.35 收口）。
/// </para>
/// <para>
/// <b>目录每次手动选</b>（v1.34）：本页与设置彻底解绑 —— 不再有"视频库默认目录"，也不回写 <c>app.json</c>
/// （视频 / 音乐这类库常常分散在好几个文件夹，写死一个默认目录只会误导）。只在**本次程序运行内**记住上次选的目录。
/// </para>
/// <para>
/// <b>多选</b>：与「媒体资产 → 全部资产」同一套（原生多选，无修饰键 = 单选 / `Ctrl` 增选 / `Shift` 连选 /
/// `Ctrl+A` 全选），选中一批后「➡ 发送到批量处理」建任务 —— 任务清单由单例 <see cref="VideoTaskStore"/>
/// 持有，切页不丢。
/// </para>
/// <para>
/// <b>媒体信息</b>（分辨率 / 编码 / 码率 / 时长）走 ffprobe：
/// ① 扫描目录时先用 SQLite 缓存（<c>store\video\video.db</c>）把已知信息填上，零成本；
/// ② 没缓存的点「🔎 读取全部」并发批量探测（并发度取「设置 → 通用」里的最大并发数），读完写回缓存。
/// </para>
/// <para><b>没有「保存」按钮</b>：这里没有任何"设置项"，全是动作按钮（规范 3.5）。</para>
/// </summary>
public partial class VideoListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条 + 选择器命令（/ 需求 §4.8.5） ====================

    private const string VideoFilePatterns =
        "*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m4v;*.mpg;*.mpeg";

    /// <summary>📂 导入（主按钮 / 「📁 文件夹…」）：选目录 → 写回 <see cref="Folder"/> 并扫描（替换当前列表）。</summary>
    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择视频目录", Folder);
        if (string.IsNullOrWhiteSpace(dir)) return;
        Folder = dir;
        ScanCommand.Execute(null);
    }

    /// <summary>📂 导入 → 「🎞 视频文件…」：多选视频文件，直接追加进当前列表（⛔ 不扫描整目录）。</summary>
    [RelayCommand]
    private async Task PickVideoFilesAsync()
    {
        var paths = await DialogHooks.PickFilesAsync("选择视频文件（可多选）", null, true, VideoFilePatterns);
        if (paths.Count == 0) return;
        ImportVideoFiles(paths);
    }

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Kind = ToolbarItemKind.SplitButton, Text = "📂 导入", CommandPath = nameof(PickFolderCommand), ToolTip = "导入视频：选文件夹扫描（替换当前列表）/ 选单个视频文件追加；目录框里回车也等于「选文件夹」",
                Items = new ToolbarMenuItemDescriptor[]
                {
                    new() { Text = "📁 文件夹…", CommandPath = nameof(PickFolderCommand), ToolTip = "选一个目录扫描（替换当前列表）" },
                    new() { Text = "🎞 视频文件…", CommandPath = nameof(PickVideoFilesCommand), ToolTip = "选一个或多个视频文件，追加进当前列表" },
                } },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "如 D:\\Videos（回车即扫描）", ValuePath = nameof(Folder), EnterCommandPath = nameof(ScanCommand) },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "按文件名 / 类型过滤…", ValuePath = nameof(SearchText) },
        new() { Text = "🔄 重扫目录", CommandPath = nameof(RefreshCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "重新枚举当前目录（按左边「含子目录」决定深不深扫）；勾选状态保留，媒体信息优先走 SQLite 缓存" },
        new() { Kind = ToolbarItemKind.Toggle, Text = "含子目录", ValuePath = nameof(Recursive) },
        new() { Text = "🔎 读取全部媒体信息", CommandPath = nameof(LoadAllMediaInfoCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = "对还没有媒体信息的视频并发跑 ffprobe（并发度见「设置 → 通用」的最大并发数）；读完写进 store\\video\\video.db 缓存，下次扫描直接命中" },
        new() { Kind = ToolbarItemKind.Separator },
        // ⚠️ 原先 `TextPath = CheckedCount` + `TextFormat = "… ({0})"`，渲染出来的按钮是**空白**的
        //    （根因 = `ToolbarHost.BuildContent` 的动态文案绑定漏了 Source，⛔ Host 没有 DataContext）。
        //    已按需求 §4.8.4 第 3 条改回纯文案（⛔ 工具条不放计数指示文字），渲染侧那条也已修。
        new() { Text = "➡ 发送到批量处理", CommandPath = nameof(SendToBatchCommand),
                IsEnabledPath = nameof(HasSelectedRows), ShowToolTipOnDisabled = true, ToolTip = "先选中视频（Ctrl 增选 / Shift 连选 / Ctrl+A 全选），再点这里：批量生成**转码**任务送到「批量处理」页（只是建任务，不立刻跑）" },
        new() { Kind = ToolbarItemKind.Separator },
        new() { Text = "🧹 清除列表", CommandPath = nameof(ClearListCommand), ToolTip = "清空当前列表（含选中 / 筛选），回到空态；⛔ 不删除任何视频文件、不重扫目录" }
    };

    /// <summary>上次选的目录（**只在本次运行内**记住；不写配置，见类注释）。</summary>
    private static string _lastFolder = "";

    private readonly IAppConfigService? _cfg;
    private readonly VideoMediaIndex? _index;
    private readonly VideoTaskStore? _store;
    private readonly INavigationService? _nav;

    /// <summary>跨模块消息通道（媒体资产 → 本页投递待导入的视频路径）。</summary>
    private readonly IModuleBus? _bus;

    /// <summary>本模块设置（v3.64：并发数已从 app.json 下沉到 config\video-tools.json）。</summary>
    private readonly IVideoToolsSettingsService? _vt;

    private readonly List<VideoItem> _all = new();

    private bool _syncing;

    /// <summary>扫描 / 探测取消用（重复点先取消上一次）。</summary>
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _probeCts;

    public ObservableCollection<VideoItem> Videos { get; } = new();

    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    // StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private string _summaryText = "尚未扫描";
    [ObservableProperty] private VideoItem? _selected;

    /// <summary>供行内按钮绑定（<c>{Binding !IsBusy}</c> 只对同层路径可靠，跨 ListBox 取父级 VM 得用这个）。</summary>
    public bool IsNotBusy => !IsBusy;

    // 表头（点一下切换排序）
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _durationHeader = "时长";
    [ObservableProperty] private string _resolutionHeader = "分辨率";
    [ObservableProperty] private string _codecHeader = "编码";
    [ObservableProperty] private string _bitrateHeader = "码率";
    [ObservableProperty] private string _modifiedHeader = "修改时间";
    // 扩充列（§6.4.8）
    [ObservableProperty] private string _fpsHeader = "帧率";
    [ObservableProperty] private string _containerHeader = "容器格式";
    [ObservableProperty] private string _audioHeader = "音频编码";
    [ObservableProperty] private string _pathHeader = "路径";
    [ObservableProperty] private string _createdHeader = "创建时间";

    // 详情卡里的**可编辑**字段（§6.4.7 文件名）：提交时才动磁盘。
    [ObservableProperty] private string _editName = "";

    // ==================== 列显隐（基类管状态；这里只给 XAML 提供具名属性） ====================

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsSizeVisible => Vis("Size");
    public bool IsDurationVisible => Vis("Duration");
    public bool IsResolutionVisible => Vis("Resolution");
    public bool IsCodecVisible => Vis("Codec");
    public bool IsBitrateVisible => Vis("Bitrate");
    public bool IsModifiedVisible => Vis("Modified");
    // 扩充列
    public bool IsFpsVisible => Vis("Fps");
    public bool IsContainerVisible => Vis("Container");
    public bool IsAudioVisible => Vis("Audio");
    public bool IsPathVisible => Vis("Path");
    public bool IsCreatedVisible => Vis("Created");

    protected override string PageKey => "video-list";

    /// <summary>
    /// 列的元数据（顺序即表头与行的列顺序）。
    /// <para>最后一列 <c>Detail</c> 是表头右侧那个无文字小按钮的固定窄列。</para>
    /// </summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // §4.8.1：每列给一条**示例值** —— 用「示例宽 ≤ 表头宽」判定该列是否锁成表头宽（锁了就不可拖，且不低于 MinLockedWidth=34 保证把手可拖）。
        new("Name", "名称", "3.2*", sample: "示例视频 2026.mp4"),
        new("Type", "类型", "0.9*", sample: ".mp4"),
        new("Size", "大小", "1*", sample: "1.5 GB"),
        new("Duration", "时长", "0.9*", sample: "1:02:03"),
        new("Resolution", "分辨率", "1.1*", sample: "1920×1080"),
        new("Codec", "编码", "1.1*", sample: "H.265/HEVC"),
        new("Bitrate", "码率", "1.1*", sample: "8.5 Mbps"),
        new("Modified", "修改时间", "1.5*", sample: "2026-10-13 15:00"),
        // 扩充（表头右键均可隐藏）：帧率 / 容器格式 / 音频编码 / 路径 / 创建时间
        new("Fps", "帧率", "0.8*", sample: "29.97"),
        new("Container", "容器格式", "1*", sample: "MP4"),
        new("Audio", "音频编码", "1*", sample: "AAC"),
        new("Path", "路径", "2.5*", sample: @"D:\Videos\示例视频 2026.mp4"),
        new("Created", "创建时间", "1.5*", sample: "2026-10-13 15:00"),
    };

    public bool HasSelection => Selected != null;
    public bool HasItems => Videos.Count > 0;

    /// <summary>详情卡的数据源：有选中项时显示它。</summary>
    public VideoItem? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（的 L2 迁移页，需求 §4.5.1） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

}
