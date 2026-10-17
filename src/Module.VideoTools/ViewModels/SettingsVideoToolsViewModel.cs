using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Shared.Services;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 「视频工具」模块设置页（v3.64 / NT-061，需求 §4.9）：View/VM 归 <c>Module.VideoTools</c>，
/// 由 <c>VideoToolsModule.Initialize</c> 自注册进**本模块分组 <c>video</c>**（Key <c>settings-video-tools</c>，Order 100）。
/// <para>
/// 本模块**独有的**可配项 = 并发数（ffprobe 媒体信息 / 批处理），落 <c>config\video-tools.json</c>；
/// <c>ffmpeg</c> / 外部播放器 / 导入导出目录是**全局项**（「设置 → 路径与配置」）—— 媒体资产模块也在用同一份，
/// 这里**只展示、⛔ 不另存一份**（§4.9「复用全局项」）。
/// </para>
/// </summary>
public partial class SettingsVideoToolsViewModel : ObservableObject, IStatusProvider
{
    private readonly IVideoToolsSettingsService _settings = null!;
    private readonly IAppConfigService? _app;

    /// <summary>灌值时抑制「改属性 → 回写」。</summary>
    private bool _syncing;

    public SettingsVideoToolsViewModel() { }

    public SettingsVideoToolsViewModel(IVideoToolsSettingsService settings, IAppConfigService? app = null)
    {
        _settings = settings;
        _app = app;

        _syncing = true;
        MaxConcurrency = settings.MaxConcurrency;
        _syncing = false;

        StatusText = $"本模块设置就绪 · 写入 {settings.FilePath}";
    }

    // ==================== 并发数（本模块唯一可配项，即时生效） ====================

    [ObservableProperty] private int _maxConcurrency = 2;

    partial void OnMaxConcurrencyChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetMaxConcurrency(value);
    }

    /// <summary>取值范围说明（跟服务里的钳位口径一致）。</summary>
    public string ConcurrencyHint =>
        $"建议 1~4：视频列表「补媒体信息」一次并行 ffprobe，批量处理按它并行跑任务。" +
        $"范围 {VideoToolsSettingsService.MinConcurrency}~{VideoToolsSettingsService.MaxConcurrencyLimit}，" +
        $"默认 {_settings?.DefaultMaxConcurrency ?? VideoToolsSettingsService.DefaultMaxConcurrencyValue}。";

    // ==================== 全局项（只读展示；⛔ 本页不写） ====================

    /// <summary>ffmpeg 路径（全局项，见「设置 → 路径与配置」）。</summary>
    public string FfmpegPathDisplay => Or(_app?.FfmpegPath, "未配置（走 PATH 里的 ffmpeg）");

    /// <summary>外部播放器（全局项）。</summary>
    public string PlayerPathDisplay => Or(_app?.PlayerPath, "未配置（用系统默认关联程序）");

    /// <summary>导入导出默认目录（全局项）。</summary>
    public string ImportExportDirDisplay => Or(_app?.VideoDbFolder, "未配置（用系统默认目录）");

    /// <summary>本模块设置文件全路径。</summary>
    public string ConfigFilePath => _settings?.FilePath ?? "";

    private static string Or(string? value, string fallback)
    {
        var v = (value ?? "").Trim();
        return v.Length > 0 ? v : fallback;
    }

    // ==================== 状态栏（IStatusProvider） ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
