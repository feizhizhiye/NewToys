using Avalonia.Controls;

namespace NewToys.PcTools.Views;

/// <summary>
/// 软件源设置页（v3.43 由 Module.Settings 迁入 Module.PcTools，NT-050：模块专属设置归模块）。
/// <para>本页没有右键菜单 / code-behind 逻辑，全部走 MVVM 绑定 —— 与主题外观、通用设置同型。</para>
/// </summary>
public partial class PackageSourceView : UserControl
{
    public PackageSourceView() { InitializeComponent(); }
}
