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

public sealed partial class RuleRunService
{

    /// <summary>取输出的最后一行（脚本可能先打了别的东西，JSON 只在最后一行）。</summary>
    private static string LastLine(string? output)
    {
        var json = (output ?? "").Trim();
        var nl = json.LastIndexOf('\n');
        return nl >= 0 ? json[(nl + 1)..].Trim() : json;
    }

    /// <summary>调 Python：<c>mode=list|run</c>，返回命令行。</summary>
    private async Task<List<RuleCommand>> RunPythonAsync(string ruleName, string mode, string baseUrl, string htmlPath, CancellationToken ct)
    {
        var req = new PyRequest
        {
            Mode = "code",
            TimeoutSeconds = 60,
            ArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["mode"] = mode,
                ["path"] = _files.PathOf(ruleName),
                ["html_path"] = string.IsNullOrEmpty(htmlPath) ? HtmlPath(ruleName) : htmlPath,
                ["base"] = baseUrl ?? ""
            }),
            Code = Program
        };

        var res = await _py.RunAsync(req, ct).ConfigureAwait(false);
        if (!res.Ok || !string.IsNullOrEmpty(res.Error))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(res.Traceback) ? res.Error : res.Traceback);

        var json = LastLine(res.Out);

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
                cmd.Count = count;
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

            // 🔴 错误回显：Python 侧的异常必须搬到界面上，⛔ 不静默显示「0 条」
            if (el.TryGetProperty("error", out var err))
            {
                cmd.Error = err.GetString() ?? "";
                if (cmd.Error.Length > 0) cmd.HitText = "⚠ 出错";
            }

            list.Add(cmd);
        }

        return list;
    }

    // ==================== Python 侧引擎（⛔ C# 不解析规则） ====================
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

_JSON = {}

def _json_doc(text):
    # 页面里内嵌的 JSON（<script>var x = {...}</script> 或纯 JSON 响应）。
    # 先整篇试，失败再找**能解析的最大片段**（用标准库，⛔ 不引 jsonpath）。
    if "doc" in _JSON:
        return _JSON["doc"]
    doc = None
    t = (text or "").strip()
    try:
        doc = json.loads(t)
    except Exception:
        doc = None
    if doc is None:
        best = ""
        for opener, closer in (("{", "}"), ("[", "]")):
            i = t.find(opener)
            while i >= 0:
                depth, j = 0, i
                while j < len(t):
                    if t[j] == opener:
                        depth += 1
                    elif t[j] == closer:
                        depth -= 1
                        if depth == 0:
                            break
                    j += 1
                frag = t[i:j + 1]
                if len(frag) > len(best):
                    try:
                        json.loads(frag)
                        best = frag
                    except Exception:
                        pass
                i = t.find(opener, i + 1)
        try:
            doc = json.loads(best) if best else None
        except Exception:
            doc = None
    _JSON["doc"] = doc
    return doc

def _json_walk(doc, path):
    p = (path or "").strip()
    if p.startswith("$"):
        p = p[1:]
    toks, i = [], 0
    while i < len(p):
        if p[i] == ".":
            i += 1
            continue
        if p[i] == "[":
            j = p.find("]", i)
            if j < 0:
                break
            toks.append(p[i + 1:j].strip().strip("'\""))
            i = j + 1
        else:
            j = i
            while j < len(p) and p[j] not in ".[":
                j += 1
            toks.append(p[i:j])
            i = j
    cur = [doc]
    for t in toks:
        nxt = []
        for c in cur:
            if t in ("*", ""):
                if isinstance(c, list):
                    nxt.extend(c)
                elif isinstance(c, dict):
                    nxt.extend(c.values())
            elif isinstance(c, dict) and t in c:
                nxt.append(c[t])
            elif isinstance(c, list) and t.lstrip("-").isdigit():
                k = int(t)
                if -len(c) <= k < len(c):
                    nxt.append(c[k])
        cur = nxt
    out = []
    for v in cur:
        out.append(v if isinstance(v, str) else json.dumps(v, ensure_ascii=False))
    return [v for v in out if v]

