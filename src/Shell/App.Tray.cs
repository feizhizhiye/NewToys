using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;
using NewToys.UI.Controls;

namespace NewToys.Shell;

public partial class App : Application
{
    // ==================== 托盘图标（「关闭按钮最小化到托盘」的落点） ====================

    private static TrayIcon? _tray;

    /// <summary>主窗口（托盘菜单「先拉起窗口、再导航」要用它；主题切换后重画窗口图标也要用它）。</summary>
    private static Window? _mainWindow;

    /// <summary>托盘 / 窗口导航用的固定页 key（首页与设置总表，见需求 §5）。</summary>
    private const string TrayHomeKey = "home";
    private const string TraySettingsKey = "settings-path";

    /// <summary>建托盘图标并挂上菜单（左键切窗口；右键五项：显示 / 隐藏、首页、设置、用另一个配置打开、退出）。（WHY: SHL-007）</summary>
    /// <returns>托盘是否创建成功（失败时「最小化到托盘 / 关闭到托盘」会退化成普通最小化，并如实提示）。</returns>
    private bool SetupTrayIcon(Window win)
    {
        try
        {
            // ⚠️ 用对象初始化器而不是带参构造：Header 是确定存在的可写属性，ctor 重载各版本不完全一致
            var show = new NativeMenuItem { Header = "显示 / 隐藏窗口" };
            show.Click += (_, _) => ToggleWindow(win);

            var home = new NativeMenuItem { Header = "打开首页" };
            home.Click += (_, _) => ShowAndNavigate(win, TrayHomeKey);

            var settings = new NativeMenuItem { Header = "打开设置…" };
            settings.Click += (_, _) => ShowAndNavigate(win, TraySettingsKey);

            var profiles = BuildProfileMenuItem();
            var exit = new NativeMenuItem { Header = "退出 NewToys" };

            exit.Click += (_, _) =>
            {
                // ⚠️ 必须走 ForceClose：否则 ShellView.OnClosing 会按「关闭到托盘」把它拦下来，永远退不掉
                if (win is ShellView shell) shell.ForceClose = true;
                win.Close();
            };

            var icon = new TrayIcon
            {
                Icon = CreateAppIcon(CurrentAccent()),
                ToolTipText = "NewToys",
                IsVisible = true,
                // 顺序即优先级（需求 §4.6.1）：① 显示/隐藏 ② 首页 ③ 设置 ④ 另一个配置 ⑤ 退出
                Menu = new NativeMenu { Items = { show, home, settings, profiles, exit } }
            };

            // 左键 = 切换显示 / 隐藏（与菜单①同义；⛔ 与右键菜单互不抢：Clicked 只管左键）
            icon.Clicked += (_, _) => ToggleWindow(win);

            var icons = TrayIcon.GetIcons(this);
            if (icons == null)
            {
                icons = new TrayIcons();
                TrayIcon.SetIcons(this, icons);
            }
            icons.Add(icon);
            _tray = icon;
            return true;
        }
        catch (Exception ex)
        {
            // 托盘建不起来（平台不支持 / 无托盘环境）不该让程序起不来
            System.Diagnostics.Debug.WriteLine("托盘图标创建失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 左键 / 菜单①：**切换**主窗口显示与隐藏（⛔ 不是"只显示"）。
    /// 隐藏（或最小化）态必须能一次拉回前台：还原为非最小化 → <c>Show</c> → <c>Activate</c>。
    /// </summary>
    private static void ToggleWindow(Window win)
    {
        if (win.IsVisible && win.WindowState != WindowState.Minimized) { win.Hide(); return; }
        ShowWindow(win);
    }

    /// <summary>把主窗口拉到前台（已可见就只激活，不动它的最大化状态）。</summary>
    private static void ShowWindow(Window win)
    {
        win.Show();
        if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
        win.Activate();
    }

    /// <summary>
    /// 菜单②③：**先拉起窗口、再导航**（⛔ 不许弹个没跳转的空窗口）。
    /// 导航排到 UI 队列里执行，确保窗口已显示完再切页。
    /// </summary>
    private void ShowAndNavigate(Window win, string pageKey)
    {
        ShowWindow(win);
        var nav = _sp?.GetService<INavigationService>();
        if (nav == null) return;
        // ⚠️ 托盘菜单的回调线程不保证是 UI 线程 ⇒ 一律 Post 回 UI 线程
        Dispatcher.UIThread.Post(() =>
        {
            try { nav.NavigateTo(pageKey); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("托盘导航失败：" + ex.Message); }
        });
    }

    /// <summary>
    /// 菜单④「用另一个配置打开…」子菜单 = 全部配置**排除当前配置**；
    /// 一个配置只能开一个实例（进程级 Mutex，见 <see cref="AcquireProfileMutex"/>）⇒ 当前配置⛔ 不进菜单。
    /// 没有其他配置时置灰并在标题里写明原因（需求 §4.6.1）。
    /// </summary>
    private NativeMenuItem BuildProfileMenuItem()
    {
        var item = new NativeMenuItem();
        IProfileService? profiles = null;
        try { profiles = _sp?.GetService<IProfileService>(); }
        catch { /* 服务拿不到就退化成"没有其他配置" */ }

        var current = profiles?.ActiveProfile;
        var others = profiles == null
            ? new List<string>()
            : profiles.Profiles.Where(p => !string.Equals(p, current, StringComparison.OrdinalIgnoreCase)).ToList();

        if (others.Count == 0)
        {
            item.Header = "用另一个配置打开…（没有其他配置）";
            item.IsEnabled = false;
            return item;
        }

        item.Header = "用另一个配置打开…";
        var sub = new NativeMenu();
        foreach (var name in others)
        {
            var one = new NativeMenuItem { Header = name };
            one.Click += (_, _) =>
            {
                try { profiles!.LaunchNewInstance(name); }
                catch (Exception ex)
                {
                    // 不来（互斥体挡住 / 路径非法）：托盘里⛔ 不弹窗打断用户，只留调试线索
                    System.Diagnostics.Debug.WriteLine("托盘启动配置实例失败：" + ex.Message);
                }
            };
            sub.Items.Add(one);
        }
        item.Menu = sub;
        return item;
    }

    /// <summary>唯一的应用图标工厂（/ 需求 §4.6.1）：窗口标题栏图标与托盘图标用同一份， 形状与 Resources/app.ico（exe 图标，由 &lt;ApplicationIcon&gt; 编进 exe）一致；（WHY: SHL-008）</summary>
    private static WindowIcon? CreateAppIcon(Color accent)
    {
        try
        {
            const int size = 32;
            var bmp = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
                                          PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var fb = bmp.Lock();

            // ⚠️ 位图是 BGRA（内存小端）：按 0xAA_BB_GG_RR 组装，alpha 255 时预乘等价于原色
            var back = (uint)((255u << 24) | ((uint)accent.B << 16) | ((uint)accent.G << 8) | accent.R);
            const uint white = 0xFFFFFFFFu;
            const uint transparent = 0x00000000u;

            void Put(int x, int y, uint argb)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) return;
                Marshal.WriteInt32(fb.Address, y * fb.RowBytes + x * 4, unchecked((int)argb));
            }

            // 圆角：四角 6px 以内且落在圆外的像素留透明
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = Math.Min(x, size - 1 - x);
                    var dy = Math.Min(y, size - 1 - y);
                    if (dx < 6 && dy < 6)
                    {
                        var cx = 6 - dx; var cy = 6 - dy;
                        if (cx * cx + cy * cy > 36) { Put(x, y, transparent); continue; }
                    }
                    Put(x, y, back);
                }

            // 白色 "N"：两根竖条 + 一条斜杠
            for (var y = 8; y <= 23; y++)
            {
                for (var w = 0; w < 3; w++)
                {
                    Put(9 + w, y, white);
                    Put(20 + w, y, white);
                }
            }
            for (var t = 0; t <= 14; t++)
            {
                var x = 9 + t;
                var y = 8 + t;
                for (var w = 0; w < 3; w++) Put(x + w, y, white);
            }

            return new WindowIcon(bmp);
        }
        catch
        {
            return null;   // 画不出来就用系统默认图标（托盘照样可用）
        }
    }

