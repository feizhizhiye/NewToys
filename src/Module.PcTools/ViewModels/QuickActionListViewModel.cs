using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.PcTools.Models;
using NewToys.PcTools.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

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
/// 「电脑工具」里两个<b>同类列表页</b>（「设置与脚本」与「快捷设置」）共用的 ViewModel。
/// <para>
/// 两页的布局 / 交互<b>完全一致</b>（规范 3.6）：左「可排序表头 + 列表 + 行内「▶ 运行」」，右「选中项属性卡」，
/// 列表上挂右键菜单（运行 / 收藏 / 复制命令 / 删除）。差别只有三点，由派生类给出：
/// ① <see cref="PageKind"/>：本页显示哪一批条目（**每页一份清单文件**，见 <c>FilePath</c>）；
/// ② <see cref="ColumnLayoutKey"/>：列显隐偏好落 <c>config\columnlayout.json</c> 时用的页键；
/// ③ 页面名字 <see cref="PageDisplayName"/>（提示文案用）。
/// <b>新增项的默认类型</b>由 <see cref="PageKind"/> 推出（见 <c>IQuickActionService.Add</c>）：
/// 「快捷设置」= 命令行命令、「设置与脚本」= 脚本文件（v1.33）。
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
public abstract partial class QuickActionListViewModel : ListPageViewModelBase
{
    private readonly IQuickActionService? _svc;
    private readonly IAppConfigService? _app;

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
    [ObservableProperty] private string _categoryHeader = "分类";
    [ObservableProperty] private string _favoriteHeader = "★";

    // ==================== 列显隐（基类管状态，这里只给 XAML 提供具名属性） ====================

    public bool IsTitleVisible => Vis("Title");
    public bool IsCategoryVisible => Vis("Category");
    public bool IsDescriptionVisible => Vis("Description");
    public bool IsFavoriteVisible => Vis("Favorite");
    public bool IsActionVisible => Vis("Action");

    protected override string PageKey => ColumnLayoutKey;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。
    /// <para>最后一列 <c>Detail</c> 是**表头右侧的那个无文字小按钮**用的固定窄列（规范 3.6）：
    /// 不参与列显隐菜单、也不进 <c>columnlayout.json</c>，它只是把表头与行的最后一格对齐留出来。</para></summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Title", "名称", "3*"),
        new("Category", "分类", "1.3*"),
        new("Description", "简介", "4*", sortable: false),
        new("Favorite", "★", "0.9*"),
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

    /// <summary>是否显示 Shell / 隐藏终端窗口 两个运行选项（有选中且是命令行命令时，v3.19 / NT-006）。</summary>
    public bool ShowCommandRunOptions => HasSelection && IsCommandKind;

    // ==================== 脚本目录（v1.35：顶部可切换） ====================

    /// <summary>脚本目录（生效值），详情卡里提示"文件放哪"。</summary>
    public string ScriptsDirectory => _svc?.ScriptsDirectory ?? "";

    /// <summary>脚本目录的默认值（配置留空时用的那个）。</summary>
    public string DefaultScriptFolder => _app?.DefaultScriptFolder ?? "";

    /// <summary>脚本目录当前是不是"用默认值"。</summary>
    public bool IsDefaultScriptFolder
        => string.IsNullOrWhiteSpace(_app?.ScriptFolder);

    /// <summary>
    /// 顶部「脚本目录」用文件选择器选完之后由 View 的 code-behind 调用：这是**明确的动作** → 立即生效并落盘
    /// （不用再点保存；避免"选了却没生效"的困惑，与「设置 → 路径配置」同一条规则）。
    /// </summary>
    public void ApplyPickedScriptFolder(string path)
    {
        if (_app == null || string.IsNullOrWhiteSpace(path)) return;

        _app.ScriptFolder = path.Trim();
        StatusText = $"脚本目录已设为：{ScriptsDirectory}（写入 app.json，立即生效）";
        OnPropertyChanged(nameof(ScriptsDirectory));
        OnPropertyChanged(nameof(IsDefaultScriptFolder));
        OnPropertyChanged(nameof(ScriptHint));
    }

    /// <summary>恢复成默认脚本目录（<c>&lt;应用根&gt;\scripts</c>）。</summary>
    [RelayCommand]
    private void ResetScriptFolder()
    {
        if (_app == null) return;
        _app.ScriptFolder = "";
        StatusText = $"已恢复默认脚本目录：{DefaultScriptFolder}";
        OnPropertyChanged(nameof(ScriptsDirectory));
        OnPropertyChanged(nameof(IsDefaultScriptFolder));
        OnPropertyChanged(nameof(ScriptHint));
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

    /// <summary>
    /// 详情卡「📂」按钮选好脚本文件后调用：把路径填进编辑缓冲。
    /// <para>落在脚本目录里的文件<b>存相对文件名</b>（换机器 / 换目录也能跑），目录外的存绝对路径。</para>
    /// </summary>
    public void ApplyPickedScriptFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var dir = ScriptsDirectory;
        EditScriptFile = !string.IsNullOrWhiteSpace(dir)
                         && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
            ? path[dir.Length..].TrimStart('\\', '/')
            : path;
        StatusText = $"已选择脚本文件：{EditScriptFile}（点「💾 保存」写盘）";
    }

