using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace NewToys.UI.Markup;

/// <summary>把 Shell/App.axaml 的 Icon* 几何键变成图标控件。</summary>
public sealed class GlyphExtension : MarkupExtension
{
    public GlyphExtension()
    {
    }

    public GlyphExtension(string key) => Key = key;

    /// <summary>几何资源键（如 <c>IconOpen</c>）；键不存在 ⇒ 无图标，不抛异常。</summary>
    public string Key { get; set; } = "";

    /// <summary>边长，默认 14：与 12~13px 文字并排时不撑高所在行。</summary>
    public double Size { get; set; } = 14;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        (object?)Glyph.Create(Key, Size) ?? new TextBlock();
}

/// <summary>图标 + 文字合成一个内容控件，给 Button.Content / MenuItem.Header 这类 只能写一处内容的位置用（按钮本身没有独立图标位）。</summary>
public sealed class GlyphTextExtension : MarkupExtension
{
    public GlyphTextExtension()
    {
    }

    public GlyphTextExtension(string key) => Key = key;

    public GlyphTextExtension(string key, string text)
    {
        Key = key;
        Text = text;
    }

    /// <summary>几何资源键；键不存在 ⇒ 只显示文字。</summary>
    public string Key { get; set; } = "";

    /// <summary>图标右侧的文字（空 = 纯图标按钮）。</summary>
    public string Text { get; set; } = "";

    /// <summary>图标边长，默认 13（按钮内文字 12）。</summary>
    public double Size { get; set; } = 13;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var glyph = Glyph.Create(Key, Size);
        if (string.IsNullOrEmpty(Text)) return (object?)glyph ?? new TextBlock();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (glyph is not null) row.Children.Add(glyph);
        row.Children.Add(new TextBlock { Text = Text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}

/// <summary>几何键 ⇒ 描边 <see cref="Path"/>；描边色由 <c>Path.icon-fg</c> 样式跟随宿主文字色。</summary>
public static class Glyph
{
    public static Path? Create(string key, double size)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Application.Current?.TryFindResource(key, out var value) != true || value is not Geometry geometry) return null;

        var path = new Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        path.Classes.Add("icon-fg");
        return path;
    }
}
