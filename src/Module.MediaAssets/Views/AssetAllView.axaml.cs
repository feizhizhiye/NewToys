using System;
using Avalonia.Controls;
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
    }

    /// <summary>
    /// 🔴 跨页取件点（NT-018，规范 §4.8 硬规则 8）：别的页面（标签管理 / 分类管理）请求"打开某个资产"时，
    /// 在这里取件并选中。⛔ 不能放 `OnDataContextChanged` —— 标签已存在、切回来的那次只有可视树重挂，
    /// DataContext 不变、那个回调**不会触发**。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AssetAllViewModel vm) vm.ConsumePendingAsset();
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
