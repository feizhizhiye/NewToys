using System;

namespace NewToys.WebTools.Services;

/// <summary>
/// 「网页工具」模块设置的读写契约（v3.64 / NT-061，需求 §3.5 + §4.9）：**本模块配置的唯一写入点**。
/// <list type="bullet">
///   <item>数据落 <c>&lt;配置根&gt;\config\web-tools.json</c>（走 Core 的 <c>IOptionsStore</c>）。</item>
///   <item>本模块可配项 = <b>网页抓取的默认 User-Agent 与单次抓取超时</b>（原来硬编码在
///   <c>ScrapeService</c> 里，现在下沉为可手改的模块配置）。</item>
///   <item>⚠️ 书签 / Ping / Hosts 三个功能**没有模块级配置**（它们是数据清单，落各自的
///   <c>config\*.json</c>，由页面自身维护）—— 所以本页只有抓取默认项。</item>
/// </list>
/// </summary>
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
}
