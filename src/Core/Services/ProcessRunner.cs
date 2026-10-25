using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IProcessRunner"/> 实现。
/// <para>
/// 行为与原 <c>ScriptRunner.RunCoreAsync</c> / <c>PackageManagerService.RunAsync</c> 逐条对齐：
/// 隐藏 + 重定向；stdout 严格 UTF-8 / stderr 宽容；先起读任务再 <c>WaitForExitAsync</c>；
/// 超时或取消 ⇒ <c>Kill(entireProcessTree: true)</c>；<c>finally</c> 必 Dispose。
/// </para>
/// <para>无状态 ⇒ 单例 / 瞬态皆可（Shell 注册为单例）。</para>
/// </para>
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
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
            // 先起读取任务（边跑边读，避免输出超过管道缓冲把子进程卡死）
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

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

            var stdout = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeReadAsync(stderrTask).ConfigureAwait(false);

            if (stdout is null || stderr is null)
                return new ProcessRunResult
                {
                    Started = true,
                    TimedOut = timedOut,
                    Canceled = canceled,
                    StdErr = stderr ?? "",
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

    /// <summary>读 stdout/stderr；解码非法（严格 UTF-8）时任务以异常结束 ⇒ 返回 null。</summary>
    private static async Task<string?> SafeReadAsync(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return null; }
    }

    /// <summary>强杀进程树（吞掉所有异常：目标可能已经自己退了）。</summary>
    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* 已退出 / 无权限 */ }
    }
}
