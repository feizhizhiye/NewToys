using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 抓取 + 跑规则。
/// <para>
/// 🔴 <b>C# 不做任何规则解析</b>：把 HTML 存成文件，交给 Python（<see cref="IPythonRuntime"/>）跑
/// <c>RULES</c>，C# 只负责"取 HTML / 送路径 / 解析返回的 JSON"。
/// </para>
/// <para>
/// 缓存：<c>&lt;DataDir&gt;\script\cache\&lt;规则集名&gt;\page.html</c>（单文件覆盖）——
/// 调试时反复跑命令 **不再重复发请求**。
/// </para>
/// </summary>
public sealed partial class RuleRunService
{
    private readonly IPythonRuntime _py;
    private readonly RuleFileService _files;
    private readonly ConfigStoreService _cfg;
    /// <summary>
    /// 抓取默认项（UA / 超时）的取值处。
    /// <para>
    /// 🔴 设置页「网络工具设置」仍留在网络工具模块（菜单 Key `settings-web-tools` ⛔ 不改），
    /// 它把值写进 <c>config\web-tools.json</c>；而抓取服务现在归脚本中心（模块禁互引 ⛔ 不能引用
    /// <c>IWebToolsSettingsService</c>）⇒ 这里经 **Core 的 <see cref="IOptionsStore"/>** 只读同一份文件，
    /// 键名 / 默认值与设置页保持逐字一致。设置拆分后再归位到 <c>config\script-manager.json</c>。
    /// </para>
    /// </summary>
    private readonly IOptionsStore? _opts;

    private static bool _encodingReady;

