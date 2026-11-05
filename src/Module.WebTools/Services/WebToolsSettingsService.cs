using System;
using System.Globalization;
using System.IO;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.WebTools.Services;

/// <summary>IWebToolsSettingsService 的实现：落 config\web-tools.json。</summary>
public class WebToolsSettingsService : IWebToolsSettingsService
{
    /// <summary>模块分区名 = 配置文件名基名（<c>config\web-tools.json</c>）。</summary>
    public const string Module = "web-tools";

    private const string KeyUserAgent = "defaultUserAgent";
    private const string KeyScrapeTimeout = "scrapeTimeoutSeconds";
    private const string KeyHostsTimeout = "hostsCheckTimeoutSeconds";
    private const string KeyHostsConcurrency = "hostsCheckConcurrency";
    private const string KeyHostsKeep = "hostsBackupKeep";
    private const string KeyHostsFlush = "hostsFlushDnsAfterApply";

    /// <summary>内置 UA（v3.64 前硬编码在 <c>ScrapeService</c> 里，现在这里是唯一来源）。</summary>
    public const string BuiltInUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    public const int DefaultTimeout = 20;
    public const int MinTimeout = 5;
    public const int MaxTimeout = 300;

    private readonly IOptionsStore _store;
    private readonly ConfigStoreService _cfg;

    public WebToolsSettingsService(IOptionsStore store, ConfigStoreService cfg)
    {
        _store = store;
        _cfg = cfg;
        SeedDefaults();
    }

    public string ModuleKey => Module;

    public string FilePath => Path.Combine(_cfg.ConfigDir, Module + ".json");

    public string BuiltInUserAgent => BuiltInUa;

    public string DefaultUserAgent => Read(KeyUserAgent);

    public string EffectiveUserAgent
    {
        get
        {
            var ua = DefaultUserAgent;
            return ua.Length > 0 ? ua : BuiltInUa;
        }
    }

    public int DefaultScrapeTimeoutSeconds => DefaultTimeout;

    public int ScrapeTimeoutSeconds
        => Math.Clamp(Parse(Read(KeyScrapeTimeout), DefaultTimeout), MinTimeout, MaxTimeout);

    public event Action? Changed;

