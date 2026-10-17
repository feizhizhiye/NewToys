using System;
using Avalonia.Controls;

namespace NewToys.ScriptManager.Views;

public partial class ScriptLibraryView : UserControl
{
    public ScriptLibraryView()
    {
        InitializeComponent();
        // NT-055（需求 §4.8.5）：页内工具条已删，按钮全部由工具条描述符的命令承担
        // （命令在 VM 里，需要选择器的走 Core.DialogHooks ⇒ VM ⛔ 不引 Avalonia）。
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ActionList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.6）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
