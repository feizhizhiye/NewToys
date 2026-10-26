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
/// 应用级设置页的「运行环境」节（原「设置 → 路径与配置」，v3.53：是**设置总表**）：
/// 一张列表按**分类列**承载两类行 —— `路径`（`app.json` 的运行环境路径，可编辑）
/// 与 `配置`（`profiles\` 下多套独立 config/data/logs）。
/// <para>
/// 🔴 分类决定"哪些工具可用"：路径专属工具在选中配置行时**置灰并给出原因提示**，反之亦然；
/// 与分类无关的工具（📂 打开配置目录 / ＋ 新建配置）恒可用。
/// </para>
/// <para>
/// 工具条由 Shell 声明式承载（/ 需求 §4.8.5），本页只输出描述符。
/// 🔴 v3.66：细节调整：**本页详情卡已删除**（⛔ 不再实现 <c>IDetailPanelProvider</c> /
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

    /// <summary>外部程序注册表（v3.65）：进本页时才做**惰性探测**，缺失 ⇒ 状态栏 `⚠` 可点击。</summary>
    private readonly IExternalToolRegistry? _ext;

    /// <summary>外部程序缺失的警示文案（null = 没问题）；它同时决定状态栏那一行**是否可点击**。</summary>
    private string? _externalWarn;

    /// <summary>Python 运行时：只为「Python 共享环境」只读行提供状态文案 + 目录。</summary>
    private readonly IPythonRuntime? _py;

    /// <summary>导入 / 导出默认目录（当前配置档 <c>store\</c>）。</summary>
    public string ImportExportDefaultDir => _cfg?.StoreDirectory ?? "";

    private bool _syncing;

    public ObservableCollection<PathSettingItem> Items { get; } = new();

    // StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
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
        // §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）；「操作」是按钮列 → 不给
        new("Category", "分类", "1*", sample: "路径"),
        new("Name", "名称", "2.2*", sample: "ffmpeg 路径"),
        new("Value", "当前值", "4*", sample: @"C:\ffmpeg\bin\ffmpeg.exe"),
        new("Kind", "类型", "1*", sample: "文件"),
        new("Description", "说明", "3*", sortable: false, sample: "视频的媒体信息用它旁边的 ffprobe 读取"),
        new("Action", "操作", "3.8*", sortable: false),
    };

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;
    // ==================== v3.53：工具可用性（按选中行的分类） ====================

    /// <summary>当前选中的是路径行。</summary>
    public bool IsPathRowSelected => Selected?.IsPathRow == true;

    /// <summary>当前选中的是配置行。</summary>
    public bool IsProfileRowSelected => Selected?.IsProfileRow == true;

    /// <summary>选中的配置行就是**本窗口正在使用**的那个。</summary>
    private bool IsActiveProfileRow => Selected?.IsProfileRow == true && Selected.IsActiveProfile;

    /// <summary>「💾 保存 / 📂 浏览 / ↩ 还原默认」可用（路径行且非只读展示项）。</summary>
    public bool CanSavePath => IsPathRowSelected && Selected!.IsEditable;

    /// <summary>「⇄ 切换 / 🚀 新开」可用（配置行且不是当前配置 —— 同一配置只允许一个窗口）。</summary>
    public bool CanSwitchProfile => IsProfileRowSelected && !IsActiveProfileRow;

    /// <summary>「🗑 删除」可用（配置行且不是当前配置）。</summary>
    public bool CanDeleteProfile => IsProfileRowSelected && !IsActiveProfileRow;

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

}
