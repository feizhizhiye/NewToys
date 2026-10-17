using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jint;
using NewToys.Core.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>
/// 网页抓取 / 爬虫管理器：多配置（起始网址 / 规则 / UA / JS 整合脚本）。
/// <para>
/// 流程：用 <see cref="HttpClient"/> 拉取页面 → 按 <c>Css</c>（AngleSharp 选择器）或 <c>Regex</c> 抽取字符串数组
/// → 用 <see cref="Jint"/> 跑一段 JS 整合脚本（<c>items</c> 为数组）得到最终输出。
/// 数据落 <c>config\scrape-tasks.json</c>（可被数据管理备份）。
/// </para>
/// </summary>
public class ScrapeService
{
    private readonly ConfigStoreService _cfg;

    /// <summary>
    /// 本模块设置（v3.64 / NT-061）：抓取的**默认 UA** 与**单次超时**从这里来（落 <c>config\web-tools.json</c>，
    /// 可手改；v3.64 前这两个值硬编码在本类里）。
    /// </summary>
    private readonly IWebToolsSettingsService? _wt;

    public ScrapeService(ConfigStoreService cfg, IWebToolsSettingsService? wt = null)
    {
        _cfg = cfg;
        _wt = wt;
    }

    private string FilePath => Path.Combine(_cfg.ConfigDir, "scrape-tasks.json");

    public IReadOnlyList<ScrapeTask> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ScrapeTask>();
            var list = JsonSerializer.Deserialize<List<ScrapeTask>>(File.ReadAllText(FilePath));
            return list ?? new List<ScrapeTask>();
        }
        catch { return new List<ScrapeTask>(); }
    }

    public void Save(IEnumerable<ScrapeTask> items)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>执行一次抓取：拉页面 → 抽取 → JS 整合。返回 (成功, 输出, 错误, 条目数)。</summary>
    public async Task<(bool ok, string output, string error, int count)> RunAsync(ScrapeTask task)
    {
        if (string.IsNullOrWhiteSpace(task.StartUrl)) return (false, "", "起始网址为空", 0);
        try
        {
            var timeout = _wt?.ScrapeTimeoutSeconds ?? WebToolsSettingsService.DefaultTimeout;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                string.IsNullOrWhiteSpace(task.UserAgent)
                    ? (_wt?.EffectiveUserAgent ?? WebToolsSettingsService.BuiltInUa)
                    : task.UserAgent);

            var html = await client.GetStringAsync(task.StartUrl).ConfigureAwait(false);

            List<string> items;
            if (string.Equals(task.RuleType, "Regex", StringComparison.OrdinalIgnoreCase))
            {
                var re = new Regex(task.Rule, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                items = re.Matches(html).Cast<Match>().Select(m => m.Value).Where(s => s.Length > 0).ToList();
            }
            else
            {
                var doc = new HtmlParser().ParseDocument(html);
                items = doc.QuerySelectorAll(task.Rule)
                          .Select(e => (e as IElement)?.TextContent?.Trim() ?? "")
                          .Where(s => s.Length > 0)
                          .ToList();
            }

            var output = Integrate(items, task.JsScript);
            return (true, output, "", items.Count);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message, 0);
        }
    }

    /// <summary>用 Jint 跑整合脚本：<c>items</c> 为字符串数组，脚本返回最终文本。脚本为空则直接拼接。</summary>
    private static string Integrate(List<string> items, string js)
    {
        try
        {
            var engine = new Engine();
            engine.SetValue("items", items.ToArray());
            var script = string.IsNullOrWhiteSpace(js) ? "items.join('\\n')" : js;
            // Jint 3.x 没有 GetCompletionValue()：用 Evaluate 直接取脚本的求值结果（JsValue）。
            var val = engine.Evaluate(script);
            var s = val?.ToString() ?? "";
            return string.IsNullOrWhiteSpace(s) ? string.Join("\n", items) : s;
        }
        catch
        {
            // 脚本出错 → 退回原始抓取结果，保证总能看到内容
            return string.Join("\n", items);
        }
    }
}
