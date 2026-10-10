using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// ① <see cref="PageKind"/>：本页显示哪一批条目（同属 <c>config\quickactions.json</c>，按 <see cref="QuickAction.Page"/> 分栏）；
/// ② <see cref="ColumnLayoutKey"/>：列显隐偏好落 <c>config\columnlayout.json</c> 时用的页键；
/// ③ 页面名字 <see cref="PageDisplayName"/>（提示文案用）。
/// <b>新增项的默认类型</b>由 <see cref="PageKind"/> 推出（见 <c>IQuickActionService.Add</c>）：
/// 「快捷设置」= 命令行命令、「设置与脚本」= 脚本文件（v1.33）。
/// </para>
/// <para>
/// ⚠️ 这里的「设置」是<b>操作系统设置项</b>，不是本软件自己的设置（后者在「设置」模块，见规范 3.9）。
/// </para>
/// <para>
/// 收藏 = 每条 <see cref="QuickAction.IsFavorite"/> 上的标记（单一真相源），
/// 「收藏夹」页读的是同一批对象，因此这里右键收藏后那边天然一致。
/// </para>
/// </summary>
public abstract partial class QuickActionListViewModel : ObservableObject
{
    private readonly IQuickActionService? _svc;
    private readonly IColumnLayoutService? _cols;

    /// <summary>本页归属的 <see cref="QuickAction.Page"/>（<see cref="QuickActionPage"/> 之一）。</summary>
    private readonly string _pageKind;

    /// <summary>列显隐偏好在 <c>config\columnlayout.json</c> 里的键（如 <c>"pc-shortcuts"</c>）。</summary>
    private readonly string _columnLayoutKey;

    private string _sortColumn = "Title";
    private bool _sortDescending;

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

    [ObservableProperty] private string _statusText = "就绪";
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

    // ==================== 列显隐（右键表头增删列，规范 3.6） ====================

