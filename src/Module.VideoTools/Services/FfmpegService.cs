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

/// <summary>ffmpeg / ffprobe 的调用封装（剪切与转码两页共用）。（WHY: VT-012）</summary>
public static partial class FfmpegService
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
        // 多段（≥2 段）= 逐段裁剪 + 拼成一条流。单条命令做不到"先切再拼"，
        // 所以走 -filter_complex（trim/atrim + concat）。⇒ 必须重新编码（copy 无法拼时间轴）。
        if (task.Segments.Count > 1) return BuildMultiCutArgs(task, output);

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
            // `-ss` 在 `-i` 前（快速定位）+ copy 时，首段的 DTS 可能早于 0，
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

    /// <summary>
    /// 多段剪切 + 拼接：一条 <c>-filter_complex</c> 命令 = 每段 trim/atrim → concat。
    /// <para>⚠️ 假设源有音轨：无音轨文件会因 <c>[0:a]</c> 不存在而报错（页面上如实显示 ffmpeg 的报错，不猜）。</para>
    /// </summary>
    private static List<string> BuildMultiCutArgs(CutTask task, string? output)
    {
        var segs = task.Segments;
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", task.SourcePath };

        var sb = new StringBuilder();
        for (var i = 0; i < segs.Count; i++)
        {
            sb.Append("[0:v]trim=start=").Append(Sec(segs[i].Start))
              .Append(":end=").Append(Sec(segs[i].End))
              .Append(",setpts=PTS-STARTPTS[v").Append(i).Append("];");
            sb.Append("[0:a]atrim=start=").Append(Sec(segs[i].Start))
              .Append(":end=").Append(Sec(segs[i].End))
              .Append(",asetpts=PTS-STARTPTS[a").Append(i).Append("];");
        }
        for (var i = 0; i < segs.Count; i++)
            sb.Append("[v").Append(i).Append("][a").Append(i).Append(']');
        sb.Append("concat=n=").Append(segs.Count).Append(":v=1:a=1[v][a]");

        args.AddRange(new[] { "-filter_complex", sb.ToString(), "-map", "[v]", "-map", "[a]" });

        var crf = task.Crf > 0 ? task.Crf : 20;
        args.AddRange(new[] { "-c:v", "libx264", "-crf", crf.ToString(CultureInfo.InvariantCulture),
                              "-preset", "veryfast", "-c:a", "aac", "-b:a", "192k" });

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

}
