using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.Models;

/// <summary>
/// 任务的运行状态（剪切 / 转码两页共用）。
/// <para>
/// ⚠️ 类名**不能**叫 <c>TaskStatus</c>：项目开了隐式 using，<c>System.Threading.Tasks.TaskStatus</c>
/// 也在作用域里，同名会到处 CS0104（"不明确的引用"）。命名铁律：模型名避开 BCL 公开类型名。
/// </para>
/// </summary>
public static class VideoTaskStatus
{
    /// <summary>排队等待（还没跑）。</summary>
    public const string Pending = "等待中";

    /// <summary>正在跑。</summary>
    public const string Running = "运行中";

    /// <summary>跑完了（成功）。</summary>
    public const string Done = "已完成";

    /// <summary>失败（<see cref="VideoTaskBase.Message"/> 里有原因）。</summary>
    public const string Failed = "失败";

    /// <summary>被用户取消。</summary>
    public const string Canceled = "已取消";

    public static bool IsFinished(string? s) => s is Done or Failed or Canceled;
}

/// <summary>
/// 剪切 / 转码任务的公共字段。
/// <para>
/// 任务<b>只是"待办清单"</b>（参数 + 状态），真正的活由 <c>FfmpegService</c> 去跑；
/// 任务清单落 <c>config\video-tasks.json</c>，所以关掉软件再回来还在（会被「设置 → 数据管理」一起备份）。
/// </para>
/// </summary>
public abstract partial class VideoTaskBase : ObservableObject
{
    /// <summary>稳定标识（新建时生成）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>任务创建时间（v3.15「创建时间」列）。⚠️ 老任务（本字段落盘之前建的）反序列化后是**载入时刻**，可接受。</summary>
    public DateTime Created { get; set; } = DateTime.Now;

    public string CreatedDisplay => Created.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// v3.15「命令摘要」列要显示的文本 —— **由批量处理页的 VM 在刷新列表时写入**。
    /// <para>为什么不能由模型自己算：拼 ffmpeg 参数需要 <c>FfmpegService</c> 与配置里的 ffmpeg 路径，
    /// 模型层拿不到（也不该拿）。VM 每次 <c>RefreshView</c> 填一次，行绑定时读它。</para>
    /// </summary>
    public string CommandSummaryDisplay { get; set; } = "";

    /// <summary>源视频全路径。</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>输出文件全路径（留空 = 在源目录旁边自动命名）。</summary>
    public string OutputPath { get; set; } = "";

    /// <summary>
    /// 用户在「视频预览」页**手动改写过**的 ffmpeg 参数串（⛔ **不含** ffmpeg.exe，也**不含**输出路径 ——
    /// 输出由 <see cref="OutputPath"/> 自动补在末尾）。
    /// <para>非空 ⇒ 批量处理运行时**原样执行**它，不再按任务参数重新拼装（手改不被悄悄覆盖）。</para>
    /// </summary>
    [ObservableProperty] private string _rawArgs = "";

    /// <summary>
    /// v3.16：批量处理列表**行首勾选框**（表头 ☑ 的「全选 / 取消选择 / 反选」改它）。
    /// <para>⛔ 只是界面上的多选标记，**不参与命令生成**（跑哪些任务由「▶ 跑选中 / ▶▶ 批量运行」决定）。</para>
    /// </summary>
    [ObservableProperty] private bool _isChecked;

    [ObservableProperty] private string _status = VideoTaskStatus.Pending;

    /// <summary>进度 0~100（ffmpeg 的 -progress 输出解析得到；跑完置 100）。</summary>
    [ObservableProperty] private int _progress;

    /// <summary>结果 / 失败原因（状态行与详情卡都显示它）。</summary>
    [ObservableProperty] private string _message = "";

    /// <summary>输出文件的实际大小（跑完才有）。</summary>
    [ObservableProperty] private long _outputSize;

    /// <summary>文件名（列表第一列）。</summary>
    public string Name => string.IsNullOrEmpty(SourcePath) ? "（未选择文件）" : Path.GetFileName(SourcePath);