    [ObservableProperty] private bool _isTitleVisible = true;
    [ObservableProperty] private bool _isCategoryVisible = true;
    [ObservableProperty] private bool _isDescriptionVisible = true;
    [ObservableProperty] private bool _isFavoriteVisible = true;
    [ObservableProperty] private bool _isActionVisible = true;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。</summary>
    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Title", "名称", "3*"),
        new("Category", "分类", "1.3*"),
        new("Description", "简介", "4*", sortable: false),
        new("Favorite", "★", "0.9*"),
        new("Action", "操作", "1.5*", sortable: false),
    };

    /// <summary>列宽规格串（可绑定）：可见列写星号宽度，隐藏列写 "0"（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串而不是 <c>ColumnDefinitions</c>：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，
    /// Avalonia XAML 不允许在它上面写 {Binding}（编译期 AVLN3000），故由 <c>ColumnGrid</c> 控件解析（规范 3.6）。</summary>
    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                parts[i] = IsVisibleFor(Columns[i].Key) ? Columns[i].Width : "0";
            return string.Join(",", parts);
        }
    }

    private bool IsVisibleFor(string key) => key switch
    {
        "Title" => IsTitleVisible,
        "Category" => IsCategoryVisible,
        "Description" => IsDescriptionVisible,
        "Favorite" => IsFavoriteVisible,
        "Action" => IsActionVisible,
        _ => true,
    };

    /// <summary>右键表头勾选 / 取消勾选某列（增删表头项）。</summary>
    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        switch (key)
        {
            case "Title": IsTitleVisible = !IsTitleVisible; break;
            case "Category": IsCategoryVisible = !IsCategoryVisible; break;
            case "Description": IsDescriptionVisible = !IsDescriptionVisible; break;
            case "Favorite": IsFavoriteVisible = !IsFavoriteVisible; break;
            case "Action": IsActionVisible = !IsActionVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>把当前列显隐偏好落盘（config\columnlayout.json，会被数据管理一起备份）。</summary>
    private void SaveColumnVisibility()
    {
        if (_cols == null) return;
        _cols.SavePage(_columnLayoutKey, new Dictionary<string, bool>
        {
            ["Title"] = IsTitleVisible, ["Category"] = IsCategoryVisible,
            ["Description"] = IsDescriptionVisible, ["Favorite"] = IsFavoriteVisible,
            ["Action"] = IsActionVisible,
        });
    }

    /// <summary>进入页面时读回上次保存的列显隐偏好（未保存过的列保持默认「可见」）。</summary>
    private void LoadColumnVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(_columnLayoutKey);
        if (saved.TryGetValue("Title", out var t)) IsTitleVisible = t;
        if (saved.TryGetValue("Category", out var c)) IsCategoryVisible = c;
        if (saved.TryGetValue("Description", out var d)) IsDescriptionVisible = d;
        if (saved.TryGetValue("Favorite", out var f)) IsFavoriteVisible = f;
        if (saved.TryGetValue("Action", out var a)) IsActionVisible = a;
    }

    // ==================== 右栏详情开合（默认收起 → 列表占满宽度） ====================

    /// <summary>
    /// 是否显示右侧属性卡。<b>默认收起</b>（空列表也能多看几行）；用户的点选会落
    /// <c>config\columnlayout.json</c>，下次进本页保持上次的选择。
    /// </summary>
    [ObservableProperty] private bool _isDetailVisible;

    /// <summary>切换按钮上的文字（收起时提示"详情"，展开时提示"收起"）。</summary>
    public string DetailButtonText => IsDetailVisible ? "▤ 收起详情" : "▤ 详情";

    partial void OnIsDetailVisibleChanged(bool value) => OnPropertyChanged(nameof(DetailButtonText));

    /// <summary>点一下展开 / 收起右栏详情（"点一下就看到结果"的轻偏好 → 立即落盘）。</summary>
    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        _cols?.SaveFlag(_columnLayoutKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);
        OnPropertyChanged(nameof(DetailButtonText));
    }

    // ==================== 属性卡的编辑缓冲（多字段文本表单 → 显式「💾 保存」，规范 3.5） ====================

    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editCategory = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editCommand = "";
    [ObservableProperty] private bool _editNeedAdmin;

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
    }

    /// <summary>当前编辑的是"脚本文件"类型 → 详情卡显示脚本字段、隐藏命令框（反之亦然）。</summary>
    public bool IsScriptKind => string.Equals(EditKind, QuickActionKind.Script, StringComparison.Ordinal);

    public bool IsCommandKind => !IsScriptKind;

    /// <summary>脚本目录（生效值），详情卡里提示"文件放哪"。</summary>
    public string ScriptsDirectory => _svc?.ScriptsDirectory ?? "";

    /// <summary>脚本字段下方的提示（解释器怎么挑 + 脚本目录在哪）。</summary>
    public string ScriptHint =>
        $"脚本文件 = 脚本目录里的 .py / .ps1 / .bat（可填相对文件名，也可填绝对路径）。\n脚本目录：{ScriptsDirectory}\n" +
        "解释器留「自动」即可：.py→python、.ps1→pwsh（没配则退回 5.1）、.bat/.cmd→cmd；三个解释器路径在「设置 → 路径配置」里配，留空 = 用系统默认。";

    // ==================== 派生显示 ====================

    /// <summary>右键菜单里「收藏 / 取消收藏」的动态文案。</summary>
    [ObservableProperty] private string _favoriteMenuLabel = "★ 添加到收藏夹";

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>属性卡里收藏按钮的文案。</summary>
    public string FavoriteButtonText => Selected?.IsFavorite == true ? "★ 取消收藏" : "☆ 收藏";

    /// <summary>底部提示：数据落在哪、会不会被备份（规范 3.9）。</summary>
    public string DataFileHint =>
        $"数据文件：{FilePath}\n（放在 config\\ 下，会被「设置 → 数据管理」的备份 / 导入一起覆盖）";

    /// <summary>配置文件全路径。</summary>
    public string FilePath => _svc?.FilePath ?? "";

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    protected QuickActionListViewModel()
    {
        _pageKind = QuickActionPage.Shortcuts;
        _columnLayoutKey = "pc-shortcuts";
    }

    protected QuickActionListViewModel(IQuickActionService svc, IColumnLayoutService cols)
    {
        _svc = svc;
        _cols = cols;
        // 抽象属性在基类构造里读一次存字段：避免「构造期虚调用」反复触发，也让后续逻辑读到稳定值。
        _pageKind = PageKind;
        _columnLayoutKey = ColumnLayoutKey;
        LoadColumnVisibility();
        IsDetailVisible = cols.LoadFlag(_columnLayoutKey, IColumnLayoutService.FlagDetailPanel);   // 默认收起
        RebuildCategories();
        ApplyView();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

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

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = _sortColumn switch
        {
            "Category" => _sortDescending ? q.OrderByDescending(i => i.Category, byText) : q.OrderBy(i => i.Category, byText),
            "Favorite" => _sortDescending ? q.OrderByDescending(i => i.IsFavorite) : q.OrderBy(i => i.IsFavorite),
            _ => _sortDescending ? q.OrderByDescending(i => i.Title, byText) : q.OrderBy(i => i.Title, byText),
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
        var arrow = _sortDescending ? " ▼" : " ▲";
        TitleHeader = "名称" + (_sortColumn == "Title" ? arrow : "");
        CategoryHeader = "分类" + (_sortColumn == "Category" ? arrow : "");
        FavoriteHeader = "★" + (_sortColumn == "Favorite" ? arrow : "");
    }

    // ==================== 过滤 / 排序（左键点表头） ====================

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

        // 类型 / 解释器用下拉的选中项承载（赋值会经 OnSelected*Changed 回写上面两个缓冲字段）。
        var kind = value?.Kind ?? QuickActionKind.Command;
        var interp = value?.Interpreter ?? ScriptInterpreter.Auto;
        SelectedKind = KindOptions.FirstOrDefault(o => string.Equals(o.Value, kind, StringComparison.OrdinalIgnoreCase))
                       ?? KindOptions[0];
        SelectedInterpreter = InterpreterOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", interp ?? "", StringComparison.OrdinalIgnoreCase))
                              ?? InterpreterOptions[0];

        UpdateFavoriteUi();
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>点表头：同一列再点一次 = 反向；换列 = 升序（★ 列例外，一上来就「收藏在前」）。</summary>
    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;

        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase))
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column == "Favorite";   // ★ 列默认降序 = 收藏的排在前面
        }
        ApplyView();
    }

    // ==================== 运行 ====================

    /// <summary>
    /// 运行一条命令（行内「▶ 运行」传本行、详情卡与右键菜单传当前选中项）。
    /// <para>命令经 <c>cmd /c</c> 执行；需要管理员权限的走 UAC 提权。结果 / 报错压成一行写进 StatusText。</para>
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
        Selected.Kind = string.IsNullOrWhiteSpace(EditKind) ? QuickActionKind.Command : EditKind;
        Selected.ScriptFile = (EditScriptFile ?? "").Trim();
        Selected.Interpreter = EditInterpreter ?? ScriptInterpreter.Auto;

        _svc.Save();
        RebuildCategories();
        ApplyView();

        var warn = Selected.Kind == QuickActionKind.Script && Selected.ScriptFile.Length == 0
            ? "（还没填脚本文件，运行时会被提示）"
            : "";
        StatusText = $"已保存到 {FilePath}{warn}";
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
