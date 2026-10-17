using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.Controls;

/// <summary>
/// 工具条宿主（NT-055，需求 §4.8.5）：把 <see cref="IToolbarSource"/> 的**描述符**渲染成真实工具条。
/// <para>职责两件：① 按容器宽度**自动折叠**（两行放得下就全显示，放不下才把尾部收进「⋯」）；
/// ② **详情开关浮层**（右上角、⛔ 不占独立列，但容量计算给它留安全区）。</para>
/// <para>🔴 折叠算法由「全部资产」页（<c>AssetAllView.axaml.cs</c>）**原样搬迁**，两条防抖必须保留：
/// 项宽**首帧缓存一次**、容量**幂等短路** —— 否则切标签 / 拉窗口时工具条会抖。</para>
/// </summary>
public partial class ToolbarHost : UserControl
{
    /// <summary>页面 VM（= 描述符里所有路径的解析根）。</summary>
    public static readonly StyledProperty<object?> SourceProperty =
        AvaloniaProperty.Register<ToolbarHost, object?>(nameof(Source));

    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // ==================== 折叠状态 ====================

    /// <summary>各项的布局宽（不含间距），**首次布局后缓存一次** —— 用它贪心算"两行能放几项"。</summary>
    private double[]? _itemWidths;

    /// <summary>末尾「⋯」自己的宽 —— 算预算时先给它留位，保证它永远落得下。</summary>
    private double _moreWidth;

    private int _appliedCapacity = -1;
    private bool _expanded;

    private IToolbarSource? _toolbarSource;
    private IDetailPanelProvider? _detailProvider;

    private TextBlock? _moreGlyphEllipsis;
    private TextBlock? _moreGlyphChevron;