def _xpath_by_text(t, tag=""):
    # 三个语义操作符的公共转写：按**可见文本**找元素。
    # ⛔ 不做绝对位置路径（/html/body/div[3]…）—— 契约明令禁用，一改版式就断。
    q = (t or "").replace("'", "").strip()
    if not q:
        return ""
    return "//" + (tag or "*") + "[contains(normalize-space(.), '" + q + "')]"

def _one(root, cand, base):
    c = cand.strip()
    if not c:
        return []
    low = c.lower()
    if low.startswith("json:"):
        doc = _json_doc(ROOT_HTML)
        if doc is None:
            return ["(json: 页面里没找到可解析的 JSON)"]
        return _json_walk(doc, c[5:]) or ["(json: 路径没命中：" + c[5:] + ")"]
    if low.startswith("js:"):
        return ["(js: 规则暂未支持 —— 需要 JS 引擎，⛔ 不做)"]
    if low.startswith("regex:"):
        return [m for m in re.findall(c[6:], ROOT_HTML, re.S) if m]
    # 语义操作符：text: 任意元素按文本（优先 a）／ link: 只找 a 按文本／ url: 按 href 片段找 a
    if low.startswith("text:"):
        q, take = _split_take(c[5:])
        nodes = root.xpath(_xpath_by_text(q, "a"))
        if not nodes:
            nodes = root.xpath(_xpath_by_text(q))
        return _extract(nodes, take, base)
    if low.startswith("link:"):
        q, take = _split_take(c[5:])
        return _extract(root.xpath(_xpath_by_text(q, "a")), take, base)
    if low.startswith("url:"):
        q, take = _split_take(c[4:])
        if take == "text":
            take = "href"      # url: 不写 @ ⇒ 取链接地址（翻页 / 下钻要的就是地址）
        return _extract(root.xpath("//a[contains(@href, '" + q.replace("'", "") + "')]"), take, base)
    if low.startswith("xpath:"):
        q, take = _split_take(c[6:])
        return _extract(root.xpath(q), take, base)
    if low.startswith("css:"):
        q, take = _split_take(c[4:])
        return _extract(root.css(q), take, base)
    if c.startswith("="):
        return [c[1:]]
    # 不写前缀 ⇒ 按 CSS 解析（🔴 2026-10-20 起：legado 默认规则方言 class.x@tag.a@text 已移除，⛔ 不再兼容）
    q, take = _split_take(c)
    if not q:
        return []
    return _extract(root.css(q), take, base)

_PREFIXES = ("css:", "xpath:", "json:", "regex:", "text:", "link:", "url:", "js:")

def _norm_cands(core):
    # 🔴 || 的后续候选省写前缀时继承前一个的（css:a||b 的 b 也按 css 解析）—— 修
    out, inherit = [], ""
    for c in core.split("||"):
        c = c.strip()
        low = c.lower()
        if c.startswith("="):
            inherit = ""
        elif low.startswith(_PREFIXES):
            inherit = c[:c.index(":") + 1]
        elif inherit:
            c = inherit + c
        out.append(c)
    return out

def eval_expr(root, expr, base):
    parts = expr.split("##")
    core, filters = parts[0], parts[1:]
    vals = []
    for cand in _norm_cands(core):
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

if mode == "list":
    rules = _load_rules(vars.get("path") or "")
    print(json.dumps([{"key": str(k), "expr": str(e)} for k, e in rules], ensure_ascii=False))

