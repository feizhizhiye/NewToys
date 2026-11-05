using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.Models;


/// <summary>一条剪切任务：从源视频里切出 [开始, 结束) 这一段。（WHY: VT-008）</summary>
public sealed class CutPresetOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// 剪切预设表（**不写死在 XAML 里**）。
/// <para>预设决定"默认怎么切"，用户在详情卡里填的<b>自定义参数</b>会**追加在预设之后**
/// —— ffmpeg 后出现的同名参数会覆盖前面的，所以自定义参数天然能覆盖预设。</para>
/// </summary>
public static class CutPresets
{
    /// <summary>直接剪切：<c>-c copy</c>，秒级完成、无损，但起止点会被挪到最近的关键帧。</summary>
    public const string Copy = "copy";

    /// <summary>重新编码：精确到帧，慢。</summary>
    public const string ReEncode = "reencode";

    public static IReadOnlyList<CutPresetOption> All { get; } = new List<CutPresetOption>
    {
        new CutPresetOption { Id = Copy,     Name = "直接剪切（-c copy，秒完成、无损，起止会就近对齐关键帧）" },
        new CutPresetOption { Id = ReEncode, Name = "重新编码（H.264，精确到帧，慢）" },
    };

    public static CutPresetOption Find(string? id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public partial class CutTask : VideoTaskBase
{
    /// <summary>源视频总时长（秒）；0 = 还没探测。</summary>
    [ObservableProperty] private double _duration;

    /// <summary>剪切预设（取值见 <see cref="CutPresets"/>）。</summary>
    [ObservableProperty] private string _presetId = CutPresets.Copy;

    /// <summary>
    /// **自定义 ffmpeg 参数**（追加在预设之后，留空 = 只用预设）。
    /// 例：<c>-vf scale=1280:-2</c>、<c>-c:v libx264 -crf 18</c>（会覆盖预设的编码设置）。
    /// </summary>
    [ObservableProperty] private string _extraArgs = "";

    /// <summary>开始时间（秒）。</summary>
    [ObservableProperty] private double _start;

    /// <summary>结束时间（秒）；0 = 到末尾。</summary>
    [ObservableProperty] private double _end;

    /// <summary>是否重新编码（false = <c>-c copy</c>，快但只能切在关键帧上）。</summary>
    [ObservableProperty] private bool _reEncode;

    /// <summary>重编码时用的 CRF（越小越清晰越大；18~28 常用）。</summary>
    [ObservableProperty] private int _crf = 20;

    // ==================== 多段剪切 / 拼接 ====================

    /// <summary>
    /// **多段剪切串**（每行一段：<c>00:00:05-00:00:12</c>）。解析出 **≥2 段** ⇒ 走「逐段裁剪 + 拼接」，
    /// 此时上面的「开始 / 结束」被忽略。
    /// </summary>
    [ObservableProperty] private string _segmentsText = "";

    /// <summary>解析出的多段（<see cref="OnSegmentsTextChanged"/> 时重算，避免每次绑定时重解析）。</summary>
    public IReadOnlyList<CutSegment> Segments { get; private set; } = Array.Empty<CutSegment>();

    /// <summary>是否多段（≥2 段；1 段按普通剪切处理）。</summary>
    public bool IsMultiSegment => Segments.Count > 1;

    /// <summary>多段摘要（详情卡提示：几段 + 合计时长）。</summary>
    public string SegmentsDisplay
        => Segments.Count == 0
            ? ""
            : $"{Segments.Count} 段 · 合计 {FormatTime(Segments.Sum(s => s.Length))}";

    partial void OnSegmentsTextChanged(string value)
    {
        Segments = CutSegments.Parse(value);
        OnPropertyChanged(nameof(Segments));
        OnPropertyChanged(nameof(IsMultiSegment));
        OnPropertyChanged(nameof(SegmentsDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    /// <summary>片段时长（秒）。</summary>
    public double Length => End > Start ? End - Start : 0;

    public string RangeDisplay => $"{FormatTime(Start)} → {(End > 0 ? FormatTime(End) : "末尾")}";

    public string LengthDisplay => Length > 0 ? FormatTime(Length) : "—";

    public string DurationDisplay => Duration > 0 ? FormatTime(Duration) : "未读取";

    /// <summary>预设名（列表 / 详情卡显示）。</summary>
    public string PresetName => CutPresets.Find(PresetId).Name;

    public override string KindDisplay => "剪辑";
    public override string ParamDisplay => IsMultiSegment ? $"多段 {Segments.Count} 段" : RangeDisplay;
    public override string BeforeDisplay => DurationDisplay;

    partial void OnPresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(PresetName));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnStartChanged(double value) => NotifyRange();
    partial void OnEndChanged(double value) => NotifyRange();
    partial void OnDurationChanged(double value) => NotifyRange();

    private void NotifyRange()
    {
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(RangeDisplay));
        OnPropertyChanged(nameof(LengthDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
        OnPropertyChanged(nameof(BeforeDisplay));
    }

    /// <summary>秒 → <c>HH:MM:SS.mmm</c>（ffmpeg 认的时间码格式）。</summary>
    public static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    /// <summary>解析 <c>HH:MM:SS.mmm</c> / <c>MM:SS</c> / 纯秒数；解析不了返回 null。</summary>
    public static double? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        // 纯数字 = 秒
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var raw))
            return raw;

        var parts = s.Split(':');
        try
        {
            if (parts.Length == 2)
                return int.Parse(parts[0]) * 60 + double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Length == 3)
                return int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60
                     + double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return null; }
        return null;
    }
}

