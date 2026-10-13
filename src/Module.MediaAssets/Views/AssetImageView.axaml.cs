using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「图片」页薄壳（NT-017 / v3.34）：只承载内层 <see cref="AssetAllView"/>，⛔ 不搬任何接线。
/// ⛔ 卡片 / 网格视图不做（v3.34）—— 本页与其余四个类型页完全同构。
/// </summary>
public partial class AssetImageView : UserControl
{
    public AssetImageView() => InitializeComponent();
}
