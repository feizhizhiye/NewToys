using System;
using Avalonia.Controls;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class HostsView : UserControl
{
    public HostsView() => InitializeComponent();

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ProfileList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
