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

    /// <summary>源视频全路径。</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>输出文件全路径（留空 = 在源目录旁边自动命名）。</summary>
    public string OutputPath { get; set; } = "";

    /// <summary>
    /// 用户在「命令生成」页**手动改写过**的 ffmpeg 参数串（⛔ **不含** ffmpeg.exe，也**不含**输出路径 ——
    /// 输出由 <see cref="OutputPath"/> 自动补在末尾）。
    /// <para>非空 ⇒ 批量处理运行时**原样执行**它，不再按任务参数重新拼装（手改不被悄悄覆盖）。</para>
    /// </summary>
    [ObservableProperty] private string _rawArgs = "";

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

    /// <summary>片段时长（秒）。</summary>
    public double Length => End > Start ? End - Start : 0;

    public string RangeDisplay => $"{FormatTime(Start)} → {(End > 0 ? FormatTime(End) : "末尾")}";

    public string LengthDisplay => Length > 0 ? FormatTime(Length) : "—";

    public string DurationDisplay => Duration > 0 ? FormatTime(Duration) : "未读取";

    /// <summary>预设名（列表 / 详情卡显示）。</summary>
    public string PresetName => CutPresets.Find(PresetId).Name;

    public override string KindDisplay => "剪辑";
    public override string ParamDisplay => RangeDisplay;
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

    public static System.Collections.Generic.List<TranscodePreset> All { get; } = new()
    {
        new TranscodePreset { Id = "h264",   Name = "H.264 MP4（通用，兼容性最好）", VideoCodec = "libx264", Crf = 20, Extension = ".mp4" },
        new TranscodePreset { Id = "h265",   Name = "H.265 MP4（体积更小，编码更慢）", VideoCodec = "libx265", Crf = 26, Extension = ".mp4", Speed = "medium" },
        new TranscodePreset { Id = "av1",    Name = "AV1 MP4（最小，最慢）",          VideoCodec = "libsvtav1", Crf = 32, Extension = ".mp4", Speed = "8" },
        new TranscodePreset { Id = "copy",   Name = "只换容器（不重编码，秒完成）",     VideoCodec = "copy",      AudioCodec = "copy", Extension = ".mp4" },
        new TranscodePreset { Id = "audio",  Name = "仅提取音频 MP3",                 VideoCodec = "",          AudioCodec = "libmp3lame", AudioBitrate = "192k", Extension = ".mp3" },
    };

    public static TranscodePreset Find(string? id)
        => All.Find(p => string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase)) ?? All[0];
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
    /// <summary>预设 id（<see cref="TranscodePreset.Id"/>）—— 决定**编码族**与默认容器。</summary>
    [ObservableProperty] private string _presetId = "h264";

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
    /// 码率模式：<c>crf</c> = 恒定质量（<see cref="Crf"/>）；<c>bitrate</c> = 目标码率（<see cref="TargetBitrate"/>）。
    /// </summary>
    [ObservableProperty] private string _bitrateMode = "crf";

    /// <summary>目标码率文本（如 <c>5000k</c> / <c>5M</c>）；仅 <c>bitrate</c> 模式生效。</summary>
    [ObservableProperty] private string _targetBitrate = "";

    /// <summary>**既剪辑又转码**时的起点（秒）；<see cref="HasTrim"/> 为真才生效。</summary>
    [ObservableProperty] private double _trimStart;

    /// <summary>**既剪辑又转码**时的终点（秒）；0 = 到末尾。</summary>
    [ObservableProperty] private double _trimEnd;

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

    /// <summary>是否带"剪辑区间"（既剪辑又转码的任务）。终点 0 = 到末尾。</summary>
    public bool HasTrim => TrimEnd > TrimStart;

    /// <summary>区间文本（<c>00:01:02.000 → 00:01:30.000</c>）；没设返回空串。</summary>
    public string TrimDisplay => HasTrim
        ? $"{CutTask.FormatTime(TrimStart)} → {(TrimEnd > 0 ? CutTask.FormatTime(TrimEnd) : "末尾")}"
        : "";

    /// <summary>实际会用的视频编码器名（CPU / GPU 由 <see cref="UseGpuEncoder"/> 决定）。</summary>
    public string EncoderDisplay => FfmpegService.ResolveEncoder(Preset.VideoCodec, UseGpuEncoder);

    /// <summary>容器（格式）显示文本。</summary>
    public string ContainerDisplay => string.IsNullOrWhiteSpace(Container) ? Preset.Extension : Container;

    /// <summary>分辨率设置显示；两个都为 0 = 保持原分辨率。</summary>
    public string ResolutionDisplay =>
        TargetWidth > 0 && TargetHeight > 0 ? $"{TargetWidth}×{TargetHeight}"
      : TargetHeight > 0 ? $"自适应 × 高 {TargetHeight}"
      : TargetWidth > 0 ? $"宽 {TargetWidth} × 自适应"
      : (MaxHeight > 0 ? $"限高 {MaxHeight}p" : "原分辨率");

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
    public override string ParamDisplay => HasTrim ? TrimDisplay + " · " + PresetName : PresetName;
    public override string BeforeDisplay => SourceSizeDisplay;

    partial void OnPresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(PresetName));
        OnPropertyChanged(nameof(ParamDisplay));
    }

    partial void OnSourceSizeChanged(long value) => OnPropertyChanged(nameof(SavingDisplay));

    /// <summary>
    /// 「输出大小变了」的回调。
    /// <para>⚠️ <see cref="VideoTaskBase.OutputSize"/> 是<b>基类</b>里的 <c>[ObservableProperty]</c>，
    /// 所以它的分部方法 <c>OnOutputSizeChanged</c> 只能在基类里声明（写在这里会 CS0759：找不到定义声明）。
    /// 基类那边会转调这个虚方法，让派生类照样能补发自己的派生属性通知。</para>
    /// </summary>
    protected override void NotifyOutputSizeChanged() => OnPropertyChanged(nameof(SavingDisplay));
}
