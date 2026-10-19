using Avalonia;
using Avalonia.Controls;
using NewToys.ScriptManager.ViewModels;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 集合页 code-behind（NT-068 S4）：⛔ 无业务逻辑 —— 两栏与成员勾选全走 ViewModel 绑定。
/// <para>NT-072：切回本页时重算「其他页面正在使用」的标记（信息面板切集合只写 <c>lastCollectionId</c>，本页收不到通知）。</para>
/// </summary>
public partial class CollectionView : UserControl
{
    public CollectionView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as CollectionViewModel)?.RefreshUsageState();
    }
}
