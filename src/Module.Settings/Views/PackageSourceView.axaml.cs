using Avalonia.Controls;

namespace NewToys.Settings.Views;

/// <summary>
/// 软件源设置页。
/// <para>本页没有右键菜单 / code-behind 逻辑，全部走 MVVM 绑定 —— 与主题外观、通用设置同型。</para>
/// </summary>
public partial class PackageSourceView : UserControl
{
    public PackageSourceView() { InitializeComponent(); }
}
