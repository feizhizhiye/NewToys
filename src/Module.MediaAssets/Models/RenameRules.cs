using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace NewToys.MediaAssets.Models;

/// <summary>编号在名字里的落点。</summary>
public enum RenameNumberPlacement
{
    /// <summary>用「前缀 + 序号 + 后缀」**替换**原名（例：<c>旅途_01</c>）。</summary>
    Replace,

    /// <summary>序号放在原名前面（例：<c>旅途_01假日</c>）。</summary>
    Before,

    /// <summary>序号放在原名后面（例：<c>假日_01</c>）。</summary>
    After
}

/// <summary>大小写变换。</summary>
public enum RenameCaseMode
{
    /// <summary>不动。</summary>
    Keep,

    /// <summary>全小写。</summary>
    Lower,

    /// <summary>全大写。</summary>
    Upper,

    /// <summary>首字母大写。</summary>
    Title
}

/// <summary>
/// 批量重命名的**规则集**：纯数据 + 纯文本变换（⛔ 不碰磁盘、⛔ 不查重 —— 查重与序号避让在调用方）。
/// <para>
/// 变换顺序**固定**：① 查找替换 → ② 大小写 → ③ 前缀 / 编号 / 后缀组装。固定顺序才可复现、才能在预览里说清
/// 「为什么改成了这个名字」；⛔ 不按开关先后、⛔ 不叠加多轮。
/// </para>
/// </summary>
public sealed class RenameRules
{
    /// <summary>查找内容；空 = 不做替换。</summary>
    public string FindText { get; set; } = "";

    /// <summary>替换成；配合 <see cref="UseRegex"/> 时可用 <c>$1</c> 分组引用。</summary>
    public string ReplaceText { get; set; } = "";

    /// <summary>查找按正则解释（默认关：普通字符串替换，区分大小写）。</summary>
    public bool UseRegex { get; set; }

    /// <summary>前缀。</summary>
    public string Prefix { get; set; } = "";

    /// <summary>后缀（落在扩展名之前）。</summary>
    public string Suffix { get; set; } = "";

    /// <summary>是否插入序号。</summary>
    public bool Numbering { get; set; }

    /// <summary>起始编号。</summary>
    public int NumberStart { get; set; } = 1;

    /// <summary>步长。</summary>
    public int NumberStep { get; set; } = 1;

    /// <summary>位数（不足左补 0）。</summary>
    public int NumberDigits { get; set; } = 2;

    /// <summary>序号与名字的连接符。</summary>
    public string NumberSeparator { get; set; } = "_";

    /// <summary>序号落点。</summary>
    public RenameNumberPlacement NumberPlacement { get; set; } = RenameNumberPlacement.Before;

    /// <summary>大小写。</summary>
    public RenameCaseMode CaseMode { get; set; } = RenameCaseMode.Keep;

    /// <summary>保留原扩展名（默认开 ⇒ 规则只作用在主名上）。</summary>
    public bool KeepExtension { get; set; } = true;

    /// <summary>
    /// 把规则应用到第 <paramref name="index"/> 个文件（0 = 第一个，编号按它推进）。
    /// 返回新文件名（含扩展名）；<c>Error</c> 非空 = 这个名字不能要（空名 / 非法字符 / 正则错）。
    /// </summary>
    public (string NewName, string? Error) Apply(string fileName, int index)
    {
        var oldBase = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var body = KeepExtension ? oldBase : oldBase + ext;

        if (FindText.Length > 0)
        {
            if (UseRegex)
            {
                try
                {
                    body = Regex.Replace(body, FindText, ReplaceText ?? "", RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    return ("", $"正则写错了：{ex.Message}");
                }
            }
            else
            {
                body = body.Replace(FindText, ReplaceText ?? "", StringComparison.Ordinal);
            }
        }

        body = CaseMode switch
        {
            RenameCaseMode.Lower => body.ToLowerInvariant(),
            RenameCaseMode.Upper => body.ToUpperInvariant(),
            RenameCaseMode.Title => CapitalizeFirst(body),
            _ => body
        };

        var number = Numbering ? BuildNumber(NumberStart + index * NumberStep) : "";
        var name = NumberPlacement switch
        {
            RenameNumberPlacement.Replace => Prefix + number + Suffix,
            RenameNumberPlacement.After => Prefix + body + number + Suffix,
            _ => Prefix + number + body + Suffix
        };

        if (KeepExtension && ext.Length > 0) name += ext;
        name = name.Trim();

        if (name.Length == 0) return ("", "改出来是空名字");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar))
            return ("", "含非法字符");

        return (name, null);
    }

    /// <summary>按位数左补 0 的序号（带连接符）。</summary>
    private string BuildNumber(int value)
        => (NumberSeparator ?? "") + value.ToString(new string('0', Math.Clamp(NumberDigits, 1, 6)), CultureInfo.InvariantCulture);

    private static string CapitalizeFirst(string text)
        => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
