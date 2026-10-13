using Avalonia.Controls;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「任务中心」页 code-behind（NT-019）：**没有 View 专属逻辑** —— 队列刷新走 VM 的命令与
/// <c>TaskQueue.Changed</c>（VM 里已 Dispatcher 切回 UI 线程）；右键菜单的选中项由
/// <c>$parent[ListBox].SelectedItem</c> 直接绑定，⛔ 不必再手工同步 DataContext。
/// </summary>
public partial class AssetTasksView : UserControl
{
    public AssetTasksView() => InitializeComponent();
}
