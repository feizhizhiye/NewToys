using System;
using System.Globalization;
using NewToys.Shared.Services;

namespace NewToys.Reading.Services;

/// <summary>
/// 阅读模块设置（<c>config\reading.json</c>）的唯一读取点。
/// <para>唯一写入点 = 本模块设置页；本类只读，缺项回落到默认值（⛔ 不因配置缺失改变行为）。</para>
/// </summary>
public sealed class ReadingSettingsService
{
    /// <summary>配置分区名（<c>config\reading.json</c>）。</summary>
    public const string Module = "reading";

    /// <summary>默认 UA：桌面浏览器标识，多数站点按它返回桌面版页面。</summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private readonly IOptionsStore? _opts;

    public ReadingSettingsService(IOptionsStore? opts = null) => _opts = opts;

    /// <summary>并发抓取上限（多源搜索 / 批量抓取用）。</summary>
    public int Concurrency => ReadInt("concurrency", 3, 1, 16);

    /// <summary>单次请求超时（秒）。</summary>
    public int TimeoutSeconds => ReadInt("timeout-seconds", 15, 5, 120);

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

    private int ReadInt(string key, int fallback, int min, int max)
    {
        var raw = _opts?.Get(Module, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private bool ReadBool(string key, bool fallback)
    {
        var raw = _opts?.Get(Module, key);
        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
