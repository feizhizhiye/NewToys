using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

/// <summary>分类下拉项（<see cref="Value"/> 为 null = 「全部分类」）。</summary>
public class ActionCategoryOption
{
    public string Label { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>「类型 / 解释器」下拉项（<see cref="Value"/> = 存进配置的值）。</summary>
public class ActionKindOption
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// 「脚本管理器」里两个<b>同类列表页</b>（「脚本库」与「命令库」）共用的 ViewModel。
/// <para>
/// 两页的布局 / 交互<b>完全一致</b>（规范 3.6）：左「可排序表头 + 列表 + 行内「▶ 运行」」，右「选中项属性卡」，
/// 列表上挂右键菜单（运行 / 收藏 / 复制命令 / 删除）。差别只有三点，由派生类给出：
/// ① <see cref="PageKind"/>：本页显示哪一批条目（**每页一份清单文件**，见 <c>FilePath</c>）；
/// ② <see cref="ColumnLayoutKey"/>：列显隐偏好落 <c>config\columnlayout.json</c> 时用的页键；
/// ③ 页面名字 <see cref="PageDisplayName"/>（提示文案用）。
/// <b>新增项的默认类型</b>由 <see cref="PageKind"/> 推出（见 <c>IQuickActionService.Add</c>）：
/// 「命令库」= 命令行命令、「脚本库」= 脚本文件（v1.33）。
/// </para>
/// <para>
/// 列显隐 / 详情栏开合 / 排序 / 多项筛选继承自 <see cref="ListPageViewModelBase"/>（v1.35 收口），
/// 本类只留「两页共有的业务」：分类过滤、运行、增删改查、属性卡编辑缓冲。
/// </para>
/// <para>
/// ⚠️ 这里的「设置」是<b>操作系统设置项</b>，不是本软件自己的设置（后者在「设置」模块，见规范 3.9）。
/// </para>
/// <para>
/// 收藏 = 每条 <see cref="QuickAction.IsFavorite"/> 上的标记（单一真相源），
/// 「收藏夹」页读的是同一批对象，因此这里右键收藏后那边天然一致。
/// </para>
/// </summary>
public abstract partial class QuickActionListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly IQuickActionService? _svc;
    private readonly IAppConfigService? _app;

    /// <summary>本模块设置（v3.64 / NT-061：脚本目录 + 解释器路径的唯一写入点，落 config\script-manager.json）。</summary>
    private readonly IScriptManagerSettingsService? _pc;

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    /// <summary>本页归属的 <see cref="QuickAction.Page"/>（<see cref="QuickActionPage"/> 之一）。</summary>
    private readonly string _pageKind;

    /// <summary>灌值 / 改过滤项时抑制「属性变化 → 立即重排」的开关，避免构造期重复计算。</summary>
    private bool _suppress;

    /// <summary>本页归属的 <see cref="QuickAction.Page"/>。</summary>
    protected abstract string PageKind { get; }

    /// <summary>列显隐偏好落盘用的页键（唯一，两页各一份）。</summary>
    protected abstract string ColumnLayoutKey { get; }

    /// <summary>页面名字（新增 / 空态等提示文案用）。</summary>
    protected abstract string PageDisplayName { get; }

    public ObservableCollection<QuickAction> Items { get; } = new();
    public ObservableCollection<ActionCategoryOption> CategoryOptions { get; } = new();

    // ==================== 页面状态 ====================

    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ActionCategoryOption? _selectedCategory;
    [ObservableProperty] private QuickAction? _selected;

    /// <summary>供行内按钮绑定（<c>{Binding !IsBusy}</c> 只能用于同层路径，跨 ListBox 取父级 VM 得用这个）。</summary>
    public bool IsNotBusy => !IsBusy;

    // ==================== 表头（左键点一下切换排序） ====================

    [ObservableProperty] private string _titleHeader = "名称 ▲";
    [ObservableProperty] private string _categoryHeader = "功能";
    [ObservableProperty] private string _favoriteHeader = "★";

    // ==================== 列显隐（基类管状态，这里只给 XAML 提供具名属性） ====================

    public bool IsTitleVisible => Vis("Title");
    public bool IsCategoryVisible => Vis("Category");
    public bool IsClassifyVisible => Vis("Classify");
    public bool IsDescriptionVisible => Vis("Description");
    public bool IsFavoriteVisible => Vis("Favorite");
    public bool IsCollectableVisible => Vis("Collectable");
    public bool IsActionVisible => Vis("Action");

    protected override string PageKey => ColumnLayoutKey;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。
    /// <para>最后一列 <c>Detail</c> 是**表头右侧的那个无文字小按钮**用的固定窄列（规范 3.6）：
    /// 不参与列显隐菜单、也不进 <c>columnlayout.json</c>，它只是把表头与行的最后一格对齐留出来。</para></summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.22 §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）；「操作」是按钮列 → 不给
        new("Title", "名称", "3*", sample: "清理临时文件"),
        // v3.63：「功能」= 原来那列 `Category`（维护 / 网络 …）—— 表头与详情卡同名
        new("Category", "功能", "1.3*", sample: "维护"),
        // v3.63 新增「分类」：脚本 = 脚本后缀（.ps1 …），命令行命令 = Shell 宿主（cmd / pwsh / 自动）
        new("Classify", "分类", "1.1*", sortable: false, sample: ".ps1"),
        new("Description", "简介", "4*", sortable: false, sample: "删除 %TEMP% 下的临时文件"),
        new("Favorite", "★", "0.9*", sample: "★"),
        // NT-068 S3：「可采集」= 能不能当采集器用（✔ / ✖，✖ 悬浮看原因）；派生值，不落盘
        new("Collectable", "可采集", "0.9*", sample: "✔"),
        new("Action", "操作", "1.5*", sortable: false),
    };

    // ==================== 属性卡的编辑缓冲（多字段文本表单 → 显式「💾 保存」，规范 3.5） ====================

    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editCategory = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editCommand = "";
    [ObservableProperty] private bool _editNeedAdmin;

    // ---- 运行窗口行为（v3.19 / NT-006，仅命令行命令有意义）----

    /// <summary>编辑缓冲：是否「隐藏终端窗口」（GUI 启动类静默跑、不弹终端；管理员时失效）。</summary>
    [ObservableProperty] private bool _editRunHidden;

    /// <summary>编辑缓冲：命令行宿主（取值见 <see cref="QuickActionShell"/>，旧数据缺省 = Auto）。</summary>
    [ObservableProperty] private string _editShell = QuickActionShell.Auto;

    /// <summary>「Shell」下拉的选项（来自 <see cref="QuickActionShell.Options"/>）。</summary>
    public ObservableCollection<ActionKindOption> ShellOptions { get; } = new(
        QuickActionShell.Options.Select(o => new ActionKindOption { Label = o.Label, Value = o.Value }));

