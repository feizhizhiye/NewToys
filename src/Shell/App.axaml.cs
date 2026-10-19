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

public class App : Application
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
            // NT-070（启动提速）：打点 —— 相对本方法进入时刻的累计毫秒，首帧后写一行到 logs\startup-*.log。
            // 用途：判断「延迟初始化」到底省了多少（⛔ 靠猜）。Debug 下同时进调试输出。
            var swStartup = System.Diagnostics.Stopwatch.StartNew();
            var startupMarks = new List<(string Name, long Ms)>();
            void Mark(string name) => startupMarks.Add((name, swStartup.ElapsedMilliseconds));

            var services = new ServiceCollection();

            // v3.26：多实例启动参数 --profile <配置名>（需求 §3.5）——最早处解析，交给 ConfigStoreService。
            var profileOverride = ParseProfileOverride(Environment.GetCommandLineArgs());

            // Core services
            services.AddSingleton(sp => new ConfigStoreService(profileOverride));
            // NT-077：框架级偏好（主题 / 窗口 / 收藏 / 列宽 / 页码大小）统一落 config\shell.json
            services.AddSingleton<ShellConfigStore>();
            // NT-061 / 需求 §3.5：按模块分区的 KV 选项存储（落 config\<module>.json；换 profile ⇒ 天然读到另一份）。
            // 模块只依赖 IOptionsStore、⛔ 不碰文件；将来整体换 SQLite 调用方零改动。
            services.AddSingleton<IOptionsStore>(sp => new JsonOptionsStore(
                sp.GetRequiredService<ConfigStoreService>(),
                sp.GetRequiredService<ShellConfigStore>()));
            // NT-077 第二批：机器级配置（解释器 / ffmpeg / 播放器 / 更新地址 / DNS）落 %LOCALAPPDATA%\NewToys\env.json
            services.AddSingleton<EnvConfigService>();
            // NT-083：Python 运行时底座（跨模块共用 —— 网络工具的规则集 / 脚本管理器的 .py 命令）。
            // 长驻 worker + stdin/stdout 行分隔 JSON；⛔ 不引 pythonnet（保持平台中立，framework.md §2.1）。
            services.AddSingleton<IPythonRuntime, PythonRuntime>();
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ILaunchConfigService, LaunchConfigService>();
            services.AddSingleton<IMenuService, MenuService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INavigationService, NavigationService>();
            // NT-052：模块热加载契约（标题栏按钮移入功能清单页；Shell 订阅 Reloaded 重建左树）。
            services.AddSingleton<IModuleReloadService, ModuleReloadService>();
            // v3.65 / NT-062：外部程序注册表（清单 + **惰性探测**，需求 §4.9.2 第 5 条）——
            // 单例 + 结果缓存：进「运行环境」节 / 功能首次要用到时才真探，⛔ 不拖启动。
            services.AddSingleton<IExternalToolRegistry, ExternalToolRegistry>();
            services.AddSingleton<LaunchRunner>();
            // ⚠️ 之前只注册了**具体类型**，没有注册接口 —— 于是「设置 → 加密 DNS」页要想拿
            // IEncryptedDnsService 就解析不到（DI 会退回到无参构造，页面变成"未就绪"骨架）。
            // 现在两个都注册，且指向**同一个单例**（接口 → 具体类型），别改成两次 new。
            services.AddSingleton<EncryptedDnsService>();
            services.AddSingleton<IEncryptedDnsService>(sp => sp.GetRequiredService<EncryptedDnsService>());

            // NT-074：「软件包管理」的两项服务（IPackageManagerService / IPackageSettingsService）
            // 已随整套代码下放到 Module.ScriptManager（只有它用）⇒ DI 注册移到 ScriptManagerModule，
            // Shell 这里不再注册、也不再给它的确认钩子接线（改走 Core.DialogHooks，见下面）。

            // 首页收藏夹：落 config\favorites.json（可被数据管理备份），单一真相源，标题/图标/简介现从菜单取。
            services.AddSingleton<IFavoritesService, FavoritesService>();

            // 列表页「列显隐」偏好（右键表头增删列，规范 3.8）：落 config\columnlayout.json（会被数据管理备份）。
            services.AddSingleton<IColumnLayoutService, ColumnLayoutService>();

            // v3.5：多配置（一个配置 = 一套独立的 config+data+logs）；v3.26 起切换 = 本窗重启到目标配置（显式传 --profile）。
            services.AddSingleton<IProfileService, ProfileService>();

            // 工程内唯一的本地数据库（data\newtoys.db）：当前给「视频工具 → 视频库」的媒体信息索引用；
            // 后续更多结构化数据也走它（见 SqliteDatabase 的类注释）。
            services.AddSingleton<SqliteDatabase>();
            // NT-076：总库 schema 统一入口 —— 容器里所有 ILibrarySchemaContributor（由各模块 RegisterServices 注册）
            // 在首帧之后由 ModuleLoader.InitializeAllDeferred 统一跑一遍（建表 / 补列 / 一次性迁移）。
            services.AddSingleton<NewToys.Core.Services.LibrarySchemaService>();

            // v3.66 / NT-070：跨模块共用的文件系统动作（改名 / 移动 / 复制 / 回收站删除）。
            // 只管磁盘，不记账、不弹窗 —— 库表日志与确认框留在各模块自己那层。
            services.AddSingleton<IFileOperationService, FileOperationService>();

            // NT-081 步骤3：外部工具执行底座（捕获式：显式 UTF-8 / 超时 / 取消杀进程树）。
            // 接口在 Shared、实现在 Core；平台能力（打开文件）与可见控制台/提权不走这里（见 IProcessRunner 注释）。
            services.AddSingleton<IProcessRunner, ProcessRunner>();

            // NT-078：备份路由 —— 框架节在这里注册，模块节在各自 RegisterServices 里注册
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

            // NT-062：应用级设置 6 页（原 Module.Settings 的框架职责收归 Shell，View/VM 已在 src/Shell 下）。
            services.AddTransient<GeneralView>();        services.AddTransient<GeneralViewModel>();
            // v3.66 第五轮：「通用」节拆成三个节（启动与退出 / 更新 / 主题外观）⇒ 两个新 View，
            // 三节共用同一个 GeneralViewModel（只在上面注册一次）。
            services.AddTransient<UpdateView>();
            services.AddTransient<ThemeView>();
            // v3.67：「更新」+「关于」合并成「版本与更新」节 ⇒ 拼装 View（内部仍用上面两个 View）
            services.AddTransient<UpdateAboutView>();
            services.AddTransient<PathView>();           services.AddTransient<PathViewModel>();
            services.AddTransient<LaunchConfigView>();   services.AddTransient<LaunchConfigViewModel>();
            services.AddTransient<DnsView>();            services.AddTransient<DnsViewModel>();
            services.AddTransient<DataView>();           services.AddTransient<DataViewModel>();
            services.AddTransient<AllFunctionsView>();   services.AddTransient<AllFunctionsViewModel>();
            // v3.65 / NT-062：「一页多节」复合页 + 「关于」节（需求 §4.9.2）
            services.AddTransient<SettingsView>();       services.AddTransient<SettingsViewModel>();
            services.AddTransient<AboutSettingsView>();  services.AddTransient<AboutSettingsViewModel>();

            // v3.26：主窗口大小 / 位置记忆（落本配置 config\window.json；多实例各记各的，需求 §四）
            services.AddSingleton<NewToys.Shell.Services.WindowBoundsService>();

            // 模块加载（v3.26，需求 §3.4）：必载清单（NT-062 起为空 —— 应用级设置归 Shell 自带，不再单独成程序集）
            // + ModuleLoader 扫 Modules\ 补功能模块。新增功能模块两处登记：Shell.csproj 的 Debug ProjectReference
            // + 下面 GetMandatoryModuleTypes 的 #if DEBUG 追加。
            Mark("DI注册");
            Core.Services.ModuleLoader.LoadAll(services, GetMandatoryModuleTypes());
            Mark("模块扫描");

            _sp = services.BuildServiceProvider();
            _current = this;
            AppServices.Initialize(_sp);
            Mark("容器构建");

            // v3.26：每配置单例 + --profile 诊断（需求 §3.5）——同一配置只允许一个实例，
            // 第二例提示后退出（数据隔离：不同配置各跑各的目录，同配置共享会互相覆盖）。
            var store = _sp.GetRequiredService<ConfigStoreService>();
            if (store.ProfileOverrideError != null)
                WriteStartupLog($"{store.ProfileOverrideError} —— 已回退到配置「{store.ActiveProfileName}」");
            if (!AcquireProfileMutex(store)) return;

            // v3.66：内置三档（debug / default / release）已撤销 —— 配置 = 纯根目录，⛔ 不再自动播种任何档。

            // NT-062：应用级设置的菜单注册归 Shell（原 Module.Settings 的 Initialize 职责）——
            // v3.66 起左树**没有**「设置」一级菜单项：注册的是一个 IsHome 的不渲染宿主分组
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

            // v3.26：标题带配置名（多实例在任务栏里可区分，需求 §3.5）
            win.Title = $"NewToys · {store.ActiveProfileName}";

            // NT-057：窗口图标 = 应用图标（与托盘同源同一份，底色随当前强调色）
            win.Icon = CreateAppIcon(CurrentAccent());

            // 公共对话框钩子（Core.DialogHooks）：给各模块用（视频工具的重命名 / 删除文件 / 清空缓存 …）。
            // NT-074：软件包的「卸载 / 删源」二次确认也走这一条（原 PackageManagerService.ConfirmHandler 已删）。
            // ⛔ 模块不能反向引用 Shell，所以只能由这里注入；没注入时钩子一律按"用户取消"处理。
            // NT-084：宿主实现改为页内**弹出卡片**（DialogCardHost）——原独立系统 Window 的标题栏是系统装饰，
            //   明暗主题下与主窗口割裂；现在遮罩 + 主题色卡片铺在主窗口上，所有页面同一套视觉。
            DialogHooks.ConfirmHandler = msg => ConfirmAsync(msg);
            DialogHooks.InputHandler = (title, initial) => InputAsync(title, initial);

            // NT-063：详情卡渲染器随控件库搬进 NewToys.UI（⛔ 不引 Core/Shell）⇒ 它的「确认弹窗」
            // 钩子在 UI 侧声明（DetailCardHooks），这里把 Core 的 DialogHooks.ConfirmHandler 转接过去。
            DetailCardHooks.ConfirmHandler = DialogHooks.ConfirmHandler;

            // v3.53 / NT-055：选择器钩子 —— 工具条由 Shell 声明式承载后，页面 VM 也要能"弹选择器"
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

            // ==================== NT-070：模块重活延迟到首帧之后 ====================
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

            // NT-057：托盘建不起来（平台不支持 / 无托盘环境）⇒ 如实提示一次，行为退化成普通最小化
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

    // ==================== 全局异常捕获（诊断用） ====================

    private static bool _crashHooked;

    /// <summary>
    /// 挂上三道「最后的防线」，把 UI 线程 / 后台线程 / 未观察 Task 上的未处理异常
    /// 追加写到 <c>&lt;应用根&gt;\logs\crash-yyyyMMdd.log</c>。
    /// <para>
    /// Why：模块页里的绑定失败、异步回调异常默认只进 Debug 输出（Release 下根本看不到），
    /// 表现成「页面突然乱了 / 卡住 / 直接退出」却拿不到堆栈。落盘之后，把那个 log 发出来即可定位。
    /// </para>
    /// <para>
    /// ⚠️ 只记录、不改行为：<c>Handled</c> 保持原样（该崩还是崩），不把程序留在半坏状态里假装没事。
    /// </para>
    /// </summary>
    private static void InstallCrashHandlers()
    {
        if (_crashHooked) return;
        _crashHooked = true;

        // ① UI 线程（Avalonia Dispatcher）—— 绝大多数的「页面崩了」都在这里
        Dispatcher.UIThread.UnhandledException += (_, e) => WriteCrashLog("UI", e.Exception);

        // ② 后台线程上的未处理异常（进程级）
        AppDomain.CurrentDomain.UnhandledException += (_, e)
            => WriteCrashLog("AppDomain", e.ExceptionObject as Exception);

        // ③ fire-and-forget 的 Task 抛了异常又没人 await（抽帧 / 转码的异步回调就是这种）
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("Task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>追加一处未处理异常到当天的崩溃日志；写盘失败静默吞掉（记日志本身不能再崩）。</summary>
    private static void WriteCrashLog(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            // v3.5：优先用 ConfigStoreService 给的日志目录（只有它知道当前是哪个「配置」）；
            // 启动早期（服务还没建好）崩溃时，退回「应用根\logs」的兜底算法。
            string dir;
            var store = AppServices.Get<ConfigStoreService>();
            if (store != null)
            {
                dir = store.LogsDirectory;
            }
            else
            {
                var root = Environment.GetEnvironmentVariable("NEWTOYS_HOME");
                if (string.IsNullOrEmpty(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewToys");
                dir = Path.Combine(root, "logs");
            }

            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file,
                $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [{source}]  线程 {Environment.CurrentManagedThreadId} =====\n"
                + ex + "\n");

            System.Diagnostics.Debug.WriteLine($"[crash:{source}] {ex}");
        }
        catch { /* 记不下来也不影响主流程 */ }
    }

    // ==================== 多实例：--profile 参数 + 每配置单例（v3.26，需求 §3.5） ====================

    /// <summary>从命令行取 <c>--profile &lt;配置名&gt;</c>；没有该参数 → null（走 active-profile.txt 全局默认）。</summary>
    private static string? ParseProfileOverride(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// 每配置单例：同一配置同时只允许一个实例（数据隔离——同配置共享 config/db 会互相覆盖）。
    /// 返回 false = 配置已在运行（已弹系统提示并退出进程）。
    /// </summary>
    private static bool AcquireProfileMutex(ConfigStoreService store)
    {
        // ⛔ 别用 Global\（多会话 / 终端服务场景误伤）；配置名大小写不敏感 → 统一小写
        var name = "Local\\NewToys_Profile_" + store.ActiveProfileName.ToLowerInvariant();
        try
        {
            var m = new Mutex(false, name, out var createdNew);
            if (createdNew)
            {
                _profileMutex = m;   // ⛔ 必须进程级持有：回收 = 互斥体立刻释放
                return true;
            }
        }
        catch (Exception ex)
        {
            // 互斥体建不出来（异常名等）→ 放行，⛔ 不让诊断机制挡住启动
            WriteStartupLog($"配置互斥体创建失败，已放行：{ex.Message}");
            return true;
        }

        ShowPlatformMessage(
            $"配置「{store.ActiveProfileName}」已在运行。\n\n"
            + "同一个配置同时只允许一个窗口（否则两边的设置与数据会互相覆盖）。\n"
            + "请切换到已打开的窗口，或换一个配置启动。",
            "NewToys");
        Environment.Exit(0);
        return false; // 到不了（Exit 不返回），写上防流程分析报警
    }

    /// <summary>追加一行启动诊断到当天的启动日志（v3.26：--profile 被拒 / 互斥体异常等；写盘失败静默吞掉）。</summary>
    private static void WriteStartupLog(string message)
    {
        try
        {
            string dir;
            var store = AppServices.Get<ConfigStoreService>();
            if (store != null)
            {
                dir = store.LogsDirectory;
            }
            else
            {
                var root = Environment.GetEnvironmentVariable("NEWTOYS_HOME");
                if (string.IsNullOrEmpty(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewToys");
                dir = Path.Combine(root, "logs");
            }

            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"startup-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}\n");
            System.Diagnostics.Debug.WriteLine($"[startup] {message}");
        }
        catch { /* 记不下来也不影响主流程 */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;

    /// <summary>
    /// 平台门控的系统提示框（NT-081）。
    /// <para>🔴 <c>MessageBoxW</c> 是 user32 的 P/Invoke：非 Windows 上一调就 <c>DllNotFoundException</c>。
    /// 这里统一降级为「静默跳过」（调用点都已同时写启动日志，信息不丢）。</para>
    /// </summary>
    private static void ShowPlatformMessage(string text, string caption)
    {
        try
        {
            // 内联 IsWindows()：CA1416 只认这种写法
            if (!OperatingSystem.IsWindows()) return;
            MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONINFORMATION);
        }
        catch { /* 提示失败不影响主流程 */ }
    }

    // ==================== 托盘图标（「关闭按钮最小化到托盘」的落点） ====================

    private static TrayIcon? _tray;

    /// <summary>主窗口（NT-057：托盘菜单「先拉起窗口、再导航」要用它；主题切换后重画窗口图标也要用它）。</summary>
    private static Window? _mainWindow;

    /// <summary>托盘 / 窗口导航用的固定页 key（首页与设置总表，见需求 §5）。</summary>
    private const string TrayHomeKey = "home";
    private const string TraySettingsKey = "settings-path";

    /// <summary>
    /// 建托盘图标并挂上菜单（左键切窗口；右键五项：显示 / 隐藏、首页、设置、用另一个配置打开、退出）。
    /// <para>
    /// ⚠️ 托盘是 Avalonia 的原生控件，必须落在 Shell 这一层（<c>Core</c> 不引用 Avalonia）。
    /// 图标走 <see cref="CreateAppIcon"/>（与窗口图标**同源同一份**，不依赖打包资源）。
    /// </para>
    /// </summary>
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
                    // 起不来（互斥体挡住 / 路径非法）：托盘里⛔ 不弹窗打断用户，只留调试线索
                    System.Diagnostics.Debug.WriteLine("托盘启动配置实例失败：" + ex.Message);
                }
            };
            sub.Items.Add(one);
        }
        item.Menu = sub;
        return item;
    }

    /// <summary>
    /// **唯一的应用图标工厂**（NT-057 / 需求 §4.6.1）：窗口标题栏图标与托盘图标**用同一份**，
    /// 形状与 <c>Resources/app.ico</c>（exe 图标，由 <c>&lt;ApplicationIcon&gt;</c> 编进 exe）一致；
    /// 底色取**当前强调色**（⛔ 不写死），换主题 / 换强调色图标跟着变。
    /// <para>⛔ 改这里的圆角 / N 笔画比例，必须同步 <c>Resources/app.ico</c> 的形状口径，否则三处会走形。</para>
    /// </summary>
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
    /// 必载模块清单（v3.26，需求 §3.4）：NT-062 起 **Release 为空** —— 原唯一的框架组件
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
#endif
        return list.ToArray();
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
            S("ContentBrush","#191919");
            // NT-039：窗口外框（自绘 1px）。激活态要能在同色窗口上分辨出来 → 比 DividerBrush(#333) 更亮；
            // 失焦态压暗到接近底色，形成「哪个窗口在前台」的直接视觉信号。
            S("WindowBorderBrush","#4A4A4A"); S("WindowBorderInactiveBrush","#2A2A2A");
            S("TitleBarCloseHoverBrush","#E81123"); S("TitleBarCloseHoverTextBrush","#FFFFFF");
            S("SiderBrush","#202020"); S("SiderTextBrush","#FFFFFF"); S("SiderIconBrush","#9A9A9A"); S("SiderIconActiveBrush","#FFFFFF"); S("SiderSelectedBrush","#2E2E2E"); S("SiderDividerBrush","#2E2E2E");
            S("SubSiderBrush","#262626"); S("SubSiderHeaderBrush","#2B2B2B");
            // 「选中的卡片」用强调色派生：底 ≈ 强调色压暗到深色面板上，字 ≈ 强调色提亮（深色底上才看得清）
            // v3.19（NT-010 / 需求 §4.10.4「激活态加深」）：原强调色占比 22% 太淡，菜单/标签/列表选中不凸显；
            //   加深到 34%（选中 vs 悬停/常规对比更明显），字色同步提亮到 34% 保证深色底上可读。
            S("CardSelectedBrush", Hex(Blend(accentColor, Color.Parse("#262626"), 0.34)));
            S("CardSelectedTextBrush", Hex(Blend(accentColor, Colors.White, 0.34)));
            S("ContentBrush","#191919"); S("ContentHeaderBrush","#1F1F1F"); S("StatusBarBrush","#1F1F1F");
            S("PanelBrush","#262626"); S("CardHoverBrush","#333333"); S("DividerBrush","#333333"); S("DangerBrush","#F85149"); S("LoadingOverlayBrush","#80000000");
            S("PrimaryTextBrush","#E5E5E5"); S("SecondaryTextBrush","#9E9E9E"); S("SiderIconHoverBrush","#3A3A3A");
            // 输入框水印（v3.68 / 用户定调「太重」）：比次要文字再淡一档，只给 PlaceholderText 用
            S("PlaceholderTextBrush","#6B6B6B");
            S("AccentBrush", accent.Color); S("AccentHoverBrush", accent.HoverColor); S("AccentTextBrush","#FFFFFF");
            S("CodeBlockBrush","#1F1F1F"); S("InputBorderBrush","#3D3D3D"); S("InputHoverBorderBrush", accent.Color);
        }
        else
        {
            S("ContentBrush","#FFFFFF");
            // NT-039：浅色下窗口底色就是白的 → 边框必须够深才看得见（比 DividerBrush(#E6E8EC) 深一档）；
            // 失焦态退回 DividerBrush 同档，几乎融进白底。
            S("WindowBorderBrush","#C8CCD2"); S("WindowBorderInactiveBrush","#E6E8EC");
            S("TitleBarCloseHoverBrush","#E81123"); S("TitleBarCloseHoverTextBrush","#FFFFFF");
            S("SiderBrush","#2E2E2E"); S("SiderTextBrush","#FFFFFF"); S("SiderIconBrush","#B7B7B7"); S("SiderIconActiveBrush","#FFFFFF"); S("SiderSelectedBrush","#444444"); S("SiderDividerBrush","#3F3F3F");
            S("SubSiderBrush","#F3F3F3"); S("SubSiderHeaderBrush","#F7F7F7");
            // 浅色底上的选中卡片：底 ≈ 强调色冲淡到接近白，字 = 强调色本身
            // v3.19（NT-010）：浅色选中底同样加深（16%→26%），选中态更清晰、不易被忽略。
            S("CardSelectedBrush", Hex(Blend(accentColor, Colors.White, 0.26)));
            S("CardSelectedTextBrush", accent.Color);
            S("ContentBrush","#FFFFFF"); S("ContentHeaderBrush","#FFFFFF"); S("StatusBarBrush","#FAFBFC");
            S("PanelBrush","#F7F8FA"); S("CardHoverBrush","#EFEFEF"); S("DividerBrush","#E6E8EC"); S("DangerBrush","#D93026"); S("LoadingOverlayBrush","#80FFFFFF");
            S("PrimaryTextBrush","#1F1F1F"); S("SecondaryTextBrush","#8A8A8F"); S("SiderIconHoverBrush","#E9E9E9");
            // 输入框水印：浅色下同样比次要文字淡一档
            S("PlaceholderTextBrush","#B4B4BA");
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

        // NT-057：强调色可能刚变过 ⇒ 窗口 / 托盘图标一起重画
        RefreshAppIcon();
    }

    /// <summary>按 <paramref name="fgWeight"/>（0~1，前景占比）做线性混色 —— 用来从强调色派生浅/深变体。</summary>
    private static Color Blend(Color fg, Color bg, double fgWeight)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(Math.Round(a * fgWeight + b * (1 - fgWeight)), 0, 255);
        return Color.FromArgb(255, Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }

    /// <summary><c>#RRGGBB</c> 文本（<c>Color.Parse</c> 能原样读回）。</summary>
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ==================== 选择器钩子的宿主实现（v3.53 / NT-055） ====================

    /// <summary>把选择器的起始位置定到某个目录（拿不到就交给系统默认位置，⛔ 不抛）。</summary>
    private static async Task<IStorageFolder?> TryStartFolderAsync(IStorageProvider sp, string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir!); }
        catch { return null; }
    }

    private static async Task<string?> PickFolderAsync(Window owner, string title, string? initialDir)
    {
        var sp = owner.StorageProvider;
        if (sp == null) return null;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryStartFolderAsync(sp, initialDir)
        }).ConfigureAwait(false);
        return folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static async Task<IReadOnlyList<string>> PickFilesAsync(
        Window owner, string title, string? initialDir, bool allowMultiple, string pattern)
    {
        var sp = owner.StorageProvider;
        if (sp == null) return Array.Empty<string>();

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            SuggestedStartLocation = await TryStartFolderAsync(sp, initialDir),
            FileTypeFilter = BuildFileTypes(pattern)
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0) return Array.Empty<string>();
        var paths = new List<string>();
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        return paths;
    }

    private static async Task<string?> SaveFileAsync(
        Window owner, string title, string? initialDir, string suggestedName, string pattern)
    {
        var sp = owner.StorageProvider;
        if (sp == null) return null;

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = await TryStartFolderAsync(sp, initialDir),
            SuggestedFileName = suggestedName,
            FileTypeChoices = BuildFileTypes(pattern)
        }).ConfigureAwait(false);
        return file?.TryGetLocalPath();
    }

    /// <summary>把通配模式转成选择器的类型过滤（多个模式用 <c>;</c> 分隔；<c>*</c> = 所有文件）。</summary>
    private static FilePickerFileType[] BuildFileTypes(string pattern)
    {
        var patterns = (pattern ?? "").Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (patterns.Length == 0 || (patterns.Length == 1 && patterns[0] == "*"))
            return new[] { new FilePickerFileType("所有文件") { Patterns = new[] { "*" } } };

        return new[]
        {
            new FilePickerFileType(string.Join(" / ", patterns)) { Patterns = patterns },
            new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
        };
    }

    /// <summary>
    /// 通用二次确认对话框（NT-084 起 = 页内弹出卡片，见 <see cref="DialogCardHost"/>）。
    /// <para>钩子没接上（主窗口未构造）时返回 false = 当作用户没同意。</para>
    /// </summary>
    private static Task<bool> ConfirmAsync(string message)
        => (_mainWindow as ShellView)?.DialogCards.ConfirmAsync(message) ?? Task.FromResult(false);

    /// <summary>
    /// 通用单行输入对话框（NT-084 起 = 页内弹出卡片；取消返回 null）。
    /// </summary>
    private static Task<string?> InputAsync(string title, string initialValue)
        => (_mainWindow as ShellView)?.DialogCards.InputAsync(title, initialValue)
           ?? Task.FromResult<string?>(null);
}