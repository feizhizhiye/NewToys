using System;
using Avalonia.Controls;

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
        if (SystemList?.ContextMenu is { } left) left.DataContext = DataContext;
        if (ProfileList?.ContextMenu is { } right) right.DataContext = DataContext;
    }
}
