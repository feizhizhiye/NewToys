using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;

namespace NewToys.Shell;

public class App : Application
{
    private ServiceProvider? _sp;
    private static App? _current;
    private ResourceDictionary? _themeDictionary;

    public static T? GetService<T>() where T : class
        => _current?._sp?.GetService<T>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();

            // Core services
            services.AddSingleton<ConfigStoreService>();
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ILaunchConfigService, LaunchConfigService>();
            services.AddSingleton<IMenuService, MenuService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<LaunchRunner>();
            // ⚠️ 之前只注册了**具体类型**，没有注册接口 —— 于是「设置 → 加密 DNS」页要想拿
            // IEncryptedDnsService 就解析不到（DI 会退回到无参构造，页面变成"未就绪"骨架）。
            // 现在两个都注册，且指向**同一个单例**（接口 → 具体类型），别改成两次 new。
            services.AddSingleton<EncryptedDnsService>();
            services.AddSingleton<IEncryptedDnsService>(sp => sp.GetRequiredService<EncryptedDnsService>());

            // 「软件包管理」模块的两项：命令行后端 + 它自己的源设置（落 config\package.json，可被数据管理备份）。
            services.AddSingleton<IPackageSettingsService, PackageSettingsService>();
            services.AddSingleton<IPackageManagerService, PackageManagerService>();

            // 首页收藏夹：落 config\favorites.json（可被数据管理备份），单一真相源，标题/图标/简介现从菜单取。
            services.AddSingleton<IFavoritesService, FavoritesService>();

            // 列表页「列显隐」偏好（右键表头增删列，规范 3.8）：落 config\columnlayout.json（会被数据管理备份）。
            services.AddSingleton<IColumnLayoutService, ColumnLayoutService>();

            // 工程内唯一的本地数据库（data\newtoys.db）：当前给「视频工具 → 视频库」的媒体信息索引用；
            // 后续更多结构化数据也走它（见 SqliteDatabase 的类注释）。
            services.AddSingleton<SqliteDatabase>();

            // 「设置 → 通用」的系统级行为：开机启动项（注册表）+ 启动时检查更新。
            // 两者都不碰 Avalonia，所以能放在 Core 里；这里只是把它们挂进 DI。
            services.AddSingleton<StartupService>();
            services.AddSingleton<UpdateService>();

            services.AddSingleton<ShellViewModel>();
            services.AddTransient<HomeView>();
            services.AddTransient<HomeViewModel>();

            // 模块加载：Debug 走显式类型清单（避免程序集延迟加载扫不到），Release 走 Modules 目录扫描。
            // 新增模块时两处都要登记：Shell.csproj 的 Debug ProjectReference + 下面的 #if DEBUG 清单。
            Core.Services.ModuleLoader.LoadAll(services, GetDebugModuleTypes());

            _sp = services.BuildServiceProvider();
            _current = this;
            AppServices.Initialize(_sp);
            Core.Services.ModuleLoader.InitializeAll(_sp);

            var theme = _sp.GetRequiredService<IThemeService>();
            WatchSystemTheme(theme);          // 先注入系统明暗（「跟随系统」要用），再刷第一帧
            ApplyTheme(theme);
            theme.ThemeChanged += () => ApplyTheme(theme);

            var win = new ShellView { DataContext = _sp.GetRequiredService<ShellViewModel>() };

            // 卸载 / 删除前的二次确认：Core 里的服务不能引用 Avalonia，
            // 所以由宿主在这里把「怎么问」接上去（弹窗在下面实现）。
            PackageManagerService.ConfirmHandler = msg => ConfirmAsync(win, msg);

            // 公共对话框钩子（Core.DialogHooks）：给各模块用（视频工具的重命名 / 删除文件 / 清空缓存 …）。
            // ⛔ 模块不能反向引用 Shell，所以只能由这里注入；没注入时钩子一律按"用户取消"处理。
            DialogHooks.ConfirmHandler = msg => ConfirmAsync(win, msg);
            DialogHooks.InputHandler = (title, initial) => InputAsync(win, title, initial);