    [ObservableProperty] private ActionKindOption? _selectedShell;

    partial void OnSelectedShellChanged(ActionKindOption? value)
    {
        if (value != null) EditShell = value.Value;
    }

    // ---- 类型 / 脚本（v1.33）----

    /// <summary>编辑缓冲里的条目类型（取值见 <see cref="QuickActionKind"/>）。</summary>
    [ObservableProperty] private string _editKind = QuickActionKind.Command;

    /// <summary>编辑缓冲里的脚本文件（相对脚本目录的文件名，或绝对路径）。</summary>
    [ObservableProperty] private string _editScriptFile = "";

    /// <summary>编辑缓冲里的解释器（取值见 <see cref="ScriptInterpreter"/>，空 = 自动）。</summary>
    [ObservableProperty] private string _editInterpreter = ScriptInterpreter.Auto;

    /// <summary>「类型」下拉的选项。</summary>
    public ObservableCollection<ActionKindOption> KindOptions { get; } = new()
    {
        new ActionKindOption { Label = "命令行命令（直接跑一行命令）", Value = QuickActionKind.Command },
        new ActionKindOption { Label = "脚本文件（跑脚本目录里的文件）", Value = QuickActionKind.Script },
    };

    /// <summary>「解释器」下拉的选项（空 = 按扩展名自动）。</summary>
    public ObservableCollection<ActionKindOption> InterpreterOptions { get; } = new()
    {
        new ActionKindOption { Label = "自动（按扩展名）", Value = ScriptInterpreter.Auto },
        new ActionKindOption { Label = "Python（.py）", Value = ScriptInterpreter.Python },
        new ActionKindOption { Label = "PowerShell 7+ / pwsh（.ps1）", Value = ScriptInterpreter.Pwsh },
        new ActionKindOption { Label = "Windows PowerShell 5.1（.ps1）", Value = ScriptInterpreter.PowerShell51 },
        new ActionKindOption { Label = "cmd（.bat / .cmd）", Value = ScriptInterpreter.Cmd },
    };

    [ObservableProperty] private ActionKindOption? _selectedKind;
    [ObservableProperty] private ActionKindOption? _selectedInterpreter;

    partial void OnSelectedKindChanged(ActionKindOption? value)
    {
        if (value != null) EditKind = value.Value;
    }

    partial void OnSelectedInterpreterChanged(ActionKindOption? value)
    {
        if (value != null) EditInterpreter = value.Value;
    }

