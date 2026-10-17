using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.ViewModels;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 信息面板（NT-068 S5）：节列表 + 节内行点选 + 定时刷新生命周期。
/// </summary>
/// <remarks>
/// 🔴 行用 <c>ItemsControl</c> + <c>PointerPressed</c> 而不是 <c>ListBox</c>：
/// 每节一个 <c>ListBox</c> 会在 <c>ScrollViewer</c> 里再套一层滚动区（嵌套滚动 = 滚不动 / 抢滚轮），
/// 所以整页只有一个 <c>ScrollViewer</c>，选中由这里手动落到 VM。
/// </remarks>
public partial class ScriptInfoView : UserControl
{
    public ScriptInfoView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        (DataContext as ScriptInfoViewModel)?.StartAutoRefresh();
    }

    /// <summary>本页进入可视树 ⇒ 起定时刷新（是否真的刷由集合 / 视图的刷新策略决定）。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as ScriptInfoViewModel)?.StartAutoRefresh();
    }

    /// <summary>离开可视树（切页 / 关窗）⇒ 停表；在跑的采集让它自然结束（⛔ 不强杀）。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ScriptInfoViewModel)?.StopAutoRefresh();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>点在节内任意一行 ⇒ 选中（详情卡显示这一行）。</summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not SysInfoRow row) return;
        (DataContext as ScriptInfoViewModel)?.SelectRow(row);
    }
}