    /// <summary>输出文件名（详情卡显示）。</summary>
    public string OutputName => string.IsNullOrEmpty(OutputPath) ? "（自动命名）" : Path.GetFileName(OutputPath);

    public bool IsFinished => VideoTaskStatus.IsFinished(Status);
    public bool IsRunning => Status == VideoTaskStatus.Running;

    /// <summary>
    /// 输出大小变化的挂钩（由 <see cref="OutputSize"/> 的分部方法转调）。
    /// 派生类（如 <see cref="TranscodeTask"/> 的「省了多少」）在这里补发自己的通知 ——
    /// ⚠️ 分部方法只能声明在**定义该属性**的类里，所以跨类只能靠虚方法转一手。
    /// </summary>
    protected virtual void NotifyOutputSizeChanged() { }

    partial void OnOutputSizeChanged(long value) => NotifyOutputSizeChanged();

    /// <summary>是否在等待（可再次加入批量运行）。</summary>
    public bool IsPending => Status == VideoTaskStatus.Pending;

    /// <summary>输出大小（人类可读）。</summary>
    public string OutputSizeDisplay => FormatSize(OutputSize);

    /// <summary>状态 + 进度（列表「状态」列显示）。</summary>
    public string StatusDisplay => Status == VideoTaskStatus.Running ? $"{Status} {Progress}%" : Status;

    // ==================== 「批量处理」页的列表列（v3.8） ====================
    // 两类任务混排在同一个列表里，所以这三列由**模型自己**给（⛔ 不能让 XAML 去调 VM 的静态方法）。

    /// <summary>任务类型名（剪辑 / 转码）。</summary>
    public virtual string KindDisplay => "任务";

    /// <summary>参数摘要：剪辑看区间，转码看预设。</summary>
    public virtual string ParamDisplay => "";

    /// <summary>「处理前」列：转码 = 源大小，剪辑 = 源时长。</summary>
    public virtual string BeforeDisplay => "";

    /// <summary>把任务恢复到"等待中"（改了参数后重新跑）。</summary>
    public void Reset()
    {
        Status = VideoTaskStatus.Pending;
        Progress = 0;
        Message = "";
        OutputSize = 0;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };
}

/// <summary>
/// 一条<b>剪切任务</b>：从源视频里切出 [开始, 结束) 这一段。
/// <para>
/// 默认 <c>-c copy</c>（不重新编码，秒出、无损）；勾了「重新编码」才走编码（首尾不在关键帧上时必须重编码才准）。
/// </para>
/// </summary>
/// <summary>剪切预设下拉项。</summary>
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

/// <summary>
/// 一条<b>转码任务</b>：源 → 目标（按预设）。
/// <para>
/// <b>前后对比</b>就落在这条记录上：源信息（<c>Source*</c>）在加任务时用 ffprobe 填一次，
/// 跑完再探测输出文件填 <c>Output*</c>，详情卡里并排显示"前 → 后"。
/// </para>
/// </summary>
public partial class TranscodeTask : VideoTaskBase
{
    /// <summary>预设 id（<see cref="TranscodePreset.Id"/>）—— 决定**编码族**与默认容器。v3.19 默认改 H.265。</summary>
    [ObservableProperty] private string _presetId = "h265";

    /// <summary>单独改过的 CRF（0 = 用预设的值）。</summary>
    [ObservableProperty] private int _crf;

    /// <summary>缩放到的最大边（0 = 不改分辨率）。例：720 = 缩到 720p。</summary>
    [ObservableProperty] private int _maxHeight;

    // ==================== v3.10 补足：格式 / 编码器(CPU·GPU) / 分辨率 / 码率 ====================

    /// <summary>
    /// 是否用 **GPU 编码器**（`h264_nvenc` / `hevc_nvenc` / `av1_nvenc`）。
    /// <para>⚠️ 机器没有对应显卡时 ffmpeg 会直接报错 —— 如实显示错误即可，⛔ 不做"悄悄回退"。</para>
    /// </summary>
    [ObservableProperty] private bool _useGpuEncoder;

