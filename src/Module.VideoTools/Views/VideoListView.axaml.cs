using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.Shared.Models;
using NewToys.VideoTools.Services;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoListView : UserControl
{
    public VideoListView()
    {
        InitializeComponent();

        // 目录框回车 = 扫描（Avalonia 的 TextBox 没有 AcceptButton 之类的属性）
        // §6.4.7：详情卡「文件名」原先是**回车 / 失焦即提交**（详情卡内不放按钮），由这里挂 NameBox 的
        // KeyDown / LostFocus 触发。NT-054 迁移后输入框归 Shell 的声明式卡渲染，页面 code-behind 够不着它 ⇒
        // 改由 VM 的 OnEditNameChanged（失焦写回 → 自动提交）+ 卡里「✏ 应用新文件名」按钮承担（见 VideoListViewModel）。

        // 双击列表项 = 播放（和资源管理器习惯一致）
        VideoList.DoubleTapped += (_, _) =>
        {
            if (DataContext is VideoListViewModel vm) vm.PlayCommand.Execute(null);
        };

        // v3.20 §6.4.15：拖动表头列边界 → VM 改「手动列宽」→ 表头与所有行同规格重建
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is ListPageViewModelBase vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };
        // v3.22 §4.8.1：表头首次布局后把「表头宽 + 示例值宽」交给 VM —— 示例 ≤ 表头的列锁成表头宽
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is ListPageViewModelBase vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VideoList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        // v3.16 §6.4.10：表头勾选框的「全选 / 取消选择 / 反选」菜单同理（不同步的话右键点了没反应）。
        // v3.16.1：菜单挪到 HeaderCheckHost（透明 Border = 整格热区）上。
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;
    }

}
