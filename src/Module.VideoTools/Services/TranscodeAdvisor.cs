using System;
using System.Globalization;

namespace NewToys.VideoTools.Services;

/// <summary>收益预估的结论档位。</summary>
public enum TranscodeVerdict
{
    /// <summary>信息不足无法预估 —— 一律按「建议」处理，⛔ 不据此拦任务。</summary>
    Unknown,

    /// <summary>预估省幅低于门槛（甚至变大）—— 严格策略下应跳过。</summary>
    Skip,

    /// <summary>能省，但省得不多（门槛 ~ 门槛+15 个百分点）。</summary>
    Marginal,

    /// <summary>明显值得转。</summary>
    Worth
}

/// <summary>一次判定的结果（<see cref="EstRatio"/> = 预估输出体积 ÷ 源体积，&lt;1 表示变小）。</summary>
public sealed record TranscodeAdvice(
    TranscodeVerdict Verdict,
    double EstRatio,
    double SavingPercent,
    string Headline,
    string Detail)
{
    /// <summary>值不值得跑（严格策略据此跳过）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShouldSkip => Verdict == TranscodeVerdict.Skip;
}

/// <summary>判定用的源侧画像（全部来自 ffprobe，缺项留 0 / 空串）。</summary>
public readonly record struct SourceProfile(
    long SizeBytes,
    double DurationSec,
    int Width,
    int Height,
    double Fps,
    long TotalBitRate,
    long AudioBitRate,
    string VideoCodec)
{
    /// <summary>视频流码率（bps）：总码率减去音轨；音轨未知时按总码率算。</summary>
    public double VideoBitRate => TotalBitRate > 0
        ? Math.Max(TotalBitRate - Math.Max(AudioBitRate, 0), TotalBitRate * 0.5)
        : 0;
}

/// <summary>判定用的目标侧画像（来自预设 + 任务参数）。</summary>
public readonly record struct TargetProfile(
    string Codec,
    string AudioCodec,
    string AudioBitrate,
    int OutWidth,
    int OutHeight,
    double Fps)
{
    /// <summary>只换容器 / 只搬流，不重编码视频。</summary>
    public bool IsStreamCopy => string.Equals(Codec, "copy", StringComparison.OrdinalIgnoreCase);

    /// <summary>目标音轨码率（bps）；空 / 无法解析 → 0（表示跟随源）。</summary>
    public double AudioBitRateBps => ParseBitrate(AudioBitrate);

    private static double ParseBitrate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var t = text.Trim().ToLowerInvariant();
        var mul = 1.0;
        if (t.EndsWith('k')) { mul = 1000; t = t[..^1]; }
        else if (t.EndsWith('m')) { mul = 1_000_000; t = t[..^1]; }
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v * mul
            : 0;
    }
}

/// <summary>转码前的静态收益判定：只读源属性 + 目标参数，不预编码、不碰磁盘、不碰 UI。（WHY: VT-018）</summary>
public static class TranscodeAdvisor
{
    /// <summary>x264 的视觉透明线（1080p30 约 6 Mbps）。</summary>
    public const double LineX264 = 0.10;

    /// <summary>x265 的视觉透明线（1080p30 约 3.7 Mbps）。</summary>
    public const double LineX265 = 0.06;

    /// <summary>SVT-AV1 的视觉透明线（1080p30 约 3.1 Mbps）。</summary>
    public const double LineAv1 = 0.05;

    /// <summary>默认省幅门槛（%）：低于它就不值得重新编码一整部片子。</summary>
    public const int DefaultMinSavingPercent = 10;

    /// <summary>边际区宽度（百分点）：门槛 ~ 门槛+15 = 「能省但不多」。</summary>
    private const double MarginalBand = 15;

    /// <summary>编码器 → 视觉透明 bpp 线；未知编码器返回 0（不判）。</summary>
    public static double LineOf(string? codec)
    {
        var c = (codec ?? "").ToLowerInvariant();
        if (c.Contains("265") || c.Contains("hevc")) return LineX265;
        if (c.Contains("264") || c.Contains("avc")) return LineX264;
        if (c.Contains("av1") || c.Contains("svt")) return LineAv1;
        return 0;
    }

    /// <summary>
    /// 速度档对码率的影响折算：medium 档达到同视觉质量要比 slow 多用约 25% 码率，
    /// 所以「理论透明线」要按速度档放大，否则会高估省幅。
    /// </summary>
    private static double SpeedPenalty(string? codec)
    {
        var c = (codec ?? "").ToLowerInvariant();
        if (c.Contains("265") || c.Contains("hevc")) return 1.25;
        if (c.Contains("av1") || c.Contains("svt")) return 1.15;
        return 1.0;
    }

