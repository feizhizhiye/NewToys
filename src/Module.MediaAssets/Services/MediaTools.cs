using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// ffprobe / ffmpeg 的**最小封装**（NT-021）：模块内自建，⛔ 不引 <c>Module.VideoTools</c>（模块禁互引）。
/// <para>
/// 可执行文件路径取自全局设置 <see cref="IAppConfigService.FfmpegPath"/>（「设置 → 路径配置」里配）：
/// 配的是 <c>ffmpeg</c> 裸名 → 走 PATH；配的是全路径 → 同目录换 <c>ffprobe.exe</c>。
/// </para>
/// </summary>
internal static class MediaTools
{
    /// <summary>ffprobe 路径（由 ffmpeg 路径推导；推导不了就裸名走 PATH）。</summary>
    public static string FfprobePath(IAppConfigService? app)
    {
        var ffmpeg = (app?.FfmpegPath ?? "").Trim();
        if (ffmpeg.Length == 0) return "ffprobe";
        var name = Path.GetFileName(ffmpeg.TrimEnd('\\', '/'));
        if (name.Length == 0) return "ffprobe";
        if (name.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)) return "ffprobe";
        if (name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)) return "ffprobe.exe";
        return name + "probe";
    }

    /// <summary>ffmpeg 路径（配置为空 → 裸名走 PATH）。</summary>
    public static string FfmpegPath(IAppConfigService? app)
    {
        var ffmpeg = (app?.FfmpegPath ?? "").Trim();
        return ffmpeg.Length == 0 ? "ffmpeg" : ffmpeg;
    }

    /// <summary>
    /// 跑一个进程并收 stdout / stderr（⛔ 不抛异常：起不来 / 超时都变成返回码）。
    /// 返回 <c>Code</c>：0 = 成功；-1 = 起不来；-2 = 超时 / 取消。
    /// </summary>
    public static async Task<(int Code, string Out, string Err)> RunAsync(
        string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process? proc = null;
        try
        {
            proc = new Process { StartInfo = psi };
            if (!proc.Start()) return (-1, "", $"无法启动 {exe}。");
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            return (proc.ExitCode, await outTask.ConfigureAwait(false), await errTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            return (-2, "", ct.IsCancellationRequested ? "已取消。" : $"{exe} 执行超时，已中止。");
        }
        catch (Exception)
        {
            TryKill(proc);
            return (-1, "", $"{exe} 无法启动（可在「设置 → 路径配置」里指定 ffmpeg 路径）");
        }
        finally
        {
            proc?.Dispose();
        }
    }

    private static void TryKill(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); }
        catch { /* 进程已退出或已释放 */ }
    }
}
