using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Jint;

namespace NewToys.Reading.Services;

/// <summary>
/// 求值器的 JS 兼栈层：<c>@js:</c> / <c>&lt;js&gt;</c> 片段 + <c>java.*</c> 桥。
/// <para>
/// 🔴 桥的边界只有 <b>string 进 / string 出</b>（见 <see cref="JsBridge"/>）：元素集走 JSON 索引数组、
/// 连接走 JSON 快照。Jint 的 interop 参数推断各版本差异大，统一 string 是唯一不会踩坑的接缝。
/// </para>
/// <para>
/// 🔴 一次求值一个 <see cref="Engine"/>，用完丢弃 —— 书源之间不会因全局变量串味；异常一律包死转成
/// <see cref="RuleContext.Errors"/> 的一行，⛔ 不冒到界面。
/// </para>
/// </summary>
public sealed partial class RuleEvaluator
{
    private readonly Func<string, string, string, string>? _fetch;

    /// <summary>无网络桥（单测 / 纯字符串 JS 用）。</summary>
    public RuleEvaluator()
    {
    }

    /// <summary>
    /// <paramref name="fetch"/>：<c>(url, method, body) → 响应正文</c>，由抓取层注入。
    /// 🔴 必须是**同步返回**的委托（内部自己 <c>Task.Run</c>），JS 侧是同步语义。
    /// </summary>
    public RuleEvaluator(Func<string, string, string, string> fetch) => _fetch = fetch;

    /// <summary>单段 JS 的最长执行时间（书源脚本死循环的兜底）。</summary>
    private static readonly TimeSpan JsTimeout = TimeSpan.FromSeconds(8);

    /// <summary>执行一段 JS：<paramref name="result"/> 注入为同名变量，返回最后表达式值（undefined 时回退取该变量）。</summary>
    private (bool Ok, string Text) EvaluateJs(string code, string result, RuleContext ctx)
    {
        if (string.IsNullOrWhiteSpace(code)) return (true, result);

        try
        {
            var bridge = new JsBridge(this, ctx, _fetch);

            // 超时兜底：书源脚本里 `while(true) { … }` 这类轮询写法并不罕见，
            // 没有上限就是永久卡死在求值这一步（外部只能杀进程）。
            var engine = new Engine(o => o.TimeoutInterval(JsTimeout));
            bridge.Bind(engine);

            engine.SetValue("result", result);
            engine.SetValue("baseUrl", ctx.BaseUrl);
            engine.SetValue("sourceUrl", Var(ctx, "sourceUrl"));
            engine.SetValue("key", Var(ctx, "keyUnescaped"));
            engine.SetValue("page", Var(ctx, "page"));

            var value = engine.Evaluate(code);
            var text = value.ToString() ?? "";

            if (text is "undefined" or "null")
            {
                // 代码以语句结尾（`var x = ...;`）时取全局 result（前段结果 / 代码自己改写过）
                var fallback = engine.GetValue("result").ToString() ?? "";
                text = fallback is "undefined" or "null" ? "" : fallback;
            }

            return (true, text);
        }
        catch (Exception ex)
        {
            Report(ctx, "JS 执行失败：" + FirstLine(ex.Message), code);
            return (false, "");
        }
    }

    /// <summary><c>{{java.xxx(...)}}</c> 这类表达式插值；算不出来返回 null（调用方保留原文并标记需 JS）。</summary>
    private string? EvalJsExpression(string code, RuleContext ctx)
    {
        var (ok, text) = EvaluateJs(code, "", ctx);
        return ok ? text : null;
    }

    /// <summary><see cref="EvalJsExpression"/> 的对外入口（请求地址 / 请求体的插值走这里）。</summary>
    public string? EvaluateExpression(string code, RuleContext ctx) => EvalJsExpression(code, ctx);

    /// <summary>
    /// 执行整段 JS 并取字符串结果（发现页 <c>@js:</c> 动态分类表用：整段就是 JS，返回「分类::地址」列表串）。
    /// <para>失败返回 <c>null</c>（调用方跳过该段，⛔ 不阻断其它分类）。</para>
    /// </summary>
    public string? EvaluateJsBlock(string code, RuleContext ctx)
    {
        var (ok, text) = EvaluateJs(code, "", ctx);
        return ok ? text : null;
    }

