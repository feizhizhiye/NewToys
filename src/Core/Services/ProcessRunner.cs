using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>IProcessRunner 实现。</summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>进程退出后，等输出读完的宽限期。（WHY: CR-041）</summary>
    private static readonly TimeSpan OutputDrainGrace = TimeSpan.FromMilliseconds(500);

    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: request.StrictOutput),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
        };

        if (request.ArgumentList is { } list)
            foreach (var a in list) psi.ArgumentList.Add(a);
        else if (!string.IsNullOrEmpty(request.Arguments))
            psi.Arguments = request.Arguments!;

        var wd = (request.WorkingDirectory ?? "").Trim();
        if (wd.Length > 0) psi.WorkingDirectory = wd;

        if (request.Environment is { } env)
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        Process? proc = null;
        try
        {
            proc = new Process { StartInfo = psi };
            if (!proc.Start()) return new ProcessRunResult { Error = "进程启动失败" };
        }
        catch (Exception ex)
        {
            proc?.Dispose();
            return new ProcessRunResult { Error = ex.Message };
        }

        try
        {
            // 先起读取任务（边跑边读，避免输出超过管道缓冲把子进程卡死）。
            // 读取阶段可以被取消 ⇒ 进程退出后最多再等 OutputDrainGrace。
            using var drainCts = new CancellationTokenSource();
            var stdoutTask = ReadAllAsync(proc.StandardOutput, drainCts.Token);
            var stderrTask = ReadAllAsync(proc.StandardError, drainCts.Token);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var timedOut = false;
            var canceled = false;
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                canceled = ct.IsCancellationRequested;
                timedOut = !canceled;
                TryKill(proc);
            }

            drainCts.CancelAfter(OutputDrainGrace);
            var (stdout, stdoutBad) = await stdoutTask.ConfigureAwait(false);
            var (stderr, stderrBad) = await stderrTask.ConfigureAwait(false);

            if (stdoutBad || stderrBad)
                return new ProcessRunResult
                {
                    Started = true,
                    TimedOut = timedOut,
                    Canceled = canceled,
                    StdOut = stdout,
                    StdErr = stderr,
                    OutputDecodeFailed = true,
                    Error = "输出编码非 UTF-8（中文会乱码，请把输出改成 UTF-8）"
                };

            var exit = -1;
            try { exit = proc.ExitCode; } catch { /* 已被强杀 / 没正常退出 */ }

            if (canceled)
                return new ProcessRunResult { Started = true, Canceled = true, ExitCode = exit, StdOut = stdout, StdErr = stderr };

            if (timedOut)
                return new ProcessRunResult
                {
                    Started = true, TimedOut = true, ExitCode = exit, StdOut = stdout, StdErr = stderr,
                    Error = $"超时（>{timeout.TotalSeconds:0}）"
                };

            return new ProcessRunResult
            {
                Started = true, ExitCode = exit, StdOut = stdout, StdErr = stderr,
                Error = exit == 0 ? null : $"退出码 {exit}"
            };
        }
        catch (Exception ex)
        {
            return new ProcessRunResult { Started = true, Error = ex.Message };
        }
        finally
        {
            proc?.Dispose();
        }
    }

    /// <summary>
    /// 增量读完一个流（取消 ⇒ 立刻收尾，返回**已读到的部分**）。
    /// <para>返回 <c>Failed = true</c> 表示解码失败（严格 UTF-8 遇到非法字节）。</para>
    /// </summary>
    private static async Task<(string Text, bool Failed)> ReadAllAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[8 * 1024];
        while (true)
        {
            int n;
            try
            {
                n = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return (sb.ToString(), false); }
            catch (DecoderFallbackException) { return (sb.ToString(), true); }
            catch { return (sb.ToString(), false); }   // 流已被释放 / 管道断开

            if (n <= 0) return (sb.ToString(), false);
            sb.Append(buffer, 0, n);
        }
    }

    /// <summary>强杀进程树（吞掉所有异常：目标可能已经自己退了）。</summary>
    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* 已退出 / 无权限 */ }
    }
}
