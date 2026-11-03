using System;
using System.Globalization;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Reading.Services;

/// <summary>阅读区配色档（<c>reader-theme</c>）。</summary>
public enum ReaderTheme
{
    /// <summary>白底黑字。</summary>
    Light = 0,

    /// <summary>米黄护眼。</summary>
    Sepia = 1,

    /// <summary>暗底浅字。</summary>
    Dark = 2,
}

/// <summary>
/// 网络阅读模块设置（<c>config\reading.json</c>）的唯一读写点。
/// <para>两组设置共用一份文件：<b>抓取</b>（并发 / 超时 / UA / 缓存）与<b>阅读器</b>（字号 / 行距 / 字体 / 页宽 / 配色 / 翻页）。</para>
/// <para>阅读器那组在阅读页的浮层里也能改（写到同一处），⛔ 不另存一份。</para>
/// </summary>
public sealed class ReadingSettingsService
{
    /// <summary>配置分区名（<c>config\reading.json</c>）。</summary>
    public const string Module = "reading";

    /// <summary>默认 UA：桌面浏览器标识，多数站点按它返回桌面版页面。</summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    /// <summary>默认正文字体（系统自带、中文字形完整）。</summary>
    public const string DefaultFontFamily = "Microsoft YaHei UI";

    public const int MinConcurrency = 1;
    public const int MaxConcurrencyLimit = 16;
    public const int DefaultConcurrency = 3;

    public const int MinTimeout = 5;
    public const int MaxTimeout = 120;
    public const int DefaultTimeout = 15;

    public const int MinPrefetchCount = 0;
    public const int MaxPrefetchCount = 50;
    public const int DefaultPrefetchCount = 5;

    public const int MinFontSize = 12;
    public const int MaxFontSize = 36;
    public const int DefaultFontSize = 17;

    public const double MinLineSpacing = 1.2;
    public const double MaxLineSpacing = 2.6;
    public const double DefaultLineSpacing = 1.8;

    public const int MinContentWidth = 480;
    public const int MaxContentWidth = 1400;
    public const int DefaultContentWidth = 820;

    public const int MinIndent = 0;
    public const int MaxIndent = 4;
    public const int DefaultIndent = 2;

    public const int MinParagraphSpacing = 0;
    public const int MaxParagraphSpacing = 3;
    public const int DefaultParagraphSpacing = 1;

    public const int MinTtsRate = -10;
    public const int MaxTtsRate = 10;
    public const int DefaultTtsRate = 0;
    public const int DefaultTtsVolume = 100;

    private readonly IOptionsStore? _opts;
    private readonly ConfigStoreService? _cfg;

    public ReadingSettingsService(IOptionsStore? opts = null, ConfigStoreService? cfg = null)
    {
        _opts = opts;
        _cfg = cfg;
    }

    /// <summary>设置文件全路径（设置页展示用）。</summary>
    public string FilePath => _cfg is null
        ? "config\\reading.json"
        : Path.Combine(_cfg.ConfigDir, Module + ".json");

    /// <summary>任一设置有变（阅读页据此即时重排）。</summary>
    public event Action? Changed;

    // ==================== 抓取 ====================

    /// <summary>并发抓取上限（多源搜索 / 批量抓取用）。</summary>
    public int Concurrency => ReadInt("concurrency", DefaultConcurrency, MinConcurrency, MaxConcurrencyLimit);

    /// <summary>单次请求超时（秒）。⚠️ 改了要重启页面才生效（<c>HttpClient</c> 在构造时定超时）。</summary>
    public int TimeoutSeconds => ReadInt("timeout-seconds", DefaultTimeout, MinTimeout, MaxTimeout);

    /// <summary>请求 UA。</summary>
    public string UserAgent
    {
        get
        {
            var value = _opts?.Get(Module, "user-agent");
            return string.IsNullOrWhiteSpace(value) ? DefaultUserAgent : value!;
        }
    }

    /// <summary>是否缓存正文（默认关：正文缓存会让「重看同一章」拿不到站点更新）。</summary>
    public bool CacheEnabled => ReadBool("cache-enabled", false);

    /// <summary>阅读时向后预下载的章数（0 = 不预下载）。</summary>
    public int PrefetchCount => ReadInt("reader-prefetch", DefaultPrefetchCount, MinPrefetchCount, MaxPrefetchCount);

    /// <summary>全局替换净化规则（多行，每行 <c>正则##替换</c>）；空 = 不净化。</summary>
    public string ReplaceRules
    {
        get
        {
            var value = _opts?.Get(Module, "reader-replace-rules");
            return value ?? "";
        }
    }

    // ==================== 阅读器 ====================

    /// <summary>正文字号（px）。</summary>
    public int FontSize => ReadInt("reader-font-size", DefaultFontSize, MinFontSize, MaxFontSize);

    /// <summary>行距倍数（相对字号）。</summary>
    public double LineSpacing => ReadDouble("reader-line-spacing", DefaultLineSpacing, MinLineSpacing, MaxLineSpacing);

