using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NewToys.Shared.Services;
using NewToys.Shell.Controls;
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
/// ⑥ 窗口缩放抓取区 + 大小 / 位置记忆（v3.26）；
/// ⑦ DWM 圆角 / 矩形区域压制（三保险）+ 零内缩（消灭 ≈8px 非客户区边框）+ 接管窗口过程做「转发后置」压制
/// —— 贴靠资格由官方 `WindowDecorations="Full"` 自带（手工补 `WS_CAPTION` 的 `ApplySnapEligibleStyles` 已删，）；
/// —— `WM_NCCALCSIZE` 零内缩是**Windows 独有的例外**（钩子只在 Windows 装得上，Linux 自然不走）；
/// —— 🔴 2026-10-15 定案：**官方 CSD 路线 + 这一处零内缩**（跨平台只有官方 CSD 能对接，需求 §4.1.1）。
/// ⑧ 全屏按钮 + <c>F11</c> / <c>Esc</c>。
/// </para>
/// <para>
/// 🔴 ⑦ 的全部实现（DllImport / Win32 结构 / 钩子 / 贴靠纠偏）在 **`ShellView.Win32.cs`**（partial，2026-10-21 收敛）：
/// 本文件只留业务逻辑与它们的调用点，⛔ 别把 Win32 那段搬回来。
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

    /// <summary>
    /// 全局弹出卡片层（确认 / 提示 / 输入的唯一呈现器）。
    /// 🔴 **⛔ 不在 XAML 里声明**：XAML 引用该 UserControl 会连带整个窗口的 x:Name 回填失败
    /// （运行时表现 = 启动即崩在 OnLoaded 的 BtnMin，2026-10-20 实测；IL 侧 29 个 stfld 都在、
    /// 树却拿不到字段）⇒ 由构造末尾**代码挂到根 Grid**，叠在最顶层、不占布局。
    /// </summary>
    public DialogCardHost? DialogCards { get; private set; }

    public ShellView()
    {
        InitializeComponent();

        // 弹出卡片层代码挂载（原因见 DialogCards 注释）——根 Grid 的最后一个子元素，
        // 绘制在窗口外框 / 缩放热区之上；未展示时整层 IsVisible=False ⇒ 不吃命中测试。
        var cards = new DialogCardHost();
        DialogCards = cards;
        if (Content is Grid rootGrid) rootGrid.Children.Add(cards);

        _bounds = App.GetService<WindowBoundsService>();
        RestoreWindowBounds();
        // ⛔ 的圆角开关**不在这里调**：它靠「属性 Changed」下发，且要求 PlatformImpl 已就绪；
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

        // **趁窗口句柄还在**存边界 —— Closed 时句柄已销毁，Position 会退化成 (0,0)（实测踩过）
        SaveWindowBounds();

        App.ShutdownTray();
        base.OnClosing(e);
    }

    /// <summary>首次显示后再补一次位置（构造期设的 Position 可能被首次 Show 覆盖）。</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 句柄到这一刻才稳 → 在这里装窗口过程钩子（贴靠缝隙的根治）
        InstallSnapFrameFix();

        // 关闭 DWM 圆角（Win11 顶部两个圆角 → 最大化 / 分屏时露缝）
        ApplyCornerPreference();
        ApplyNoRoundCorners();

        if (_pendingFrame is { } frame) ApplyWindowFrame(frame);
        _pendingFrame = null;
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
            _vm.TreeNodeExpanded += OnTreeNodeExpanded;   // 展开一级分组后把整组滚入视野
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 防御（2026-10-20 启动崩溃复盘）：x:Name 回填异常时字段全为 null（BtnMin 首当其冲）。
        // 先按名字兜底找一次；兜底也拿不到就放弃接线（崩溃 log 已留现场，总比启动即崩好）。
        if (BtnMin is null) BtnMin = this.FindControl<Button>("BtnMin");
        if (BtnMin is null) return;

        BtnMin.Click += (s, ev) => { WindowState = WindowState.Minimized; ev.Handled = true; };
        BtnMax.Click += (s, ev) => { ToggleMaxRestore(); ev.Handled = true; };
        BtnClose.Click += (s, ev) => { Close(); ev.Handled = true; };
        BtnFullScreen.Click += (s, ev) => { ToggleFullScreen(); ev.Handled = true; };   // 
        UpdateMaxIcon();

        // F11 切全屏 / Esc 退全屏。⚠️ 用**隧道**注册（参照标签栏滚轮的先例）——
        // 事件从根往下传，Window 先拿到，页面内的输入框 / 列表不会把它吃掉。
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        // 拖动 / 双击最大化：挂在整个标题栏 Grid 上；点中任意按钮时不拖拽（见 IsOverButton）。
        // ⚠️ 标题栏 Grid 与标签区两侧的留白 Panel 都显式设了 Background="Transparent"，
        //    否则 Avalonia 不做命中测试，鼠标事件直接穿透，拖不动。
        TitleBarGrid.PointerPressed += OnTitleBarPointerPressed;

        // 窗口缩放抓取区（v3.26）：四边 5px + 四角 10px 透明热区 → BeginResizeDrag（八向）+ 对应光标。
        WireResizeGrips();
        UpdateResizeGrips();

        // 窗口外框（厚度 / 颜色由样式给，这里只切 .maximized / .inactive 两个伪类）
        UpdateWindowFrame();

        // 标签页区域滚轮 = 切换标签。用 **隧道** 方式注册：滚动容器（ScrollViewer）会先把
        // 滚轮当成横向滚动吃掉，隧道阶段拦下来才能变成"切标签"。
        TabStrip.AddHandler(InputElement.PointerWheelChangedEvent, OnTabStripWheel, RoutingStrategies.Tunnel);

        // 标签栏宽度跟随标签数量（类浏览器）—— 标签少时 `＋` 紧跟最后一个标签；标签溢出时把滚动区
        // 收到可用宽度，`＋` 常驻最右、始终可见。窗口宽度 / 标签集合变化都要重算。
        TabStrip.SizeChanged += (_, _) => SyncTabStripWidthCap();
        TabsControl.SizeChanged += (_, _) => SyncTabStripWidthCap();
        NewTabButton.SizeChanged += (_, _) => SyncTabStripWidthCap();
    }

    /// <summary>
    /// 把标签滚动区的最大宽度收到「标签栏宽度 − `＋` 按钮宽度」：
    /// 未溢出时 MaxWidth 不起作用（Auto 列仍按内容量）⇒ `＋` 贴着最后一个标签；溢出时滚动区被收窄 ⇒ `＋` 不被挤出去。
    /// </summary>
    private void SyncTabStripWidthCap()
    {
        var available = TabStrip.Bounds.Width - NewTabButton.Bounds.Width - NewTabButton.Margin.Left;
        if (!double.IsFinite(available) || available < 0) return;
        TabsScroll.MaxWidth = available;
    }

    // ==================== 标签栏：滚轮切换 + 切换后滚到可见 ====================

    /// <summary>标签页区域滚轮：上滚 = 上一个标签，下滚 = 下一个标签（**到头即止，不循环**）。</summary>
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
    /// 一级分组展开后，把**整组（分组头 + 子项）**滚入视野 ——
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

        // 🔴 v3.66 第十八轮：标签栏**不再有固定列**（首页与其余标签同处滚动区）⇒ 一律按 `Tabs` 索引找容器。
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

    /// <summary>
    /// 最大化 / **全屏**时整层关掉命中测试（⛔ 不只视觉隐藏——热区必须真关，否则还能拖出缩放）。
    /// <para>全屏态同样不能缩放（窗口本就铺满屏，拖边只会把状态搞乱）。</para>
    /// </summary>
    private void UpdateResizeGrips()
    {
        if (ResizeGrips == null) return;
        ResizeGrips.IsHitTestVisible = WindowState != WindowState.Maximized
                                       && WindowState != WindowState.FullScreen;
    }

    // ==================== 窗口外框（需求 §4.1.1 ③） ====================

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

        // 全屏态与最大化同处理 —— 1px 外框收掉（不贴屏幕边）。
        WindowFrameBorder.Classes.Set("maximized", WindowState == WindowState.Maximized
                                                   || WindowState == WindowState.FullScreen);
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
            //    见 SaveWindowBounds 的注释：一律按外框存取，避免 Avalonia 的「外框 = 客户区 + 边框」换算把窗口撑大。
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
            // 窗口几何只能从 Win32 读（GetWindowPlacement / GetWindowRect）⇒ 非 Windows 直接跳过落盘
            if (!OperatingSystem.IsWindows()) return;   // 内联 IsWindows()：CA1416 只认这种写法

            var h = TryGetPlatformHandle()?.Handle;
            if (h is not { } hwnd || hwnd == IntPtr.Zero) return;

            // 🔴 v3.29：**存外框物理像素**（⛔ 不存 ClientSize）。恢复一律用 `SetWindowPos` 直接设外框
            //    = 绕开 Avalonia「外框 = 客户区 + 边框」那层换算，存取同一坐标系 → 闭环稳定。
            //    （早期存客户区时恢复会被再加一圈边框 → **窗口每次启动变大一圈**，实测 16px。客户区内外缩与
            //      存取口径无关：官方 CSD 下客户区被内缩，但这里进出的都是**外框**，仍然自洽。）
            // ⚠️ 最大化时要存「还原后的矩形」（rcNormalPosition），
            //    否则存下来的是比屏幕还大一圈的最大化外框，下次还原会偏。
            // 全屏态与最大化同处理 —— 存**还原后的矩形**。
            // ⛔ 否则存下来的是铺满整屏的外框 → 下次启动是一个「跟屏幕一样大的普通窗口」（位置还偏）。
            // ⚠️ 2026-10-21：读外框的两条 Win32 路径（GetWindowPlacement / GetWindowRect）收进
            //    ShellView.Win32.cs 的 TryReadFrameGeometry —— 主文件不再出现 Win32 类型。
            var preferRestoreRect = WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen;
            if (!TryReadFrameGeometry(hwnd, preferRestoreRect, out var x, out var y, out var w2, out var h2)) return;

            _bounds.Save(new WindowBoundsService.WindowBounds
            {
                X = x,
                Y = y,
                Width = w2,
                Height = h2,
                State = preferRestoreRect ? "Maximized" : "Normal"
            });
        }
        catch { /* 记不下来不影响关闭 */ }
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

    /// <summary>同步最大化 / 还原按钮图标 + 窗口缩放抓取区门控（Win+↑↓、双击、拖到顶边吸附等操作不会通知我们，必须监听）。</summary>
    // ⚠️ 判断属性一律用 == 逐个比（分析器要求常量比较，⛔ 别用 is A or B）
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            _isFullScreen = WindowState == WindowState.FullScreen;   // 先记状态，下面 region 要用
            UpdateMaxIcon();
            UpdateResizeGrips();   // v3.26：最大化时关掉缩放热区
            UpdateWindowFrame();   // 最大化 / 全屏时收掉外框
            ApplyNoRoundCorners();       // 状态变化时也重设 DWM 圆角偏好（防被重算抹掉）
        }
        else if (change.Property == IsActiveProperty)
        {
            UpdateWindowFrame();   // 失焦 → 外框切 WindowBorderInactiveBrush
        }
    }

    private void ToggleMaxRestore()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ==================== 全屏（需求 §4.1） ====================

    /// <summary>
    /// 全屏开关。12.1.3 **没有** `ToggleFullScreen` / `ExitFullScreen`（已核实）→ 自己切 `WindowState`。
    /// <para>
    /// ⚠️ 退出一律回 `Normal`（⛔ 不回 `Maximized`）：进全屏前的旧状态Avalonia 自己会记，
    ///    我们额外记住反而会和它打架。
    /// </para>
    /// </summary>
    private void ToggleFullScreen()
        => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    /// <summary>
    /// `F11` 切换全屏；`Esc` **仅在全屏态下**退出（⛔ 平时不吞 Esc —— 页面里的取消 / 关闭浮层要用它）。
    /// <para>
    /// `Ctrl+,` = 打开**应用级设置页**（需求 §4.3 / §4.9.2）。
    /// 🔴 折叠左树后版本行的齿轮仍在，`Ctrl+,` 是它的备用入口（约定俗成的设置快捷键）。
    /// ⚠️ 走隧道路由（Tunnel）⇒ 即使焦点在页内输入框也照样命中；判定用 `KeyModifiers` 而不是 `KeyGesture`。
    /// </para>
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma && e.KeyModifiers == KeyModifiers.Control)
        {
            _vm?.OpenSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 搜索框挂上视觉树：**清空上次状态 + 聚焦 + 全选**（弹出即可直接打字）。
    /// <para>⚠️ 弹窗内容在**弹出树**里、不在本窗口的 NameScope ⇒ 不给它 `x:Name`，一律从 `sender` 取。</para>
    /// <para>🔴 弹出内容在**弹出树**里 ⇒ 绑定的 `DataContext` 未必继承到本窗口；这里兜底补一次
    /// （补不上 ⇒ 结果列表静默空白，且不报错）。</para>
    /// </summary>
    private void OnSearchBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.Parent is Control host && host.DataContext == null) host.DataContext = DataContext;
        _vm?.ResetSearch();
        tb.Focus();
        tb.SelectAll();
    }

    /// <summary>
    /// 搜索结果键盘操作：**↑↓** 移动高亮、**回车**打开高亮项（无高亮则取第一条）。
    /// 打开后关掉弹窗；未命中不做事（提示已由 <c>SearchHint</c> 显示）。
    /// </summary>
    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;

        switch (e.Key)
        {
            case Key.Enter:
            {
                e.Handled = true;
                var target = _vm.SelectedSearchResult
                             ?? (_vm.SearchResults.Count > 0 ? _vm.SearchResults[0] : null);
                if (target == null) return;
                BtnSettingsSearch.Flyout?.Hide();
                _vm.OpenSearchResultCommand.Execute(target);
                break;
            }
            case Key.Down:
                e.Handled = true;
                _vm.MoveSearchSelection(1);
                break;
            case Key.Up:
                e.Handled = true;
                _vm.MoveSearchSelection(-1);
                break;
        }
    }

    /// <summary>
    /// 结果列表**双击** = 打开：单击只改高亮，⛔ 不导航 —— 免得手滑点到就跳走。
    /// </summary>
    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm == null || sender is not ListBox list || list.SelectedItem is not SearchTarget target) return;
        e.Handled = true;
        BtnSettingsSearch.Flyout?.Hide();
        _vm.OpenSearchResultCommand.Execute(target);
    }

    private void UpdateMaxIcon()
    {
        // 四个图标都是矢量 `Path`（几何键见 App.axaml 的 Icon* 资源），两组各显其一。
        // 最大化态 → 显示「还原」双框；全屏态 → 显示「退出全屏」图标。
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.IsVisible = !maximized;
        RestoreIcon.IsVisible = maximized;

        // 全屏按钮图标随状态切换；改型：进入 = 两箭头朝外 / 退出 = 两箭头朝内。
        FullScreenIcon.IsVisible = WindowState != WindowState.FullScreen;
        ExitFullScreenIcon.IsVisible = WindowState == WindowState.FullScreen;
    }
}
