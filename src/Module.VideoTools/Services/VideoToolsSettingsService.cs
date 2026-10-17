using System;
using System.Globalization;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.VideoTools.Services;

/// <summary>
/// <see cref="IVideoToolsSettingsService"/> 的实现（v3.64 / NT-061）：落 <c>config\video-tools.json</c>。
/// <para>
/// <b>一次性迁移</b>：并发数原来在 <c>app.json</c> 的 <c>MaxConcurrency</c>（没有界面、只能手改文件）。
/// 首次运行本服务时把它搬进 <c>video-tools.json</c>（⛔ 不丢配置）；之后 <c>app.json</c> 那个字段不再被读写。
/// </para>
/// </summary>
public class VideoToolsSettingsService : IVideoToolsSettingsService
{
    /// <summary>模块分区名 = 配置文件名基名（<c>config\video-tools.json</c>）。</summary>
    public const string Module = "video-tools";

    private const string KeyMaxConcurrency = "maxConcurrency";

    /// <summary>并发数上下限（与 <c>VideoListViewModel</c> 原来的 <c>Math.Clamp(…, 1, 16)</c> 同一口径）。</summary>
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