    partial void OnEditKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsScriptKind));
        OnPropertyChanged(nameof(IsCommandKind));
        OnPropertyChanged(nameof(ShowCommandRunOptions));
    }

    /// <summary>当前编辑的是"脚本文件"类型 → 详情卡显示脚本字段、隐藏命令框（反之亦然）。</summary>
    public bool IsScriptKind => string.Equals(EditKind, QuickActionKind.Script, StringComparison.Ordinal);

    public bool IsCommandKind => !IsScriptKind;

    /// <summary>是否显示 Shell / 隐藏终端窗口 两个运行选项（v3.32：详情卡正文显示时且是命令行命令）。</summary>
    public bool ShowCommandRunOptions => ShowDetailBody && IsCommandKind;

    // ==================== 脚本目录（v3.64：唯一写入点 = 本模块设置页；这里的框只是同一份值的另一个视图） ====================

    /// <summary>脚本目录（生效值），详情卡里提示"文件放哪"。</summary>
    public string ScriptsDirectory => _svc?.ScriptsDirectory ?? "";

    /// <summary>脚本目录的默认值（配置留空时用的那个）。</summary>
    public string DefaultScriptFolder => _pc?.DefaultScriptsFolder ?? "";

    /// <summary>脚本目录当前是不是"用默认值"。</summary>
    public bool IsDefaultScriptFolder => string.IsNullOrWhiteSpace(_pc?.ScriptsFolder);

    /// <summary>
    /// 工具条「脚本目录」框的**编辑态草稿**（v3.64 / NT-061）：框 TwoWay 绑到它，
    /// 回车 / 失焦时由 <see cref="CommitScriptsFolderCommand"/> 一次提交 —— ⛔ 不每敲一个字就写盘。
    /// </summary>
    [ObservableProperty] private string _scriptsFolderDraft = "";

    /// <summary>
    /// 提交工具条那个框：交**模块设置服务**校验 + 落盘（<c>config\script-manager.json</c>）。
    /// 路径无效 ⇒ 状态文案以 `⚠` 开头（底栏红字）+ ⛔ 不写盘，保持上一个有效值。
    /// </summary>
    [RelayCommand]
    private void CommitScriptsFolder()
    {
        if (_pc == null) return;
        StatusText = _pc.SetScriptsFolder(ScriptsFolderDraft);
        RefreshScriptFolderViews();
    }

    /// <summary>
    /// 顶部「脚本目录」用文件选择器选完之后由 View 的 code-behind 调用：这是**明确的动作** → 立即生效并落盘
    /// （不用再点保存；避免"选了却没生效"的困惑，与模块设置页同一条规则）。
    /// </summary>
    public void ApplyPickedScriptFolder(string path)
    {
        if (_pc == null || string.IsNullOrWhiteSpace(path)) return;
        ScriptsFolderDraft = path.Trim();
        CommitScriptsFolder();
    }

    /// <summary>恢复成默认脚本目录（<c>&lt;应用根&gt;\scripts</c>）。</summary>
    [RelayCommand]
    private void ResetScriptFolder()
    {
        if (_pc == null) return;
        ScriptsFolderDraft = "";
        StatusText = _pc.ResetScriptsFolder();
        RefreshScriptFolderViews();
    }

    /// <summary>在资源管理器里打开脚本目录（没有就先建出来）。</summary>
    [RelayCommand]
    private void OpenScriptsFolder()
    {
        var dir = ScriptsDirectory;
        try
        {
            if (string.IsNullOrWhiteSpace(dir)) { StatusText = "脚本目录未配置。"; return; }
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            StatusText = "已打开脚本目录：" + dir;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>脚本目录相关的显示项一起刷新（生效值 / 默认值 / 是否默认 / 提示文本）。</summary>
    private void RefreshScriptFolderViews()
    {
        OnPropertyChanged(nameof(ScriptsDirectory));
        OnPropertyChanged(nameof(DefaultScriptFolder));
        OnPropertyChanged(nameof(IsDefaultScriptFolder));
        OnPropertyChanged(nameof(ScriptHint));
    }

    /// <summary>脚本字段下方的提示（解释器怎么挑 + 脚本目录在哪）。</summary>
    public string ScriptHint =>
        $"脚本文件 = 脚本目录里的 .py / .ps1 / .bat（填相对文件名即可，目录在「设置 → 脚本管理器设置」里改，工具条那个框也能直接改）；工具条「📥 导入脚本」可批量导入。\n脚本目录：{ScriptsDirectory}" +
        (IsDefaultScriptFolder ? "（默认值）\n" : "\n") +
        "解释器留「自动」即可：.py→python、.ps1→pwsh（没配则退回 5.1）、.bat/.cmd→cmd；三个解释器路径在「设置 → 脚本管理器设置」里配，留空 = 用系统默认。";

    // ==================== 派生显示 ====================

    /// <summary>右键菜单里「收藏 / 取消收藏」的动态文案。</summary>
    [ObservableProperty] private string _favoriteMenuLabel = "★ 添加到收藏夹";

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public QuickAction? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================
    //    本基类被两页共用（「脚本库」/「命令库」）⇒ 契约在这里实现一次，
    //    **卡片描述符由两个子类各自 override <see cref="BuildCard"/>**（两页字段本来就不同：
    //    脚本页有「类型 / 脚本文件 / 解释器」，命令库页有「Shell / 隐藏终端窗口」）。

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

    /// <summary>本页详情卡描述符（**C# 对象字面量**；由两个子类各自实现，因为两页字段不同）。</summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    /// <summary>构造本页的详情卡描述符（子类实现）。</summary>
    protected abstract DetailCardDescriptor BuildCard();

    /// <summary>
    /// 详情卡的**图标动作组**（NT-059 / <see cref="FieldKind.IconBar"/>）：运行 / 收藏 / 保存 / 删除
    /// 合成**一行 32×32 小图标按钮**（替代原来四个撑满宽度的文字按钮）。
    /// <para>
    /// 两页**完全一致** ⇒ 基类给一份，子类把它插进字段区末尾。
    /// 收藏 = 「未收藏 / 已收藏」两个**互斥**图标（靠 <c>IsActivePath</c> 走强调色表达状态，⛔ 无文字）。
    /// </para>
    /// </summary>
    protected DetailField BuildActionBar() => new()
    {
        Kind = FieldKind.IconBar,
        IconActions =
        {
            new DetailIconAction
            {
                IconKey = "IconPlay",
                ToolTip = "运行这条（命令用 cmd 执行；勾了「需要管理员权限」会弹 UAC）",
                CommandPath = nameof(RunCommand),
                IsVisiblePath = nameof(HasSelection),
                IsEnabledPath = nameof(IsNotBusy)
            },
            new DetailIconAction
            {
                IconKey = "IconStar",
                ToolTip = "收藏 / 取消收藏（图标亮 = 已收藏）",
                CommandPath = nameof(ToggleFavoriteCommand),
                IsVisiblePath = nameof(HasSelection),
                IsActivePath = nameof(IsSelectedFavorite)
            },
            new DetailIconAction
            {
                IconKey = "IconSave",
                ToolTip = "把当前内容写回本页数据文件",
                CommandPath = nameof(SaveCommand),
                IsVisiblePath = nameof(HasSelection)
            },
            new DetailIconAction { IsSeparator = true },
            new DetailIconAction
            {
                IconKey = "IconTrash",
                ToolTip = "从列表移除这条（⛔ 只动清单、不删磁盘文件；删文件用列表右键菜单）",
                CommandPath = nameof(DeleteCommand),
                IsVisiblePath = nameof(HasSelection),
                IsEnabledPath = nameof(IsNotBusy),
                IsDanger = true
            }
        }
    };

    // ==================== 分类 = 功能标签（v3.62：详情卡 chips） ====================

    /// <summary>分类标签（**功能**维度，⛔ 不再是来源）：设置 / 脚本 / 定时任务。</summary>
    public static readonly string[] CategoryPresets = { "设置", "脚本", "定时任务" };

    /// <summary>
    /// 详情卡「分类」chips 的数据源。
    /// <para>🔴 **集合实例必须稳定**：只改每项的 <c>IsSelected</c>，⛔ 不要 Clear / 重建
    /// （重建会把 chip 的选中态清掉并回写空值）。</para>
    /// </summary>
    public ObservableCollection<CategoryPick> CategoryPicks { get; } =
        new(CategoryPresets.Select(p => new CategoryPick(p)));

    /// <summary>
    /// 详情卡「分类」：与表头「分类」列**同源**（<see cref="QuickAction.Classify"/>）
    /// —— 脚本条目 = 脚本后缀（.py / .ps1 / .bat / .cmd），命令行命令 = Shell 宿主（cmd / pwsh / 自动）。
    /// </summary>
    public string ClassifyText => DetailItem?.Classify ?? "";

    /// <summary>详情卡「分类」（旧）：脚本条目 = **脚本后缀**（.py / .ps1 / .bat / .cmd），没有脚本文件 = 「—」。</summary>
    public string ScriptExtText
    {
        get
        {
            var file = (DetailItem?.ScriptFile ?? "").Trim();
            var ext = Path.GetExtension(file).ToLowerInvariant();
            return ext.Length > 0 ? ext : "—";
        }
    }

    /// <summary>点分类 chip：**单选**语义（点已选中的那个 = 取消），改完立即落盘。</summary>
    [RelayCommand]
    private void ToggleCategory(CategoryPick? pick)
    {
        if (pick == null || Selected == null || _svc == null) return;

        var next = pick.IsSelected ? "" : pick.Name;
        Selected.Category = next;
        EditCategory = next;
        _svc.Save();

        SyncCategoryPicks();
        RebuildCategories();      // 分类值变了 ⇒ 分类候选 / 筛选菜单要重算
        ApplyViewKeepSelection();

        StatusText = next.Length > 0
            ? $"已把「{Selected.Title}」归到「{next}」。"
            : $"已清掉「{Selected.Title}」的分类。";
    }

    /// <summary>把 chips 的选中态同步到**当前卡片条目**（集合实例不变，只改 <c>IsSelected</c>）。</summary>
    private void SyncCategoryPicks()
    {
        var cur = (DetailItem?.Category ?? "").Trim();
        foreach (var p in CategoryPicks)
            p.IsSelected = string.Equals(p.Name, cur, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// 刷新列表但**保住选中项**：<see cref="ApplyView"/> 会 <c>Items.Clear()</c> 再重填，
    /// ListBox 的 <c>SelectedItem</c> 双向绑定在清空这一瞬被回写成 null ⇒ 详情卡掉回「未选中」空态
    /// （"点收藏 / 点分类后详情卡变空白"就是这个）⇒ 改完列表内容后把选中项原样放回去。
    /// </summary>
    protected void ApplyViewKeepSelection()
    {
        var keep = Selected;
        ApplyView();
        if (keep != null && Selected == null && Items.Contains(keep)) Selected = keep;
    }

    /// <summary>选中项是否已收藏（没选中 → false）—— 收藏图标的高亮。</summary>
    public bool IsSelectedFavorite => DetailItem?.IsFavorite == true;

    /// <summary>选中项尚未收藏（没选中 → false）。</summary>
    public bool IsSelectedNotFavorite => DetailItem?.IsFavorite != true;

    /// <summary>属性卡里收藏按钮的文案。</summary>
    public string FavoriteButtonText => DetailItem?.IsFavorite == true ? "★ 取消收藏" : "☆ 收藏";

    /// <summary>底部提示：数据落在哪、会不会被备份（规范 §9）。
    /// 两页各有自己的文件（v1.34）：「脚本库」= <c>pc-scripts.json</c>、「命令库」= <c>pc-quick.json</c>。</summary>
    public string DataFileHint =>
        $"本页数据文件：{FilePath}\n（两个页面的文件都在 config\\ 下，会被「设置 → 数据管理」的备份 / 导入一起覆盖）";

    /// <summary>本页的配置文件全路径（两页各一份，v1.34 拆文件）。</summary>
    public string FilePath => _pageKind == QuickActionPage.Quick
        ? _svc?.QuickFilePath ?? ""
        : _svc?.ScriptsFilePath ?? "";

    /// <summary>另一个页面的名字（右键「移动到…」的提示文案用）。</summary>
    public string OtherPageName => _pageKind == QuickActionPage.Quick ? "脚本库" : "命令库";

    /// <summary>右键菜单里「移到另一页」的动态文案。</summary>
    public string MoveMenuLabel => _pageKind == QuickActionPage.Quick
        ? "➡ 移到「脚本库」"
        : "➡ 移到「命令库」（本页只放有脚本文件的脚本）";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    protected QuickActionListViewModel()
    {
        _pageKind = QuickActionPage.Shortcuts;
        FillEditors(null);
    }

    protected QuickActionListViewModel(IQuickActionService svc, IColumnLayoutService cols,
        IAppConfigService app, IScriptManagerSettingsService? pc = null)
        : base(cols)
    {
        _svc = svc;
        _app = app;
        _pc = pc;
        ScriptsFolderDraft = pc?.ScriptsFolder ?? "";
        // 抽象属性在基类构造里读一次存字段：避免「构造期虚调用」反复触发，也让后续逻辑读到稳定值。
        _pageKind = PageKind;
        LoadLayout();
        RebuildCategories();
        ApplyView();
        FillEditors(null);
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 <c>SortCommand</c> 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    /// <summary>筛选取值：按列键拿这一行该列显示的文本（多项筛选用）。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Title" => ((QuickAction)row).Title,
        "Category" => ((QuickAction)row).Category,
        "Description" => ((QuickAction)row).Description,
        "Favorite" => ((QuickAction)row).FavoriteMark,
        // NT-068 S3：可采集（✔ / ✖）——「仅可采集」筛选与表头右键都走这一列。
        "Collectable" => ((QuickAction)row).CollectableMark,
        _ => null
    };

    // ==================== 列表构建 ====================

    private void RebuildCategories()
    {
        if (_svc == null) return;

        var keep = SelectedCategory?.Value;
        var source = _svc.Items.Where(i => BelongsToThisPage(i)).ToList();

        _suppress = true;
        CategoryOptions.Clear();
        CategoryOptions.Add(new ActionCategoryOption { Label = "全部类型", Value = null });
        foreach (var c in source.Select(i => i.Category)
                                .Where(c => !string.IsNullOrWhiteSpace(c))
                                .Distinct()
                                .OrderBy(c => c, StringComparer.CurrentCulture))
            CategoryOptions.Add(new ActionCategoryOption { Label = c, Value = c });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == keep) ?? CategoryOptions[0];

        // v3.19 / NT-006：分类也进表头右键「🔎 筛选」子菜单（原工具条「分类筛选框」移入这里）
        // NT-068 S3：追加「仅可采集」—— 按需生成集合的入口（卡 §三.5）。
        var quick = CategoryOptions.Where(o => !string.IsNullOrEmpty(o.Value))
                                   .Select(o => (o.Label, "Category", o.Value!))
                                   .Append(("仅可采集", "Collectable", "✔"));
        SetQuickFilters(quick);
        _suppress = false;
    }

    /// <summary>本条是否属于本页（「脚本库」= 非命令库；「命令库」= Quick）。</summary>
    private bool BelongsToThisPage(QuickAction item)
        => _pageKind == QuickActionPage.Quick
            ? item.Page == QuickActionPage.Quick
            : item.Page != QuickActionPage.Quick;

    private void ApplyView()
    {
        if (_svc == null) return;

        IEnumerable<QuickAction> q = _svc.Items.Where(BelongsToThisPage);

        var cat = SelectedCategory?.Value;
        if (!string.IsNullOrEmpty(cat))
            q = q.Where(i => string.Equals(i.Category, cat, StringComparison.CurrentCultureIgnoreCase));

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(i => i.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || i.Category.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || i.Description.Contains(text, StringComparison.CurrentCultureIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Category" => SortDescending ? q.OrderByDescending(i => i.Category, byText) : q.OrderBy(i => i.Category, byText),
            "Favorite" => SortDescending ? q.OrderByDescending(i => i.IsFavorite) : q.OrderBy(i => i.IsFavorite),
            _ => SortDescending ? q.OrderByDescending(i => i.Title, byText) : q.OrderBy(i => i.Title, byText),
        };

        Items.Clear();
        foreach (var i in q) Items.Add(i);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        TitleHeader = "名称" + SortArrow("Title");
        CategoryHeader = "功能" + SortArrow("Category");   // v3.63：原「类型」改叫「功能」（后缀那列才是「分类」）
        FavoriteHeader = "★" + SortArrow("Favorite");
    }

    // ==================== 过滤 ====================

    partial void OnSearchTextChanged(string value) { if (!_suppress) ApplyView(); }

    partial void OnSelectedCategoryChanged(ActionCategoryOption? value) { if (!_suppress) ApplyView(); }

    partial void OnSelectedChanged(QuickAction? value)
    {
        OnPropertyChanged(nameof(DetailItem));
        FillEditors(DetailItem);             // 填充详情卡编辑缓冲
        // 切选中项 → 收藏按钮的互斥可见性跟着换
        OnPropertyChanged(nameof(IsSelectedFavorite));
        OnPropertyChanged(nameof(IsSelectedNotFavorite));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowCommandRunOptions));
        OnPropertyChanged(nameof(CanEditScript));
        OnPropertyChanged(nameof(ScriptExtText));
        OnPropertyChanged(nameof(ClassifyText));
        SyncCategoryPicks();          // chips 的选中态跟着新条目走（集合实例不变）
    }

    /// <summary>
    /// 把一条条目（**真正的选中项**）灌进编辑缓冲。
    /// <para>抽成方法：编辑缓冲的填充只此一条路径，⛔ 别在多处各填一遍。</para>
    /// </summary>
    private void FillEditors(QuickAction? value)
    {
        // 点「💾 保存」才写回对象并落盘
        EditTitle = value?.Title ?? "";
        EditCategory = value?.Category ?? "";
        EditDescription = value?.Description ?? "";
        EditCommand = value?.Command ?? "";
        EditNeedAdmin = value?.NeedAdmin ?? false;
        EditScriptFile = value?.ScriptFile ?? "";
        EditRunHidden = value?.RunHidden ?? false;

        // 类型 / 解释器用下拉的选中项承载（赋值会经 OnSelected*Changed 回写上面两个缓冲字段）。
        var kind = value?.Kind ?? QuickActionKind.Command;
        var interp = value?.Interpreter ?? ScriptInterpreter.Auto;
        SelectedKind = KindOptions.FirstOrDefault(o => string.Equals(o.Value, kind, StringComparison.OrdinalIgnoreCase))
                       ?? KindOptions[0];
        SelectedInterpreter = InterpreterOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", interp ?? "", StringComparison.OrdinalIgnoreCase))
                              ?? InterpreterOptions[0];
        SelectedShell = ShellOptions.FirstOrDefault(o => string.Equals(o.Value, QuickActionShell.Normalize(value?.Shell), StringComparison.OrdinalIgnoreCase))
                        ?? ShellOptions[0];

        UpdateFavoriteUi(value);
    }

    // ==================== 运行 ====================

    /// <summary>
    /// 运行一条命令（行内「▶ 运行」传本行、详情卡与右键菜单传当前选中项）。
    /// <para>命令经可见终端窗口执行（优先 pwsh，需要管理员权限的走 UAC 提权，窗口同样可见）。
    /// 输出直接显示在弹出的窗口里，状态行只回报「已打开窗口运行」。</para>
    /// </summary>
    [RelayCommand]
    private async Task RunAsync(QuickAction? item)
    {
        var target = item ?? Selected;
        if (target == null || _svc == null) return;
        if (IsBusy) return;

        IsBusy = true;
        StatusText = $"正在运行：{target.Title} …";
        try
        {
            var (ok, message) = await _svc.RunAsync(target).ConfigureAwait(true);
            StatusText = ok ? message : "⚠ " + message;
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 运行失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ==================== 批量导入脚本目录（v1.35） ====================

    /// <summary>认得的脚本扩展名（小写，带点）。导入时只挑这些。</summary>
    public static readonly string[] ScriptExtensions = { ".py", ".ps1", ".bat", ".cmd" };

    /// <summary>文件选择器的类型过滤（<c>;</c> 分隔的通配模式，见 <c>App.BuildFileTypes</c>）。</summary>
    public static string ScriptFilePattern => string.Join(";", ScriptExtensions.Select(e => "*" + e));

    /// <summary>导入子目录里的脚本吗（工具条上的勾选框直接读它；只对「导入文件夹」生效）。</summary>
    [ObservableProperty] private bool _importSubFolders = true;

    /// <summary>上一次导入的结论（状态行就是它）。</summary>
    public string ImportResultText { get; private set; } = "";

    private static string ScriptExtensionsList => string.Join(" / ", ScriptExtensions);

    /// <summary>
    /// 📥 导入**一批脚本文件**（工具条「导入文件 / 导入文件夹」两路共用）。
    /// <para>
    /// 规则（都写进状态行，绝不"悄悄跳过"）：
    /// ① 只收 <see cref="ScriptExtensions"/> 里的扩展名；
    /// ② 清单里已有的（按脚本文件名字比对）**跳过**，不重复建；
    /// ③ 落在脚本目录内的存**相对文件名**、目录外的存绝对路径（路径一律以**配置的脚本目录**为准）；
    /// ④ 名称取文件名（去扩展名）、分类填「导入」，方便之后批量改。
    /// </para>
    /// <para>
    /// ⚠️ 导入后**清掉搜索 / 分类 / 表头筛选**并选中第一条新增 —— 否则新条目可能被当前筛选挡住，
    /// 看起来像"导入没生效"（与「➕ 新增」同一口径）。
    /// </para>
    /// </summary>
    /// <returns>(新增几条, 跳过几条)。</returns>
    public (int Added, int Skipped) ImportScriptFiles(IEnumerable<string> files)
    {
        if (_svc == null) return (0, 0);

        var scriptsDir = ScriptsDirectory;
        var existing = new HashSet<string>(
            _svc.Items.Select(i => (i.ScriptFile ?? "").Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var skipped = 0;
        QuickAction? first = null;

        foreach (var file in files ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file)) continue;
            if (!ScriptExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;

            var stored = ToStoredScriptPath(file, scriptsDir);
            if (existing.Contains(stored)) { skipped++; continue; }

            var item = _svc.Add(_pageKind);
            item.Kind = QuickActionKind.Script;
            item.Title = Path.GetFileNameWithoutExtension(file);
            // 分类**不自动填**（v3.62：分类 = 功能标签，由用户在详情卡的 chips 上点；⛔ 不再写"导入"）
            item.Category = "";
            item.Description = $"导入：{Path.GetFileName(file)}";
            item.ScriptFile = stored;
            item.Interpreter = ScriptInterpreter.Auto;
            existing.Add(stored);
            added++;
            first ??= item;
        }

        if (added > 0)
        {
            _svc.Save();
            RebuildCategories();
            _suppress = true;
            SearchText = "";
            if (CategoryOptions.Count > 0) SelectedCategory = CategoryOptions[0];
            _suppress = false;
            ResetFilters();
            ApplyView();
            if (first != null) Selected = Items.FirstOrDefault(i => ReferenceEquals(i, first)) ?? first;
        }

        ImportResultText = added == 0
            ? $"没有可导入的新脚本（只认 {ScriptExtensionsList}）"
              + (skipped > 0 ? $"；{skipped} 个已在清单里，已跳过" : "")
            : $"已导入 {added} 个脚本"
              + (skipped > 0 ? $"；{skipped} 个已存在，已跳过" : "");
        StatusText = ImportResultText;
        return (added, skipped);
    }

    /// <summary>📥 导入一个**目录**里的脚本（<paramref name="includeSubFolders"/> = 是否连子目录一起收）。</summary>
    public (int Added, int Skipped) ImportScriptFolder(string folder, bool includeSubFolders = true)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return (0, 0);

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*",
                        includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .ToList();   // 先固化，避免枚举中途目录变动
        }
        catch { return (0, 0); }

        return ImportScriptFiles(files);
    }

    /// <summary>
    /// 落盘的路径怎么存：脚本目录**内**的文件存**相对文件名**（换机器 / 换目录也能跑），目录外的存绝对路径。
    /// </summary>
    private static string ToStoredScriptPath(string file, string scriptsDir)
        => !string.IsNullOrWhiteSpace(scriptsDir)
           && file.StartsWith(scriptsDir, StringComparison.OrdinalIgnoreCase)
            ? file[scriptsDir.Length..].TrimStart('\\', '/')
            : file;

    // ==================== 工具条（NT-055 / 需求 §4.8.5：由 Shell 统一渲染） ====================

    /// <summary>导出时建议的文件名（两页不同：<c>pc-scripts-export.json</c> / <c>pc-quick-export.json</c>）。</summary>
    protected virtual string ExportSuggestedName => "pc-export.json";

    /// <summary>搜索框的悬停提示（「命令库」页原本没有 → 派生类覆盖成 null）。</summary>
    protected virtual string? SearchToolTip => "即时过滤（本地）";

    /// <summary>
    /// 夹在「搜索框」与「导入 / 导出」之间的**页专属**项（默认空）：
    /// 「脚本库」页放「📥 导入脚本」按钮 + 脚本目录只读框；「命令库」页没有。
    /// </summary>
    protected virtual IEnumerable<ToolbarItemDescriptor> ToolbarExtras => Array.Empty<ToolbarItemDescriptor>();

    /// <summary>
    /// 排在**共用搜索框之后**的页专属项（v3.61）：给「入口动作 / 输入框 / 搜索框」三件套后面还要挂按钮的页用
    /// （如 <c>script-library</c> 的「含子目录」；⛔ 别塞进 <see cref="ToolbarExtras"/>，否则会把搜索框挤到后面）。
    /// </summary>
    // ==================== 手动扫描（NT-068 §三.6：用户自己扫，⛔ 不自动登记） ====================

    /// <summary>扫描候选（页内候选面板的数据源）。</summary>
    public ObservableCollection<ScanCandidateItem> ScanCandidates { get; } = new();

    /// <summary>候选面板是否展开（有扫描结果才展开）。</summary>
    [ObservableProperty] private bool _hasScanResult;

    /// <summary>「加入库」是否可点 = 至少勾了一条新文件。</summary>
    public bool CanAddScanCandidates => ScanCandidates.Any(c => c.Selectable && c.IsSelected);

    /// <summary>🔍 扫描目录：只列候选，⛔ 不自动入库（用户 2026-10-17 拍板「用户自己扫描」）。</summary>
    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        if (_svc == null) return;
        var dir = await DialogHooks.PickFolderAsync("扫描脚本目录（只列候选，不会自动入库）", ScriptsDirectory);
        if (string.IsNullOrWhiteSpace(dir)) return;

        var found = _svc.Scan(dir, true);
        ScanCandidates.Clear();
        foreach (var c in found) ScanCandidates.Add(new ScanCandidateItem(c));

        HasScanResult = ScanCandidates.Count > 0;
        OnPropertyChanged(nameof(CanAddScanCandidates));
        OnPropertyChanged(nameof(Toolbar));

        var newCount = ScanCandidates.Count(c => c.Selectable);
        var known = ScanCandidates.Count(c => c.State == ScriptCandidate.StateExisting);
        var bad = ScanCandidates.Count(c => c.State == ScriptCandidate.StateIneligible);
        StatusText = ScanCandidates.Count == 0
            ? $"「{dir}」里没有 {string.Join(" / ", ScriptExtensions)} 这类脚本文件。"
            : $"扫描完成：新文件 {newCount} · 已入库 {known} · 不可采集 {bad}（勾选后点「➕ 加入库」）";
    }

    /// <summary>➕ 加入库：把勾选的新文件建成条目（复用 <see cref="ImportScriptFiles"/> 的路径归一与去重）。</summary>
    [RelayCommand]
    private void AddScanCandidates()
    {
        var files = ScanCandidates.Where(c => c.Selectable && c.IsSelected).Select(c => c.FullPath).ToList();
        if (files.Count == 0) { StatusText = "没有勾选任何新文件。"; return; }

        CloseScan();
        ImportScriptFiles(files);
    }

    /// <summary>✕ 收起候选面板。</summary>
    [RelayCommand]
    private void CloseScan()
    {
        ScanCandidates.Clear();
        HasScanResult = false;
        OnPropertyChanged(nameof(CanAddScanCandidates));
        OnPropertyChanged(nameof(Toolbar));
    }

    protected virtual IEnumerable<ToolbarItemDescriptor> ToolbarTailExtras => Array.Empty<ToolbarItemDescriptor>();

    /// <summary>
    /// v3.63：共用的「📥 导入」（json 清单）放哪儿 —— **true = 排最前**（「导入 → 输入框 → 搜索」三件套，
    /// 「命令库」页走这条）；**false = 排在「📤 导出」前面**（「脚本库」页：最前已经有一个「📂 导入」脚本文件了，
    /// 两个导入挨在一起会分不清）。
    /// </summary>
    protected virtual bool JsonImportFirst => true;

    /// <summary>共用的「📥 导入」按钮（从 json 文件导入本页清单：合并 / 整页替换两路菜单）。</summary>
    private static ToolbarItemDescriptor JsonImportItem => new()
    {
        // v3.63：与「📤 导出」同一口径（emoji + 一个词），⛔ 不再写「导入清单」这类后缀
        Text = "📥 导入",
        Classes = "ghost",
        ToolTip = "从 json 文件导入本页清单（合并 / 整页替换）",
        Items = new ToolbarMenuItemDescriptor[]
        {
            new() { Text = "🔀 合并导入（按 Id 去重，同 Id 覆盖）", CommandPath = nameof(ImportPageMergeCommand) },
            new() { Text = "♻ 整页替换（先清空本页再导入）", CommandPath = nameof(ImportPageReplaceCommand) },
        }
    };

    /// <summary>
    /// 工具条（NT-055 / 需求 §4.8.5）：页面⛔ 不再自写 <c>Border.toolbar</c>，改由 Shell 渲染本描述符。
    /// <para>
    /// 顺序按「全部资产」同一口径：**入口动作（<see cref="ToolbarExtras"/>）→ 输入框 → 搜索框**，
    /// 页专属项在前，共用搜索框紧随其后，末尾是共用的「导入 / 导出 + 清空本页」。
    /// </para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar
    {
        get
        {
            var items = new List<ToolbarItemDescriptor>();

            if (JsonImportFirst) items.Add(JsonImportItem);

            // 页专属项（入口动作 / 输入框）在前，共用的搜索框紧随其后（与「全部资产」同一口径）。
            items.AddRange(ToolbarExtras);

            items.Add(new()
            {
                Kind = ToolbarItemKind.TextField,
                Width = 88,
                Placeholder = "搜索名称 / 分类 / 简介…",
                ValuePath = nameof(SearchText),
                ToolTip = SearchToolTip
            });

            // 搜索框之后的页专属项（⛔ 别塞进 ToolbarExtras —— 那会把搜索框从「入口动作 / 输入框」后面挤开）。
            items.AddRange(ToolbarTailExtras);

            // NT-068 S3：扫描候选面板的两个动作（只在有候选时出现，⛔ 不占常态工具条）。
            if (HasScanResult)
            {
                items.Add(new()
                {
                    Text = "➕ 加入库",
                    Classes = "ghost",
                    CommandPath = nameof(AddScanCandidatesCommand),
                    ToolTip = "把勾选的新文件建成条目（已入库 / 不可采集的不会重复添加）"
                });
                items.Add(new()
                {
                    Text = "✕ 收起",
                    Classes = "ghost",
                    CommandPath = nameof(CloseScanCommand),
                    ToolTip = "收起扫描结果面板"
                });
            }

            // v3.63：不排在最前的页 ⇒ 「📥 导入」插到「📤 导出」前面
            if (!JsonImportFirst) items.Add(JsonImportItem);

            items.AddRange(new ToolbarItemDescriptor[]
            {
                new()
                {
                    Text = "📤 导出",
                    Classes = "ghost",
                    CommandPath = nameof(ExportPageCommand),
                    ToolTip = "把本页全条目导出为 json 文件"
                },
                new()
                {
                    Text = "🧹 清空本页",
                    Classes = "danger-btn",
                    CommandPath = nameof(ClearPageCommand),
                    IsEnabledPath = "!" + nameof(IsBusy),
                    ToolTip = "删掉本页全部条目并写盘（真删除，二次确认；建议先导出备份）"
                },
            });

            return items;
        }
    }

    /// <summary>📤 导出：把本页全条目存成 json 文件（NT-055：原 code-behind 的 Click 搬进 VM，选择器走 DialogHooks）。</summary>
    [RelayCommand]
    private async Task ExportPageAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出清单", ImportExportDefaultDir, ExportSuggestedName, "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, ExportPageJson());
            StatusText = $"已导出本页全条目到：{path}";
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>📥 合并导入（按 Id 去重，同 Id 覆盖）。</summary>
    [RelayCommand]
    private Task ImportPageMergeAsync() => ImportPageFromFileAsync(replace: false);

    /// <summary>📥 整页替换导入（先清空本页再导入）。</summary>
    [RelayCommand]
    private Task ImportPageReplaceAsync() => ImportPageFromFileAsync(replace: true);

    /// <summary>📥 导入：选 json 文件 → 合并 / 整页替换导入本页（NT-055：原 code-behind 的 Click 搬进 VM）。</summary>
    private async Task ImportPageFromFileAsync(bool replace)
    {
        var files = await DialogHooks.PickFilesAsync(
            replace ? "选择要整页替换导入的清单" : "选择要合并导入的清单",
            ImportExportDefaultDir, false, "*.json");
        var path = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;

        try { StatusText = ImportPageJson(File.ReadAllText(path), replace); }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

    // ==================== 整页导入 / 导出（v3.19 / NT-006、NT-007；NT-055 收尾后控件样式由 Shell 的 ToolbarHost 统一） ====================

    /// <summary>导出本页**全条目** json 文本（供 View「📤 导出」存盘）。</summary>
    public string ExportPageJson() => _svc?.ExportPage(_pageKind) ?? "[]";

    /// <summary>导入本页（View「📥 导入」读文件后调用）；<paramref name="replace"/>=true 整页替换、false 合并。</summary>
    public string ImportPageJson(string json, bool replace)
    {
        if (_svc == null) return "服务不可用。";
        var (added, updated, skipped) = _svc.ImportPage(_pageKind, json, replace);
        if (skipped < 0) return "⚠ 导入失败：选中的文件不是有效的清单 json。";

        RebuildCategories();
        ResetFilters();
        ApplyView();

        var parts = new List<string>();
        if (added > 0) parts.Add($"新增 {added} 条");
        if (updated > 0) parts.Add($"更新 {updated} 条");
        if (skipped > 0) parts.Add($"跳过 {skipped} 条无效");
        var summary = parts.Count > 0 ? string.Join("，", parts) : "没有可导入的条目";
        return (replace ? "已整页替换：" : "已合并导入：") + summary + $"（写入 {FilePath}）";
    }

    /// <summary>🧹 清空本页（v3.19 / NT-006，方案③：**真删除并落盘**）：删掉本页全部条目，二次确认。</summary>
    [RelayCommand]
    private async Task ClearPageAsync()
    {
        if (_svc == null) return;

        var count = _svc.Items.Count(BelongsToThisPage);
        if (count == 0) { StatusText = $"「{PageDisplayName}」已经是空的。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            $"确定清空「{PageDisplayName}」全部 {count} 条吗？\n会写入 {FilePath}（真删除，不可撤销；建议先「📤 导出」备份）。").ConfigureAwait(true);
        if (!ok) { StatusText = "已取消清空。"; return; }

        IsBusy = true;
        try
        {
            _svc.ClearPage(_pageKind);
            Selected = null;
            RebuildCategories();
            ApplyView();
            StatusText = $"已清空「{PageDisplayName}」{count} 条（写入 {FilePath}）。";
        }
        finally { IsBusy = false; }
    }

    // ==================== 增删改查 ====================

    /// <summary>新增：先清掉过滤（保证新项一定可见），追加一条并选中它，改好后点「💾 保存」。</summary>
    [RelayCommand]
    private void Add()
    {
        if (_svc == null) return;

        _suppress = true;
        SearchText = "";
        if (CategoryOptions.Count > 0) SelectedCategory = CategoryOptions[0];
        _suppress = false;
        ResetFilters();   // 清掉筛选，保证新项一定看得见

        var item = _svc.Add(_pageKind);
        RebuildCategories();
        ApplyView();
        Selected = Items.FirstOrDefault(i => ReferenceEquals(i, item)) ?? item;
        StatusText = $"已在「{PageDisplayName}」新增一条「{item.Title}」，请在右侧改名 / 选类型 / 填命令或脚本文件后点「💾 保存」。";
    }

    /// <summary>保存：把编辑缓冲写回选中对象并落盘。</summary>
    [RelayCommand]
    private void Save()
    {
        if (Selected == null || _svc == null) return;

        var title = (EditTitle ?? "").Trim();
        if (title.Length == 0) { StatusText = "名称不能为空。"; return; }

        Selected.Title = title;
        Selected.Category = (EditCategory ?? "").Trim();
        Selected.Description = (EditDescription ?? "").Trim();
        Selected.Command = (EditCommand ?? "").Trim();
        Selected.NeedAdmin = EditNeedAdmin;
        Selected.RunHidden = EditRunHidden;
        Selected.Shell = QuickActionShell.Normalize(EditShell);
        Selected.Kind = string.IsNullOrWhiteSpace(EditKind) ? QuickActionKind.Command : EditKind;
        Selected.ScriptFile = (EditScriptFile ?? "").Trim();
        Selected.Interpreter = EditInterpreter ?? ScriptInterpreter.Auto;

        _svc.Save();

        // 归属铁律（v1.34）：「脚本库」只放**有脚本文件的脚本**。
        // 本页保存后若它不满足条件（类型不是脚本 / 没填脚本文件），自动挪去「命令库」并如实告知。
        var moved = false;
        if (_pageKind != QuickActionPage.Quick
            && (Selected.Kind != QuickActionKind.Script || Selected.ScriptFile.Length == 0))
        {
            var movedTitle = Selected.Title;
            _svc.Move(Selected, QuickActionPage.Quick);
            Selected = null;
            moved = true;
            StatusText = $"「{movedTitle}」没有脚本文件 → 已归入「命令库」（本页只放有脚本文件的脚本）。";
        }

        RebuildCategories();
        ApplyView();

        if (!moved) StatusText = $"已保存到 {FilePath}";
    }

    /// <summary>把选中项移到另一个页面（「脚本库」↔「命令库」，各写各的文件）。</summary>
    [RelayCommand]
    private void MoveToOtherPage()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        _svc.Move(item, _pageKind == QuickActionPage.Quick ? QuickActionPage.Shortcuts : QuickActionPage.Quick);
        Selected = null;
        RebuildCategories();
        ApplyView();
        StatusText = $"已把「{item.Title}」移到「{OtherPageName}」。";
    }

    /// <summary>
    /// 🗑 删除前是否弹二次确认（v3.63）：
    /// 「脚本库」= **索引库**，删的只是清单里的一行（脚本文件还在磁盘上）⇒ false，不打扰；
    /// 「命令库」= **json 里的真数据**，删掉就没了 ⇒ true（派生类 override）。
    /// </summary>
    protected virtual bool ConfirmBeforeDelete => false;

    /// <summary>
    /// 🗑 **从列表删除**：把这条从清单里去掉并落盘（⛔ 从不碰磁盘文件；删脚本文件走右键「🔥 从磁盘删除…」）。
    /// <para>是否弹确认框由 <see cref="ConfirmBeforeDelete"/> 决定。</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        if (ConfirmBeforeDelete)
        {
            var ok = await DialogHooks.ConfirmAsync(
                $"确定删除「{item.Title}」吗？\n\n这条是 {FilePath} 里的记录，删掉后不可撤销（磁盘上的文件不会动）。");
            if (!ok) { StatusText = "已取消删除。"; return; }
        }

        if (!RemoveFromList(item)) { StatusText = "这条不在清单里，没有可移除的。"; return; }
        StatusText = ConfirmBeforeDelete
            ? $"已删除：{item.Title}"
            : $"已从列表移除：{item.Title}（不删磁盘文件）";
    }

    /// <summary>数据服务是否可用（派生类要做"没有服务就别动磁盘"这类判断时用）。</summary>
    protected bool HasService => _svc != null;

    /// <summary>
    /// 把一条从清单里移除并刷新（⛔ 只动清单、**不碰磁盘文件**、不弹确认）。
    /// <para>「从磁盘删除」那一路由派生类先删文件、再调本方法收尾。</para>
    /// </summary>
    /// <returns>真的移掉了才 true（不在清单里 ⇒ false）。</returns>
    protected bool RemoveFromList(QuickAction item)
    {
        if (_svc == null || item == null) return false;
        if (!_svc.RemoveSilent(item)) return false;

        Selected = null;
        RebuildCategories();
        ApplyView();
        return true;
    }

    /// <summary>切换收藏（收藏夹页读的是同一批对象，天然同步）。</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        _svc.ToggleFavorite(item);
        UpdateFavoriteUi(item);
        // 图标组里收藏是**一个图标 + IsActivePath** ⇒ 切换后要让高亮重算
        OnPropertyChanged(nameof(IsSelectedFavorite));
        OnPropertyChanged(nameof(IsSelectedNotFavorite));

        ApplyViewKeepSelection();

        StatusText = item.IsFavorite ? $"已收藏：{item.Title}" : $"已取消收藏：{item.Title}";
    }

    /// <summary>复制命令内容到剪贴板。</summary>
    [RelayCommand]
    private async Task Copy()
    {
        var cmd = Selected?.Command;
        if (string.IsNullOrWhiteSpace(cmd)) { StatusText = "这条还没有命令内容。"; return; }
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(cmd);
            StatusText = "已复制命令到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    // ==================== 右键「编辑」脚本（v3.19 / NT-007，需求 §6.2.2） ====================

    /// <summary>选中项是否可「编辑」：是脚本类型且脚本文件已在磁盘上。</summary>
    public bool CanEditScript => Selected != null
        && string.Equals(Selected.Kind, QuickActionKind.Script, StringComparison.OrdinalIgnoreCase)
        && File.Exists(ResolveScriptPath(Selected.ScriptFile));

    /// <summary>把脚本文件字段（相对脚本目录或绝对路径）解析成磁盘全路径；空则返回空串。</summary>
    protected string ResolveScriptPath(string? scriptFile)
    {
        var file = (scriptFile ?? "").Trim();
        if (file.Length == 0) return "";
        return Path.IsPathRooted(file) ? file : Path.Combine(ScriptsDirectory, file);
    }

    /// <summary>📝 用记事本编辑选中脚本文件。</summary>
    [RelayCommand]
    private void EditWithNotepad()
    {
        var item = Selected;
        if (item == null) { StatusText = "先选中一条。"; return; }
        var path = ResolveScriptPath(item.ScriptFile);
        if (!File.Exists(path)) { StatusText = $"找不到脚本文件：{(path.Length > 0 ? path : "（未填）")}"; return; }
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            StatusText = $"已用记事本打开：{path}";
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>🧰 用系统「打开方式」打开选中脚本文件（选外部程序）。</summary>
    [RelayCommand]
    private void OpenWith()
    {
        var item = Selected;
        if (item == null) { StatusText = "先选中一条。"; return; }
        var path = ResolveScriptPath(item.ScriptFile);
        if (!File.Exists(path)) { StatusText = $"找不到脚本文件：{(path.Length > 0 ? path : "（未填）")}"; return; }
        try
        {
            // verb=openas：弹出系统「打开方式」对话框，让用户选用哪个程序打开。
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" });
            StatusText = $"已弹出「打开方式」：{path}";
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    private void UpdateFavoriteUi(QuickAction? item)
    {
        var fav = item?.IsFavorite == true;
        FavoriteMenuLabel = fav ? "✕ 取消收藏" : "★ 添加到收藏夹";
        OnPropertyChanged(nameof(FavoriteButtonText));
    }
}
