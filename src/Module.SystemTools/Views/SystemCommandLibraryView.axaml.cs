using System;
using Avalonia.Controls;

namespace NewToys.SystemTools.Views;

/// <summary>「系统工具 → 命令库」页（`script-commands`，对标脚本库重做）。</summary>
public partial class SystemCommandLibraryView : UserControl
{
    // 🔴 Avalonia 的 UserControl **不会自动生成**构造里的 InitializeComponent() —— 漏了整块 XAML 静默不加载（无报错）。
    public SystemCommandLibraryView() => InitializeComponent();

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（ui.md §6.2-6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (CommandList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「筛选 / 列显隐」菜单同样挂在独立弹出树，必须手工同步 DataContext。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
