using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Reading.Services;
using NewToys.Shared.Services;

namespace NewToys.Reading.ViewModels;

/// <summary>配色下拉项。</summary>
public sealed record ReaderThemeOption(ReaderTheme Value, string Label);

/// <summary>
/// 「网络阅读 → 阅读设置」页（<c>settings-reading</c>）：抓取参数 + 阅读器参数，两节落在同一份
/// <c>config\reading.json</c>；改即落盘（⛔ 无保存按钮）。
/// <para>阅读器那组在阅读页顶栏也能改，写到同一处。</para>
/// </summary>
public partial class SettingsReadingViewModel : ObservableObject, IStatusProvider
{
    private readonly ReadingSettingsService _settings = null!;
    private readonly ReaderStyleService _styles = null!;
    private readonly ContentCacheStore? _cache;

    /// <summary>灌值时抑制「改属性 → 回写」。</summary>
    private bool _syncing;

    public SettingsReadingViewModel() { }

    public SettingsReadingViewModel(ReadingSettingsService settings, ReaderStyleService styles,
        ContentCacheStore? cache = null)
    {
        _cache = cache;
        _settings = settings;
        _styles = styles;

        _syncing = true;
        Concurrency = settings.Concurrency;
        TimeoutSeconds = settings.TimeoutSeconds;
        UserAgent = settings.UserAgent;
        CacheEnabled = settings.CacheEnabled;
        PrefetchCount = settings.PrefetchCount;
        FontSize = settings.FontSize;
        LineSpacing = settings.LineSpacing;
        EnsureFontChoice(settings.FontFamily);
        FontFamily = settings.FontFamily;
        ContentWidth = settings.ContentWidth;
        ReaderPadding = settings.ReaderPadding;
        Theme = settings.Theme;
        Indent = settings.Indent;
        ParagraphSpacing = settings.ParagraphSpacing;
        CustomBackground = settings.CustomBackground;
        CustomForeground = settings.CustomForeground;
        RememberProgress = settings.RememberProgress;
        ReplaceRules = settings.ReplaceRules;
        _syncing = false;

        StatusText = $"设置就绪 · 写入 {settings.FilePath}";
    }

    // ==================== 抓取 ====================

    [ObservableProperty] private int _concurrency = ReadingSettingsService.DefaultConcurrency;

    partial void OnConcurrencyChanged(int value) => Save(_settings?.SetConcurrency(value));

    [ObservableProperty] private int _timeoutSeconds = ReadingSettingsService.DefaultTimeout;

    partial void OnTimeoutSecondsChanged(int value) => Save(_settings?.SetTimeoutSeconds(value));

    [ObservableProperty] private string _userAgent = ReadingSettingsService.DefaultUserAgent;

    partial void OnUserAgentChanged(string value) => Save(_settings?.SetUserAgent(value));

    [ObservableProperty] private bool _cacheEnabled;

    partial void OnCacheEnabledChanged(bool value) => Save(_settings?.SetCacheEnabled(value));

    [ObservableProperty] private int _prefetchCount = ReadingSettingsService.DefaultPrefetchCount;

    partial void OnPrefetchCountChanged(int value) => Save(_settings?.SetPrefetchCount(value));

    [ObservableProperty] private string _replaceRules = "";

    partial void OnReplaceRulesChanged(string value) => Save(_settings?.SetReplaceRules(value));

    /// <summary>并发说明。</summary>
    public string ConcurrencyHint =>
        $"多源搜索 / 批量抓取时的并行请求数，范围 {ReadingSettingsService.MinConcurrency}~{ReadingSettingsService.MaxConcurrencyLimit}，默认 {ReadingSettingsService.DefaultConcurrency}。调大更快，但被封 IP 的风险也更大。";

    /// <summary>超时说明。</summary>
    public string TimeoutHint =>
        $"单次请求最长等待，范围 {ReadingSettingsService.MinTimeout}~{ReadingSettingsService.MaxTimeout} 秒（默认 {ReadingSettingsService.DefaultTimeout}）。"
        + "⚠ 改完要重开一次阅读页才生效（请求连接在页面创建时定超时）。";

