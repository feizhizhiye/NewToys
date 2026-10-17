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
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>导入 / 导出用的最小包记录（只要 <see cref="Id"/> 就能重装；Name / Version 仅供人看）。v3.19 / NT-008。</summary>
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
/// 「启用 / 关闭 Store 源」这类成体系的设置归 <b>设置 → 软件源</b> 页管；本页进入时读一次，
/// 因此设置页改完再回到本页即生效（页面每次导航都会重建 VM，见规范 6.5）。
/// </para>
/// <para>
/// ⚠️ 刻意<b>不订阅</b> <see cref="IPackageSettingsService.Changed"/>：那个服务是单例，
/// 页面 VM 是瞬态，订阅了就会把每次进入页面的 VM 都挂在单例上（内存泄漏 + 重复回调）。
/// </para>
/// </summary>
public partial class PackageViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private enum ListMode { None, Search, Installed, Upgrades }

    // ==================== 动词 → 命令模板（🔴 抽象落点） ====================
    //
    // 页面上每个按钮 = 一个**通用动词**（刷新索引 / 全部升级 / 卸载 …），这里给它配一条**后端命令模板**。
    // 将来切后端（scoop / choco）或改成"脚本驱动"（每个动词一条 ScriptItem）⇒ **只改这一块**，
    // 页面与详情页的文案、按钮、状态判定一行都不用动。
    //
    // ⛔ 不要把这些模板散落到各个命令方法里 —— 散了就不是抽象，只是"每条命令各写一遍"。

    /// <summary>后端可执行文件名（切后端时改这里）。</summary>
    private const string BackendExeName = "winget.exe";

    /// <summary>刷新索引（<c>source update</c>）：把各源的元数据拉到最新，之后「可升级」才准。</summary>
    private const string CmdRefreshIndex = "{exe} source update";

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

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    /// <summary>已安装包 Id 的缓存（搜索结果要靠它把「状态」列标准）。</summary>
    private HashSet<string>? _installedIds;

    /// <summary>
    /// 同一次 <c>winget list</c> 的**完整记录**（Id / Name / Version）：用来把「显示名被截断 ⇒ Id 残缺」的
    /// 搜索结果行修回已安装版本（见 <see cref="ResolveTruncatedIdAsync"/>）。与 <see cref="_installedIds"/> 同生同灭。
    /// </summary>
    private List<PackageInfo>? _installedList;

    /// <summary>抑制"改属性 → 回写设置"的开关（批量灌值时用，避免无谓写盘与递归）。</summary>
    private bool _syncing;

    public ObservableCollection<PackageInfo> Packages { get; } = new();
    public ObservableCollection<PackageSourceOption> SourceOptions { get; } = new();

    /// <summary>
    /// 工具条（NT-055 / 需求 §4.8.5：**由 Shell 统一渲染**，页面⛔ 不再自写 <c>Border.toolbar</c>）。
    /// 顺序 = 折叠顺序的**反序**（越靠前越不易被折进「⋯」）。
    /// <para>
    /// 🔴 2026-10-17 动词层：文案一律用**通用动词**（列出 / 搜索 / 升级 / 锁定 / 卸载 …），
    /// ⛔ 不出现后端名（winget / scoop）—— 换后端时按钮文案一个字都不用改。
    /// 分组顺序 = 查看（源 / 搜索 / 三种列表）→ 刷新 → 写操作 → 偏好 → 导入导出。
    /// </para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        // 2026-10-17：一排源按钮（ChipGroup）→ **单个筛选框**（Combo）—— 源多了以后按钮条会把关键字输入框挤没，
        // 且本页的源本来就是**单选**语义（OnSelectedSourceChanged 只点亮一个），下拉更贴合。
        // 「全部源」= null ⇒ 搜索时逐个源查一遍再合并。
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 132,
            ItemsPath = nameof(SourceOptions),
            DisplayPath = "Label",
            SelectedItemPath = nameof(SelectedSource),
            ToolTip = "软件源：只影响「🔍 搜索」；「📋 已安装 / ⬆ 可升级 / 🔒 已锁定」与源无关"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "关键字：git / Docker / ffmpeg…",
            ValuePath = nameof(SearchText),
            IsEnabledPath = nameof(IsSupported),
            EnterCommandPath = nameof(SearchCommand),
            ToolTip = "已安装 / 可升级列表里，这里就是本地过滤器；点「🔍 搜索」才去查远端源"
        },
        new() { Text = "🔍 搜索", Classes = "ghost", CommandPath = nameof(SearchCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "📋 已安装", CommandPath = nameof(LoadInstalledCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "⬆ 可升级", CommandPath = nameof(LoadUpgradesCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new()
        {
            Text = "🔒 已锁定",
            CommandPath = nameof(LoadPinnedCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "列出被锁定（暂停更新）的包 —— 已存在的会贴上 🔒，不在列表里的补一行"
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Text = "🔄 重新加载",
            CommandPath = nameof(RefreshCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "按当前所在的列表重跑一次（搜索 / 已安装 / 可升级）—— 要等几秒"
        },
        new()
        {
            Text = "🔃 刷新索引",
            Classes = "ghost",
            CommandPath = nameof(RefreshIndexCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "把各软件源的元数据拉到最新（等价 apt update）—— 「可升级」的结果依赖它"
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Text = "⬇ 安装选中",
            CommandPath = nameof(InstallCheckedCommand),
            IsEnabledPath = nameof(CanRunBatch),
            ShowToolTipOnDisabled = true,
            ToolTip = "把勾选的包逐条安装（已安装的后端会自行跳过；单个失败不影响其余）"
        },
        new()
        {
            Text = "⬆ 升级选中",
            CommandPath = nameof(UpgradeCheckedCommand),
            IsEnabledPath = nameof(CanRunBatch),
            ShowToolTipOnDisabled = true,
            ToolTip = "把勾选的包逐条升级（没有可升级版本的自动跳过）"
        },
        new()
        {
            Text = "⬆⬆ 全部升级",
            CommandPath = nameof(UpgradeAllCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "把所有可升级的包一次性升到最新版（已锁定的跳过）—— 执行前会确认一次"
        },
        new()
        {
            Text = "🗑 卸载选中",
            Classes = "danger-btn",
            CommandPath = nameof(UninstallCheckedCommand),
            IsEnabledPath = nameof(CanRunBatch),
            ShowToolTipOnDisabled = true,
            ToolTip = "把勾选的包逐条卸载（不可撤销）—— 执行前只确认一次，不逐条弹窗"
        },
        // v3.60：原「已勾选 N 个」指示文字（Label + CheckedText）删除 —— 需求 §4.8.4 第 3 条：⛔ 工具条不放计数指示，
        //        勾选状态在列表勾选框上一眼可见；前置条件写进下面两个按钮的 ToolTip（ShowToolTipOnDisabled）。
        new() { Kind = ToolbarItemKind.Toggle, Text = "静默安装", ValuePath = nameof(SilentInstall) },
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
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private string _modeText = "尚未加载 —— 点「📥 已安装」或直接联网搜索";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _installedOnly;

    // v3.22：原「仅看 Pin」勾选框（`_showPinned`）已删除 —— pin 改成一条真命令「📌 Pin 列表」（见 LoadPinnedAsync）。
    [ObservableProperty] private bool _silentInstall = true;
    [ObservableProperty] private PackageInfo? _selected;
    [ObservableProperty] private PackageSourceOption? _selectedSource;
    [ObservableProperty] private string _detailExtra = "";

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

    /// <summary>v3.37 / NT-002：勾选列（Pick）恒可见 —— 它是批量操作的前提。</summary>
    public bool IsPickVisible => true;

    protected override string PageKey => "script-packages";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.37 / NT-002：首列 = **勾选列**（Pick）——
        //    加了首列后，表头 / 行模板里**所有 Grid.Column 索引都要 +1**（⛔ 别漏一处）。
        new("Pick", "", "34", sortable: false),
        // v3.22 §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）
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
    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构 = 只读信息（名称 / Id / 版本 / 状态 / 源 / 发布者 / 协议 / 安装位置 / 主页 / 描述，**全部只读可选中复制**）
    /// + 动作组合（安装 · 升级 · 详情 · 锁定/解除锁定 · 卸载 · 打开主页，**一行按钮组**，按选中项状态显隐）。</para>
    /// <para>🔴 2026-10-17 用户口径：只读信息一律只读 + 可复制；**已安装的包不再给「安装」**（由
    /// <see cref="PackageInfo.IsInstalled"/> 驱动），Pin 只留一个三态按钮（未 Pin / 已 Pin 二选一显示）。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一个包。选中后这里显示版本、源、状态、发布者、主页、协议与描述；右键列表还有安装 / 升级 / 卸载 / Pin 等操作。",
        Sections =
        {
            new DetailSection
            {
                Header = "选中包",
                Fields =
                {
                    // NT-068 S7'：11 个只读字段一律 Selectable=true（SelectableTextBlock = 只读可选可复制）。
                    // ⛔ 包装成可编辑 TextBox 会让「详情卡」变成第二个写入点，与「设置页是唯一写入点」冲突。
                    new DetailField
                    {
                        Label = "名称",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "Id",
                        ValuePath = "DetailItem.Id",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // 原来「版本 / 可升级到」是**同行并排**（版本 → 可升级）→ 按 NT-054 已拍板口径拆成两行
                    new DetailField
                    {
                        Label = "版本",
                        ValuePath = "DetailItem.Version",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "可升级到",
                        ValuePath = "DetailItem.Available",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "状态",
                        ValuePath = "DetailItem.StatusText",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "源",
                        ValuePath = "DetailItem.Source",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "发布者",
                        ValuePath = "DetailItem.Publisher",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "协议 / 许可证",
                        ValuePath = "DetailItem.License",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "安装位置",
                        ValuePath = "DetailItem.InstallLocation",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "主页",
                        ValuePath = "DetailItem.Homepage",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "描述",
                        ValuePath = "DetailItem.Description",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "（描述等信息来自「🔎 详情」，点一下才去源里查）"
                    }
                }
            }
        },
        Actions =
        {
            // —— 一条动作组合：安装 / 升级按「是否已安装」二选一，Pin 按「是否已 Pin」二选一 ——
            new DetailAction
            {
                Text = "⬇ 安装",
                Classes = "primary",
                ToolTip = "安装这个包（用当前包管理器后端）",
                IsVisiblePath = nameof(ShowInstallAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = InstallCommand
            },
            new DetailAction
            {
                Text = "⬆ 升级",
                Classes = "primary",
                ToolTip = "升级到最新版（已是最新版时置灰）",
                IsVisiblePath = nameof(ShowUpgradeAction),
                IsEnabledPath = nameof(CanUpgradeSelected),
                Command = UpgradeCommand
            },
            new DetailAction
            {
                Text = "🔎 详情",
                Classes = "ghost",
                ToolTip = "向源查询这个包的详细信息（发布者 / 协议 / 主页 / 描述）",
                IsVisiblePath = nameof(HasSelection),
                Command = LoadDetailCommand
            },
            new DetailAction
            {
                Text = "🔒 锁定版本",
                Classes = "ghost",
                ToolTip = "锁定到当前版本 = 暂停更新，「全部升级 / 升级」都会跳过它",
                IsVisiblePath = nameof(ShowPinAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = PinCommand
            },
            new DetailAction
            {
                Text = "🔒 解除锁定",
                Classes = "ghost",
                ToolTip = "解除锁定，恢复参与升级",
                IsVisiblePath = nameof(ShowUnpinAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = UnpinCommand
            },
            new DetailAction
            {
                Text = "🗑 卸载",
                Classes = "ghost",
                ToolTip = "卸载这个包（不可撤销，会再确认一次）",
                IsVisiblePath = nameof(ShowUninstallAction),
                IsEnabledPath = "!" + nameof(IsBusy),
                Command = UninstallCommand
            },
            // 2026-10-17：「📋 复制信息」删除 —— 详情卡内所有文字本身就可复制（SelectableTextBlock），
            //              再给一个"复制整卡"按钮是重复入口。
            new DetailAction
            {
                Text = "🌐 打开主页",
                Classes = "ghost",
                ToolTip = "在默认浏览器里打开主页（先点「🔎 详情」才有地址）",
                IsVisiblePath = nameof(HasSelection),
                Command = OpenHomepageCommand
            }
        }
    };

    // —— 动作可见性 / 可用性（路径绑定的具名属性；派生自 Selected，选中变化时统一通知） ——

    /// <summary>⬇ 安装：选中项**未安装**时才出现（🔴 已安装的包不该再给「安装」）。</summary>
    public bool ShowInstallAction => Selected is { IsInstalled: false };

    /// <summary>⬆ 升级：选中项**已安装**时出现（是否可点另看 <see cref="CanUpgradeSelected"/>）。</summary>
    public bool ShowUpgradeAction => Selected is { IsInstalled: true };

    /// <summary>升级按钮可用态：已安装且确有新版本（否则置灰 —— 悬浮提示说明已是最新版）。</summary>
    public bool CanUpgradeSelected => Selected is { CanUpgrade: true } && !IsBusy;

    /// <summary>📌 Pin：未 Pin 时显示。</summary>
    public bool ShowPinAction => Selected is { IsPinned: false };

    /// <summary>📌 取消 Pin：已 Pin 时显示（三态单按钮的另一半）。</summary>
    public bool ShowUnpinAction => Selected is { IsPinned: true };

    /// <summary>🗑 卸载：只有已安装的包才有意义。</summary>
    public bool ShowUninstallAction => Selected is { IsInstalled: true };


    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PackageViewModel() { }

    public PackageViewModel(IPackageManagerService pkg, IPackageSettingsService settings, IColumnLayoutService cols, IAppConfigService app)
        : base(cols)
    {
        _app = app;
        _pkg = pkg;
        _settings = settings;
        _cols = cols;

        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        _syncing = true;
        var s = settings.Settings;
        SetSort(string.IsNullOrWhiteSpace(s.SortColumn) ? "Name" : s.SortColumn, s.SortDescending);
        SilentInstall = s.SilentInstall;
        _syncing = false;
        UpdateHeaders();

        _ = InitAsync();
    }

    // ==================== 初始化 ====================

    private async Task InitAsync()
    {
        if (_pkg == null || _settings == null)
        {
            IsSupported = false;
            SupportText = "包管理服务未就绪（DI 未注册）。";
            return;
        }

        try
        {
            var (ok, msg) = await _pkg.ProbeAsync();
            IsSupported = ok;
            SupportText = ok ? $"✅ {msg}" : $"⚠️ {msg}";
            if (!ok) { StatusText = msg; ModeText = "当前环境下不可用"; return; }
            await LoadSourcesAsync();
        }
        catch (Exception ex)
        {
            IsSupported = false;
            SupportText = "检测包管理器时出错：" + ex.Message;
        }
    }

    /// <summary>
    /// 源下拉 = 「全部（系统 winget 里的）」+ 各源。
    /// <para>
    /// ⚠️ v1.33 起源完全来自**系统 winget**（<c>winget source list</c>），本应用不再自己维护
    /// "启用 / 关闭某个源"的开关 —— 要增删源请去「设置 → 软件源」页（那里改的也是系统 winget）。
    /// </para>
    /// </summary>
    private async Task LoadSourcesAsync()
    {
        if (_pkg == null) return;

        var (ok, _, items) = await _pkg.ListSourcesAsync();

        SourceOptions.Clear();
        SourceOptions.Add(new PackageSourceOption { Label = "全部源", Value = null });

        if (ok)
        {
            foreach (var s in items)
                SourceOptions.Add(new PackageSourceOption { Label = s.Name, Value = s.Name });
        }
        else SourceOptions.Add(new PackageSourceOption { Label = "winget", Value = "winget" });

        var want = _settings?.Settings.DefaultSource ?? "";
        _syncing = true;
        SelectedSource = SourceOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", want, StringComparison.OrdinalIgnoreCase))
                         ?? SourceOptions[0];
        _syncing = false;
    }

    // ==================== 加载列表 ====================

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_pkg == null || IsBusy) return;
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            StatusText = "请输入搜索关键字（例如 git / Docker / ffmpeg）。";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "正在搜索……";

            var source = SelectedSource?.Value;
            if (source == null)
            {
                // 「全部源」= 对**每个已启用的源**各查一遍再合并。
                // 这样「关掉 Store 源」就是真的不查它（而不是查完再过滤）。
                var merged = new List<PackageInfo>();
                var errs = new List<string>();
                foreach (var opt in SourceOptions.Where(o => !string.IsNullOrEmpty(o.Value)).ToList())
                {
                    var (ok, msg, items) = await _pkg.SearchAsync(SearchText, opt.Value);
                    if (ok) merged.AddRange(items);
                    else errs.Add($"{opt.Label}: {msg}");
                }
                BeginList(merged, ListMode.Search);
                ApplyView();
                StatusText = merged.Count == 0
                    ? "没有匹配的包" + (errs.Count > 0 ? "｜" + string.Join("；", errs) : "")
                    : $"找到 {merged.Count} 个包（已合并 {SourceOptions.Count - 1} 个源）";
            }
            else
            {
                var (ok, msg, items) = await _pkg.SearchAsync(SearchText, source);
                BeginList(items.ToList(), ListMode.Search);
                ApplyView();
                StatusText = msg;
            }

            await MarkInstalledAsync();
            ApplyView();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadInstalledAsync()
    {
        if (_pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = "正在读取已安装列表（首次可能稍慢）……";
            var (ok, msg, items) = await _pkg.ListInstalledAsync();
            BeginList(items.ToList(), ListMode.Installed);
            BuildInstalledCache();
            await ApplyPinnedAsync();
            ApplyView();
            StatusText = msg;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadUpgradesAsync()
    {
        if (_pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = "正在检查可升级的包……";
            var (ok, msg, items) = await _pkg.ListUpgradesAsync();
            BeginList(items.ToList(), ListMode.Upgrades);
            BuildInstalledCache();
            await ApplyPinnedAsync();
            ApplyView();
            // v3.19 / NT-008：明确「已 Pin 的不出现在可升级列表」是 winget 行为、非漏报（需求 §6.2.3）。
            StatusText = $"{msg} · 已 Pin 的不会出现在此列表（winget 行为）";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _installedIds = null;
        _installedList = null;
        switch (_mode)
        {
            case ListMode.Search: await SearchAsync(); break;
            case ListMode.Upgrades: await LoadUpgradesAsync(); break;
            default: await LoadInstalledAsync(); break;
        }
    }

    private void BeginList(List<PackageInfo> items, ListMode mode)
    {
        // v3.37 / NT-002：旧行先解绑勾选监听，新行挂上（⛔ 别直接 AddRange，否则计数不更新）
        foreach (var p in _all) p.PropertyChanged -= OnRowPropertyChanged;
        _all.Clear();
        foreach (var p in items) p.PropertyChanged += OnRowPropertyChanged;
        _all.AddRange(items);
        _mode = mode;
        ModeText = mode switch
        {
            ListMode.Search => $"搜索结果：{SearchText}",
            ListMode.Installed => "已安装（本机全部包）",
            ListMode.Upgrades => "可升级",
            _ => "尚未加载"
        };
        if (mode != ListMode.Search && SearchText.Length > 0) SearchText = "";
        RefreshChecked();
    }

    /// <summary>搜索结果没有"是否已安装"的信息 → 用已安装集合补上，让「状态」列说实话。</summary>
    private async Task MarkInstalledAsync()
    {
        if (_pkg == null) return;
        if (_installedIds == null)
        {
            var (ok, _, items) = await _pkg.ListInstalledAsync();
            if (ok)
            {
                _installedIds = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
                _installedList = items.ToList();      // 同一次调用的完整记录（截断 Id 修复要用 Name / Version）
            }
        }
        if (_installedIds == null) return;

        var known = new HashSet<string>(_all.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var p in _all) p.IsInstalled = IsInstalledId(p.Id);
        await ApplyPinnedAsync();

        // 🔴 查询词本身是个**残缺 Id**（如 `edg`）时，补一条"已安装版本"的真相：
        //    搜索结果的 Id 来自本地化显示名截断（`Microsoft Edg` → `e`），用它对不上 `winget list`
        //    的完整 Id（`Microsoft.Edge`）。这里按「Id 前缀 + 名称前缀」认出它，版本改用已装版本。
        if (await ResolveTruncatedIdAsync(known)) ApplyView();

        // 承接方式（IsInstalledId）：Id 互相包含，或前 4 字符 Jaro 式贴合 —— 只解决"名字截断导致 Id 残缺"这一种。
        bool IsInstalledId(string id)
        {
            if (_installedIds.Contains(id)) return true;
            return id.Length >= 3 && KeyName(id).Length >= 3 && _installedIds.Any(inst =>
                inst.Contains(id, StringComparison.OrdinalIgnoreCase)
                || (KeyName(id) == KeyName(inst)));
        }
    }

    /// <summary>
    /// 把「被截断的解析结果」修回完整的已安装 Id（见 <see cref="MarkInstalledAsync"/> 的说明）。
    /// <para>只在**当前列表里确实存在装不上的残缺 Id** 时触发一次 <c>winget list</c>，不影响正常搜索。</para>
    /// </summary>
    /// <returns>真改了任何行 → true（调用方需要重算列表）。</returns>
    private async Task<bool> ResolveTruncatedIdAsync(HashSet<string> knownIds)
    {
        if (_pkg == null || _installedList == null) return false;
        var candidates = _all
            .Where(p => !_installedIds!.Contains(p.Id) && p.Id.Length >= 1 && KeyName(p.Name).Length >= 2)
            .ToList();
        if (candidates.Count == 0) return false;

        var changed = false;
        foreach (var p in candidates)
        {
            var nameKey = KeyName(p.Name);
            var inst = _installedList.FirstOrDefault(i =>
                // ⚠️ 残缺 Id（`e` / `edg`）必须**紧贴**完整 Id 的头部 —— 放宽到任意子串会把 `7zip` 认成 `Microsoft.Something.Zip`。
                (p.Id.Length <= 4
                    ? i.Id.StartsWith(p.Id, StringComparison.OrdinalIgnoreCase)
                    : (i.Id.StartsWith(p.Id, StringComparison.OrdinalIgnoreCase)
                       || i.Id.Contains("." + p.Id + ".", StringComparison.OrdinalIgnoreCase)))
                && KeyName(i.Name).StartsWith(nameKey, StringComparison.OrdinalIgnoreCase));

            if (inst == null) continue;

            // 同名同源可能真有多个包（如两个「Microsoft Edge」）⇒ 目标 Id 已在列表里就不合并
            if (knownIds.Contains(inst.Id)) continue;

            knownIds.Remove(p.Id);
            knownIds.Add(inst.Id);

            p.Id = inst.Id;
            if (!string.IsNullOrWhiteSpace(inst.Name)) p.Name = inst.Name;
            p.Version = inst.Version;
            p.Available = "";
            p.Source = inst.Source;
            p.IsInstalled = true;
            changed = true;
        }
        return changed;
    }

    /// <summary>比较用的名字键：只留字母数字、转小写，用来对齐被截断的显示名与完整名称。</summary>
    private static string KeyName(string? s)
        => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private void BuildInstalledCache()
        => _installedIds = new HashSet<string>(_all.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);

    /// <summary>把 pin 状态贴回列表（pin = 暂停更新，界面上必须看得出来）。</summary>
    private async Task ApplyPinnedAsync()
    {
        if (_pkg == null) return;
        var (ok, _, pinned) = await _pkg.ListPinnedAsync();
        if (!ok) return;
        var set = new HashSet<string>(pinned.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var p in _all) p.IsPinned = set.Contains(p.Id);
    }

    // ==================== 过滤 / 排序 ====================

    partial void OnSearchTextChanged(string value)
    {
        // 已安装 / 可升级 是本地列表 → 搜索框直接当过滤器（不联网）
        if (_mode != ListMode.Search && _mode != ListMode.None) ApplyView();
    }

    partial void OnInstalledOnlyChanged(bool value) => ApplyView();

    /// <summary>
    /// 📌 Pin 列表（v3.22，需求 §6.2.3）：跑 `winget pin list`，把结果**并入 `winget list` 的已安装列表**一起显示。
    /// <list type="bullet">
    ///   <item>不在已安装列表上时，先跑一次 <see cref="LoadInstalledAsync"/>（拿 `winget list` 当底子）；</item>
    ///   <item>pin 项已在列表里 → 贴 📌（`IsPinned = true`）；不在 → 追加成一行（pin list 只给包名，版本/名称可能空）。</item>
    /// </list>
    /// ⛔ 本页**没有「仅看 Pin」勾选框**（v3.22 去掉：过滤语义说不清，改成"把 pin 项加进列表"这个真命令）。
    /// </summary>
    [RelayCommand]
    private async Task LoadPinnedAsync()
    {
        if (_pkg == null || IsBusy) return;

        if (_mode != ListMode.Installed)
            await LoadInstalledAsync();     // 先有 winget list 的底子（内部会 ApplyView）

        try
        {
            IsBusy = true;
            StatusText = "正在读取 Pin 列表（winget pin list）……";
            var (ok, msg, pinned) = await _pkg.ListPinnedAsync();
            if (!ok) { StatusText = "读取 Pin 列表失败：" + msg; return; }

            var marked = 0;
            var added = 0;
            foreach (var p in pinned)
            {
                var hit = _all.FirstOrDefault(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase));
                if (hit != null) { hit.IsPinned = true; marked++; continue; }
                var row = new PackageInfo { Id = p.Id, Name = p.Name, IsPinned = true };
                row.PropertyChanged += OnRowPropertyChanged;
                _all.Add(row);
                added++;
            }

            ApplyView();
            StatusText = pinned.Count == 0
                ? "没有被 Pin 的包（winget pin list 为空）。"
                : $"Pin 列表 {pinned.Count} 项已并入已安装列表：{marked} 个已存在的贴上 📌、{added} 个新加入（Pin = 暂停更新）。";
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedChanged(PackageInfo? value)
    {
        DetailExtra = "";
        OnPropertyChanged(nameof(DetailItem));
        OnPropertyChanged(nameof(HasSelection));
        NotifyActionVisibility();
    }

    private void ApplyView()
    {
        IEnumerable<PackageInfo> q = _all;

        if (InstalledOnly && _mode == ListMode.Search)
            q = q.Where(p => p.IsInstalled);

        // v3.19 / NT-008：可升级列表**不显示已 Pin 的**（winget 会跳过它们 —— 需求 §6.2.3；也让它更贴近终端 winget upgrade 的条数）
        if (_mode == ListMode.Upgrades)
            q = q.Where(p => !p.IsPinned);

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0 && _mode != ListMode.Search)
            q = q.Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || p.Id.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        // 排序是"点一下就看到结果"的轻设置 → 变了就落盘（config\package.json）
        if (_settings != null && !_syncing
            && (!string.Equals(_settings.Settings.SortColumn, SortColumn, StringComparison.OrdinalIgnoreCase)
                || _settings.Settings.SortDescending != SortDescending))
        {
            _settings.Settings.SortColumn = SortColumn;
            _settings.Settings.SortDescending = SortDescending;
            _settings.Save();
        }

        var byVersion = Comparer<string>.Create(CompareVersion);
        var byText = StringComparer.CurrentCultureIgnoreCase;

        q = SortColumn switch
        {
            "Id" => SortDescending ? q.OrderByDescending(p => p.Id, StringComparer.OrdinalIgnoreCase)
                                    : q.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
            "Version" => SortDescending ? q.OrderByDescending(p => p.Version, byVersion)
                                         : q.OrderBy(p => p.Version, byVersion),
            "Source" => SortDescending ? q.OrderByDescending(p => p.Source, byText)
                                        : q.OrderBy(p => p.Source, byText),
            "Status" => SortDescending ? q.OrderByDescending(p => p.StatusText, byText)
                                        : q.OrderBy(p => p.StatusText, byText),
            "Pinned" => SortDescending ? q.OrderByDescending(p => p.IsPinned)
                                        : q.OrderBy(p => p.IsPinned),
            _ => SortDescending ? q.OrderByDescending(p => p.Name, byText)
                                 : q.OrderBy(p => p.Name, byText)
        };

        _syncing = true;                                  // 重建集合期间不要再触发回写
        try
        {
            Packages.Clear();
            foreach (var p in q) Packages.Add(p);
        }
        finally { _syncing = false; }

        if (Selected != null && !Packages.Contains(Selected)) Selected = null;

        UpdateHeaders();
        RefreshChecked();                         // v3.37 / NT-002：列表重建 → 勾选计数 / 表头三态跟上
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        NotifyActionVisibility();                 // 列表重建可能把选中项换掉 → 动作组合跟着重算
    }

    /// <summary>
    /// 详情卡动作组合的可见 / 可用性（安装 vs 升级、Pin vs 取消 Pin、卸载）都派生自 <see cref="Selected"/>，
    /// 选中变化或忙碌态变化时统一通知（⛔ 别散落各处）。
    /// </summary>
    private void NotifyActionVisibility()
    {
        OnPropertyChanged(nameof(ShowInstallAction));
        OnPropertyChanged(nameof(ShowUpgradeAction));
        OnPropertyChanged(nameof(CanUpgradeSelected));
        OnPropertyChanged(nameof(ShowPinAction));
        OnPropertyChanged(nameof(ShowUnpinAction));
        OnPropertyChanged(nameof(ShowUninstallAction));
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        IdHeader = "Id" + SortArrow("Id");
        VersionHeader = "版本" + SortArrow("Version");
        SourceHeader = "源" + SortArrow("Source");
        StatusHeader = "状态" + SortArrow("Status");
        PinnedHeader = "锁定" + SortArrow("Pinned");
    }

    /// <summary>版本比较：能解析成 <see cref="System.Version"/> 就按版本比（1.10 &gt; 1.9），否则退回字符串比较。</summary>
    private static int CompareVersion(string a, string b)
        => System.Version.TryParse(a, out var va) && System.Version.TryParse(b, out var vb)
            ? va.CompareTo(vb)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    // ==================== 右键菜单 / 详情列的动作 ====================

    [RelayCommand]
    private async Task InstallAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;

        // 🔴 装完刷新会整表重建，`p` 就成了**游离实例**（已不在 Packages 里）⇒ 详情卡要手动回写。
        //    这里记住它在 _all 里的新实例，刷新后把「已安装」抄回 p 并发通知（否则按钮仍显示「⬇ 安装」）。
        var wantedId = p.Id;
        try
        {
            IsBusy = true;
            StatusText = $"正在安装 {p.Id} ……（较大的包会久一点，请勿关闭窗口）";
            var (ok, msg) = await _pkg.InstallAsync(p.Id, string.IsNullOrEmpty(p.Source) ? SelectedSource?.Value : p.Source, SilentInstall);
            StatusText = msg;
            if (ok)
            {
                _installedIds = null;
                _installedList = null;
                await MarkInstalledAsync();
                ApplyView();
                SyncSelectedInstalledState(wantedId);
            }
        }
        finally { IsBusy = false; }
    }

    /// <summary>安装 / 卸载后用最新列表回写「详情卡当前引用的那个实例」的安装状态（见 <see cref="InstallAsync"/>）。</summary>
    private void SyncSelectedInstalledState(string id)
    {
        var fresh = _all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        var current = Selected;
        if (fresh == null || current == null) return;
        if (ReferenceEquals(fresh, current)) { NotifyActionVisibility(); return; }

        current.IsInstalled = fresh.IsInstalled;
        current.Available = fresh.Available;
        if (!string.IsNullOrWhiteSpace(fresh.Version) && !string.Equals(fresh.Version, current.Version, StringComparison.Ordinal))
            current.Version = fresh.Version;

        // ⚠️ 这些派生属性在**另一个实例**上变了 ⇒ 详情卡的渲染器订阅的是 VM（不是 PackageInfo），
        //    所以必须从 VM 侧补一次通知才能整体重解析。
        DetailChangedFromItem();
    }

    /// <summary>让详情卡整体重解析（渲染器订阅 VM 的 PropertyChanged）。</summary>
    private void DetailChangedFromItem() => OnPropertyChanged(nameof(DetailItem));

    [RelayCommand]
    private async Task UpgradeAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        if (!p.CanUpgrade)
        {
            StatusText = $"{p.Name} 已是最新版（当前 {p.Version}），无需升级。";
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = $"正在升级 {p.Id} ……";
            var (ok, msg) = await _pkg.UpgradeAsync(p.Id, SilentInstall);
            StatusText = msg;
            if (ok && _mode == ListMode.Upgrades) await LoadUpgradesAsync();
            else
            {
                if (ok) { _installedIds = null; _installedList = null; await MarkInstalledAsync(); }
                ApplyView();
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在卸载 {p.Id} ……";
            // ⚠️ 二次确认在 IPackageManagerService 内部完成（宿主把钩子接到了模态对话框上，见 App.axaml.cs）
            var (ok, msg) = await _pkg.UninstallAsync(p.Id);
            StatusText = msg;
            if (ok) { _installedIds = null; _installedList = null; await RefreshAsync(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PinAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.PinAsync(p.Id);
            StatusText = msg;
            if (ok) { await ApplyPinnedAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UnpinAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.UnpinAsync(p.Id);
            StatusText = msg;
            if (ok) { await ApplyPinnedAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    // ==================== 动词执行底座（模块内，⛔ 不改 Shared 的 IPackageManagerService） ====================

    /// <summary>
    /// 跑一条后端命令（命令型条目走 <c>cmd.exe /c</c>，⛔ 不经 pwsh ⇒ 无冷启动开销）。
    /// <para>用模块内 <see cref="ScriptRunner"/> 而非给 <see cref="IPackageManagerService"/> 加方法 —— 后者在 Shared（框架层冻结）。</para>
    /// </summary>
    private async Task<ScriptRunResult> RunBackendAsync(string command, int timeoutSeconds = ScriptRunner.MaxTimeoutSeconds)
    {
        var runner = new ScriptRunner(_app);
        return await runner.RunCommandAsync(new CommandRunRequest
        {
            Command = command,
            Shell = QuickActionShell.Cmd,
            TimeoutSeconds = Math.Min(timeoutSeconds, ScriptRunner.MaxTimeoutSeconds),
        });
    }

    /// <summary>失败原因：超时 &gt; 进程错误 &gt; stderr 末行 &gt; 退出码。</summary>
    private static string ErrorOf(ScriptRunResult r)
    {
        if (r.TimedOut) return "执行超时";
        if (!string.IsNullOrWhiteSpace(r.Error)) return r.Error!;
        if (!string.IsNullOrWhiteSpace(r.StdErr)) return r.StdErr.Trim();
        return $"退出码 {r.ExitCode}";
    }

    /// <summary>命令参数加引号（包 Id 允许含空格 / 括号 —— 见 <c>IdLike</c>）；顺手去掉双引号防拼接被破。</summary>
    private static string QuoteArg(string s) => "\"" + (s ?? "").Replace("\"", "") + "\"";

    /// <summary>
    /// 后端可执行文件的**绝对路径**。
    /// <para>🔴 不能只写 <c>winget</c>：它是 **App Execution Alias**（<c>WindowsApps\winget.exe</c> 是 0 字节重解析点），
    /// <c>UseShellExecute=false</c> 的进程创建不解析别名 ⇒ 必须自己按 PATH 找出真实路径（<c>PackageManagerService.ResolveExe</c>
    /// 就是为这个存在的，它 private ⇒ 这里在模块内重一份）。</para>
    /// </summary>
    private string? _backendExe;

    private string BackendExe
    {
        get
        {
            if (_backendExe != null) return _backendExe;

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var full = Path.Combine(dir.Trim(), BackendExeName);
                    if (File.Exists(full)) return _backendExe = full;
                }
                catch { /* 非法路径段，跳过 */ }
            }

            var alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", BackendExeName);

            return _backendExe = File.Exists(alias) ? alias : BackendExeName;   // 兜底：交给宿主解析
        }
    }

    /// <summary>把命令模板里的占位符替换掉：<c>{exe}</c> → 后端绝对路径，<c>{id}</c> → 包 Id（都加引号）。</summary>
    private string ResolveCommand(string template, string? id = null)
    {
        var s = template.Replace("{exe}", QuoteArg(BackendExe));
        return id is null ? s : s.Replace("{id}", QuoteArg(id));
    }

    /// <summary>破坏性动作的二次确认（走模块内 <see cref="QuickActionService.ConfirmHandler"/>；没接上钩子时一律 false）。</summary>
    private static async Task<bool> ConfirmAsync(string message)
        => QuickActionService.ConfirmHandler != null
           && await QuickActionService.ConfirmHandler(message).ConfigureAwait(true);

    // ==================== 新增动词：刷新索引 / 全部升级 / 卸载选中 ====================

    /// <summary>🔃 刷新索引（<see cref="CmdRefreshIndex"/>）：源元数据拉新，之后「可升级」才准。</summary>
    [RelayCommand]
    private async Task RefreshIndexAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = "正在刷新软件源索引……（首次会比较慢）";
            var r = await RunBackendAsync(ResolveCommand(CmdRefreshIndex));
            StatusText = r.Ok ? "软件源索引已刷新。" : "刷新索引失败：" + ErrorOf(r);
            if (r.Ok) await LoadSourcesAsync();      // 索引变了，源清单可能也变
        }
        finally { IsBusy = false; }
    }

    /// <summary>⬆⬆ 全部升级（<see cref="CmdUpgradeAll"/>）：**先整体确认一次**，再跑后端命令。</summary>
    [RelayCommand]
    private async Task UpgradeAllAsync()
    {
        if (IsBusy) return;
        if (!await ConfirmAsync("确定把全部可升级的包升到最新版吗？\n\n· 已锁定（暂停更新）的包会被跳过\n· 过程可能持续很久，请勿关闭窗口"))
        {
            StatusText = "已取消「全部升级」。";
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = "正在升级全部包……（可能持续很久，请勿关闭窗口）";
            var cmd = ResolveCommand(SilentInstall ? CmdUpgradeAll + ArgSilent : CmdUpgradeAll);
            var r = await RunBackendAsync(cmd);
            StatusText = r.Ok ? "全部升级已执行完成。" : "全部升级失败：" + ErrorOf(r);

            _installedIds = null;
            _installedList = null;
            await MarkInstalledAsync();
            ApplyView();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 🗑 卸载选中（<see cref="CmdUninstall"/> 逐条跑）。
    /// <para>⚠️ 刻意<b>不走</b> <see cref="IPackageManagerService.UninstallAsync"/>：那个方法内部每次都弹二次确认，
    /// 批量 N 个就要弹 N 次。这里整体确认一次，再逐条跑命令。</para>
    /// </summary>
    [RelayCommand]
    private async Task UninstallCheckedAsync()
    {
        var targets = _all.Where(p => p.IsChecked).ToList();
        if (targets.Count == 0) { StatusText = "先勾选要卸载的包（行首勾选框）。"; return; }
        if (IsBusy) return;
        if (!await ConfirmAsync($"确定卸载勾选的 {targets.Count} 个包吗？此操作不可撤销。"))
        {
            StatusText = "已取消卸载。";
            return;
        }
        try
        {
            IsBusy = true;
            var fails = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                StatusText = $"正在卸载 {i + 1}/{targets.Count}：{p.Id} ……";
                var r = await RunBackendAsync(ResolveCommand(CmdUninstall, p.Id));
                if (!r.Ok) fails.Add($"{p.Id}（{ErrorOf(r)}）");
            }

            StatusText = fails.Count == 0
                ? $"已卸载 {targets.Count} 个包。"
                : $"卸载完成：{targets.Count - fails.Count} 成功 / {fails.Count} 失败 —— " + string.Join("、", fails);

            _installedIds = null;
            _installedList = null;
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>按需拉一次 <c>winget show</c>，把长描述 / 发布者 / 主页 / 协议补进详情列。</summary>
    [RelayCommand]
    private async Task LoadDetailAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null) return;

        StatusText = "正在读取详情（winget show）……";
        var (ok, msg, detail) = await _pkg.ShowAsync(p.Id);
        if (!ok || detail == null) { StatusText = msg; return; }

        if (!string.IsNullOrWhiteSpace(detail.Name)) p.Name = detail.Name;
        // ⛔ 不覆盖 Version：winget show 的 Version 是「最新可用版」——覆盖已安装项会把「⬆ 升级到 X」错显成「已安装」
        //    （v3.19 / NT-008 修的 bug：点一下详情，状态就从"升级"变"已安装"）。只在原本为空时才补。
        if (string.IsNullOrWhiteSpace(p.Version) && !string.IsNullOrWhiteSpace(detail.Version)) p.Version = detail.Version;
        p.Publisher = detail.Publisher;
        p.Description = detail.Description;
        p.Homepage = detail.Homepage;
        p.License = detail.License;
        p.InstallLocation = detail.InstallLocation;

        // v3.19 / NT-008：PackageInfo 已是 ObservableObject → 上面的赋值直接通知详情卡 / 列表行刷新，
        // ⛔ 不用再整体重建列表（以前是普通类才需要 ApplyView，且重建会重置滚动位）。
        DetailExtra = detail.Description;
        StatusText = msg;
    }

    // ==================== v3.37 / NT-002：勾选列 + 批量安装 / 升级队列 ====================

    /// <summary>行被勾了几条（工具条「⬇ 安装选中」「⬆ 升级选中」按它操作）。</summary>
    public int CheckedCount => _all.Count(p => p.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => CheckedCount == 0 ? "未勾选" : $"已勾选 {CheckedCount} 个";

    /// <summary>批量按钮的可用态：有勾选且不在忙（Avalonia 绑定不支逻辑运算符，⛔ 别在 XAML 里写 And）。</summary>
    public bool CanRunBatch => HasChecked && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunBatch));
        OnPropertyChanged(nameof(CanUpgradeSelected));
    }

    /// <summary>
    /// 表头 ☑ 的三态（🔴 用**原生三态 CheckBox**，⛔ 别用字符"☑"画 —— 字体会渲染成深色块）：
    /// <c>true</c> = 全选、<c>false</c> = 全不选、<c>null</c> = 部分选中。
    /// </summary>
    [ObservableProperty] private bool? _allCheckedState = false;

    /// <summary>表头 ☑ 点一下：当前不是"全选"就全选，否则全不选。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var target = AllCheckedState != true;
        foreach (var p in _all) p.IsChecked = target;
        RefreshChecked();
        StatusText = target ? $"已全选 {_all.Count} 条。" : "已取消全选。";
    }

    /// <summary>表头右键：取消选择。</summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var p in _all) p.IsChecked = false;
        RefreshChecked();
        StatusText = "已取消选择。";
    }

    /// <summary>表头右键：反选。</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var p in _all) p.IsChecked = !p.IsChecked;
        RefreshChecked();
        StatusText = $"已反选，当前勾选 {CheckedCount} 条。";
    }

    /// <summary>重算勾选计数与表头三态（行的 IsChecked 一变就会被调）。</summary>
    public void RefreshChecked()
    {
        var total = _all.Count;
        var n = _all.Count(p => p.IsChecked);
        AllCheckedState = total == 0 ? false : n == 0 ? false : n == total ? true : null;

        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckedText));
        OnPropertyChanged(nameof(CanRunBatch));
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageInfo.IsChecked)) RefreshChecked();
    }

    /// <summary>
    /// 「⬇ 安装选中」：把勾选的包**逐条串行**安装（winget 自身会争用源，⛔ 不并行）。
    /// <para>单条失败不影响其余（失败隔离）；结束汇总成功 / 失败明细。已安装的包 winget 会自行跳过。</para>
    /// </summary>
    [RelayCommand]
    private async Task InstallCheckedAsync()
    {
        if (_pkg == null || IsBusy) return;

        var targets = _all.Where(p => p.IsChecked).ToList();
        if (targets.Count == 0) { StatusText = "先勾选要安装的包（行首勾选框，或表头 ☑ 全选）。"; return; }

        try
        {
            IsBusy = true;
            var ok = 0;
            var fails = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                StatusText = $"正在安装 {i + 1}/{targets.Count}：{p.Id} …（较大的包会久一点，请勿关闭窗口）";
                var (success, msg) = await _pkg.InstallAsync(p.Id,
                    string.IsNullOrEmpty(p.Source) ? SelectedSource?.Value : p.Source, SilentInstall);
                if (success) ok++;
                else fails.Add($"{p.Id}（{msg}）");
            }

            _installedIds = null;
            _installedList = null;
            await MarkInstalledAsync();
            ApplyView();
            StatusText = fails.Count == 0
                ? $"批量安装完成：{ok} 个全部成功。"
                : $"批量安装完成：成功 {ok} / 失败 {fails.Count}；失败明细 —— {string.Join("；", fails)}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 「⬆ 升级选中」：勾选的包里**有可升级版本**的逐条升级；没有的跳过并计数（⛔ 不拿没有新版本的包去试）。
    /// </summary>
    [RelayCommand]
    private async Task UpgradeCheckedAsync()
    {
        if (_pkg == null || IsBusy) return;

        var checkedAll = _all.Where(p => p.IsChecked).ToList();
        if (checkedAll.Count == 0) { StatusText = "先勾选要升级的包（行首勾选框，或表头 ☑ 全选）。"; return; }

        var targets = checkedAll.Where(p => p.HasUpgrade).ToList();
        var skipped = checkedAll.Count - targets.Count;
        if (targets.Count == 0)
        {
            StatusText = $"勾选的 {checkedAll.Count} 个都没有可升级版本（已是最新）。";
            return;
        }

        try
        {
            IsBusy = true;
            var ok = 0;
            var fails = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                StatusText = $"正在升级 {i + 1}/{targets.Count}：{p.Id} {p.Version} → {p.Available} …";
                var (success, msg) = await _pkg.UpgradeAsync(p.Id, SilentInstall);
                if (success) ok++;
                else fails.Add($"{p.Id}（{msg}）");
            }

            if (_mode == ListMode.Upgrades) await LoadUpgradesAsync();
            else { await ApplyPinnedAsync(); ApplyView(); }
            StatusText = (fails.Count == 0
                    ? $"批量升级完成：{ok} 个全部成功"
                    : $"批量升级完成：成功 {ok} / 失败 {fails.Count}；失败明细 —— {string.Join("；", fails)}")
                + (skipped > 0 ? $"；跳过 {skipped} 个无可升级版本" : "");
        }
        finally { IsBusy = false; }
    }

    // ==================== 导入 / 导出（v3.19 / NT-008：换机恢复） ====================

    /// <summary>
    /// 📤 导出当前列表为 json（建议先点「📥 已安装」再导）。
    /// <para>NT-055：原来在 <c>PackageView.axaml.cs</c>（要 TopLevel / StorageProvider），
    /// 工具条改由 Shell 渲染后搬进 VM，选择器走 <see cref="DialogHooks"/>（VM ⛔ 不引 Avalonia UI 类型）。</para>
    /// </summary>
    [RelayCommand]
    private async Task ExportPackagesAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出已安装包清单", ImportExportDefaultDir, "packages.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, ExportPackagesJson());
            StatusText = $"已导出当前列表到：{path}（新系统上用它生成安装脚本）";
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>📥 导入导出的 json → 生成 winget 批量安装脚本(.bat)（NT-055：同「📤 导出」搬进 VM）。</summary>
    [RelayCommand]
    private async Task ImportPackagesAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要恢复的包清单 json", ImportExportDefaultDir, false, "*.json");
        var src = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(src)) return;

        try
        {
            var script = BuildInstallScript(File.ReadAllText(src));
            if (string.IsNullOrEmpty(script))
            {
                StatusText = "⚠ 这个 json 里没有可安装的包（或不是本程序导出的清单）。";
                return;
            }

            var dst = await DialogHooks.SaveFileAsync("保存批量安装脚本", ImportExportDefaultDir, "install-packages.bat", "*.bat");
            if (string.IsNullOrWhiteSpace(dst)) return;

            File.WriteAllText(dst, script);
            StatusText = $"已生成批量安装脚本：{dst}（在新系统上运行它即可恢复）";
        }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

    /// <summary>📤 导出：把当前列表导成 json（换机恢复用；只留 Id / Name / Version）。建议先在「📥 已安装」后再导。</summary>
    public string ExportPackagesJson()
    {
        var list = _all.Where(p => !string.IsNullOrWhiteSpace(p.Id))
                       .Select(p => new PackageExportItem { Id = p.Id, Name = p.Name, Version = p.Version })
                       .ToList();
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>📥 导入：把导出的 json 变成 winget 批量安装脚本（.bat）文本；解析失败 / 无有效条目返回空串。</summary>
    public static string BuildInstallScript(string json)
    {
        List<PackageExportItem>? items = null;
        try { items = JsonSerializer.Deserialize<List<PackageExportItem>>(json); }
        catch { return ""; }
        if (items == null || items.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("rem NewToys 生成的 winget 批量安装脚本（换机恢复；建议以管理员运行）");
        var n = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Id)) continue;
            sb.AppendLine($"winget install --id {it.Id} --exact --accept-package-agreements --accept-source-agreements --disable-interactivity");
            n++;
        }
        if (n == 0) return "";
        sb.AppendLine("echo Done.");
        return sb.ToString();
    }

    [RelayCommand]
    private async Task CopyDetailAsync()
    {
        var p = Selected;
        if (p == null) return;

        // 与详情卡字段**一一对应**（加了字段记得同步这里，⛔ 不要漏成"复制少了"）。
        var sb = new StringBuilder();
        Append("名称", p.Name);
        Append("Id", p.Id);
        Append("版本", p.Version);
        Append("可升级到", p.Available);
        Append("状态", p.StatusText);
        Append("源", p.Source);
        Append("发布者", p.Publisher);
        Append("协议 / 许可证", p.License);
        Append("安装位置", p.InstallLocation);
        Append("主页", p.Homepage);
        Append("描述", p.Description);

        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(sb.ToString().TrimEnd());
            StatusText = $"已复制「{p.Name}」的详情信息。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }

        void Append(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.Append(label).Append(" = ").AppendLine(value.Trim());
        }
    }

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        var p = Selected;
        if (p == null) return;
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(p.Id);
            StatusText = $"已复制 Id：{p.Id}";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        var url = Selected?.Homepage;
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText = "这个包没有主页信息；可先点「🔎 读取详情」。";
            return;
        }
        LaunchRunner.OpenUrlDefault(url);
        StatusText = "已用默认浏览器打开：" + url;
    }

    /// <summary>「更多功能」里的一项：在 winget.run 上查这个包（网页版 winget 索引）。</summary>
    [RelayCommand]
    private void OpenInWingetRun()
    {
        var id = Selected?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        LaunchRunner.OpenUrlDefault("https://winget.run/pkg/" + id.Replace('.', '/'));
        StatusText = "已在浏览器打开 winget.run 的包页面。";
    }

    // ==================== 即时写盘（无保存按钮，规范 3.7） ====================

    /// <summary>v3.19 / NT-008：点「源」按钮切换当前源（原下拉框改按钮）。</summary>
    [RelayCommand]
    private void SelectSource(PackageSourceOption? opt)
    {
        if (opt != null) SelectedSource = opt;
    }

    /// <summary>v3.19 / NT-008：「仅看已安装」从工具条勾选并入表头右键菜单（切换过滤）。</summary>
    [RelayCommand]
    private void ToggleInstalledOnly()
    {
        InstalledOnly = !InstalledOnly;
        StatusText = InstalledOnly ? "仅看已安装（搜索结果里只留本机已装的）。" : "已取消「仅看已安装」。";
    }

    partial void OnSelectedSourceChanged(PackageSourceOption? value)
    {
        foreach (var o in SourceOptions) o.IsSelected = ReferenceEquals(o, value);   // v3.19 / NT-008：源按钮高亮
        if (_syncing || _settings == null || value == null) return;
        _settings.Settings.DefaultSource = value.Value ?? "";
        _settings.Save();
        StatusText = value.Value == null
            ? "已切到「全部源（已启用的）」，下次联网搜索会逐个源查一遍。"
            : $"已切到源：{value.Value}";
    }

    partial void OnSilentInstallChanged(bool value)
    {
        if (_syncing || _settings == null) return;
        _settings.Settings.SilentInstall = value;
        _settings.Save();
        StatusText = value ? "安装 / 升级将使用 --silent（静默）。" : "安装 / 升级会保留安装器界面。";
    }

    // 注：源的下拉只是**只读快照**（进入本页读一次系统 winget 的源）。
    // 要增删源请去「设置 → 软件源」页 —— 那里改的也是系统 winget，两边共用一套数据（v1.33）。
}
