using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shell.Models;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 应用级设置页的「运行环境」节（原「设置 → 路径与配置」，v3.53 / NT-056 起是**设置总表**）：
/// 一张列表按**分类列**承载两类行 —— `路径`（`app.json` 的运行环境路径，可编辑）
/// 与 `配置`（`profiles\` 下多套独立 config/data/logs）。
/// <para>
/// 🔴 分类决定"哪些工具可用"：路径专属工具在选中配置行时**置灰并给出原因提示**，反之亦然；
/// 与分类无关的工具（📂 打开配置目录 / ＋ 新建配置）恒可用。
/// </para>
/// <para>
/// 工具条由 Shell 声明式承载（NT-055 / 需求 §4.8.5），本页只输出描述符。
/// 🔴 v3.66 / NT-062 细节调整：**本页详情卡已删除**（⛔ 不再实现 <c>IDetailPanelProvider</c> /
/// <c>IDetailCardSource</c>）—— 设置页是**一页多节**（节 = 竖向折叠块），右详情列在同页里无处安放；
/// 全应用口径 = 设置页不分左右栏。列表本体、右键菜单、工具条（含「💾 保存」）全部保留。
/// </para>
/// <para>
/// 本页**保留「💾 保存」**（规范 3.5 的分界）：路径是自由文本的复杂输入，边打边落盘没有意义，
/// 也可能把半截路径写进配置。例外是「📂 浏览…」选出来的路径 —— 那是明确的动作，**选完立即生效**。
/// </para>
/// <para>
/// ⚠️ v1.34 曾去掉"视频库默认目录"；v3.15 重新引入为**「导入导出默认目录」**（全局，覆盖视频库 .db 与
/// 批量任务 .json 的导入/导出对话框定位），不回写扫描目录，不影响"每次手动选目录"的扫描逻辑。
/// </para>
/// </summary>
public partial class PathViewModel : ListPageViewModelBase, IStatusActionProvider
{
    private readonly IAppConfigService? _cfg;
    private readonly ConfigStoreService? _store;
    private readonly IProfileService? _profiles;

    /// <summary>外部程序注册表（v3.65 / NT-062）：进本页时才做**惰性探测**，缺失 ⇒ 状态栏 `⚠` 可点击。</summary>
    private readonly IExternalToolRegistry? _ext;

    /// <summary>外部程序缺失的警示文案（null = 没问题）；它同时决定状态栏那一行**是否可点击**。</summary>
    private string? _externalWarn;

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _cfg?.VideoDbFolder ?? "";

    private bool _syncing;

    public ObservableCollection<PathSettingItem> Items { get; } = new();

    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private PathSettingItem? _selected;

    public bool IsNotBusy => !IsBusy;

    // ==================== 表头 ====================

    [ObservableProperty] private string _categoryHeader = "分类 ▲";
    [ObservableProperty] private string _nameHeader = "名称";
    [ObservableProperty] private string _valueHeader = "当前值";
    [ObservableProperty] private string _kindHeader = "类型";
    [ObservableProperty] private string _descriptionHeader = "说明";

    // ==================== 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ====================
    //
    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里，落 config\columnlayout.json）。

    public bool IsCategoryVisible => Vis("Category");
    public bool IsNameVisible => Vis("Name");
    public bool IsValueVisible => Vis("Value");
    public bool IsKindVisible => Vis("Kind");
    public bool IsDescriptionVisible => Vis("Description");
    public bool IsActionVisible => Vis("Action");

