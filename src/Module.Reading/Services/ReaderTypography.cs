using System;
using System.Collections.Generic;
using System.Text;

namespace NewToys.Reading.Services;

/// <summary>
/// 正文排版：把抓回来的纯文本按「首行缩进 + 段间距」重排。
/// <para>
/// 🔴 只吐字符串给 <c>TextViewer</c>（它就是一个 <c>SelectableTextBlock</c>，
/// 没有段落级样式）⇒ 缩进 = 段首补全角空格，段间距 = 段间多插空行。⛔ 不为此改 NewToys.UI。
/// </para>
/// </summary>
public static class ReaderTypography
{
    /// <summary>全角空格：中文排版的首行缩进单位（半角空格在等宽判断里宽度不稳）。</summary>
    private const char IdeographicSpace = '　';

    /// <summary>
    /// 重排正文：<paramref name="indent"/> = 每段首行缩进几个全角空格；
    /// <paramref name="paragraphSpacing"/> = 段与段之间插几个空行。
    /// </summary>
    public static string Apply(string? text, int indent, int paragraphSpacing)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var prefix = indent > 0 ? new string(IdeographicSpace, indent) : "";
        var gap = paragraphSpacing < 0 ? 0 : paragraphSpacing;
        var separator = new string('\n', gap + 1);

        var builder = new StringBuilder(text.Length + 64);
        var paragraphs = SplitParagraphs(text);

        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0) builder.Append(separator);
            builder.Append(prefix).Append(paragraphs[i]);
        }

        return builder.ToString();
    }

    /// <summary>按行切成段落：去每行首尾空白、丢掉空行（原文里连续空行不保留，段间距由参数统一给）。</summary>
    private static List<string> SplitParagraphs(string text)
    {
        var list = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) list.Add(trimmed);
        }

        return list;
    }
}
