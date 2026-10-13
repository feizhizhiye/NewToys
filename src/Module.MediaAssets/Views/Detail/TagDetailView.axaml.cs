using Avalonia.Controls;

namespace NewToys.MediaAssets.Views.Detail;

/// <summary>
/// 标签详情卡（NT-018）：纯展示 + 绑定，**没有 code-behind 逻辑**
/// （💾 保存 = VM 的命令；色板点击 = VM 的命令；资产链接 = 跨页投递命令）。
/// </summary>
public partial class TagDetailView : UserControl
{
    public TagDetailView() => InitializeComponent();
}
