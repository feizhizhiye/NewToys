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
    private ServiceProvider? _sp;
    private static App? _current;
    private ResourceDictionary? _themeDictionary;

    /// <summary>每配置单例的互斥体（v3.26，需求 §3.5）——⛔ 必须静态持有整个进程生命周期，回收 = 释放。</summary>
#pragma warning disable CS0414 // 字段只为进程级持有防 GC 回收，本进程不读取
    private static Mutex? _profileMutex;
#pragma warning restore CS0414

    public static T? GetService<T>() where T : class
        => _current?._sp?.GetService<T>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 最先挂上全局异常捕获：模块页里任何"没接住"的异常都会落到 logs\crash-*.log，
        // 拿真实堆栈定位，而不是靠猜（见方法注释）。
        InstallCrashHandlers();

        try
        {
            // （启动提速）：打点 —— 相对本方法进入时刻的累计毫秒，首帧后写一行到 logs\startup-*.log。
            // 用途：判断「延迟初始化」到底省了多少（⛔ 靠猜）。Debug 下同时进调试输出。
            var swStartup = System.Diagnostics.Stopwatch.StartNew();
            var startupMarks = new List<(string Name, long Ms)>();
            void Mark(string name) => startupMarks.Add((name, swStartup.ElapsedMilliseconds));

            var services = new ServiceCollection();

            // 多实例启动参数 --profile <配置名>（需求 §3.5）——最早处解析，交给 ConfigStoreService。
            var profileOverride = ParseProfileOverride(Environment.GetCommandLineArgs());

            // Core services
            services.AddSingleton(sp => new ConfigStoreService(profileOverride));
            // 框架级偏好（主题 / 窗口 / 收藏 / 列宽 / 页码大小）统一落 config\shell.json
            services.AddSingleton<ShellConfigStore>();
            // / 需求 §3.5：按模块分区的 KV 选项存储（落 config\<module>.json；换 profile ⇒ 天然读到另一份）。
            // 模块只依赖 IOptionsStore、⛔ 不碰文件；将来整体换 SQLite 调用方零改动。
            services.AddSingleton<IOptionsStore>(sp => new JsonOptionsStore(
                sp.GetRequiredService<ConfigStoreService>(),
                sp.GetRequiredService<ShellConfigStore>()));
            // 机器级配置（解释器 / ffmpeg / 播放器 / 更新地址 / DNS）落 %LOCALAPPDATA%\NewToys\env.json
            services.AddSingleton<EnvConfigService>();
            // Python 运行时底座（跨模块共用 —— 网络工具的规则集 / 脚本管理器的 .py 命令）。
            // 长驻 worker + stdin/stdout 行分隔 JSON；⛔ 不引 pythonnet（保持平台中立，framework.md §2.1）。
            services.AddSingleton<IPythonRuntime, PythonRuntime>();
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ILaunchConfigService, LaunchConfigService>();
            services.AddSingleton<IMenuService, MenuService>();
            // 页面类型索引（菜单项上的 ViewTypeName 是字符串，View/VM 只能反射落地）——
            // 单例 + 缓存，避免每次导航全量扫程序集；模块热加载后由 ModuleReloadService 失效重建。
            services.AddSingleton<IPageCatalog, PageCatalog>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INavigationService, NavigationService>();
            // 模块热加载契约（标题栏按钮移入功能清单页；Shell 订阅 Reloaded 重建左树）。
            services.AddSingleton<IModuleReloadService, ModuleReloadService>();
            // 外部程序注册表（清单 + **惰性探测**，需求 §4.9.2 第 5 条）——
            // 单例 + 结果缓存：进「运行环境」节 / 功能首次要用到时才真探，⛔ 不拖启动。
            services.AddSingleton<IExternalToolRegistry, ExternalToolRegistry>();
            services.AddSingleton<LaunchRunner>();
            // ⚠️ 之前只注册了**具体类型**，没有注册接口 —— 于是「设置 → 加密 DNS」页要想拿
            // IEncryptedDnsService 就解析不到（DI 会退回到无参构造，页面变成"未就绪"骨架）。
            // 现在两个都注册，且指向**同一个单例**（接口 → 具体类型），别改成两次 new。
            services.AddSingleton<EncryptedDnsService>();
            services.AddSingleton<IEncryptedDnsService>(sp => sp.GetRequiredService<EncryptedDnsService>());

            // 「软件包管理」的两项服务（IPackageManagerService / IPackageSettingsService）
            // 已随整套代码下放到功能模块 ⇒ DI 注册在模块内（归 Module.SystemTools，不再在脚本中心），
            // Shell 这里不再注册、也不再给它的确认钩子接线（改走 Core.DialogHooks，见下面）。

            // 首页收藏夹：落 config\favorites.json（可被数据管理备份），单一真相源，标题/图标/简介现从菜单取。
            services.AddSingleton<IFavoritesService, FavoritesService>();

            // 列表页「列显隐」偏好（右键表头增删列，规范 3.8）：落 config\columnlayout.json（会被数据管理备份）。
            services.AddSingleton<IColumnLayoutService, ColumnLayoutService>();

            // 多配置（一个配置 = 一套独立的 config+data+logs）；v3.26 起切换 = 本窗重启到目标配置（显式传 --profile）。
            services.AddSingleton<IProfileService, ProfileService>();

            // 工程内唯一的本地数据库（data\newtoys.db）：当前给「视频工具 → 视频库」的媒体信息索引用；
            // 后续更多结构化数据也走它（见 SqliteDatabase 的类注释）。
            services.AddSingleton<SqliteDatabase>();
            // 总库 schema 统一入口 —— 容器里所有 ILibrarySchemaContributor（由各模块 RegisterServices 注册）
            // 在首帧之后由 ModuleLoader.InitializeAllDeferred 统一跑一遍（建表 / 补列 / 一次性迁移）。
            services.AddSingleton<NewToys.Core.Services.LibrarySchemaService>();

            // 跨模块共用的文件系统动作（改名 / 移动 / 复制 / 回收站删除）。
            // 只管磁盘，不记账、不弹窗 —— 库表日志与确认框留在各模块自己那层。
            services.AddSingleton<IFileOperationService, FileOperationService>();

            // 外部工具执行底座（捕获式：显式 UTF-8 / 超时 / 取消杀进程树）。
            // 接口在 Shared、实现在 Core；平台能力（打开文件）与可见控制台/提权不走这里（见 IProcessRunner 注释）。
            services.AddSingleton<IProcessRunner, ProcessRunner>();

            // 备份路由 —— 框架节在这里注册，模块节在各自 RegisterServices 里注册
            // （同一个 IBackupSection 契约，BackupService 经 IEnumerable<IBackupSection> 聚合）。
            services.AddSingleton<IBackupSection, NewToys.Shell.Services.ShellBackupSection>();
            services.AddSingleton<IBackupSection, NewToys.Shell.Services.EnvBackupSection>();
            // 数据节（默认不勾）：总库走 VACUUM INTO 快照；缩略图目录读 media-assets 设置，空则 data\thumbs
            services.AddSingleton<IBackupSection, NewToys.Shell.Services.DataDbBackupSection>();
            services.AddSingleton<IBackupSection, NewToys.Shell.Services.DataThumbsBackupSection>();
            services.AddSingleton<BackupService>();

            // 「设置 → 通用」的系统级行为：开机启动项（注册表）+ 启动时检查更新。
            // 两者都不碰 Avalonia，所以能放在 Core 里；这里只是把它们挂进 DI。
            services.AddSingleton<StartupService>();
            services.AddSingleton<UpdateService>();

            services.AddSingleton<ShellViewModel>();
            services.AddTransient<HomeView>();
            services.AddTransient<HomeViewModel>();

            // 应用级设置 6 页（原 Module.Settings 的框架职责收归 Shell，View/VM 已在 src/Shell 下）。
            services.AddTransient<GeneralView>();        services.AddTransient<GeneralViewModel>();
            // 「通用」节拆成三个节（启动与退出 / 更新 / 主题外观）⇒ 两个新 View，
            // 三节共用同一个 GeneralViewModel（只在上面注册一次）。
            services.AddTransient<UpdateView>();
            services.AddTransient<ThemeView>();
            // 「更新」+「关于」合并成「版本与更新」节 ⇒ 拼装 View（内部仍用上面两个 View）
            services.AddTransient<UpdateAboutView>();
            services.AddTransient<PathView>();           services.AddTransient<PathViewModel>();
            services.AddTransient<LaunchConfigView>();   services.AddTransient<LaunchConfigViewModel>();
            services.AddTransient<DnsView>();            services.AddTransient<DnsViewModel>();
            services.AddTransient<DataView>();           services.AddTransient<DataViewModel>();
            services.AddTransient<AllFunctionsView>();   services.AddTransient<AllFunctionsViewModel>();
            // 「一页多节」复合页 + 「关于」节（需求 §4.9.2）
            services.AddTransient<SettingsView>();       services.AddTransient<SettingsViewModel>();
            services.AddTransient<AboutSettingsView>();  services.AddTransient<AboutSettingsViewModel>();

            // 主窗口大小 / 位置记忆（落本配置 config\window.json；多实例各记各的，需求 §四）
            services.AddSingleton<NewToys.Shell.Services.WindowBoundsService>();

            // 模块加载（v3.26，需求 §3.4）：必载清单（为空 —— 应用级设置归 Shell 自带，不再单独成程序集）
            // + ModuleLoader 扫 Modules\ 补功能模块。新增功能模块两处登记：Shell.csproj 的 Debug ProjectReference
            // + 下面 GetMandatoryModuleTypes 的 #if DEBUG 追加。
            Mark("DI注册");
            Core.Services.ModuleLoader.LoadAll(services, GetMandatoryModuleTypes());
            Mark("模块扫描");

            _sp = services.BuildServiceProvider();
            _current = this;
            AppServices.Initialize(_sp);
            Mark("容器构建");

            // 每配置单例 + --profile 诊断（需求 §3.5）——同一配置只允许一个实例，
            // 第二例提示后退出（数据隔离：不同配置各跑各的目录，同配置共享会互相覆盖）。
            var store = _sp.GetRequiredService<ConfigStoreService>();
            if (store.ProfileOverrideError != null)
                WriteStartupLog($"{store.ProfileOverrideError} —— 已回退到配置「{store.ActiveProfileName}」");
            if (!AcquireProfileMutex(store)) return;

            // 内置三档（debug / default / release）已撤销 —— 配置 = 纯根目录，⛔ 不再自动播种任何档。

            // 应用级设置的菜单注册归 Shell（原 Module.Settings 的 Initialize 职责）——
            // 左树**没有**「设置」一级菜单项：注册的是一个 IsHome 的不渲染宿主分组
            // （挂应用级设置的节锚点 + 功能清单页），设置页入口 = 左树底部齿轮 / Ctrl+, / 首页一键操作的齿轮。
            // 🔴 必须先于 InitializeAll：功能模块要在自己的分组下注册菜单（分组先存在才命中）。
            NewToys.Shell.Settings.AppSettingsRegistration.RegisterMenu(_sp);

            Core.Services.ModuleLoader.InitializeAll(_sp);
            Mark("模块菜单初始化");

            var theme = _sp.GetRequiredService<IThemeService>();
            WatchSystemTheme(theme);          // 先注入系统明暗（「跟随系统」要用），再刷第一帧
            ApplyTheme(theme);
            theme.ThemeChanged += () => ApplyTheme(theme);

            var win = new ShellView { DataContext = _sp.GetRequiredService<ShellViewModel>() };
            _mainWindow = win;
            Mark("主窗口构造");

            // 标题带配置名（多实例在任务栏里可区分，需求 §3.5）
            win.Title = $"NewToys · {store.ActiveProfileName}";

            // 窗口图标 = 应用图标（与托盘同源同一份，底色随当前强调色）
            win.Icon = CreateAppIcon(CurrentAccent());

            // 公共对话框钩子（Core.DialogHooks）：给各模块用（视频工具的重命名 / 删除文件 / 清空缓存 …）。
            // 软件包的「卸载 / 删源」二次确认也走这一条（原 PackageManagerService.ConfirmHandler 已删）。
            // ⛔ 模块不能反向引用 Shell，所以只能由这里注入；没注入时钩子一律按"用户取消"处理。
            // 宿主实现改为页内**弹出卡片**（DialogCardHost）——原独立系统 Window 的标题栏是系统装饰，
            //   明暗主题下与主窗口割裂；现在遮罩 + 主题色卡片铺在主窗口上，所有页面同一套视觉。
            DialogHooks.ConfirmHandler = msg => ConfirmAsync(msg);
            DialogHooks.InputHandler = (title, initial) => InputAsync(title, initial);

            // 详情卡渲染器随控件库搬进 NewToys.UI（⛔ 不引 Core/Shell）⇒ 它的「确认弹窗」
            // 钩子在 UI 侧声明（DetailCardHooks），这里把 Core 的 DialogHooks.ConfirmHandler 转接过去。
            DetailCardHooks.ConfirmHandler = DialogHooks.ConfirmHandler;

            // 选择器钩子 —— 工具条由 Shell 声明式承载后，页面 VM 也要能"弹选择器"
            // （VM ⛔ 不引 Avalonia，所以统一走 Core.DialogHooks，与上面两条同一个套路）。
            DialogHooks.PickFolderHandler = (title, initialDir) => PickFolderAsync(win, title, initialDir);
            DialogHooks.PickFilesHandler = (title, initialDir, multi, pattern) => PickFilesAsync(win, title, initialDir, multi, pattern);
            DialogHooks.SaveFileHandler = (title, initialDir, suggestedName, pattern) => SaveFileAsync(win, title, initialDir, suggestedName, pattern);

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

            // ==================== 模块重活延迟到首帧之后 ====================
            // Show() 只是把窗口挂到窗口管理器上，首帧还没画。把「建库 / 读配置 / 种子播种 /
            // 扫描器接线」排进 Background 优先级的 UI 队列 ⇒ 排在首帧渲染之后执行：
            // 用户先看到完整界面（左树菜单在首帧前已注册，⛔ 不会先空后跳），再跑这些 IO。
            // ⚠️ 刻意留在 UI 线程（不用 Task.Run）：Sqlite / 配置 store 都不是线程安全的，
            //    换线程会和「用户已经点开页面」的读取撞车。代价 = 启动瞬间 UI 卡几十毫秒。
            Mark("窗口显示");
            WriteStartupLog("启动打点（累计毫秒）：" + string.Join(" → ", startupMarks.Select(m => $"{m.Name} {m.Ms}")));

            var spForDeferred = _sp!;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () =>
                {
                    var deferredMs = Core.Services.ModuleLoader.InitializeAllDeferred(spForDeferred);
                    WriteStartupLog($"模块延迟初始化 {deferredMs}ms（首帧之后）");
                },
                Avalonia.Threading.DispatcherPriority.Background);

            // 托盘建不起来（平台不支持 / 无托盘环境）⇒ 如实提示一次，行为退化成普通最小化
            if (!trayOk && launchCfg.Config.StartMinimized)
            {
                ShowPlatformMessage("系统托盘不可用，「启动时最小化到托盘」已改为普通最小化。", "NewToys");
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
            var host = (_mainWindow as ShellView)?.DialogCards;
            if (host != null)
                await host.NotifyAsync("发现新版本", message + "\n\n（NewToys 不会自动替换自己，请手动更新。）");
        }
        catch { /* 检查更新失败绝不影响启动 */ }
    }

    /// <summary>
    /// 必载模块清单（v3.26，需求 §3.4）：**Release 为空** —— 原唯一的框架组件
    /// <c>Module.Settings</c> 已撤销，应用级设置归 Shell 自带（<c>AppSettingsRegistration</c> 建 <c>system</c> 宿主分组）。
    /// <para>Release 全部功能模块由 ModuleLoader 扫 Modules\ 补全；Debug 见下。</para>
    /// </summary>
    private static Type[] GetMandatoryModuleTypes()
    {
        var list = new List<Type>();
#if DEBUG
        // Debug 的显式清单：程序集延迟加载，纯扫描一个都扫不到（表现为「左侧菜单全空」）。
        // 新增功能模块：这里追加 + Shell.csproj 的 Debug ProjectReference，两处都要改。
        list.Add(typeof(NewToys.ScriptManager.ScriptManagerModule));
        list.Add(typeof(NewToys.WebTools.WebToolsModule));
        list.Add(typeof(NewToys.VideoTools.VideoToolsModule));
        list.Add(typeof(NewToys.MediaAssets.MediaAssetsModule));
        list.Add(typeof(NewToys.SystemTools.SystemToolsModule));
#endif
        return list.ToArray();
    }

}