    private static string Var(RuleContext ctx, string name)
        => ctx.Vars.TryGetValue(name, out var value) ? value : "";

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(new[] { '\r', '\n' });
        var line = index < 0 ? text : text.Substring(0, index);
        return line.Length <= 160 ? line : line.Substring(0, 160) + "…";
    }

    /// <summary>把求值结果拍平成 JS 里的 <c>result</c> 字符串：多值按换行连接（与 legado 一致）。</summary>
    private static string JoinTexts(RuleValue value)
    {
        var texts = value.Kind switch
        {
            RuleValueKind.Texts => value.Texts,
            RuleValueKind.Nodes => value.Nodes.Select(node => node.TextContent.Trim()).ToList(),
            RuleValueKind.Json => value.JsonItems.Select(ScalarText).ToList(),
            _ => new List<string>(),
        };

        return string.Join("\n", texts.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    // ==================== java.* 桥 ====================

    /// <summary>
    /// <c>java.*</c> 的实现。所有成员都是「string 参数 → string 返回值」，prelude 里的 JS 负责重载与对象包装。
    /// </summary>
    private sealed class JsBridge
    {
        private readonly RuleEvaluator _owner;
        private readonly RuleContext _ctx;
        private readonly Func<string, string, string, string>? _fetch;
        private readonly List<IElement> _pool = new();
        private int _lastStatus = 200;

        public JsBridge(RuleEvaluator owner, RuleContext ctx, Func<string, string, string, string>? fetch)
        {
            _owner = owner;
            _ctx = ctx;
            _fetch = fetch;
        }

        public void Bind(Engine engine)
        {
            engine.SetValue("__s_getstring", new Func<string, string>(rule => _owner.EvaluateText(rule, _ctx)));
            engine.SetValue("__s_getelements", new Func<string, string>(rule => Pool(_owner.Evaluate(rule, _ctx).Nodes)));

            engine.SetValue("__s_eltag", new Func<string, string>(i => Element(i)?.LocalName ?? ""));
            engine.SetValue("__s_eltext", new Func<string, string>(i => Element(i) is { } e ? Clean(e.TextContent) : ""));
            engine.SetValue("__s_elown", new Func<string, string>(i => Element(i) is { } e ? Clean(DirectText(e)) : ""));
            engine.SetValue("__s_elhtml", new Func<string, string>(i => Element(i)?.InnerHtml ?? ""));
            engine.SetValue("__s_elouter", new Func<string, string>(i => Element(i)?.OuterHtml ?? ""));
            engine.SetValue("__s_elattr", new Func<string, string, string>(
                (i, name) => Element(i)?.GetAttribute(name) ?? ""));
            engine.SetValue("__s_elabs", new Func<string, string, string>(
                (i, name) => _ctx.AbsoluteUrl(Element(i)?.GetAttribute(name))));
            engine.SetValue("__s_elsel", new Func<string, string, string>((i, css) =>
            {
                var node = Element(i);
                if (node is null) return "[]";
                try { return Pool(node.QuerySelectorAll(RuleSyntax.ToCss(css))); }
                catch { return "[]"; }
            }));

            engine.SetValue("__s_put", new Func<string, string, string>((key, value) => { _ctx.Store[key] = value; return ""; }));
            engine.SetValue("__s_get", new Func<string, string>(key => _ctx.Store.TryGetValue(key, out var value) ? value : ""));

            engine.SetValue("__s_ajax", new Func<string, string, string, string>(Ajax));
            engine.SetValue("__s_http", new Func<string, string, string, string>(HttpSnapshot));

            engine.SetValue("__s_md5", new Func<string, string>(Md5));
            engine.SetValue("__s_b64", new Func<string, string>(Base64Decode));
            engine.SetValue("__s_b64enc", new Func<string, string>(text => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))));
            engine.SetValue("__s_aes", new Func<string, string, string, string, string>(AesDecode));
            engine.SetValue("__s_encodeuri", new Func<string, string, string>((text, _) => Uri.EscapeDataString(text)));
            engine.SetValue("__s_timeformat", new Func<string, string>(TimeFormat));
            engine.SetValue("__s_tonum", new Func<string, string>(ToNumChapter));
            engine.SetValue("__s_t2s", new Func<string, string>(TraditionalToSimplified.Convert));
            engine.SetValue("__s_log", new Func<string, string>(text =>
            {
                Debug.WriteLine("[reading/js] " + text);
                return "";
            }));

            engine.Execute(Prelude);
        }

        // ---------- 元素池 ----------

        private string Pool(IEnumerable<IElement> nodes)
        {
            var indexes = new List<int>();
            foreach (var node in nodes)
            {
                _pool.Add(node);
                indexes.Add(_pool.Count - 1);
            }
            return "[" + string.Join(",", indexes) + "]";
        }

        private IElement? Element(string index)
            => int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
               && at >= 0 && at < _pool.Count
                ? _pool[at]
                : null;

        // ---------- 请求 ----------

        private string Ajax(string url, string method, string body)
        {
            if (_fetch is null)
            {
                _ctx.Errors.Add("java.ajax 不可用（未接抓取层）→ " + url);
                return "";
            }

            try
            {
                var target = _ctx.AbsoluteUrl(url);
                var text = _fetch(target, method, body);
                _lastStatus = string.IsNullOrEmpty(text) ? 0 : 200;
                return text;
            }
            catch (Exception ex)
            {
                _ctx.Errors.Add("java.ajax 失败：" + FirstLine(ex.Message) + " → " + url);
                _lastStatus = 0;
                return "";
            }
        }

        private string HttpSnapshot(string url, string method, string body)
        {
            var text = Ajax(url, method, body);
            return JsonSerializer.Serialize(new { code = _lastStatus, body = text });
        }

        // ---------- 编解码 ----------

        private static string Md5(string text)
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string Base64Decode(string text)
        {
            try
            {
                var normalized = text.Trim().Replace('-', '+').Replace('_', '/');
                var padding = normalized.Length % 4;
                if (padding > 0) normalized += new string('=', 4 - padding);
                return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            }
            catch { return ""; }
        }

        /// <summary><c>aesBase64DecodeToString(data, key, transformation, iv)</c>：密钥长度不足按 UTF-8 字节补零。</summary>
        private static string AesDecode(string data, string key, string transformation, string iv)
        {
            try
            {
                var cipher = Base64Decode(data);
                if (cipher.Length == 0) return "";

                var bytes = Encoding.UTF8.GetBytes(cipher);
                using var aes = Aes.Create();

                var mode = transformation.ToUpperInvariant();
                aes.Mode = mode.Contains("CBC") ? CipherMode.CBC
                    : mode.Contains("CFB") ? CipherMode.CFB
                    : CipherMode.ECB;
                aes.Padding = mode.Contains("NOPADDING") ? PaddingMode.None : PaddingMode.PKCS7;

                var keyBytes = Encoding.UTF8.GetBytes(key);
                aes.Key = keyBytes.Length is 16 or 24 or 32 ? keyBytes : Pad(keyBytes, 16);

                var ivBytes = string.IsNullOrEmpty(iv) ? null : Encoding.UTF8.GetBytes(iv);
                if (aes.Mode != CipherMode.ECB) aes.IV = ivBytes is { Length: 16 } ? ivBytes : new byte[16];

                using var decryptor = aes.CreateDecryptor();
                var plain = decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return ""; }
        }

        private static byte[] Pad(byte[] source, int size)
        {
            var result = new byte[size];
            Array.Copy(source, result, Math.Min(source.Length, size));
            return result;
        }

        // ---------- 时间 / 章节号 ----------

        private static string TimeFormat(string value)
        {
            if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return value;

            var stamp = number > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(number)
                : DateTimeOffset.FromUnixTimeSeconds(number);

            return stamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static readonly string[] ChineseDigits =
        {
            "零", "一", "二", "三", "四", "五", "六", "七", "八", "九",
        };

        /// <summary><c>toNumChapter("第１２３章")</c> ⇒ <c>"123"</c>；纯数字原样返回。</summary>
        private static string ToNumChapter(string value)
        {
            var text = value.Trim();
            if (text.Length == 0) return "";

            var digits = new StringBuilder();
            foreach (var ch in text)
            {
                if (ch is >= '0' and <= '9') digits.Append(ch);
                else if (ch is >= '０' and <= '９') digits.Append((char)('0' + (ch - '０')));
            }
            if (digits.Length > 0) return digits.ToString();

            return ChineseToNumber(text).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>中文数字（<c>一千零二十</c>）转整数；不含中文数字返回 0。</summary>
        private static int ChineseToNumber(string text)
        {
            var total = 0;
            var section = 0;
            var number = 0;
            var seen = false;

            foreach (var ch in text)
            {
                var digit = Array.IndexOf(ChineseDigits, ch.ToString());
                if (digit >= 0)
                {
                    number = digit;
                    seen = true;
                    continue;
                }

                var unit = ch switch
                {
                    '十' => 10,
                    '百' => 100,
                    '千' => 1000,
                    '万' => 10000,
                    _ => 0,
                };
                if (unit == 0) continue;

                seen = true;
                if (unit == 10000)
                {
                    total += (section + (number == 0 ? 0 : number)) * 10000;
                    section = 0;
                }
                else
                {
                    section += (number == 0 ? 1 : number) * unit;
                }
                number = 0;
            }

            return seen ? total + section + number : 0;
        }

        // ---------- prelude ----------

        private const string Prelude = """
            var java = (function () {
                function el(i) {
                    var k = String(i);
                    return {
                        __i: k,
                        tagName: __s_eltag(k),
                        text: function () { return __s_eltext(k); },
                        ownText: function () { return __s_elown(k); },
                        html: function () { return __s_elhtml(k); },
                        outerHtml: function () { return __s_elouter(k); },
                        attr: function (name) { return __s_elattr(k, String(name)); },
                        absUrl: function (name) { return __s_elabs(k, String(name)); },
                        select: function (sel) { return JSON.parse(__s_elsel(k, String(sel))).map(el); },
                        toString: function () { return __s_eltext(k); }
                    };
                }
                function elements(raw) { return JSON.parse(raw).map(el); }

                return {
                    ajax: function (url, options) {
                        var method = "", body = "";
                        if (options) {
                            if (typeof options === "string") { method = "POST"; body = options; }
                            else {
                                method = options.method || "";
                                body = options.body === undefined || options.body === null ? "" : options.body;
                            }
                        }
                        return __s_ajax(String(url), String(method), String(body));
                    },
                    post: function (url, body) {
                        return __s_ajax(String(url), "POST", body === undefined || body === null ? "" : String(body));
                    },
                    connect: function (url) {
                        var response = JSON.parse(__s_http(String(url), "GET", ""));
                        return {
                            getResponseCode: function () { return response.code; },
                            getInputStream: function () { return response.body; },
                            getContent: function () { return response.body; }
                        };
                    },
                    getString: function (rule, isRule) {
                        return isRule === false ? String(rule) : __s_getstring(String(rule));
                    },
                    getElement: function (rule) {
                        var list = elements(__s_getelements(String(rule)));
                        return list.length > 0 ? list[0] : null;
                    },
                    getElements: function (rule) { return elements(__s_getelements(String(rule))); },
                    put: function (key, value) {
                        __s_put(String(key), value === undefined || value === null ? "" : String(value));
                    },
                    get: function (key) { return __s_get(String(key)); },
                    md5Encode: function (text) { return __s_md5(String(text)); },
                    md5: function (text) { return __s_md5(String(text)); },
                    base64Decode: function (text) { return __s_b64(String(text)); },
                    base64Encode: function (text) { return __s_b64enc(String(text)); },
                    aesBase64DecodeToString: function (data, key, transformation, iv) {
                        return __s_aes(String(data), String(key),
                            String(transformation || "AES/ECB/PKCS5Padding"),
                            iv === undefined || iv === null ? "" : String(iv));
                    },
                    encodeURI: function (text, encoding) { return __s_encodeuri(String(text), String(encoding || "UTF-8")); },
                    timeFormat: function (stamp) { return __s_timeformat(String(stamp)); },
                    timeZoneFormat: function (stamp) { return __s_timeformat(String(stamp)); },
                    toNumChapter: function (name) { return __s_tonum(String(name)); },
                    log: function (text) { __s_log(String(text)); },
                    toast: function (text) { __s_log(String(text)); },
                    longToast: function (text) { __s_log(String(text)); },
                    t2s: function (text) { return __s_t2s(String(text)); },
                    s2t: function (text) { return String(text); },
                    webview: function () { __s_log("桌面端不支持 WebView"); return ""; },
                    startBrowserAwait: function () { __s_log("桌面端不启动浏览器"); return ""; }
                };
            })();

            var console = { log: function (text) { __s_log(String(text)); } };
            """;
    }
}
