using System;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // 默认原生 Fluent 皮肤：它的明暗层次比自制色阶更细（同一档背景能分出标题条 / 面板 / 悬浮三层）
    private ThemeSkin _skin = ThemeSkin.Fluent;

    public event Action? ThemeChanged;

    public ThemeMode Mode => _mode;
    public string AccentId => _accentId;
    public ThemeSkin Skin => _skin;
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

    public ThemeService(ConfigStoreService cfg, ShellConfigStore shell)
    {
        _cfg = cfg;
        _shell = shell;
        Load();
    }

    private readonly ShellConfigStore _shell;

    // ==================== 持久化 ====================

    /// <summary>
    /// 读 <c>shell.json</c> 的 <c>theme</c> 节（前是独立的 <c>theme.json</c>，迁移由
    /// <see cref="ConfigStoreService"/> 一次性完成）。两种格式都认：
    /// - 现格式：<c>{ "Mode": "System", "AccentId": "blue", "IsDark": true }</c>；
    /// - **旧格式**（v1.18 之前只有 <c>{ "IsDark": true }</c>）→ 映射成 Dark / Light，老用户的偏好不丢。
    /// 任何异常（节不存在 / 值非法）都静默回退默认值，绝不让主题读取把启动搞崩。
    /// </summary>
    public void Load()
    {
        try
        {
            if (_shell.Get("theme") is not JsonObject root) return;

            if (root.TryGetPropertyValue("Mode", out var modeNode) && modeNode is not null)
            {
                if (modeNode.GetValueKind() == JsonValueKind.String)
                {
                    if (Enum.TryParse<ThemeMode>(modeNode.GetValue<string>(), ignoreCase: true, out var parsed))
                        _mode = parsed;
                }
                else if (modeNode.GetValueKind() == JsonValueKind.Number
                         && int.TryParse(modeNode.ToJsonString(), out var n)
                         && Enum.IsDefined(typeof(ThemeMode), n))
                {
                    _mode = (ThemeMode)n;   // 允许数字写法
                }
            }
            else if (root.TryGetPropertyValue("IsDark", out var darkNode) && darkNode is not null
                     && darkNode.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                _mode = darkNode.GetValueKind() == JsonValueKind.True ? ThemeMode.Dark : ThemeMode.Light;
            }

            if (root.TryGetPropertyValue("AccentId", out var accentNode) && accentNode is not null
                && accentNode.GetValueKind() == JsonValueKind.String)
            {
                _accentId = AccentPalette.Find(accentNode.GetValue<string>()).Id;   // Find 自带回退，废弃 id 也安全
            }

            if (root.TryGetPropertyValue("Skin", out var skinNode) && skinNode is not null
                && skinNode.GetValueKind() == JsonValueKind.String
                && Enum.TryParse<ThemeSkin>(skinNode.GetValue<string>(), ignoreCase: true, out var skin))
            {
                _skin = skin;
            }
        }
        catch
        {
            _mode = ThemeMode.Dark;
            _accentId = AccentPalette.Default.Id;
            _skin = ThemeSkin.Fluent;
        }
    }

    /// <summary>写入 <c>shell.json</c> 的 <c>theme</c> 节（缩进、可读、便于手工改）。</summary>
    public void Save()
    {
        try
        {
            _shell.Set("theme", new JsonObject
            {
                ["Mode"] = _mode.ToString(),
                ["AccentId"] = _accentId,
                ["Skin"] = _skin.ToString(),
                ["IsDark"] = IsDark,
            });
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

    public void SetSkin(ThemeSkin skin)
    {
        if (_skin == skin) return;
        _skin = skin;
        Save();
        ThemeChanged?.Invoke();
    }
}
