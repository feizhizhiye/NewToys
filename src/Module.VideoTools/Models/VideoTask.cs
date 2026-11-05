using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.Models;

/// <summary>任务的运行状态（剪切 / 转码两页共用）。（WHY: VT-006）</summary>
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

    /// <summary>转码前判定「不值得」而主动跳过（没有跑 ffmpeg，<see cref="VideoTaskBase.Message"/> 里写原因）。</summary>
    public const string Skipped = "已跳过";

    public static bool IsFinished(string? s) => s is Done or Failed or Canceled or Skipped;
}

/// <summary>剪切 / 转码任务的公共字段。</summary>
public abstract partial class VideoTaskBase : ObservableObject
{
    /// <summary>稳定标识（新建时生成）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>任务创建时间（v3.15「创建时间」列）。⚠️ 老任务（本字段落盘之前建的）反序列化后是**载入时刻**，可接受。</summary>
    public DateTime Created { get; set; } = DateTime.Now;

    public string CreatedDisplay => Created.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// 「命令摘要」列要显示的文本 —— **由批量处理页的 VM 在刷新列表时写入**。
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
    /// 最后一次运行的结束时间（成功 / 失败 / 取消都写；<c>null</c> = 还没跑过）。
    /// <para>这就是列表里的「运行记录」：跑完一个任务留一行，关掉软件再导入也还在。
    /// 批量运行的「跳过已完成」判断的是 <see cref="Status"/>，本字段只负责展示。</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishedDisplay))]
    private DateTime? _finishedAt;

    /// <summary>最后一次运行的耗时（秒；0 = 未记录）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishedDisplay))]
    private int _elapsedSeconds;

    /// <summary>「完成时间」列：<c>10-30 15:32 · 1m20s</c>；没跑过显示破折号。</summary>
    public string FinishedDisplay
    {
        get
        {
            if (FinishedAt is not { } t) return "—";
            var stamp = t.ToString("MM-dd HH:mm");
            if (ElapsedSeconds <= 0) return stamp;
            var span = ElapsedSeconds < 60
                ? $"{ElapsedSeconds}s"
                : ElapsedSeconds < 3600
                    ? $"{ElapsedSeconds / 60}m{ElapsedSeconds % 60:00}s"
                    : $"{ElapsedSeconds / 3600}h{ElapsedSeconds % 3600 / 60:00}m";
            return $"{stamp} · {span}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private string _status = VideoTaskStatus.Pending;

    /// <summary>
    /// 进度 0~100（ffmpeg 的 -progress 输出解析得到；跑完置 100）。
    /// <para>🔴 必须通知 <see cref="StatusDisplay"/>：状态列显示的是「运行中 42%」这种拼出来的串，
    /// 只通知 <c>Progress</c> 的话百分比只有在状态变化（一次运行结束）时才刷新 —— 看着像"进度不走了"。</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private int _progress;

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

    /// <summary>把任务恢复到"等待中"（改了参数后重新跑）。<para>顺带清掉上次运行记录 —— 重新排队 = 新的一轮。</para></summary>
    public void Reset()
    {
        Status = VideoTaskStatus.Pending;
        Progress = 0;
        Message = "";
        OutputSize = 0;
        FinishedAt = null;
        ElapsedSeconds = 0;
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