    /// <summary>判定一次转码值不值得跑。</summary>
    /// <param name="src">源画像（ffprobe）。</param>
    /// <param name="dst">目标画像（预设 + 任务参数）。</param>
    /// <param name="minSavingPercent">省幅门槛（%），来自设置。</param>
    public static TranscodeAdvice Evaluate(SourceProfile src, TargetProfile dst, int minSavingPercent)
    {
        var threshold = Math.Clamp(minSavingPercent, 0, 90);

        var audioDeltaMb = EstimateAudioDeltaMb(src, dst, out var audioTouched);

        // 只换容器：视频流一个比特都不动，收益只可能来自音轨 / 去轨
        if (dst.IsStreamCopy)
        {
            var copySaving = src.SizeBytes > 0 ? -(audioDeltaMb * 1024 * 1024) / src.SizeBytes * 100 : 0;
            if (!audioTouched || Math.Abs(audioDeltaMb) < 1)
            {
                return new TranscodeAdvice(TranscodeVerdict.Skip, 1.0, 0,
                    "不建议：只换容器且音轨原样拷贝 —— 体积几乎不变",
                    "视频 / 音频都是 -c copy，收益只可能来自去掉多余轨道；这条任务没有可去的轨。");
            }

            var verdictCopy = copySaving >= threshold ? TranscodeVerdict.Worth : TranscodeVerdict.Marginal;
            return new TranscodeAdvice(verdictCopy, 1 - copySaving / 100, copySaving,
                $"只换容器：音轨重编码 {DescribeAudioDelta(audioDeltaMb)}",
                "视频流不重编码 ⇒ 画质 100% 不变，变化全部来自音轨。");
        }

        // 缺关键项就不判（宁可多转一个，也不误杀）
        var fps = dst.Fps > 0 ? dst.Fps : src.Fps;
        var (w, h) = (dst.OutWidth > 0 ? dst.OutWidth : src.Width,
                      dst.OutHeight > 0 ? dst.OutHeight : src.Height);
        var line = LineOf(dst.Codec);

        if (line <= 0 || w <= 0 || h <= 0 || fps <= 0 || src.VideoBitRate <= 0)
        {
            return new TranscodeAdvice(TranscodeVerdict.Unknown, 0, 0,
                "无法预估：缺少码率 / 分辨率 / 帧率",
                "先让列表把媒体信息补全（ffprobe），再回来看这一条；本条按「建议」处理，不会被拦。");
        }

        var srcBpp = src.VideoBitRate / (w * (double)h * fps);
        var targetVideoBps = line * w * (double)h * fps * SpeedPenalty(dst.Codec);
        var videoRatio = Math.Clamp(targetVideoBps / src.VideoBitRate, 0.10, 1.50);

        var newVideoBps = src.VideoBitRate * videoRatio;
        var newAudioBps = audioTouched && dst.AudioBitRateBps > 0 ? dst.AudioBitRateBps : src.AudioBitRate;
        var newTotal = newVideoBps + newAudioBps;
        var oldTotal = src.VideoBitRate + src.AudioBitRate;
        var estRatio = oldTotal > 0 ? newTotal / oldTotal : 1;
        var saving = (1 - estRatio) * 100;

        var verdict = saving < threshold ? TranscodeVerdict.Skip
            : saving < threshold + MarginalBand ? TranscodeVerdict.Marginal
            : TranscodeVerdict.Worth;

        var codecName = CodecName(dst.Codec);
        var estOut = src.SizeBytes > 0 ? (long)(src.SizeBytes * estRatio) : 0;

        var headline = verdict switch
        {
            TranscodeVerdict.Skip => srcBpp <= line
                ? $"不建议：源已压到 {codecName} 透明线下（{srcBpp:0.000} ≤ {line:0.000}），预计 {Signed(saving)}"
                : $"不建议：预计只省 {saving:0}%，低于门槛 {threshold}%",
            TranscodeVerdict.Marginal => $"收益有限：预计 {Signed(saving)}，略高于门槛 {threshold}%",
            _ => src.SizeBytes > 0
                ? $"值得：预计 {Signed(saving)}（{FormatSize(src.SizeBytes)} → {FormatSize(estOut)}）"
                : $"值得：预计 {Signed(saving)}"
        };

        var detail = $"{w}×{h}@{fps:0.##}fps · 源 {srcBpp:0.000} bpp → {codecName} 透明线 {line:0.000}"
                   + $"，视频 {Mbps(src.VideoBitRate)} → {Mbps(newVideoBps)}"
                   + (audioTouched ? $"，音轨 {DescribeAudioDelta(audioDeltaMb)}" : "，音轨原样拷贝");

        // 音轨把收益吃光时，单独点出来（这是「转完反而变大」最常见的成因）
        if (audioTouched && audioDeltaMb > 0 && saving < 5)
            detail += " —— 音轨重编码抬高的部分吃掉了视频收益";

        return new TranscodeAdvice(verdict, estRatio, saving, headline, detail);
    }

    /// <summary>音轨体积变化（MB，正 = 变大）；<paramref name="touched"/> = 音轨是否被重编码。</summary>
    private static double EstimateAudioDeltaMb(SourceProfile src, TargetProfile dst, out bool touched)
    {
        touched = false;
        if (src.DurationSec <= 0) return 0;
        if (string.Equals(dst.AudioCodec, "copy", StringComparison.OrdinalIgnoreCase)) return 0;
        if (dst.AudioBitRateBps <= 0) return 0;
        if (src.AudioBitRate <= 0) return 0;

        touched = true;
        var deltaBits = (dst.AudioBitRateBps - src.AudioBitRate) * src.DurationSec;
        return deltaBits / 8 / 1024 / 1024;
    }

    private static string DescribeAudioDelta(double deltaMb)
        => deltaMb >= 0.5 ? $"+{deltaMb:0} MB"
         : deltaMb <= -0.5 ? $"−{Math.Abs(deltaMb):0} MB"
         : "≈无变化";

    private static string Signed(double percent)
        => percent >= 0 ? $"−{percent:0}%" : $"+{Math.Abs(percent):0}%";

    private static string CodecName(string? codec)
    {
        var c = (codec ?? "").ToLowerInvariant();
        if (c.Contains("265") || c.Contains("hevc")) return "x265";
        if (c.Contains("264") || c.Contains("avc")) return "x264";
        if (c.Contains("av1") || c.Contains("svt")) return "AV1";
        return codec ?? "目标编码";
    }

    private static string Mbps(double bps) => bps >= 1_000_000
        ? $"{bps / 1_000_000:0.0} Mbps"
        : $"{bps / 1000:0} kbps";

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
        >= 1L << 20 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B"
    };
}
