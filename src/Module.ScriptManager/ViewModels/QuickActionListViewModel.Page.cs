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

public abstract partial class QuickActionListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    protected QuickActionListViewModel()
    {
        _pageKind = QuickActionPage.Shortcuts;
        FillEditors(null);
    }

    protected QuickActionListViewModel(IQuickActionService svc, IColumnLayoutService cols,
        IAppConfigService app, IScriptManagerSettingsService? pc = null, IScriptStoreService? store = null)
        : base(cols)
    {
        _svc = svc;
        _app = app;
        _pc = pc;
        _store = store;
        ScriptsFolderDraft = pc?.ScriptsFolder ?? "";
        // 抽象属性在基类构造里读一次存字段：避免「构造期虚调用」反复触发，也让后续逻辑读到稳定值。
        _pageKind = PageKind;
        LoadLayout();
        RefreshLibraries();
        RebuildCategories();
        _storeStamp = StoreStamp;
        ApplyView();
        FillEditors(null);
    }

    // ==================== 数据层计数戳（v3.80） ====================

    /// <summary>数据层「条目数 + 集合数」的组合戳（见 <see cref="RefreshIfStoreChanged"/>）。</summary>
    private int _storeStamp;

    private int StoreStamp => _store == null ? 0 : _store.Data.Items.Count + _store.Data.Collections.Count * 1000;

    /// <summary>
    /// 切回本页时，若 <c>script.json</c> 被**别的页**写过（设置页「＋ 加入命令库（winget 源）」），重读清单。
    /// <para>🔴 用计数戳判断，⛔ 无条件重建 —— 否则每次切标签都把列表清一遍、丢掉选中与滚动位置。</para>
    /// <para>⚠️ 由 View 的 <c>OnAttachedToVisualTree</c> 调用（与 <c>CollectionView.RefreshUsageState</c> 同一套做法）。</para>
    /// </summary>
    public void RefreshIfStoreChanged()
    {
        if (_svc == null || _store == null) return;

        var stamp = StoreStamp;
        if (stamp == _storeStamp) return;
        _storeStamp = stamp;

        _svc.Reload();          // 重建 service 镜像（内部 Load 会同步内置集合）
        RefreshLibraries();
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
        // 可采集（✔ / ✖）——「仅可采集」筛选与表头右键都走这一列。
        "Collectable" => ((QuickAction)row).CollectableMark,
        _ => null
    };

    // ==================== 列表构建 ====================

    private void RebuildCategories()
    {
        if (_svc == null) return;

        var keep = SelectedCategory?.Value;
        var source = _svc.Items.Where(i => BelongsToThisPage(i) && AcceptLibrary(i)).ToList();

        _suppress = true;
        CategoryOptions.Clear();
        CategoryOptions.Add(new ActionCategoryOption { Label = "全部类型", Value = null });
        foreach (var c in source.Select(i => i.Category)
                                .Where(c => !string.IsNullOrWhiteSpace(c))
                                .Distinct()
                                .OrderBy(c => c, StringComparer.CurrentCulture))
            CategoryOptions.Add(new ActionCategoryOption { Label = c, Value = c });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == keep) ?? CategoryOptions[0];

        // v3.19：分类也进表头右键「🔎 筛选」子菜单（原工具条「分类筛选框」移入这里）
        // 追加「仅可采集」—— 按需生成集合的入口（卡 §三.5）。
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

        IEnumerable<QuickAction> q = _svc.Items.Where(BelongsToThisPage).Where(AcceptLibrary);

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

}