    public ToolbarHost()
    {
        InitializeComponent();

        // 🔴 用 LayoutUpdated 而不是 SizeChanged：首帧要等子元素量出宽度才算得了。
        ItemsPanel.LayoutUpdated += (_, _) => ApplyLayout();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty) Rebuild();
    }

    // ==================== 构建 ====================

    /// <summary>换页面 / 换工具条构成时重建（旧的一律退订）。</summary>
    private void Rebuild()
    {
        if (_toolbarSource != null) { _toolbarSource.ToolbarChanged -= OnToolbarChanged; _toolbarSource = null; }
        if (_detailProvider != null) { _detailProvider.DetailChanged -= OnDetailChanged; _detailProvider = null; }

        ItemsPanel.Children.Clear();
        _itemWidths = null;
        _appliedCapacity = -1;
        _expanded = false;

        if (Source is IToolbarSource ts)
        {
            _toolbarSource = ts;
            ts.ToolbarChanged += OnToolbarChanged;

            var items = ts.Toolbar;
            if (items != null)
                foreach (var d in items) ItemsPanel.Children.Add(Build(d, Source));

            // 🔴 「⋯」必须是 ItemsPanel 的**最后一个**子元素（ApplyLayout 据此定位）。
            ItemsPanel.Children.Add(BuildMoreButton());
        }

        SyncDetailToggle();
        ApplyLayout();
    }

    private void OnToolbarChanged(object? sender, EventArgs e) => Rebuild();

    private Control Build(ToolbarItemDescriptor d, object src) => d.Kind switch
    {
        ToolbarItemKind.TextField => BuildTextField(d, src),
        ToolbarItemKind.Combo => BuildCombo(d, src),
        ToolbarItemKind.Toggle => BuildToggle(d, src),
        ToolbarItemKind.ChipGroup => BuildChipGroup(d, src),
        ToolbarItemKind.Label => BuildLabel(d, src),
        ToolbarItemKind.Separator => BuildSeparator(),
        ToolbarItemKind.SplitButton => BuildSplitButton(d, src),
        _ => BuildButton(d, src)
    };

    private static Control BuildSeparator()
    {
        var b = new Border { Width = 1, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
        b.Classes.Add("divider-line");
        return b;
    }

    private Control BuildButton(ToolbarItemDescriptor d, object src)
    {
        var btn = new Button { Content = BuildContent(d, src) };
        ApplyClasses(btn, d.Classes, "ghost");
        ApplyWidth(btn, d.Width);
        ApplyTip(btn, d, src);

        if (d.CommandPath != null) Bind(btn, Button.CommandProperty, src, d.CommandPath);
        if (d.CommandArgPath != null) Bind(btn, Button.CommandParameterProperty, src, d.CommandArgPath);

        ApplyEnabledVisible(btn, d, src);

        if (d.Items is { Count: > 0 }) btn.Flyout = BuildFlyout(d.Items, src);
        return btn;
    }

    private Control BuildSplitButton(ToolbarItemDescriptor d, object src)
    {
        var btn = new SplitButton { Content = BuildContent(d, src) };
        ApplyClasses(btn, d.Classes, "ghost");
        ApplyWidth(btn, d.Width);
        ApplyTip(btn, d, src);

        if (d.CommandPath != null) Bind(btn, SplitButton.CommandProperty, src, d.CommandPath);

        ApplyEnabledVisible(btn, d, src);

        if (d.Items is { Count: > 0 }) btn.Flyout = BuildFlyout(d.Items, src);
        return btn;
    }

    private Control BuildToggle(ToolbarItemDescriptor d, object src)
    {
        var cb = new CheckBox
        {
            Content = d.Text ?? "",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (d.ValuePath != null) Bind(cb, ToggleButton.IsCheckedProperty, src, d.ValuePath, BindingMode.TwoWay);
        ApplyTip(cb, d, src);
        ApplyEnabledVisible(cb, d, src);
        return cb;
    }

    private Control BuildTextField(ToolbarItemDescriptor d, object src)
    {
        var tb = new TextBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            IsReadOnly = d.IsReadOnly
        };
        if (d.Width is { } w) tb.Width = w;
        if (d.Placeholder != null) tb.PlaceholderText = d.Placeholder;

        // v3.63：**只读框** ⇒ 未聚焦时框里只放占位文字（`Placeholder`），聚焦才填入真实值并自动全选（方便 Ctrl+C）。
        // ⚠️ 不能用「把文字设成透明」那套：Avalonia 的 PlaceholderText **只在 Text 为空时才显示**，
        //    文字还在框里 ⇒ 水印永远不出现。这里让 Text 完全由本控件掌管，真实值走 Tag（OneWay）暂存。
        if (d.IsReadOnly && d.ValuePath != null && !string.IsNullOrEmpty(d.Placeholder))
        {
            var ph = d.Placeholder;
            Bind(tb, Control.TagProperty, src, d.ValuePath, BindingMode.OneWay);
            tb.Text = ph;
            tb.GotFocus += (_, _) => { tb.Text = tb.Tag?.ToString() ?? ""; tb.SelectAll(); };
            tb.LostFocus += (_, _) => tb.Text = ph;
        }
        else if (d.ValuePath != null)
            Bind(tb, TextBox.TextProperty, src, d.ValuePath, d.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay);

        ApplyTip(tb, d, src);
        ApplyEnabledVisible(tb, d, src);

        if (d.EnterCommandPath is { } enterPath)
        {
            // 提交通道（v3.64 / NT-061）：`EnterCommandPath` = 该框的**唯一提交入口**，**回车 + 失焦都走它**
            // （⛔ 不再加第二处反射）。可编辑框靠 TwoWay 绑定把新值写回 VM，命令只负责「校验 + 落盘」；
            // 只读框没有提交语义 ⇒ 不挂失焦（否则每个只读框一失焦就触发一次动作）。
            Action commit = () =>
            {
                if (ResolveCommand(src, enterPath) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
            };
            tb.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                commit();
                e.Handled = true;
            };
            if (!d.IsReadOnly) tb.LostFocus += (_, _) => commit();
        }
        return tb;
    }

    private Control BuildCombo(ToolbarItemDescriptor d, object src)
    {
        var cb = new ComboBox { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        if (d.Width is { } w) cb.Width = w;

        if (d.ItemsPath != null) Bind(cb, ItemsControl.ItemsSourceProperty, src, d.ItemsPath);

        if (d.DisplayPath is { } display)
            cb.ItemTemplate = new FuncDataTemplate<object>((_, _) =>
                new TextBlock { FontSize = 12, [!TextBlock.TextProperty] = new Binding(display) });

        if (d.SelectedItemPath != null)
            Bind(cb, SelectingItemsControl.SelectedItemProperty, src, d.SelectedItemPath, BindingMode.TwoWay);

        if (d.SelectedValuePath != null)
        {
            Bind(cb, SelectingItemsControl.SelectedValueProperty, src, d.SelectedValuePath, BindingMode.TwoWay);
            if (d.SelectedValueBindingPath != null) cb.SelectedValueBinding = new Binding(d.SelectedValueBindingPath);
        }

        ApplyTip(cb, d, src);
        ApplyEnabledVisible(cb, d, src);
        return cb;
    }

    /// <summary>
    /// 多选 / 单选按钮组（<c>ChipGroup</c>；v3.54 / NT-055 补迁 <c>script-packages</c> 时新增）：
    /// 按 <c>ItemsPath</c> 生成一排按钮（样式类 = 页面里「源」按钮用的 <c>src-btn</c>），
    /// 每项文案取 <c>DisplayPath</c>、高亮跟随数据项的 <c>SelectedPath</c>，
    /// 点击把**数据项本身**当参数执行 <c>CommandPath</c>。
    /// </summary>
    private Control BuildChipGroup(ToolbarItemDescriptor d, object src)
    {
        var items = new ItemsControl { VerticalAlignment = VerticalAlignment.Center };
        // ⚠️ `ItemsControl.ItemsPanel` 的目标类型是 `ITemplate<Panel?>` ⇒ 类型参数必须写 `Panel?`
        //    （写 `Panel` 只是警告 CS8619，但会污染编译输出）。
        items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        });

        if (d.ItemsPath != null) Bind(items, ItemsControl.ItemsSourceProperty, src, d.ItemsPath);

        items.ItemTemplate = new FuncDataTemplate<object>((item, _) => BuildChip(item, d, src), true);

        ApplyEnabledVisible(items, d, src);
        return items;
    }

    private static Control BuildChip(object? item, ToolbarItemDescriptor d, object src)
    {
        var btn = new Button { VerticalAlignment = VerticalAlignment.Center };
        btn.Classes.Add("src-btn");

        if (d.DisplayPath != null) btn.Bind(ContentControl.ContentProperty, new Binding(d.DisplayPath));
        if (d.ToolTip != null) ToolTip.SetTip(btn, d.ToolTip);
        if (d.ShowToolTipOnDisabled) ToolTip.SetShowOnDisabled(btn, true);

        if (d.CommandPath != null && ResolveCommand(src, d.CommandPath) is { } cmd)
        {
            btn.Command = cmd;
            btn.CommandParameter = item;          // 🔴 参数 = 数据项本身（⛔ 不是字面量）
        }

        if (d.SelectedPath != null) BindSelectedClass(btn, item, d.SelectedPath);
        return btn;
    }

    /// <summary>
    /// 把「选中态样式类」挂到数据项的 bool 属性上：C# 里 <c>Classes</c> 绑不了（XAML 的
    /// <c>Classes.selected="{Binding …}"</c> 才有编译期支持），只能订阅 <see cref="INotifyPropertyChanged"/>
    /// 手动切类 —— <c>src-btn.selected</c> 本来就是靠这个类高亮的。
    /// </summary>
    private static void BindSelectedClass(Button btn, object? item, string path)
    {
        if (item == null) return;
        var prop = item.GetType().GetProperty(path);
        if (prop == null) return;

        void Sync() => btn.Classes.Set("selected", prop.GetValue(item) is true);
        Sync();

        if (item is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += (_, e) => { if (e.PropertyName == path) Sync(); };
    }

    private Control BuildLabel(ToolbarItemDescriptor d, object src)
    {
        var tb = new TextBlock { Text = d.Text ?? "", VerticalAlignment = VerticalAlignment.Center };
        tb.Classes.Add("toolbar-label");
        if (d.TextPath != null) Bind(tb, TextBlock.TextProperty, src, d.TextPath);
        ApplyTip(tb, d, src);
        ApplyEnabledVisible(tb, d, src);
        return tb;
    }

    /// <summary>按钮 / 标签的内容：静态文案 / 动态文案 / 双文案互斥切换。</summary>
    private Control BuildContent(ToolbarItemDescriptor d, object src)
    {
        if (d.AltText != null && d.AltTextPath != null)
        {
            var main = new TextBlock { Text = d.Text ?? "", VerticalAlignment = VerticalAlignment.Center };
            Bind(main, Visual.IsVisibleProperty, src, "!" + d.AltTextPath);
            var alt = new TextBlock { Text = d.AltText, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            Bind(alt, Visual.IsVisibleProperty, src, d.AltTextPath);
            var panel = new Panel();
            panel.Children.Add(main);
            panel.Children.Add(alt);
            return panel;
        }

        if (d.TextPath != null)
        {
            var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            // 🔴 必须显式给 Source：ToolbarHost ⛔ 没有 DataContext（与 Flyout 同一坑），
            //    漏了 Source ⇒ 绑定取不到值 ⇒ 按钮渲染成**空白无字**（v3.60 视频列表两按钮丢字就是这个根因）。
            var binding = new Binding(d.TextPath) { Source = src };
            if (d.TextFormat != null) binding.StringFormat = d.TextFormat;
            tb.Bind(TextBlock.TextProperty, binding);
            return tb;
        }

        return new TextBlock { Text = d.Text ?? "", VerticalAlignment = VerticalAlignment.Center };
    }

    private static FlyoutBase BuildFlyout(IReadOnlyList<ToolbarMenuItemDescriptor> items, object src)
    {
        var menu = new MenuFlyout();
        foreach (var mi in items)
        {
            var item = new MenuItem { Header = mi.Text };
            if (mi.ToolTip != null) ToolTip.SetTip(item, mi.ToolTip);
            if (mi.ToolTipPath != null)
                item.Bind(ToolTip.TipProperty, new Binding(mi.ToolTipPath) { Source = src });
            if (mi.CommandPath != null)
                item.Bind(MenuItem.CommandProperty, new Binding(mi.CommandPath) { Source = src });
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>末尾固定「⋯」：只放图标、宽度固定（开合不会让工具条重新换行）。</summary>
    private Button BuildMoreButton()
    {
        _moreGlyphEllipsis = new TextBlock
        {
            Text = "⋯",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _moreGlyphChevron = new TextBlock
        {
            Text = "▲",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };

        var panel = new Panel();
        panel.Children.Add(_moreGlyphEllipsis);
        panel.Children.Add(_moreGlyphChevron);

        var btn = new Button { Content = panel, Width = 32, MinWidth = 32 };
        btn.Classes.Add("ghost");
        ToolTip.SetTip(btn, "显示全部按钮 / 收起（本工具条默认最多两行，超出部分收在这里）");
        btn.Click += OnMoreClick;
        return btn;
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        if (_moreGlyphEllipsis != null) _moreGlyphEllipsis.IsVisible = !_expanded;
        if (_moreGlyphChevron != null) _moreGlyphChevron.IsVisible = _expanded;
        _appliedCapacity = -1;
        ApplyLayout();
    }

    // ==================== 详情开关浮层 ====================

    private void SyncDetailToggle()
    {
        if (Source is IDetailPanelProvider p && p.HasDetailPanel)
        {
            _detailProvider = p;
            p.DetailChanged += OnDetailChanged;
            DetailToggle.IsVisible = true;
            UpdateGlyph();
        }
        else
        {
            DetailToggle.IsVisible = false;
        }
    }

    private void OnDetailChanged(object? sender, EventArgs e) => UpdateGlyph();

    private void UpdateGlyph()
    {
        var open = _detailProvider?.IsDetailOpen == true;
        GlyphOpen.IsVisible = open;
        GlyphClosed.IsVisible = !open;

        _appliedCapacity = -1;   // 浮层显隐 / 宽度可能变了 → 重排一次
        ApplyLayout();
    }

    private void OnDetailToggleClick(object? sender, RoutedEventArgs e) => _detailProvider?.ToggleDetail();

    // ==================== 折叠 ====================

    /// <summary>浮层占位宽（不显示 = 0）：⛔ 它**不占列**，但第一行要给它留安全区。</summary>
    private double ToggleReserve => DetailToggle.IsVisible ? Math.Max(20, DetailToggle.Bounds.Width) : 0;

    /// <summary>
    /// 按容器宽度重排：**两行内放得下就全显示，放不下才把序列尾部几项收进「⋯」**。
    /// <para>约定：<c>ItemsPanel</c> 的**最后一个**子元素就是「⋯」；其余按加入顺序 = **优先级顺序**。</para>
    /// <para>🔴 为什么不会布局循环 / 抖动：项宽**只在首次布局后测一次并缓存**，之后只按"容器宽 + 缓存宽"纯计算
    /// ⇒ 折叠结果**不反过来影响计算输入**（容器宽由窗口决定，与内容无关）。</para>
    /// </summary>
    private void ApplyLayout()
    {
        if (ItemsPanel is null) return;
        var children = ItemsPanel.Children;
        if (children.Count < 2) return;                 // 只剩「⋯」= 本页没有工具条

        var width = ItemsPanel.Bounds.Width;
        if (width <= 0) return;                          // 还没量到尺寸 → 等下一次布局回调

        var itemCount = children.Count - 1;              // 末尾那个是「⋯」本身

        // ① 首次（或子元素数变了）：此刻子元素都还可见 → 记下各自宽。
        //    ⚠️ 任一项量到 0 就整体放弃缓存（说明布局还没完成），⛔ 不能把脏数据存下来。
        if (_itemWidths is null || _itemWidths.Length != itemCount)
        {
            var widths = new double[itemCount];
            for (var i = 0; i < itemCount; i++)
            {
                var w = children[i].Bounds.Width;
                if (w <= 0) return;
                widths[i] = w;
            }
            var moreWidth = children[itemCount].Bounds.Width;
            if (moreWidth <= 0) return;
            _itemWidths = widths;
            _moreWidth = moreWidth;
        }

        var capacity = _expanded ? itemCount : FitTwoRows(width);

        if (capacity == _appliedCapacity) return;        // 幂等短路：容量没变就不动（避免无谓重排）
        _appliedCapacity = capacity;

        for (var i = 0; i < itemCount; i++)
            children[i].IsVisible = i < capacity;

        // 「⋯」：收起态且确实有东西被折时才显示；展开态保留（否则没法收起）；全放得下则自己消失。
        children[itemCount].IsVisible = _expanded || capacity < itemCount;
    }

    /// <summary>
    /// 贪心算"容器宽 <paramref name="width"/> 下、**两行**内能放下几项"。
    /// <para>第一行要给右上角浮层让位；第二行要给末尾「⋯」留位 —— 保证两者都不遮按钮。</para>
    /// </summary>
    private int FitTwoRows(double width)
    {
        var widths = _itemWidths;
        if (widths is null) return 0;

        var spacing = ItemsPanel.ItemSpacing;
        var limit0 = Math.Max(0, width - ToggleReserve - spacing);   // 第一行：让开浮层
        var limit1 = Math.Max(0, width - _moreWidth - spacing);      // 第二行：留出「⋯」
        if (limit0 <= 0) return 1;                                   // 极端窄也留一项：⛔ 不许工具条整条空掉

        var count = 0;
        var rowUsed = 0.0;
        var row = 0;

        foreach (var w in widths)
        {
            var limit = row == 0 ? limit0 : limit1;
            var add = (rowUsed > 0 ? spacing : 0) + w;

            if (rowUsed > 0 && rowUsed + add > limit)
            {
                row++;
                if (row > 1) break;                                  // 两行用完 → 剩下的都进「⋯」
                rowUsed = 0;
                add = w;
            }

            if (add > limit) break;                                  // 单个就超宽 → 折
            rowUsed += add;
            count++;
        }
        return count;
    }

    // ==================== 小工具 ====================

    private static void Bind(AvaloniaObject target, AvaloniaProperty property, object src, string path,
                             BindingMode mode = BindingMode.OneWay)
        => target.Bind(property, new Binding(path) { Source = src, Mode = mode });

    private static void ApplyClasses(StyledElement control, string? classes, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(classes) ? fallback : classes!;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            control.Classes.Add(token);
    }

    private static void ApplyWidth(Control control, double? width)
    {
        if (width is not { } w) return;
        control.Width = w;
        control.MinWidth = w;
    }

    private static void ApplyTip(Control control, ToolbarItemDescriptor d, object src)
    {
        if (d.ToolTip != null) ToolTip.SetTip(control, d.ToolTip);
        if (d.ToolTipPath != null) Bind(control, ToolTip.TipProperty, src, d.ToolTipPath);
        if (d.ShowToolTipOnDisabled) ToolTip.SetShowOnDisabled(control, true);
    }

    private static void ApplyEnabledVisible(Control control, ToolbarItemDescriptor d, object src)
    {
        if (d.IsEnabled is { } enabled) control.IsEnabled = enabled;
        if (d.IsEnabledPath != null) Bind(control, InputElement.IsEnabledProperty, src, d.IsEnabledPath);
        if (d.IsVisiblePath != null) Bind(control, Visual.IsVisibleProperty, src, d.IsVisiblePath);
    }

    private static ICommand? ResolveCommand(object src, string path)
        => src.GetType().GetProperty(path)?.GetValue(src) as ICommand;
}
