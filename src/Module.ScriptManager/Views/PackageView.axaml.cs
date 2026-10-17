using System;
using Avalonia.Controls;

namespace NewToys.ScriptManager.Views;

public partial class PackageView : UserControl
{
    public PackageView()
    {
        InitializeComponent();

        // NT-055（需求 §4.8.5）：原来这里的「搜索框回车 = 联网搜索」接线随页内工具条一起删了 ——
        // 现在它由描述符的 `EnterCommandPath` 承担（Shell 的 ToolbarHost 统一渲染）。
        // 「📤 导出 / 📥 导入」的 Click 接线同理，已搬进 VM（走 DialogHooks 弹选择器）。
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// 这里跟着页面的 DataContext 走（页面 DataContext 就是 <c>PackageViewModel</c>）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PkgList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        // v3.37 / NT-002：表头 ☑ 的勾选菜单（全选 / 取消 / 反选）同理。
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;
    }
}
