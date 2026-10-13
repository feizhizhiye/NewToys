using System;
using Avalonia.Controls;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「分类管理」页 code-behind（NT-018）：只转发表头拖拽 / 测宽事件 + 同步弹出菜单 DataContext。
/// 树的展开 / 选中 / 成员加载全在 VM（⛔ 不用 TreeView，所以没有树事件要接线）。
/// </summary>
public partial class AssetCategoriesView : UserControl
{
    public AssetCategoriesView()
    {
        InitializeComponent();

        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetCategoriesViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetCategoriesViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };
    }

    /// <summary>⚠️ ContextMenu 在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (MemberList?.ContextMenu is { } lmenu) lmenu.DataContext = DataContext;
    }
}
