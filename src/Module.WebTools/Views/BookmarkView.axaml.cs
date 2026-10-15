using System;
using Avalonia.Controls;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class BookmarkView : UserControl
{
    /// <summary>
    /// ⚠️ NT-055（需求 §4.8.5）：页内工具条已迁到 **Shell 承载** ⇒ 「📥 导入书签」的文件选择器改由
    /// <c>BookmarkViewModel.ImportBookmarksCommand</c> 经 <c>Core.DialogHooks</c> 触发（VM ⛔ 不引 Avalonia UI），
    /// 这里不再有 <c>BtnImport</c> 接线。
    /// </summary>
    public BookmarkView() => InitializeComponent();

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (BookmarkList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

}
