using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>导入 / 导出用的最小包记录（只要 <see cref="Id"/> 就能重装；Name / Version 仅供人看）。v3.19。</summary>
public class PackageExportItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

/// <summary>
/// 软件包页。
/// <para>
/// 布局遵循规范 <b>3.8 列表页规范</b>：左边「可排序表头的列表」，右边「选中项的详情列」，
/// 列表上挂右键菜单（安装 / 升级 / 卸载 / Pin / 取消 Pin / 复制 Id / 读取详情 / 打开主页）。
/// </para>
/// <para>
/// <b>本页没有任何「保存」按钮</b>（规范 3.7）：源、静默安装、排序列都是点一下就生效并写
/// <c>config\package.json</c>（由 <see cref="IPackageSettingsService"/> 落盘）。
/// / v3.80：源的**增删 / 切换**全要 UAC ⇒ 归「命令库 → winget 源」集合的命令；本页只管"查包"。
/// **勾一下源就立刻生效** —— 勾上但系统里没有的源（如默认未启用的 <c>msstore</c>）
/// 会标「（未启用）」，勾它立刻跑 <c>winget source enable</c>（⛔ 工具条不再有「✅ 生效」按钮）。
/// 本页进入时读一次源列表，因此设置页改完再回到本页即生效（页面每次导航都会重建 VM，见规范 6.5）。
/// </para>
/// <para>
/// ⚠️ 刻意<b>不订阅</b> <see cref="IPackageSettingsService.Changed"/>：那个服务是单例，
/// 页面 VM 是瞬态，订阅了就会把每次进入页面的 VM 都挂在单例上（内存泄漏 + 重复回调）。
/// </para>
/// </summary>
public partial class PackageViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private enum ListMode { None, Search, Installed, Upgrades, Pinned }

    // ==================== 动词 → 命令模板（🔴 抽象落点） ====================
    // 页面上每个按钮 = 一个**通用动词**（刷新索引 / 全部升级 / 卸载 …），这里给它配一条**后端命令模板**。
    // 将来切后端（scoop / choco）或改成"脚本驱动"（每个动词一条 ScriptItem）⇒ **只改这一块**，
    // 页面与详情页的文案、按钮、状态判定一行都不用动。
    // ⛔ 不要把这些模板散落到各个命令方法里 —— 散了就不是抽象，只是"每条命令各写一遍"。

    /// <summary>后端可执行文件名（切后端时改这里）。</summary>
    private const string BackendExeName = "winget.exe";

    // 🔴 工具条每个按钮的 ToolTip = 它真正执行的 winget 命令（用户定调：⛔ 不写抽象描述）。
    // 下面每条既是文档也是提示词来源；<占位符> 会原样显示给用户（告诉他这里填什么）。

    /// <summary>列出已安装（<c>winget list</c>）。</summary>
    private const string CmdListInstalled = "winget list --accept-source-agreements";

    /// <summary>列出可升级（<c>winget upgrade</c>）。</summary>
    private const string CmdListUpgrades = "winget upgrade --accept-source-agreements --include-unknown";

    /// <summary>列出被 pin 的包（<c>winget pin list</c>）。</summary>
    private const string CmdListPinned = "winget pin list";

    /// <summary>搜索（<c>winget search</c>）：一律不传 <c>--source</c>，走 winget 自己的默认源集合。</summary>
    private const string CmdSearch = "winget search <关键字> --accept-source-agreements";

    /// <summary>刷新索引（<c>source update</c>）：把各源的元数据拉到最新，之后「可升级」才准。</summary>
    private const string CmdRefreshIndex = "{exe} source update";

    /// <summary>安装（<c>winget install</c>）。</summary>
    private const string CmdInstall =
        "winget install --id <id> --exact --accept-package-agreements --accept-source-agreements --disable-interactivity";

    /// <summary>升级单个包（<c>winget upgrade --id</c>）。</summary>
    private const string CmdUpgrade =
        "winget upgrade --id <id> --exact --accept-package-agreements --accept-source-agreements --disable-interactivity";

    /// <summary>全部升级（<c>upgrade --all</c>）。已锁定的包后端会自行跳过。</summary>
    private const string CmdUpgradeAll =
        "{exe} upgrade --all --accept-package-agreements --accept-source-agreements --disable-interactivity";

    /// <summary>卸载（<c>uninstall --id {id}</c>）。</summary>
    private const string CmdUninstall = "{exe} uninstall --id {id} --exact --disable-interactivity";

    /// <summary>静默后缀（安装器不弹界面）；与 <see cref="CmdUpgradeAll"/> 拼接。</summary>
    private const string ArgSilent = " --silent";

    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;


    /// <summary>当前模式下的全量结果（界面集合只放过滤 + 排序后的投影）。</summary>
    private readonly List<PackageInfo> _all = new();

    private ListMode _mode = ListMode.None;

    private readonly IAppConfigService? _app;

    /// <summary>导入 / 导出默认目录（当前配置档 <c>store\</c>；本模块没有自己的库子目录）。</summary>
    public string ImportExportDefaultDir => _app?.StoreDirectory ?? "";

    /// <summary>已安装包 Id 的缓存（搜索结果要靠它把「状态」列标准）。</summary>
    private HashSet<string>? _installedIds;

    /// <summary>
    /// 系统里**显式源**的名字（<c>source list</c> 的「显式」列 = true，如 <c>winget-font</c>）：
    /// 它们不参与默认搜索 ⇒ 「全部源」时要单独补查一遍，否则勾了也搜不到（v3.79）。
    /// </summary>
    private readonly HashSet<string> _explicitSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 同一次 <c>winget list</c> 的**完整记录**（Id / Name / Version）：用来把「显示名被截断 ⇒ Id 残缺」的
    /// 搜索结果行修回已安装版本（见 <see cref="ResolveTruncatedIdAsync"/>）。与 <see cref="_installedIds"/> 同生同灭。
    /// </summary>
    private List<PackageInfo>? _installedList;

    /// <summary>抑制"改属性 → 回写设置"的开关（批量灌值时用，避免无谓写盘与递归）。</summary>
    private bool _syncing;

    public ObservableCollection<PackageInfo> Packages { get; } = new();

    /// <summary>
    /// 工具条（/ 需求 §4.8.5：**由 Shell 统一渲染**，页面⛔ 不再自写 <c>Border.toolbar</c>）。
    /// 顺序 = 折叠顺序的**反序**（越靠前越不易被折进「⋯」）。
    /// <para>
    /// 🔴 2026-10-17 动词层：**按钮文案**一律用通用动词（列出 / 搜索 / 升级 / 锁定 / 卸载 …），
    /// ⛔ 不出现后端名 —— 换后端时按钮文案一个字都不用改。
    /// 🔴 反转：**ToolTip 一律给真实 winget 命令**（用户定调）—— 用户要看的是"到底跑了什么"。
    /// 分组顺序 = 查看（源 / 搜索 / 三种列表）→ 刷新 → 写操作 → 偏好 → 导入导出。
    /// </para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        // 🔴 v3.80：源选择框**已删除**（用户定调）—— 源是系统 winget 的配置，增删 / 切换全要 UAC，
        // 由「命令库 → winget 源」集合里的命令执行；本页搜索一律不传 --source（走默认源集合 + 补查显式源）。
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "关键字：git / Docker / ffmpeg…",
            ValuePath = nameof(SearchText),
            IsEnabledPath = nameof(IsSupported),
            EnterCommandPath = nameof(SearchCommand),
            ToolTip = CmdSearch
        },
        new() { Text = "🔍 搜索", Classes = "ghost", CommandPath = nameof(SearchCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = CmdSearch },
        new() { Text = "📋 已安装", CommandPath = nameof(LoadInstalledCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = CmdListInstalled },
        new() { Text = "⬆ 可升级", CommandPath = nameof(LoadUpgradesCommand), IsEnabledPath = "!" + nameof(IsBusy), ToolTip = CmdListUpgrades },
        new()
        {
            Text = "🔒 已锁定",
            CommandPath = nameof(LoadPinnedCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = CmdListPinned
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Text = "🔄 重载",
            CommandPath = nameof(RefreshCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = CmdListInstalled + " / " + CmdListUpgrades + " / " + CmdSearch + "（按当前列表重跑）"
        },
        new()
        {
            Text = "🔄 刷新索引",
            Classes = "ghost",
            CommandPath = nameof(RefreshIndexCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = CmdRefreshIndex.Replace("{exe}", "winget")
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Text = "⬇ 安装选中",
            CommandPath = nameof(InstallCheckedCommand),
            IsEnabledPath = nameof(CanRunBatch),
            ShowToolTipOnDisabled = true,
            ToolTip = CmdInstall
        },
        new()
        {
            Text = "⬆ 升级选中",
            CommandPath = nameof(UpgradeCheckedCommand),
            IsEnabledPath = nameof(CanRunBatch),
            ShowToolTipOnDisabled = true,
            ToolTip = CmdUpgrade
        },
        new()
        {
            Text = "⬆ 全部升级",
            CommandPath = nameof(UpgradeAllCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = CmdUpgradeAll.Replace("{exe}", "winget") + ArgSilent
        },
        new()
        {
            Text = "🗑 卸载选中",
            Classes = "danger-btn",
            CommandPath = nameof(UninstallCheckedCommand),
            IsEnabledPath = nameof(CanRunBatch),
            ShowToolTipOnDisabled = true,
            ToolTip = CmdUninstall.Replace("{exe}", "winget")
        },
        // 原「已勾选 N 个」指示文字（Label + CheckedText）删除 —— 需求 §4.8.4 第 3 条：⛔ 工具条不放计数指示，
        //        勾选状态在列表勾选框上一眼可见；前置条件写进下面两个按钮的 ToolTip（ShowToolTipOnDisabled）。
        new() { Kind = ToolbarItemKind.Toggle, Text = "静默安装", ValuePath = nameof(SilentInstall), ToolTip = ArgSilent.Trim() + "（追加到 install / upgrade 末尾；商店源不支持，会自动跳过）" },
        new()
        {
            Text = "📤 导出",
            Classes = "ghost",
            CommandPath = nameof(ExportPackagesCommand),
            ToolTip = "把当前列表（建议先点「📋 已安装」）导成 json，供新系统恢复"
        },
        new()
        {
            Text = "📥 导入",
            Classes = "ghost",
            CommandPath = nameof(ImportPackagesCommand),
            ToolTip = "选一个导出的 json，生成 winget 批量安装脚本(.bat)，到新系统运行即可恢复"
        },
    };

    // ==================== 页面状态 ====================

    [ObservableProperty] private bool _isSupported = true;
    [ObservableProperty] private string _supportText = "正在检测包管理器……";
    [ObservableProperty] private bool _isBusy;
    // StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private string _modeText = "尚未加载 —— 点「📥 已安装」或直接联网搜索";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _installedOnly;

    // 原「仅看 Pin」勾选框（`_showPinned`）已删除 —— pin 改成一条真命令「📌 Pin 列表」（见 LoadPinnedAsync）。
    [ObservableProperty] private bool _silentInstall = true;
    [ObservableProperty] private PackageInfo? _selected;
    [ObservableProperty] private string _detailExtra = "";

    /// <summary>
    /// 给搜索结果**补上来源**（v3.78）：老版本 winget 的 <c>search</c> 输出不一定有 Source 列，
    /// 而详情 / 安装都要拿它拼 <c>--source</c> —— 商店包（msstore）不带源是读不出详情的。
    /// </summary>
    private static void StampSource(IReadOnlyList<PackageInfo> items, string? source)
    {
        if (string.IsNullOrEmpty(source)) return;
        foreach (var it in items)
            if (string.IsNullOrEmpty(it.Source)) it.Source = source;
    }

    // ==================== 表头（点一下切换排序） ====================

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _idHeader = "Id";
    [ObservableProperty] private string _versionHeader = "版本";
    [ObservableProperty] private string _sourceHeader = "源";
    [ObservableProperty] private string _statusHeader = "状态";
    [ObservableProperty] private string _pinnedHeader = "锁定";

    // ==================== 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ====================

    private readonly IColumnLayoutService? _cols;

    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里）
    public bool IsNameVisible => Vis("Name");
    public bool IsIdVisible => Vis("Id");
    public bool IsVersionVisible => Vis("Version");
    public bool IsSourceVisible => Vis("Source");
    public bool IsStatusVisible => Vis("Status");

    protected override string PageKey => "script-packages";

    // ==================== 分页 ====================
    // 数据全在内存（winget 一次性吐完），分页只做**切片**（⛔ 不回查后端）；
    // 预设 20 / 50 / 100 / 全部 —— 用户定调，⛔ 别动全局那档（25/50/100/500/全部）。

    /// <summary>本页自己的页大小预设。</summary>
    private static readonly int[] PackagePageSizes = { 20, 50, 100, AllPageSize };

    public override IReadOnlyList<int> PageSizeChoices => PackagePageSizes;

    /// <summary>启用底栏分页（基类默认 false）；数据源在内存 ⇒ 切片由 <see cref="ApplyView"/> 做。</summary>
    protected override bool PagingEnabled => true;

    /// <summary>列的元数据（顺序即表头与行的列顺序）。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）
        new("Name", "名称", "3*", sample: "7-Zip 24.08"),
        new("Id", "Id", "3.6*", sample: "7zip.7zip"),
        new("Version", "版本", "1.3*", sample: "24.08"),
        new("Source", "源", "1.1*", sample: "winget"),
        new("Status", "状态", "1.5*", sample: "可升级"),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Packages.Count > 0) Selected = Packages[0];
    }

    /// <summary>筛选取值（多项筛选用）：按列键拿这一行该列的文本。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Name" => ((PackageInfo)row).Name,
        "Id" => ((PackageInfo)row).Id,
        "Version" => ((PackageInfo)row).Version,
        "Source" => ((PackageInfo)row).Source,
        "Status" => ((PackageInfo)row).StatusText,
        _ => null
    };

    public bool HasSelection => Selected != null;
    public bool HasItems => Packages.Count > 0;

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public PackageInfo? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（的 L2 迁移页，需求 §4.5.1） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

}
