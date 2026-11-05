using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>阅读样式的导入 / 导出 / 预设：一份 ReaderStyle 就是一个可交换的排版快照。（WHY: RD-014）</summary>
public sealed class ReaderStyleService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ReadingSettingsService _settings;

    public ReaderStyleService(ReadingSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>内置样式：常见四种场景，点了即整体套用（每一项都给全，⛔ 不挑着改）。</summary>
    public IReadOnlyList<ReaderStyle> Presets { get; } = new[]
    {
        new ReaderStyle
        {
            Name = "默认白底",
            Theme = "light",
            FontFamily = ReadingSettingsService.DefaultFontFamily,
            FontSize = ReadingSettingsService.DefaultFontSize,
            LineSpacing = ReadingSettingsService.DefaultLineSpacing,
            ContentWidth = ReadingSettingsService.DefaultContentWidth,
            Padding = 28,
            Background = "",
            Foreground = "",
            Indent = ReadingSettingsService.DefaultIndent,
            ParagraphSpacing = ReadingSettingsService.DefaultParagraphSpacing,
        },
        new ReaderStyle
        {
            Name = "护眼米黄",
            Theme = "sepia",
            FontFamily = ReadingSettingsService.DefaultFontFamily,
            FontSize = 18,
            LineSpacing = 1.9,
            ContentWidth = 860,
            Padding = 32,
            Background = "",
            Foreground = "",
            Indent = 2,
            ParagraphSpacing = 1,
        },
        new ReaderStyle
        {
            Name = "夜间暗色",
            Theme = "dark",
            FontFamily = ReadingSettingsService.DefaultFontFamily,
            FontSize = 17,
            LineSpacing = 1.8,
            ContentWidth = 820,
            Padding = 32,
            Background = "",
            Foreground = "",
            Indent = 2,
            ParagraphSpacing = 1,
        },
        new ReaderStyle
        {
            Name = "大字宽松",
            Theme = "light",
            FontFamily = ReadingSettingsService.DefaultFontFamily,
            FontSize = 24,
            LineSpacing = 2.2,
            ContentWidth = 900,
            Padding = 40,
            Background = "",
            Foreground = "",
            Indent = 2,
            ParagraphSpacing = 2,
        },
    };

    /// <summary>当前设置的快照（导出用）。</summary>
    public ReaderStyle Current() => new()
    {
        Name = "我的样式",
        FontFamily = _settings.FontFamily,
        FontSize = _settings.FontSize,
        LineSpacing = _settings.LineSpacing,
        ContentWidth = _settings.ContentWidth,
        Padding = _settings.ReaderPadding,
        Theme = ReadingSettingsService.ThemeKey(_settings.Theme),
        Background = _settings.CustomBackground,
        Foreground = _settings.CustomForeground,
        Indent = _settings.Indent,
        ParagraphSpacing = _settings.ParagraphSpacing,
    };

    /// <summary>
    /// 套用一份样式：<c>null</c> 的项跳过（保留当前值），其余逐项写设置。
    /// </summary>
    /// <returns>状态栏文本（已应用几项）。</returns>
    public string Apply(ReaderStyle style)
    {
        if (style is null) return "⚠ 样式为空，未改动";

        var applied = 0;

        if (style.FontFamily is { } family && family.Trim().Length > 0) { _settings.SetFontFamily(family); applied++; }
        if (style.FontSize is { } size) { _settings.SetFontSize(size); applied++; }
        if (style.LineSpacing is { } spacing) { _settings.SetLineSpacing(spacing); applied++; }
        if (style.ContentWidth is { } width) { _settings.SetContentWidth(width); applied++; }
        if (style.Padding is { } padding) { _settings.SetReaderPadding(padding); applied++; }
        if (ParseTheme(style.Theme) is { } theme) { _settings.SetTheme(theme); applied++; }
        if (style.Background is { } bg) { _settings.SetCustomBackground(bg); applied++; }
        if (style.Foreground is { } fg) { _settings.SetCustomForeground(fg); applied++; }
        if (style.Indent is { } indent) { _settings.SetIndent(indent); applied++; }
        if (style.ParagraphSpacing is { } gap) { _settings.SetParagraphSpacing(gap); applied++; }

        if (applied == 0) return "⚠ 这份样式里没有可识别的项，未改动";

        var name = string.IsNullOrWhiteSpace(style.Name) ? "导入的样式" : $"样式「{style.Name.Trim()}」";
        return $"{name}已应用（{applied} 项）";
    }

    /// <summary>读一个样式文件并套用。</summary>
    /// <returns>状态栏文本（成功 / 失败原因）。</returns>
    public string Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "⚠ 找不到这个样式文件";

        string text;
        try { text = ReadText(path); }
        catch (Exception ex) { return $"⚠ 样式文件读不出来：{ex.Message}"; }

        ReaderStyle? style;
        try { style = JsonSerializer.Deserialize<ReaderStyle>(text, ReadOptions); }
        catch (JsonException ex) { return $"⚠ 样式文件不是合法 JSON：{ex.Message}"; }

        if (style is null) return "⚠ 样式文件是空的，未改动";
        return Apply(style);
    }

    /// <summary>把当前设置导成一个样式文件。</summary>
    /// <returns>状态栏文本。</returns>
    public string Export(string path, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return "⚠ 没选保存位置，导出取消";

        var style = Current();
        if (!string.IsNullOrWhiteSpace(name)) style.Name = name!.Trim();

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
            File.WriteAllText(path, JsonSerializer.Serialize(style, WriteOptions), new UTF8Encoding(false));
            return $"样式已导出：{path}";
        }
        catch (Exception ex)
        {
            return $"⚠ 导出失败：{ex.Message}";
        }
    }

    /// <summary>配色档文本（<c>light</c> / <c>sepia</c> / <c>dark</c>）→ 枚举；认不出 = <c>null</c>（不动它）。</summary>
    private static ReaderTheme? ParseTheme(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
    {
        "light" => ReaderTheme.Light,
        "sepia" => ReaderTheme.Sepia,
        "dark" => ReaderTheme.Dark,
        _ => null,
    };

    /// <summary>BOM → 严格 UTF-8 → GB18030（与书源导入同一套嗅探，手写样式文件常见 GBK 保存）。</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding("gb18030").GetString(bytes);
            }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }
}
