using System;
using System.Globalization;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.VideoTools.Services;

/// <summary>IVideoToolsSettingsService 的实现：落 config\video-tools.json。（WHY: VT-026）</summary>
public class VideoToolsSettingsService : IVideoToolsSettingsService
{
    /// <summary>模块分区名 = 配置文件名基名（<c>config\video-tools.json</c>）。</summary>
    public const string Module = "video-tools";

    private const string KeyMaxConcurrency = "maxConcurrency";
    private const string KeyAdviceMode = "adviceMode";
    private const string KeyAdviceMinSaving = "adviceMinSavingPercent";

    /// <summary>省幅门槛的上下限（0 = 只要能小就转；90 = 至少要省九成才算值得）。</summary>
    public const int MinSavingLower = 0;
    public const int MinSavingUpper = 90;

    /// <summary>并发数上下限（与 <c>VideoListViewModel</c> 的 <c>Math.Clamp(…, 1, 16)</c> 同一口径）。</summary>
    public const int MinConcurrency = 1;
    public const int MaxConcurrencyLimit = 16;

    public const int DefaultMaxConcurrencyValue = 2;

    private readonly IOptionsStore _store;
    private readonly ConfigStoreService _cfg;

    public VideoToolsSettingsService(IOptionsStore store, ConfigStoreService cfg, IAppConfigService? legacy = null)
    {
        _store = store;
        _cfg = cfg;
        MigrateLegacyOnce(legacy);
    }

    public string ModuleKey => Module;

    public string FilePath => Path.Combine(_cfg.ConfigDir, Module + ".json");

    public int DefaultMaxConcurrency => DefaultMaxConcurrencyValue;

    public int MaxConcurrency => Math.Clamp(Parse(Read(KeyMaxConcurrency), DefaultMaxConcurrencyValue),
        MinConcurrency, MaxConcurrencyLimit);

    public event Action? Changed;

    public string SetMaxConcurrency(int value)
    {
        if (value < MinConcurrency || value > MaxConcurrencyLimit)
            return $"⚠ 并发数要在 {MinConcurrency}~{MaxConcurrencyLimit} 之间（收到 {value}）—— 未写盘，仍用上一个有效值。";

        try
        {
            _store.Set(Module, KeyMaxConcurrency, value.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            return $"⚠ 并发数保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return $"并发数已保存：{value} · 写入 {Module}.json，立即生效。";
    }

    // ==================== 转码前收益判定 ====================

    public string AdviceMode => AdviceModes.Normalize(Read(KeyAdviceMode));

    public string SetAdviceMode(string value)
    {
        var normalized = AdviceModes.Normalize(value);
        try
        {
            _store.Set(Module, KeyAdviceMode, normalized);
        }
        catch (Exception ex)
        {
            return $"⚠ 判定策略保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return normalized switch
        {
            AdviceModes.Strict => "判定策略：严格 —— 预估不值得的任务会被跳过（不算失败，列表里会写明原因）。",
            AdviceModes.Off => "判定策略：关闭 —— 不再做收益预估，所有任务照旧执行。",
            _ => "判定策略：建议 —— 只在列表 / 详情卡标黄提示，任务照常执行。"
        };
    }

    public int AdviceMinSavingPercent => Math.Clamp(
        Parse(Read(KeyAdviceMinSaving), TranscodeAdvisor.DefaultMinSavingPercent),
        MinSavingLower, MinSavingUpper);

    public string SetAdviceMinSavingPercent(int value)
    {
        if (value < MinSavingLower || value > MinSavingUpper)
            return $"⚠ 门槛要在 {MinSavingLower}~{MinSavingUpper}% 之间（收到 {value}）—— 未写盘，仍用上一个有效值。";

        try
        {
            _store.Set(Module, KeyAdviceMinSaving, value.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            return $"⚠ 门槛保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return $"省幅门槛已保存：{value}% —— 预估省不到 {value}% 的任务会被判为「不值得」。";
    }

    // ==================== 内部 ====================

    private string Read(string key)
    {
        try { return (_store.Get(Module, key) ?? "").Trim(); }
        catch { return ""; }
    }

    private static int Parse(string raw, int fallback)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private void MigrateLegacyOnce(IAppConfigService? legacy)
    {
        if (legacy == null) return;
        try
        {
            if (_store.Get(Module, KeyMaxConcurrency) != null) return;
            _store.Set(Module, KeyMaxConcurrency,
                Math.Clamp(legacy.MaxConcurrency, MinConcurrency, MaxConcurrencyLimit)
                     .ToString(CultureInfo.InvariantCulture));
        }
        catch { /* 迁移失败不阻断启动：本次用默认并发数 */ }
    }
}
