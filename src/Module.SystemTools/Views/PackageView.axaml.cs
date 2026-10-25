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
        // 「📤 导出 / 📥 导入」的 Click 接线同理，已搬进 VM（走 DialogHooks 弹选择器）。

        // 底栏显隐的另一半口径 = 列表内容是否超出视口（没翻页需求但滚起来了 ⇒ 也要出计数）。
        // 同 AssetAllView：LayoutUpdated 上量一次，VM 侧 SetContentOverflow 对同值 no-op。
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

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// 这里跟着页面的 DataContext 走（页面 DataContext 就是 <c>PackageViewModel</c>）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PkgList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        // 表头 ☑ 的勾选菜单（全选 / 取消 / 反选）同理。
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;
    }
}
