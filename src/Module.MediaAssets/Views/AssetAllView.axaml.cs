using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「全部资产」页的 code-behind：只做**三件 View 才做得了的事**
/// （转发表头拖拽 / 表头测量事件、给 ContextMenu 补 DataContext、取跨页投递的件），业务逻辑一律在 VM。
/// <para>🔴 NT-055（需求 §4.8.5）：页内工具条已迁到 **Shell 承载** —— 全部按钮 / 输入框 / 勾选框在
/// <c>AssetAllViewModel.Toolbar</c> 里声明（5 个类型薄壳页继承本 VM ⇒ 一处覆盖 6 个页面）；
/// 文件 / 目录选择器也改由 VM 经 <c>Core.DialogHooks</c> 触发 ⇒ 这里不再接线，
/// 原 v3.42 的按宽度折叠算法（<c>ApplyToolbarLayout</c> / <c>FitTwoRows</c>）整体搬进
/// <c>Shell/Controls/ToolbarHost</c>（两条防抖原样保留）。</para>
/// </summary>
public partial class AssetAllView : UserControl
{
    public AssetAllView()
    {
        InitializeComponent();

        // ① 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // ② 表头首次测量完 → 让 VM 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };

        // ③ v3.42.1：表头 ☑ 上的右键菜单（全选 / 取消选择 / 反选）**只挂外层 Border**（HeaderCheckHost），
        //    右键由 Avalonia 原生弹出 —— ⛔ 不要再手动 menu.Open()（二次 Open 会抛异常直接崩）。

        // ③b NT-064：右键菜单在**独立弹出树**上，DataContext 不随宿主继承 ⇒ 每次弹之前重新同步一次。
        //    （OnDataContextChanged 只在 DataContext 变化时同步；菜单可能早于宿主 DataContext 最终态构建，
        //    晚同步一次最稳 —— MenuItem 的 Command 绑不到 VM 时表现为「菜单在但点了没反应」。）
        if (HeaderCheckHost?.ContextMenu is { } checkMenu)
            checkMenu.Opening += (_, _) => checkMenu.DataContext = DataContext;

        // ④ NT-064：底栏显隐的另一半口径 = 列表内容是否超出视口（滚动出现）。
        //    布局每帧都可能变（开合详情栏 / 改窗口大小 / 翻页）⇒ 挂 LayoutUpdated 量一次；
        //    VM 侧 SetContentOverflow 对同值 no-op，⛔ 不会抖动。
        LayoutUpdated += (_, _) => ReportListOverflow();
    }

    /// <summary>列表滚动器的内容高 vs 视口高 → 回填 VM（超出 ⇒ 底栏才显示）。</summary>
    private void ReportListOverflow()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        _listScroller ??= AssetList?.FindDescendantOfType<ScrollViewer>();
        if (_listScroller is not { } sv) return;
        // Avalonia 的 ScrollViewer 是 Extent / Viewport（Size），⛔ 不是 WPF 的 ExtentHeight / ViewportHeight
        vm.SetContentOverflow(sv.Extent.Height - sv.Viewport.Height > 1);
    }

    private ScrollViewer? _listScroller;

    /// <summary>
    /// 🔴 跨页取件点（NT-018，规范 §4.8 硬规则 8）：别的页面（标签管理 / 分类管理）请求"打开某个资产"时，
    /// 在这里取件并选中。⛔ 不能放 `OnDataContextChanged` —— 标签已存在、切回来的那次只有可视树重挂，
    /// DataContext 不变、那个回调**不会触发**。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AssetAllViewModel vm)
        {
            vm.ConsumePendingAsset();
            vm.RefreshFooterVisibility();   // NT-064：兜底重算底栏显隐（时序漏推防护）
        }
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (AssetList?.ContextMenu is { } amenu) amenu.DataContext = DataContext;
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;
    }
}
