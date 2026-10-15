using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 通用设置页（v1.35：**四项系统级行为全部真正生效**；NT-051：合并「主题外观」「关于」两页为四节）。
/// <list type="bullet">
///   <item><b>开机时启动</b>：写 / 删 <c>HKCU\…\Run</c> 里的启动项（<see cref="StartupService"/>），勾选即改注册表；</item>
///   <item><b>启动时最小化到托盘</b>：由 Shell 在启动时读本开关决定是否显示主窗口（改了下次启动生效）；</item>
///   <item><b>关闭按钮最小化到托盘</b>：主窗口 <c>OnClosing</c> 每次关闭时实时读本开关 → <b>立即生效</b>；</item>
///   <item><b>启动时检查更新</b>：启动时 GET 一个纯文本版本地址（<c>app.json</c> 的 <c>UpdateUrl</c>），只提示不自动更新；</item>
///   <item><b>主题外观</b>：明暗模式 + 强调色，点一下立即生效（原 ThemeViewModel，NT-051 并入）；</item>
///   <item><b>关于</b>：版本号 / 技术栈（原 AboutViewModel，NT-051 并入）。</item>
/// </list>
/// <para>
/// ⚠️ 诚实边界（规范 0.3）：没配更新地址就是「未配置」，页面照实写，不假装检查过；
/// 托盘 / 最小化这类**要下一次启动才看得到**的，也在界面上标注清楚。
/// </para>
/// </summary>
public partial class GeneralViewModel : ObservableObject, IStatusProvider
{
    private readonly ILaunchConfigService? _launch;
    private readonly IAppConfigService? _app;
    private readonly StartupService? _startup;
    private readonly UpdateService? _update;
    private readonly IThemeService? _theme;

    /// <summary>抑制标志：加载配置时回写属性不该反过来触发保存。</summary>
    private bool _loading;

    /// <summary>抑制标志：区分"程序回写属性"与"用户点击控件"，避免互相触发（主题节用）。</summary>
    private bool _syncing;

    // ==================== 启动与退出 ====================

    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _checkUpdateOnStartup = true;
    [ObservableProperty] private bool _closeToTray;

    /// <summary>更新检查用的版本文件地址（自由文本 → 需点「💾 保存」，规范 3.5）。</summary>
    [ObservableProperty] private string _updateUrl = "";

    [ObservableProperty] private string _statusText = "";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>开机启动项的**真实状态**（读注册表，不是读配置）—— 让"配置勾了但没写进去"这种偏差一眼可见。</summary>
    [ObservableProperty] private string _startupStateText = "";

    [ObservableProperty] private bool _isCheckingUpdate;

    public bool IsNotBusy => !IsCheckingUpdate;

    /// <summary>当前程序版本（显示在页面上，检查更新时与线上比对）。</summary>
    public string CurrentVersion => UpdateService.CurrentVersion;

    /// <summary>托盘相关开关的说明（哪些要重启才生效，一次讲清）。</summary>
    public string TrayHint =>
        "「启动时最小化到托盘」与「开机启动」要**下一次启动**才看得到效果；" +
        "「关闭按钮最小化到托盘」**立即生效**（主窗口每次关闭时实时读这个开关）。" +
        "想彻底退出：托盘图标右键 → 「退出 NewToys」。";

    // ==================== 主题外观（NT-051 由 ThemeViewModel 并入） ====================

    [ObservableProperty] private bool _isDarkMode;
    [ObservableProperty] private bool _isLightMode;
    [ObservableProperty] private bool _isSystemMode;
    [ObservableProperty] private AccentOption? _selectedAccent;

    /// <summary>强调色色卡数据源（直接取自 <see cref="AccentPalette"/>，与 Shell 注入用的是同一份表）。</summary>
    public ObservableCollection<AccentOption> Accents { get; } = new();

    // ==================== 关于（NT-051 由 AboutViewModel 并入） ====================

    [ObservableProperty] private string _appName = "NewToys";
    /// <summary>InformationalVersion（编译时由 src\Directory.Build.props 自动生成，禁止手写）。</summary>
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _buildConfiguration = "";
    [ObservableProperty] private string _runtimeDirectory = AppContext.BaseDirectory;
    [ObservableProperty] private string _dataDirectory = "";
    [ObservableProperty] private string _description = "个人高强度使用工具集";

    public ObservableCollection<TechStackItem> TechStack { get; } = new();
    public ObservableCollection<string> CoreConstraints { get; } = new();

    public GeneralViewModel() { BuildAccents(); }

