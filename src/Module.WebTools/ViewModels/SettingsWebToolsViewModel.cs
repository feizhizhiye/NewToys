using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 「网页工具」模块设置页（v3.64 / NT-061，需求 §4.9）：View/VM 归 <c>Module.WebTools</c>，
/// 由 <c>WebToolsModule.Initialize</c> 自注册进**本模块分组 <c>web</c>**（Key <c>settings-web-tools</c>，Order 100）。
/// <para>
/// 内容 = 网页抓取的**默认 UA + 单次抓取超时**（原来硬编码在 <c>ScrapeService</c>，现在落
/// <c>config\web-tools.json</c> 可手改、可备份）。生效方式（规范 §4.9）：失焦即落盘，⛔ 无保存按钮。
/// </para>
/// </summary>
public partial class SettingsWebToolsViewModel : ObservableObject, IStatusProvider
{
    private readonly IWebToolsSettingsService _settings = null!;

    /// <summary>灌值时抑制「改属性 → 回写」。</summary>
    private bool _syncing;

    public SettingsWebToolsViewModel() { }

    public SettingsWebToolsViewModel(IWebToolsSettingsService settings)
    {
        _settings = settings;

        _syncing = true;
        DefaultUserAgent = settings.DefaultUserAgent;
        ScrapeTimeoutSeconds = settings.ScrapeTimeoutSeconds;
        _syncing = false;

        StatusText = $"本模块设置就绪 · 写入 {settings.FilePath}";
    }

    // ==================== 抓取默认项（即时生效） ====================

    [ObservableProperty] private string _defaultUserAgent = "";
    [ObservableProperty] private int _scrapeTimeoutSeconds = WebToolsSettingsService.DefaultTimeout;

    partial void OnDefaultUserAgentChanged(string value)
    {
        if (_syncing) return;
        StatusText = _settings.SetDefaultUserAgent(value);
        OnPropertyChanged(nameof(EffectiveUserAgent));
    }

    partial void OnScrapeTimeoutSecondsChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetScrapeTimeoutSeconds(value);
    }

    // ==================== 只读展示 ====================

    /// <summary>生效的 UA（留空时回落内置那一串）。</summary>
    public string EffectiveUserAgent => _settings?.EffectiveUserAgent ?? "";

    /// <summary>超时取值范围说明（与服务里的钳位口径一致）。</summary>
    public string TimeoutHint =>
        $"范围 {WebToolsSettingsService.MinTimeout}~{WebToolsSettingsService.MaxTimeout} 秒，" +
        $"默认 {WebToolsSettingsService.DefaultTimeout}。";

    /// <summary>本模块设置文件全路径。</summary>
    public string ConfigFilePath => _settings?.FilePath ?? "";

    /// <summary>默认 UA 恢复内置（清空用户值）。</summary>
    [RelayCommand]
    private void ResetUserAgent() => DefaultUserAgent = "";

    // ==================== 状态栏（IStatusProvider） ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
