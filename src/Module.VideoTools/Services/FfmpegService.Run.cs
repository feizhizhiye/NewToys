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
            // `-progress` / `-nostats` 是**全局选项**，必须放在输出路径之前 ——
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

    // ==================== 外部播放 ====================

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

    // ==================== 进度解析 ====================

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
