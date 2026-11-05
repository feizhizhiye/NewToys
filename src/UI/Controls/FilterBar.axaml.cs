using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using NewToys.Shared.Models;

namespace NewToys.UI.Controls;

/// <summary>
/// 列表页通用筛选条：左侧内容槽放页面自有的下拉，右侧放快捷筛选下拉 / 清空 / 命中摘要。
/// <para>快捷项直接消费 <see cref="FilterQuickItem"/>，无需新增 ViewModel 契约。</para>
/// </summary>
public partial class FilterBar : UserControl
{
    /// <summary>页面自有的筛选控件（原样保留其绑定与命令）。</summary>
    public static readonly StyledProperty<object?> FieldsContentProperty =
        AvaloniaProperty.Register<FilterBar, object?>(nameof(FieldsContent));

    public object? FieldsContent
    {
        get => GetValue(FieldsContentProperty);
        set => SetValue(FieldsContentProperty, value);
    }

    /// <summary>快捷筛选项（通常是 VM 的 <c>QuickFilters</c>）。</summary>
    public static readonly StyledProperty<IEnumerable?> QuickItemsSourceProperty =
        AvaloniaProperty.Register<FilterBar, IEnumerable?>(nameof(QuickItemsSource));

    public IEnumerable? QuickItemsSource
    {
        get => GetValue(QuickItemsSourceProperty);
        set => SetValue(QuickItemsSourceProperty, value);
    }

    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<FilterBar, ICommand?>(nameof(ClearCommand));

    public ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    /// <summary>一键复位时，是否把内容槽里的常用下拉复位到首项（「全部」）。默认 false ⇒ 其余页行为不变。</summary>
    public static readonly StyledProperty<bool> ResetFieldsOnClearProperty =
        AvaloniaProperty.Register<FilterBar, bool>(nameof(ResetFieldsOnClear));

    public bool ResetFieldsOnClear
    {
        get => GetValue(ResetFieldsOnClearProperty);
        set => SetValue(ResetFieldsOnClearProperty, value);
    }

    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<FilterBar, string?>(nameof(Summary));

    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public FilterBar()
    {
        InitializeComponent();

        FieldsHost.Bind(ContentControl.ContentProperty,
            new Binding(nameof(FieldsContent)) { Source = this });
        SummaryText.Bind(TextBlock.TextProperty,
            new Binding(nameof(Summary)) { Source = this });
        QuickCombo.SelectionChanged += OnQuickComboSelectionChanged;
        ClearButton.Click += (_, _) =>
        {
            if (ResetFieldsOnClear) ResetFieldCombos();
            ResetQuickComboSelection();
            ClearCommand?.Execute(null);
        };
        ClearButton.IsVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == QuickItemsSourceProperty) RebuildQuickItems();
        if (change.Property == ClearCommandProperty || change.Property == ResetFieldsOnClearProperty)
            UpdateClearButtonVisibility();
    }

    private void UpdateClearButtonVisibility()
        => ClearButton.IsVisible = ClearCommand is not null || ResetFieldsOnClear;

    /// <summary>内容槽里的常用下拉复位到首项；用 SetCurrentValue 保留页面原有的 TwoWay 绑定。</summary>
    private void ResetFieldCombos()
    {
        if (FieldsHost.Content is not Visual root) return;
        foreach (var combo in root.GetVisualDescendants().OfType<ComboBox>())
        {
            if (!combo.Classes.Contains("filter-combo")) continue;
            object? first = combo.ItemsSource is IEnumerable items
                ? items.Cast<object?>().FirstOrDefault()
                : combo.Items.Count > 0 ? combo.Items[0] : null;
            if (first is not null)
                combo.SetCurrentValue(ComboBox.SelectedItemProperty, first);
        }
    }

    private INotifyCollectionChanged? _quickNotifier;
    private bool _rebuildingQuick;
    private FilterQuickItem? _allItem;

    /// <summary>快捷项渲染成一个下拉：首项「全部」+ 各分类值；没有分类值时整个下拉隐藏。</summary>
    private void RebuildQuickItems()
    {
        if (_quickNotifier is not null) _quickNotifier.CollectionChanged -= OnQuickCollectionChanged;
        _quickNotifier = QuickItemsSource as INotifyCollectionChanged;
        if (_quickNotifier is not null) _quickNotifier.CollectionChanged += OnQuickCollectionChanged;

        _rebuildingQuick = true;
        try
        {
            var items = QuickItemsSource?.OfType<FilterQuickItem>()
                .Where(i => i.Command is not null && !IsActionItem(i))
                .ToList() ?? new List<FilterQuickItem>();

            if (items.Count == 0)
            {
                QuickCombo.ItemsSource = null;
                QuickCombo.IsVisible = false;
                _allItem = null;
                return;
            }

            _allItem = new FilterQuickItem { Label = "全部" };
            items.Insert(0, _allItem);
            QuickCombo.ItemsSource = items;
            QuickCombo.SelectedIndex = 0;
            QuickCombo.IsVisible = true;
        }
        finally { _rebuildingQuick = false; }
    }

    /// <summary>基类的两个动作项不是筛选值，不进下拉；其余带命令的项各当一个可选条件。</summary>
    private static bool IsActionItem(FilterQuickItem item)
        => item.Label.StartsWith("＋ 添加筛选列", StringComparison.Ordinal)
           || item.Label.StartsWith("清空筛选", StringComparison.Ordinal);

    private void OnQuickComboSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rebuildingQuick || QuickCombo.SelectedItem is not FilterQuickItem item) return;
        if (ReferenceEquals(item, _allItem)) { ClearCommand?.Execute(null); return; }
        item.Command?.Execute(null);
    }

    private void ResetQuickComboSelection()
    {
        if (!QuickCombo.IsVisible || QuickCombo.SelectedIndex == 0) return;
        _rebuildingQuick = true;
        try { QuickCombo.SelectedIndex = 0; }
        finally { _rebuildingQuick = false; }
    }

    private void OnQuickCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildQuickItems();
}