    /// <summary>正文字体名。</summary>
    public string FontFamily
    {
        get
        {
            var value = _opts?.Get(Module, "reader-font-family");
            return string.IsNullOrWhiteSpace(value) ? DefaultFontFamily : value!;
        }
    }

    /// <summary>正文最大宽度（px）：桌面窗口很宽时限制行长，读起来才不费眼。</summary>
    public int ContentWidth => ReadInt("reader-content-width", DefaultContentWidth, MinContentWidth, MaxContentWidth);

    /// <summary>配色档。</summary>
    public ReaderTheme Theme => ParseTheme(_opts?.Get(Module, "reader-theme"));

    /// <summary>左右留白（px）。</summary>
    public int ReaderPadding => ReadInt("reader-padding", 28, 0, 120);

    /// <summary>首行缩进（全角空格个数）。</summary>
    public int Indent => ReadInt("reader-indent", DefaultIndent, MinIndent, MaxIndent);

    /// <summary>段间距（段与段之间的空行数）。</summary>
    public int ParagraphSpacing => ReadInt("reader-paragraph-spacing", DefaultParagraphSpacing, MinParagraphSpacing, MaxParagraphSpacing);

    /// <summary>自定义背景色（<c>#RRGGBB</c>）；空 = 跟配色档走。</summary>
    public string CustomBackground => ReadColor("reader-bg");

    /// <summary>自定义前景色；空 = 跟配色档走。</summary>
    public string CustomForeground => ReadColor("reader-fg");

    /// <summary>
    /// 阅读器壳（ReaderShell）配色档序号：0 跟随主题 / 1 米黄护眼 / 2 淡绿 / 3 夜间。
    /// <para>🔴 与 <see cref="Theme"/> 并存：壳的配色档自己一套（含淡绿），由壳的排版浮层读写。</para>
    /// </summary>
    public int ShellTheme => ReadInt("reader-shell-theme", 0, 0, 3);

    /// <summary>是否记住阅读进度。</summary>
    public bool RememberProgress => ReadBool("reader-remember-progress", true);

    // ==================== 听书 ====================

    /// <summary>朗读语速（SAPI 约定 −10~10，0 = 正常；负数更慢）。</summary>
    public int TtsRate => ReadInt("tts-rate", DefaultTtsRate, MinTtsRate, MaxTtsRate);

    /// <summary>朗读音量（0~100）。</summary>
    public int TtsVolume => ReadInt("tts-volume", DefaultTtsVolume, 0, 100);

    /// <summary>音色（系统音色的完整描述）；空 = 系统默认音色。</summary>
    public string TtsVoice => (_opts?.Get(Module, "tts-voice") ?? "").Trim();

    // ==================== 写入（设置页 / 阅读浮层共用） ====================

    public string SetConcurrency(int value)
        => Save("concurrency", Clamp(value, MinConcurrency, MaxConcurrencyLimit).ToString(CultureInfo.InvariantCulture),
            "并发数", MinConcurrency, MaxConcurrencyLimit);

    public string SetTimeoutSeconds(int value)
        => Save("timeout-seconds", Clamp(value, MinTimeout, MaxTimeout).ToString(CultureInfo.InvariantCulture),
            "超时", MinTimeout, MaxTimeout);

