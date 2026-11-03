using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NewToys.Reading.Controls;

namespace NewToys.Reading.Services;

/// <summary>一条 txt 章节识别规则。</summary>
public sealed class TxtChapterRule
{
    /// <summary>规则名（设置界面上显示）。</summary>
    public string Name { get; set; } = "";

    /// <summary>正则；按行匹配，命中即视为「新章标题行」。</summary>
    public string Pattern { get; set; } = "";

    public override string ToString() => Name;
}

/// <summary>
/// 本地文本读取与章节切分：编码嗅探 → 章节规则匹配 → 切片。
/// <para>
/// 🔴 章节是「整本书字符串上的区间」，不复制正文 —— 一本 5MB 的 txt 切成 3000 章也只占一份字符串。
/// </para>
/// <para>
/// 🔴 规则按顺序试，第一个命中的规则胜出；全部不命中时按约 3000 字等长切块（尽量落在空行处），
/// ⛔ 不要「识别不到就整本一章」—— 那样目录和进度都废了。
/// </para>
/// </summary>
public sealed class TxtBookService
{
    private static bool _encodingReady;

    /// <summary>等长切块的字数（识别不到章节时）。</summary>
    private const int FallbackChunk = 3000;

    /// <summary>内置规则（用户自定义规则排在前面优先匹配）。</summary>
    public static IReadOnlyList<TxtChapterRule> BuiltinRules { get; } = new List<TxtChapterRule>
    {
        new() { Name = "第 X 章 / 节 / 卷 / 回", Pattern = @"^\s*第\s*[0-9零一二三四五六七八九十百千万两]{1,10}\s*[章节回卷集部篇][\s、.．:：]?\s*\S.{0,40}$" },
        new() { Name = "Chapter N", Pattern = @"^\s*chapter\s+[0-9ivxlcdm]{1,6}\b.{0,40}$" },
        new() { Name = "数字编号标题", Pattern = @"^\s*[0-9]{1,4}\s*[、.．,，]\s*\S.{0,40}$" },
        new() { Name = "Markdown 标题（# / ## / ###）", Pattern = @"^\s{0,3}#{1,3}\s+\S.{0,60}$" },
        new() { Name = "分隔线（==== / ----）", Pattern = @"^\s*(={4,}|-{4,}|\*{4,})\s*$" },
    };

    /// <summary>读文件（BOM → 严格 UTF-8 → GB18030）。</summary>
    public string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            if (!_encodingReady)
            {
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _encodingReady = true; } catch { }
            }
            try { return Encoding.GetEncoding("gb18030").GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }

    /// <summary>把整本书切成章节；<paramref name="rules"/> 为空则只用内置规则。</summary>
    public IReadOnlyList<ReaderChapter> BuildChapters(string text, IEnumerable<TxtChapterRule>? rules)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<ReaderChapter>();

        var compiled = CompileRules(rules);

        // 逐行扫描，记下「章节标题行」的起点
        var starts = new List<(int Start, int BodyStart, string Title)>();
        var lineStart = 0;

        while (lineStart < text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = text.Length;

            var line = text.AsSpan(lineStart, lineEnd - lineStart).TrimEnd('\r').ToString();
            if (line.Length > 0 && line.Length <= 80)
            {
                foreach (var regex in compiled)
                {
                    if (!regex.IsMatch(line)) continue;

                    var title = Collapse(line.Trim());
                    starts.Add((lineStart, Math.Min(text.Length, lineEnd + 1), Truncate(title, 60)));
                    break;
                }
            }

            lineStart = lineEnd + 1;
        }

        if (starts.Count == 0) return BuildFallbackChapters(text);

        var chapters = new List<ReaderChapter>(starts.Count + 1);

        // 首个标题之前的内容（前言 / 作者的话）单独成章
        if (starts[0].Start > 0)
        {
            var preface = text.AsSpan(0, starts[0].Start).ToString().Trim();
            if (preface.Length > 0)
            {
                chapters.Add(new ReaderChapter
                {
                    Title = "卷首",
                    Start = 0,
                    Length = starts[0].Start,
                    BodyStart = 0,
                });
            }
        }

        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i].Start;
            var end = i + 1 < starts.Count ? starts[i + 1].Start : text.Length;
            chapters.Add(new ReaderChapter
            {
                Title = starts[i].Title,
                Start = start,
                Length = Math.Max(1, end - start),
                BodyStart = Math.Min(starts[i].BodyStart, end),
            });
        }

        return chapters;
    }

    /// <summary>规则试跑：返回命中的前 <paramref name="limit"/> 行（供设置界面预览，不改任何状态）。</summary>
    public IReadOnlyList<string> TestRule(string sample, string pattern, int limit = 30)
    {
        var hits = new List<string>();
        if (string.IsNullOrWhiteSpace(sample) || string.IsNullOrWhiteSpace(pattern)) return hits;

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)); }
        catch (ArgumentException ex) { return new[] { "⚠ 正则无效：" + ex.Message }; }

        foreach (var line in sample.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (hits.Count >= limit) break;
            if (line.Length == 0 || line.Length > 80) continue;
            try
            {
                if (regex.IsMatch(line)) hits.Add(line.Trim());
            }
            catch (RegexMatchTimeoutException) { return new[] { "⚠ 正则执行超时（可能有灾难性回溯）" }; }
        }

        return hits;
    }

    // ---------- 内部 ----------

    private static IReadOnlyList<Regex> CompileRules(IEnumerable<TxtChapterRule>? userRules)
    {
        var result = new List<Regex>();

        if (userRules is not null)
        {
            foreach (var rule in userRules)
                if (TryCompile(rule.Pattern) is { } regex) result.Add(regex);
        }

        foreach (var rule in BuiltinRules)
            if (TryCompile(rule.Pattern) is { } regex) result.Add(regex);

        return result;
    }

    private static Regex? TryCompile(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            return new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200));
        }
        catch { return null; }
    }

    /// <summary>识别不到章节：按字数切块，尽量落在空行 / 段落边界。</summary>
    private IReadOnlyList<ReaderChapter> BuildFallbackChapters(string text)
    {
        var chapters = new List<ReaderChapter>();
        var start = 0;
        var index = 1;

        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + FallbackChunk);

            if (end < text.Length)
            {
                // 往后找最近的空行，最多再找 400 字；找不到就硬切
                var searchTo = Math.Min(text.Length, end + 400);
                var breakAt = text.IndexOf("\n\n", end, searchTo - end, StringComparison.Ordinal);
                if (breakAt < 0) breakAt = text.LastIndexOf('\n', end, Math.Min(end - start, 400));
                if (breakAt > start) end = breakAt;
            }

            chapters.Add(new ReaderChapter
            {
                Title = "第 " + index.ToString(CultureInfo.InvariantCulture) + " 段",
                Start = start,
                Length = Math.Max(1, end - start),
                BodyStart = start,
            });

            start = end;
            index++;
        }

        return chapters;
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
