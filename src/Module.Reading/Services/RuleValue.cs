using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AngleSharp.Dom;

namespace NewToys.Reading.Services;

/// <summary>规则求值结果的三态（节点 / 文本 / JSON）—— ⛔ 不能一律压成字符串。</summary>
public enum RuleValueKind
{
    Empty = 0,
    Texts = 1,
    Nodes = 2,
    Json = 3,
}

/// <summary>
/// 求值结果的联合体。
/// <para>
/// 🔴 为什么必须分态：书单 / 目录这类规则要拿到**节点集**，后续字段规则在**每个节点内**分别求值；
/// 压成 <c>List&lt;string&gt;</c> 就只能按索引回填，条目错位（既有抓取引擎的返工根因）。
/// </para>
/// <para>JSON 源同理会拿到**元素集**，后续 <c>@json:$.xxx</c> 相对该元素求值。</para>
/// </summary>
public sealed class RuleValue
{
    public static readonly RuleValue Empty = new();

    private RuleValue()
    {
        Kind = RuleValueKind.Empty;
        Texts = Array.Empty<string>();
        Nodes = Array.Empty<IElement>();
        JsonItems = Array.Empty<JsonElement>();
    }

    private RuleValue(RuleValueKind kind, IReadOnlyList<string> texts, IReadOnlyList<IElement> nodes, IReadOnlyList<JsonElement> json)
    {
        Kind = kind;
        Texts = texts;
        Nodes = nodes;
        JsonItems = json;
    }

    public RuleValueKind Kind { get; }

    public IReadOnlyList<string> Texts { get; }

    public IReadOnlyList<IElement> Nodes { get; }

    public IReadOnlyList<JsonElement> JsonItems { get; }

    public static RuleValue FromText(string? text)
        => string.IsNullOrWhiteSpace(text) ? Empty : new(RuleValueKind.Texts, new[] { text! }, Array.Empty<IElement>(), Array.Empty<JsonElement>());

    public static RuleValue FromTexts(IEnumerable<string?> texts)
    {
        var list = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
        return list.Count == 0 ? Empty : new(RuleValueKind.Texts, list, Array.Empty<IElement>(), Array.Empty<JsonElement>());
    }

    public static RuleValue FromNodes(IEnumerable<IElement> nodes)
    {
        var list = nodes.ToList();
        return list.Count == 0 ? Empty : new(RuleValueKind.Nodes, Array.Empty<string>(), list, Array.Empty<JsonElement>());
    }

    public static RuleValue FromJson(IEnumerable<JsonElement> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? Empty : new(RuleValueKind.Json, Array.Empty<string>(), Array.Empty<IElement>(), list);
    }

    /// <summary>首个非空文本（<c>@json:</c> 的标量结果也走这里）。</summary>
    public string FirstText => Texts.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
}
