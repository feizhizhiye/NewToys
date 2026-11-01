using System;
using System.Collections.Generic;

namespace NewToys.Reading.Models;

/// <summary>抓取管线的四步（错误定位 / 界面提示按它显示「哪一步、哪条规则」）。</summary>
public enum CrawlStep
{
    Search = 0,
    Detail = 1,
    Toc = 2,
    Content = 3,
}

/// <summary>一步失败的**可读原因**（⛔ 不把异常原文直接甩给用户）。</summary>
public sealed class StepError
{
    public StepError(CrawlStep step, string ruleName, string rule, string message)
    {
        Step = step;
        RuleName = ruleName;
        Rule = rule;
        Message = message;
    }

    public CrawlStep Step { get; }

    /// <summary>规则字段名（如 <c>ruleSearch.bookList</c>）。</summary>
    public string RuleName { get; }

    /// <summary>该字段的原文（截断到 120 字，够定位）。</summary>
    public string Rule { get; }

    /// <summary>可读原因：超时 / 404 / 编码 / 选择器空 / 需 JS / 规则不完整。</summary>
    public string Message { get; }

    public string StepText => Step switch
    {
        CrawlStep.Search => "搜索",
        CrawlStep.Detail => "详情",
        CrawlStep.Toc => "目录",
        _ => "正文",
    };

    /// <summary>界面单行提示：`搜索 · ruleSearch.bookList 出问题：选择器未命中`。</summary>
    public string Display => $"{StepText} · {RuleName}：{Message}";
}

/// <summary>一步的结果（值 + 可选错误；两者可以同时非空：部分成功）。</summary>
public sealed class StepResult<T>
{
    private StepResult(T? value, StepError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public StepError? Error { get; }

    public bool Ok => Error is null;

    public static StepResult<T> Success(T value) => new(value, null);

    public static StepResult<T> Fail(StepError error) => new(default, error);

    public static StepResult<T> Partial(T value, StepError error) => new(value, error);
}

/// <summary>搜索结果的一条。</summary>
public sealed class SearchItem
{
    public string Name { get; set; } = "";

    public string Author { get; set; } = "";

    public string CoverUrl { get; set; } = "";

    /// <summary>详情页绝对地址。</summary>
    public string BookUrl { get; set; } = "";

    public string Intro { get; set; } = "";

    public string Kind { get; set; } = "";

    public string LastChapter { get; set; } = "";

    /// <summary>命中这条的书源名（多源搜索结果合并后仍能看出出处）。</summary>
    public string SourceName { get; set; } = "";

    /// <summary>命中这条的书源地址（后续读目录 / 正文时据此找回源）。</summary>
    public string SourceUrl { get; set; } = "";

    public string AuthorText => string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;
}

/// <summary>详情页解析出的书目信息。</summary>
public sealed class BookDetail
{
    public string Name { get; set; } = "";

    public string Author { get; set; } = "";

    public string CoverUrl { get; set; } = "";

    public string Intro { get; set; } = "";

    public string Kind { get; set; } = "";

    public string LastChapter { get; set; } = "";

    /// <summary>目录页地址（规则没给就用详情页）。</summary>
    public string TocUrl { get; set; } = "";
}

/// <summary>章节目录的一条。</summary>
public sealed class Chapter
{
    public Chapter(string name, string url, int index)
    {
        Name = name;
        Url = url;
        Index = index;
    }

    public string Name { get; }

    /// <summary>章节页绝对地址（可能为空：纯标题目录）。</summary>
    public string Url { get; }

    public int Index { get; }

    public string IndexText => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>发现页的一个分类（<c>exploreUrl</c> 里的一项）。</summary>
public sealed class ExploreCategory
{
    public ExploreCategory(string name, string url)
    {
        Name = name;
        Url = url;
    }

    public string Name { get; }

    /// <summary>分类地址（可能含 <c>{{page}}</c>，绝对地址）。</summary>
    public string Url { get; }
}

/// <summary>书架里的一本书（<c>shelf</c> 表的一行）。</summary>
public sealed class ShelfBook
{
    public string BookUrl { get; set; } = "";

    public string SourceUrl { get; set; } = "";

    public string Name { get; set; } = "";

    public string Author { get; set; } = "";

    public string CoverUrl { get; set; } = "";

    public DateTimeOffset AddedAt { get; set; }

    public string AddedText => AddedAt == default
        ? ""
        : AddedAt.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public string AuthorText => string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;
}

/// <summary>书源在界面上的「上次使用」文本（列表 / 详情卡共用）。</summary>
public static class SourceTimeText
{
    public static string Of(DateTimeOffset? at)
    {
        if (at is null) return "还没用过";
        var local = at.Value.ToLocalTime();
        var span = DateTimeOffset.Now - local;
        if (span.TotalMinutes < 1) return "刚才";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前";
        return local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
