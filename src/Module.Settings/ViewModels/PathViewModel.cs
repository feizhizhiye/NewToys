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
using NewToys.Settings.Models;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 「设置 → 路径与配置」页（v3.53 / NT-056 起是**设置总表**）：一张列表按**分类列**承载两类行 ——
/// `路径`（`app.json` 的运行环境路径，可编辑）与 `配置`（`profiles\` 下多套独立 config/data/logs）。
/// <para>
/// 🔴 分类决定"哪些工具可用"：路径专属工具在选中配置行时**置灰并给出原因提示**，反之亦然；
/// 与分类无关的工具（📂 打开配置目录 / ＋ 新建配置）恒可用。
/// </para>
/// <para>
/// 工具条由 Shell 声明式承载（NT-055 / 需求 §4.8.5），本页只输出描述符；详情卡按分类**切换字段集** ——
/// 选中行的分类一变就换一份描述符实例并通知 Shell 重取（⛔ 不是"缓存一次"的静态卡）。
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
public partial class PathViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly IAppConfigService? _cfg;
    private readonly ConfigStoreService? _store;
    private readonly IProfileService? _profiles;

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _cfg?.VideoDbFolder ?? "";

    private bool _syncing;

    /// <summary>上一次交给 Shell 的详情卡实例 —— 只有**分类变化**才需要通知重建（同分类内换行不必）。</summary>
    private DetailCardDescriptor? _lastCard;

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
        // v3.32 §4.8.3：每列给一条示例值（空态水印行 + 列宽判定）；「操作」是按钮列 → 不给
        new("Category", "分类", "1*", sample: "路径"),
        new("Name", "名称", "2.2*", sample: "ffmpeg 路径"),
        new("Value", "当前值", "4*", sample: @"C:\ffmpeg\bin\ffmpeg.exe"),
        new("Kind", "类型", "1*", sample: "文件"),
        new("Description", "说明", "3*", sortable: false, sample: "视频的媒体信息用它旁边的 ffprobe 读取"),
        new("Action", "操作", "3.8*", sortable: false),
    };

    // ==================== 详情栏开合（基类管状态；这里只补"没选中就选第一项"） ====================

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    // ==================== v3.32 §4.8.3：列表示例行 + 详情卡示例态 ====================

    /// <summary>详情卡 / 列表示例行用的**示例项**（路径行）。</summary>
    private static readonly PathSettingItem SampleItem = new()
    {
        Key = "FfmpegPath",
        Name = "ffmpeg 路径",
        Kind = PathItemKind.File,
        Description = "视频的媒体信息（时长 / 分辨率 / 编码 / 码率）用它旁边的 ffprobe 读取（示例数据）",
        Value = @"C:\ffmpeg\bin\ffmpeg.exe"
    };

    /// <summary>列表为空时显示**示例行**（⛔ 不进 <c>Items</c>、不参与统计 / 筛选 / 排序）。</summary>
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例项**。</summary>
    public PathSettingItem DetailItem => Selected ?? SampleItem;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    /// <summary>「当前值」是否可编辑（只读展示项 = 数据目录 / 配置目录 / 配置行的数据目录 ⇒ ⛔ 不让改）。</summary>
    public bool IsValueEditable => DetailItem.IsEditable;

    /// <summary>「当前值」是只读展示项（与 <see cref="IsValueEditable"/> 互斥 ⇒ 两个字段行各自可见）。</summary>
    public bool IsValueReadOnly => DetailItem.IsReadOnly;

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

    private DetailCardDescriptor? _pathCard;
    private DetailCardDescriptor? _profileCard;

    /// <summary>
    /// 当前详情卡描述符（**按选中行的分类切换字段集**，v3.53 / NT-056）：路径行 → <see cref="PathCard"/>，
    /// 配置行 → <see cref="ProfileCard"/>；两张卡各缓存一份实例，分类变化时在 <see cref="OnSelectedChanged"/> 里通知 Shell 重取。
    /// </summary>
    public DetailCardDescriptor? Card => Selected?.IsProfileRow == true ? ProfileCard : PathCard;

    private DetailCardDescriptor PathCard => _pathCard ??= BuildPathCard();
    private DetailCardDescriptor ProfileCard => _profileCard ??= BuildProfileCard();

    /// <summary>
    /// 「路径」行的详情卡（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）：
    /// 名称 / 当前值（可编辑 ↔ 只读两种形态）/ 类型 / 说明 + 配置文件提示 + 卡内四个动作。
    /// </summary>
    private DetailCardDescriptor BuildPathCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        SamplePath = nameof(IsDetailSample),
        EmptyText = "在左侧列表里选中一项。这些是「运行环境」相关的路径：ffmpeg / 播放器 / 三个解释器 / 脚本目录。留空 = 用系统默认；数据目录与配置目录是只读展示项。",
        SampleText = "（示例数据）在左侧选中一项后，这里显示它的真实内容；示例态下不可编辑。",
        Sections =
        {
            new DetailSection
            {
                Header = "选中项",
                Fields =
                {
                    new DetailField
                    {
                        Label = "分类",
                        ValuePath = "DetailItem.CategoryText",
                        Kind = FieldKind.Text
                    },
                    new DetailField
                    {
                        Label = "名称",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text
                    },
                    // 可编辑项：名字框失焦提交到编辑缓冲，落盘交「💾 保存」按钮
                    new DetailField
                    {
                        Label = "当前值",
                        ValuePath = nameof(EditValue),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true,
                        VisibleWhen = nameof(IsValueEditable)
                    },
                    // 只读展示项：同一条路径，但只读可复制（⛔ 不给编辑框，免得像能改）
                    new DetailField
                    {
                        Label = "当前值",
                        ValuePath = nameof(EditValue),
                        Kind = FieldKind.Text,
                        Mono = true,
                        Selectable = true,
                        VisibleWhen = nameof(IsValueReadOnly)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "改完点「💾 保存」；点「📂 浏览…」选的路径会立即生效。",
                        VisibleWhen = nameof(IsValueEditable)
                    },
                    new DetailField
                    {
                        Label = "类型",
                        ValuePath = "DetailItem.KindText",
                        Kind = FieldKind.Text
                    },
                    new DetailField
                    {
                        Label = "说明",
                        ValuePath = "DetailItem.Description",
                        Kind = FieldKind.Text
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "配置文件：%LocalAppData%\\NewToys\\config\\app.json（会被「设置 → 数据管理」的备份一起覆盖）"
                    }
                }
            }
        },
        Actions =
        {
            // 原来这四个按钮是页内卡顶部的两排（只在有选中项时显示）；声明式卡里进 Actions 区。
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "primary",
                ToolTip = "把「当前值」写回配置并落盘",
                IsVisiblePath = nameof(HasSelection),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "↩ 还原默认",
                Classes = "ghost",
                ToolTip = "恢复成该路径的默认值（⛔ 只填进输入框，仍需点「💾 保存」）",
                IsVisiblePath = nameof(HasSelection),
                Command = RestoreDefaultCommand
            },
            new DetailAction
            {
                Text = "📂 浏览…",
                Classes = "ghost",
                ToolTip = "选文件 / 选目录（按项的类型决定），选完立即生效并落盘",
                IsVisiblePath = nameof(HasSelection),
                Command = PickPathCommand
            },
            new DetailAction
            {
                Text = "↗ 打开",
                Classes = "ghost",
                ToolTip = "在资源管理器里打开这个路径",
                IsVisiblePath = nameof(HasSelection),
                // OpenCommand 是 RelayCommand<PathSettingItem?>（要参数）⇒ 这里包一层拿当前选中项
                Command = new RelayCommand(() => OpenCommand.Execute(Selected))
            }
        }
    };

    /// <summary>
    /// 「配置」行的详情卡（v3.53 / NT-056）：配置名 / 数据目录 / 状态 + 说明 + 配置侧七个动作
    /// —— 与原来的「设置 → 配置」页语义**一一对应**（切换仍二次确认 + 本窗重启；⛔ 语义零改动）。
    /// </summary>
    private DetailCardDescriptor BuildProfileCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        SamplePath = nameof(IsDetailSample),
        EmptyText = "在左侧列表里选中一个**配置行**。一个配置 = 一套独立的 config + data + logs；可同时打开多个配置的窗口（各跑各的数据）。",
        SampleText = "（示例数据）在左侧选中一个配置行后，这里显示它的真实内容。",
        Sections =
        {
            new DetailSection
            {
                Header = "选中配置",
                Fields =
                {
                    new DetailField
                    {
                        Label = "分类",
                        ValuePath = "DetailItem.CategoryText",
                        Kind = FieldKind.Text
                    },
                    new DetailField
                    {
                        Label = "配置名",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "数据目录",
                        ValuePath = "DetailItem.Value",
                        Kind = FieldKind.Text,
                        Mono = true,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "状态",
                        ValuePath = "DetailItem.ProfileStateText",
                        Kind = FieldKind.Text
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "一个配置 = 一套独立的 config / data / logs。「⇄ 切换」只重启**本窗口**，不影响其它窗口与全局默认；「⭐ 设为默认」决定以后直接启动 NewToys（不带参数）用哪个。"
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "⚠️ 删除不可撤销；当前正在使用的配置不能删除，也不能重置 default 与当前配置。"
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "⇄ 切换到此配置",
                Classes = "primary",
                ToolTip = "本窗口重启到目标配置（不影响其它窗口和默认配置）",
                IsEnabledPath = nameof(CanSwitchProfile),
                Command = SwitchProfileCommand
            },
            new DetailAction
            {
                Text = "🚀 新开一个窗口",
                Classes = "ghost",
                ToolTip = "以该配置再启动一个窗口（多实例；同一配置只能开一个）",
                IsEnabledPath = nameof(CanSwitchProfile),
                Command = LaunchProfileNewCommand
            },
            new DetailAction
            {
                Text = "⭐ 设为默认",
                Classes = "ghost",
                ToolTip = "写全局默认：以后直接启动 NewToys（不带参数）就是它",
                IsEnabledPath = nameof(IsProfileRowSelected),
                Command = SetProfileDefaultCommand
            },
            new DetailAction
            {
                Text = "＋ 新建配置",
                Classes = "ghost",
                ToolTip = "新建一套独立的 config / data / logs",
                Command = NewProfileCommand
            },
            new DetailAction
            {
                Text = "✏ 重命名",
                Classes = "ghost",
                ToolTip = "重命名该配置（目录一起改名）",
                IsEnabledPath = nameof(IsProfileRowSelected),
                Command = RenameProfileCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "danger-btn",
                ToolTip = "会永久删除该配置的全部数据；不能删除当前配置",
                IsEnabledPath = nameof(CanDeleteProfile),
                Command = DeleteProfileCommand
            },
            new DetailAction
            {
                Text = "♻ 重置为初始态",
                Classes = "ghost",
                ToolTip = "清空该配置回初始态（debug 会重新复制当前 default 的配置）；不能重置 default 或当前配置",
                IsEnabledPath = nameof(CanResetProfile),
                Command = ResetProfileCommand
            }
        }
    };

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

    // ==================== 工具条（由 Shell 的 ToolbarHost 渲染，NT-055） ====================

    /// <summary>
    /// 工具条描述符（按优先级顺序）：前四个是**路径**侧工具，中间是共用的目录 / 新建，后面是**配置**侧工具。
    /// 🔴 启停全部落在 <c>IsEnabledPath</c> 上（选中另一分类 ⇒ 置灰 + <c>ToolTipPath</c> 说明原因）。
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📂 打开配置目录", CommandPath = nameof(OpenConfigDirCommand),
                ToolTip = "打开 app.json / columnlayout.json 等所在的目录（可整体备份）" },
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

    /// <summary>详情卡里正在编辑的值（点「💾 保存」才写回对象并落盘）。</summary>
    [ObservableProperty] private string _editValue = "";

    partial void OnSelectedChanged(PathSettingItem? value)
    {
        UpdateDetailSample(value != null);   // v3.32 §4.8.3：没选中项 → 详情卡改用示例项
        OnPropertyChanged(nameof(DetailItem));
        // 切选中项 / 示例态 → 「当前值」在可编辑 ↔ 只读两种形态间切换（描述符里是两行互斥字段）
        OnPropertyChanged(nameof(IsValueEditable));
        OnPropertyChanged(nameof(IsValueReadOnly));
        _syncing = true;
        // 只有路径行才进「当前值」编辑缓冲（配置行 / 示例态都是只读展示）
        EditValue = value is { IsPathRow: true } ? value.Value
            : value == null && HasSampleRow ? SampleItem.Value : "";
        _syncing = false;
        OnPropertyChanged(nameof(HasSelection));

        RaiseActionStates();   // v3.53 / NT-056：工具条按分类启停，靠这批通知刷新

        // 🔴 分类变了 ⇒ 详情卡换描述符：Shell 只在收到 DetailChanged 时才重取 Card（需求 §4.9.1 第 3 条）
        var card = value?.IsProfileRow == true ? ProfileCard : PathCard;
        OnPropertyChanged(nameof(Card));
        if (!ReferenceEquals(card, _lastCard))
        {
            _lastCard = card;
            DetailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnEditValueChanged(string value)
    {
        // 绑定到 TextBox 时不要反过来打扰输入；这里只是把"是否改过"反映到提示上
        if (!_syncing) StatusText = "改完点「💾 保存」写入 app.json。";
    }

    // ==================== 构造 ====================

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PathViewModel() { }

    public PathViewModel(IAppConfigService cfg, ConfigStoreService store, IColumnLayoutService cols, IProfileService profiles)
        : base(cols)
    {
        _cfg = cfg;
        _store = store;
        _profiles = profiles;

        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        SetSort("Category", false);       // v3.53：默认按「分类」升序（同类聚在一起），组内再按名称
        BuildItems();
        RebuildQuickFilters();            // v3.21 §4.8.2：分类 / 类型进表头右键「筛选」
        ApplyView();
        UpdateDetailSample(false);        // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
        EditValue = SampleItem.Value;
        SelectActiveProfile();            // v3.53：默认选中「正在使用」的配置行（一眼看到我在哪）
    }

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
        Items.Add(new PathSettingItem
        {
            Key = "PythonPath", Name = "Python 路径", Kind = PathItemKind.File,
            Description = "「设置与脚本」里 .py 的解释器；留空 = 用 PATH 里的 python",
            Value = _cfg.PythonPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PwshPath", Name = "PowerShell 7+（pwsh）路径", Kind = PathItemKind.File,
            Description = ".ps1 优先用它；留空 = 用 PATH 里的 pwsh，找不到则退回系统自带的 5.1",
            Value = _cfg.PwshPath
        });
        Items.Add(new PathSettingItem
        {
            Key = "PowerShell51Path", Name = "Windows PowerShell 5.1 路径", Kind = PathItemKind.File,
            Description = "没配 pwsh 时 .ps1 用它；留空 = 系统自带的 powershell.exe",
            Value = _cfg.PowerShell51Path
        });
        Items.Add(new PathSettingItem
        {
            Key = "ScriptFolder", Name = "脚本目录", Kind = PathItemKind.Folder,
            DefaultValue = _cfg.DefaultScriptFolder,
            Description = "「设置与脚本」页引用的 .py / .ps1 / .bat 都放这里；首次运行会把示例脚本复制进去",
            Value = _cfg.ScriptFolder
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
        Items.Add(new PathSettingItem
        {
            Key = "ConfigDir", Name = "配置目录", Kind = PathItemKind.ReadOnly,
            Description = "app.json / launch.json / theme.json / pc-quick.json / pc-scripts.json / columnlayout.json 等都在这里，可整体备份",
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
        OnPropertyChanged(nameof(ShowSampleRow));
        OnPropertyChanged(nameof(IsDetailSample));
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
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.32：有真实数据后示例行消失
        UpdateDetailSample(Selected != null);
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
                case "PythonPath": _cfg.PythonPath = v; break;
                case "PwshPath": _cfg.PwshPath = v; break;
                case "PowerShell51Path": _cfg.PowerShell51Path = v; break;
                case "ScriptFolder": _cfg.ScriptFolder = v; break;
                case "VideoDbFolder": _cfg.VideoDbFolder = v; break;
                // DataDir / ConfigDir 是只读展示项，不写；配置行（Key = "profile:<名>"）不属于 app.json
            }
        }
        _cfg.Save();
    }

    /// <summary>脚本目录的默认值（详情卡提示用）。</summary>
    public string DefaultScriptFolder => _cfg?.DefaultScriptFolder ?? "";

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
