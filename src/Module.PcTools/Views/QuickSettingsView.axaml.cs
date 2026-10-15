using System;
using Avalonia.Controls;

namespace NewToys.PcTools.Views;

public partial class QuickSettingsView : UserControl
{
    public QuickSettingsView() { InitializeComponent(); }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// <para>
    /// NT-055（需求 §4.8.5）：原来这里的「📤 导出 / 📥 导入（合并 / 整页替换）」三个 Click 接线随页内工具条
    /// 一起删了 —— 现在它们是 VM 上的命令（走 <c>DialogHooks</c> 弹选择器）。
    /// </para>
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ActionList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.6）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
