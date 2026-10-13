using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoFavoritesView : UserControl
{
    public VideoFavoritesView()
    {
        InitializeComponent();

        // §6.4.7 与视频列表页同款：详情卡「文件名」**回车 / 失焦即提交**
        // （命令里自带"名字没变"的短路，切换卡片时顺带触发一次失焦也只会空转）。
        // v3.21：标签改点色片选择 → TagsBox 提交逻辑删除。
        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); e.Handled = true; } };
        NameBox.LostFocus += (_, _) => CommitName();
    }

    /// <summary>
    /// 🔴 **取件点**（规范 §4.8 硬规则 8）：「视频预览」页「📸 取当前帧」送过来的帧在这里转成收藏卡。
    /// <para>
    /// ⛔ 必须挂在 <c>OnAttachedToVisualTree</c> —— 标签页已经存在再切回来时只重挂可视树，
    /// DataContext **不变**，<c>OnDataContextChanged</c> 不会触发（放在那里会漏掉第二次之后的取件）。
    /// </para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is VideoFavoritesViewModel vm) vm.ConsumePendingFrames();
    }

    private void CommitName()
    {
        if (DataContext is VideoFavoritesViewModel vm) vm.SaveNameCommand.Execute(null);
    }
}
