using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;   // Win32Properties 在 Avalonia.Controls.dll 的 Avalonia.Controls 命名空间（不是 Avalonia.Win32）
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NewToys.Shell.Views;

/// <summary>ShellView 的 Windows 平台专属部分。（WHY: SHL-061）</summary>
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

    /// <summary>走 Avalonia 官方圆角开关（Win32Properties.WindowCornerPreference）。（WHY: SHL-062）</summary>
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

    /// <summary>是否处于全屏态。（WHY: SHL-063）</summary>
    private static bool _isFullScreen;

    /// <summary>🔴 保留开关，且恒为 true： 基准测试（置 false = 不套矩形区域）实测结果： ① DWM 阴影没有回来 → 阴影消失不是 SetWindowRgn 的代价，而是客户区扩展（CSD 官方路线）的固有结果；（WHY: SHL-064）</summary>
    // ⚠️ 刻意**不用 const**：置 false 时若用 const，编译器会判后面的代码不可达 →
    //    CS0162 警告（本项目开 TreatWarningsAsErrors → 直接编译失败），基准测试就跑不起来了。
    private static bool UseSquareRegionHack = true;    // 结论：保留（阴影与 region 无关）

    /// <summary>兜底（硬手段）：给窗口套一个矩形窗口区域（SetWindowRgn）。（WHY: SHL-065）</summary>
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

    /// <summary>只写 DWM 圆角属性（不发任何消息）。（WHY: SHL-066）</summary>
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

    /// <summary>① WM_NCCALCSIZE 零内缩（客户区 = 整个外框，消灭那圈 ≈8px 的非客户区边框） ② 窗口尺寸 / 位置 / 样式变化后，重做「方角」相关的两件事（DWM 圆角偏好 + 矩形窗口区域）。（WHY: SHL-067）</summary>
    private void InstallSnapFrameFix()
    {
        try
        {
            if (_wndProcHookInstalled) return;               // 只装一次
            if (!OperatingSystem.IsWindows()) return;        // 内联 IsWindows()：CA1416 只认这种写法

            Win32Properties.AddWndProcHookCallback(this, OnWndProcHook);
            _wndProcHookInstalled = true;
            _hookedView = this;                              // 边缘命中测试要用 Visual 树（见 HitTestEdgeControl）
        }
        catch { /* 挂不上就维持现状 */ }
    }

    /// <summary>官方钩子是否已挂（幂等标记）。</summary>
    private static bool _wndProcHookInstalled;

    /// <summary>挂了钩子的窗口实例：<c>WM_NCHITTEST</c> 里做视觉命中要用（静态钩子拿不到 this）。</summary>
    private static ShellView? _hookedView;

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

    /// <summary>🔴 把系统给的贴靠矩形还原成真正的半屏 / 四分屏。（WHY: SHL-068）</summary>
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

    /// <summary>官方 WndProc 钩子：在 Avalonia 处理消息之前被调用（WindowImpl.WndProcMessageHandler）。（WHY: SHL-069）</summary>
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

        // 🔴 控件优先于系统 resize 边框：客户区已零内缩，最右/最下 ~8px 既是控件区域又是
        //    WS_THICKFRAME 的拉伸热区（系统命中永远赢），不覆盖的话贴边滚动条整条抓不住。
        if (msg == WM_NCHITTEST && HitTestEdgeControl(hWnd, lParam, ref handled))
            return new IntPtr(HTCLIENT);

        return IntPtr.Zero;          // handled 保持 false → Avalonia 继续正常处理
    }

    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;

    /// <summary>边缘命中带宽度（DIP）：必须 ≥ 系统拉伸边框（96 DPI 下 8px），否则滚动条尾巴仍被抢。</summary>
    private const double EdgeResizeBandDip = 16;

    /// <summary>
    /// 边缘拉伸热区上的「可交互控件」优先：命中滚动条就返回 <c>HTCLIENT</c>（自己吃掉消息），
    /// 其余情况一律不干预 → 贴靠 / 圆角 / 拖边框缩放全部维持原样。
    /// <para>⚠️ 只在非最大化时接管：最大化没有可拉边框（见 <see cref="OnWndProcHook"/> 的注释）。</para>
    /// </summary>
    private static bool HitTestEdgeControl(IntPtr hwnd, IntPtr lParam, ref bool handled)
    {
        try
        {
            var view = _hookedView;
            if (view is null || IsWindowMaximized(hwnd)) return false;

            // lParam = 屏幕坐标（低 16 位 X / 高 16 位 Y，均为有符号 short）
            var raw = lParam.ToInt64();
            var point = view.PointToClient(new PixelPoint((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF)));

            // 廉价闸门：绝大多数移动不贴边，先挡掉再谈命中测试
            var size = view.ClientSize;
            if (point.X < size.Width - EdgeResizeBandDip && point.Y < size.Height - EdgeResizeBandDip) return false;

            // 🔴 用 GetVisualsAt（全部命中）而非 GetVisualAt（仅最浅一个）：滚动条是 ScrollViewer 的
            //    兄弟节点，不在内容命中的祖先链上，最浅命中往往只是内容容器 → 单点判定会漏掉滚动条。
            foreach (var hit in view.GetVisualsAt(point))
            {
                if (hit is ScrollBar || hit.FindAncestorOfType<ScrollBar>() is not null)
                {
                    handled = true;
                    return true;
                }
            }
            return false;
        }
        catch { return false; }   // 钩子里抛异常会卡死消息循环 ⇒ 失败一律不干预
    }

    /// <summary>方角重置的**待执行标志**（<c>SetWindowRgn</c> 会再触发同类消息 → 无它就会自激成环）。</summary>
    private static bool _chromeReassertPending;

    /// <summary>把方角重置排到「Avalonia 处理完本次消息之后」执行。（WHY: SHL-070）</summary>
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
