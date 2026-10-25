using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.Models;


/// <summary>
/// 转码预设（「转成什么」的可选项）。**不写死在 XAML 里**：列表在这里，界面只负责展示。
/// </summary>
public sealed class TranscodePreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>ffmpeg 的视频编码器名。</summary>
    public string VideoCodec { get; set; } = "libx264";

    /// <summary>CRF / 质量参数（0 = 该预设不用 CRF）。</summary>
    public int Crf { get; set; } = 20;

    /// <summary>音频编码器（留空 = 直接拷贝音轨）。</summary>
    public string AudioCodec { get; set; } = "aac";

    /// <summary>音频码率。</summary>
    public string AudioBitrate { get; set; } = "192k";

    /// <summary>输出容器扩展名（带点）。</summary>
    public string Extension { get; set; } = ".mp4";

    /// <summary>附加快慢预设（preset）。</summary>
    public string Speed { get; set; } = "medium";

    /// <summary>输出文件名后缀（v3.19：默认 H.265 转码加 <c>_H265</c>）。空 = 不加。</summary>
    public string OutputSuffix { get; set; } = "";

    public static System.Collections.Generic.List<TranscodePreset> All { get; } = new()
    {
        new TranscodePreset { Id = "h264",   Name = "H.264 MP4（通用，兼容性最好）", VideoCodec = "libx264", Crf = 20, Extension = ".mp4", OutputSuffix = "_H264" },
        new TranscodePreset { Id = "h265",   Name = "H.265 MP4（体积更小，编码更慢）", VideoCodec = "libx265", Crf = 23, Extension = ".mp4", Speed = "medium", OutputSuffix = "_H265" },
        new TranscodePreset { Id = "av1",    Name = "AV1 MP4（最小，最慢）",          VideoCodec = "libsvtav1", Crf = 32, Extension = ".mp4", Speed = "8", OutputSuffix = "_AV1" },
        new TranscodePreset { Id = "copy",   Name = "只换容器（不重编码，秒完成）",     VideoCodec = "copy",      AudioCodec = "copy", Extension = ".mp4", OutputSuffix = "_copy" },
        new TranscodePreset { Id = "audio",  Name = "仅提取音频 MP3",                 VideoCodec = "",          AudioCodec = "libmp3lame", AudioBitrate = "192k", Extension = ".mp3", OutputSuffix = "_audio" },
        // GIF 走**独立命令分支**（`BuildTranscodeArgs` 里 `IsGif` 判定）——
        // 无音轨、无限循环、不吃 CRF/preset，滤镜链自带 palettegen/paletteuse。
        new TranscodePreset { Id = "gif",    Name = "GIF 动图（无声，循环）",          VideoCodec = "gif",       AudioCodec = "", Extension = ".gif", Speed = "", OutputSuffix = "_GIF" },
    };

    public static TranscodePreset Find(string? id)
        => All.Find(p => string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// <summary>
/// 字幕处理方式。
/// <para><see cref="Soft"/> = 只把字幕**封装**进容器（视频/音频 <c>-c copy</c>，秒完成、可在播放器里开关）；
/// <see cref="Hard"/> = 把字幕**烧进画面**（必须重新编码，画面上永久带字）。</para>
/// </summary>
public static class SubtitleModes
{
    /// <summary>不加字幕。</summary>
    public const string None = "";

    /// <summary>软字幕（封装成可选轨；输出容器必须是 .mkv）。</summary>
    public const string Soft = "soft";

    /// <summary>硬字幕（烧进画面，必须重编码）。</summary>
    public const string Hard = "hard";

    public static IReadOnlyList<SubtitleModeOption> All { get; } = new[]
    {
        new SubtitleModeOption { Value = None, Label = "不加字幕" },
        new SubtitleModeOption { Value = Soft, Label = "软字幕（封装成 .mkv 可选轨，不重编码）" },
        new SubtitleModeOption { Value = Hard, Label = "硬字幕（烧进画面，需重编码）" },
    };

    public static SubtitleModeOption Find(string? value)
        => All.FirstOrDefault(o => string.Equals(o.Value, value ?? "", StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>字幕文件后缀白名单（ffmpeg 支持的常见文本字幕）。</summary>
    public const string FilePatterns = "*.srt;*.ass;*.ssa;*.vtt";
}

/// <summary>「字幕方式」下拉项（<see cref="Value"/> = 存进 <see cref="TranscodeTask.SubtitleMode"/> 的值）。</summary>
public sealed class SubtitleModeOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// 音频处理方式：一个下拉单选，⛔ 不用三个独立开关（互斥关系靠界面管不住）。
/// </summary>
public static class AudioModes
{
    /// <summary>保持原音频（默认）。</summary>
    public const string Keep = "";

    /// <summary>静音（<c>-an</c>，去掉音轨）。</summary>
    public const string Mute = "mute";

    /// <summary>音量增益 / 衰减（<c>-af volume=±NdB</c>，必须重编码音频）。</summary>
    public const string Volume = "volume";

    /// <summary>替换音轨（额外一条输入 + <c>-map</c>，视频仍取源片）。</summary>
    public const string Replace = "replace";

    public static IReadOnlyList<AudioModeOption> All { get; } = new[]
    {
        new AudioModeOption { Value = Keep,    Label = "保持原音频" },
        new AudioModeOption { Value = Mute,    Label = "静音（去掉音轨）" },
        new AudioModeOption { Value = Volume,  Label = "音量增益（±dB）" },
        new AudioModeOption { Value = Replace, Label = "替换音轨（换一条音频）" },
    };

    public static AudioModeOption Find(string? value)
        => All.FirstOrDefault(o => string.Equals(o.Value, value ?? "", StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>选音频文件时的后缀白名单。</summary>
    public const string FilePatterns = "*.mp3;*.m4a;*.aac;*.wav;*.flac;*.ogg;*.opus";
}

/// <summary>「音频方式」下拉项（<see cref="Value"/> = 存进 <see cref="TranscodeTask.AudioMode"/> 的值）。</summary>
public sealed class AudioModeOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>多段剪切里的一段（<c>Start</c> 秒 → <c>End</c> 秒；<c>End</c>=0 表示到末尾，但多段时按无效处理）。</summary>
public readonly record struct CutSegment(double Start, double End)
{
    /// <summary>段时长（秒）。</summary>
    public double Length => End > Start ? End - Start : 0;
}

/// <summary>
/// **多段剪切串**的解析：一行一段，形如 <c>00:00:05-00:00:12</c> / <c>5-12.5</c>。
/// <para>分隔符支持 <c>-</c> / <c>~</c> / <c>→</c> / 逗号 / 空白；时间码的冒号与小数点不受影响。</para>
/// <para>⛔ 解析失败的行**直接丢掉**（界面会显示解析出几段，用户能立刻发现写错），不做"猜一个"。</para>
/// </summary>
public static class CutSegments
{
    private static readonly char[] Separators = { '-', '~', '→', ',', ' ', '\t' };

    /// <summary>解析多段文本（空 / 全无效 → 空列表）。</summary>
    public static IReadOnlyList<CutSegment> Parse(string? text)
    {
        var list = new List<CutSegment>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var idx = line.IndexOfAny(Separators);
            if (idx <= 0 || idx >= line.Length - 1) continue;

            var a = CutTask.ParseTime(line[..idx]);
            var b = CutTask.ParseTime(line[(idx + 1)..]);
            if (a == null || b == null) continue;
            if (b.Value <= a.Value) continue;

            list.Add(new CutSegment(a.Value, b.Value));
        }
        return list;
    }

    /// <summary>段的显示文本（如 <c>00:00:05.000 → 00:00:12.000</c>）。</summary>
    public static string Describe(IReadOnlyList<CutSegment> segments)
        => segments.Count == 0
            ? ""
            : string.Join(" ｜ ", segments.Select(s => $"{CutTask.FormatTime(s.Start)} → {CutTask.FormatTime(s.End)}"));
}

/// <summary>「旋转 / 翻转」下拉项（<see cref="Value"/> = 存进 <see cref="TranscodeTask.Rotate"/> 的值）。</summary>
public sealed class RotateOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// 视频「旋转 / 翻转」选项（v3.19，需求 §6.4.13）：命令卡旋转字段 + 预览旋转按钮共用这一份。
/// <para>值存进 <see cref="TranscodeTask.Rotate"/>，<see cref="ToFilter"/> 映射到 ffmpeg <c>-vf</c> 过滤器；
/// 预览的 ↻/↺ 按钮经 <see cref="FromAngle"/> / <see cref="ToAngle"/> 与角度互转，实现"转预览 → 同步字段"。</para>
/// </summary>
public static class RotateOptions
{
    /// <summary>不旋转（默认）。</summary>
    public const string None = "";
    /// <summary>顺时针 90°。</summary>
    public const string Cw90 = "cw90";
    /// <summary>逆时针 90°（= 顺时针 270°）。</summary>
    public const string Ccw90 = "ccw90";
    /// <summary>180°。</summary>
    public const string R180 = "r180";
    /// <summary>水平翻转（镜像）。</summary>
    public const string FlipH = "fliph";

    public static IReadOnlyList<RotateOption> All { get; } = new[]
    {
        new RotateOption { Value = None,   Label = "不旋转" },
        new RotateOption { Value = Cw90,   Label = "顺时针 90°" },
        new RotateOption { Value = Ccw90,  Label = "逆时针 90°" },
        new RotateOption { Value = R180,   Label = "180°" },
        new RotateOption { Value = FlipH,  Label = "水平翻转" },
    };

    public static RotateOption Find(string? value)
        => All.FirstOrDefault(o => string.Equals(o.Value, value ?? "", StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>旋转值 → ffmpeg <c>-vf</c> 过滤器片段；不旋转 / 未知返回空串。</summary>
    public static string ToFilter(string? rotate) => (rotate ?? "").ToLowerInvariant() switch
    {
        Cw90 => "transpose=clock",
        Ccw90 => "transpose=cclock",
        R180 => "hflip,vflip",
        FlipH => "hflip",
        _ => ""
    };

    /// <summary>预览旋转角度（0/90/180/270）→ 旋转值（预览按钮同步命令卡字段用）。</summary>
    public static string FromAngle(double angle)
    {
        var a = ((int)Math.Round(angle) % 360 + 360) % 360;
        return a switch { 90 => Cw90, 180 => R180, 270 => Ccw90, _ => None };
    }

    /// <summary>旋转值 → 预览旋转角度（命令卡改值时回带预览；翻转无角度→0，由调用方决定是否保留）。</summary>
    public static double ToAngle(string? rotate) => (rotate ?? "").ToLowerInvariant() switch
    {
        Cw90 => 90, Ccw90 => 270, R180 => 180, _ => 0
    };
}

