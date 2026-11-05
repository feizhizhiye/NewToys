using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using NewToys.Shared.Models;

namespace NewToys.UI.Controls;

/// <summary>
/// 列表页通用筛选条：左侧内容槽放页面自有的下拉，右侧放快捷筛选 chips / 清空 / 命中摘要。
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
        ClearButton.Bind(Button.CommandProperty,
            new Binding(nameof(ClearCommand)) { Source = this });
        SummaryText.Bind(TextBlock.TextProperty,
            new Binding(nameof(Summary)) { Source = this });
        ClearButton.IsVisible = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == QuickItemsSourceProperty) RebuildQuickItems();
        if (change.Property == ClearCommandProperty) ClearButton.IsVisible = ClearCommand is not null;
    }

    /// <summary>无命令的占位项不显示；点中的 chip 加粗，让「当前筛了什么」常驻可见。</summary>
    private INotifyCollectionChanged? _quickNotifier;

    private void RebuildQuickItems()
    {
        if (_quickNotifier is not null) _quickNotifier.CollectionChanged -= OnQuickCollectionChanged;
        _quickNotifier = QuickItemsSource as INotifyCollectionChanged;
        if (_quickNotifier is not null) _quickNotifier.CollectionChanged += OnQuickCollectionChanged;

        QuickPanel.Children.Clear();
        if (QuickItemsSource is null) return;

        foreach (var item in QuickItemsSource.OfType<FilterQuickItem>())
        {
            if (item.Command is null) continue;

            var button = new Button
            {
                Content = item.Label,
                Command = item.Command,
                FontSize = 12,
                Padding = new Thickness(6, 1),
                MinWidth = 0,
                MinHeight = 0
            };
            button.Classes.Add("ghost");
            button.Click += (_, _) =>
            {
                foreach (var other in QuickPanel.Children.OfType<Button>())
                    other.FontWeight = FontWeight.Normal;
                button.FontWeight = FontWeight.Bold;
            };
            QuickPanel.Children.Add(button);
        }
    }

    private void OnQuickCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildQuickItems();
}
