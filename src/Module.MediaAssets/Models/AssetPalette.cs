namespace NewToys.MediaAssets.Models;

/// <summary>
/// 标签 / 分类 / 集合的**数据色板**（NT-018）—— 仿视频工具 <c>TagPalette</c> 先例（`Services/TagPalette.cs`）：
/// 颜色是**数据**（可落库、可被用户改），⛔ **不走主题键**、⛔ **不在 XAML 里写死色值**。
/// <para>取色按名称做 FNV-1a 稳定散列 → **同名同色**（重启、换页、换机器都不变）。</para>
/// </summary>
public static class AssetPalette
{
    /// <summary>8 色固定板（与视频工具 `TagPalette` 同一批色调，保持观感一致）。</summary>
    public static readonly string[] Colors =
    {
        "#4C8DFF", "#3FBF8F", "#E0A63C", "#E05C6E",
        "#9B6BE0", "#39B7C9", "#D2699B", "#7E8CA8"
    };

    /// <summary>按名称稳定取色（⛔ 别用 `GetHashCode()` —— .NET 每进程随机化，重启就变色）。</summary>
    public static string For(string? key)
    {
        var s = key ?? "";
        uint h = 2166136261;
        foreach (var ch in s) { h ^= ch; h *= 16777619; }
        return Colors[(int)(h % (uint)Colors.Length)];
    }

    /// <summary>库里存了颜色就用库里的（用户改过色），否则按名字取。</summary>
    public static string Resolve(string? stored, string? name)
        => string.IsNullOrWhiteSpace(stored) ? For(name) : stored!;
}

/// <summary>
/// 色板上的一个色块（详情卡里点一下就改色）。
/// <para>⛔ XAML 不能直接拿字符串当背景 → 这里把 hex 转成画刷，界面绑 <see cref="Brush"/>。</para>
/// </summary>
public class ColorSwatch
{
    public ColorSwatch(string hex)
    {
        Hex = hex;
        Brush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
    }

    /// <summary>#RRGGBB（落库 / 作命令参数用）。</summary>
    public string Hex { get; }

    /// <summary>色块画刷。</summary>
    public Avalonia.Media.IBrush Brush { get; }

    public override string ToString() => Hex;
}
