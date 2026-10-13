using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NewToys.Shared.Services;
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

    /// <summary>待补设的位置（v3.28 实测：构造期设的 <c>Position</c> 可能被首次 Show 覆盖 → 在 OnOpened 再补一次）。</summary>
    private PixelPoint? _pendingPosition;

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
        if (_pendingPosition is { } p && Position != p) Position = p;
        _pendingPosition = null;
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

        // 窗口缩放抓取区（v3.26）：四边 5px + 四角 10px 透明热区 → BeginResizeDrag（八向）+ 对应光标。
        WireResizeGrips();
        UpdateResizeGrips();

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

            var w = Math.Max(MinWidth, b.Width);
            var h = Math.Max(MinHeight, b.Height);
            Width = w;
            Height = h;

            if (string.Equals(b.State, "Maximized", StringComparison.OrdinalIgnoreCase))
            {
                WindowState = WindowState.Maximized;
                return;
            }

            // 位置是物理像素、尺寸是逻辑单位 → 乘渲染缩放后再和工作区比
            var scale = RenderScaling > 0 ? RenderScaling : 1;
            var pt = new PixelPoint((int)b.X, (int)b.Y);
            var rect = new PixelRect(pt, new PixelSize((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale)));
            if (IsVisibleOnAnyScreen(rect))
            {
                Position = pt;
                _pendingPosition = pt;   // OnOpened 再补一次（首次 Show 可能覆盖构造期设的位置）
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

            // ⚠️ 位置必须走 GetWindowRect 读真值：`Window.Position` 在句柄已销毁时会退化成 (0,0)，
            //    而 v3.28 实测正是踩到这个（存出来 X=0,Y=0 → 下次启动落在屏幕左上）。
            var pos = TryGetWindowTopLeft() ?? Position;

            _bounds.Save(new WindowBoundsService.WindowBounds
            {
                X = pos.X,
                Y = pos.Y,
                // ⚠️ 尺寸存 ClientSize：OS 拖拽改的是实际尺寸，Width/Height 可能还停在初始请求值
                Width = ClientSize.Width,
                Height = ClientSize.Height,
                State = WindowState == WindowState.Maximized ? "Maximized" : "Normal"
            });
        }
        catch { /* 记不下来不影响关闭 */ }
    }

    // ==================== Win32 平台细节（v3.28）：真实位置 + 贴靠资格 ====================

    /// <summary>
    /// 读窗口真实左上角（物理像素）。比 <c>Window.Position</c> 可靠：后者在句柄失效后返回 (0,0)。
    /// 读不到 → null（调用方退回 <c>Position</c>）。
    /// </summary>
    private PixelPoint? TryGetWindowTopLeft()
    {
        try
        {
            var h = TryGetPlatformHandle()?.Handle;
            if (h is { } hwnd && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
                return new PixelPoint(r.Left, r.Top);
        }
        catch { /* 拿不到就算了 */ }
        return null;
    }

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

    // ===== v3.29 贴靠缝隙修补：读真实客户区与显示器工作区（全程**物理像素**，⛔ 不做 DPI 换算） =====

    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int CbSize;
        public RECT RcMonitor;
        public RECT RcWork;
        public uint DwFlags;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>把客户区坐标 (0,0) 换成屏幕坐标 —— 用它反推「客户区左上角离外框多远」。</summary>
    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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
            ApplySnapEligibleStyles();   // v3.28：状态变化时 Avalonia 会重算样式 → 把贴靠资格位补回去
        }
        else if (change.Property == TopLevel.ClientSizeProperty)
        {
            FixSnappedBounds();   // v3.29：Win+←/→ 贴靠后补掉左右下的缝隙
        }
    }

    // ==================== 贴靠缝隙修补（v3.29） ====================
    //
    // 🔴 **根因（读 Avalonia 12.1.3 源码定论，不是猜的）**：
    //    `WindowImpl.AppWndProc` 的 `WM_NCCALCSIZE` 在「客户区已扩展到装饰区」时，会按**当前窗口样式**
    //    算出边框厚度，再把它**留在客户区之外**：
    //        rect.left   -= borderThickness.left;      // 左右下保留
    //        rect.right  -= borderThickness.right;
    //        rect.bottom -= borderThickness.bottom;
    //        rect.top    -= 0;                          // 顶部因为我们的标题栏被清零
    //    平时 DWM 会在那 ~8px 上画标准窗框（所以看着正常、验收「外观 ✅」）；
    //    但 **Win+←/→ 贴靠后 DWM 不再画这圈框**，而客户区仍被扣掉它 → 左右下露出缝隙（顶部被 40px 标题栏盖住看不出来）。
    //
    // ✅ **修法**：贴靠后把**窗口外框**按这圈厚度向外撑（最大化窗口本来就是这么处理的 —— 外框比屏幕大一圈、边框在屏外）。
    //    厚度**实测得来**（GetWindowRect 外框 vs ClientToScreen+GetClientRect 客户区），⛔ 不查表、不猜 8px、不碰 DPI 换算（全程物理像素）。
    //
    // ⚠️ 只在「外框**正好等于**半个屏幕工作区」时动手 —— 手工拖到半屏大小几乎不可能精确到 ±2px，
    //    所以不会误伤手动调整；校正完外框就不再等于半屏 → 不会来回抖。

    /// <summary>上次校正的时间戳（防同一帧重复校正）。</summary>
    private long _lastSnapFixTick;

    private void FixSnappedBounds()
    {
        if (WindowState != WindowState.Normal) return;          // 最大化由系统自己管（那条路径没问题）

        var now = Environment.TickCount64;
        if (now - _lastSnapFixTick < 300) return;               // 刚校正过，等窗口稳定

        var h = TryGetPlatformHandle()?.Handle;
        if (h is not { } hwnd || hwnd == IntPtr.Zero) return;

        if (!GetWindowRect(hwnd, out var outer)) return;

        // ① 实测四边的"被扣掉"厚度（外框 vs 客户区）
        if (!GetClientRect(hwnd, out var client)) return;
        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin)) return;

        var insetLeft = origin.X - outer.Left;
        var insetTop = origin.Y - outer.Top;
        var insetRight = outer.Right - (origin.X + client.Right);
        var insetBottom = outer.Bottom - (origin.Y + client.Bottom);

        if (insetLeft == 0 && insetRight == 0 && insetBottom == 0 && insetTop == 0) return;   // 没有留边 → 没事可做

        // ② 该显示器的工作区（贴靠的目标矩形）
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;
        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var wa = info.RcWork;

        // ③ 当前外框是否"正好"是左半屏 / 右半屏
        var half = (wa.Right - wa.Left) / 2;
        if (half <= 0) return;

        var isLeftHalf = Near(outer.Left, wa.Left) && Near(outer.Right, wa.Left + half)
                         && Near(outer.Top, wa.Top) && Near(outer.Bottom, wa.Bottom);
        var isRightHalf = Near(outer.Left, wa.Right - half) && Near(outer.Right, wa.Right)
                          && Near(outer.Top, wa.Top) && Near(outer.Bottom, wa.Bottom);
        if (!isLeftHalf && !isRightHalf) return;

        // ④ 把外框向外撑：让**客户区**正好等于半屏（缝隙就没了）
        var targetLeft = isLeftHalf ? wa.Left : wa.Right - half;
        var targetRight = isLeftHalf ? wa.Left + half : wa.Right;

        var x = targetLeft - insetLeft;
        var y = wa.Top - insetTop;
        // ⚠️ 别叫 w / h：本方法开头已有 `var h = TryGetPlatformHandle()?.Handle`（句柄）→ 同名会撞 CS0128
        var newWidth = (targetRight + insetRight) - x;
        var newHeight = (wa.Bottom + insetBottom) - y;
        if (newWidth <= 0 || newHeight <= 0) return;

        _lastSnapFixTick = now;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, newWidth, newHeight,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>两px 内算"正好"（贴靠矩形是系统给的，取整误差在这范围内）。</summary>
    private static bool Near(int a, int b) => Math.Abs(a - b) <= 2;

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