    /// <summary>UA 说明。</summary>
    public string UserAgentHint =>
        "多数站点按 UA 返回桌面版页面。留空 = 用内置的 Chrome 桌面 UA。";

    /// <summary>缓存说明。</summary>
    public string CacheHint =>
        "开：每章读完顺手存一份，下次同一章直接命中；关（默认）：读的时候不存。"
        + "无论开关，已经存过的章节（含预下载进来的）都会直接用缓存。";

    /// <summary>预下载说明。</summary>
    public string PrefetchHint =>
        $"读到第 N 章时，后台顺手把后面这么多章抓进缓存（{ReadingSettingsService.MinPrefetchCount}~{ReadingSettingsService.MaxPrefetchCount}，默认 {ReadingSettingsService.DefaultPrefetchCount}，0 = 不预下载）。"
        + "⚠ 只在「缓存正文」开启时生效。";

    /// <summary>替换规则说明。</summary>
    public string ReplaceRulesHint =>
        "每行一条：正则##替换文本；只写正则 = 直接删掉命中的内容；行首 # 当注释。执行顺序在书源自带的 replaceRule 之后，"
        + "所以清广告 / 「本站域名」这类共性问题写在这、源特有的规则留在源里。单条正则写错只跳过那一条。"
        + "⚠ 已缓存的章节仍是旧正文，改完规则可以点下面的「清空正文缓存」。";

    // ==================== 正文缓存 ====================

    [RelayCommand]
    private void ClearCache()
    {
        var removed = _cache?.Clear() ?? 0;
        StatusText = removed > 0
            ? $"已清空正文缓存（{removed} 章）"
            : "正文缓存本来就是空的";
    }

    // ==================== 阅读器 ====================

    [ObservableProperty] private int _fontSize = ReadingSettingsService.DefaultFontSize;

    partial void OnFontSizeChanged(int value) => Save(_settings?.SetFontSize(value));

    [ObservableProperty] private double _lineSpacing = ReadingSettingsService.DefaultLineSpacing;

    partial void OnLineSpacingChanged(double value) => Save(_settings?.SetLineSpacing(value));

    [ObservableProperty] private string _fontFamily = ReadingSettingsService.DefaultFontFamily;

    partial void OnFontFamilyChanged(string value) => Save(_settings?.SetFontFamily(value));

    [ObservableProperty] private int _contentWidth = ReadingSettingsService.DefaultContentWidth;

    partial void OnContentWidthChanged(int value) => Save(_settings?.SetContentWidth(value));

    [ObservableProperty] private int _readerPadding = 28;

    partial void OnReaderPaddingChanged(int value) => Save(_settings?.SetReaderPadding(value));

    [ObservableProperty] private ReaderTheme _theme = ReaderTheme.Light;

    partial void OnThemeChanged(ReaderTheme value) => Save(_settings?.SetTheme(value));

    [ObservableProperty] private int _indent = ReadingSettingsService.DefaultIndent;

    partial void OnIndentChanged(int value) => Save(_settings?.SetIndent(value));

    [ObservableProperty] private int _paragraphSpacing = ReadingSettingsService.DefaultParagraphSpacing;

    partial void OnParagraphSpacingChanged(int value) => Save(_settings?.SetParagraphSpacing(value));

    [ObservableProperty] private string _customBackground = "";

    partial void OnCustomBackgroundChanged(string value) => Save(_settings?.SetCustomBackground(value));

    [ObservableProperty] private string _customForeground = "";

    partial void OnCustomForegroundChanged(string value) => Save(_settings?.SetCustomForeground(value));

    [ObservableProperty] private bool _rememberProgress = true;

    partial void OnRememberProgressChanged(bool value) => Save(_settings?.SetRememberProgress(value));

    /// <summary>常用中文字体（下拉候选；也可以在下框手填别的字体名）。</summary>
    public ObservableCollection<string> FontChoices { get; } = new()
    {
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimSun",
        "SimHei",
        "KaiTi",
        "FangSong",
        "Source Han Serif SC",
        "Consolas",
    };