    public GeneralViewModel(ILaunchConfigService launch, IAppConfigService app,
                            StartupService startup, UpdateService update, IThemeService theme,
                            ConfigStoreService? store = null)
    {
        _launch = launch;
        _app = app;
        _startup = startup;
        _update = update;
        _theme = theme;

        // —— 主题外观（原 ThemeViewModel 构造体：建色卡 + 读当前值）——
        BuildAccents();
        Load();

        // —— 启动与退出（原 GeneralViewModel 构造体）——
        var c = launch.Config;
        _loading = true;
        StartWithSystem = c.StartWithSystem;
        StartMinimized = c.StartMinimized;
        CheckUpdateOnStartup = c.CheckUpdateOnStartup;
        CloseToTray = c.CloseToTray;
        UpdateUrl = (app.UpdateUrl ?? "").Trim();
        _loading = false;

        RefreshStartupState();
        StatusText = $"当前版本 {CurrentVersion} · 选项已同步（launch.json / app.json）。";

        // —— 关于（原 AboutViewModel 构造体）——
        _version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion ?? "unknown";
#if DEBUG
        _buildConfiguration = "Debug";
#else
        _buildConfiguration = "Release";
#endif
        _dataDirectory = store?.DataDir ?? app.DataDirectory ?? "";

        TechStack.Add(new TechStackItem { Name = ".NET", Ver = "10" });
        TechStack.Add(new TechStackItem { Name = "Avalonia UI", Ver = "12" });
        TechStack.Add(new TechStackItem { Name = "CommunityToolkit.Mvvm", Ver = "8.x" });
        TechStack.Add(new TechStackItem { Name = "Microsoft.Extensions.DependencyInjection", Ver = "9.x" });

        CoreConstraints.Add("项目分 7 层：Shared / Core / Shell + PcTools / WebTools / VideoTools / Settings 四个模块");
        CoreConstraints.Add("依赖方向单向：Module.* → Core → Shared；模块禁止反向引用 Shell，模块之间禁止互相引用");
        CoreConstraints.Add("菜单 100% 由 IMenuService 注册，Shell 里禁止硬编码任何模块名 / 页面名 / 导航键");
        CoreConstraints.Add("Debug 走显式模块类型清单，Release 只扫 <程序目录>\\Modules\\");
        CoreConstraints.Add("View ↔ ViewModel 按约定自动配对：XxxView ↔ 同命名空间 .ViewModels.XxxViewModel");
        CoreConstraints.Add("主题 Token 用 *Brush 语义键；数据持久化在 <应用根>\\data");
    }

    partial void OnStartWithSystemChanged(bool value) { Persist(); ApplyStartup(); }
    partial void OnStartMinimizedChanged(bool value) => Persist();
    partial void OnCheckUpdateOnStartupChanged(bool value) => Persist();
    partial void OnCloseToTrayChanged(bool value) => Persist();

    /// <summary>把四个开关整体写回 <c>launch.json</c>（改哪个字段都走这里，避免半保存状态）。</summary>
    private void Persist()
    {
        if (_loading || _launch == null) return;

        var c = _launch.Config;
        c.StartWithSystem = StartWithSystem;
        c.StartMinimized = StartMinimized;
        c.CheckUpdateOnStartup = CheckUpdateOnStartup;
        c.CloseToTray = CloseToTray;

        var (ok, msg) = _launch.Save(c);
        StatusText = ok
            ? $"已保存 · {DateTime.Now:HH:mm:ss}（写入 launch.json）"
            : $"保存失败：{msg}";
    }

    /// <summary>「开机时启动」：把注册表改成与勾选一致（真实生效，不只是存起来）。</summary>
    private void ApplyStartup()
    {
        if (_loading || _startup == null) return;

        if (!_startup.IsSupported)
        {
            StartupStateText = "当前平台不支持开机启动（仅 Windows）。";
            StatusText = "已保存到 launch.json，但开机启动只在 Windows 上能真正生效。";
            return;
        }

        var (ok, msg) = _startup.SetEnabled(StartWithSystem);
        RefreshStartupState();
        StatusText = ok ? "已保存 · " + msg : "⚠ " + msg;
    }

    /// <summary>重读一次注册表，把开机启动项的真实状态显示出来。</summary>
    private void RefreshStartupState()
    {
        if (_startup == null) { StartupStateText = "开机启动服务未就绪。"; return; }
        if (!_startup.IsSupported) { StartupStateText = "当前平台不支持开机启动（仅 Windows）。"; return; }

        var real = _startup.IsEnabled;
        StartupStateText = real == StartWithSystem
            ? $"注册表状态：{(real ? "已启用" : "未启用")}（与上方勾选一致）"
            : $"⚠ 注册表状态：{(real ? "已启用" : "未启用")}，与上方勾选不一致 —— 点一下勾选框可重新写一次";
    }

    /// <summary>💾 保存更新地址（自由文本 → 显式保存，不边打边写）。</summary>
    [RelayCommand]
    private void SaveUpdateUrl()
    {
        if (_app == null) return;
        _app.UpdateUrl = (UpdateUrl ?? "").Trim();
        StatusText = (UpdateUrl ?? "").Trim().Length == 0
            ? "已清空更新地址 —— 启动时不再检查更新。"
            : "已保存更新地址（app.json），下次启动会检查。";
    }

