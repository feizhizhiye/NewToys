using System;
using Avalonia.Controls;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「标签管理」页 code-behind：只做 **View 才做得了的事**
/// （给弹出树补 DataContext / 转发表头拖拽与测宽事件），业务逻辑全在 VM。
/// </summary>
public partial class AssetTagsView : UserControl
{
    public AssetTagsView()
    {
        InitializeComponent();

    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (TagList?.ContextMenu is { } lmenu) lmenu.DataContext = DataContext;
    }
}