    public string SetDefaultUserAgent(string? value)
    {
        var v = (value ?? "").Trim();
        try
        {
            _store.Set(Module, KeyUserAgent, v);
        }
        catch (Exception ex)
        {
            return $"⚠ 默认 User-Agent 保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return v.Length == 0
            ? $"默认 User-Agent 已恢复内置那一串 · 写入 {Module}.json，立即生效。"
            : $"默认 User-Agent 已保存 · 写入 {Module}.json，立即生效。";
    }

    public string SetScrapeTimeoutSeconds(int value)
    {
        if (value < MinTimeout || value > MaxTimeout)
            return $"⚠ 抓取超时要在 {MinTimeout}~{MaxTimeout} 秒之间（收到 {value}）—— 未写盘，仍用上一个有效值。";

        try
        {
            _store.Set(Module, KeyScrapeTimeout, value.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            return $"⚠ 抓取超时保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。";
        }

        Changed?.Invoke();
        return $"抓取超时已保存：{value} 秒 · 写入 {Module}.json，立即生效。";
    }

    // ==================== Hosts 检测 / 备份 ====================

    public const int DefaultHostsTimeout = 3;
    public const int MinHostsTimeout = 1;
    public const int MaxHostsTimeout = 30;

    public const int DefaultHostsConcurrency = 8;
    public const int MinHostsConcurrency = 1;
    public const int MaxHostsConcurrency = 32;

    public const int DefaultHostsKeep = 10;
    public const int MinHostsKeep = 1;
    public const int MaxHostsKeep = 100;

    public int HostsCheckTimeoutSeconds
        => Math.Clamp(Parse(Read(KeyHostsTimeout), DefaultHostsTimeout), MinHostsTimeout, MaxHostsTimeout);

    public int HostsCheckConcurrency
        => Math.Clamp(Parse(Read(KeyHostsConcurrency), DefaultHostsConcurrency), MinHostsConcurrency, MaxHostsConcurrency);

    public int HostsBackupKeep
        => Math.Clamp(Parse(Read(KeyHostsKeep), DefaultHostsKeep), MinHostsKeep, MaxHostsKeep);

    public bool HostsFlushDnsAfterApply => Read(KeyHostsFlush) is not "false";

    public string SetHostsCheckTimeoutSeconds(int value)
    {
        if (value < MinHostsTimeout || value > MaxHostsTimeout)
            return $"⚠ 检测超时要在 {MinHostsTimeout}~{MaxHostsTimeout} 秒之间（收到 {value}）—— 未写盘。";
        return Write(KeyHostsTimeout, value.ToString(CultureInfo.InvariantCulture), $"检测超时已保存：{value} 秒");
    }

    public string SetHostsCheckConcurrency(int value)
    {
        if (value < MinHostsConcurrency || value > MaxHostsConcurrency)
            return $"⚠ 检测并发要在 {MinHostsConcurrency}~{MaxHostsConcurrency} 之间（收到 {value}）—— 未写盘。";
        return Write(KeyHostsConcurrency, value.ToString(CultureInfo.InvariantCulture), $"检测并发已保存：{value}");
    }

    public string SetHostsBackupKeep(int value)
    {
        if (value < MinHostsKeep || value > MaxHostsKeep)
            return $"⚠ 备份保留份数要在 {MinHostsKeep}~{MaxHostsKeep} 之间（收到 {value}）—— 未写盘。";
        return Write(KeyHostsKeep, value.ToString(CultureInfo.InvariantCulture), $"备份保留份数已保存：{value} 份");
    }

    public string SetHostsFlushDnsAfterApply(bool value)
        => Write(KeyHostsFlush, value ? "true" : "false", value ? "应用方案后将自动刷新 DNS 缓存" : "应用方案后不再刷新 DNS 缓存");

    /// <summary>统一的「写 + 状态文案」。</summary>
    private string Write(string key, string value, string okMessage)
    {
        try { _store.Set(Module, key, value); }
        catch (Exception ex) { return $"⚠ 保存失败：{ex.Message} —— 未写盘，仍用上一个有效值。"; }

        Changed?.Invoke();
        return $"{okMessage} · 写入 {Module}.json，立即生效。";
    }

    // ==================== 内部 ====================

    private string Read(string key)
    {
        try { return (_store.Get(Module, key) ?? "").Trim(); }
        catch { return ""; }
    }

    private static int Parse(string raw, int fallback)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>首次运行把两个默认值写进文件（已存在则不动）——配置要「看得见、可手改」。</summary>
    private void SeedDefaults()
    {
        try
        {
            if (_store.Get(Module, KeyUserAgent) == null) _store.Set(Module, KeyUserAgent, BuiltInUa);
            if (_store.Get(Module, KeyScrapeTimeout) == null)
                _store.Set(Module, KeyScrapeTimeout, DefaultTimeout.ToString(CultureInfo.InvariantCulture));
            if (_store.Get(Module, KeyHostsTimeout) == null)
                _store.Set(Module, KeyHostsTimeout, DefaultHostsTimeout.ToString(CultureInfo.InvariantCulture));
            if (_store.Get(Module, KeyHostsConcurrency) == null)
                _store.Set(Module, KeyHostsConcurrency, DefaultHostsConcurrency.ToString(CultureInfo.InvariantCulture));
            if (_store.Get(Module, KeyHostsKeep) == null)
                _store.Set(Module, KeyHostsKeep, DefaultHostsKeep.ToString(CultureInfo.InvariantCulture));
            if (_store.Get(Module, KeyHostsFlush) == null) _store.Set(Module, KeyHostsFlush, "true");
        }
        catch { /* 播种失败不阻断启动：读到 null 就走内置默认值 */ }
    }
}
