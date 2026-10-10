using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.PcTools.ViewModels;

namespace NewToys.PcTools.Views;

public partial class PackageManagerView : UserControl
{
    public PackageManagerView()
    {
        InitializeComponent();

        // 搜索框回车 = 点「🔍 联网搜索」（Avalonia 的 TextBox 没有 AcceptButton 之类的属性）
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (DataContext is PackageManagerViewModel vm) vm.SearchCommand.Execute(null);
            e.Handled = true;
        };
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// 这里跟着页面的 DataContext 走（页面 DataContext 就是 <c>PackageManagerViewModel</c>）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PkgList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
