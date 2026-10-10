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

/// <summary>
/// 「电脑工具 → 设置与脚本」页：一张<b>系统设置项 + 脚本</b>清单。
/// <para>
/// 布局遵循规范 <b>3.8</b>：左边「可排序表头（左键点表头排序）的列表」，右边「选中项的属性卡」，
/// 列表上挂右键菜单（收藏 / 复制命令 / 删除）。
/// </para>
/// <para>
/// ⚠️ 这里的「设置」是<b>操作系统设置项</b>，不是本软件自己的设置（后者在「设置」模块，见规范 3.9）。
/// 本轮（v1.24）只做<b>列表 / 增删改查 / 收藏</b>：<see cref="QuickAction.Command"/> 可编辑、可展示，
/// 但<b>尚未接「运行」</b>，所以界面不出现执行按钮 —— 不虚报能力。
/// </para>
/// <para>
/// 收藏 = 每条 <see cref="QuickAction.IsFavorite"/> 上的标记（单一真相源），
/// 「收藏夹」页读的是同一批对象，因此这里右键收藏后那边天然一致。
/// </para>
/// </summary>
public partial class MyShortcutsViewModel : ObservableObject
{
    private readonly IQuickActionService? _svc;

    private string _sortColumn = "Title";
    private bool _sortDescending;

    /// <summary>灌值 / 改过滤项时抑制「属性变化 → 立即重排」的开关，避免构造期重复计算。</summary>
    private bool _suppress;

    public ObservableCollection<QuickAction> Items { get; } = new();
    public ObservableCollection<ActionCategoryOption> CategoryOptions { get; } = new();

    // ==================== 页面状态 ====================

    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ActionCategoryOption? _selectedCategory;
    [ObservableProperty] private QuickAction? _selected;

    // ==================== 表头（左键点一下切换排序） ====================

    [ObservableProperty] private string _titleHeader = "名称 ▲";
    [ObservableProperty] private string _categoryHeader = "分类";
    [ObservableProperty] private string _favoriteHeader = "★";

    // ==================== 列显隐（右键表头增删列，规范 3.8） ====================
    private const string PageKey = "pc-shortcuts";
    private readonly IColumnLayoutService? _cols;

    [ObservableProperty] private bool _isTitleVisible = true;
    [ObservableProperty] private bool _isCategoryVisible = true;
    [ObservableProperty] private bool _isDescriptionVisible = true;
    [ObservableProperty] private bool _isFavoriteVisible = true;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。</summary>
    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Title", "名称", "3*"),
        new("Category", "分类", "1.3*"),
        new("Description", "简介", "4*", sortable: false),
        new("Favorite", "★", "0.9*"),
    };

    /// <summary>列宽规格串（可绑定）：可见列写星号宽度，隐藏列写 "0"（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串而不是 <c>ColumnDefinitions</c>：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，
    /// Avalonia XAML 不允许在它上面写 {Binding}（编译期 AVLN3000），故由 <c>ColumnGrid</c> 控件解析（规范 3.8.5）。</summary>
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
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>把当前列显隐偏好落盘（config\columnlayout.json，会被数据管理一起备份）。</summary>
    private void SaveColumnVisibility()
    {
        if (_cols == null) return;
        _cols.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Title"] = IsTitleVisible, ["Category"] = IsCategoryVisible,
            ["Description"] = IsDescriptionVisible, ["Favorite"] = IsFavoriteVisible,
        });
    }

    /// <summary>进入页面时读回上次保存的列显隐偏好（未保存过的列保持默认「可见」）。</summary>
    private void LoadColumnVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(PageKey);
        if (saved.TryGetValue("Title", out var t)) IsTitleVisible = t;
        if (saved.TryGetValue("Category", out var c)) IsCategoryVisible = c;
        if (saved.TryGetValue("Description", out var d)) IsDescriptionVisible = d;
        if (saved.TryGetValue("Favorite", out var f)) IsFavoriteVisible = f;
    }

    // ==================== 属性卡的编辑缓冲（多字段文本表单 → 显式「💾 保存」，规范 3.7） ====================

    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editCategory = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editCommand = "";
    [ObservableProperty] private bool _editNeedAdmin;

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
    public MyShortcutsViewModel() { }

    public MyShortcutsViewModel(IQuickActionService svc, IColumnLayoutService cols)
    {
        _svc = svc;
        _cols = cols;
        LoadColumnVisibility();
        RebuildCategories();
        ApplyView();
    }

    // ==================== 列表构建 ====================

    private void RebuildCategories()
    {
        if (_svc == null) return;

        var keep = SelectedCategory?.Value;
        _suppress = true;
        CategoryOptions.Clear();
        CategoryOptions.Add(new ActionCategoryOption { Label = "全部分类", Value = null });
        foreach (var c in _svc.Items.Select(i => i.Category)
                                    .Where(c => !string.IsNullOrWhiteSpace(c))
                                    .Distinct()
                                    .OrderBy(c => c, StringComparer.CurrentCulture))
            CategoryOptions.Add(new ActionCategoryOption { Label = c, Value = c });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == keep) ?? CategoryOptions[0];
        _suppress = false;
    }

    private void ApplyView()
    {
        if (_svc == null) return;

        IEnumerable<QuickAction> q = _svc.Items;

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

        var item = _svc.Add();
        RebuildCategories();
        ApplyView();
        Selected = Items.FirstOrDefault(i => ReferenceEquals(i, item)) ?? item;
        StatusText = "已新增一条「新的设置项」，请在右侧改名 / 填分类与命令后点「💾 保存」。";
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

        _svc.Save();
        RebuildCategories();
        ApplyView();
        StatusText = $"已保存到 {FilePath}";
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
