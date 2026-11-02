using System.Text.Json.Serialization;

namespace NewToys.Reading.Models;

/// <summary>
/// 阅读样式：排版参数的一份**可交换快照**（内置预设 / 导出文件 / 导入文件都是这个形状）。
/// <para>
/// 🔴 字段为 <c>null</c> = 「这一项不参与导入」，保留用户当前值 —— 手写的样式文件只写两三项也能用。
/// 预设与导出文件则每格都填，导入即整体覆盖。
/// </para>
/// </summary>
public sealed class ReaderStyle
{
    /// <summary>样式名（预设 / 导出文件都带；导入后显示在状态栏）。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; set; }

    /// <summary>正文字号（px）。</summary>
    [JsonPropertyName("fontSize")]
    public int? FontSize { get; set; }

    /// <summary>行距倍数。</summary>
    [JsonPropertyName("lineSpacing")]
    public double? LineSpacing { get; set; }

    /// <summary>正文最大宽度（px）。</summary>
    [JsonPropertyName("contentWidth")]
    public int? ContentWidth { get; set; }

    /// <summary>左右留白（px）。</summary>
    [JsonPropertyName("padding")]
    public int? Padding { get; set; }

    /// <summary>配色档：<c>light</c> / <c>sepia</c> / <c>dark</c>。</summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    /// <summary>自定义背景色（<c>#RRGGBB</c>）；空串 = 清掉自定义、跟配色档走。</summary>
    [JsonPropertyName("background")]
    public string? Background { get; set; }

    /// <summary>自定义前景色。</summary>
    [JsonPropertyName("foreground")]
    public string? Foreground { get; set; }

    /// <summary>首行缩进（全角空格个数）。</summary>
    [JsonPropertyName("indent")]
    public int? Indent { get; set; }

    /// <summary>段间距（段与段之间的空行数）。</summary>
    [JsonPropertyName("paragraphSpacing")]
    public int? ParagraphSpacing { get; set; }
}