    /// <summary>输出容器扩展名（带点）。ffmpeg 靠输出文件扩展名判断容器，所以它同时决定默认输出名后缀。</summary>
    [ObservableProperty] private string _container = ".mp4";

    /// <summary>缩放到的**宽**（像素）；0 = 不约束宽（配合 <see cref="TargetHeight"/> 按比例算）。</summary>
    [ObservableProperty] private int _targetWidth;

    /// <summary>缩放到的**高**（像素）；0 = 不约束高。</summary>
    [ObservableProperty] private int _targetHeight;

    /// <summary>
    /// **GIF 帧率**（只对 GIF 预设生效）：12 是「体积 / 流畅度」的常用折中，24 = 接近原速。
    /// </summary>
    [ObservableProperty] private int _frameRate = 12;

    /// <summary>**GIF 宽度**（只对 GIF 预设生效）：0 = 保持原宽（体积会很大），默认 480。</summary>
    [ObservableProperty] private int _gifWidth = 480;

    /// <summary>
    /// **默认宽 / 高上限**（v3.19；0 = 不启用）：源宽高**大于**它才缩到它，**小于等于**则保持原始大小
    /// （ffmpeg <c>scale=W:H:force_original_aspect_ratio=decrease</c>，只缩不放、保比例）。
    /// 与 <see cref="TargetWidth"/>/<see cref="TargetHeight"/>（显式宽高）二选一，优先用显式宽高。
    /// </summary>
    [ObservableProperty] private int _defaultWidth = 1920;

    /// <summary>默认高度上限（配合 <see cref="DefaultWidth"/>；0 = 不启用）。</summary>
    [ObservableProperty] private int _defaultHeight = 1080;

    /// <summary>
    /// 码率模式：<c>crf</c> = 恒定质量（<see cref="Crf"/>）；<c>bitrate</c> = 目标码率（<see cref="TargetBitrate"/>）。
    /// </summary>
    [ObservableProperty] private string _bitrateMode = "crf";

    /// <summary>目标码率文本（如 <c>5000k</c> / <c>5M</c>）；仅 <c>bitrate</c> 模式生效。</summary>
    [ObservableProperty] private string _targetBitrate = "";

    /// <summary>
    /// v3.16：**是否带"剪辑区间"**（详情卡转码分区上方那个「✂ 剪辑」勾选框绑它）。
    /// <para>以前只看"终点 &gt; 起点"，导致"勾了剪辑但结束留空（= 到末尾）"被判成不剪辑、勾选框显示不出来。
    /// 现在勾了就是勾了：终点留空 = 从起点到文件末尾。</para>
    /// </summary>
    [ObservableProperty] private bool _useTrim;

    /// <summary>**既剪辑又转码**时的起点（秒）；<see cref="HasTrim"/> 为真才生效。</summary>
    [ObservableProperty] private double _trimStart;

    /// <summary>**既剪辑又转码**时的终点（秒）；0 = 到末尾。</summary>
    [ObservableProperty] private double _trimEnd;

    /// <summary>
    /// 旋转 / 翻转（v3.19，取值见 <see cref="RotateOptions"/>；空 = 不旋转）。
    /// 映射到 ffmpeg <c>-vf</c>（<c>transpose</c> / <c>hflip</c>），保证转码后视频方向正确。
    /// </summary>
    [ObservableProperty] private string _rotate = "";

    /// <summary>旋转的中文名（详情卡 / 命令摘要显示）。</summary>
    public string RotateLabel => RotateOptions.Find(Rotate).Label;

    // ==================== 字幕（软字幕 / 硬字幕烧入） ====================

    /// <summary>字幕文件全路径（<c>.srt / .ass / .ssa / .vtt</c>；空 = 不加字幕）。</summary>
    [ObservableProperty] private string _subtitlePath = "";

    /// <summary>字幕方式（取值见 <see cref="SubtitleModes"/>；<c>""</c> = 不加）。</summary>
    [ObservableProperty] private string _subtitleMode = SubtitleModes.None;