    /// <summary>脚本字段下方的提示（解释器怎么挑 + 脚本目录在哪）。</summary>
    public string ScriptHint =>
        $"脚本文件 = 脚本目录里的 .py / .ps1 / .bat（可填相对文件名，也可填绝对路径）；点「📂」可以直接选文件。\n脚本目录：{ScriptsDirectory}" +
        (IsDefaultScriptFolder ? "（默认值）\n" : "\n") +
        "解释器留「自动」即可：.py→python、.ps1→pwsh（没配则退回 5.1）、.bat/.cmd→cmd；三个解释器路径在「设置 → 路径配置」里配，留空 = 用系统默认。";

    // ==================== 派生显示 ====================

    /// <summary>右键菜单里「收藏 / 取消收藏」的动态文案。</summary>
    [ObservableProperty] private string _favoriteMenuLabel = "★ 添加到收藏夹";

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>属性卡里收藏按钮的文案。</summary>
    public string FavoriteButtonText => Selected?.IsFavorite == true ? "★ 取消收藏" : "☆ 收藏";

    /// <summary>底部提示：数据落在哪、会不会被备份（规范 §9）。
    /// 两页各有自己的文件（v1.34）：「设置与脚本」= <c>pc-scripts.json</c>、「快捷设置」= <c>pc-quick.json</c>。</summary>
    public string DataFileHint =>
        $"本页数据文件：{FilePath}\n（两个页面的文件都在 config\\ 下，会被「设置 → 数据管理」的备份 / 导入一起覆盖）";

    /// <summary>本页的配置文件全路径（两页各一份，v1.34 拆文件）。</summary>
    public string FilePath => _pageKind == QuickActionPage.Quick
        ? _svc?.QuickFilePath ?? ""
        : _svc?.ScriptsFilePath ?? "";

    /// <summary>另一个页面的名字（右键「移动到…」的提示文案用）。</summary>
    public string OtherPageName => _pageKind == QuickActionPage.Quick ? "设置与脚本" : "快捷设置";

    /// <summary>右键菜单里「移到另一页」的动态文案。</summary>
    public string MoveMenuLabel => _pageKind == QuickActionPage.Quick
        ? "➡ 移到「设置与脚本」"
        : "➡ 移到「快捷设置」（本页只放有脚本文件的脚本）";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    protected QuickActionListViewModel()
    {
        _pageKind = QuickActionPage.Shortcuts;
    }

