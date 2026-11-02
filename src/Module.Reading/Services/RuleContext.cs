using System;
using System.Collections.Generic;
using System.Text.Json;
using AngleSharp.Dom;

namespace NewToys.Reading.Services;

/// <summary>
/// 一次规则求值的上下文：**根**（文档节点 / 条目节点 / JSON 元素）+ 变量表 + <c>@put</c> 字典。
/// <para>
/// 🔴 条目字段（书名 / 作者 / 章节名）必须用<b>条目自己的节点</b>当根 —— 见
/// <see cref="ForElement"/> / <see cref="ForJson"/>；⛔ 不许拿整页求值再按索引拼。
/// </para>
/// <para>
/// 🔴 变量表 / <c>@put</c> 字典 / 告警列表在派生上下文之间**共享同一份引用**：
/// 同一条规则里 <c>@put</c> 写的值要能被后续字段读到，告警也要汇总回最外层。
/// </para>
/// </summary>
public sealed class RuleContext
{
    private readonly Dictionary<string, string> _store;
    private readonly Dictionary<string, string> _vars;
    private readonly List<string> _errors;

    public RuleContext()
        : this(new Dictionary<string, string>(StringComparer.Ordinal),
               new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
               new List<string>())
    {
    }

    private RuleContext(Dictionary<string, string> store, Dictionary<string, string> vars, List<string> errors)
    {
        _store = store;
        _vars = vars;
        _errors = errors;
    }

    /// <summary>HTML 根（文档根或某条目节点）；JSON 源为空。</summary>
    public IElement? Root { get; private init; }

    /// <summary>JSON 根（<c>@json:</c> / <c>$.xxx</c> 的相对基准）；HTML 源为空。</summary>
    public JsonElement? JsonRoot { get; private init; }

    /// <summary>响应原文（JSON 规则与 <c>{{}}</c> 之外的兜底用）。</summary>
    public string RawText { get; private init; } = "";

    /// <summary>当前页地址（相对链接转绝对、<c>{{baseUrl}}</c> 用）。</summary>
    public string BaseUrl { get; private init; } = "";

    /// <summary>变量表（<c>key</c> / <c>page</c> / <c>baseUrl</c> 等）。</summary>
    public Dictionary<string, string> Vars => _vars;

    /// <summary><c>@put:</c> / <c>@get:</c> 的字典。</summary>
    public IDictionary<string, string> Store => _store;

    /// <summary>求值过程中的可读告警（未知前缀 / JS 失败 / 选择器空）。</summary>
    public List<string> Errors => _errors;

    /// <summary>以某个节点为新根（条目内求值）。</summary>
    public RuleContext ForElement(IElement root) => new(_store, _vars, _errors)
    {
        Root = root,
        RawText = RawText,
        BaseUrl = BaseUrl,
    };

    /// <summary>以某个 JSON 元素为新根（JSON 源条目内求值）。</summary>
    public RuleContext ForJson(JsonElement root) => new(_store, _vars, _errors)
    {
        JsonRoot = root,
        RawText = RawText,
        BaseUrl = BaseUrl,
    };

    /// <summary>换文档 / 换地址（抓下一段时复用变量表与 <c>@put</c> 字典）。</summary>
    public RuleContext WithDocument(IElement? root, string baseUrl, string rawText) => new(_store, _vars, _errors)
    {
        Root = root,
        RawText = rawText,
        BaseUrl = baseUrl,
    };

    /// <summary>把 <paramref name="value"/> 按当前文档地址转成绝对 URL（解析不了就原样返回）。</summary>
    public string AbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value!.Trim();
        if (text.StartsWith("//", StringComparison.Ordinal)) return "https:" + text;
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute)) return absolute.ToString();
        if (string.IsNullOrEmpty(BaseUrl)) return text;
        return Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri)
               && Uri.TryCreate(baseUri, text, out var merged)
            ? merged.ToString()
            : text;
    }
}
