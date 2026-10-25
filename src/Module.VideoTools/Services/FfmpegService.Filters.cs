using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

public static partial class FfmpegService
{

    /// <summary>
    /// CPU 编码器 → GPU 编码器映射（NVIDIA NVENC 系）。
    /// <para>映射不上（copy / 纯音频 / 未知编码器）就**保持原样** —— ⛔ 不做"悄悄换一个"，
    /// 跑不起来就让 ffmpeg 自己报错，页面如实显示。</para>
    /// </summary>
    public static string ResolveEncoder(string cpuCodec, bool useGpu)
    {
        // 先兜底再判断：写成 `(cpuCodec ?? "") switch { ... _ => cpuCodec }` 会让编译器
        // 把 cpuCodec 的 null-state 推断成 maybe-null，兜底臂反而报 CS8603。
        var codec = cpuCodec ?? "";
        if (!useGpu) return codec;
        return codec switch
        {
            "libx264"   => "h264_nvenc",
            "libx265"   => "hevc_nvenc",
            "libsvtav1" => "av1_nvenc",
            _           => codec
        };
    }

    /// <summary>质量参数名：NVENC 系列是 <c>-cq</c>，其余（x264/x265/svtav1）是 <c>-crf</c>。</summary>
    private static string QualityArg(string codec)
        => codec.EndsWith("_nvenc", StringComparison.Ordinal) ? "-cq" : "-crf";

