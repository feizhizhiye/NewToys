using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Shared.Services;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>「视频工具」模块设置页（，需求 §4.9）：View/VM 归 Module.VideoTools， 由 VideoToolsModule.Initialize 自注册进本模块分组 video（Key settings-video-tools，Order 100）。（WHY: VT-028）</summary>
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
        SelectedAdviceMode = settings.AdviceMode;
        AdviceMinSaving = settings.AdviceMinSavingPercent;
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

    // ==================== 转码前收益判定（省体积闸门） ====================

    /// <summary>策略下拉项。</summary>
    public IReadOnlyList<AdviceModeOption> AdviceModeChoices => AdviceModes.All;

    /// <summary>当前策略（<c>suggest</c> / <c>strict</c> / <c>off</c>）。</summary>
    [ObservableProperty] private string _selectedAdviceMode = AdviceModes.Suggest;

    partial void OnSelectedAdviceModeChanged(string value)
    {
        if (_syncing) return;
        StatusText = _settings.SetAdviceMode(value);
    }

    /// <summary>省幅门槛（%）。</summary>
    [ObservableProperty] private int _adviceMinSaving = TranscodeAdvisor.DefaultMinSavingPercent;

    partial void OnAdviceMinSavingChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetAdviceMinSavingPercent(value);
    }

    /// <summary>判定机制说明。</summary>
    public string AdviceHint =>
        "转码前用源属性（码率 / 分辨率 / 帧率 / 编码族）算 bpp，对照目标编码族的「视觉透明线」，"
        + "判断这次转码还能不能真的省体积 —— 源码率已经压到线下时，再转只会变大。"
        + "严格档会直接跳过这类任务并写明原因；目标体积模式不看内容，直接按体积比给结论。";

    // ==================== 全局项（只读展示；⛔ 本页不写） ====================

    /// <summary>ffmpeg 路径（全局项，见「设置 → 路径与配置」）。</summary>
    public string FfmpegPathDisplay => Or(_app?.FfmpegPath, "未配置（走 PATH 里的 ffmpeg）");

    /// <summary>外部播放器（全局项）。</summary>
    public string PlayerPathDisplay => Or(_app?.PlayerPath, "未配置（用系统默认关联程序）");

    /// <summary>导入导出默认目录（本模块 <c>store\video</c>，未建出 ⇒ <c>store\</c>）。</summary>
    public string ImportExportDirDisplay
    {
        get
        {
            var root = _app?.StoreDirectory;
            if (string.IsNullOrEmpty(root)) return "当前配置档的 store 目录";
            var dir = Path.Combine(root, VideoDb.Key);
            return Directory.Exists(dir) ? dir : root;
        }
    }

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
