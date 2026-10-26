using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.WebTools.Models;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class BookmarkView : UserControl
{
    /// <summary>程序化回填选中集合时抑制自己的 <c>SelectionChanged</c> 回抛。</summary>
    private bool _syncingSelection;

    /// <summary>
    /// ⚠️ 页内工具条由 **Shell 承载** ⇒「📥 导入书签」的文件选择器改由
    /// <c>BookmarkViewModel.ImportBookmarksCommand</c> 经 <c>Core.DialogHooks</c> 触发（VM ⛔ 不引 Avalonia UI），
    /// 这里只留列表的展开 / 折叠 + 多选交互。
    /// </summary>
    public BookmarkView() => InitializeComponent();

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (BookmarkList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;

        if (_vm != null) _vm.SelectionRestoreRequested -= OnSelectionRestoreRequested;
        _vm = DataContext as BookmarkViewModel;
        if (_vm != null) _vm.SelectionRestoreRequested += OnSelectionRestoreRequested;
    }

    private BookmarkViewModel? _vm;

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
    /// 让「📁 移动到」对整批生效（列表右键默认不动选中项）。
    /// </summary>
    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm) return;
        if ((e.Source as Control)?.DataContext is not BookmarkNode node) return;

        if (BookmarkList?.SelectedItems?.Contains(node) == true) return;

        BookmarkList?.SelectedItems?.Clear();
        BookmarkList?.SelectedItems?.Add(node);
        vm.SelectedNode = node;
    }

    /// <summary>键盘 ← / →：折叠 / 展开当前文件夹（双击之外的键盘通道）。</summary>
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm || !vm.IsFolderSelected) return;

        if (e.Key == Key.Left) { vm.CollapseCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Right) { vm.ExpandCommand.Execute(null); e.Handled = true; }
    }
}