    protected override string PageKey => "settings-path";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.22 §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）；「操作」是按钮列 → 不给
        new("Category", "分类", "1*", sample: "路径"),
        new("Name", "名称", "2.2*", sample: "ffmpeg 路径"),
        new("Value", "当前值", "4*", sample: @"C:\ffmpeg\bin\ffmpeg.exe"),
        new("Kind", "类型", "1*", sample: "文件"),
        new("Description", "说明", "3*", sortable: false, sample: "视频的媒体信息用它旁边的 ffprobe 读取"),
        new("Action", "操作", "3.8*", sortable: false),
    };

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;
    // ==================== v3.53 / NT-056：工具可用性（按选中行的分类） ====================

    /// <summary>当前选中的是路径行。</summary>
    public bool IsPathRowSelected => Selected?.IsPathRow == true;

    /// <summary>当前选中的是配置行。</summary>
    public bool IsProfileRowSelected => Selected?.IsProfileRow == true;

    /// <summary>选中的配置行就是**本窗口正在使用**的那个。</summary>
    private bool IsActiveProfileRow => Selected?.IsProfileRow == true && Selected.IsActiveProfile;

    /// <summary>选中的是内置 `default` 档（⛔ 不可重置）。</summary>
    private bool IsDefaultProfileRow
        => Selected?.IsProfileRow == true && string.Equals(Selected.Name, "default", StringComparison.OrdinalIgnoreCase);

    /// <summary>「💾 保存 / 📂 浏览 / ↩ 还原默认」可用（路径行且非只读展示项）。</summary>
    public bool CanSavePath => IsPathRowSelected && Selected!.IsEditable;

    /// <summary>「⇄ 切换 / 🚀 新开」可用（配置行且不是当前配置 —— 同一配置只允许一个窗口）。</summary>
    public bool CanSwitchProfile => IsProfileRowSelected && !IsActiveProfileRow;

    /// <summary>「🗑 删除」可用（配置行且不是当前配置）。</summary>
    public bool CanDeleteProfile => IsProfileRowSelected && !IsActiveProfileRow;

    /// <summary>「♻ 重置」可用（配置行、不是当前配置、也不是内置 default）。</summary>
    public bool CanResetProfile => IsProfileRowSelected && !IsActiveProfileRow && !IsDefaultProfileRow;

    // —— 工具条悬停提示：可用时说明语义，置灰时说明**为什么**不可用（需求 §4.9.1 第 2 条） ——

    private string PathReason => IsPathRowSelected
        ? "只读展示项不能改（数据目录 / 配置目录由应用决定）"
        : HasSelection ? "该操作只对「路径」行可用" : "先在列表里选中一行";

    private string ProfileReason => IsProfileRowSelected
        ? ""
        : HasSelection ? "该操作只对「配置」行可用" : "先在列表里选中一行";

    public string SaveTip => CanSavePath
        ? "把「当前值」写回 app.json（路径是自由文本，不适合边打边存）"
        : PathReason;

    public string BrowseTip => CanSavePath
        ? "选文件 / 选目录（按行类型决定），选完立即生效并落盘"
        : PathReason;

    public string OpenTip => IsPathRowSelected
        ? "在资源管理器里打开这个路径"
        : PathReason;

    public string RestoreTip => CanSavePath
        ? "恢复成该路径的默认值（只填进输入框，仍需点「💾 保存」）"
        : PathReason;

    public string SwitchTip => CanSwitchProfile
        ? "本窗口重启到该配置（不影响其它窗口和全局默认）"
        : IsProfileRowSelected ? "当前窗口已经在用这个配置了" : ProfileReason;

    public string LaunchTip => CanSwitchProfile
        ? "以该配置再起一个窗口（多实例；同一配置只能开一个）"
        : IsProfileRowSelected ? "当前窗口已经在用这个配置了 —— 同一配置同时只允许一个窗口" : ProfileReason;

    public string DefaultTip => IsProfileRowSelected
        ? "写全局默认：以后直接启动 NewToys（不带参数）就是它"
        : ProfileReason;

    public string RenameTip => IsProfileRowSelected
        ? "重命名这个配置（目录一起改名）"
        : ProfileReason;

    public string DeleteTip => CanDeleteProfile
        ? "永久删除这个配置的全部数据（会二次确认）"
        : IsProfileRowSelected ? "不能删除当前正在使用的配置 —— 先切到别的配置" : ProfileReason;

    public string ResetTip => CanResetProfile
        ? "清空回初始态（debug 复刻当前 default 的配置；会二次确认）"
        : IsProfileRowSelected ? "不能重置 default 或当前正在使用的配置" : ProfileReason;

    /// <summary>选中行变化 / 列表重建后，把上面这批"可用性 + 提示词"整体通知一遍（工具条按钮绑的就是它们）。</summary>
    private void RaiseActionStates()
    {
        OnPropertyChanged(nameof(IsPathRowSelected));
        OnPropertyChanged(nameof(IsProfileRowSelected));
        OnPropertyChanged(nameof(CanSavePath));
        OnPropertyChanged(nameof(CanSwitchProfile));
        OnPropertyChanged(nameof(CanDeleteProfile));
        OnPropertyChanged(nameof(CanResetProfile));

        OnPropertyChanged(nameof(SaveTip));
        OnPropertyChanged(nameof(BrowseTip));
        OnPropertyChanged(nameof(OpenTip));
        OnPropertyChanged(nameof(RestoreTip));
        OnPropertyChanged(nameof(SwitchTip));
        OnPropertyChanged(nameof(LaunchTip));
        OnPropertyChanged(nameof(DefaultTip));
        OnPropertyChanged(nameof(RenameTip));
        OnPropertyChanged(nameof(DeleteTip));
        OnPropertyChanged(nameof(ResetTip));
    }

    // ==================== 工具条（v3.66 第五轮起由**设置页「运行环境」节**的节点内嵌 ToolbarHost 渲染） ====================

    /// <summary>
    /// 工具条描述符（按优先级顺序）：前四个是**路径**侧工具，中间是共用的目录 / 新建，后面是**配置**侧工具。
    /// 🔴 启停全部落在 <c>IsEnabledPath</c> 上（选中另一分类 ⇒ 置灰 + <c>ToolTipPath</c> 说明原因）。
    /// <para>⚠️ 渲染位置 = 宿主节点（节点抬头下方、正文之上），⛔ 不再上抛到 Shell 的页面顶部工具条行。</para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📂 打开配置目录", CommandPath = nameof(OpenConfigDirCommand),
                ToolTip = "打开本配置档的 config 目录（launch.json / theme.json / 模块设置等；可整体备份）" },
        new() { Text = "💾 保存", Classes = "ghost", CommandPath = nameof(SaveCommand),
                IsEnabledPath = nameof(CanSavePath), ToolTipPath = nameof(SaveTip), ShowToolTipOnDisabled = true },
        new() { Text = "📂 浏览…", CommandPath = nameof(PickPathCommand),
                IsEnabledPath = nameof(CanSavePath), ToolTipPath = nameof(BrowseTip), ShowToolTipOnDisabled = true },
        new() { Text = "↗ 打开位置", CommandPath = nameof(OpenCommand),
                IsEnabledPath = nameof(IsPathRowSelected), ToolTipPath = nameof(OpenTip), ShowToolTipOnDisabled = true },
        new() { Text = "↩ 还原默认", CommandPath = nameof(RestoreDefaultCommand),
                IsEnabledPath = nameof(CanSavePath), ToolTipPath = nameof(RestoreTip), ShowToolTipOnDisabled = true },

        new() { Kind = ToolbarItemKind.Separator },

        new() { Text = "＋ 新建配置", CommandPath = nameof(NewProfileCommand),
                ToolTip = "新建一套独立的 config / data / logs（切换过去后从空白开始）" },
        new() { Text = "⇄ 切换", CommandPath = nameof(SwitchProfileCommand),
                IsEnabledPath = nameof(CanSwitchProfile), ToolTipPath = nameof(SwitchTip), ShowToolTipOnDisabled = true },
        new() { Text = "🚀 新开", CommandPath = nameof(LaunchProfileNewCommand),
                IsEnabledPath = nameof(CanSwitchProfile), ToolTipPath = nameof(LaunchTip), ShowToolTipOnDisabled = true },
        new() { Text = "⭐ 设为默认", CommandPath = nameof(SetProfileDefaultCommand),
                IsEnabledPath = nameof(IsProfileRowSelected), ToolTipPath = nameof(DefaultTip), ShowToolTipOnDisabled = true },
        new() { Text = "✏ 重命名", CommandPath = nameof(RenameProfileCommand),
                IsEnabledPath = nameof(IsProfileRowSelected), ToolTipPath = nameof(RenameTip), ShowToolTipOnDisabled = true },
        new() { Text = "🗑 删除", Classes = "danger-btn", CommandPath = nameof(DeleteProfileCommand),
                IsEnabledPath = nameof(CanDeleteProfile), ToolTipPath = nameof(DeleteTip), ShowToolTipOnDisabled = true },
        new() { Text = "♻ 重置", CommandPath = nameof(ResetProfileCommand),
                IsEnabledPath = nameof(CanResetProfile), ToolTipPath = nameof(ResetTip), ShowToolTipOnDisabled = true },
        // v3.60：原 Label「「路径」行改默认路径；「配置」行管多套数据」删除（工具条⛔ 不放说明文字）——
        //        表头「分类」列已经把两类行分开，各行自带说明（StringFormat 那条坑见工程规范）。
    };

    // ==================== 编辑缓冲 ====================

    /// <summary>「当前值」编辑缓冲里正在编辑的值（点「💾 保存」才写回对象并落盘）。</summary>
    [ObservableProperty] private string _editValue = "";

    partial void OnSelectedChanged(PathSettingItem? value)
    {
        _syncing = true;
        // 只有路径行才进「当前值」编辑缓冲（配置行只读展示）
        EditValue = value is { IsPathRow: true } ? value.Value : "";        _syncing = false;
        OnPropertyChanged(nameof(HasSelection));

        RaiseActionStates();   // v3.53 / NT-056：工具条按分类启停，靠这批通知刷新
    }

    partial void OnEditValueChanged(string value)
    {
        // 绑定到 TextBox 时不要反过来打扰输入；这里只是把"是否改过"反映到提示上
        if (!_syncing) StatusText = "改完点「💾 保存」写入 app.json。";
    }

    // ==================== 构造 ====================

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PathViewModel() { }

    public PathViewModel(IAppConfigService cfg, ConfigStoreService store, IColumnLayoutService cols, IProfileService profiles,
                         IExternalToolRegistry? ext = null)
        : base(cols)
    {
        _cfg = cfg;
        _store = store;
        _profiles = profiles;
        _ext = ext;

        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        SetSort("Category", false);       // v3.53：默认按「分类」升序（同类聚在一起），组内再按名称
        BuildItems();
        RebuildQuickFilters();            // v3.21 §4.8.2：分类 / 类型进表头右键「筛选」
        ApplyView();
        SelectActiveProfile();            // v3.53：默认选中「正在使用」的配置行（一眼看到我在哪）
    }

    // ==================== 外部程序「惰性探测」 + 状态栏 ⚠ 可跳转（v3.65 / NT-062） ====================

    /// <summary>
    /// 惰性探测外部程序（需求 §4.9.2 第 5 条）：**进入「运行环境」节时**由设置页触发 ——
    /// ⛔ 不在启动时全量探测（拖启动）。缺硬依赖（ffmpeg）或"配了但文件不在" ⇒ 底栏 `⚠`，
    /// 且这一行**可点击**（<see cref="StatusActionKey"/> 指回本节）方便就地改路径。
    /// </summary>
    public void ProbeExternalTools()
    {
        if (_ext == null) return;

        var all = _ext.ProbeAll();
        var attention = all.Where(s => s.NeedsAttention).ToList();

        // ⚠️ 先定好"可点击目标"，再改 StatusText（StatusChanged 触发时 Shell 会立刻读它）
        _externalWarn = attention.Count == 0
            ? null
            : $"⚠ 未找到 {string.Join(" / ", attention.Select(s => s.WarnText))}（点这里到「运行环境」填路径）";

        OnPropertyChanged(nameof(StatusActionKey));
        OnPropertyChanged(nameof(StatusActionTip));

        StatusText = _externalWarn ?? $"运行环境已探测：{all.Count} 个外部程序均可解析（留空 = 用系统默认）。";
    }

    /// <summary>状态栏 `⚠` 的跳转目标 = 本页（「运行环境」节）；没问题时为 null（不可点）。</summary>
    public string? StatusActionKey => _externalWarn == null ? null : "settings-path";

    public string StatusActionTip => _externalWarn == null
        ? ""
        : "打开「运行环境」节，填好缺失的外部程序路径（留空 = 用系统默认）";

    /// <summary>筛选快捷项：**分类**（路径 / 配置）+ **类型**（文件 / 目录 / 只读 / 配置档）。</summary>
    private void RebuildQuickFilters()
    {
        var items = new List<(string Label, string ColumnKey, string Value)>();

        items.AddRange(Items.Select(i => i.CategoryText)
                            .Where(c => c.Length > 0)
                            .Distinct(StringComparer.CurrentCultureIgnoreCase)
                            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                            .Select(c => ("分类：" + c, "Category", c)));

        items.AddRange(Items.Select(i => i.KindText)
                            .Where(k => k.Length > 0)
                            .Distinct(StringComparer.CurrentCultureIgnoreCase)
                            .OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)
                            .Select(k => ("类型：" + k, "Kind", k)));

        SetQuickFilters(items);
    }

    // ==================== 行数据 ====================

    /// <summary>重建整张表：**路径行**（app.json）+ **配置行**（profiles\）。</summary>
    private void BuildItems()
    {
        Items.Clear();
        BuildPathRows();
        BuildProfileRows();
    }

    private void BuildPathRows()
    {
        if (_cfg == null) return;

        // v3.64 / NT-061：下面两者是**跨模块共用的全局项** ⇒ 保持应用级、⛔ 不下沉。
        //   ffmpeg：媒体资产（缩略图抽帧）与视频工具都用同一份；外部播放器：视频列表「▶ 播放」用。
        Items.Add(new PathSettingItem
        {
            Key = "FfmpegPath", Name = "ffmpeg 路径", Kind = PathItemKind.File, DefaultValue = "ffmpeg",
            Description = "视频的媒体信息（时长 / 分辨率 / 编码 / 码率）用它旁边的 ffprobe 读取；留空或写 ffmpeg 则走 PATH",
            Value = _cfg.FfmpegPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PlayerPath", Name = "外部播放器路径", Kind = PathItemKind.File,
            Description = "视频「▶ 播放」用它打开；留空 = 系统默认关联程序",
            Value = _cfg.PlayerPath
        });
        // v3.65 / NT-062：三个解释器路径**回迁应用级**（判据 = 语义：「怎么跑外部程序」= 运行环境 ⇒ 应用级路由，
        // ⛔ 不随配置档切换）。它们曾于 NT-061 下沉到「脚本管理器设置」，现回到本页（唯一写入点），落 app.json。
        // ⚠️ 脚本目录仍归「脚本管理器设置」（模块级，config\script-manager.json）。
        Items.Add(new PathSettingItem
        {
            Key = "PythonPath", Name = "Python 解释器", Kind = PathItemKind.File,
            Description = "「设置与脚本」里 .py 脚本的解释器；留空 = 用 PATH 里的 python",
            Value = _cfg.PythonPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PwshPath", Name = "PowerShell 7+（pwsh）", Kind = PathItemKind.File,
            Description = ".ps1 优先用它；留空 = 优先探常见安装位、再退回 PATH 里的 pwsh",
            Value = _cfg.PwshPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PowerShell51Path", Name = "Windows PowerShell 5.1", Kind = PathItemKind.File,
            Description = "没配 pwsh 时 .ps1 用它；留空 = 用系统自带的 powershell.exe",
            Value = _cfg.PowerShell51Path
        });
        Items.Add(new PathSettingItem
        {
            Key = "VideoDbFolder", Name = "导入导出默认目录", Kind = PathItemKind.Folder,
            Description = "批量任务 .json（及媒体资产库文件）的导入/导出对话框打开时直接定位到这里；留空 = 用系统默认",
            Value = _cfg.VideoDbFolder
        });
        Items.Add(new PathSettingItem
        {
            Key = "DataDir", Name = "数据目录", Kind = PathItemKind.ReadOnly,
            Description = "本地数据库 newtoys.db（视频媒体信息缓存）等数据放这里；由应用决定，可用环境变量 NEWTOYS_HOME 整根切换",
            Value = _cfg.DataDirectory
        });
        // v3.65 / NT-062：app.json 已上提到**应用根**（与 profiles\ 同级）—— 它是应用级「运行环境」，
        // ⛔ 不再随配置档切换，也不再位于 <配置根>\config。这里如实展示新位置（只读）。
        Items.Add(new PathSettingItem
        {
            Key = "AppConfigFile", Name = "app.json（应用级配置）", Kind = PathItemKind.ReadOnly,
            Description = "本页这些行的落盘文件；v3.65 起放在应用根（profiles 的同级），多配置共享同一份",
            Value = _cfg.ConfigFilePath
        });
        Items.Add(new PathSettingItem
        {
            Key = "ConfigDir", Name = "配置目录", Kind = PathItemKind.ReadOnly,
            Description = "本配置档的 config 目录：launch.json / theme.json / script-manager.json / pc-quick.json / pc-scripts.json / columnlayout.json 等都在这里，可整体备份",
            Value = _store?.ConfigDir ?? ""
        });
    }

    /// <summary>
    /// 配置行（v3.53 / NT-056 由「设置 → 配置」页并入）：`Kind` 借只读展示位（它的「当前值」= 数据目录，
    /// 只读展示），⛔ 不新增行类型。
    /// </summary>
    private void BuildProfileRows()
    {
        if (_profiles == null) return;

        var active = _profiles.ActiveProfile;
        foreach (var name in _profiles.Profiles)
        {
            var isActive = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
            Items.Add(new PathSettingItem
            {
                Key = "profile:" + name,
                Category = SettingCategory.Profile,
                Kind = PathItemKind.ReadOnly,
                Name = name,
                Value = _profiles.GetDirectory(name),
                IsActiveProfile = isActive,
                Description = isActive
                    ? "当前窗口正在使用的配置（切换 = 重启本窗口；⭐ 设为默认 = 以后不带参数启动用它）"
                    : "一套独立的 config / data / logs，可切换 / 新开窗口 / 设为默认 / 重命名 / 删除 / 重置"
            });
        }
    }

    /// <summary>默认选中「正在使用」的配置行（与并入前的「设置 → 配置」页一致）；没有就选第一行。</summary>
    private void SelectActiveProfile()
    {
        Selected = Items.FirstOrDefault(i => i.IsProfileRow && i.IsActiveProfile)
            ?? (Items.Count > 0 ? Items[0] : null);
    }

    /// <summary>配置侧动作（新建 / 重命名 / 删除 / 重置 / 切换 / 设为默认）之后重建配置行并保持"选中当前配置"。</summary>
    private void RefreshProfiles()
    {
        BuildItems();
        RebuildQuickFilters();
        ApplyView();
        SelectActiveProfile();
        OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    /// <summary>筛选取值（多项筛选用）。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Category" => ((PathSettingItem)row).CategoryText,
        "Name" => ((PathSettingItem)row).Name,
        "Value" => ((PathSettingItem)row).ValueDisplay,
        "Kind" => ((PathSettingItem)row).KindText,
        "Description" => ((PathSettingItem)row).Description,
        _ => null
    };

    private void ApplyView()
    {
        IEnumerable<PathSettingItem> q = Items;

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            // v3.53：分类是"分组列"—— 同分类聚在一起，组内再按名称（否则同类行会被名称打散）
            "Category" => SortDescending
                ? q.OrderByDescending(i => i.CategoryText, byText).ThenByDescending(i => i.Name, byText)
                : q.OrderBy(i => i.CategoryText, byText).ThenBy(i => i.Name, byText),
            "Value" => SortDescending ? q.OrderByDescending(i => i.Value, byText) : q.OrderBy(i => i.Value, byText),
            "Kind" => SortDescending ? q.OrderByDescending(i => i.KindText, byText) : q.OrderBy(i => i.KindText, byText),
            _ => SortDescending ? q.OrderByDescending(i => i.Name, byText) : q.OrderBy(i => i.Name, byText)
        };

        var ordered = q.ToList();
        Items.Clear();
        foreach (var i in ordered) Items.Add(i);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        CategoryHeader = "分类" + SortArrow("Category");
        NameHeader = "名称" + SortArrow("Name");
        ValueHeader = "当前值" + SortArrow("Value");
        KindHeader = "类型" + SortArrow("Kind");
    }

    // ==================== 路径侧增删改查 ====================

    /// <summary>💾 保存：把当前选中项的值写回 <c>app.json</c>。</summary>
    [RelayCommand]
    private void Save()
    {
        var item = Selected;
        if (item == null || _cfg == null) { StatusText = "先在左侧选中一项。"; return; }

        if (item.IsReadOnly) { StatusText = $"「{item.Name}」是只读项，不能改。"; return; }

        item.Value = (EditValue ?? "").Trim();
        WriteBack();
        StatusText = $"已保存「{item.Name}」→ {item.ValueDisplay}（下一轮启动 / 调用即生效）";
    }

    /// <summary>↩ 还原默认：只**预填**默认值，还要点「💾 保存」才生效（规范 3.5）。</summary>
    [RelayCommand]
    private void RestoreDefault()
    {
        var item = Selected;
        if (item == null) { StatusText = "先在左侧选中一项。"; return; }
        if (item.IsReadOnly) { StatusText = "只读项没有默认值可还原。"; return; }

        EditValue = item.DefaultValue;
        StatusText = item.DefaultValue.Length > 0
            ? $"已预填默认值「{item.DefaultValue}」，点「💾 保存」生效。"
            : "默认值就是留空（用系统默认），点「💾 保存」生效。";
    }

    /// <summary>工具条 / 右键菜单的「📂 浏览…」：作用于当前选中项。</summary>
    [RelayCommand]
    private async Task PickPathAsync() => await BrowseAsync(Selected);

    /// <summary>行内「📂 浏览」：作用于这一行（⛔ 不必先选中）。</summary>
    [RelayCommand]
    private async Task PickRowPathAsync(PathSettingItem? row) => await BrowseAsync(row ?? Selected);

    /// <summary>
    /// 选文件 / 选目录（按行的 <see cref="PathItemKind"/> 决定用哪个选择器），选完**立即生效**并落盘。
    /// <para>v3.53：改走 <c>DialogHooks</c>（宿主注入）—— VM ⛔ 不引 Avalonia，页内 code-behind 不再需要接线。</para>
    /// </summary>
    private async Task BrowseAsync(PathSettingItem? item)
    {
        if (item == null) { StatusText = "先在左侧选中一项。"; return; }
        if (item.IsReadOnly) { StatusText = $"「{item.Name}」是只读展示项，不能改。"; return; }

        try
        {
            if (item.Kind == PathItemKind.Folder)
            {
                var dir = await DialogHooks.PickFolderAsync($"选择{item.Name}", ImportExportDefaultDir);
                if (!string.IsNullOrWhiteSpace(dir)) ApplyPickedValue(item, dir!);
                return;
            }

            var files = await DialogHooks.PickFilesAsync($"选择{item.Name}", ImportExportDefaultDir, false, "*");
            var path = files.Count > 0 ? files[0] : null;
            if (!string.IsNullOrWhiteSpace(path)) ApplyPickedValue(item, path!);
        }
        catch (Exception ex)
        {
            StatusText = "选择路径失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 「📂 浏览…」选完之后调用：选路径是**明确的动作** → 立即生效并落盘
    /// （不用再点保存；这也避免了"选了却没生效"的困惑）。
    /// </summary>
    public void ApplyPickedValue(PathSettingItem item, string path)
    {
        if (item == null || item.IsReadOnly || string.IsNullOrWhiteSpace(path)) return;

        item.Value = path.Trim();
        if (ReferenceEquals(item, Selected)) EditValue = item.Value;
        WriteBack();
        StatusText = $"已设置「{item.Name}」→ {item.Value}";
    }

    /// <summary>↗ 打开：目录项打开目录；文件项在资源管理器里定位（文件不存在就退到它的上级目录）。
    /// 不传参就用当前选中项（行内按钮 / 详情卡按钮都可用）。</summary>
    [RelayCommand]
    private void Open(PathSettingItem? item)
    {
        var target = item ?? Selected;
        if (target == null) { StatusText = "先在左侧选中一项。"; return; }

        var value = (target.Value.Length > 0 ? target.Value : target.DefaultValue).Trim();
        if (value.Length == 0) { StatusText = "这一项还是空的，没有可打开的位置。"; return; }

        try
        {
            if (target.Kind == PathItemKind.Folder || Directory.Exists(value))
            {
                Directory.CreateDirectory(value);
                Process.Start(new ProcessStartInfo("explorer.exe", value) { UseShellExecute = true });
            }
            else if (File.Exists(value))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{value}\"") { UseShellExecute = true });
            }
            else
            {
                var dir = Path.GetDirectoryName(value) ?? "";
                if (dir.Length == 0 || !Directory.Exists(dir)) { StatusText = $"找不到这个位置：{value}"; return; }
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            StatusText = "已打开：" + value;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = _store?.ConfigDir ?? Path.Combine(AppContext.BaseDirectory, "config");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { StatusText = "无法打开配置目录。"; }
    }

    /// <summary>把列表里每个可编辑项的值写回 <see cref="IAppConfigService"/>（每个 setter 各自落盘，这里收口存储根）。</summary>
    private void WriteBack()
    {
        if (_cfg == null) return;
        foreach (var it in Items)
        {
            var v = it.Value.Trim();
            switch (it.Key)
            {
                case "FfmpegPath": _cfg.FfmpegPath = v; break;
                case "PlayerPath": _cfg.PlayerPath = v; break;
                // v3.65 / NT-062：三个解释器路径回迁应用级（app.json）—— 本页是它们的唯一写入点。
                case "PythonPath": _cfg.PythonPath = v; break;
                case "PwshPath": _cfg.PwshPath = v; break;
                case "PowerShell51Path": _cfg.PowerShell51Path = v; break;
                case "VideoDbFolder": _cfg.VideoDbFolder = v; break;
                // ⚠️ 脚本目录（ScriptFolder）仍归「设置 → 脚本管理器设置」（config\script-manager.json），⛔ 本页不写它。
                // DataDir / ConfigDir / AppConfigFile 是只读展示项，不写；配置行（Key = "profile:<名>"）不属于 app.json
            }
        }
        _cfg.Save();
    }

    // ==================== 配置侧动作（v3.53 / NT-056：由「设置 → 配置」页原样并入） ====================
    //
    // 🔴 语义**零改动**：⇄ 切换仍二次确认 + 本窗重启；🗑 删除仍禁删当前配置并二次确认；♻ 重置仍禁 default / 当前配置。
    // 每个命令都收一个行参数（行内按钮传行、工具条与详情卡不传 ⇒ 用当前选中项）。

    /// <summary>把行参数归一成"当前要操作的配置行"（不传参就用选中项）。</summary>
    private PathSettingItem? Resolve(PathSettingItem? row) => row ?? Selected;

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (_profiles == null) return;

        var name = await DialogHooks.InputAsync("新建配置（名称）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        try
        {
            _profiles.Create(name);
            StatusText = $"已新建配置「{name}」—— 切换过去后从空白开始。";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "新建失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task RenameProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var oldName = target.Name;
        var name = await DialogHooks.InputAsync($"重命名配置：{oldName}", oldName);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (string.Equals(name, oldName, StringComparison.Ordinal)) return;

        try
        {
            _profiles.Rename(oldName, name);
            StatusText = $"已重命名：{oldName} → {name}";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "重命名失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        if (string.Equals(name, _profiles.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "不能删除当前正在使用的配置 —— 先切到别的配置再删。";
            return;
        }

        var dir = _profiles.GetDirectory(name);
        var ok = await DialogHooks.ConfirmAsync(
            $"确定要删除配置「{name}」吗？\n\n" +
            "这会永久删除它的全部设置与功能数据（config / data / logs）：\n" +
            $"{dir}\n\n" +
            "此操作不可撤销。");
        if (!ok) return;

        try
        {
            _profiles.Delete(name);
            StatusText = $"已删除配置：{name}";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "删除失败：" + ex.Message; }
    }

    /// <summary>把选中配置**重置为初始态**（NT-038）：debug = 重新复制 default 的 config；release = 清空。⛔ 不能重置 default 或当前配置。</summary>
    [RelayCommand]
    private async Task ResetProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        var ok = await DialogHooks.ConfirmAsync(
            $"把配置「{name}」重置为初始态？\n\n" +
            "会清空它的 config / data / logs（debug 会重新复制当前 default 的配置）。\n" +
            "此操作不可撤销；不能重置 default 或当前正在使用的配置。");
        if (!ok) return;

        try
        {
            _profiles.ResetToInitial(name);
            StatusText = $"已重置配置：{name}";
            RefreshProfiles();
        }
        catch (Exception ex) { StatusText = "重置失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task SwitchProfileAsync(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        if (string.Equals(name, _profiles.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"当前已经在「{name}」上了。";
            return;
        }

        var dir = _profiles.GetDirectory(name);
        var ok = await DialogHooks.ConfirmAsync(
            $"切换到配置「{name}」？\n\n" +
            "本窗口会自动重启，之后所有设置与功能数据都来自：\n" +
            $"{dir}\n\n" +
            "（只影响本窗口——其它已打开的窗口和全局默认配置都不变）");
        if (!ok) return;

        try
        {
            _profiles.SwitchTo(name);
            _profiles.RestartApp(name);   // 起新实例（带 --profile）+ 退出自己；新实例起不来时这里会返回（不退出）
            StatusText = $"已切到「{name}」，但自动重启失败 —— 请手动重启 NewToys 使配置生效。";
        }
        catch (Exception ex) { StatusText = "切换失败：" + ex.Message; }
    }

    /// <summary>把选中配置设为**全局默认**（v3.26）：写 active-profile.txt，影响以后无参启动的实例。</summary>
    [RelayCommand]
    private void SetProfileDefault(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        try
        {
            _profiles.SetAsDefault(name);
            StatusText = $"已将「{name}」设为默认配置 —— 以后直接启动 NewToys（不带参数）就是它。";
        }
        catch (Exception ex) { StatusText = "设置默认失败：" + ex.Message; }
    }

    /// <summary>以选中配置**新开一个窗口**（v3.26 多实例）：不退出当前窗口。</summary>
    [RelayCommand]
    private void LaunchProfileNew(PathSettingItem? row)
    {
        if (_profiles == null) return;
        var target = Resolve(row);
        if (target?.IsProfileRow != true) { StatusText = "先在列表里选中一个配置行。"; return; }

        var name = target.Name;
        if (string.Equals(name, _profiles.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"「{name}」就是当前窗口正在用的配置 —— 同一配置同时只允许一个窗口（数据会互相覆盖）。";
            return;
        }

        try
        {
            _profiles.LaunchNewInstance(name);
            StatusText = $"已新开一个「{name}」窗口（任务栏里能看到，标题带配置名）。";
        }
        catch (Exception ex) { StatusText = "新开失败：" + ex.Message; }
    }
}