    public string SetUserAgent(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) text = DefaultUserAgent;
        return Save("user-agent", text, "User-Agent", 0, 0);
    }

    public string SetCacheEnabled(bool value) => Save("cache-enabled", value ? "true" : "false", "正文缓存", 0, 0);

    public string SetPrefetchCount(int value)
        => Save("reader-prefetch", Clamp(value, MinPrefetchCount, MaxPrefetchCount).ToString(CultureInfo.InvariantCulture),
            "预下载章数", MinPrefetchCount, MaxPrefetchCount);

    public string SetReplaceRules(string? value) => Save("reader-replace-rules", (value ?? "").Trim(), "替换规则", 0, 0);

    public string SetFontSize(int value)
        => Save("reader-font-size", Clamp(value, MinFontSize, MaxFontSize).ToString(CultureInfo.InvariantCulture),
            "字号", MinFontSize, MaxFontSize);

    public string SetLineSpacing(double value)
        => Save("reader-line-spacing",
            Math.Clamp(value, MinLineSpacing, MaxLineSpacing).ToString("0.0#", CultureInfo.InvariantCulture),
            "行距", 0, 0);

    public string SetFontFamily(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) text = DefaultFontFamily;
        return Save("reader-font-family", text, "字体", 0, 0);
    }

    public string SetContentWidth(int value)
        => Save("reader-content-width", Clamp(value, MinContentWidth, MaxContentWidth).ToString(CultureInfo.InvariantCulture),
            "正文宽度", MinContentWidth, MaxContentWidth);

    public string SetTheme(ReaderTheme value) => Save("reader-theme", ThemeKey(value), "配色", 0, 0);

    public string SetShellTheme(int value) => Save("reader-shell-theme", Clamp(value, 0, 3).ToString(CultureInfo.InvariantCulture), "阅读配色", 0, 0);

    public string SetReaderPadding(int value)
        => Save("reader-padding", Clamp(value, 0, 120).ToString(CultureInfo.InvariantCulture), "左右留白", 0, 120);

    public string SetIndent(int value)
        => Save("reader-indent", Clamp(value, MinIndent, MaxIndent).ToString(CultureInfo.InvariantCulture),
            "首行缩进", MinIndent, MaxIndent);

    public string SetParagraphSpacing(int value)
        => Save("reader-paragraph-spacing",
            Clamp(value, MinParagraphSpacing, MaxParagraphSpacing).ToString(CultureInfo.InvariantCulture),
            "段间距", MinParagraphSpacing, MaxParagraphSpacing);

    public string SetCustomBackground(string? value) => Save("reader-bg", NormalizeColor(value), "背景色", 0, 0);

    public string SetCustomForeground(string? value) => Save("reader-fg", NormalizeColor(value), "文字色", 0, 0);

    public string SetRememberProgress(bool value) => Save("reader-remember-progress", value ? "true" : "false", "记住进度", 0, 0);

    public string SetTtsRate(int value)
        => Save("tts-rate", Clamp(value, MinTtsRate, MaxTtsRate).ToString(CultureInfo.InvariantCulture),
            "朗读语速", MinTtsRate, MaxTtsRate);

    public string SetTtsVolume(int value)
        => Save("tts-volume", Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture), "朗读音量", 0, 100);

    public string SetTtsVoice(string? value) => Save("tts-voice", (value ?? "").Trim(), "朗读音色", 0, 0);

    /// <summary>配色档的存储键（与 <see cref="ParseTheme"/> 对称）。</summary>
    public static string ThemeKey(ReaderTheme theme) => theme switch
    {
        ReaderTheme.Sepia => "sepia",
        ReaderTheme.Dark => "dark",
        _ => "light",
    };

    /// <summary>配色档的中文名（下拉项用）。</summary>
    public static string ThemeText(ReaderTheme theme) => theme switch
    {
        ReaderTheme.Sepia => "米黄",
        ReaderTheme.Dark => "暗色",
        _ => "白底",
    };

    /// <summary>阅读区背景色（⛔ 不写死在 XAML：配色由设置决定）。</summary>
    public static string BackgroundOf(ReaderTheme theme) => theme switch
    {
        ReaderTheme.Sepia => "#F5EBD8",
        ReaderTheme.Dark => "#1E1E1E",
        _ => "#FFFFFF",
    };

    /// <summary>阅读区前景色。</summary>
    public static string ForegroundOf(ReaderTheme theme) => theme switch
    {
        ReaderTheme.Sepia => "#3B2F1F",
        ReaderTheme.Dark => "#C9C9C9",
        _ => "#1F1F1F",
    };

    /// <summary>最终背景色：自定义色优先，空则回落配色档。</summary>
    public string EffectiveBackground(ReaderTheme theme)
        => CustomBackground.Length > 0 ? CustomBackground : BackgroundOf(theme);

    /// <summary>最终前景色：自定义色优先，空则回落配色档。</summary>
    public string EffectiveForeground(ReaderTheme theme)
        => CustomForeground.Length > 0 ? CustomForeground : ForegroundOf(theme);

    // ==================== 内部 ====================

    /// <summary>自定义色只收 <c>#RRGGBB</c>；其它形态（含空）一律当「不自定义」处理。</summary>
    private static string NormalizeColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "";
        if (text[0] != '#') text = "#" + text;

        if (text.Length != 7) return "";
        foreach (var c in text.AsSpan(1))
            if (!Uri.IsHexDigit(c)) return "";

        return text.ToUpperInvariant();
    }

    private static ReaderTheme ParseTheme(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
    {
        "sepia" => ReaderTheme.Sepia,
        "dark" => ReaderTheme.Dark,
        _ => ReaderTheme.Light,
    };

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private string Save(string key, string value, string label, int min, int max)
    {
        if (_opts is null) return $"⚠ {label}保存失败：设置存储不可用 —— 未写盘，仍用上一个有效值。";

        try
        {
            _opts.Set(Module, key, value);
        }
        catch (Exception ex)
        {
            return $"⚠ {label}保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return min < max ? $"{label}已保存（{value}，范围 {min}~{max}）" : $"{label}已保存";
    }

    private int ReadInt(string key, int fallback, int min, int max)
    {
        var raw = _opts?.Get(Module, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private double ReadDouble(string key, double fallback, double min, double max)
    {
        var raw = _opts?.Get(Module, key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    /// <summary>读自定义色；存进去时已校验过，这里再兜一次（手改 json 写坏了 ⇒ 回落配色档）。</summary>
    private string ReadColor(string key) => NormalizeColor(_opts?.Get(Module, key));

    private bool ReadBool(string key, bool fallback)
    {
        var raw = _opts?.Get(Module, key);
        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
