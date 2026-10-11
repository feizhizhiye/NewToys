using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>
/// 主窗口（v3.6：标题栏（含标签栏）/ 左树 / 内容区 / 右详情）。
/// <para>
/// 标签栏与左树是**纯数据驱动**（ItemsControl + 绑定），code-behind 只剩五件事：
/// ① 关闭拦截（「关闭到托盘」）；② 窗口按钮；③ 标题栏拖拽 + 双击最大化；
/// ④ 标签页区域滚轮切换标签；⑤ 切换后把当前标签**滚进视野**（v3.6）。
/// </para>
/// </summary>
public partial class ShellView : Window
{
    private ShellViewModel? _vm;

    /// <summary>
    /// 真·退出开关。「关闭按钮最小化到托盘」开启后，<see cref="OnClosing"/> 会把关闭拦下来；
    /// 托盘菜单里的「退出 NewToys」必须把它置 true，否则 <see cref="Window.Close"/> 会被自己拦住。
    /// </summary>
    public bool ForceClose { get; set; }

    public ShellView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 「关闭按钮最小化到托盘」的落点（「设置 → 通用」里那个开关）。
    /// 拦下来的方式：勾了开关 → <c>e.Cancel = true</c> 并 <see cref="Window.Hide"/>；
    /// 没勾 / 托盘「退出」置了 <see cref="ForceClose"/> → 正常关，并收掉托盘图标。
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

    /// <summary>跟着页面的 DataContext 走：订阅 ShellViewModel，用于「切换标签后把当前标签滚进视野」。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as ShellViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        BtnMin.Click += (s, ev) => { WindowState = WindowState.Minimized; ev.Handled = true; };
        BtnMax.Click += (s, ev) => { ToggleMaxRestore(); ev.Handled = true; };
        BtnClose.Click += (s, ev) => { Close(); ev.Handled = true; };
        UpdateMaxIcon();

        // 拖动 / 双击最大化：挂在整个标题栏 Grid 上；点中任意按钮时不拖拽（见 IsOverButton）。
        // ⚠️ 标题栏 Grid 与标签区两侧的留白 Panel 都显式设了 Background="Transparent"，
        //    否则 Avalonia 不做命中测试，鼠标事件直接穿透，拖不动。
        TitleBarGrid.PointerPressed += OnTitleBarPointerPressed;

        // 标签页区域滚轮 = 切换标签。用 **隧道** 方式注册：滚动容器（ScrollViewer）会先把
        // 滚轮当成横向滚动吃掉，隧道阶段拦下来才能变成"切标签"。
        TabStrip.AddHandler(InputElement.PointerWheelChangedEvent, OnTabStripWheel, RoutingStrategies.Tunnel);
    }

    // ==================== 标签栏：滚轮切换 + 切换后滚到可见 ====================

    /// <summary>标签页区域滚轮：上滚 = 上一个标签，下滚 = 下一个标签（循环）。</summary>
    private void OnTabStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;

        if (e.Delta.Y > 0) vm.PrevTabCommand.Execute(null);
        else if (e.Delta.Y < 0) vm.NextTabCommand.Execute(null);

        e.Handled = true;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.ActiveTab)) return;

        // 延后到本帧布局跑完再滚 —— 否则容器可能还没生成 / 尺寸还没定，BringIntoView 会算错。
        Dispatcher.UIThread.Post(ScrollActiveTabIntoView, DispatcherPriority.Background);
    }

    /// <summary>
    /// 让**当前标签始终可见**（v3.6）：用「最小滚动」把它带进视野 —— 已经在视野里就一点都不滚，
    /// 只有露出视野才滚最小的距离（类 Chrome 行为）。
    /// </summary>
    private void ScrollActiveTabIntoView()
    {
        if (_vm?.ActiveTab is not { } tab) return;

        var index = _vm.Tabs.IndexOf(tab);
        if (index < 0) return;

        if (TabsControl.ContainerFromIndex(index) is Control container)
            container.BringIntoView();
    }

    // ==================== 标题栏：拖拽 / 双击最大化 ====================

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs ev)
    {
        if (!ev.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 鼠标点在任意按钮上 → 不处理拖拽，让按钮自己响应
        if (IsOverButton(ev.Source as InputElement)) return;

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
    /// 从触发元素向上查找，命中链里是否有 <see cref="Button"/>。
    /// ⚠️ 用「祖先是不是 Button」通用判定，新增标题栏按钮时**无需**再登记白名单
    /// （旧实现要逐个 ReferenceEquals，漏登记就会把点击变成拖窗口）。
    /// </summary>
    private static bool IsOverButton(InputElement? hit)
    {
        while (hit != null)
        {
            if (hit is Button) return true;
            hit = hit.Parent as InputElement;
        }
        return false;
    }

    /// <summary>同步最大化 / 还原按钮图标（Win+↑↓、双击、拖到顶边吸附等操作不会通知我们，必须监听）。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
            UpdateMaxIcon();
    }

    private void ToggleMaxRestore()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaxIcon()
    {
        MaxIcon.Data = WindowState == WindowState.Maximized
            ? PathGeometry.Parse("M1,2 L9,2 L9,8 L1,8 Z")
            : PathGeometry.Parse("M0,0 L10,0 L10,10 L0,10 Z");
    }
}
