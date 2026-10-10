using System;
using System.IO;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// 主题状态（用户选定模式 + 强调色）的持有者与持久化实现。
/// <para>
/// ⚠️ 本类在 <c>Core</c>，**不引用 Avalonia**：所以它只管状态与 <c>theme.json</c>，
/// 真正的「把颜色刷上窗口」由 Shell 的 <c>App.ApplyTheme()</c> 订阅 <see cref="ThemeChanged"/> 完成。
/// 「跟随系统」所需的操作系统状态也由 Shell 读 <c>Application.PlatformSettings</c> 后写进
/// <see cref="SystemDark"/>（见 <c>App.WatchSystemTheme</c>）。
/// </para>
/// </summary>
public class ThemeService : IThemeService
{
    private readonly ConfigStoreService _cfg;

    private ThemeMode _mode = ThemeMode.Dark;   // 默认深色（读不到配置 / 配置损坏时也回退到这里）
    private bool _systemDark = true;            // 跟随系统时用；默认按"系统是深色"起步，Shell 启动时会立刻覆盖
    private string _accentId = AccentPalette.Default.Id;

    public event Action? ThemeChanged;

    public ThemeMode Mode => _mode;
    public string AccentId => _accentId;
    public bool SystemDark
    {
        get => _systemDark;
        set
        {
            if (_systemDark == value) return;
            _systemDark = value;
            // 只有「跟随系统」模式才需要因为系统变化重刷；显式模式下系统怎么变都不影响外观。
            if (_mode == ThemeMode.System) ThemeChanged?.Invoke();
        }
    }

    /// <summary>当前实际生效的深色判定：显式模式直接给答案，System 模式交给系统状态。</summary>
    public bool IsDark => _mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => _systemDark,
    };

    public string ConfigDir => _cfg.ConfigDir;

    public ThemeService(ConfigStoreService cfg)
    {
        _cfg = cfg;
        Load();
    }

    private string ThemeFile => Path.Combine(ConfigDir, "theme.json");

    // ==================== 持久化 ====================

    /// <summary>
    /// 读取 <c>theme.json</c>。两种格式都认：
    /// - 现格式：<c>{ "Mode": "System", "AccentId": "blue", "IsDark": true }</c>；
    /// - **旧格式**（v1.18 之前只有 <c>{ "IsDark": true }</c>）→ 映射成 Dark / Light，老用户的偏好不丢。
    /// 任何异常（文件不存在 / JSON 坏了 / 值非法）都静默回退默认值，绝不让主题读取把启动搞崩。
    /// </summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(ThemeFile)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(ThemeFile));
            var root = doc.RootElement;

            if (root.TryGetProperty("Mode", out var modeEl))
            {
                if (modeEl.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ThemeMode>(modeEl.GetString(), ignoreCase: true, out var parsed))
                {
                    _mode = parsed;
                }
                else if (modeEl.ValueKind == JsonValueKind.Number && modeEl.TryGetInt32(out var n)
                         && Enum.IsDefined(typeof(ThemeMode), n))
                {
                    _mode = (ThemeMode)n;   // 允许数字写法
                }
            }
            else if (root.TryGetProperty("IsDark", out var darkEl)
                     && (darkEl.ValueKind == JsonValueKind.True || darkEl.ValueKind == JsonValueKind.False))
            {
                _mode = darkEl.GetBoolean() ? ThemeMode.Dark : ThemeMode.Light;
            }

            if (root.TryGetProperty("AccentId", out var accentEl) && accentEl.ValueKind == JsonValueKind.String)
            {
                _accentId = AccentPalette.Find(accentEl.GetString()).Id;   // Find 自带回退，废弃 id 也安全
            }
        }
        catch
        {
            _mode = ThemeMode.Dark;
            _accentId = AccentPalette.Default.Id;
        }
    }

    /// <summary>写入 <c>theme.json</c>（缩进、可读、便于手工改）。</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = $$"""
            {
              "Mode": "{{_mode}}",
              "AccentId": "{{_accentId}}",
              "IsDark": {{(IsDark ? "true" : "false")}}
            }
            """;
            File.WriteAllText(ThemeFile, json);
        }
        catch { }
    }

    // ==================== 变更 ====================

    /// <summary>
    /// 切模式并立即落盘 + 广播。
    /// 注意：**不比较新旧观感**——Dark → System（系统恰好也是深色）时观感没变，但界面上"选中哪个单选"
    /// 必须让人觉得生效了，所以照样广播（<c>ApplyTheme</c> 是幂等的，重复刷一次无副作用）。
    /// </summary>
    public void SetMode(ThemeMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        Save();
        ThemeChanged?.Invoke();
    }

    public void SetDark(bool dark) => SetMode(dark ? ThemeMode.Dark : ThemeMode.Light);

    public void Toggle() => SetMode(IsDark ? ThemeMode.Light : ThemeMode.Dark);

    public void SetAccent(string accentId)
    {
        var id = AccentPalette.Find(accentId).Id;
        if (string.Equals(_accentId, id, StringComparison.Ordinal)) return;
        _accentId = id;
        Save();
        ThemeChanged?.Invoke();
    }
}