    public RuleRunService(IPythonRuntime py, RuleFileService files, ConfigStoreService cfg, IOptionsStore? opts = null)
    {
        _py = py;
        _files = files;
        _cfg = cfg;
        _opts = opts;

        if (!_encodingReady)
        {
            // GBK / GB18030 站点：需注册代码页编码器（net10.0 共享框架自带，无需第三方包）
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _encodingReady = true; } catch { }
        }
    }

    // ==================== 抓取默认项（经 Core 的 IOptionsStore 读 config\web-tools.json） ====================

    /// <summary>设置分区名（= 配置文件名基名）；与「网络工具设置」页写入的文件同源。</summary>
    private const string ScrapeModule = "web-tools";
    private const string KeyUserAgent = "defaultUserAgent";
    private const string KeyScrapeTimeout = "scrapeTimeoutSeconds";
    private const int DefaultScrapeTimeout = 20;
    private const int MinScrapeTimeout = 5;
    private const int MaxScrapeTimeout = 300;

    /// <summary>生效的 UA（配置留空 ⇒ 兜底串）。</summary>
    private string EffectiveUserAgent
    {
        get
        {
            var ua = ReadOption(KeyUserAgent);
            return ua.Length > 0 ? ua : "Mozilla/5.0";
        }
    }

    /// <summary>生效的单次抓取超时（秒；非法 / 留空 ⇒ 默认值，钳 5~300）。</summary>
    private int ScrapeTimeoutSeconds
    {
        get
        {
            var raw = ReadOption(KeyScrapeTimeout);
            var v = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : DefaultScrapeTimeout;
            return Math.Clamp(v, MinScrapeTimeout, MaxScrapeTimeout);
        }
    }

    private string ReadOption(string key)
    {
        if (_opts == null) return "";
        try { return (_opts.Get(ScrapeModule, key) ?? "").Trim(); }
        catch { return ""; }
    }

    // ==================== 缓存路径 ====================

    public string CacheDir(string ruleName) => Path.Combine(_cfg.DataDir, "script", "cache", RuleFileService.SafeName(ruleName));

    public string HtmlPath(string ruleName) => Path.Combine(CacheDir(ruleName), "page.html");

    /// <summary>读缓存 HTML（没抓过返回空串）。</summary>
    public string LoadCachedHtml(string ruleName)
    {
        try
        {
            var p = HtmlPath(ruleName);
            return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// 调试集合共用的那份 HTML：<c>cache\_debug\page.html</c>。
    /// 🔴 试跑推荐的前提是「多个候选脚本对<b>同一份</b> HTML 求值」，⛔ 不能各抓各的。
    /// </summary>
    public string DebugHtmlPath => Path.Combine(_cfg.DataDir, "script", "cache", "_debug", "page.html");

    /// <summary>爬虫面板共用的那份 HTML：<c>cache\_crawl\page.html</c>。</summary>
    public string CrawlHtmlPath => Path.Combine(_cfg.DataDir, "script", "cache", "_crawl", "page.html");

    // ==================== 抓取 ====================

    /// <summary>抓一次页面并落**该规则集自己的**缓存。返回 (成功, HTML, 错误)。</summary>
    public Task<(bool ok, string html, string error)> FetchAsync(
        string ruleName, string url, string encoding, CancellationToken ct = default)
        => FetchIntoAsync(url, encoding, HtmlPath(ruleName), ct);

    /// <summary>抓一次页面并写到 <paramref name="htmlPath"/>（调试集合 / 试跑推荐共用入口）。</summary>
    public async Task<(bool ok, string html, string error)> FetchIntoAsync(
        string url, string encoding, string htmlPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return (false, "", "起始网址为空（文件头该有 # base: https://…）");
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return (false, "", "网址不是绝对地址：" + url);

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(ScrapeTimeoutSeconds)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(EffectiveUserAgent);

            var bytes = await client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            var (declared, useGbk) = SniffCharset(bytes, encoding);
            var text = Decode(bytes, declared, useGbk);

            var dir = Path.GetDirectoryName(htmlPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(htmlPath, text, new UTF8Encoding(false));

            return (true, text, "");
        }
        catch (OperationCanceledException) { return (false, "", "抓取已取消或超时。"); }
        catch (Exception ex) { return (false, "", "抓取失败：" + ex.Message); }
    }

    /// <summary>从 <c>&lt;meta charset=...&gt;</c> 猜编码；猜不到用文件头 <c># encoding:</c>。</summary>
    private static (Encoding enc, bool isCodePage) SniffCharset(byte[] bytes, string declared)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096)).ToLowerInvariant();
        var name = "";
        var idx = head.IndexOf("charset=", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var rest = head[(idx + 8)..].TrimStart('"', '\'', ' ');
            var end = rest.IndexOfAny(new[] { '"', '\'', ';', '>', ' ', '\r', '\n' });
            name = end > 0 ? rest[..end] : rest;
        }
        if (name.Length == 0) name = (declared ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) name = "utf-8";

        try
        {
            if (name is "gbk" or "gb2312" or "gb18030" or "936") return (Encoding.GetEncoding("gb18030"), true);
            if (name is "utf-8" or "utf8") return (new UTF8Encoding(false), false);
            return (Encoding.GetEncoding(name), true);
        }
        catch { return (new UTF8Encoding(false), false); }
    }

    private static string Decode(byte[] bytes, Encoding enc, bool isCodePage)
    {
        try { return enc.GetString(bytes); }
        catch { return Encoding.UTF8.GetString(bytes); }
    }

    // ==================== 跑规则 ====================

    /// <summary>只读 <c>RULES</c> 列表（不抓网页，用于打开编辑器时回填命令列表）。</summary>
    public async Task<List<RuleCommand>> ListCommandsAsync(string ruleName, CancellationToken ct = default)
        => await RunPythonAsync(ruleName, "list", "", "", ct).ConfigureAwait(false);

    /// <summary>按缓存 HTML 跑全部命令，回填命中数与预览。</summary>
    public async Task<List<RuleCommand>> RunRulesAsync(string ruleName, string baseUrl, CancellationToken ct = default)
    {
        var htmlPath = HtmlPath(ruleName);
        if (!File.Exists(htmlPath)) throw new InvalidOperationException("还没抓到 HTML —— 先点「🌐 抓取 HTML」。");
        return await RunPythonAsync(ruleName, "run", baseUrl, htmlPath, ct).ConfigureAwait(false);
    }

    /// <summary>用**指定的 HTML** 跑指定脚本（调试集合：一份 HTML 喂给不同段的脚本）。</summary>
    public async Task<List<RuleCommand>> RunScriptAsync(string ruleName, string htmlPath, string baseUrl, CancellationToken ct = default)
    {
        if (!File.Exists(htmlPath)) throw new InvalidOperationException("还没抓到 HTML —— 先点「🌐 抓页」。");
        return await RunPythonAsync(ruleName, "run", baseUrl, htmlPath, ct).ConfigureAwait(false);
    }

}