    /// <summary>候选里补上当前生效的字体，免得下拉框显示空白并把自定义字体覆盖掉。</summary>
    private void EnsureFontChoice(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        if (!FontChoices.Contains(family)) FontChoices.Insert(0, family);
    }

    /// <summary>配色候选。</summary>
    public IReadOnlyList<ReaderThemeOption> ThemeChoices { get; } = new[]
    {
        new ReaderThemeOption(ReaderTheme.Light, "白底"),
        new ReaderThemeOption(ReaderTheme.Sepia, "米黄"),
        new ReaderThemeOption(ReaderTheme.Dark, "暗色"),
    };

    // ==================== 样式方案（与阅读页「🎨 样式」卡同一套） ====================

    /// <summary>内置样式（点一下整套套用）。</summary>
    public IReadOnlyList<Models.ReaderStyle> StylePresets =>
        _styles?.Presets ?? (IReadOnlyList<Models.ReaderStyle>)Array.Empty<Models.ReaderStyle>();

    /// <summary>样式卡说明。</summary>
    public string StyleHint =>
        "样式文件就是一份 JSON：字体 / 字号 / 行距 / 正文宽度 / 留白 / 配色 / 首行缩进 / 段间距。"
        + "缺的项保持原值 —— 手写两三项也能用；导入后立即生效（与阅读页同一份设置）。";

    [RelayCommand]
    private void ApplyPreset(Models.ReaderStyle? style)
    {
        if (style is null) return;
        if (_styles is null) { StatusText = "⚠ 样式服务未就绪。"; return; }

        Save(_styles.Apply(style));
        ReloadTypography();
    }

    [RelayCommand]
    private async Task ImportStyle()
    {
        if (_styles is null) { StatusText = "⚠ 样式服务未就绪。"; return; }

        var files = await DialogHooks.PickFilesAsync("选择阅读样式 JSON", null, false, "*.json").ConfigureAwait(true);
        if (files.Count == 0) { StatusText = "已取消导入样式"; return; }

        Save(_styles.Import(files[0]));
        ReloadTypography();
    }

    [RelayCommand]
    private async Task ExportStyle()
    {
        if (_styles is null) { StatusText = "⚠ 样式服务未就绪。"; return; }

        var path = await DialogHooks.SaveFileAsync("导出阅读样式", null, "reader-style.json", "*.json").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) { StatusText = "已取消导出样式"; return; }

        Save(_styles.Export(path!));
    }

    /// <summary>排版说明。</summary>
    public string TypographyHint =>
        $"字号 {ReadingSettingsService.MinFontSize}~{ReadingSettingsService.MaxFontSize}px、行距倍数 {ReadingSettingsService.MinLineSpacing}~{ReadingSettingsService.MaxLineSpacing}、"
        + $"正文宽度 {ReadingSettingsService.MinContentWidth}~{ReadingSettingsService.MaxContentWidth}px（窗口很宽时限制行长，读着不累眼）。";

    /// <summary>套用样式 / 导入后回读一次排版参数（服务已钳位 ⇒ 界面与磁盘始终一致）。</summary>
    private void ReloadTypography()
    {
        if (_settings is null) return;

        _syncing = true;
        FontSize = _settings.FontSize;
        LineSpacing = _settings.LineSpacing;
        EnsureFontChoice(_settings.FontFamily);
        FontFamily = _settings.FontFamily;
        ContentWidth = _settings.ContentWidth;
        ReaderPadding = _settings.ReaderPadding;
        Theme = _settings.Theme;
        Indent = _settings.Indent;
        ParagraphSpacing = _settings.ParagraphSpacing;
        CustomBackground = _settings.CustomBackground;
        CustomForeground = _settings.CustomForeground;
        PrefetchCount = _settings.PrefetchCount;
        ReplaceRules = _settings.ReplaceRules;
        _syncing = false;
    }

    // ==================== 只读展示 ====================

    /// <summary>设置文件全路径。</summary>
    public string ConfigFilePath => _settings?.FilePath ?? "config\\reading.json";

    // ==================== 状态栏 ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;

    private void Save(string? status)
    {
        if (_syncing || status is null) return;
        StatusText = status;
    }
}
