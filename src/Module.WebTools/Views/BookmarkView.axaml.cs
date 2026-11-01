using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NewToys.WebTools.Models;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class BookmarkView : UserControl
{
    /// <summary>程序化回填选中集合时抑制自己的 <c>SelectionChanged</c> 回抛。</summary>
    private bool _syncingSelection;

    /// <summary>Shift 连选的基准行（上一次普通点击 / Ctrl 点击的行）。</summary>
    private BookmarkNode? _anchor;

    /// <summary>
    /// ⚠️ 页内工具条由 **Shell 承载** ⇒「📥 导入书签」的文件选择器改由
    /// <c>BookmarkViewModel.ImportBookmarksCommand</c> 经 <c>Core.DialogHooks</c> 触发（VM ⛔ 不引 Avalonia UI），
    /// 这里只留列表的展开 / 折叠 + 多选交互。
    /// </summary>
    public BookmarkView()
    {
        InitializeComponent();

        // 多选修饰键（Ctrl / Shift）自己算：ListBoxItem 默认逻辑只认「点谁选谁」，
        // 隧道阶段先截住修饰键点击，避免与默认选中来回打架。
        BookmarkList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null) _vm.SelectionRestoreRequested -= OnSelectionRestoreRequested;
        _vm = DataContext as BookmarkViewModel;
        if (_vm != null) _vm.SelectionRestoreRequested += OnSelectionRestoreRequested;
    }

    private BookmarkViewModel? _vm;

    // ==================== 多选（Ctrl 增选 / Shift 连选） ====================

    /// <summary>
    /// 修饰键点击在**隧道**阶段接管：Ctrl = 切换该行，Shift = 从基准行连选到该行（Ctrl+Shift = 追加连选）。
    /// 无修饰键 ⇒ 直接返回，选中 / 双击展开交给默认逻辑。
    /// </summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || BookmarkList == null) return;
        if (!e.GetCurrentPoint(BookmarkList).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not BookmarkNode node) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!ctrl && !shift)
        {
            _anchor = node;        // 普通点击 = 重设连选基准，选中仍走默认逻辑
            return;
        }

        e.Handled = true;
        (e.Source as Control)?.Focus();

        if (shift) SelectRange(node, additive: ctrl);
        else ToggleNode(node);

        _anchor = node;
    }

    private void ToggleNode(BookmarkNode node)
    {
        if (BookmarkList?.SelectedItems is not { } items) return;
        if (items.Contains(node)) items.Remove(node);
        else items.Add(node);
    }

    private void SelectRange(BookmarkNode target, bool additive)
    {
        if (BookmarkList?.SelectedItems is not { } items || _vm == null) return;

        var nodes = _vm.VisibleNodes;
        var to = nodes.IndexOf(target);
        if (to < 0) return;

        var from = _anchor != null ? nodes.IndexOf(_anchor) : -1;
        if (from < 0) from = to;

        if (!additive) items.Clear();
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            var node = nodes[i];
            if (!items.Contains(node)) items.Add(node);
        }
    }

    /// <summary>Ctrl+A：把当前可见行全选（VM 侧只把书签行计入多选快照）。</summary>
    private void SelectAllVisible()
    {
        if (BookmarkList?.SelectedItems is not { } items || _vm == null) return;

        var nodes = _vm.VisibleNodes.ToList();
        _syncingSelection = true;
        try
        {
            items.Clear();
            foreach (var node in nodes) items.Add(node);
        }
        finally { _syncingSelection = false; }

        _vm.SetSelectionFromView(nodes);
        _anchor = nodes.FirstOrDefault();
    }

    /// <summary>多选回填：只收书签行，文件夹行混选时由 VM 忽略。</summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || DataContext is not BookmarkViewModel vm) return;
        vm.SetSelectionFromView(BookmarkList.SelectedItems?.OfType<BookmarkNode>().ToList() ?? new());
    }

    /// <summary>树重建（折叠 / 筛选 / 移动后）把多选高亮补回去 —— 节点实例会换，按书签 Id 匹配。</summary>
    private void OnSelectionRestoreRequested(object? sender, EventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm) return;

        _syncingSelection = true;
        try
        {
            BookmarkList?.SelectedItems?.Clear();
            foreach (var node in vm.SelectionToHighlight())
                BookmarkList?.SelectedItems?.Add(node);
        }
        finally { _syncingSelection = false; }
    }

    // ==================== 行交互 ====================

    /// <summary>
    /// 双击文件夹整行 = 展开 / 折叠（走系统双击间隔，⛔ 不自写计时器）。
    /// 书签行双击放行 —— 留给以后「双击打开网址」。
    /// </summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm || !vm.IsFolderSelected) return;
        vm.ToggleExpandCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// 右键先校正选中：点击**未被选中**的行 ⇒ 改为单选它；点在**已选中**的行上 ⇒ 保持当前多选，
    /// 让「📁 移动到」/「🗑 删除」对整批生效（列表右键默认不动选中项）。
    /// </summary>
    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm) return;
        if ((e.Source as Control)?.DataContext is not BookmarkNode node) return;

        if (BookmarkList?.SelectedItems?.Contains(node) == true) return;

        BookmarkList?.SelectedItems?.Clear();
        BookmarkList?.SelectedItems?.Add(node);
        vm.SelectedNode = node;
        _anchor = node;
    }

    /// <summary>键盘：Ctrl+A 全选、← / → 折叠 / 展开当前文件夹。</summary>
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm) return;

        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SelectAllVisible();
            e.Handled = true;
            return;
        }

        if (!vm.IsFolderSelected) return;

        if (e.Key == Key.Left) { vm.CollapseCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Right) { vm.ExpandCommand.Execute(null); e.Handled = true; }
    }
}
