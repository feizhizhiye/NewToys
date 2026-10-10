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

    /// <summary>拼剪切命令的参数（不含 <c>ffmpeg</c> 本身）。</summary>
    public static List<string> BuildCutArgs(CutTask task, string output)
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

        args.Add(output);
        return args;
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

    /// <summary>拼转码命令的参数（不含 <c>ffmpeg</c> 本身）。</summary>
    public static List<string> BuildTranscodeArgs(TranscodeTask task, string output)
    {
        var preset = task.Preset;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", task.SourcePath };

        var crf = task.Crf > 0 ? task.Crf : preset.Crf;

        if (string.Equals(preset.VideoCodec, "copy", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-c", "copy" });
        }
        else if (preset.VideoCodec.Length == 0)
        {
            // 纯音频（只提取音轨）
            args.AddRange(new[] { "-vn" });
            args.AddRange(new[] { "-c:a", preset.AudioCodec });
            if (!string.IsNullOrWhiteSpace(preset.AudioBitrate))
                args.AddRange(new[] { "-b:a", preset.AudioBitrate });
        }
        else
        {
            args.AddRange(new[] { "-c:v", preset.VideoCodec });
            if (crf > 0)
            {
                // libsvtav1 用 -crf；libx264 / libx265 也是 -crf（这里统一）
                args.AddRange(new[] { "-crf", crf.ToString(CultureInfo.InvariantCulture) });
            }
            if (!string.IsNullOrWhiteSpace(preset.Speed))
            {
                args.AddRange(preset.VideoCodec == "libsvtav1"
                    ? new[] { "-preset", preset.Speed }
                    : new[] { "-preset", preset.Speed });
            }

            // 缩放：只约束最大高度，宽度按比例（-2 保证偶数）
            if (task.MaxHeight > 0)
                args.AddRange(new[] { "-vf", $"scale=-2:{task.MaxHeight}" });

            if (string.IsNullOrWhiteSpace(preset.AudioCodec))
                args.AddRange(new[] { "-an" });
            else
            {
                args.AddRange(new[] { "-c:a", preset.AudioCodec });
                if (!string.IsNullOrWhiteSpace(preset.AudioBitrate))
                    args.AddRange(new[] { "-b:a", preset.AudioBitrate });
            }
        }

        args.Add(output);
        return args;
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