    /// <summary>硬字幕字号（像素；0 = 用字幕文件自带的样式）。仅硬字幕生效。</summary>
    [ObservableProperty] private int _subtitleFontSize = 22;

    /// <summary>硬字幕距底边距（像素；0 = 不设）。仅硬字幕生效。</summary>
    [ObservableProperty] private int _subtitleMarginV = 24;

    /// <summary>是否软字幕（只封装、不重编码）。</summary>
    public bool IsSoftSubtitle
        => string.Equals(SubtitleMode, SubtitleModes.Soft, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(SubtitlePath);

    /// <summary>是否硬字幕（烧进画面）。</summary>
    public bool IsHardSubtitle
        => string.Equals(SubtitleMode, SubtitleModes.Hard, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(SubtitlePath);

    /// <summary>字幕摘要（命令摘要 / 详情卡提示）。</summary>
    public string SubtitleDisplay
        => !IsSoftSubtitle && !IsHardSubtitle
            ? "无字幕"
            : $"{(IsSoftSubtitle ? "软字幕" : "硬字幕")} · {System.IO.Path.GetFileName(SubtitlePath)}";

    partial void OnSubtitleModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSoftSubtitle));
        OnPropertyChanged(nameof(IsHardSubtitle));
        OnPropertyChanged(nameof(SubtitleDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnSubtitlePathChanged(string value)
    {
        OnPropertyChanged(nameof(IsSoftSubtitle));
        OnPropertyChanged(nameof(IsHardSubtitle));
        OnPropertyChanged(nameof(SubtitleDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 画面裁剪（去黑边 / 只留画面一块） ====================

    /// <summary>裁剪宽（像素；0 = 不裁剪）。必须与 <see cref="CropHeight"/> 同时 &gt; 0 才生效。</summary>
    [ObservableProperty] private int _cropWidth;

    /// <summary>裁剪高（像素；0 = 不裁剪）。</summary>
    [ObservableProperty] private int _cropHeight;

    /// <summary>裁剪左上角 X（像素；0 = 贴左边）。</summary>
    [ObservableProperty] private int _cropX;

    /// <summary>裁剪左上角 Y（像素；0 = 贴顶边）。</summary>
    [ObservableProperty] private int _cropY;

    /// <summary>是否真的有裁剪（宽高都 &gt; 0）。</summary>
    public bool IsCropping => CropWidth > 0 && CropHeight > 0;

    /// <summary>裁剪摘要（详情卡提示 / 列表参数列）。</summary>
    public string CropDisplay
        => IsCropping ? $"{CropWidth}×{CropHeight} @ ({CropX}, {CropY})" : "不裁剪";

    partial void OnCropWidthChanged(int value) => NotifyCropChanged();
    partial void OnCropHeightChanged(int value) => NotifyCropChanged();
    partial void OnCropXChanged(int value) => NotifyCropChanged();
    partial void OnCropYChanged(int value) => NotifyCropChanged();

    private void NotifyCropChanged()
    {
        OnPropertyChanged(nameof(IsCropping));
        OnPropertyChanged(nameof(CropDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 音轨（静音 / 音量 / 替换） ====================

    /// <summary>音频方式（取值见 <see cref="AudioModes"/>；<c>""</c> = 保持原音频）。</summary>
    [ObservableProperty] private string _audioMode = AudioModes.Keep;

    /// <summary>音量增益（dB，正 = 变响、负 = 变轻；0 = 不改）。仅 <see cref="AudioModes.Volume"/> 生效。</summary>
    [ObservableProperty] private double _volumeDb;

    /// <summary>替换用的音频文件全路径。仅 <see cref="AudioModes.Replace"/> 生效。</summary>
    [ObservableProperty] private string _replaceAudioPath = "";

    public bool IsAudioMuted => string.Equals(AudioMode, AudioModes.Mute, StringComparison.OrdinalIgnoreCase);
    public bool IsVolumeMode => string.Equals(AudioMode, AudioModes.Volume, StringComparison.OrdinalIgnoreCase);
    public bool IsReplaceAudio => string.Equals(AudioMode, AudioModes.Replace, StringComparison.OrdinalIgnoreCase);

    /// <summary>音频摘要（详情卡 / 参数列）。</summary>
    public string AudioDisplay => AudioMode switch
    {
        AudioModes.Mute => "静音",
        AudioModes.Volume => VolumeDb == 0 ? "音量（未设 dB）" : $"音量 {VolumeDb:+0.#;-0.#;0} dB",
        AudioModes.Replace => string.IsNullOrWhiteSpace(ReplaceAudioPath)
            ? "替换音轨（未选文件）"
            : "替换音轨 · " + System.IO.Path.GetFileName(ReplaceAudioPath),
        _ => "原音频"
    };

    partial void OnAudioModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsAudioMuted));
        OnPropertyChanged(nameof(IsVolumeMode));
        OnPropertyChanged(nameof(IsReplaceAudio));
        OnPropertyChanged(nameof(AudioDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnVolumeDbChanged(double value)
    {
        OnPropertyChanged(nameof(AudioDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnReplaceAudioPathChanged(string value)
    {
        OnPropertyChanged(nameof(AudioDisplay));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 目标体积（按 MB 反算码率） ====================

    /// <summary>
    /// 目标体积（MB；0 = 不用）。启用后按 <see cref="SourceDuration"/> 反算视频码率
    /// （<see cref="FfmpegService.AutoBitrateKbps"/>），**优先于** CRF / 目标码率。
    /// </summary>
    [ObservableProperty] private double _targetSizeMb;

    public bool HasTargetSize => TargetSizeMb > 0;

    partial void OnTargetSizeMbChanged(double value)
    {
        OnPropertyChanged(nameof(HasTargetSize));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    // ==================== 转码前（源） ====================

    [ObservableProperty] private long _sourceSize;
    [ObservableProperty] private string _sourceCodec = "";
    [ObservableProperty] private string _sourceResolution = "";
    [ObservableProperty] private string _sourceBitRate = "";
    [ObservableProperty] private double _sourceDuration;

    // ==================== 转码后（输出） ====================

    [ObservableProperty] private string _outputCodec = "";
    [ObservableProperty] private string _outputResolution = "";
    [ObservableProperty] private string _outputBitRate = "";
    [ObservableProperty] private double _outputDuration;

    public TranscodePreset Preset => TranscodePreset.Find(PresetId);

    public string PresetName => Preset.Name;

    public string SourceSizeDisplay => FormatSize(SourceSize);

    // ==================== v3.10：参数摘要（详情卡 / 批量列表显示） ====================

    /// <summary>是否带"剪辑区间"（既剪辑又转码的任务）。终点 0 / 留空 = 到末尾。</summary>
    public bool HasTrim => UseTrim || TrimEnd > TrimStart;

    /// <summary>区间文本（<c>00:01:02.000 → 00:01:30.000</c>）；没设返回空串。</summary>
    public string TrimDisplay => HasTrim
        ? $"{CutTask.FormatTime(TrimStart)} → {(TrimEnd > 0 ? CutTask.FormatTime(TrimEnd) : "末尾")}"
        : "";

    /// <summary>实际会用的视频编码器名（CPU / GPU 由 <see cref="UseGpuEncoder"/> 决定）。</summary>
    public string EncoderDisplay => FfmpegService.ResolveEncoder(Preset.VideoCodec, UseGpuEncoder);

    /// <summary>容器（格式）显示文本。GIF 预设强制 <c>.gif</c>；**软字幕强制 <c>.mkv</c>**（mp4 装不了 srt 字幕轨）。</summary>
    public string ContainerDisplay =>
        FfmpegService.IsGif(Preset) ? ".gif"
        : IsSoftSubtitle ? ".mkv"
        : string.IsNullOrWhiteSpace(Container) ? Preset.Extension
        : Container;

    /// <summary>分辨率设置显示；两个都为 0 = 保持原分辨率。</summary>
    public string ResolutionDisplay =>
        TargetWidth > 0 && TargetHeight > 0 ? $"{TargetWidth}×{TargetHeight}"
      : TargetHeight > 0 ? $"自适应 × 高 {TargetHeight}"
      : TargetWidth > 0 ? $"宽 {TargetWidth} × 自适应"
      : (MaxHeight > 0 ? $"限高 {MaxHeight}p"
      : (DefaultWidth > 0 && DefaultHeight > 0 ? $"不超 {DefaultWidth}×{DefaultHeight}" : "原分辨率"));

    /// <summary>码率设置显示。</summary>
    public string RateDisplay
    {
        get
        {
            if (string.Equals(BitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(TargetBitrate) ? "—" : "目标 " + TargetBitrate;
            var crf = Crf > 0 ? Crf : Preset.Crf;
            return crf > 0 ? $"CRF {crf}" : "—";
        }
    }

    public string SourceDurationDisplay => SourceDuration > 0 ? CutTask.FormatTime(SourceDuration) : "—";

    public string OutputDurationDisplay => OutputDuration > 0 ? CutTask.FormatTime(OutputDuration) : "—";

    /// <summary>
    /// 省了多少（负数 = 变大了）。只在两头都有大小时才有意义。
    /// 例：源 100MB、输出 60MB → "−40%（省 40.0 MB）"。
    /// </summary>
    public string SavingDisplay
    {
        get
        {
            if (SourceSize <= 0 || OutputSize <= 0) return "—";
            var delta = (double)(OutputSize - SourceSize) / SourceSize;
            var saved = SourceSize - OutputSize;
            var sign = delta <= 0 ? "−" : "+";
            return $"{sign}{Math.Abs(delta) * 100:0.0}%（{sign}{FormatSize(Math.Abs(saved))}）";
        }
    }

    public bool HasResult => OutputSize > 0;

    public override string KindDisplay => HasTrim ? "剪辑+转码" : "转码";

    public override string ParamDisplay
    {
        get
        {
            var head = HasTrim ? TrimDisplay + " · " + PresetName : PresetName;
            if (IsSoftSubtitle || IsHardSubtitle) head += " · " + SubtitleDisplay;
            // 非默认值才追加，避免列表里堆一串"不裁剪 · 原音频"
            if (IsCropping) head += " · 裁剪 " + CropDisplay;
            if (!string.IsNullOrEmpty(AudioMode)) head += " · " + AudioDisplay;
            if (HasTargetSize) head += $" · 目标 {TargetSizeMb:0.#} MB";
            return head;
        }
    }
    public override string BeforeDisplay => SourceSizeDisplay;

    partial void OnPresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(PresetName));
        OnPropertyChanged(nameof(ParamDisplay));
        // 预设变了 → 容器（GIF 强制 .gif）、编码器、码率、分辨率显示都要跟着变
        OnPropertyChanged(nameof(ContainerDisplay));
        OnPropertyChanged(nameof(EncoderDisplay));
        OnPropertyChanged(nameof(RateDisplay));
        OnPropertyChanged(nameof(IsGifPreset));
    }

    /// <summary>是不是 GIF 预设（详情卡据此显隐「GIF 帧率 / 宽度」）。</summary>
    public bool IsGifPreset => FfmpegService.IsGif(Preset);

    partial void OnRotateChanged(string value) => OnPropertyChanged(nameof(RotateLabel));

    partial void OnSourceSizeChanged(long value) => OnPropertyChanged(nameof(SavingDisplay));

    /// <summary>
    /// 「输出大小变了」的回调。
    /// <para>⚠️ <see cref="VideoTaskBase.OutputSize"/> 是<b>基类</b>里的 <c>[ObservableProperty]</c>，
    /// 所以它的分部方法 <c>OnOutputSizeChanged</c> 只能在基类里声明（写在这里会 CS0759：找不到定义声明）。
    /// 基类那边会转调这个虚方法，让派生类照样能补发自己的派生属性通知。</para>
    /// </summary>
    protected override void NotifyOutputSizeChanged() => OnPropertyChanged(nameof(SavingDisplay));
}
