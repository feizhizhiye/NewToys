using System.Text.RegularExpressions;

namespace NewToys.Reading.Services;

/// <summary>正文净化规则（legado 的「替换净化」）：每行 正则##替换，只写正则 = 删除命中内容。（WHY: RD-016）</summary>
public static class ReplaceRuleService
{
    /// <summary>按行应用规则；<paramref name="rules"/> 为空原样返回。</summary>
    public static string Apply(string? text, string? rules)
    {
        var content = text ?? "";
        if (content.Length == 0 || string.IsNullOrWhiteSpace(rules)) return content;

        var result = content;
        foreach (var line in rules!.Split('\n'))
        {
            var (pattern, replacement) = Split(TrimComment(line));
            if (pattern.Length == 0) continue;

            try
            {
                result = Regex.Replace(result, pattern, replacement, RegexOptions.None, TimeSpan.FromSeconds(2));
            }
            catch { /* 单条正则写错 ⇒ 跳过，不影响其它规则 */ }
        }

        return result;
    }

    /// <summary>
    /// 规则行的可读校验（设置页保存时提示用）：返回错误说明，<c>null</c> = 这一行能用。
    /// </summary>
    public static string? Validate(string line)
    {
        var (pattern, _) = Split(TrimComment(line));
        if (pattern.Length == 0) return null;

        try
        {
            _ = new Regex(pattern);
            return null;
        }
        catch (System.Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>拆一行：<c>正则##替换</c>；没有 <c>##</c> 只有正则 ⇒ 替换为空（= 删除命中内容）。</summary>
    private static (string Pattern, string Replacement) Split(string line)
    {
        var text = (line ?? "").Trim();
        if (text.Length == 0) return ("", "");

        var index = text.IndexOf("##", System.StringComparison.Ordinal);
        return index < 0
            ? (text, "")
            : (text.Substring(0, index).Trim(), text.Substring(index + 2));
    }

    /// <summary>行首 <c>#</c> 开头的当注释（方便用户给规则分组写说明）。</summary>
    private static string TrimComment(string? line)
    {
        var text = (line ?? "").Trim();
        return text.StartsWith("#", System.StringComparison.Ordinal) ? "" : text;
    }
}
