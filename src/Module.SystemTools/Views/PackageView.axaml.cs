using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using NewToys.SystemTools.ViewModels;

namespace NewToys.SystemTools.Views;

public partial class PackageView : UserControl
{
    public PackageView()
    {
        InitializeComponent();

        // （需求 §4.8.5）：「搜索框回车 = 联网搜索」由描述符的 `EnterCommandPath` 承担
        //（Shell 的 ToolbarHost 统一渲染）—— ⛔ 不在本层接线。
        // 「导出 / 📥 导入」的 Click 接线同理，已搬进 VM（走 DialogHooks 弹选择器）。

        // 底栏显隐的另一半口径 = 列表内容是否超出视口（没翻页需求但滚起来了 ⇒ 也要出计数）。
        LayoutUpdated += (_, _) => ReportListOverflow();
    }

    /// <summary>列表滚动器内容高 vs 视口高 → 回填 VM（超出 ⇒ 底栏才显示）。</summary>
    private void ReportListOverflow()
    {
        if (DataContext is not PackageViewModel vm) return;
        _listScroller ??= PkgList?.FindDescendantOfType<ScrollViewer>();
        if (_listScroller is not { } sv) return;
        vm.SetContentOverflow(sv.Extent.Height - sv.Viewport.Height > 1);
    }

    private ScrollViewer? _listScroller;
}
