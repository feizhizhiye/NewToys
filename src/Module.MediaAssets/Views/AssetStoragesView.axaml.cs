using System;
using Avalonia.Controls;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「存储源」页 code-behind：只做 **View 才做得了的事**
/// （弹目录选择器 / 转发表头拖拽与测宽事件 / 给弹出树补 DataContext），业务逻辑全在 VM。
/// </summary>
public partial class AssetStoragesView : UserControl
{
    public AssetStoragesView()
    {
        InitializeComponent();

    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (StorageList?.ContextMenu is { } lmenu) lmenu.DataContext = DataContext;
    }

    // （需求 §4.8.5）：原「➕ 添加存储源」的 Click 接线随页内工具条一起删了 ——
    // 现在它是 VM 上的命令（目录选择走 DialogHooks）。
}
