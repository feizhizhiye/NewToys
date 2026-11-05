using System;

namespace NewToys.WebTools.Services;

/// <summary>「网页工具」模块设置的读写契约（，需求 §3.5 + §4.9）：本模块配置的唯一写入点。（WHY: WT-010）</summary>
public interface IWebToolsSettingsService
{
    /// <summary>选项存储里的模块分区名（= 配置文件名基名）。</summary>
    string ModuleKey { get; }

    /// <summary>本模块设置文件全路径（<c>config\web-tools.json</c>）。</summary>
    string FilePath { get; }

    /// <summary>内置默认 UA（配置留空时用它）。</summary>
    string BuiltInUserAgent { get; }

    /// <summary>用户配置的 UA（留空 = 用内置）。</summary>
    string DefaultUserAgent { get; }

    /// <summary>**生效的** UA（配置留空时回落 <see cref="BuiltInUserAgent"/>）—— 抓取服务直接用这个。</summary>
    string EffectiveUserAgent { get; }

    /// <summary>单次抓取超时默认值（秒）。</summary>
    int DefaultScrapeTimeoutSeconds { get; }

    /// <summary>生效的单次抓取超时（秒；非法 / 留空 ⇒ 默认值）。</summary>
    int ScrapeTimeoutSeconds { get; }

    /// <summary>写盘成功后触发。</summary>
    event Action? Changed;

    /// <summary>写「默认 UA」（空 = 恢复内置）；返回状态文案。</summary>
    string SetDefaultUserAgent(string value);

    /// <summary>写「抓取超时（秒）」；返回状态文案（越界 ⇒ <c>⚠</c> 开头 + 未写盘）。</summary>
    string SetScrapeTimeoutSeconds(int value);

    // ==================== Hosts 检测 / 备份 ====================

    /// <summary>hosts 条目检测的单次连接超时（秒）。</summary>
    int HostsCheckTimeoutSeconds { get; }

    /// <summary>hosts 条目检测的并发数。</summary>
    int HostsCheckConcurrency { get; }

    /// <summary>系统 hosts 备份保留份数（超出删最旧）。</summary>
    int HostsBackupKeep { get; }

    /// <summary>应用方案后是否自动刷新 DNS 缓存（<c>ipconfig /flushdns</c>）。</summary>
    bool HostsFlushDnsAfterApply { get; }

    /// <summary>写「检测超时（秒）」。</summary>
    string SetHostsCheckTimeoutSeconds(int value);

    /// <summary>写「检测并发」。</summary>
    string SetHostsCheckConcurrency(int value);

    /// <summary>写「备份保留份数」。</summary>
    string SetHostsBackupKeep(int value);

    /// <summary>写「应用后刷新 DNS」。</summary>
    string SetHostsFlushDnsAfterApply(bool value);
}
