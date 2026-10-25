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

    /// <summary>拼转码命令的参数（不含 <c>ffmpeg</c> 本身）。支持"既剪辑又转码"（<see cref="TranscodeTask.HasTrim"/>）。</summary>
    public static List<string> BuildTranscodeArgs(TranscodeTask task, string? output)
    {
        var preset = task.Preset;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };

        // ① 区间（"既剪辑又转码"）：-ss 放在 -i **前面** = 关键帧快速定位；-to 是相对时长
        //    终点留空（0）= 从起点一直到文件末尾，此时不能加 -to（否则算出负数时长）。
        AppendTrimAndInput(args, task);

        // ①-b 替换音轨：音频文件作为**第二条输入**，视频仍取 0 号输入的源片。
        //     🔴 必须在所有输出选项之前加 -i；软字幕分支自己也要 -i（两者互斥，预检会拦）。
        if (task.IsReplaceAudio && !task.IsSoftSubtitle && !string.IsNullOrWhiteSpace(task.ReplaceAudioPath))
            args.AddRange(new[] { "-i", task.ReplaceAudioPath });

        // ② 软字幕：**只封装** —— 视频/音频原样 copy，字幕作为第 2 条输入单独成轨。
        //    容器必须是 .mkv（mp4 装不了 srt 轨），所以编码/分辨率等设置在本模式下不生效（详情卡有提示）。
        if (task.IsSoftSubtitle)
        {
            args.AddRange(new[]
            {
                "-i", task.SubtitlePath,
                "-map", "0", "-map", "1",
                "-c", "copy", "-c:s", "srt",
                "-metadata:s:s:0", "language=chi",
                "-disposition:s:0", "default",
                "-avoid_negative_ts", "make_zero"
            });
            AppendOutput(args, output);
            return args;
        }

        // ② GIF：gif 编码器不吃 -crf / -preset / 码率 / 音频参数 ⇒ 走独立分支。
        //    单遍调色板（split 同图）：一条命令 = 看到的即执行的，⛔ 不做两遍临时文件（预览不可见会骗人）；
        //    代价是整段帧要在内存里过一遍 ⇒ 预检（CommandLint）会提示「GIF 建议 ≤30 秒」。
        if (IsGif(preset))
        {
            args.AddRange(new[] { "-an", "-c:v", "gif", "-loop", "0" });
            // GIF 也要吃旋转 / 裁剪（去黑边做动图是常见组合），再挂调色板链
            var gifFilters = new List<string>();
            var gifRotate = RotateOptions.ToFilter(task.Rotate);
            if (gifRotate.Length > 0) gifFilters.Add(gifRotate);
            var gifCrop = CropFilter(task);
            if (gifCrop.Length > 0) gifFilters.Add(gifCrop);
            gifFilters.Add(GifFilterChain(task));
            args.AddRange(new[] { "-vf", string.Join(",", gifFilters) });
            AppendOutput(args, output);
            return args;
        }

        var baseCodec = preset.VideoCodec ?? "";
        // 硬字幕/ 裁剪/ 音轨处理/ 目标体积都必须重新编码：
        // copy 预设自动升级成 libx264（否则滤镜和码率控制都没有落脚处）
        var needsReEncode = task.IsHardSubtitle || task.IsCropping || task.HasTargetSize
                            || task.IsReplaceAudio || task.IsVolumeMode;
        if (needsReEncode && string.Equals(baseCodec, "copy", StringComparison.OrdinalIgnoreCase))
            baseCodec = "libx264";
        var codec = ResolveEncoder(baseCodec, task.UseGpuEncoder);

        // 替换音轨要显式 map：视频取第一条输入的图像，音频取第二条输入（否则 ffmpeg 会同时带上原音轨）
        if (task.IsReplaceAudio && !string.IsNullOrWhiteSpace(task.ReplaceAudioPath))
            args.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0" });

        if (string.Equals(baseCodec, "copy", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-c", "copy" });
        }
        else if (baseCodec.Length == 0)
        {
            // 纯音频（只提取音轨）
            args.AddRange(new[] { "-vn" });
            args.AddRange(new[] { "-c:a", preset.AudioCodec });
            if (!string.IsNullOrWhiteSpace(preset.AudioBitrate))
                args.AddRange(new[] { "-b:a", preset.AudioBitrate });
        }
        else
        {
            args.AddRange(new[] { "-c:v", codec });

            // ② 码率：目标体积（反算，最高优先） > 目标码率 > 恒定质量
            var sizeKbps = AutoBitrateKbps(task);
            if (sizeKbps > 0)
            {
                args.AddRange(new[]
                {
                    "-b:v", sizeKbps.ToString(CultureInfo.InvariantCulture) + "k",
                    "-maxrate", (sizeKbps * 3 / 2).ToString(CultureInfo.InvariantCulture) + "k",
                    "-bufsize", (sizeKbps * 2).ToString(CultureInfo.InvariantCulture) + "k"
                });
            }
            else if (string.Equals(task.BitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(task.TargetBitrate))
            {
                args.AddRange(new[] { "-b:v", task.TargetBitrate.Trim() });
            }
            else
            {
                var quality = task.Crf > 0 ? task.Crf : preset.Crf;
                if (quality > 0) args.AddRange(new[] { QualityArg(codec), quality.ToString(CultureInfo.InvariantCulture) });
            }

            // ③ 速度档：只给 CPU 编码器（nvenc 的 -preset 取值语义不同，不猜）
            if (!task.UseGpuEncoder && !string.IsNullOrWhiteSpace(preset.Speed))
            {
                // libsvtav1 的 -preset 取数字档（如 8），libx264/265 取 veryfast~veryslow；预设里已经按编码器配好
                args.AddRange(new[] { "-preset", preset.Speed });
            }

            // ④ 视频过滤器：旋转 / 翻转（v3.19）+ 分辨率，合并进同一个 -vf（逗号分隔、按序生效；
            //    旋转放前面——transpose 会交换宽高，之后的 scale 作用在旋转后的尺寸上）
            var filters = new List<string>();
            var rotate = RotateOptions.ToFilter(task.Rotate);
            if (rotate.Length > 0) filters.Add(rotate);
            // 裁剪紧跟旋转：transpose 会换宽高，坐标必须作用在旋转后的画幅上；
            // 又必须在缩放**之前**——先缩再裁，用户填的像素坐标就对不上了。
            var crop = CropFilter(task);
            if (crop.Length > 0) filters.Add(crop);
            if (task.TargetWidth > 0 || task.TargetHeight > 0)
                filters.Add(ScaleFilter(task.TargetWidth, task.TargetHeight));
            else if (task.MaxHeight > 0)
                filters.Add($"scale=-2:{task.MaxHeight}");
            else if (task.DefaultWidth > 0 && task.DefaultHeight > 0)
                filters.Add(FitFilter(task.DefaultWidth, task.DefaultHeight));
            // 硬字幕放最后：先缩放 / 旋转，再在**最终画幅**上烧字（否则字会跟着被缩放）
            if (task.IsHardSubtitle) filters.Add(SubtitlesFilter(task));
            if (filters.Count > 0)
                args.AddRange(new[] { "-vf", string.Join(",", filters) });

            // ⑤ 音频（静音 / 替换 / 音量增益都优先于预设的音频设置）
            if (task.IsAudioMuted)
            {
                args.AddRange(new[] { "-an" });
            }
            else if (task.IsReplaceAudio && !string.IsNullOrWhiteSpace(task.ReplaceAudioPath))
            {
                // 换进来的音频统一转 aac（源可能是 mp3 / flac / wav；-c copy 换个容器就废）
                args.AddRange(new[] { "-c:a", "aac" });
                if (!string.IsNullOrWhiteSpace(preset.AudioBitrate))
                    args.AddRange(new[] { "-b:a", preset.AudioBitrate });
            }
            else if (string.IsNullOrWhiteSpace(preset.AudioCodec))
            {
                args.AddRange(new[] { "-an" });
            }
            else
            {
                var acodec = preset.AudioCodec;
                // 🔴 -af 只对**重编码**的音频有效：copy 预设 + 音量增益 ⇒ 音频改 aac
                if (task.IsVolumeMode && string.Equals(acodec, "copy", StringComparison.OrdinalIgnoreCase))
                    acodec = "aac";
                args.AddRange(new[] { "-c:a", acodec });
                if (!string.IsNullOrWhiteSpace(preset.AudioBitrate))
                    args.AddRange(new[] { "-b:a", preset.AudioBitrate });
            }

            // 音量增益（静音时没音轨可调，跳过）
            if (task.IsVolumeMode && !task.IsAudioMuted)
            {
                var volume = VolumeFilter(task.VolumeDb);
                if (volume.Length > 0) args.AddRange(new[] { "-af", volume });
            }
        }

        AppendOutput(args, output);
        return args;
    }

    /// <summary>
    /// 把 <c>ffmpeg</c> + 参数拼成**一行完整命令行文本**（给界面预览 / 复制用，不执行）。
    /// 含空格的参数自动加引号；输出路径通常在调用方的参数列表末尾。
    /// </summary>
    public static string DescribeCommand(string ffmpeg, IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(ffmpeg).Append('"');
        foreach (var a in args)
        {
            sb.Append(' ');
            if (a.Length > 0 && !a.Contains(' ')) sb.Append(a);
            else sb.Append('"').Append(a).Append('"');
        }
        return sb.ToString();
    }

    /// <summary>给"输出路径留空"的任务算一个默认输出名（源目录旁边 + 后缀）。</summary>
    public static string DefaultOutputPath(string source, string suffix, string extension)
    {
        var dir = Path.GetDirectoryName(source) ?? "";
        var name = Path.GetFileNameWithoutExtension(source);
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var candidate = Path.Combine(dir, name + suffix + ext);

        // 撞名就加序号，绝不覆盖已有文件
        var i = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{name}{suffix}_{i}{ext}");
            i++;
            if (i > 999) break;
        }
        return candidate;
    }

    // ==================== 执行（带进度） ====================

}
