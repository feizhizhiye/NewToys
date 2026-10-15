using System;
using Avalonia.Controls;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「存储源」页 code-behind（NT-020）：只做 **View 才做得了的事**
/// （弹目录选择器 / 转发表头拖拽与测宽事件 / 给弹出树补 DataContext），业务逻辑全在 VM。
/// </summary>
public partial class AssetStoragesView : UserControl
{
    public AssetStoragesView()
    {
        InitializeComponent();

        // 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetStoragesViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // 表头首次测量完 → 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetStoragesViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (StorageList?.ContextMenu is { } lmenu) lmenu.DataContext = DataContext;
    }

    // NT-055 三轮（需求 §4.8.5）：原「➕ 添加存储源」的 Click 接线随页内工具条一起删了 ——
    // 现在它是 VM 上的命令（目录选择走 DialogHooks）。
}