    /// <summary>当前强调色（取主题服务的 accent id；拿不到就回退预设默认色，⛔ 不在图标里写死颜色）。</summary>
    private static Color CurrentAccent()
    {
        try
        {
            var theme = _current?._sp?.GetService<IThemeService>();
            return Color.Parse(AccentPalette.Find(theme?.AccentId).Color);
        }
        catch
        {
            return Color.Parse(AccentPalette.Default.Color);
        }
    }

    /// <summary>主题 / 强调色变了就把窗口图标与托盘图标一起重画（同一份 <see cref="CreateAppIcon"/>）。</summary>
    private static void RefreshAppIcon()
    {
        try
        {
            var icon = CreateAppIcon(CurrentAccent());
            if (icon == null) return;
            if (_mainWindow != null) _mainWindow.Icon = icon;
            if (_tray != null) _tray.Icon = icon;
        }
        catch { /* 图标重画失败无所谓：不影响窗口本身 */ }
    }

    /// <summary>真正的退出（托盘「退出」走这里）：收掉托盘图标，免得进程退了图标还挂着。</summary>
    public static void ShutdownTray()
    {
        try
        {
            // ⚠️ TrayIcon.GetIcons(Application o) 的形参不可为空：_current 是 App?，先判一次（CS8604）
            if (_tray == null || _current == null) return;
            var icons = TrayIcon.GetIcons(_current);
            icons?.Remove(_tray);
            _tray.Dispose();
            _tray = null;
        }
        catch { /* 收不掉也无所谓：进程马上就退了 */ }
    }

}