    protected QuickActionListViewModel(IQuickActionService svc, IColumnLayoutService cols, IAppConfigService app)
        : base(cols)
    {
        _svc = svc;
        _app = app;
        // 抽象属性在基类构造里读一次存字段：避免「构造期虚调用」反复触发，也让后续逻辑读到稳定值。
        _pageKind = PageKind;
        LoadLayout();
        RebuildCategories();
        ApplyView();
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
        CategoryOptions.Add(new ActionCategoryOption { Label = "全部分类", Value = null });
        foreach (var c in source.Select(i => i.Category)
                                .Where(c => !string.IsNullOrWhiteSpace(c))
                                .Distinct()
                                .OrderBy(c => c, StringComparer.CurrentCulture))
            CategoryOptions.Add(new ActionCategoryOption { Label = c, Value = c });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == keep) ?? CategoryOptions[0];
        _suppress = false;
    }

    /// <summary>本条是否属于本页（「设置与脚本」= 非快捷设置；「快捷设置」= Quick）。</summary>
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
        CategoryHeader = "分类" + SortArrow("Category");
        FavoriteHeader = "★" + SortArrow("Favorite");
    }

    // ==================== 过滤 ====================

    partial void OnSearchTextChanged(string value) { if (!_suppress) ApplyView(); }

    partial void OnSelectedCategoryChanged(ActionCategoryOption? value) { if (!_suppress) ApplyView(); }

    partial void OnSelectedChanged(QuickAction? value)
    {
        // 选中项变了 → 把字段灌进编辑缓冲（点「💾 保存」才写回对象并落盘）。
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

        UpdateFavoriteUi();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowCommandRunOptions));
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

    /// <summary>认得的脚本扩展名（小写，带点）。批量导入时只挑这些。</summary>
    public static readonly string[] ScriptExtensions = { ".py", ".ps1", ".bat", ".cmd" };

    /// <summary>导入子目录里的脚本吗（View 上的勾选框直接读它）。</summary>
    [ObservableProperty] private bool _importSubFolders = true;

    /// <summary>上一次导入的结论（状态行就是它）。</summary>
    public string ImportResultText { get; private set; } = "";

    private static string ScriptExtensionsList => string.Join(" / ", ScriptExtensions);

    /// <summary>
    /// 批量导入：把一个目录里的脚本文件**全部建成条目**。
    /// <para>
    /// 规则（都写进状态行，绝不"悄悄跳过"）：
    /// ① 只收 <see cref="ScriptExtensions"/> 里的扩展名；
    /// ② 清单里已有的（按脚本文件名字比对）**跳过**，不重复建；
    /// ③ 落在脚本目录内的存**相对文件名**、目录外的存绝对路径 —— 与手动「📂」选文件保持一致；
    /// ④ 名称取文件名（去扩展名）、分类填「导入」，方便之后批量改。
    /// </para>
    /// </summary>
    /// <returns>(新增几条, 跳过几条)。</returns>
    public (int Added, int Skipped) ImportScriptFolder(string folder, bool includeSubFolders = true)
    {
        if (_svc == null) return (0, 0);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return (0, 0);

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*",
                        includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .ToList();   // 先固化，避免枚举中途目录变动
        }
        catch { return (0, 0); }

        var scriptsDir = ScriptsDirectory;
        var existing = new HashSet<string>(
            _svc.Items.Select(i => (i.ScriptFile ?? "").Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            if (!ScriptExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;

            var stored = !string.IsNullOrWhiteSpace(scriptsDir)
                         && file.StartsWith(scriptsDir, StringComparison.OrdinalIgnoreCase)
                ? file[scriptsDir.Length..].TrimStart('\\', '/')
                : file;

            if (existing.Contains(stored)) { skipped++; continue; }

            var item = _svc.Add(_pageKind);
            item.Kind = QuickActionKind.Script;
            item.Title = Path.GetFileNameWithoutExtension(file);
            item.Category = "导入";
            item.Description = $"从目录导入：{Path.GetFileName(file)}";
            item.ScriptFile = stored;
            item.Interpreter = ScriptInterpreter.Auto;
            existing.Add(stored);
            added++;
        }

        if (added > 0) _svc.Save();
        RebuildCategories();
        ResetFilters();          // 新导入的脚本可能正被筛选条件挡着 → 清掉筛选，保证看得见
        ApplyView();

        ImportResultText = added == 0
            ? $"「{folder}」里没有可导入的新脚本（只认 {ScriptExtensionsList}）"
              + (skipped > 0 ? $"；{skipped} 个已在清单里，已跳过" : "")
            : $"已导入 {added} 个脚本"
              + (skipped > 0 ? $"；{skipped} 个已存在，已跳过" : "");
        StatusText = ImportResultText;
        return (added, skipped);
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

        // 归属铁律（v1.34）：「设置与脚本」只放**有脚本文件的脚本**。
        // 本页保存后若它不满足条件（类型不是脚本 / 没填脚本文件），自动挪去「快捷设置」并如实告知。
        var moved = false;
        if (_pageKind != QuickActionPage.Quick
            && (Selected.Kind != QuickActionKind.Script || Selected.ScriptFile.Length == 0))
        {
            var movedTitle = Selected.Title;
            _svc.Move(Selected, QuickActionPage.Quick);
            Selected = null;
            moved = true;
            StatusText = $"「{movedTitle}」没有脚本文件 → 已归入「快捷设置」（本页只放有脚本文件的脚本）。";
        }

        RebuildCategories();
        ApplyView();

        if (!moved) StatusText = $"已保存到 {FilePath}";
    }

    /// <summary>把选中项移到另一个页面（「设置与脚本」↔「快捷设置」，各写各的文件）。</summary>
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

    /// <summary>删除：内部走二次确认（钩子在宿主接到模态框上）。</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        IsBusy = true;
        try
        {
            var ok = await _svc.RemoveAsync(item);
            if (!ok) { StatusText = "已取消删除。"; return; }
            Selected = null;
            RebuildCategories();
            ApplyView();
            StatusText = $"已删除：{item.Title}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>切换收藏（收藏夹页读的是同一批对象，天然同步）。</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        var item = Selected;
        if (item == null || _svc == null) return;

        _svc.ToggleFavorite(item);
        UpdateFavoriteUi();
        ApplyView();
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

    private void UpdateFavoriteUi()
    {
        var fav = Selected?.IsFavorite == true;
        FavoriteMenuLabel = fav ? "✕ 取消收藏" : "★ 添加到收藏夹";
        OnPropertyChanged(nameof(FavoriteButtonText));
    }
}
