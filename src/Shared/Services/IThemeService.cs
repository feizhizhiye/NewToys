namespace NewToys.Shared.Services;

/// <summary>
/// 主题模式。
/// - <see cref="Light"/> / <see cref="Dark"/>：用户显式指定，任何情况下都不随系统变。
/// - <see cref="System"/>：跟随操作系统（Windows 的「应用模式」/ macOS 的外观）。
/// </summary>
public enum ThemeMode
{
    Light = 0,
    Dark = 1,
    System = 2,
}

/// <summary>
/// 明暗主题 + 强调色。持久化到 <c>&lt;应用根&gt;\config\theme.json</c>。
///
/// ⚠️ 本接口位于 <c>Core</c>（**不引用 Avalonia**），所以它只保存状态、不碰界面：
/// - 「跟随系统」需要的操作系统状态由 **Shell 层**读出来后写进 <see cref="SystemDark"/>；
/// - 真正把颜色刷到窗口上的是 Shell 的 <c>App.ApplyTheme()</c>，它订阅 <see cref="ThemeChanged"/>。
///
/// 因此约定：**任何导致外观变化的写入都要触发 <see cref="ThemeChanged"/>**（哪怕新旧观感恰好一样）。
/// </summary>
public interface IThemeService
{
    /// <summary>用户选定的模式（Light / Dark / System）。</summary>
    ThemeMode Mode { get; }

    /// <summary>当前**实际生效**的是不是深色。System 模式下由 <see cref="SystemDark"/> 决定。</summary>
    bool IsDark { get; }

    /// <summary>操作系统的深色状态。由 Shell 从 <c>Application.PlatformSettings</c> 读取后写入；Core 不去探测。</summary>
    bool SystemDark { get; set; }

    /// <summary>强调色预设 id（见 <see cref="AccentPalette"/>）。</summary>
    string AccentId { get; }

    /// <summary>设置模式并立即持久化（触发的刷新通过 <see cref="ThemeChanged"/> 广播）。</summary>
    void SetMode(ThemeMode mode);

    /// <summary>等价于 <c>SetMode(Dark / Light)</c>。</summary>
    void SetDark(bool dark);

    /// <summary>在深色 / 浅色之间翻转（跟随系统时，翻转到当前观感的相反面）。</summary>
    void Toggle();

    /// <summary>设置强调色预设并立即持久化。</summary>
    void SetAccent(string accentId);

    /// <summary>外观需要重刷（Shell 订阅它来重建资源字典）。</summary>
    event Action? ThemeChanged;

    /// <summary>用户配置目录（theme.json 所在目录）。</summary>
    string ConfigDir { get; }
}
