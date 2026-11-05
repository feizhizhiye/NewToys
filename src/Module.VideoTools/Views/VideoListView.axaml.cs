using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

/// <summary>视频列表页 code-behind：只做 View 才做得了的事 （给 ContextMenu 补 DataContext、多选的修饰键接管）。（WHY: VT-041）</summary>
public partial class VideoListView : UserControl
{
    private VideoListViewModel? _vm;

    /// <summary>Shift 连选的基准行。</summary>
    private VideoItem? _anchor;

    /// <summary>程序化改 <c>SelectedItems</c> 时抑制自己的 <c>SelectionChanged</c> 回抛。</summary>
    private bool _syncingSelection;

    public VideoListView()
    {
        InitializeComponent();

        // 多选：修饰键在**隧道**阶段接管（ListBox 在 Multiple 下单击默认是「追加」，
        // 与资源管理器「单击 = 只选这一行」不一致 ⇒ 无修饰键也一并接管）
        VideoList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        VideoList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        VideoList.SelectionChanged += OnListSelectionChanged;
    }

    /// <summary>🔴 右键必须在按压阶段定行：ContextRequested 常被菜单自己先吃掉， 结果菜单项读的 Selected 还是上一条/为空 —— 表现为「右键删除点了没反应」。（WHY: VT-042）</summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if ((e.Source as Control)?.DataContext is not VideoItem row) return;

        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            if (VideoList.SelectedItems is not { } selected || !selected.Contains(row))
            {
                ReplaceWith(row);
                _anchor = row;
            }
            _vm.Selected = row;
            PushSelectionToVm(row);
            return;   // ⛔ 不 Handled：否则右键菜单弹不出来
        }

        if (!props.IsLeftButtonPressed) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // 行要拿到键盘焦点，否则 Ctrl+A 之类的键事件到不了列表
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.Focus();
        e.Handled = true;

        if (e.ClickCount >= 2)
        {
            ReplaceWith(row);
            _anchor = row;
            _vm.Selected = row;
            PushSelectionToVm(row);
            _vm.PlayCommand.Execute(null);
            return;
        }

        if (shift) SelectRange(row, additive: ctrl);
        else if (ctrl) Toggle(row);
        else ReplaceWith(row);

        if (!shift) _anchor = row;
        _vm.Selected = row;          // 详情卡跟最后一次点击走
        PushSelectionToVm(row);
    }

    /// <summary>Shift 连选：从基准行到目标行整段选中；<paramref name="additive"/> = 保留原有选中（Ctrl+Shift）。</summary>
    private void SelectRange(VideoItem target, bool additive)
    {
        if (_vm == null) return;

        var rows = _vm.Videos.ToList();
        var to = rows.IndexOf(target);
        if (to < 0) return;

        var from = _anchor != null ? rows.IndexOf(_anchor) : to;
        if (from < 0) from = to;

        BulkUpdateSelection(items =>
        {
            if (!additive) items.Clear();
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                if (!items.Contains(rows[i])) items.Add(rows[i]);
        });
    }

    /// <summary>Ctrl 点击：已选中就去掉，没选中就加上。</summary>
    private void Toggle(VideoItem row)
    {
        BulkUpdateSelection(items =>
        {
            if (items.Contains(row)) items.Remove(row);
            else items.Add(row);
        });
    }

    /// <summary>无修饰键：清空其余、只留这一行（资源管理器语义）。</summary>
    private void ReplaceWith(VideoItem row)
    {
        BulkUpdateSelection(items =>
        {
            items.Clear();
            items.Add(row);
        });
    }

    /// <summary>
    /// 批量改 <c>SelectedItems</c>：**期间关掉自动滚动**。
    /// <para>🔴 每 <c>Add</c> 一项 ListBox 都会把该项滚进视野（虚拟化下还要建容器 + 触发布局）——
    /// 全选 / 大范围连选上百行就是上百次滚动，界面看起来像卡死。</para>
    /// </summary>
    private void BulkUpdateSelection(Action<System.Collections.IList> mutate)
    {
        if (VideoList?.SelectedItems is not { } items) return;

        var autoScroll = VideoList.AutoScrollToSelectedItem;
        VideoList.AutoScrollToSelectedItem = false;
        _syncingSelection = true;
        try { mutate(items); }
        finally
        {
            _syncingSelection = false;
            VideoList.AutoScrollToSelectedItem = autoScroll;
        }
    }

    /// <summary>Ctrl+A：交给 VM 的全选命令（与右键「全选」同一入口）。</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        _vm?.SelectAllCommand.Execute(null);
    }

    /// <summary>列表自身的选中变化（键盘方向键 / 程序化）→ 回填 VM。</summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (_vm?.IsSyncingView == true) return;   // 列表重建中，别拿「被清空」覆盖用户的选中
        PushSelectionToVm(null);
    }

    private void PushSelectionToVm(VideoItem? clicked)
    {
        if (_vm == null || VideoList?.SelectedItems is not { } items) return;
        _vm.SetSelectionFromView(items.OfType<VideoItem>().ToList(), clicked);
    }

    /// <summary>VM 侧改了选中（全选 / 反选 / 取消 / 列表重算认领）⇒ 回填 <c>SelectedItems</c>。</summary>
    private void OnSelectionSyncRequested(object? sender, EventArgs e)
    {
        if (_vm == null) return;

        BulkUpdateSelection(items =>
        {
            items.Clear();
            foreach (var v in _vm.SelectedRows) items.Add(v);
        });
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null) _vm.SelectionSyncRequested -= OnSelectionSyncRequested;
        _vm = DataContext as VideoListViewModel;
        if (_vm != null) _vm.SelectionSyncRequested += OnSelectionSyncRequested;

        if (VideoList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }
}
