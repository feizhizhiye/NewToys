using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.ViewModels;

namespace NewToys.ScriptManager.Views;

public partial class ScriptInfoView : UserControl
{
    public ScriptInfoView()
    {
        InitializeComponent();
        if (InfoList is not null) InfoList.ContextRequested += OnListContextRequested;
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// <para>顺手让本页的定时刷新只在「本页还在树上」时跑（切页即停）。</para>
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (InfoList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        (DataContext as ScriptInfoViewModel)?.StartAutoRefresh();
    }

    /// <summary>本页进入可视树 ⇒ 起定时刷新（1s tick，只跑 manifest 里 <c>auto:true</c> 的脚本）。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as ScriptInfoViewModel)?.StartAutoRefresh();
    }

    /// <summary>离开可视树（切页 / 关窗）⇒ 停定时刷新；在跑的脚本让它自然结束（⛔ 不强杀）。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ScriptInfoViewModel)?.StopAutoRefresh();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// 右键某一行时**先把选中项换到该行** —— 行级菜单命令用的是 <c>{Binding Selected}</c>
    /// （菜单在弹出树上，取不到"被点的那一项"）。
    /// <para>沿**逻辑父链**找 <see cref="SysInfoRow"/> 的 DataContext（Avalonia 的 ListBoxItem 只在左键改选中）。</para>
    /// </summary>
    private void OnListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ScriptInfoViewModel vm) return;

        var cur = e.Source as Control;
        while (cur is not null)
        {
            if (cur.DataContext is SysInfoRow row)
            {
                vm.Selected = row;
                return;
            }
            cur = cur.Parent as Control;
        }
    }
}
