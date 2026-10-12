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

/// <summary>
/// ffmpeg / ffprobe 的调用封装（剪切与转码两页共用）。
/// <para>
/// 设计原则与 <see cref="VideoScanner"/> 一致：**不碰 UI、不改模型**，只负责"跑命令行 + 把结果返回"，
/// 由 ViewModel 在 UI 线程上更新任务状态 —— 这样批量运行时多个任务互不干扰。
/// </para>
/// <para>
/// ⚠️ 诚实边界：ffmpeg 没配好（PATH 里没有 / 路径不对）时返回明确的错误信息，
/// 页面照实显示"找不到 ffmpeg —— 去「设置 → 路径配置」里指定"，绝不假装跑过。
/// </para>
/// </summary>
public static class FfmpegService
{
    /// <summary>
    /// 生效的 ffmpeg 路径：配置里的值优先，留空则裸名 <c>ffmpeg</c>（走 PATH）。
    /// </summary>
    public static string ResolveFfmpeg(string? configured)
    {
        var p = (configured ?? "").Trim();
        return p.Length == 0 ? "ffmpeg" : p;
    }

    /// <summary>一次性探活：ffmpeg 到底能不能跑起来（页面据此给出明确提示）。</summary>
    public static async Task<(bool Ok, string Message)> CheckAsync(string ffmpeg)
    {
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
            psi.ArgumentList.Add("-version");

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return (false, "无法启动 ffmpeg。");

            var outTask = proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync().ConfigureAwait(false);
            var text = await outTask.ConfigureAwait(false);

            return proc.ExitCode == 0
                ? (true, (text.Split('\n').FirstOrDefault() ?? "ffmpeg 可用").Trim())
                : (false, $"ffmpeg 退出码 {proc.ExitCode}（可在「设置 → 路径配置」里指定 ffmpeg 路径）");
        }
        catch (Exception ex)
        {
            return (false, $"找不到 ffmpeg：{ex.Message} —— 可在「设置 → 路径配置」里指定 ffmpeg.exe 的完整路径。");
        }
    }

    // ==================== 关键帧 / 预览 ====================

    /// <summary>
    /// 读出源视频的<b>关键帧时间点</b>（最多 <paramref name="max"/> 个）—— 预览图就抽这些时刻，
    /// 抽关键帧比随机时间点快得多（不用先解码到那一帧）。
    /// <para>命令：<c>ffprobe -select_streams v:0 -skip_frame nokey -show_entries frame=pts_time -of csv=p=0</c>。</para>
    /// </summary>
    public static async Task<List<double>> ReadKeyFrameTimesAsync(string ffprobe, string source, int max = 12)
    {
        var list = new List<double>();
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return list;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in new[]
                     {
                         "-v", "error",
                         "-select_streams", "v:0",
                         "-skip_frame", "nokey",
                         "-show_entries", "frame=pts_time",
                         "-of", "csv=p=0",
                         source
                     }) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return list;

            var outTask = proc.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { TryKill(proc); }

            var text = await outTask.ConfigureAwait(false);
            foreach (var line in (text ?? "").Split('\n'))
            {
                var s = line.Trim().TrimEnd(',');
                if (s.Length == 0) continue;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t >= 0)
                    list.Add(t);
                if (list.Count >= max) break;
            }
        }
        catch { /* 读不出关键帧就交给调用方按等分时间点抽帧 */ }

        return list;
    }

    /// <summary>
    /// 抽一帧存成 jpg（预览用）。
    /// <para>命令：<c>ffmpeg -ss &lt;t&gt; -i &lt;src&gt; -frames:v 1 -q:v 3 &lt;out&gt; -y</c>
    /// （<c>-ss</c> 放在 <c>-i</c> 前面 = 快速定位，配合关键帧几乎瞬间完成）。</para>
    /// </summary>
    public static async Task<bool> ExtractFrameAsync(string ffmpeg, string source, double timeSec, string outputFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

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
                         "-hide_banner", "-loglevel", "error",
                         "-ss", timeSec.ToString("0.###", CultureInfo.InvariantCulture),
                         "-i", source,
                         "-frames:v", "1",
                         "-q:v", "3",
                         "-y", outputFile
                     }) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return false;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { TryKill(proc); return false; }

            return proc.ExitCode == 0 && File.Exists(outputFile);
        }
        catch { return false; }
    }

    // ==================== 命令行拼装 ====================

    /// <summary>
    /// 拼剪切命令的参数（不含 <c>ffmpeg</c> 本身）。
    /// <para><paramref name="output"/> 传 <c>null</c> → **只拼参数、不追加输出路径**（给"命令预览"用，
    /// 输出由调用方统一补在末尾，保证"看到的"和"执行的"是同一套）。</para>
    /// </summary>
    public static List<string> BuildCutArgs(CutTask task, string? output)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };

        // -ss 放前面 = 先定位（快）；-to 是绝对结束时间
        args.AddRange(new[] { "-ss", task.Start.ToString("0.###", CultureInfo.InvariantCulture) });
        args.AddRange(new[] { "-i", task.SourcePath });
        if (task.End > task.Start)
            args.AddRange(new[] { "-to", (task.End - task.Start).ToString("0.###", CultureInfo.InvariantCulture) });

        // ① 预设
        if (string.Equals(task.PresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase) || task.ReEncode)
        {
            args.AddRange(new[] { "-c:v", "libx264", "-crf", task.Crf.ToString(CultureInfo.InvariantCulture),
                                  "-preset", "veryfast", "-c:a", "aac", "-b:a", "192k" });
        }
        else
        {
            args.AddRange(new[] { "-c", "copy" });
        }

        // ② 自定义参数**追加在预设之后** —— ffmpeg 里后出现的同名参数覆盖前面的，
        //    所以用户填 `-c:v libx265` 之类能直接顶掉预设，不用另做一套"覆盖模式"。
        var extra = (task.ExtraArgs ?? "").Trim();
        if (extra.Length > 0)
        {
            foreach (var a in SplitArgs(extra)) args.Add(a);
        }

        AppendOutput(args, output);
        return args;
    }

    /// <summary>输出路径非空才追加（拼"纯参数"时传 null）。</summary>
    private static void AppendOutput(List<string> args, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output)) args.Add(output);
    }

    /// <summary>把一行自定义参数拆成参数数组（支持英文双引号包起来的带空格参数）。</summary>
    public static List<string> SplitArgs(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var cur = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var ch in text)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
                continue;
            }
            cur.Append(ch);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    /// <summary>
    /// 把一条剪切任务拼成**完整的 ffmpeg 命令行文本**（给界面预览 / 复制用，不执行）。
    /// 与 <see cref="BuildCutArgs"/> 同源，界面上看到的就是真正要跑的那条。
    /// </summary>
    public static string DescribeCutCommand(CutTask task, string ffmpeg, string output)
    {
        var args = BuildCutArgs(task, output);
        var sb = new System.Text.StringBuilder();
        sb.Append('"').Append(ffmpeg).Append('"');
        foreach (var a in args)
        {
            sb.Append(' ');
            if (a.Length > 0 && !a.Contains(' ') && a != output) sb.Append(a);
            else sb.Append('"').Append(a).Append('"');
        }
        return sb.ToString();
    }

    /// <summary>常见输出容器（ffmpeg **靠输出文件的扩展名**判断容器，所以它同时决定默认输出名后缀）。</summary>
    public static IReadOnlyList<string> Containers { get; } = new[] { ".mp4", ".mkv", ".mov", ".webm", ".avi" };

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
    /// 「不超 WxH」过滤器（v3.19 / NT-013 默认宽高）：源**大于** WxH 才缩到框内、**小于等于**保持原始大小，
    /// 且保持宽高比 —— <c>force_original_aspect_ratio=decrease</c> 只缩不放。
    /// </summary>
    public static string FitFilter(int width, int height)
        => $"scale={width}:{height}:force_original_aspect_ratio=decrease";

    /// <summary>拼转码命令的参数（不含 <c>ffmpeg</c> 本身）。支持"既剪辑又转码"（<see cref="TranscodeTask.HasTrim"/>）。</summary>
    public static List<string> BuildTranscodeArgs(TranscodeTask task, string? output)
    {
        var preset = task.Preset;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };

        // ① 区间（"既剪辑又转码"）：-ss 放在 -i **前面** = 关键帧快速定位；-to 是相对时长
        //    v3.16：终点留空（0）= 从起点一直到文件末尾，此时不能加 -to（否则算出负数时长）。
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

        var baseCodec = preset.VideoCodec ?? "";
        var codec = ResolveEncoder(baseCodec, task.UseGpuEncoder);

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

            // ② 码率：目标码率 或 恒定质量
            if (string.Equals(task.BitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase)
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

            // ④ 视频过滤器：旋转 / 翻转（v3.19 / NT-012）+ 分辨率，合并进同一个 -vf（逗号分隔、按序生效；
            //    旋转放前面——transpose 会交换宽高，之后的 scale 作用在旋转后的尺寸上）
            var filters = new List<string>();
            var rotate = RotateOptions.ToFilter(task.Rotate);
            if (rotate.Length > 0) filters.Add(rotate);
            if (task.TargetWidth > 0 || task.TargetHeight > 0)
                filters.Add(ScaleFilter(task.TargetWidth, task.TargetHeight));
            else if (task.MaxHeight > 0)
                filters.Add($"scale=-2:{task.MaxHeight}");
            else if (task.DefaultWidth > 0 && task.DefaultHeight > 0)
                filters.Add(FitFilter(task.DefaultWidth, task.DefaultHeight));
            if (filters.Count > 0)
                args.AddRange(new[] { "-vf", string.Join(",", filters) });

            // ⑤ 音频
            if (string.IsNullOrWhiteSpace(preset.AudioCodec))
                args.AddRange(new[] { "-an" });
            else
            {
                args.AddRange(new[] { "-c:a", preset.AudioCodec });
                if (!string.IsNullOrWhiteSpace(preset.AudioBitrate))
                    args.AddRange(new[] { "-b:a", preset.AudioBitrate });
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

    /// <summary>
    /// 跑一条 ffmpeg 命令，并把进度（0~100）回调出去。
    /// <para>进度靠 <c>-progress pipe:1</c> 输出的 <c>out_time_ms</c> 除以总时长算出来 ——
    /// 这是 ffmpeg 官方给的机器可读进度，比抓 stderr 的刷新行稳。</para>
    /// </summary>
    /// <returns>(是否成功, 说明, 输出文件大小)。</returns>
    public static async Task<(bool Ok, string Message, long OutputSize)> RunAsync(
        string ffmpeg, List<string> args, string output, double totalSeconds,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
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
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("-progress");
            psi.ArgumentList.Add("pipe:1");
            psi.ArgumentList.Add("-nostats");

            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return (false, "无法启动 ffmpeg（可在「设置 → 路径配置」里检查路径）。", 0);

            var errTask = proc.StandardError.ReadToEndAsync();
            var last = -1;

            // 边跑边读进度（不能 ReadToEnd：那会等到进程结束）
            var readTask = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        if (line.StartsWith("out_time_ms=", StringComparison.Ordinal)
                            || line.StartsWith("out_time_us=", StringComparison.Ordinal))
                        {
                            var v = line[(line.IndexOf('=') + 1)..].Trim();
                            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var micros))
                            {
                                var seconds = line.StartsWith("out_time_us=", StringComparison.Ordinal)
                                    ? micros / 1_000_000.0
                                    : micros / 1000.0;
                                if (totalSeconds > 0)
                                {
                                    var pct = (int)Math.Clamp(seconds * 100.0 / totalSeconds, 0, 100);
                                    if (pct != last) { last = pct; progress?.Report(pct); }
                                }
                            }
                        }
                    }
                }
                catch { /* 管道关了就收 */ }
            }, CancellationToken.None);

            try
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                TryDelete(output);
                return (false, "已取消。", 0);
            }

            await readTask.ConfigureAwait(false);
            var err = (await errTask.ConfigureAwait(false) ?? "").Trim();

            if (proc.ExitCode != 0)
            {
                TryDelete(output);
                var tail = err.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
                if (string.IsNullOrEmpty(tail)) tail = $"退出码 {proc.ExitCode}";
                return (false, "ffmpeg 失败：" + tail, 0);
            }

            var size = File.Exists(output) ? new FileInfo(output).Length : 0;
            progress?.Report(100);
            return (true, "完成。", size);
        }
        catch (OperationCanceledException)
        {
            return (false, "已取消。", 0);
        }
        catch (Exception ex)
        {
            return (false, "运行失败：" + ex.Message, 0);
        }
    }

    private static void TryKill(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); }
        catch { /* 已退出 */ }
    }

    /// <summary>失败 / 取消时把半成品输出删掉，避免留下半截文件误导人。</summary>
    private static void TryDelete(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { /* 删不掉就算了 */ }
    }
}
