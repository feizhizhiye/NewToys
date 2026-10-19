using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>
/// 抓取 + 跑规则（NT-084 批 C）。
/// <para>
/// 🔴 <b>C# 不做任何规则解析</b>：把 HTML 存成文件，交给 Python（<see cref="IPythonRuntime"/>）跑
/// <c>RULES</c>，C# 只负责"取 HTML / 送路径 / 解析返回的 JSON"。
/// </para>
/// <para>
/// 缓存：<c>&lt;DataDir&gt;\web-tools\cache\&lt;规则集名&gt;\page.html</c>（单文件覆盖）——
/// 调试时反复跑命令 **不再重复发请求**。
/// </para>
/// </summary>
public sealed class RuleRunService
{
    private readonly IPythonRuntime _py;
    private readonly RuleFileService _files;
    private readonly ConfigStoreService _cfg;
    private readonly IWebToolsSettingsService? _wt;

    private static bool _encodingReady;

    public RuleRunService(IPythonRuntime py, RuleFileService files, ConfigStoreService cfg, IWebToolsSettingsService? wt = null)
    {
        _py = py;
        _files = files;
        _cfg = cfg;
        _wt = wt;

        if (!_encodingReady)
        {
            // GBK / GB18030 站点：需注册代码页编码器（net10.0 共享框架自带，无需第三方包）
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _encodingReady = true; } catch { }
        }
    }

    // ==================== 缓存路径 ====================

    public string CacheDir(string ruleName) => Path.Combine(_cfg.DataDir, "web-tools", "cache", RuleFileService.SafeName(ruleName));

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

    // ==================== 抓取 ====================

    /// <summary>抓一次页面并落缓存。返回 (成功, HTML, 错误)。</summary>
    public async Task<(bool ok, string html, string error)> FetchAsync(
        string ruleName, string url, string encoding, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return (false, "", "起始网址为空（文件头该有 # base: https://…）");
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return (false, "", "网址不是绝对地址：" + url);

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_wt?.ScrapeTimeoutSeconds ?? 20)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                string.IsNullOrWhiteSpace(_wt?.EffectiveUserAgent) ? "Mozilla/5.0" : _wt!.EffectiveUserAgent);

            var bytes = await client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            var (declared, useGbk) = SniffCharset(bytes, encoding);
            var text = Decode(bytes, declared, useGbk);

            var dir = CacheDir(ruleName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(HtmlPath(ruleName), text, new UTF8Encoding(false));

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

    // ==================== 导入 Legado 书源（批 D） ====================

    /// <summary>
    /// 把 Legado（阅读 App）书源 json 转成规则集文件：**转换逻辑全在随包脚本
    /// <c>defaults\tools\convert_legado.py</c>**，C# 只送路径 + 解析回来的一行 JSON。
    /// </summary>
    /// <returns>(写入了几条, 给用户看的一句话)</returns>
    public async Task<(int written, string message)> ConvertLegadoAsync(string jsonPath, CancellationToken ct = default)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "defaults", "tools", "convert_legado.py");
        if (!File.Exists(script)) return (0, "⚠ 找不到转换脚本：" + script);

        var req = new PyRequest
        {
            Mode = "file",
            Path = script,
            TimeoutSeconds = 120,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["in"] = jsonPath,
                ["outDir"] = _files.RulesDir
            })
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
        {
            var detail = !string.IsNullOrWhiteSpace(res.Traceback) ? res.Traceback
                       : (res.Error + " " + res.Err).Trim();
            return (0, "⚠ " + detail);
        }

        var json = (res.Out ?? "").Trim();
        var nl = json.LastIndexOf('\n');
        if (nl >= 0) json = json[(nl + 1)..].Trim();   // 脚本可能先打了别的东西

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err)) return (0, "⚠ " + (err.GetString() ?? ""));

            var names = new List<string>();
            if (root.TryGetProperty("written", out var w) && w.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in w.EnumerateArray())
                {
                    if (el.TryGetProperty("name", out var n)) names.Add(n.GetString() ?? "");
                }
            }

            return (names.Count, names.Count == 0
                ? "没有可转换的书源（检查文件是不是 Legado 书源 json）"
                : "已导入 " + names.Count + " 条：" + string.Join("、", names));
        }
        catch (Exception ex) { return (0, "⚠ 转换结果无法解析：" + ex.Message); }
    }

    // ==================== 跑规则 ====================

    /// <summary>只读 <c>RULES</c> 列表（不抓网页，用于打开编辑器时回填命令列表）。</summary>
    public async Task<List<RuleCommand>> ListCommandsAsync(string ruleName, CancellationToken ct = default)
    {
        var result = await RunPythonAsync(ruleName, "list", "", ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>按缓存 HTML 跑全部命令，回填命中数与预览。</summary>
    public async Task<List<RuleCommand>> RunRulesAsync(string ruleName, string baseUrl, CancellationToken ct = default)
    {
        var htmlPath = HtmlPath(ruleName);
        if (!File.Exists(htmlPath)) throw new InvalidOperationException("还没抓到 HTML —— 先点「🌐 抓取 HTML」。");
        return await RunPythonAsync(ruleName, "run", baseUrl, ct).ConfigureAwait(false);
    }

    /// <summary>调 Python：<c>mode=list|run</c>，返回命令行。</summary>
    private async Task<List<RuleCommand>> RunPythonAsync(string ruleName, string mode, string baseUrl, CancellationToken ct)
    {
        var req = new PyRequest
        {
            Mode = "code",
            TimeoutSeconds = 60,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["mode"] = mode,
                ["path"] = _files.PathOf(ruleName),
                ["html_path"] = HtmlPath(ruleName),
                ["base"] = baseUrl ?? ""
            }),
            Code = Program
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(res.Traceback) ? res.Error : res.Traceback);

        var json = (res.Out ?? "").Trim();
        var nl = json.LastIndexOf('\n');
        if (nl >= 0) json = json[(nl + 1)..].Trim();   // 脚本可能先打了别的东西

        if (json.Length == 0) return new List<RuleCommand>();

        using var doc = JsonDocument.Parse(json);
        var list = new List<RuleCommand>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var cmd = new RuleCommand
            {
                Key = el.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                Expr = el.TryGetProperty("expr", out var e) ? e.GetString() ?? "" : ""
            };

            if (el.TryGetProperty("count", out var c) && c.TryGetInt32(out var count))
            {
                cmd.HitText = count + " 条";
                if (el.TryGetProperty("preview", out var pv) && pv.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var v in pv.EnumerateArray())
                    {
                        var s = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
                        if (s.Length > 60) s = s[..60] + "…";
                        items.Add(s.Replace("\r", " ").Replace("\n", " "));
                    }
                    cmd.Preview = string.Join("\n", items);
                }
            }

            list.Add(cmd);
        }

        return list;
    }

    // ==================== Python 侧引擎（⛔ C# 不解析规则） ====================
    //
    // 职责：读规则文件 → 取 RULES → 按表达式求值 → 一行 JSON 打到 stdout。
    // mode=list 只回 key/expr；mode=run 额外回 count / preview。
    private const string Program = """
import sys, re, json, importlib.util
from urllib.parse import urljoin

ROOT_HTML = ""

def _load_rules(path):
    spec = importlib.util.spec_from_file_location("rulemod", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return list(getattr(mod, "RULES", []) or [])

def _split_take(s):
    m = re.search(r"@([A-Za-z_][\w-]*)$", s)
    if m:
        return s[:m.start()], m.group(1)
    return s, "text"

def _default_to_css(rule):
    parts = [p.strip() for p in rule.split("@") if p.strip()]
    if not parts:
        return "", "text"
    take = "text"
    segs = []
    for i, p in enumerate(parts):
        if i == len(parts) - 1 and p in ("text", "html", "outerHtml", "textNodes", "href", "src"):
            take = p
            continue
        m = re.match(r"^(class|id|tag)\.(.+)$", p)
        if not m:
            return "", "text"
        kind, val = m.group(1), re.sub(r"\.-?\d+$", "", m.group(2))
        if not val:
            continue
        if kind == "class":
            segs.append("." + val)
        elif kind == "id":
            segs.append("#" + val)
        else:
            segs.append(val)
    return " ".join(segs), take

def _extract(nodes, take, base):
    vals = []
    for n in nodes:
        try:
            if take in ("text", "textNodes"):
                v = (n.xpath("string(.)").get() or "").strip()
            elif take in ("html", "outerHtml"):
                v = n.get() or ""
            else:
                v = n.attrib.get(take, "") if hasattr(n, "attrib") else ""
                if v and take in ("href", "src") and base:
                    v = urljoin(base, v)
            if v:
                vals.append(v)
        except Exception:
            continue
    return vals

def _one(root, cand, base):
    c = cand.strip()
    if not c:
        return []
    low = c.lower()
    if low.startswith("json:") or low.startswith("js:"):
        return ["(" + low.split(":")[0] + ": 规则暂未支持，见 NT-084 批 D)"]
    if low.startswith("regex:"):
        return [m for m in re.findall(c[6:], ROOT_HTML, re.S) if m]
    if low.startswith("xpath:"):
        q, take = _split_take(c[6:])
        return _extract(root.xpath(q), take, base)
    if low.startswith("css:"):
        q, take = _split_take(c[4:])
        return _extract(root.css(q), take, base)
    if c.startswith("="):
        return [c[1:]]
    q, take = _default_to_css(c)
    if not q:
        return []
    return _extract(root.css(q), take, base)

def eval_expr(root, expr, base):
    parts = expr.split("##")
    core, filters = parts[0], parts[1:]
    vals = []
    for cand in core.split("||"):
        vals = _one(root, cand, base)
        if vals:
            break
    if vals and filters:
        for i in range(0, len(filters) - 1, 2):
            try:
                vals = [re.sub(filters[i], filters[i + 1], v) for v in vals]
            except Exception:
                pass
    return [v for v in vals if v]

mode = vars.get("mode") or "list"
rules = _load_rules(vars.get("path") or "")

if mode == "list":
    print(json.dumps([{"key": str(k), "expr": str(e)} for k, e in rules], ensure_ascii=False))
else:
    from parsel import Selector
    p = vars.get("html_path") or ""
    ROOT_HTML = open(p, "r", encoding="utf-8").read()
    root = Selector(text=ROOT_HTML)
    base = vars.get("base") or ""
    out = []
    for item in rules:
        k = str(item[0])
        e = str(item[1])
        try:
            vals = eval_expr(root, e, base)
            out.append({"key": k, "expr": e, "count": len(vals), "preview": vals[:3]})
        except Exception as ex:
            out.append({"key": k, "expr": e, "count": 0, "preview": [], "error": str(ex)})
    print(json.dumps(out, ensure_ascii=False))
""";
}
