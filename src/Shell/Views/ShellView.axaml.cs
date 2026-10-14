using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NewToys.Shared.Services;
using NewToys.Shell.Models;
using NewToys.Shell.Services;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>
/// 主窗口（v3.6：标题栏（含标签栏）/ 左树 / 内容区 / 右详情）。
/// <para>
/// 标签栏与左树是**纯数据驱动**（ItemsControl + 绑定），code-behind 只剩这几件事：
/// ① 关闭拦截（「关闭到托盘」）；② 窗口按钮；③ 标题栏拖拽 + 双击最大化；
/// ④ 标签页区域滚轮切换标签；⑤ 切换后把当前标签**滚进视野**（v3.6）；
/// ⑥ 窗口缩放抓取区 + 大小 / 位置记忆（v3.26）；⑦ 贴靠资格样式补丁（v3.28）。
/// </para>
/// </summary>
public partial class ShellView : Window
{
    private ShellViewModel? _vm;

    /// <summary>窗口大小 / 位置记忆（v3.26，需求 §四）：落本配置 <c>config\window.json</c>；解析不到就纯用默认。</summary>
    private readonly WindowBoundsService? _bounds;

    /// <summary>
    /// 待补设的**外框**（物理像素；v3.29 起连同尺寸一起补）。
    /// 原因：构造期设的位置 / 尺寸可能被首次 Show 覆盖 → 在 <c>OnOpened</c> 再补一次。
    /// </summary>
    private PixelRect? _pendingFrame;

    /// <summary>
    /// 真·退出开关。「关闭按钮最小化到托盘」开启后，<see cref="OnClosing"/> 会把关闭拦下来；
    /// 托盘菜单里的「退出 NewToys」必须把它置 true，否则 <see cref="Window.Close"/> 会被自己拦住。
    /// </summary>
    public bool ForceClose { get; set; }

    public ShellView()
    {
        InitializeComponent();

        _bounds = App.GetService<WindowBoundsService>();
        RestoreWindowBounds();
        // ⛔ NT-034 的圆角开关**不在这里调**：它靠「属性 Changed」下发，且要求 PlatformImpl 已就绪；
        //    构造期调一次若没下发成功，OnOpened 再设同一个值就不会再触发 Changed（值没变）→ 永久失效。
        //    统一放到 OnOpened（句柄已稳）。
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

        // v3.28：**趁窗口句柄还在**存边界 —— Closed 时句柄已销毁，Position 会退化成 (0,0)（实测踩过）
        SaveWindowBounds();

        App.ShutdownTray();
        base.OnClosing(e);
    }

    /// <summary>首次显示后再补一次位置（构造期设的 Position 可能被首次 Show 覆盖）。</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // v3.29：句柄到这一刻才稳 → 在这里装窗口过程钩子（贴靠缝隙的根治）
        InstallSnapFrameFix();

        // NT-034：关闭 DWM 圆角（Win11 顶部两个圆角 → 最大化 / 分屏时露缝）
        ApplyCornerPreference();
        ApplyNoRoundCorners();

