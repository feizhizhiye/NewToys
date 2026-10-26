using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NewToys.WebTools.Models;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class HostsView : UserControl
{
    /// <summary>程序化回填选中集合时抑制自己的 <c>SelectionChanged</c> 回抛。</summary>
    private bool _syncingSelection;

    /// <summary>Shift 连选的基准行（左右各一个，两侧数据源不同）。</summary>
    private HostsLine? _leftAnchor;
    private HostsLine? _rightAnchor;

    private HostsViewModel? _vm;

    public HostsView()
    {
        InitializeComponent();

        // 列表多选（Ctrl 增选 / Shift 连选）：修饰键点击在**隧道**阶段接管，
        // 避免与 ListBox 默认的「点谁选谁」来回打架。
        SystemList.AddHandler(PointerPressedEvent, OnLeftPointerPressed, RoutingStrategies.Tunnel);
        ProfileList.AddHandler(PointerPressedEvent, OnRightPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (SystemList?.ContextMenu is { } left) left.DataContext = DataContext;
        if (ProfileList?.ContextMenu is { } right) right.DataContext = DataContext;

        if (_vm != null) _vm.SelectionRestoreRequested -= OnSelectionRestoreRequested;
        _vm = DataContext as HostsViewModel;
        if (_vm != null) _vm.SelectionRestoreRequested += OnSelectionRestoreRequested;
    }

    // ==================== 多选（Ctrl 增选 / Shift 连选 / Ctrl+A 全选） ====================

    private void OnLeftPointerPressed(object? sender, PointerPressedEventArgs e) =>
        OnListPointerPressed(SystemList, e, isSystem: true);

    private void OnRightPointerPressed(object? sender, PointerPressedEventArgs e) =>
        OnListPointerPressed(ProfileList, e, isSystem: false);

    /// <summary>
    /// Ctrl = 切换该行；Shift = 从基准行连选到该行（Ctrl+Shift = 追加连选）。
    /// 无修饰键 ⇒ 直接返回，选中交给默认逻辑（并重设连选基准）。
    /// </summary>
    private void OnListPointerPressed(ListBox? list, PointerPressedEventArgs e, bool isSystem)
    {
        if (_vm == null || list == null) return;
        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not HostsLine row) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!ctrl && !shift)
        {
            if (isSystem) _leftAnchor = row;
            else _rightAnchor = row;
            return;
        }

        e.Handled = true;
        (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.Focus();

        ClearOtherSide(isSystem);

        if (shift) SelectRange(list, isSystem, row, additive: ctrl);
        else Toggle(list, row);

        if (isSystem) _leftAnchor = row;
        else _rightAnchor = row;
    }

    /// <summary>左右互斥：一侧开始多选就把另一侧的选中清掉（详情卡只认一侧）。</summary>
    private void ClearOtherSide(bool isSystem)
    {
        var other = isSystem ? ProfileList : SystemList;
        if (other?.SelectedItems is not { Count: > 0 } items) return;

        _syncingSelection = true;
        try { items.Clear(); }
        finally { _syncingSelection = false; }

        _vm?.SetSelectionFromView(!isSystem, Array.Empty<HostsLine>());
    }

    private void SelectRange(ListBox list, bool isSystem, HostsLine target, bool additive)
    {
        if (list.SelectedItems is not { } items || _vm == null) return;

        var rows = (isSystem ? _vm.SystemRows : _vm.ProfileRows).ToList();
        var to = rows.IndexOf(target);
        if (to < 0) return;

        var anchor = isSystem ? _leftAnchor : _rightAnchor;
        var from = anchor != null ? rows.IndexOf(anchor) : -1;
        if (from < 0) from = to;

        if (!additive) items.Clear();
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            if (!items.Contains(rows[i])) items.Add(rows[i]);
        }
    }

    private static void Toggle(ListBox list, HostsLine row)
    {
        if (list.SelectedItems is not { } items) return;
        if (items.Contains(row)) items.Remove(row);
        else items.Add(row);
    }

    private void OnSystemSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SyncSelection(SystemList, isSystem: true);

    private void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SyncSelection(ProfileList, isSystem: false);

    private void SyncSelection(ListBox? list, bool isSystem)
    {
        if (_syncingSelection || _vm == null || list == null) return;

        var rows = list.SelectedItems?.OfType<HostsLine>().ToList() ?? new List<HostsLine>();
        _vm.SetSelectionFromView(isSystem, rows);

        if (rows.Count > 1) ClearOtherSide(isSystem);
    }

    /// <summary>行集合重建后按行号把多选高亮补回去（节点实例会换）。</summary>
    private void OnSelectionRestoreRequested(object? sender, EventArgs e)
    {
        if (_vm == null) return;

        _syncingSelection = true;
        try
        {
            Restore(SystemList, _vm.HighlightRows(true));
            Restore(ProfileList, _vm.HighlightRows(false));
        }
        finally { _syncingSelection = false; }

        static void Restore(ListBox? list, IReadOnlyList<HostsLine> rows)
        {
            if (list?.SelectedItems is not { } items) return;
            items.Clear();
            foreach (var r in rows) items.Add(r);
        }
    }

    /// <summary>Ctrl+A：把本侧条目行全选（另一侧清空）。</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (sender is not ListBox list || _vm == null) return;

        var isSystem = ReferenceEquals(list, SystemList);
        var rows = (isSystem ? _vm.SystemRows : _vm.ProfileRows).Where(r => r.IsEntry).ToList();

        ClearOtherSide(isSystem);

        _syncingSelection = true;
        try
        {
            list.SelectedItems?.Clear();
            foreach (var r in rows) list.SelectedItems?.Add(r);
        }
        finally { _syncingSelection = false; }

        _vm.SetSelectionFromView(isSystem, rows);
        if (isSystem) _leftAnchor = rows.FirstOrDefault();
        else _rightAnchor = rows.FirstOrDefault();

        e.Handled = true;
    }

    /// <summary>
    /// 右键先校正选中：点在**已选中**的行上 ⇒ 保持整批（让批量命令作用于整批）；
    /// 否则改为单选它（列表右键默认不动选中项）。
    /// </summary>
    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not ListBox list) return;
        if ((e.Source as Control)?.DataContext is not HostsLine row) return;

        if (list.SelectedItems?.Contains(row) == true) return;

        _syncingSelection = true;
        try
        {
            list.SelectedItems?.Clear();
            list.SelectedItems?.Add(row);
        }
        finally { _syncingSelection = false; }

        var isSystem = ReferenceEquals(list, SystemList);
        _vm.SetSelectionFromView(isSystem, new[] { row });

        if (isSystem) { _vm.SelectedSystemRow = row; _leftAnchor = row; }
        else { _vm.SelectedProfileRow = row; _rightAnchor = row; }
    }
}
