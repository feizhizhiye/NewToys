using System;
using Avalonia.Controls;

namespace NewToys.Shell.Views;

public partial class PathView : UserControl
{
    public PathView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// <para>
    /// v3.53 / NT-056：文件 / 目录选择器已改走 <c>DialogHooks</c>（由 Shell 注入），
    /// 页内不再需要接管「📂 浏览」的点击 —— 本文件只保留这处菜单 DataContext 同步。
    /// </para>
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PathList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
