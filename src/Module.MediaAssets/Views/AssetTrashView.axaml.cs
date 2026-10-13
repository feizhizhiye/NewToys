using System;
using Avalonia.Controls;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>「回收站」页 code-behind（NT-019）：只转发表头拖拽 / 测宽事件 + 同步弹出菜单 DataContext。</summary>
public partial class AssetTrashView : UserControl
{
    public AssetTrashView()
    {
        InitializeComponent();

        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetTrashViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetTrashViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };
    }

    /// <summary>⚠️ ContextMenu 在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (TrashList?.ContextMenu is { } lmenu) lmenu.DataContext = DataContext;
    }
}
