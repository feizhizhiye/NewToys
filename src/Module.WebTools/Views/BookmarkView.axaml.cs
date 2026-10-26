using System;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class BookmarkView : UserControl
{
    /// <summary>
    /// ⚠️ 页内工具条由 **Shell 承载** ⇒「📥 导入书签」的文件选择器改由
    /// <c>BookmarkViewModel.ImportBookmarksCommand</c> 经 <c>Core.DialogHooks</c> 触发（VM ⛔ 不引 Avalonia UI），
    /// 这里只留列表的展开 / 折叠交互。
    /// </summary>
    public BookmarkView() => InitializeComponent();

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (BookmarkList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
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

    /// <summary>键盘 ← / →：折叠 / 展开当前文件夹（双击之外的键盘通道）。</summary>
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BookmarkViewModel vm || !vm.IsFolderSelected) return;

        if (e.Key == Key.Left) { vm.CollapseCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Right) { vm.ExpandCommand.Execute(null); e.Handled = true; }
    }
}