        if (_pendingFrame is { } frame) ApplyWindowFrame(frame);
        _pendingFrame = null;
    }

    /// <summary>
    /// 直接按**外框物理像素**摆放窗口。
    /// ⛔ 不走 <c>Width</c>/<c>Height</c>：Avalonia 的 <c>Resize()</c> 会再给外框加一圈边框
    /// （它以为边框在客户区外面）—— 用它做"存取闭环"会让窗口一次比一次大。
    /// </summary>
    private void ApplyWindowFrame(PixelRect frame)
    {
        var h = TryGetPlatformHandle()?.Handle;
        if (h is not { } hwnd || hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, frame.X, frame.Y, frame.Width, frame.Height,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
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

        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.TreeNodeExpanded -= OnTreeNodeExpanded;
        }
        _vm = DataContext as ShellViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.TreeNodeExpanded += OnTreeNodeExpanded;   // NT-037：展开一级分组后把整组滚入视野
        }
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

        // 窗口缩放抓取区（v3.26）：四边 5px + 四角 10px 透明热区 → BeginResizeDrag（八向）+ 对应光标。
        WireResizeGrips();
        UpdateResizeGrips();

        // NT-039：窗口外框（厚度 / 颜色由样式给，这里只切 .maximized / .inactive 两个伪类）
        UpdateWindowFrame();

        // v3.28：给无边框窗口补回「可缩放」标准样式 —— Windows 的贴靠（Win+方向键 / 拖到屏幕边缘 /
        // 拖到顶边最大化）只认带 WS_THICKFRAME 等标准样式的窗口。
        ApplySnapEligibleStyles();

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
    /// NT-037：一级分组展开后，把**整组（分组头 + 子项）**滚入视野 ——
    /// 否则靠底部的分组展开后，子项被左树 ScrollViewer 裁掉、看不见。
    /// 收起时不触发（只在展开时滚）；延后到本帧布局跑完，容器才存在、尺寸才定。
    /// </summary>
    private void OnTreeNodeExpanded(object? sender, TreeMenuItem node)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var idx = _vm?.TreeNodes.IndexOf(node) ?? -1;
            if (idx < 0) return;
            if (TreeControl.ContainerFromIndex(idx) is Control container)
                container.BringIntoView();
        }, DispatcherPriority.Background);
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

    // ==================== 窗口缩放抓取区 + 大小/位置记忆（v3.26，需求 §4.1 / §四） ====================

    /// <summary>
    /// 把 8 个抓取热区接到 <see cref="Window.BeginResizeDrag"/>（八向）。
    /// 🔴 Avalonia 12 的托管装饰在 Win32 上不可用（规范 附录A）→ 自绘是唯一路线。
    /// </summary>
    private void WireResizeGrips()
    {
        AddGrip(GripTop, WindowEdge.North, StandardCursorType.SizeNorthSouth);
        AddGrip(GripBottom, WindowEdge.South, StandardCursorType.SizeNorthSouth);
        AddGrip(GripLeft, WindowEdge.West, StandardCursorType.SizeWestEast);
        AddGrip(GripRight, WindowEdge.East, StandardCursorType.SizeWestEast);
        AddGrip(GripTopLeft, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner);
        AddGrip(GripTopRight, WindowEdge.NorthEast, StandardCursorType.TopRightCorner);
        AddGrip(GripBottomLeft, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner);
        AddGrip(GripBottomRight, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner);
    }

    private void AddGrip(Control grip, WindowEdge edge, StandardCursorType cursor)
    {
        grip.Cursor = new Cursor(cursor);
        grip.PointerPressed += (_, e) =>
        {
            if (WindowState == WindowState.Maximized) return;    // 整层门控之外再判一次，防边缘态
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            BeginResizeDrag(edge, e);
            e.Handled = true;
        };
    }

    /// <summary>最大化时整层关掉命中测试（⛔ 不只视觉隐藏——热区必须真关，否则最大化后还能拖出缩放）。</summary>
    private void UpdateResizeGrips()
    {
        if (ResizeGrips == null) return;
        ResizeGrips.IsHitTestVisible = WindowState != WindowState.Maximized;
    }

    // ==================== 窗口外框（NT-039，需求 §4.1.1 ③） ====================

    /// <summary>
    /// 同步窗口外框的两个状态：**最大化**（收掉外框）与**激活 / 失焦**（切淡色）。
    /// <para>
    /// 🔴 这里**只切 Classes**，厚度与颜色一律由 `App.axaml` 的 `Border.window-frame` 样式给 ——
    /// 在代码里直接写 `BorderBrush` 会变成本地值，压死样式里的状态切换，且主题切换后不会跟着变。
    /// </para>
    /// <para>
    /// ⚠️ `IsActive` 走 `OnPropertyChanged`（与 `WindowState` 同一套路，见 <see cref="OnPropertyChanged"/>）；
    ///    若本地实测该属性在 Win32 不触发，退路是订阅 <c>Activated</c> / <c>Deactivated</c> 事件。
    /// </para>
    /// </summary>
    private void UpdateWindowFrame()
    {
        if (WindowFrameBorder == null) return;

        WindowFrameBorder.Classes.Set("maximized", WindowState == WindowState.Maximized);
        WindowFrameBorder.Classes.Set("inactive", !IsActive);
    }

    /// <summary>
    /// 恢复上次的窗口大小 / 位置（v3.26）。离屏 / 超小值兜底：尺寸不小于 MinWidth/MinHeight；
    /// 位置在所有屏幕工作区外 → 只用尺寸、位置留给系统默认（⛔ 不让窗口跑到看不见的地方）。
    /// </summary>
    private void RestoreWindowBounds()
    {
        try
        {
            var b = _bounds?.Load();
            if (b == null) return;

            // ⚠️ v3.29：window.json 存的是**外框物理像素**（⛔ 不是 DIP、也不是客户区）。
            //    见 SaveWindowBounds 的注释：零内缩之后「客户区 == 外框」，再按客户区存取会一次比一次大。
            var scale = RenderScaling > 0 ? RenderScaling : 1;
            var minW = (int)Math.Ceiling(MinWidth * scale);
            var minH = (int)Math.Ceiling(MinHeight * scale);
            var size = new PixelSize(Math.Max(minW, (int)Math.Round(b.Width)),
                                     Math.Max(minH, (int)Math.Round(b.Height)));

            if (string.Equals(b.State, "Maximized", StringComparison.OrdinalIgnoreCase))
            {
                WindowState = WindowState.Maximized;
                return;
            }

            var pt = new PixelPoint((int)b.X, (int)b.Y);
            var frame = new PixelRect(pt, size);
            if (IsVisibleOnAnyScreen(frame))
            {
                ApplyWindowFrame(frame);
                _pendingFrame = frame;   // OnOpened 再补一次（首次 Show 可能覆盖构造期设的值）
            }
        }
        catch { /* 恢复失败就用默认窗口（1280×800） */ }
    }

    private bool IsVisibleOnAnyScreen(PixelRect rect)
    {
        try
        {
            foreach (var s in Screens.All)
            {
                var wa = s.WorkingArea;
                var iw = Math.Min(rect.Right, wa.Right) - Math.Max(rect.X, wa.X);
                var ih = Math.Min(rect.Bottom, wa.Bottom) - Math.Max(rect.Y, wa.Y);
                if (iw > 0 && ih > 0) return true;
            }
        }
        catch { return true; }   // 拿不到屏幕信息 → 不拦，按原值恢复
        return false;
    }

    /// <summary>关闭时记窗口大小 / 位置；最小化态**不覆盖**（保留上次正常位置）。</summary>
    private void SaveWindowBounds()
    {
        try
        {
            if (_bounds == null) return;
            if (WindowState == WindowState.Minimized) return;

            var h = TryGetPlatformHandle()?.Handle;
            if (h is not { } hwnd || hwnd == IntPtr.Zero) return;

            // 🔴 v3.29：**存外框物理像素**（⛔ 不再存 ClientSize）。
            //    零内缩之后客户区 == 外框，而 Avalonia 的 Resize() 仍按「外框 = 客户区 + 边框」申请 →
            //    若继续存客户区，恢复时会被再加一圈边框，**窗口每次启动都变大一圈**（实测 16px）。
            //    存外框 + 用 SetWindowPos 直接设外框 = 绕开 Avalonia 那层换算，闭环稳定。
            //
            // ⚠️ 最大化时要存「还原后的矩形」（rcNormalPosition），
            //    否则存下来的是比屏幕还大一圈的最大化外框，下次还原会偏。
            int x, y, w2, h2;
            if (WindowState == WindowState.Maximized)
            {
                var pl = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!GetWindowPlacement(hwnd, ref pl)) return;
                x = pl.RcNormalPosition.Left;
                y = pl.RcNormalPosition.Top;
                w2 = pl.RcNormalPosition.Right - pl.RcNormalPosition.Left;
                h2 = pl.RcNormalPosition.Bottom - pl.RcNormalPosition.Top;
            }
            else
            {
                if (!GetWindowRect(hwnd, out var r)) return;
                x = r.Left;
                y = r.Top;
                w2 = r.Right - r.Left;
                h2 = r.Bottom - r.Top;
            }

            _bounds.Save(new WindowBoundsService.WindowBounds
            {
                X = x,
                Y = y,
                Width = w2,
                Height = h2,
                State = WindowState == WindowState.Maximized ? "Maximized" : "Normal"
            });
        }
        catch { /* 记不下来不影响关闭 */ }
    }

    // ==================== Win32 平台细节（v3.28）：真实位置 + 贴靠资格 ====================


    /// <summary>
    /// 给窗口补「可缩放窗口」标准样式（<c>WS_THICKFRAME</c> + <c>WS_CAPTION</c> + 最小 / 最大化 / 系统菜单）。
    /// <para>
    /// 为什么需要（v3.28 查两个源码后定论）：
    /// ① Windows 的贴靠（Win+方向键 / 拖到边缘贴半屏 / 拖到顶边最大化）**由系统执行**，前提是窗口具备标准样式；
    /// ② `WindowDecorations="BorderOnly"` 时 Avalonia 只设 `WS_BORDER`（+ `IsResizable` 时的 `WS_THICKFRAME`）——
    ///    ⛔ **不设 `WS_CAPTION`**，而 `Full` 又会触发 Avalonia 自己画一套窗框（`Window.cs::ComputeDecorationParts`
    ///    里 TitleBar 部件只在 `WindowDecorations == Full` 时才加）→ 两难。折中 = **BorderOnly（Avalonia 不画）+ 这里手工补 `WS_CAPTION`（贴靠资格）**。
    /// ③ 客户区仍归我们自绘（BorderOnly + `ExtendClientAreaToDecorationsHint` → `WM_NCCALCSIZE` 把边框算进客户区、
    ///    DWM 的 NCRenderingPolicy 交给应用画 → ⛔ 不会画出系统标题栏）。
    /// </para>
    /// <para>
    /// ⚠️ Avalonia 在窗口状态变化时会**重算样式**（`UpdateWindowProperties`），把这里补的位抹掉 →
    /// 所以除了开机调用，`WindowState` 变化时也要再补一次（见 <see cref="OnPropertyChanged"/>）。
    /// </para>
    /// </summary>
    private void ApplySnapEligibleStyles()
    {
        try
        {
            var h = TryGetPlatformHandle()?.Handle;
            if (h is not { } hwnd || hwnd == IntPtr.Zero) return;

            var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            var want = style | WS_THICKFRAME | WS_CAPTION | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
            if (want == style) return;

            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(want));
            // 样式变更要 SWP_FRAMECHANGED 才重新算非客户区（否则系统不认这次改动）
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch { /* 补样式失败就维持现状（只是没有系统贴靠，手动拖拽缩放仍可用） */ }
    }

    // ==================== DWM 圆角（NT-034，需求 §4.1） ====================

    /// <summary>
    /// NT-034：走 Avalonia 官方圆角开关（<c>Win32Properties.WindowCornerPreference</c>）。
    /// <para>
    /// 🔴 **版本事实**（2026-10-14 用本地 <c>avalonia.win32\12.1.3</c> 二进制核实，⛔ 别再照抄旧源码结论）：
    /// 12.1.3 **没有** <c>HintsToCornerPreference</c> / <c>_cornerHints</c> / <c>SetWindowCornerHints</c>
    /// （那是旧提交 b9548146 的实现，会把「无 hint」默认成 <c>DWMWCP_ROUND</c>）；
    /// 12.1.3 只有 <c>UpdateWindowCornerPreference</c> + <c>SetWindowCornerPreference</c>，
    /// 默认 = <c>Default(0)</c>（让系统决定 = Win11 圆角）。
    /// ⇒ 「Avalonia 每次 ShowWindow 把 DONOTROUND 覆盖成 ROUND、所以要压帧」**在本版本不成立**（压帧那两轮本就无效）。
    /// </para>
    /// <para>
    /// 仍然走官方开关的理由：它是 Avalonia 自己认可的下发通道（写进 WindowImpl 的偏好字段），
    /// 比我们单方面 P/Invoke 更不容易被后续重算抹掉。真正的存疑点是「下发是否成功 / DWM 是否重算」，
    /// 见 <see cref="ApplyNoRoundCorners"/> 的 <c>SWP_FRAMECHANGED</c> 与 <see cref="ReassertWindowChrome"/>。
    /// </para>
    /// <para>
    /// 🔴 两个调用纪律（来自 <c>Win32Properties.cs</c> 静态构造里的 <c>Changed.AddClassHandler</c>）：
    /// ① 只在 **属性真的变化** 时下发 → ⛔ 别在构造期先调一次「占位」，否则 <c>OnOpened</c> 再设同值
    ///    不再触发 Changed，就永远下发不出去；② 下发要求 <c>window.PlatformImpl</c> 已就绪
    ///    （→ 所以放 <c>OnOpened</c>，不放构造函数）。
    /// </para>
    /// </summary>
    private void ApplyCornerPreference()
    {
        try
        {
            // 内联 IsWindows()：CA1416 只认这种写法（规范「分析器坑」）
            if (OperatingSystem.IsWindows())
                Win32Properties.SetWindowCornerPreference(this, Win32Properties.WindowCornerPreference.DoNotRound);
        }
        catch { /* 非 Win32 平台 / API 缺失 → 退回到下面的手动 DwmSetWindowAttribute */ }
    }

    /// <summary>
    /// 兜底：直接调 DWM 关圆角（Win11 顶部两个圆角）。
    /// <para>⚠️ Win10 无圆角、该属性被忽略；失败一律吞掉。状态变化时 Avalonia 会重算 → 需重设（见 <see cref="OnPropertyChanged"/>）。</para>
    /// </summary>
    private void ApplyNoRoundCorners()
    {
        try
        {
            var h = TryGetPlatformHandle()?.Handle;
            if (h is not { } hwnd || hwnd == IntPtr.Zero) return;

            SetCornerPreferenceNoRound(hwnd);

            // NT-034：设完必须**让 DWM 重算非客户区**，否则典型症状就是"属性设进去了、画面没变"。
            // 只重算边框（不动位置 / 尺寸 / 层级 / 焦点），对窗口状态无副作用。
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            ApplySquareRegion(hwnd);   // NT-034 兜底：矩形窗口区域 → 强制方角
        }
        catch { /* 非 Win11 / 调用失败 → 维持系统默认（仅观感，不影响功能） */ }
    }

    /// <summary>
    /// NT-034 兜底（硬手段）：给窗口套一个**矩形窗口区域**（<c>SetWindowRgn</c>）。
    /// <para>
    /// 🔴 为什么需要它：corner preference 在本窗口上试了 5 轮都没稳定生效（普通窗 / 分屏仍圆角；
    /// 而「最大化没圆角」是 Win11 的固有行为，⛔ 不能算我们的功劳）。
    /// MS 文档《应用圆角》明确：**使用了「窗口区域」的应用，系统不会对其应用圆角** →
    /// 这是绕过 DWM 偏好的硬手段，不依赖 DWM 是否尊重我们。
    /// </para>
    /// <para>
    /// ⚠️ 最大化 / 全屏**必须清掉 region**（传 NULL）—— 否则矩形区域会跟最大化几何打架（露边 / 裁内容）。
    /// ⚠️ 代价：DWM 窗口阴影会一起消失（矩形区域窗口没有圆角也就没有圆角阴影）。
    /// </para>
    /// </summary>
    private static void ApplySquareRegion(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return;

            if (IsWindowMaximized(hwnd))
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            if (!GetWindowRect(hwnd, out var r)) return;
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return;

            var rgn = CreateRectRgn(0, 0, w, h);
            if (rgn == IntPtr.Zero) return;
            SetWindowRgn(hwnd, rgn, true);
            DeleteObject(rgn);      // 系统已复制该区域 → 原对象可以删
        }
        catch { /* 设区域失败就退回系统默认外观 */ }
    }

    [DllImport("gdi32.dll", SetLastError = false)]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = false)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// 只写 DWM 圆角属性（**不发任何消息**）。
    /// <para>
    /// 🔴 为什么单独拆出来：Avalonia 12.1.3 的 <c>ShowWindow</c> → <c>ExtendClientArea()</c> →
    /// <c>UpdateWindowCornerPreference()</c> 会**重算并回写**圆角偏好，把我们设的 DONOTROUND 冲掉
    /// （窗口状态变化 / 全屏 / 贴靠都会触发）→ 必须在**它处理完之后**再压一次（见 <see cref="SnapFrameWndProc"/>）。
    /// ⛔ 从窗口过程里调的这个版本**绝不能带 SWP_FRAMECHANGED**：消息处理中再发消息会递归。
    /// </para>
    /// </summary>
    private static void SetCornerPreferenceNoRound(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return;
            int pref = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* 非 Win11 / 调用失败 → 维持系统默认 */ }
    }

    /// <summary>窗口过程里同步窗口外框的**重入闸门**（<see cref="ReassertWindowChrome"/>）。</summary>
    private static bool _inChromeSync;

    /// <summary>
    /// 窗口尺寸 / 位置 / 样式变化后，把「方角」相关的两件事重做一遍：DWM 圆角偏好 + 矩形窗口区域。
    /// <para>🔴 重入防护：这两个调用本身都可能再触发同类消息 → 没有闸门会无限递归。</para>
    /// </summary>
    private static void ReassertWindowChrome(IntPtr hwnd)
    {
        if (_inChromeSync) return;
        _inChromeSync = true;
        try
        {
            SetCornerPreferenceNoRound(hwnd);
            ApplySquareRegion(hwnd);
        }
        catch { /* 同步失败不影响现有外观 */ }
        finally { _inChromeSync = false; }
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    /// <summary>⚠️ 枚举值抄自 dwmapi.h：DEFAULT=0 / **DONOTROUND=1** / ROUND=2 / ROUNDSMALL=3。
    /// 写反（用 2）= 变成"强制圆角"，反而制造圆角 —— 2026-10-14 实测踩过。</summary>
    private const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll", SetLastError = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // 🔴 NT-034 排查期曾在此处挂过一段诊断（读回 DWM 圆角值 + 写 corner-probe.txt）——
    //    2026-10-14 验证通过后已删除。两条结论留档，⛔ 以后再排查别再走这两条死路：
    //    ① 输出到 `Window.Title` 无效：本窗口无边框自绘，标题栏是自己画的 Grid，根本不显示 Title；
    //    ② `DwmGetWindowAttribute` **不支持读** `DWMWA_WINDOW_CORNER_PREFERENCE`(33)（该属性只用于 Set）
    //       → 永远读到失败值，不能当"下发是否成功"的判据。

    /// <summary>
    /// v3.29 贴靠缝隙的**根治**：接管窗口过程，在 <c>WM_NCCALCSIZE</c> 上把 Avalonia 的回答
    /// 「客户区 = 外框 − 边框」改回「客户区 = 整个外框」。
    /// <para>
    /// 🔴 为什么必须在这层动手：那圈被扣掉 ~8px 的厚度是按**窗口样式**算的，而样式里的
    /// <c>WS_THICKFRAME|WS_CAPTION</c> 是我们为**贴靠资格**补的（去掉它 = 失去贴靠，回归更糟）。
    /// 平时 DWM 在那圈上画标准窗框所以看不出来；**贴靠后 DWM 不画了** → 左右下露缝
    /// （顶部被 40px 标题栏盖住，所以只有三边看得到）。
    /// </para>
    /// <para>⚠️ 装不上就什么都不做 —— 这类代码出错的代价是"窗口完全不响应消息"，失败路径必须保守。</para>
    /// </summary>
    private void InstallSnapFrameFix()
    {
        try
        {
            var h = TryGetPlatformHandle()?.Handle;
            if (h is not { } hwnd || hwnd == IntPtr.Zero) return;
            if (_originalWndProc != IntPtr.Zero) return;              // 只装一次

            // ⛔ 委托必须挂在静态字段上：局部变量会被 GC 回收 → 回调时进程直接崩
            _snapFixProc = SnapFrameWndProc;
            var ptr = Marshal.GetFunctionPointerForDelegate(_snapFixProc);
            var old = SetWindowLongPtr(hwnd, GWLP_WNDPROC, ptr);
            if (old == IntPtr.Zero) { _snapFixProc = null; return; }   // 装失败 → 保持原样
            _originalWndProc = old;
        }
        catch { /* 装不上就维持现状 */ }
    }

    private static IntPtr SnapFrameWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_originalWndProc == IntPtr.Zero)
            return DefWindowProc(hWnd, msg, wParam, lParam);

        // wParam == 1 → lParam 指向 NCCALCSIZE_PARAMS；rgrc[0] 就在 lParam 的开头 16 字节
        if (msg == WM_NCCALCSIZE && wParam.ToInt64() == 1)
        {
            // ① 抄下系统送进来的"新外框"
            var l0 = Marshal.ReadInt32(lParam, 0);
            var t0 = Marshal.ReadInt32(lParam, 4);
            var r0 = Marshal.ReadInt32(lParam, 8);
            var b0 = Marshal.ReadInt32(lParam, 12);

            // ② 先转给 Avalonia：它按自己的逻辑算完客户区、写回 rgrc[0]
            CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);

            // ③ 最大化**不**覆盖：最大化时外框本来就比屏幕大一圈（边框被甩到屏幕外），
            //    此时若零内缩，内容会伸出屏幕 8px、标题栏顶边被切掉。
            if (!IsWindowMaximized(hWnd))
            {
                Marshal.WriteInt32(lParam, 0, l0);
                Marshal.WriteInt32(lParam, 4, t0);
                Marshal.WriteInt32(lParam, 8, r0);
                Marshal.WriteInt32(lParam, 12, b0);
            }
            return IntPtr.Zero;      // 返回 0 = 已处理，系统拿 rgrc[0] 当客户区
        }

        var result = CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);

        // NT-034：**Avalonia 处理完之后**再压一次 DONOTROUND ——
        // 它会在 ShowWindow → ExtendClientArea → UpdateWindowCornerPreference 里把偏好回写成 Default，
        // 窗口状态变化 / 全屏 / 贴靠都会触发。先转发、后压，顺序不能反。
        // ⛔ 这里只能用「只设属性」的版本（SetCornerPreferenceNoRound），带 SWP_FRAMECHANGED 会递归。
        if (msg == WM_WINDOWPOSCHANGED || msg == WM_SIZE || msg == WM_STYLECHANGED || msg == WM_SHOWWINDOW)
            ReassertWindowChrome(hWnd);

        return result;
    }

    private static bool IsWindowMaximized(IntPtr hwnd)
    {
        try
        {
            var pl = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            return GetWindowPlacement(hwnd, ref pl) && pl.ShowCmd == SW_SHOWMAXIMIZED;
        }
        catch { return false; }
    }

    private const int GWL_STYLE = -16;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_CAPTION = 0x00C00000L;   // = WS_BORDER | WS_DLGFRAME，贴靠资格的关键位
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_SYSMENU = 0x00080000L;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;   // v3.29：贴靠校正时⛔别抢焦点
    private const uint SWP_FRAMECHANGED = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ===== v3.29 贴靠缝隙根治：接管 WM_NCCALCSIZE（原理见 InstallSnapFrameFix 的注释） =====

    private const uint SWP_NOOWNERZORDER = 0x0200;

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_SHOWWINDOW = 0x0018;
    private const uint WM_WINDOWPOSCHANGED = 0x0047;
    private const uint WM_STYLECHANGED = 0x007D;
    private const int SW_SHOWMAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public POINT PtMinPosition;
        public POINT PtMaxPosition;
        public RECT RcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>窗口过程委托。⛔ **必须常驻静态字段** —— 委托被 GC 回收后窗口会直接崩。</summary>
    private delegate IntPtr WndProcFunc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static WndProcFunc? _snapFixProc;
    private static IntPtr _originalWndProc;

    // ⚠️ 本项目锁定 win-x64（见 src/Directory.Build.props）→ 用 Ptr 版导出 ✓（32 位 Windows 上导出名不同）
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                            int x, int y, int cx, int cy, uint uFlags);

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

    /// <summary>同步最大化 / 还原按钮图标 + 窗口缩放抓取区门控（Win+↑↓、双击、拖到顶边吸附等操作不会通知我们，必须监听）。</summary>
    // ⚠️ 判断属性一律用 == 逐个比（分析器要求常量比较，⛔ 别用 is A or B）
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            UpdateMaxIcon();
            UpdateResizeGrips();   // v3.26：最大化时关掉缩放热区
            UpdateWindowFrame();   // NT-039：最大化时收掉外框
            ApplySnapEligibleStyles();   // v3.28：状态变化时 Avalonia 会重算样式 → 把贴靠资格位补回去
            ApplyNoRoundCorners();       // NT-034：状态变化时也重设 DWM 圆角偏好（防被重算抹掉）
        }
        else if (change.Property == IsActiveProperty)
        {
            UpdateWindowFrame();   // NT-039：失焦 → 外框切 WindowBorderInactiveBrush
        }
    }


    private void ToggleMaxRestore()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaxIcon()
    {
        // v3.22：最大化 = 单个空心方；还原 = 两个错位方。
        // ⚠️ 图标是 Border（不是 Path）—— Path 在 BareButtonTheme 里渲染不出来，见 ShellView 注释。
        var maximized = WindowState == WindowState.Maximized;
        MaxIconSquare.IsVisible = !maximized;
        RestoreIcon.IsVisible = maximized;
    }
}
