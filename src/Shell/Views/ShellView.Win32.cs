using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;   // Win32Properties 在 Avalonia.Controls.dll 的 Avalonia.Controls 命名空间（不是 Avalonia.Win32）
using Avalonia.Threading;

namespace NewToys.Shell.Views;

/// <summary>
/// <see cref="ShellView"/> 的 **Windows 平台专属部分**（partial，2026-10-21 收敛）。
/// <para>
/// 收纳：外框几何的 Win32 读写（<c>SetWindowPos</c> / <c>GetWindowRect</c> / <c>GetWindowPlacement</c>）、
/// DWM 圆角压制 + 矩形窗口区域兜底、官方 WndProc 钩子（零内缩 + 方角同步 + 贴靠几何纠偏）。
/// 业务逻辑（标签栏 / 缩放热区 / 全屏 / 搜索 / 窗口记忆的存取口径）留在 <c>ShellView.axaml.cs</c>。
/// </para>
/// <para>
/// 🔴 平台纪律（<c>framework.md</c> §2.1）：**每个入口首行**都是
/// <c>if (!OperatingSystem.IsWindows()) …</c>（内联写法，CA1416 只认这种）⇒ 非 Windows 自然退化成系统默认外观。
/// ⛔ 别把这里的 DllImport 或 Win32 结构搬回主文件，也别在别处新开第二份。
/// </para>
/// </summary>
public partial class ShellView
{
    /// <summary>
    /// 直接按**外框物理像素**摆放窗口。
    /// ⛔ 不走 <c>Width</c>/<c>Height</c>：Avalonia 的 <c>Resize()</c> 会再给外框加一圈边框
    /// （它以为边框在客户区外面）—— 用它做"存取闭环"会让窗口一次比一次大。
    /// </summary>
    private void ApplyWindowFrame(PixelRect frame)
    {
        // Win32 handle 在 X11 / Wayland 上**也能取到非零值** ⇒ 必须显式平台守卫，
        //   否则 SetWindowPos 会走到 user32.dll 直接 DllNotFoundException。
        if (!OperatingSystem.IsWindows()) return;   // 内联 IsWindows()：CA1416 只认这种写法
        var h = TryGetPlatformHandle()?.Handle;
        if (h is not { } hwnd || hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, frame.X, frame.Y, frame.Width, frame.Height,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// 读窗口**外框**几何（物理像素），供窗口记忆落盘（<c>SaveWindowBounds</c>）。
    /// <para>⚠️ 最大化 / 全屏取 <c>WINDOWPLACEMENT.rcNormalPosition</c>（还原后的矩形），否则存下来的是铺满整屏的外框。</para>
    /// <para>⚠️ 一律读**外框**（⛔ 不读 ClientSize）：与 <see cref="ApplyWindowFrame"/> 的写入口径同一坐标系。</para>
    /// </summary>
    private static bool TryReadFrameGeometry(IntPtr hwnd, bool preferRestoreRect,
                                             out int x, out int y, out int w, out int h)
    {
        x = 0; y = 0; w = 0; h = 0;
        try
        {
            if (hwnd == IntPtr.Zero) return false;
            if (!OperatingSystem.IsWindows()) return false;   // 内联 IsWindows()：CA1416 只认这种写法

            if (preferRestoreRect)
            {
                var pl = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!GetWindowPlacement(hwnd, ref pl)) return false;
                x = pl.RcNormalPosition.Left;
                y = pl.RcNormalPosition.Top;
                w = pl.RcNormalPosition.Right - pl.RcNormalPosition.Left;
                h = pl.RcNormalPosition.Bottom - pl.RcNormalPosition.Top;
            }
            else
            {
                if (!GetWindowRect(hwnd, out var r)) return false;
                x = r.Left;
                y = r.Top;
                w = r.Right - r.Left;
                h = r.Bottom - r.Top;
            }
            return true;
        }
        catch { return false; }   // 读不到就沿用上次记录（不覆盖）
    }

    // ==================== DWM 圆角（需求 §4.1） ====================

    /// <summary>
    /// 走 Avalonia 官方圆角开关（<c>Win32Properties.WindowCornerPreference</c>）。
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
            // DWM / gdi32 / user32 全是 Windows-only ⇒ 非 Windows 直接降级为系统默认外观
            if (!OperatingSystem.IsWindows()) return;   // 内联 IsWindows()：CA1416 只认这种写法
            var h = TryGetPlatformHandle()?.Handle;
            if (h is not { } hwnd || hwnd == IntPtr.Zero) return;

            SetCornerPreferenceNoRound(hwnd);

            // 设完必须**让 DWM 重算非客户区**，否则典型症状就是"属性设进去了、画面没变"。
            // 只重算边框（不动位置 / 尺寸 / 层级 / 焦点），对窗口状态无副作用。
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            ApplySquareRegion(hwnd);   // 兜底：矩形窗口区域 → 强制方角
        }
        catch { /* 非 Win11 / 调用失败 → 维持系统默认（仅观感，不影响功能） */ }
    }

    /// <summary>
    /// 是否处于**全屏态**。
    /// <para>
    /// 🔴 为什么需要这个标记：`WINDOWPLACEMENT.showCmd` 在全屏时**未必**是 `SW_SHOWMAXIMIZED`
    /// （12.1.3 的全屏是「无边框 + 铺满屏」），但同样**必须清掉矩形窗口区域** ——
    /// 否则 `SetWindowRgn` 会沿用进全屏前的旧尺寸 → **内容被裁掉一大块**。
    /// </para>
    /// </summary>
    private static bool _isFullScreen;

    /// <summary>
    /// 🔴 **保留开关，且恒为 <c>true</c>**（结论，2026-10-15 实测）：
    /// <para>
    /// 基准测试（置 <c>false</c> = 不套矩形区域）实测结果：
    /// ① **DWM 阴影没有回来** → 阴影消失**不是** `SetWindowRgn` 的代价，而是客户区扩展（CSD 官方路线）的固有结果；
    /// ② 圆角在普通窗 / 分屏下复发。
    /// ⇒ **代价证伪、收益仍在** → 保留硬手段（开关回 <c>true</c>），技术债见《工程规范》§7 B16。
    /// </para>
    /// <para>
    /// ⚠️ 想再复核时把它改 <c>false</c> 即可（等价于把 <see cref="ApplySquareRegion"/> 两处调用都注掉）。
    /// </summary>
    // ⚠️ 刻意**不用 const**：置 false 时若用 const，编译器会判后面的代码不可达 →
    //    CS0162 警告（本项目开 TreatWarningsAsErrors → 直接编译失败），基准测试就跑不起来了。
    private static bool UseSquareRegionHack = true;    // 结论：保留（阴影与 region 无关）

    /// <summary>
    /// 兜底（硬手段）：给窗口套一个**矩形窗口区域**（<c>SetWindowRgn</c>）。
    /// <para>
    /// 🔴 为什么需要它：corner preference 在本窗口上试了 5 轮都没稳定生效（普通窗 / 分屏仍圆角；
    /// 而「最大化没圆角」是 Win11 的固有行为，⛔ 不能算我们的功劳）。
    /// MS 文档《应用圆角》明确：**使用了「窗口区域」的应用，系统不会对其应用圆角** →
    /// 这是绕过 DWM 偏好的硬手段，不依赖 DWM 是否尊重我们。
    /// </para>
    /// <para>
    /// ⚠️ 最大化 / 全屏**必须清掉 region**（传 NULL）—— 否则矩形区域会跟最大化几何打架（露边 / 裁内容）。
    /// ✅ **原「代价 = DWM 阴影消失」已被 实测证伪**：置 <c>false</c> 完全不套 region 时阴影同样没有
    ///    → 阴影消失是客户区扩展（CSD）的固有结果，与本 region 无关。边界层次由 自绘 1px 外框补。
    /// </para>
    /// </summary>
    private static void ApplySquareRegion(IntPtr hwnd)
    {
        try
        {
            if (!UseSquareRegionHack) return;    // 复核开关；置 false = 完全不套 region（仅复核用）
            // gdi32 / user32 调用前显式守卫（调用方虽已门控，这里兜住后续新入口）
            if (!OperatingSystem.IsWindows()) return;   // 内联 IsWindows()：CA1416 只认这种写法
            if (hwnd == IntPtr.Zero) return;

            // 全屏与最大化一视同仁 → 清 region
            if (IsWindowMaximized(hwnd) || _isFullScreen)
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
    /// （窗口状态变化 / 全屏 / 贴靠都会触发）→ 必须在**它处理完之后**再压一次（见 <see cref="PostChromeReassert"/>）。
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

    // 🔴 排查期曾在此处挂过一段诊断（读回 DWM 圆角值 + 写 corner-probe.txt）——
    //    2026-10-14 验证通过后已删除。两条结论留档，⛔ 以后再排查别再走这两条死路：
    //    ① 输出到 `Window.Title` 无效：本窗口无边框自绘，标题栏是自己画的 Grid，根本不显示 Title；
    //    ② `DwmGetWindowAttribute` **不支持读** `DWMWA_WINDOW_CORNER_PREFERENCE`(33)（该属性只用于 Set）
    //       → 永远读到失败值，不能当"下发是否成功"的判据。

    /// <summary>
    /// 接管窗口过程（🔴 **走 Avalonia 官方钩子**，2026-10-15 由手搓子类化迁移过来）：
    /// ① `WM_NCCALCSIZE` **零内缩**（客户区 = 整个外框，消灭那圈 ≈8px 的非客户区边框）
    /// ② 窗口尺寸 / 位置 / 样式变化后，重做「方角」相关的两件事（DWM 圆角偏好 + 矩形窗口区域）。
    /// <para>
    /// 🔴 为什么必须留 ①（2026-10-15 实测规律）：贴靠**第 1 次缝小(~1px)、第 2 次变大(~8px)** —— 8px 正是
    /// Avalonia CSD 按 `WS_CAPTION/WS_THICKFRAME` 扣出来的边框厚度；第 2 次它按「外框 = 客户区 + 边框」的记账
    /// 回写了几何、赢过系统给的贴靠矩形 ⇒ 只有让**客户区与外框同尺寸**，两边的记账才会一致。
    /// </para>
    /// <para>
    /// 🔴 为什么改走官方钩子（12.1.3 的 <c>Win32Properties.AddWndProcHookCallback</c>，取代自写子类化）：
    /// 官方钩子由 <c>WindowImpl.WndProcMessageHandler</c> 在 **Avalonia 处理消息之前**调用，用 <c>ref bool handled</c>
    /// 决定是否吞掉；委托由框架自己的字段持有 ⇒ ① 不必再「挂静态字段防 GC 回收」② 不必 <c>CallWindowProc</c> 转发
    /// ③ 只在 <c>PlatformImpl is IWin32OptionsTopLevelImpl</c> 时才挂 = **天然平台门控**（X11 / Wayland 自动不走）。
    /// </para>
    /// <para>
    /// 🔴 历史留档（⛔ 别再加回来）：
    /// ① 的「贴靠几何外部纠偏」（按屏幕自检强行 SetWindowPos）—— 判据不成立，已删；
    ///    ⚠️ 2026-10-21 的 <see cref="FixTiledGeometry"/> 形似但**判据明确**（只认 tile 矩形），不是它的复活。
    /// ② 手搓的 <c>SetWindowLongPtr(GWLP_WNDPROC)</c> 子类化 + <c>Marshal.GetFunctionPointerForDelegate</c>
    ///    + <c>CallWindowProc</c> + <c>_originalWndProc / _snapFixProc</c> 静态字段 —— 已被官方钩子整体取代后删除。
    /// ③ A 方案（删吞消息、交回官方零内缩）—— 2026-10-21 试过，首次分屏**仍略宽**（原因见 <see cref="FixTiledGeometry"/>）；
    /// ④ 「吞消息前先 <c>DwmExtendFrameIntoClientArea(0)</c>」—— 2026-10-21 试过，无效（贴靠矩形早于 DWM 记账）。
    /// </para>
    /// <para>⚠️ 装不上就什么都不做 —— 这类代码出错的代价是"窗口完全不响应消息"，失败路径必须保守。</para>
    /// </summary>
    private void InstallSnapFrameFix()
    {
        try
        {
            if (_wndProcHookInstalled) return;               // 只装一次
            if (!OperatingSystem.IsWindows()) return;        // 内联 IsWindows()：CA1416 只认这种写法

            Win32Properties.AddWndProcHookCallback(this, OnWndProcHook);
            _wndProcHookInstalled = true;
        }
        catch { /* 挂不上就维持现状 */ }
    }

    /// <summary>官方钩子是否已挂（幂等标记）。</summary>
    private static bool _wndProcHookInstalled;

    // ===== 贴靠 / 分屏几何纠偏（2026-10-21 定案）=====

    /// <summary>
    /// 贴靠矩形与本窗口真实几何的容差（**96 DPI 基准**，px）。实测「系统多算的量」= 2×8（=16）或再叠 7 ⇒ 32 足够。
    /// <para>🔴 必须按 DPI 放大（见 <see cref="SnapToleranceFor"/>）：8px/边 是 **96 DPI** 下的非客户区厚度，
    /// 200% 缩放下它变成 16px/边 ⇒ 固定 32 会把真实贴靠请求判成「不是 tile」而**完全不纠偏**。</para>
    /// </summary>
    private const int SnapToleranceAt96Dpi = 32;

    /// <summary>DPI 缩放后的容差上限（px）：防止异常 DPI 值把判据放宽到误判普通窗口。</summary>
    private const int SnapToleranceMax = 128;

    /// <summary>
    /// 按窗口当前 DPI 换算贴靠容差（<c>GetDpiForWindow</c>）。
    /// <para>⚠️ 取到的是**窗口所在屏**的 DPI：跨屏贴靠时窗口可能还在旧屏 ⇒ 与目标屏 DPI 不同；
    /// 容差只用于「近似相等」判定，这点差异不影响正确性（⛔ 不必为此引入 <c>GetDpiForMonitor</c>）。</para>
    /// </summary>
    private static int SnapToleranceFor(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;      // 取不到（老系统 / 无效句柄）→ 按 100% 处理
            var tol = (int)Math.Round(SnapToleranceAt96Dpi * dpi / 96.0);
            return Math.Clamp(tol, SnapToleranceAt96Dpi, SnapToleranceMax);
        }
        catch { return SnapToleranceAt96Dpi; }   // 缺导出（Win10 1607 之前）→ 退回 100% 基准
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>SetWindowPos 的 <c>WINDOWPOS</c>：<c>WM_WINDOWPOSCHANGING</c> / <c>WM_WINDOWPOSCHANGED</c> 的 lParam。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
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
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    private static bool Near(int a, int b, int tol) => Math.Abs(a - b) <= tol;

    // ── 分屏退回记账 ─────────────────────────────────────────────────────
    // 🔴 系统「取消贴靠」时按「外框 = 客户区 + ~8px/边」重算还原矩形，而本窗口零内缩（客户区 = 外框）
    //    ⇒ 退回后宽高各多 ~16px、位置各偏 ~8px。故进入贴靠前冻结自由外框，第一个矩形请求就改回它。
    // ⚠️ 静态字段 = 单主窗口前提（本应用只有一个 ShellView 实例）；⛔ 多窗口场景别直接复用。
    private static PixelRect? _freeFrame;    // 最近一次「自由」外框（非贴靠 / 非最大化 / 非全屏）
    private static bool _inTile;             // 当前是否处于系统贴靠态
    private static PixelRect? _preTileFrame; // 进入贴靠前的自由外框（取消贴靠时用它还原）

    /// <summary>
    /// 🔴 把系统给的**贴靠矩形**还原成真正的半屏 / 四分屏（本项目独有的问题，2026-10-21 实测确认根因）。
    /// <para>
    /// 根因：系统算贴靠矩形时按「外框 = 客户区 + 边框（本机 8px/边）」换算 —— 对普通窗口成立
    /// （实测记事本 / Edge：窗口矩形 974 宽、可见区 960 = 正好半屏）；但本窗口用零内缩
    /// （<c>WM_NCCALCSIZE</c> 被吞，客户区 = 外框）⇒ 系统仍按「客户区 = 贴靠目标」多给 16px 宽度，
    /// 而我们把 16px 全画成了内容 ⇒ **可见区比半屏宽 16px**（左贴靠右溢 16、右贴靠左溢 16）。
    /// 实测：左贴靠请求 (0,0,976,1040)，正确值应为 (0,0,960,1040)。
    /// </para>
    /// <para>
    /// 为什么不是「去掉零内缩」：去掉就等于恢复那圈 ≈8px 非客户区，贴靠态系统不画 → 露缝（v3.29 的原始问题）。
    /// 为什么不是「事后按屏幕自检纠偏」（已删）：那是**无判据的强行 SetWindowPos**；
    /// 这里只认「矩形正好是某个 tile ±32px」这一种明确的系统贴靠请求，其余一切不动。
    /// </para>
    /// <para>
    /// 两个时机：<c>POSCHANGING</c> 直接改写请求（无闪烁，覆盖绝大多数情况）；
    /// <c>POSCHANGED</c> 兜住「改写后又被补一次边框」的路径（区域 hack 开着时实测会再多 7px）。
    /// </para>
    /// </summary>
    private static void FixTiledGeometry(uint msg, IntPtr hWnd, IntPtr lParam)
    {
        try
        {
            if (IsWindowMaximized(hWnd)) { _inTile = false; return; }   // 最大化 ⇒ 已离开贴靠态

            // 🔴 容差按 DPI 缩放（200% 屏上 8px/边 → 16px/边，固定 32 会漏纠）
            int tol = SnapToleranceFor(hWnd);

            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            int x = wp.X, y = wp.Y, cx = wp.Cx, cy = wp.Cy;
            if (cx <= 0 || cy <= 0) return;

            // 🔴 用**目标矩形**找显示器（不是当前窗口位置）：跨屏贴靠时窗口还在旧屏上，
            //    拿旧屏的 rcWork 比对必然不匹配 ⇒ 只会漏纠，不会误纠；这里按目标矩形取才准。
            var probe = new RECT { Left = x, Top = y, Right = x + cx, Bottom = y + cy };
            var mon = MonitorFromRect(ref probe, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return;
            var mi = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(mon, ref mi)) return;

            var work = mi.RcWork;
            int ww = work.Right - work.Left;
            int wh = work.Bottom - work.Top;
            if (ww <= 0 || wh <= 0) return;

            // 全屏（铺满整个显示器）不算「自由几何」：全屏 → 贴靠 → 取消贴靠时若拿它还原，
            // 窗口会退回全屏尺寸。⛔ 别把它当普通位置记进 `_freeFrame`。
            bool fillsMonitor = Near(x, mi.RcMonitor.Left, tol) && Near(y, mi.RcMonitor.Top, tol)
                                && Near(cx, mi.RcMonitor.Right - mi.RcMonitor.Left, tol)
                                && Near(cy, mi.RcMonitor.Bottom - mi.RcMonitor.Top, tol);

            int hw = ww / 2, hh = wh / 2;
            int tx = int.MinValue, ty = int.MinValue, tw = 0, th = 0;

            // 半屏（左右）
            if (Near(cy, wh, tol) && Near(y, work.Top, tol))
            {
                if (Near(x, work.Left, tol) && Near(x + cx, work.Left + hw, tol))
                { tx = work.Left; ty = work.Top; tw = hw; th = wh; }
                else if (Near(x + cx, work.Right, tol) && Near(x, work.Right - hw, tol))
                { tx = work.Right - hw; ty = work.Top; tw = hw; th = wh; }
            }
            // 四分屏（四角）
            else if (Near(cy, hh, tol))
            {
                bool top = Near(y, work.Top, tol) && Near(y + cy, work.Top + hh, tol);
                bool bot = Near(y + cy, work.Bottom, tol) && Near(y, work.Bottom - hh, tol);
                if (top || bot)
                {
                    ty = top ? work.Top : work.Bottom - hh;
                    th = hh;
                    if (Near(x, work.Left, tol) && Near(x + cx, work.Left + hw, tol))
                    { tx = work.Left; tw = hw; }
                    else if (Near(x + cx, work.Right, tol) && Near(x, work.Right - hw, tol))
                    { tx = work.Right - hw; tw = hw; }
                }
            }

            if (tw == 0)                                            // 不是 tile
            {
                bool leavingTile = _inTile;
                _inTile = false;

                // 🔴 正在**取消贴靠**：把几何改回贴靠前的自由外框（系统重算会多给 ~16px，见上方字段处注释）。
                // ⚠️ 必须排除「铺满显示器」的请求：贴靠态下双击标题栏 / Win+↑ 最大化走的是同一条路径，
                //    若当成退出贴靠拽回原尺寸，最大化就再也进不去（2026-10-21 写这段时避开的坑）。
                if (leavingTile && !fillsMonitor
                    && _preTileFrame is { } pre
                    && (pre.X != x || pre.Y != y || pre.Width != cx || pre.Height != cy))
                {
                    if (msg == WM_WINDOWPOSCHANGING)
                    {
                        wp.X = pre.X; wp.Y = pre.Y; wp.Cx = pre.Width; wp.Cy = pre.Height;
                        Marshal.StructureToPtr(wp, lParam, false);
                    }
                    else
                    {
                        SetWindowPos(hWnd, IntPtr.Zero, pre.X, pre.Y, pre.Width, pre.Height,
                                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
                    }
                    return;
                }

                // 自由态：持续记账（下一次贴靠时冻结成 `_preTileFrame`）。
                // ⚠️ 全屏 / 最大化 / 刚退出贴靠的那一帧都不记账（它们的几何不是「用户想要的窗口大小」）。
                if (!fillsMonitor && !leavingTile) _freeFrame = new PixelRect(x, y, cx, cy);
                return;
            }

            // 首次进入贴靠 → 冻结「贴靠前」的自由外框供退回时使用
            if (!_inTile)
            {
                _preTileFrame = _freeFrame;
                _inTile = true;
            }
            if (x == tx && y == ty && cx == tw && cy == th) return;          // 已经精确 → 不碰

            if (msg == WM_WINDOWPOSCHANGING)
            {
                wp.X = tx; wp.Y = ty; wp.Cx = tw; wp.Cy = th;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            else
            {
                SetWindowPos(hWnd, IntPtr.Zero, tx, ty, tw, th,
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
            }
        }
        catch { /* 纠偏失败 → 维持系统给的原样（宁可略宽，不可崩） */ }
    }

    /// <summary>
    /// 官方 WndProc 钩子：**在 Avalonia 处理消息之前**被调用（<c>WindowImpl.WndProcMessageHandler</c>）。
    /// <para>🔴 零内缩的实现随之简化：手搓版要「抄下系统外框 → 转发给 Avalonia → 再把 rgrc[0] 写回原值」，
    /// 现在只需 <c>handled = true; return 0</c> 且**不改动 lParam** —— 系统拿到的仍是它自己送进来的
    /// <c>rgrc[0]</c>（= 新窗口外框）⇒ 客户区 = 外框，结果完全等价。
    /// wParam == 1 → lParam 指向 NCCALCSIZE_PARAMS；rgrc[0] 就在 lParam 的开头 16 字节。</para>
    /// <para>🔴 2026-10-21 定案：**「贴靠偏宽」不是 DWM 记账延迟，而是系统给贴靠矩形做「外框 = 客户区 + 边框」
    /// 换算**（本机 8px/边）×**本窗口客户区 = 外框**（零内缩）⇒ 多算的 16px 全变成可见内容。
    /// 修法 = <see cref="FixTiledGeometry"/>（只认 tile 矩形，其余一律不碰）。两条死路已实测排除：
    /// ① 吞消息前 <c>DwmExtendFrameIntoClientArea(0)</c> 清零扩展帧 —— 无效（矩形在 SetWindowPos 之前就算完）；
    /// ② 删掉吞消息、交回官方零内缩 —— 首次分屏仍偏宽（同因，且露缝复发）。</para>
    /// <para>⚠️ 最大化**不拦截**：最大化时外框本来就比屏幕大一圈（边框被甩到屏幕外），
    /// 此时若零内缩，内容会伸出屏幕 8px、标题栏顶边被切掉 ⇒ 交回 Avalonia 正常处理。</para>
    /// </summary>
    private static IntPtr OnWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 🔴 贴靠几何纠偏（2026-10-21 实测定案，见 FixTiledGeometry）：系统按「外框 = 客户区 + ~8px/边」
        //    设计贴靠矩形，而本窗口客户区 = 外框（零内缩）⇒ 实际可见区比目标宽 16px。
        if (msg == WM_WINDOWPOSCHANGING || msg == WM_WINDOWPOSCHANGED)
            FixTiledGeometry(msg, hWnd, lParam);

        _ = lParam;   // ⛔ 刻意**不读写**：零内缩正是靠「系统送进来的 rgrc[0] 未被改写」
        if (msg == WM_NCCALCSIZE && wParam.ToInt64() == 1 && !IsWindowMaximized(hWnd))
        {
            handled = true;          // 吞掉消息 → 系统拿「未被改写的 rgrc[0]」当客户区
            return IntPtr.Zero;      // 返回 0 = 已处理
        }

        // 方角同步（DWM 圆角偏好 + 矩形窗口区域）。
        // ⚠️ 本钩子在 Avalonia **处理之前**调用，而压制必须在框架**处理完之后**
        //    （框架会重算并回写偏好）→ 用 Post 保序，见 PostChromeReassert。
        if (msg == WM_WINDOWPOSCHANGED || msg == WM_SIZE || msg == WM_STYLECHANGED || msg == WM_SHOWWINDOW)
            PostChromeReassert(hWnd);

        return IntPtr.Zero;          // handled 保持 false → Avalonia 继续正常处理
    }

    /// <summary>方角重置的**待执行标志**（<c>SetWindowRgn</c> 会再触发同类消息 → 无它就会自激成环）。</summary>
    private static bool _chromeReassertPending;

    /// <summary>
    /// 把方角重置排到「Avalonia 处理完本次消息之后」执行。
    /// <para>🔴 为什么要 Post：官方钩子在框架**之前**调用，而 <see cref="ReassertWindowChrome"/> 必须在框架**之后**
    /// 运行 —— <c>Send</c> 优先级 = 本次 WndProc 返回后立即执行，顺序最接近原手搓版的「先转发、后压」。</para>
    /// <para>⚠️ 标志**执行完才复位**：重置过程（<c>SetWindowRgn</c>）会同步再触发同类消息，提前复位会自激成环。</para>
    /// </summary>
    private static void PostChromeReassert(IntPtr hwnd)
    {
        if (_chromeReassertPending) return;
        try
        {
            _chromeReassertPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                try { ReassertWindowChrome(hwnd); }
                finally { _chromeReassertPending = false; }
            }, DispatcherPriority.Send);
        }
        catch { _chromeReassertPending = false; }    // 派发失败 → 复位，不影响后续消息
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

    // 🔴 样式位常量（`GWL_STYLE` / `WS_*`）与 `GetWindowLongPtr` 曾用于 v3.28 的「手工补贴靠资格样式」
    //    （`ApplySnapEligibleStyles`）。官方 `WindowDecorations="Full"` 自带这些样式位 ⇒ 补丁与常量随之清理。
    //    ⛔ 若日后贴靠再失效，**先怀疑装饰是不是被改成 `BorderOnly` / `None`**，⛔ 别把手工补位抄回来。
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;   // v3.29：贴靠校正时⛔别抢焦点
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOSENDCHANGING = 0x0400;

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

    // ===== v3.29 贴靠缝隙根治：官方 WndProc 钩子做零内缩 / 圆角压制（原理见 InstallSnapFrameFix 的注释） =====

    private const uint SWP_NOOWNERZORDER = 0x0200;

    private const uint WM_WINDOWPOSCHANGING = 0x0046;
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

    // 2026-10-15：`CallWindowProc` / `DefWindowProc` / `WndProcFunc` 委托 /
    //   `_snapFixProc` · `_originalWndProc` 字段 / `SetWindowLongPtr(GWLP_WNDPROC)` **已全部删除** ——
    //   窗口过程改由官方钩子接管（`Win32Properties.AddWndProcHookCallback`，见 `InstallSnapFrameFix`）。
    //   ⛔ 别再加回来：自写子类化需要「委托挂静态字段防 GC + 手动转发 + 装失败保守」，官方钩子这三项全免。
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                            int x, int y, int cx, int cy, uint uFlags);
}