    /// <summary>🔎 立即检查一次更新（只提示，不自动下载 / 替换自己）。</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (_update == null) { StatusText = "更新服务未就绪。"; return; }
        if (IsCheckingUpdate) return;

        IsCheckingUpdate = true;
        StatusText = "正在检查更新……";
        try
        {
            var (hasUpdate, remote, message) = await _update.CheckAsync((UpdateUrl ?? "").Trim());
            StatusText = hasUpdate ? "🎉 " + message : message;
        }
        catch (Exception ex) { StatusText = "检查更新失败：" + ex.Message; }
        finally { IsCheckingUpdate = false; }
    }

    /// <summary>打开任务管理器的「启动」页，让用户能自己去核对 / 禁用。</summary>
    [RelayCommand]
    private void OpenStartupSettings()
    {
        if (_startup == null) return;
        var (_, msg) = _startup.OpenStartupSettings();
        StatusText = msg;
    }

    /// <summary>🔄 重新读一次注册表状态（怀疑外部改过启动时用）。</summary>
    [RelayCommand]
    private void RefreshStartup() => RefreshStartupState();

    // ==================== 主题外观：模式 / 强调色（原 ThemeViewModel，点一下立即生效） ====================

    private void BuildAccents()
    {
        foreach (var p in AccentPalette.All)
        {
            Accents.Add(new AccentOption
            {
                Id = p.Id,
                Name = p.Name,
                Swatch = new SolidColorBrush(Color.Parse(p.Color)),
            });
        }
    }

    private void Load()
    {
        ApplyMode(_theme?.Mode ?? ThemeMode.Dark);
        var current = AccentPalette.Find(_theme?.AccentId).Id;
        _syncing = true;
        SelectedAccent = Accents.FirstOrDefault(a => a.Id == current) ?? Accents.FirstOrDefault();
        _syncing = false;
        RefreshStatus();
    }

    partial void OnIsDarkModeChanged(bool value) { if (value) SetMode(ThemeMode.Dark); }
    partial void OnIsLightModeChanged(bool value) { if (value) SetMode(ThemeMode.Light); }
    partial void OnIsSystemModeChanged(bool value) { if (value) SetMode(ThemeMode.System); }

    private void SetMode(ThemeMode mode)
    {
        if (_syncing) return;
        _theme?.SetMode(mode);      // 立即落盘 + 广播，Shell 收到后重刷资源字典
        ApplyMode(mode);            // 三个布尔互斥由这里保证，不依赖 GroupName 的容器作用域
        RefreshStatus();
    }

    private void ApplyMode(ThemeMode mode)
    {
        _syncing = true;
        IsDarkMode = mode == ThemeMode.Dark;
        IsLightMode = mode == ThemeMode.Light;
        IsSystemMode = mode == ThemeMode.System;
        _syncing = false;
    }

    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        if (_syncing || value == null) return;
        _theme?.SetAccent(value.Id);
        RefreshStatus();
    }

    /// <summary>↺ 恢复默认：深色 + 微信绿（同样立即生效）。</summary>
    [RelayCommand]
    private void ResetTheme()
    {
        _theme?.SetMode(ThemeMode.Dark);                  // 工程约定：默认深色
        _theme?.SetAccent(AccentPalette.Default.Id);       // 默认微信绿

        ApplyMode(ThemeMode.Dark);
        _syncing = true;
        SelectedAccent = Accents.FirstOrDefault(a => a.Id == AccentPalette.Default.Id);
        _syncing = false;
        RefreshStatus("已恢复默认");
    }

    private void RefreshStatus(string? prefix = null)
    {
        var mode = IsDarkMode ? "深色" : IsLightMode ? "浅色" : "跟随系统";
        var accentName = SelectedAccent?.Name ?? AccentPalette.Find(_theme?.AccentId).Name;
        StatusText = $"{prefix ?? "当前"}：{mode} · 强调色 {accentName} —— 已立即生效并写入 theme.json";
    }

    // ==================== 关于：预留动作（原 AboutViewModel） ====================

    [RelayCommand]
    private void OpenRepo()
    {
        // 预留：打开仓库地址
    }
}

/// <summary>一张强调色色卡。<see cref="Swatch"/> 直接绑到 XAML 的小圆点背景。</summary>
public class AccentOption
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IBrush Swatch { get; init; } = Brushes.Transparent;
}

/// <summary>关于页「技术栈」清单的一行（NT-051 由 AboutViewModel 并入）。</summary>
public class TechStackItem
{
    public string Name { get; set; } = "";
    public string Ver { get; set; } = "";
}