            // ==================== 「设置 → 通用」的三项系统级行为（v1.35 真正生效） ====================
            // ① 开机启动：以注册表为准做一次静默对账（用户在系统别处改了也不会两边打架）；
            // ② 启动时最小化到托盘：托盘就绪时不显示主窗口（否则会闪一下再消失）；
            // ③ 启动时检查更新：后台跑，只提示、绝不自己替换自己。
            var launchCfg = _sp.GetRequiredService<ILaunchConfigService>();
            var startup = _sp.GetRequiredService<StartupService>();
            if (startup.IsSupported)
            {
                var want = launchCfg.Config.StartWithSystem;
                if (want != startup.IsEnabled) startup.SetEnabled(want);
            }

            var trayOk = SetupTrayIcon(win);
            var startHidden = launchCfg.Config.StartMinimized && trayOk;

            if (startHidden)
            {
                // 不 Show：直接留在托盘（Show 完再 Hide 会看到窗口闪一下）
                win.WindowState = Avalonia.Controls.WindowState.Minimized;
            }
            else
            {
                win.Show();
            }

            if (launchCfg.Config.CheckUpdateOnStartup)
            {
                var updateUrl = _sp.GetRequiredService<IAppConfigService>().UpdateUrl;
                _ = CheckUpdateAsync(win, updateUrl);
            }
        }
        catch (Exception ex)
        {
            var w = new Window
            {
                Width = 720, Height = 460,
                Title = "启动失败",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = ex.ToString(),
                        Margin = new Thickness(20),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontFamily = "Consolas"
                    }
                }
            };
            w.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ==================== 托盘图标（「关闭按钮最小化到托盘」的落点） ====================

    private static TrayIcon? _tray;

    /// <summary>
    /// 建托盘图标并挂上菜单（显示 / 隐藏窗口、退出）。
    /// <para>
    /// ⚠️ 托盘是 Avalonia 的原生控件，必须落在 Shell 这一层（<c>Core</c> 不引用 Avalonia）。
    /// 图标是**代码画出来的**（32×32 位图，不依赖任何打包资源），所以发布产物里不会漏文件。
    /// </para>
    /// </summary>
    /// <returns>托盘是否创建成功（失败时「最小化到托盘 / 关闭到托盘」会退化成普通最小化，并如实提示）。</returns>
    private bool SetupTrayIcon(Window win)
    {
        try
        {
            // ⚠️ 用对象初始化器而不是带参构造：Header 是确定存在的可写属性，ctor 重载各版本不完全一致
            var show = new NativeMenuItem { Header = "显示 / 隐藏窗口" };
            var exit = new NativeMenuItem { Header = "退出 NewToys" };

            show.Click += (_, _) =>
            {
                if (win.IsVisible)
                {
                    if (win.WindowState == WindowState.Minimized) { win.WindowState = WindowState.Normal; win.Show(); }
                    else win.Hide();
                }
                else
                {
                    win.Show();
                    win.WindowState = WindowState.Normal;
                    win.Activate();
                }
            };

            exit.Click += (_, _) =>
            {
                // ⚠️ 必须走 ForceClose：否则 ShellView.OnClosing 会按「关闭到托盘」把它拦下来，永远退不掉
                if (win is ShellView shell) shell.ForceClose = true;
                win.Close();
            };

            var icon = new TrayIcon
            {
                Icon = CreateTrayIcon(),
                ToolTipText = "NewToys",
                IsVisible = true,
                Menu = new NativeMenu { Items = { show, exit } }
            };

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

    /// <summary>代码画一个 32×32 的托盘图标（强调色圆角底 + 白色 N），不依赖任何资源文件。</summary>
    private static WindowIcon? CreateTrayIcon()
    {
        try
        {
            const int size = 32;
            var bmp = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
                                          PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var fb = bmp.Lock();

            const uint back = 0xFF07C160u;   // 强调色（微信绿）
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

    /// <summary>真正的退出（托盘「退出」走这里）：收掉托盘图标，免得进程退了图标还挂着。</summary>
    public static void ShutdownTray()
    {
        try
        {
            if (_tray == null) return;
            var icons = TrayIcon.GetIcons(_current);
            icons?.Remove(_tray);
            _tray.Dispose();
            _tray = null;
        }
        catch { /* 收不掉也无所谓：进程马上就退了 */ }
    }

    // ==================== 启动时检查更新 ====================

    /// <summary>
    /// 后台检查一次更新。<b>只提示，不自动下载 / 替换自己</b>；失败与「没配地址」都如实告知。
    /// </summary>
    private static async Task CheckUpdateAsync(Window win, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;   // 没配地址就别打扰用户

        try
        {
            var svc = _current?._sp?.GetService<UpdateService>();
            if (svc == null) return;

            var (hasUpdate, remote, message) = await svc.CheckAsync(url).ConfigureAwait(true);
            if (!hasUpdate)
            {
                System.Diagnostics.Debug.WriteLine("检查更新：" + message);
                return;
            }
            await NotifyAsync(win, "发现新版本", message + "\n\n（NewToys 不会自动替换自己，请手动更新。）");
        }
        catch { /* 检查更新失败绝不影响启动 */ }
    }

    /// <summary>单按钮提示框（与 <see cref="ConfirmAsync"/> 同款：纯代码构造 + 只取主题资源色）。</summary>
    private static async Task NotifyAsync(Window owner, string title, string message)
    {
        IBrush? Res(string key) => owner.FindResource(key) as IBrush;

        var ok = new Button { Content = "知道了", Width = 96, Classes = { "primary" } };
        var dlg = new Window
        {
            Width = 460,
            Height = 220,
            Title = title,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res("ContentBrush") ?? Brushes.Transparent,
            Content = new Border
            {
                Padding = new Thickness(20),
                Background = Res("PanelBrush") ?? Brushes.Transparent,
                Child = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold,
                                        Foreground = Res("PrimaryTextBrush") },
                        new TextBlock { Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                                        Foreground = Res("PrimaryTextBrush") },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Children = { ok }
                        }
                    }
                }
            }
        };
        ok.Click += (_, _) => dlg.Close();
        try { await dlg.ShowDialog(owner); }
        catch { /* 主窗口已关等情况：放弃提示 */ }
    }

    /// <summary>
    /// Debug 下的模块<b>显式类型清单</b>。Release 返回 null，改走 Modules 目录扫描。
    /// ⚠️ Debug 不能用 AppDomain 扫描：Debug 的程序集是延迟加载的，纯扫描经常一个模块都扫不到。
    /// </summary>
    private static Type[]? GetDebugModuleTypes()
    {
#if DEBUG
        return new[]
        {
            typeof(NewToys.PcTools.PcToolsModule),
            typeof(NewToys.WebTools.WebToolsModule),
            typeof(NewToys.VideoTools.VideoToolsModule),
            typeof(NewToys.Settings.SettingsModule),
        };
#else
        return null;
#endif
    }

    /// <summary>
    /// 把「跟随系统」所需的系统明暗状态喂给 <see cref="IThemeService"/>。
    /// <para>
    /// 为什么放在 Shell：<c>IPlatformSettings</c> 是 Avalonia 类型，而 <c>Core</c> 不引用 Avalonia，
    /// 所以由这一层读、由 <see cref="IThemeService.SystemDark"/> 收。
    /// 事件可能从**非 UI 线程**触发，改状态要 <c>Post</c> 回 UI 线程（随后 <c>ApplyTheme</c> 会动资源字典）。
    /// </para>
    /// 平台不支持时静默放弃：System 模式退化为「系统状态固定为默认深色」，不影响其余功能。
    /// </summary>
    private void WatchSystemTheme(IThemeService theme)
    {
        try
        {
            var settings = PlatformSettings;
            if (settings == null) return;

            theme.SystemDark = settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
            settings.ColorValuesChanged += (_, e) =>
            {
                var dark = e.ThemeVariant == PlatformThemeVariant.Dark;
                if (Dispatcher.UIThread.CheckAccess()) theme.SystemDark = dark;
                else Dispatcher.UIThread.Post(() => theme.SystemDark = dark);
            };
        }
        catch { /* 平台无此能力（或早期版本 API 差异）时忽略 */ }
    }

    /// <summary>
    /// 按当前主题状态重建资源字典。**幂等**：随时可重复调用。
    /// 深色 / 浅色两套基础色 + **强调色派生色**（强调色与「选中的卡片」底/字色联动，见 <see cref="AccentPalette"/>）。
    /// </summary>
    private void ApplyTheme(IThemeService theme)
    {
        var dark = theme.IsDark;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = AccentPalette.Find(theme.AccentId);
        var accentColor = Color.Parse(accent.Color);

        var d = new ResourceDictionary();
        void S(string k, string c) => d[k] = new Avalonia.Media.SolidColorBrush(Color.Parse(c));
        if (dark)
        {
            S("ContentBrush","#191919"); S("TitleBarBrush","#1F1F1F"); S("TitleBarTextBrush","#E5E5E5");
            S("TitleBarButtonHoverBrush","#333333"); S("TitleBarCloseHoverBrush","#E81123"); S("TitleBarCloseHoverTextBrush","#FFFFFF");
            S("SiderBrush","#202020"); S("SiderTextBrush","#FFFFFF"); S("SiderIconBrush","#9A9A9A"); S("SiderIconActiveBrush","#FFFFFF"); S("SiderSelectedBrush","#2E2E2E"); S("SiderDividerBrush","#2E2E2E");
            S("SubSiderBrush","#262626"); S("SubSiderHeaderBrush","#2B2B2B");
            // 「选中的卡片」用强调色派生：底 ≈ 强调色压暗到深色面板上，字 ≈ 强调色提亮（深色底上才看得清）
            S("CardSelectedBrush", Hex(Blend(accentColor, Color.Parse("#262626"), 0.22)));
            S("CardSelectedTextBrush", Hex(Blend(accentColor, Colors.White, 0.25)));
            S("ContentBrush","#191919"); S("ContentHeaderBrush","#1F1F1F"); S("StatusBarBrush","#1F1F1F");
            S("PanelBrush","#262626"); S("CardHoverBrush","#333333"); S("DividerBrush","#333333"); S("DangerBrush","#F85149"); S("LoadingOverlayBrush","#80000000");
            S("PrimaryTextBrush","#E5E5E5"); S("SecondaryTextBrush","#9E9E9E"); S("SiderIconHoverBrush","#3A3A3A");
            S("AccentBrush", accent.Color); S("AccentHoverBrush", accent.HoverColor); S("AccentTextBrush","#FFFFFF");
            S("CodeBlockBrush","#1F1F1F"); S("InputBorderBrush","#3D3D3D"); S("InputHoverBorderBrush", accent.Color);
        }
        else
        {
            S("ContentBrush","#FFFFFF"); S("TitleBarBrush","#FFFFFF"); S("TitleBarTextBrush","#1F1F1F");
            S("TitleBarButtonHoverBrush","#E5E5E5"); S("TitleBarCloseHoverBrush","#E81123"); S("TitleBarCloseHoverTextBrush","#FFFFFF");
            S("SiderBrush","#2E2E2E"); S("SiderTextBrush","#FFFFFF"); S("SiderIconBrush","#B7B7B7"); S("SiderIconActiveBrush","#FFFFFF"); S("SiderSelectedBrush","#444444"); S("SiderDividerBrush","#3F3F3F");
            S("SubSiderBrush","#F3F3F3"); S("SubSiderHeaderBrush","#F7F7F7");
            // 浅色底上的选中卡片：底 ≈ 强调色冲淡到接近白，字 = 强调色本身
            S("CardSelectedBrush", Hex(Blend(accentColor, Colors.White, 0.16)));
            S("CardSelectedTextBrush", accent.Color);
            S("ContentBrush","#FFFFFF"); S("ContentHeaderBrush","#FFFFFF"); S("StatusBarBrush","#FAFBFC");
            S("PanelBrush","#F7F8FA"); S("CardHoverBrush","#EFEFEF"); S("DividerBrush","#E6E8EC"); S("DangerBrush","#D93026"); S("LoadingOverlayBrush","#80FFFFFF");
            S("PrimaryTextBrush","#1F1F1F"); S("SecondaryTextBrush","#8A8A8F"); S("SiderIconHoverBrush","#E9E9E9");
            S("AccentBrush", accent.Color); S("AccentHoverBrush", accent.HoverColor); S("AccentTextBrush","#FFFFFF");
            S("CodeBlockBrush","#FFFFFF"); S("InputBorderBrush","#D1D5DB"); S("InputHoverBorderBrush", accent.Color);
        }
        if (Resources != null)
        {
            // 只移除上一次注入的 NewToys 主题字典，避免 Clear() 误删 FluentTheme 自带的系统资源字典
            if (_themeDictionary != null && Resources.MergedDictionaries.Contains(_themeDictionary))
                Resources.MergedDictionaries.Remove(_themeDictionary);
            Resources.MergedDictionaries.Add(d);
            _themeDictionary = d;
        }
    }

    /// <summary>按 <paramref name="fgWeight"/>（0~1，前景占比）做线性混色 —— 用来从强调色派生浅/深变体。</summary>
    private static Color Blend(Color fg, Color bg, double fgWeight)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(Math.Round(a * fgWeight + b * (1 - fgWeight)), 0, 255);
        return Color.FromArgb(255, Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }

    /// <summary><c>#RRGGBB</c> 文本（<c>Color.Parse</c> 能原样读回）。</summary>
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// 通用二次确认对话框（当前只给「软件包管理 → 卸载」用）。
    /// <para>
    /// 只用主题资源取色（<c>FindResource</c> + 兜底），不写死颜色 —— 浅色主题下不会变成黑框。
    /// 按钮沿用全局的 <c>primary</c> / <c>ghost</c> 样式。
    /// </para>
    /// </summary>
    private static async Task<bool> ConfirmAsync(Window owner, string message)
    {
        IBrush? Res(string key) => owner.FindResource(key) as IBrush;

        var cancel = new Button { Content = "取消", Width = 88, Classes = { "ghost" } };
        var ok = new Button { Content = "继续", Width = 96, Classes = { "primary" } };

        var dlg = new Window
        {
            Width = 460,
            Height = 200,
            Title = "请确认",
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res("ContentBrush") ?? Brushes.Transparent,
            Content = new Border
            {
                Padding = new Thickness(20),
                Background = Res("PanelBrush") ?? Brushes.Transparent,
                Child = new StackPanel
                {
                    Spacing = 18,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13,
                            Foreground = Res("PrimaryTextBrush")
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 10,
                            Children = { cancel, ok }
                        }
                    }
                }
            }
        };

        cancel.Click += (_, _) => dlg.Close(false);
        ok.Click += (_, _) => dlg.Close(true);
        return await dlg.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// 通用单行输入对话框（当前给「视频库 → 重命名文件」用）。
    /// <para>同样是纯代码构造 + 只取主题资源色，浅色主题下不会变成黑框。</para>
    /// </summary>
    private static async Task<string?> InputAsync(Window owner, string title, string initialValue)
    {
        IBrush? Res(string key) => owner.FindResource(key) as IBrush;

        var box = new TextBox
        {
            Text = initialValue ?? "",
            FontSize = 13,
            Foreground = Res("PrimaryTextBrush"),
            Background = Res("PanelBrush")
        };
        var cancel = new Button { Content = "取消", Width = 88, Classes = { "ghost" } };
        var ok = new Button { Content = "确定", Width = 96, Classes = { "primary" } };

        var dlg = new Window
        {
            Width = 460,
            Height = 190,
            Title = title,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res("ContentBrush") ?? Brushes.Transparent,
            Content = new Border
            {
                Padding = new Thickness(20),
                Background = Res("PanelBrush") ?? Brushes.Transparent,
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 13,
                            Foreground = Res("PrimaryTextBrush")
                        },
                        box,
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            Spacing = 10,
                            Children = { cancel, ok }
                        }
                    }
                }
            }
        };

        // 回车 = 确定（Avalonia 的 TextBox 没有 AcceptButton，只能自己接键盘）
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            dlg.Close(box.Text ?? "");
            e.Handled = true;
        };
        cancel.Click += (_, _) => dlg.Close(null);
        ok.Click += (_, _) => dlg.Close(box.Text ?? "");

        dlg.Opened += (_, _) => box.Focus();
        return await dlg.ShowDialog<string?>(owner);
    }
}