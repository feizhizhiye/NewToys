using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NewToys.MediaAssets.Views;

/// <summary>「全部资产」页的表头右键：把列显隐 / 列宽从行菜单里分出来（口径同 TableViewHost）。</summary>
public partial class AssetAllView
{
    private readonly List<object> _rowMenuItems = new();
    private bool _rowItemsCaptured;
    private bool _menuHooked;
    private bool _headerMode;
    private bool _rightPressPending;
    private Point _lastRightPress;
    private TableViewColumn? _menuColumn;
    private readonly Dictionary<TableViewColumn, GridLength> _defaultWidths = new();
    private MenuItem? _columnMenuItem;
    private readonly List<object> _columnChildren = new();

    /// <summary>表头模式把「▤ 列」的子项平铺到顶层；下次打开前先还回容器，避免第二次打开变空。</summary>
    private void RestoreColumnChildren()
    {
        if (_columnMenuItem is null || _columnChildren.Count == 0) return;
        _columnMenuItem.Items.Clear();
        foreach (var child in _columnChildren) _columnMenuItem.Items.Add(child);
        _columnChildren.Clear();
    }

    private void EnsureMenuHooked()
    {
        if (_menuHooked || AssetList.ContextMenu is not { } menu) return;
        _menuHooked = true;
        menu.Opening += OnHeaderMenuOpening;
        AssetList.AddHandler(PointerPressedEvent, OnRightPressRecorded, RoutingStrategies.Tunnel);
        AssetList.AddHandler(ContextRequestedEvent, OnContextRequestedPreview, RoutingStrategies.Tunnel);
    }

    private void OnRightPressRecorded(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(AssetList).Properties.IsRightButtonPressed)
        {
            _headerMode = false;
            _menuColumn = null;
            return;
        }

        _lastRightPress = e.GetPosition(AssetList);
        _rightPressPending = true;
        _headerMode = TryHitHeader(_lastRightPress, out _menuColumn);

        if (AssetList.Columns is { } columns)
            foreach (var column in columns)
                if (!_defaultWidths.ContainsKey(column)) _defaultWidths[column] = column.Width;
    }

    private void OnContextRequestedPreview(object? sender, ContextRequestedEventArgs e)
    {
        if (_rightPressPending)
        {
            _rightPressPending = false;
            _headerMode = TryHitHeader(_lastRightPress, out _menuColumn);
            return;
        }
        _headerMode = false;
        _menuColumn = null;
    }

    /// <summary>表头行包围盒（含列间空隙 / 最右空白）；表头背景透明，⛔ 靠 e.Source 找祖先会漏空白区。</summary>
    private bool TryHitHeader(Point point, out TableViewColumn? column)
    {
        column = null;
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var header in AssetList.GetVisualDescendants().OfType<TableViewColumnHeader>())
        {
            if (header.Column is not { IsVisible: true }) continue;
            if (header.TranslatePoint(new Point(0, 0), AssetList) is not { } origin) continue;

            var rect = new Rect(origin, header.Bounds.Size);
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
            if (rect.Contains(point)) column = header.Column;
        }

        if (double.IsInfinity(left)) return false;
        return new Rect(left, top, right - left, bottom - top).Contains(point);
    }

    private void OnHeaderMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.DataContext = DataContext;

        if (!_rowItemsCaptured)
        {
            _rowMenuItems.Clear();
            foreach (var item in menu.Items)
                if (item is not null) _rowMenuItems.Add(item);
            _rowItemsCaptured = true;
        }

        RestoreColumnChildren();
        menu.Items.Clear();
        if (!_headerMode)
        {
            // 原始前两项 = 「▤ 列」子菜单 + 分隔线；行菜单只留功能项。
            foreach (var item in _rowMenuItems.Skip(2)) menu.Items.Add(item);
            return;
        }

        var fit = new MenuItem { Header = "↔ 本列宽度自适应", IsEnabled = _menuColumn is not null };
        fit.Click += (_, _) => AutoFitHeaderColumn();
        menu.Items.Add(fit);

        var reset = new MenuItem { Header = "⟲ 恢复本页默认列宽" };
        reset.Click += (_, _) => ResetHeaderColumnWidths();
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());

        // 列显隐平铺在顶层（与 TableViewHost 口径一致），⛔ 不再套一层「▤ 列」子菜单。
        _columnMenuItem = _rowMenuItems.Count > 0 ? _rowMenuItems[0] as MenuItem : null;
        if (_columnMenuItem is not null)
        {
            foreach (var child in _columnMenuItem.Items)
                if (child is not null) _columnChildren.Add(child);
            _columnMenuItem.Items.Clear();
            foreach (var child in _columnChildren) menu.Items.Add(child);
        }
    }

    /// <summary>只调命中列：表头文字宽 + 内边距，封顶 320px（⛔ 不按全文测宽、不批量调全部列）。</summary>
    private void AutoFitHeaderColumn()
    {
        if (_menuColumn is null) return;
        var text = (_menuColumn.Header as string ?? "").Replace(" ▲", "").Replace(" ▼", "");
        var probe = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.Bold };
        probe.Measure(Size.Infinity);
        var width = Math.Min(Math.Ceiling(probe.DesiredSize.Width + 18), 320);
        if (width > 0) _menuColumn.Width = new GridLength(width, GridUnitType.Pixel);
    }

    private void ResetHeaderColumnWidths()
    {
        if (AssetList.Columns is not { } columns) return;
        foreach (var column in columns)
            if (_defaultWidths.TryGetValue(column, out var width)) column.Width = width;
    }
}