    /// <summary>秒数 → ffmpeg 认的定点文本（不写 ToString() 默认格式，避免文化差异出逗号小数）。</summary>
    private static string Sec(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>缩放过滤器：宽 / 高任一为 0 就按另一边等比（-2 保证偶数）。调用方保证至少一个 &gt; 0。</summary>
    public static string ScaleFilter(int width, int height)
        => width > 0 && height > 0 ? $"scale={width}:{height}"
         : height > 0 ? $"scale=-2:{height}"
         : $"scale={width}:-2";

    /// <summary>
    /// 「不超 WxH」过滤器（v3.19：默认宽高）：源**大于** WxH 才缩到框内、**小于等于**保持原始大小，
    /// 且保持宽高比 —— <c>force_original_aspect_ratio=decrease</c> 只缩不放。
    /// </summary>
    public static string FitFilter(int width, int height)
        => $"scale={width}:{height}:force_original_aspect_ratio=decrease";

    /// <summary>是不是 GIF 预设（`TranscodeTask` 走独立命令分支，详见 <see cref="BuildTranscodeArgs"/>）。</summary>
    public static bool IsGif(TranscodePreset preset)
        => string.Equals(preset.Id, "gif", StringComparison.OrdinalIgnoreCase)
        || string.Equals(preset.VideoCodec, "gif", StringComparison.OrdinalIgnoreCase);

    // ==================== 字幕 ====================

    /// <summary>输出扩展名（**软字幕固定 <c>.mkv</c>**、GIF 固定 <c>.gif</c>，其余按容器）。</summary>
    public static string OutputExtension(TranscodeTask task)
    {
        if (IsGif(task.Preset)) return ".gif";
        if (task.IsSoftSubtitle) return ".mkv";
        return task.ContainerDisplay;
    }

    /// <summary>
    /// 裁剪滤镜：<c>crop=w:h:x:y</c>。
    /// <para>🔴 宽高**必须取偶**：H.264/H.265 的 yuv420p 色度是 2×2 采样，奇数边长直接被编码器拒绝。</para>
    /// </summary>
    public static string CropFilter(TranscodeTask task)
    {
        if (!task.IsCropping) return "";
        var w = Math.Max(2, task.CropWidth - task.CropWidth % 2);
        var h = Math.Max(2, task.CropHeight - task.CropHeight % 2);
        return $"crop={w}:{h}:{Math.Max(0, task.CropX)}:{Math.Max(0, task.CropY)}";
    }

    /// <summary>音量滤镜：<c>volume=6dB</c> / <c>volume=-6dB</c>（0 不产生滤镜）。</summary>
    public static string VolumeFilter(double db)
        => Math.Abs(db) < 0.001 ? "" : "volume=" + db.ToString("0.#", CultureInfo.InvariantCulture) + "dB";

    /// <summary>
    /// 按**目标体积**反算视频码率，单位 kbps；返回 0 = 算不出来（没填体积 / 时长未知）。
    /// <para>算法：<c>MB × 8192 ÷ 秒数 = 总 kbps</c>（1 MB = 8192 kbit），再扣掉音频码率与容器开销。</para>
    /// </summary>
    public static int AutoBitrateKbps(TranscodeTask task)
    {
        if (task.TargetSizeMb <= 0 || task.SourceDuration <= 0) return 0;
        var audio = task.IsAudioMuted ? 0 : ParseKbps(task.Preset.AudioBitrate);
        var total = (int)(task.TargetSizeMb * 8192 / task.SourceDuration);
        var video = total - audio - 8;   // 8kbps ≈ 容器 / 索引开销
        return Math.Max(50, video);
    }

    /// <summary>把 <c>192k</c> / <c>1M</c> 这类码率文本转成 kbps。</summary>
    private static int ParseKbps(string? text)
    {
        var s = (text ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return 0;
        var mult = 1;
        if (s.EndsWith('k')) { s = s[..^1]; }
        else if (s.EndsWith('m')) { s = s[..^1]; mult = 1000; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? (int)(v * mult)
            : 0;
    }

    /// <summary>
    /// 探测黑边：跑 2 秒 <c>cropdetect</c>，解析最后一条 <c>crop=W:H:X:Y</c>。
    /// <para>返回 null = 探测失败 / 取不到（源无画面 / ffmpeg 缺失）—— 页面照实提示，⛔ 不编一个值。</para>
    /// </summary>
    public static async Task<(int Width, int Height, int X, int Y)?> DetectCropAsync(
        string ffmpeg, string source, double atSec, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ffmpeg) || string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in new[]
                     {
                         "-hide_banner", "-nostats",
                         "-ss", Sec(Math.Max(0, atSec)), "-t", "2",
                         "-i", source,
                         "-vf", "cropdetect=24:2:0",
                         "-f", "null", "-"
                     })
                psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return null;

            var errTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var err = await errTask.ConfigureAwait(false) ?? "";

            // cropdetect 的日志在 stderr：… crop=1920:1080:0:0（帧越靠后越稳定，取最后一条）
            (int W, int H, int X, int Y)? last = null;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(err, @"crop=(\d+):(\d+):(\d+):(\d+)"))
            {
                last = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                        int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
            }
            return last;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 硬字幕滤镜：<c>subtitles='路径':force_style='FontSize=22,MarginV=24'</c>。
    /// <para>
    /// 🔴 路径必须转成 <c>/</c> 并把 <c>:</c> 转义成 <c>:</c>（前面加反斜杠）—— filtergraph 用 <c>:</c> 分参数，
    /// 不转义时 <c>C:\...</c> 会被截成两段；整个值再用单引号包住，逗号才不会被当成滤镜分隔符。
    /// </para>
    /// </summary>
    public static string SubtitlesFilter(TranscodeTask task)
    {
        var path = (task.SubtitlePath ?? "").Trim().Replace('\\', '/').Replace(":", "\\:").Replace("'", "\\'");
        var style = new List<string>();
        if (task.SubtitleFontSize > 0) style.Add("FontSize=" + task.SubtitleFontSize.ToString(CultureInfo.InvariantCulture));
        if (task.SubtitleMarginV > 0) style.Add("MarginV=" + task.SubtitleMarginV.ToString(CultureInfo.InvariantCulture));

        var filter = $"subtitles='{path}'";
        if (style.Count > 0) filter += ":force_style='" + string.Join(",", style) + "'";
        return filter;
    }

    /// <summary>
    /// GIF 滤镜链：定帧率 → 定宽（<c>-1</c> = 按比例，保证偶数不用管，gif 接受奇数）→ 单遍调色板。
    /// <para><c>palettegen=stats_mode=diff</c> + <c>paletteuse=dither=bayer</c> 是画质/体积的常用折中。</para>
    /// </summary>
    public static string GifFilterChain(TranscodeTask task)
    {
        var fps = task.FrameRate > 0 ? task.FrameRate : 12;
        var head = task.GifWidth > 0 ? $"fps={fps},scale={task.GifWidth}:-1:flags=lanczos" : $"fps={fps}";
        return head + ",split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5";
    }

    /// <summary>区间 + 输入（剪切与转码共用；<c>-ss</c> 必须在 <c>-i</c> 前才是快速定位）。</summary>
    private static void AppendTrimAndInput(List<string> args, TranscodeTask task)
    {
        if (task.HasTrim)
        {
            args.AddRange(new[] { "-ss", Sec(task.TrimStart) });
            args.AddRange(new[] { "-i", task.SourcePath });
            if (task.TrimEnd > task.TrimStart)
                args.AddRange(new[] { "-to", Sec(task.TrimEnd - task.TrimStart) });
        }
        else
        {
            args.AddRange(new[] { "-i", task.SourcePath });
        }
    }

}
