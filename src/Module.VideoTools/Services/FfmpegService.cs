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
            // NT-098：`-ss` 在 `-i` 前（快速定位）+ copy 时，首段的 DTS 可能早于 0，
            // 播放器表现是「开头黑屏 / 卡一下」。make_zero 把时间轴挪到 0 起。
            args.AddRange(new[] { "-avoid_negative_ts", "make_zero" });
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

    /// <summary>是不是 GIF 预设（`TranscodeTask` 走独立命令分支，详见 <see cref="BuildTranscodeArgs"/>）。</summary>
    public static bool IsGif(TranscodePreset preset)
        => string.Equals(preset.Id, "gif", StringComparison.OrdinalIgnoreCase)
        || string.Equals(preset.VideoCodec, "gif", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>拼转码命令的参数（不含 <c>ffmpeg</c> 本身）。支持"既剪辑又转码"（<see cref="TranscodeTask.HasTrim"/>）。</summary>
    public static List<string> BuildTranscodeArgs(TranscodeTask task, string? output)
    {
        var preset = task.Preset;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };

        // ① 区间（"既剪辑又转码"）：-ss 放在 -i **前面** = 关键帧快速定位；-to 是相对时长
        //    v3.16：终点留空（0）= 从起点一直到文件末尾，此时不能加 -to（否则算出负数时长）。
        AppendTrimAndInput(args, task);

        // ② GIF（NT-099）：gif 编码器不吃 -crf / -preset / 码率 / 音频参数 ⇒ 走独立分支。
        //    单遍调色板（split 同图）：一条命令 = 看到的即执行的，⛔ 不做两遍临时文件（预览不可见会骗人）；
        //    代价是整段帧要在内存里过一遍 ⇒ 预检（CommandLint）会提示「GIF 建议 ≤30 秒」。
        if (IsGif(preset))
        {
            args.AddRange(new[] { "-an", "-c:v", "gif", "-loop", "0" });
            args.AddRange(new[] { "-vf", GifFilterChain(task) });
            AppendOutput(args, output);
            return args;
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
            // NT-098：`-progress` / `-nostats` 是**全局选项**，必须放在输出路径之前 ——
            // 追加到参数末尾（输出路径之后）时 ffmpeg 会告警 "Trailing option(s) found in the command"，
            // 且不保证被识别（进度会掉）。所以先写全局项，再写业务参数。
            psi.ArgumentList.Add("-progress");
            psi.ArgumentList.Add("pipe:1");
            psi.ArgumentList.Add("-nostats");
            foreach (var a in args) psi.ArgumentList.Add(a);

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
                        if (totalSeconds <= 0) continue;
                        if (!TryReadProgressSeconds(line, out var seconds)) continue;

                        var pct = (int)Math.Clamp(seconds * 100.0 / totalSeconds, 0, 100);
                        if (pct != last) { last = pct; progress?.Report(pct); }
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

    // ==================== 外部播放（NT-098） ====================

    /// <summary>
    /// 用外部程序播放一个文件：**配置的播放器优先**，其次 `ffplay`（与 ffmpeg 同目录 → PATH）。
    /// <para>失败时返回原因（页面照实显示），⛔ 不静默吞掉。参数用 <see cref="ProcessStartInfo.ArgumentList"/>
    /// 传递，中文 / 空格路径不需要额外转义。</para>
    /// </summary>
    public static (bool Ok, string Message) Play(string file, string? configuredPlayer, string? ffmpeg)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return (false, "要播放的文件不存在。");

        var candidates = new List<(string Exe, string Label)>();
        var player = (configuredPlayer ?? "").Trim();
        if (player.Length > 0) candidates.Add((player, "外部播放器"));

        var ffplay = DeriveSibling(ffmpeg, "ffplay");
        candidates.Add((ffplay, "ffplay"));

        var errors = new List<string>();
        foreach (var (exe, label) in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                // ffplay 播完自动关窗口；外部播放器（PotPlayer / VLC 等）直接给路径
                if (string.Equals(label, "ffplay", StringComparison.Ordinal))
                {
                    psi.ArgumentList.Add("-autoexit");
                    psi.ArgumentList.Add("-loglevel");
                    psi.ArgumentList.Add("error");
                }
                psi.ArgumentList.Add(file);

                using var proc = Process.Start(psi);
                if (proc != null) return (true, $"已用{label}打开：{Path.GetFileName(file)}");
                errors.Add($"{label} 启动失败");
            }
            catch (Exception ex)
            {
                errors.Add($"{label}：{ex.Message}");
            }
        }

        return (false, "没有可用的播放器 —— 去「设置 → 路径配置」指定播放器，或把 ffplay.exe 放到 ffmpeg 同目录。"
                       + (errors.Count > 0 ? "（" + string.Join("；", errors) + "）" : ""));
    }

    /// <summary>同目录下找兄弟可执行文件（`ffmpeg.exe` → `ffplay.exe`；裸名则原样交给 PATH）。</summary>
    private static string DeriveSibling(string? tool, string sibling)
    {
        var p = (tool ?? "").Trim();
        if (p.Length == 0) return sibling;

        try
        {
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? sibling + ".exe" : sibling);
                if (File.Exists(exe)) return exe;
            }
        }
        catch { /* 路径怪就退回 PATH */ }

        return sibling;
    }

    // ==================== 进度解析（NT-098） ====================

    /// <summary>
    /// 从 <c>-progress</c> 的一行里读出「已输出秒数」。
    /// <para>🔴 只认 <c>out_time_us</c>（微秒，单位明确）与 <c>out_time</c>（时间码）——
    /// <c>out_time_ms</c> 在不少 ffmpeg 版本里实际输出的是**微秒**（历史 bug），按毫秒解释会算错 1000 倍，
    /// 所以直接跳过。</para>
    /// </summary>
    private static bool TryReadProgressSeconds(string line, out double seconds)
    {
        seconds = 0;
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal))
        {
            var v = line[(line.IndexOf('=') + 1)..].Trim();
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var micros)) return false;
            if (micros < 0) micros = 0;   // 开头常见负值
            seconds = micros / 1_000_000.0;
            return true;
        }

        if (line.StartsWith("out_time=", StringComparison.Ordinal))
        {
            var v = line[(line.IndexOf('=') + 1)..].Trim();
            return TryParseTimecode(v, out seconds);
        }

        return false;
    }

    /// <summary>解析 <c>HH:MM:SS.ffffff</c>（ffmpeg 进度里的时间码；<c>N/A</c> 视为无效）。</summary>
    private static bool TryParseTimecode(string text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = text.Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return false;

        seconds = Math.Max(0, h * 3600 + m * 60 + s);
        return true;
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