elif mode == "probe":
    # 试跑推荐：N 个候选脚本对**同一份 HTML** 全求值（一次进程跑完，⛔ 不逐个启动）。
    # 站点探测：候选可带自己的 html_path / base（N 个候选 = N 份不同内容，缺省回落全局）。
    # 条数 = url 字段命中数（段间契约要求入口 / 中间段输出含 url），没有 url 时取最大字段命中数；
    # fill = 各字段 min(count, 条数)/条数 的平均 ⇒ 「必填字段非空率」。
    from parsel import Selector
    out = []
    for it in json.loads(vars.get("paths") or "[]"):
        try:
            rules = _load_rules(it.get("path") or "")
            hp = it.get("html_path") or vars.get("html_path") or ""
            base = it.get("base") or vars.get("base") or ""
            _JSON.clear()
            ROOT_HTML = open(hp, "r", encoding="utf-8").read()
            root = Selector(text=ROOT_HTML)
            fields, counts, urlc = [], [], 0
            for item in rules:
                k = str(item[0])
                e = str(item[1])
                try:
                    # 🔴 "(json: …)" 这类错误占位符不算条目（探测以「能解析出条目」为准）
                    vals = [v for v in eval_expr(root, e, base) if not (v.startswith("(") and v.endswith(")"))]
                    c = len(vals)
                    fields.append({"key": k, "count": c})
                except Exception as ex:
                    fields.append({"key": k, "count": 0, "error": str(ex)})
                counts.append(fields[-1]["count"])
                if k.lower() == "url":
                    urlc = fields[-1]["count"]
            cnt = urlc if urlc > 0 else (max(counts) if counts else 0)
            fill = 0.0
            if cnt > 0 and counts:
                fill = sum(min(c, cnt) for c in counts) / float(len(counts) * cnt)
            out.append({"name": it.get("name") or "", "count": cnt, "fill": round(fill, 3), "fields": fields})
        except Exception as ex:
            out.append({"name": it.get("name") or "", "count": 0, "fill": 0.0, "fields": [], "error": str(ex)})
    print(json.dumps(out, ensure_ascii=False))

elif mode == "rows":
    # 爬虫面板：按字段独立求值后**按索引合成行表**。
    # 段间契约：入口 / 中间段输出含 url；缺字段的格子补空串（⛔ 不按索引硬取 ⇒ 不错位）。
    from parsel import Selector
    rules = _load_rules(vars.get("path") or "")
    p = vars.get("html_path") or ""
    ROOT_HTML = open(p, "r", encoding="utf-8").read()
    root = Selector(text=ROOT_HTML)
    base = vars.get("base") or ""
    cols = []
    for item in rules:
        k = str(item[0])
        e = str(item[1])
        try:
            vals = eval_expr(root, e, base)
            cols.append({"key": k, "expr": e, "count": len(vals), "values": vals})
        except Exception as ex:
            cols.append({"key": k, "expr": e, "count": 0, "values": [], "error": str(ex)})
    n = 0
    for c in cols:
        if c["count"] > n:
            n = c["count"]
    n = min(n, 1000)
    rows = []
    for i in range(n):
        row = {}
        for c in cols:
            row[c["key"]] = c["values"][i] if i < c["count"] else ""
        rows.append(row)
    print(json.dumps({"columns": cols, "rows": rows, "total": n}, ensure_ascii=False))

elif mode == "step":
    # 期②下载：对**一份 HTML** 只求值指定字段（content / next），回**全量值**（⛔ 不截断、⛔ 不预览）。
    # C# 侧驱动跟进循环：next 的 URL、翻页停止条件、并发都在外面。
    from parsel import Selector
    rules = _load_rules(vars.get("path") or "")
    p = vars.get("html_path") or ""
    ROOT_HTML = open(p, "r", encoding="utf-8").read()
    root = Selector(text=ROOT_HTML)
    base = vars.get("base") or ""
    want = [k.strip() for k in (vars.get("keys") or "content").split(",") if k.strip()]
    fields, errors = {}, {}
    for item in rules:
        k = str(item[0])
        if want and k not in want:
            continue
        try:
            fields[k] = eval_expr(root, str(item[1]), base)
        except Exception as ex:
            errors[k] = str(ex)
    print(json.dumps({"fields": fields, "errors": errors}, ensure_ascii=False))

else:
    from parsel import Selector
    rules = _load_rules(vars.get("path") or "")
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
