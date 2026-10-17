using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>一次脚本执行的入参（NT-067 §二.1）。</summary>
public sealed class ScriptRunRequest
{
    /// <summary>脚本绝对路径。</summary>
    public string ScriptPath { get; init; } = "";

    /// <summary>解释器常量（<see cref="Models.ScriptInterpreter"/>）；空 = 按扩展名自动。</summary>
    public string Interpreter { get; init; } = "";

    /// <summary>工作目录（本页固定为 <c>sysinfo\</c>，脚本里的相对路径才好使）。</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>超时（秒；≤0 用默认 30，上限 300）。</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>额外环境变量（编码 / <c>NEWTOYS_SYSINFO_DIR</c> 等）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>一次脚本执行的结果。</summary>
public sealed class ScriptRunResult
{
    /// <summary>进程正常退出（退出码 0）且没有启动 / 编码类错误。</summary>
    public bool Ok { get; init; }

    /// <summary>进程退出码（起不来时为 -1）。</summary>
    public int ExitCode { get; init; } = -1;

    /// <summary>超时被强杀。</summary>
    public bool TimedOut { get; init; }

    /// <summary>被调用方的 <see cref="CancellationToken"/> 取消（= 主动放弃，不算脚本失败）。</summary>
    public bool Canceled { get; init; }

    /// <summary>stdout（UTF-8 解码后）。</summary>
    public string StdOut { get; init; } = "";

    /// <summary>stderr（诊断信息，本页不上报内容、只判断"非空"）。</summary>
    public string StdErr { get; init; } = "";

    /// <summary>启动失败 / 输出编码非法 / 超时等人话说明；正常时为 null。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 「跑脚本并拿回文本」的**唯一入口**（NT-067 §二.1）。
/// <para>
/// 🔴 与 <c>QuickActionService</c> 的根本差别：这里 <c>UseShellExecute=false</c> + 重定向 stdout/stderr，
/// 进程隐藏、输出捕获、可超时 / 可取消 —— 供信息面板页把输出当数据解析。
/// </para>
/// <para>
/// 🔴 编码（§三.4）：<c>StandardOutputEncoding</c> 用**遇非法字节即抛**的 UTF-8 解码器
/// （解码失败 ⇒ 该脚本失败并提示"输出编码非 UTF-8"）；stderr 用宽容解码（只当诊断文本）。
/// </para>
/// </summary>
public sealed class ScriptRunner
{
    private readonly IAppConfigService? _cfg;

    public ScriptRunner(IAppConfigService? cfg = null) => _cfg = cfg;

    /// <summary>默认超时（秒）。</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>超时上限（秒）。</summary>
    public const int MaxTimeoutSeconds = 300;

    /// <summary>跑一个脚本（永不抛异常：所有失败都折成 <see cref="ScriptRunResult.Error"/>）。</summary>
    public async Task<ScriptRunResult> RunAsync(ScriptRunRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.ScriptPath))
            return new ScriptRunResult { Error = "没有脚本路径" };

        var (exe, args, resolveError) = InterpreterResolver.Resolve(req.Interpreter, req.ScriptPath, _cfg);
        if (exe is null)
            return new ScriptRunResult { Error = resolveError ?? "找不到解释器" };

        var timeout = req.TimeoutSeconds <= 0 ? DefaultTimeoutSeconds : Math.Min(req.TimeoutSeconds, MaxTimeoutSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 🔴 强制 UTF-8：无 BOM 的严格解码器（非法字节 → 抛 DecoderFallbackException ⇒ 判该脚本失败）
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
        };

        var workDir = (req.WorkingDirectory ?? "").Trim();
        if (workDir.Length > 0) psi.WorkingDirectory = workDir;

        // 编码 / 定位用的环境变量：Python 两个、sysinfo 目录一个（脚本定位同目录资源用）
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        if (workDir.Length > 0) psi.Environment["NEWTOYS_SYSINFO_DIR"] = workDir;
        if (req.Environment is { } extra)
            foreach (var kv in extra) psi.Environment[kv.Key] = kv.Value;

        Process? proc = null;
        try
        {
            proc = new Process { StartInfo = psi };
            if (!proc.Start())
                return new ScriptRunResult { Error = "进程启动失败" };

            // 先起读取任务（边跑边读，避免输出超过管道缓冲把子进程卡死）
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

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
                return new ScriptRunResult
                {
                    TimedOut = timedOut,
                    Canceled = canceled,
                    StdErr = stderr ?? "",
                    Error = "输出编码非 UTF-8（中文会乱码，请把脚本输出改成 UTF-8）",
                };

            var exit = -1;
            try { exit = proc.ExitCode; } catch { /* 已被强杀 / 没正常退出 */ }

            if (canceled)
                return new ScriptRunResult { Canceled = true, ExitCode = exit, StdOut = stdout, StdErr = stderr };

            if (timedOut)
                return new ScriptRunResult
                {
                    TimedOut = true, ExitCode = exit, StdOut = stdout, StdErr = stderr,
                    Error = $"超时（>{timeout}s）",
                };

            return new ScriptRunResult
            {
                Ok = exit == 0,
                ExitCode = exit,
                StdOut = stdout,
                StdErr = stderr,
                Error = exit == 0 ? null : $"退出码 {exit}",
            };
        }
        catch (Exception ex)
        {
            proc?.Dispose();
            return new ScriptRunResult { Error = ex.Message };
        }
        finally
        {
            proc?.Dispose();
        }
    }

    /// <summary>读 stdout/stderr；解码非法（严格 UTF-8）返回 null（= 该脚本按"编码非 UTF-8"失败）。</summary>
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
