using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

public partial class ShellView : Window
{
    private ShellViewModel? _vm;

    /// <summary>
    /// 真·退出开关。「关闭按钮最小化到托盘」开启后，<see cref="OnClosing"/> 会把关闭拦下来；
    /// 托盘菜单里的「退出 NewToys」必须把它置 true，否则<see cref="Window.Close"/> 会被自己拦住、永远退不掉。
    /// </summary>
    public bool ForceClose { get; set; }

    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// 「关闭按钮最小化到托盘」的落点（「设置 → 通用」里那个开关）。
    /// <para>拦下来的方式：勾了开关 → <c>e.Cancel = true</c> 并 <see cref="Window.Hide"/>；
    /// 没勾 / 托盘「退出」置了 <see cref="ForceClose"/> → 正常关，并收掉托盘图标。</para>
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!ForceClose && CloseToTrayEnabled())
        {
            e.Cancel = true;
            Hide();
            return;
        }
        App.ShutdownTray();
        base.OnClosing(e);
    }

    private static bool CloseToTrayEnabled()
    {
        try { return App.GetService<ILaunchConfigService>()?.Config.CloseToTray == true; }
        catch { return false; }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BtnMin.Click += (s, ev) =>
        {
            WindowState = WindowState.Minimized;
            ev.Handled = true;
        };
        BtnMax.Click += (s, ev) =>
        {
            ToggleMaxRestore();
            ev.Handled = true;
        };
        BtnClose.Click += (s, ev) =>
        {
            Close();
            ev.Handled = true;
        };
        UpdateMaxIcon();

        // 主题切换后必须**重建**二级菜单行：那些行是 code-behind 手工 new 出来的，
        // 选中底色/文字色是用 FindResource 取到画刷后写成**本地值**的（换资源字典不会自动改它们）。
        // 不重建 → 切到浅色后二级菜单仍留着深色配色。
        var theme = App.GetService<IThemeService>();
        if (theme != null)
            theme.ThemeChanged += () =>
                Dispatcher.UIThread.Post(() => { UpdatePanelVisibility(); UpdateSubSelection(); });

        // 拖动/双击最大化：挂在整个标题栏 Grid 上，
        // 通过 IsOverWindowButton 过滤掉标题栏上的按钮（🌓 / 最小化 / 最大化 / 关闭，以及它们内部 Path/TextBlock）。
        // ⚠️ 新增标题栏按钮必须同时登记进 IsOverWindowButton，否则点它会变成拖窗口。
        // 注意：标题栏 Grid 和中间拖拽 Panel 都显式设了 Background="Transparent"，
        // 否则 Avalonia 不做命中测试，鼠标事件会直接穿透，拖不动。
        TitleBarGrid.PointerPressed += OnTitleBarPointerPressed;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs ev)
    {
        if (!ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 如果鼠标点在窗口按钮（最小化/最大化/关闭）上，不处理拖拽——让按钮自己响应
        var hit = (InputElement?)ev.Source;
        if (IsOverWindowButton(hit)) return;

        if (ev.ClickCount == 2)
        {
            ToggleMaxRestore();
            ev.Handled = true;
            return;
        }

        BeginMoveDrag(ev);
        ev.Handled = true;
    }

    /// <summary>
    /// 从触发元素向上查找，是否命中了标题栏上的按钮（主题 / 最小化 / 最大化 / 关闭）。
    /// ⚠️ 新增任何标题栏按钮都必须登记到这里：否则点它会走进 <c>BeginMoveDrag</c>，
    /// 系统的移动循环会吃掉后续的 PointerReleased，按钮的 Click 可能不触发。
    /// </summary>
    private bool IsOverWindowButton(InputElement? hit)
    {
        while (hit != null)
        {
            if (ReferenceEquals(hit, BtnTheme) || ReferenceEquals(hit, BtnMin)
                || ReferenceEquals(hit, BtnMax) || ReferenceEquals(hit, BtnClose))
                return true;
            hit = hit.Parent as InputElement;
        }
        return false;
    }

    /// <summary>
    /// 监听窗口状态变化（含 Win+↑/↓、标题栏双击、拖到屏幕顶边吸附最大化等系统操作），
    /// 同步最大化/还原按钮图标——Avalonia 不会主动通知代码后置 WindowState 变了，必须手动监听。
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
            UpdateMaxIcon();
    }

    private void ToggleMaxRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        _vm = vm;
        vm.PropertyChanged += (s, a) =>
        {
            // ⚠️ 二级菜单列的「可见性」由 UpdatePanelVisibility() 统一负责，
            // 它同时依赖 IsHomePage 与 CurrentGroup 两个属性 ——
            // 所以**两个属性变化都必须走它**，不能只在 IsHomePage 变化时调：
            // 否则从首页点进一级菜单时（先翻 IsHomePage、后落 CurrentGroup 的旧顺序）
            // 可见性会按「CurrentGroup 还是 null」算成 false，之后再也不翻转 → 二级菜单永不显示。
            switch (a.PropertyName)
            {
                case nameof(ShellViewModel.CurrentGroup):
                    UpdateSelection();
                    UpdatePanelVisibility();   // 内部按需重建二级菜单列表
                    break;
                case nameof(ShellViewModel.IsHomePage):
                    UpdateSelection();         // 🏠 与模块图标的高亮互斥，首页态要清掉模块高亮
                    UpdatePanelVisibility();
                    break;
                case nameof(ShellViewModel.CurrentItem):
                    UpdateSubSelection();
                    break;
            }
        };
        BuildNavButtons();
        UpdateSelection();
        UpdatePanelVisibility();
    }

    private void UpdateMaxIcon()
    {
        MaxIcon.Data = WindowState == WindowState.Maximized
            ? PathGeometry.Parse("M1,2 L9,2 L9,8 L1,8 Z")
            : PathGeometry.Parse("M0,0 L10,0 L10,10 L0,10 Z");
    }

    private void BuildNavButtons()
    {
        if (_vm == null) return;
        NavPanel.Children.Clear();
        foreach (var g in _vm.Groups.Where(x => !x.IsBottom))
        {
            NavPanel.Children.Add(MakeNavButton(g.Icon, g.Key, g.Title));
        }
        // ⚙ 底部设置按钮的点击由 XAML 上的 Command="{Binding SelectGroupCommand}" 驱动，
        // 这里**不要再挂 Click**：否则每次 DataContext 变化都会重复订阅一次（点一下执行多遍）。
    }

    private Button MakeNavButton(string icon, string key, string title)
    {
        var btn = new Button
        {
            Tag = key,
            Width = 48, Height = 48,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Classes = { "nav-btn" },
            Content = new TextBlock { Text = icon, FontSize = 22, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
        };
        // 一级菜单悬浮提示：显示模块名称（侧栏仅 48px 图标，鼠标悬停可见全名）
        ToolTip.SetTip(btn, title);
        btn.Click += (s, e) => _vm?.SelectGroupCommand.Execute(key);
        return btn;
    }

    private IBrush? Res(string key) => (IBrush?)this.FindResource(key);

    private void UpdateSelection()
    {
        if (_vm == null) return;
        // 首页态：不属于任何分组，模块图标一个都不高亮（否则"首页"和上次的模块会同时高亮）
        var cur = _vm.IsHomePage ? null : _vm.CurrentGroup;
        foreach (var b in NavPanel.Children.OfType<Button>())
            ApplyNavState(b, (string?)b.Tag == cur?.Key);
        ApplyNavState(BtnSettings, cur?.Key == "settings");
        ApplyNavState(BtnHome, _vm.IsHomePage);
    }

    /// <summary>
    /// 切换导航按钮的选中态。
    /// ⚠️ 只动 <c>active</c> class，**不要写 <c>Background</c> / <c>Foreground</c> 本地值**：
    /// 本地值优先级高于样式，一旦写上，<c>Button.nav-btn:pointerover</c> 就永远不生效（鼠标悬停没反应）。
    /// 配色统一在 <c>App.axaml</c> 的 <c>Button.nav-btn.active</c> 里定义。
    /// </summary>
    private static void ApplyNavState(Button b, bool selected)
    {
        if (selected)
        {
            if (!b.Classes.Any(c => c == "active")) b.Classes.Add("active");
        }
        else
        {
            b.Classes.Remove("active");
        }
    }

    /// <summary>
    /// 重算「第二列二级菜单」的可见性与内容区的跨列方式。
    /// 一级菜单<b>没有二级项</b>时，整块二级菜单列收成 0、内容区占满剩余宽度。
    /// 判定用 <c>CurrentGroup.Items.Count</c> 而不是「是不是首页」——这样将来任何
    /// 「只有一级、直接就是一个页面」的模块（分组下不挂任何二级项）都自动适用。
    /// ⚠️ 调用方：VM 的 <c>CurrentGroup</c> 与 <c>IsHomePage</c> 两个属性变化
    ///    <b>都要调用本方法</b>（见 <c>OnDataContextChanged</c> 里的 PropertyChanged 分支）。
    /// </summary>
    private void UpdatePanelVisibility()
    {
        if (_vm == null) return;

        var showSubMenu = !_vm.IsHomePage
                          && _vm.CurrentGroup != null
                          && _vm.CurrentGroup.Items.Count > 0;

        SubPanel.IsVisible = showSubMenu;
        Grid.SetColumn(ContentArea, showSubMenu ? 2 : 1);
        Grid.SetColumnSpan(ContentArea, showSubMenu ? 1 : 2);
        if (showSubMenu) UpdateSubMenu();
    }

    private void UpdateSubMenu()
    {
        if (_vm == null || _vm.CurrentGroup == null) return;
        PanelTitle.Text = _vm.CurrentGroup.Title;
        SubMenu.Children.Clear();
        foreach (var it in _vm.CurrentGroup.Items)
        {
            var isSel = _vm.CurrentItem?.Key == it.Key;
            var row = new Border
            {
                Height = 40,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 2),
                Padding = new Thickness(12, 0),
                Background = isSel ? Res("CardSelectedBrush") : Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Tag = it.Key,
                Child = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 10,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = it.Icon, FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = isSel ? Res("CardSelectedTextBrush") : Res("PrimaryTextBrush") },
                        new TextBlock { Text = it.Title, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = isSel ? Res("CardSelectedTextBrush") : Res("PrimaryTextBrush") }
                    }
                }
            };
            row.PointerPressed += (s, ev) =>
            {
                _vm?.SelectItemCommand.Execute((string?)((Border)s!).Tag);
            };
            SubMenu.Children.Add(row);
        }
    }

    private void UpdateSubSelection()
    {
        if (_vm?.CurrentGroup == null) return;
        foreach (var child in SubMenu.Children.OfType<Border>())
        {
            var isSel = (string?)child.Tag == _vm.CurrentItem?.Key;
            child.Background = isSel ? Res("CardSelectedBrush") : Brushes.Transparent;
            if (child.Child is StackPanel sp)
                foreach (var tb in sp.Children.OfType<TextBlock>())
                    tb.Foreground = isSel ? Res("CardSelectedTextBrush") : Res("PrimaryTextBrush");
        }
    }
}
