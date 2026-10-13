using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「视频」页薄壳（NT-017 / v3.34）：只承载内层 <see cref="AssetAllView"/>，⛔ 不搬任何接线 ——
/// 弹选择器 / 表头拖宽 / 表头右键 <c>ContextMenu</c> 的 DataContext 同步全在 <see cref="AssetAllView"/> 的
/// code-behind 里，内层实例自带，天然生效（详见 <c>AssetVideoView.axaml</c> 的注释）。
/// </summary>
public partial class AssetVideoView : UserControl
{
    public AssetVideoView() => InitializeComponent();
}
