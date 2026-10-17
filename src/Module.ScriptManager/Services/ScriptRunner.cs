using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.ScriptManager.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>一次脚本执行的入参（NT-067 §二.1）。</summary>
public sealed class ScriptRunRequest
{
    /// <summary>脚本绝对路径。</summary>
    public string ScriptPath { get; init; } = "";

    /// <summary>解释器常量（<see cref="Models.ScriptInterpreter"/>）；空 = 按扩展名自动。</summary>
    public string Interpreter { get; init; } = "";

    /// <summary>工作目录（脚本里的相对路径才好使）。</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>超时（秒；≤0 用默认 30，上限 300）。</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>额外环境变量（编码 / <c>NEWTOYS_SYSINFO_DIR</c> 等）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>一次命令行执行的入参（NT-068 §三.11：命令即采集器）。</summary>
public sealed class CommandRunRequest
{
    /// <summary>要跑的命令行。</summary>
    public string Command { get; init; } = "";

    /// <summary>宿主（<see cref="QuickActionShell.Auto"/> / <c>Cmd</c> / <c>Pwsh</c>）。</summary>
    public string Shell { get; init; } = QuickActionShell.Auto;

    public string WorkingDirectory { get; init; } = "";

    /// <summary>超时（秒；≤0 用 <see cref="CollectorService.CommandDefaultTimeout"/>，上限 <see cref="CollectorService.CommandMaxTimeout"/>）。</summary>
    public int TimeoutSeconds { get; init; }

    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

/// <summary>一次脚本 / 命令执行的结果。</summary>
public sealed class ScriptRunResult
{
    /// <summary>进程正常退出（退出码 0）且没有启动 / 编码类错误。</summary>
    public bool Ok { get; init; }

    /// <summary>进程退出码（起不来时为 -1）。</summary>
    public int ExitCode { get; init; } = -1;

    /// <summary>超时被强杀。</summary>
    public bool TimedOut { get; init; }

    /// <summary>被调用方的 <see cref="CancellationToken"/> 取消（= 主动放弃，不算失败）。</summary>
    public bool Canceled { get; init; }

    /// <summary>stdout（UTF-8 解码后）。</summary>
    public string StdOut { get; init; } = "";

    /// <summary>stderr（诊断信息，只判断"非空"当 warn）。</summary>
    public string StdErr { get; init; } = "";

    /// <summary>启动失败 / 输出编码非法 / 超时等人话说明；正常时为 null。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 「跑脚本 / 跑命令并拿回文本」的**唯一入口**（NT-067 §二.1 / NT-068 §三.11）。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 与 <c>QuickActionService</c> 的根本差别：这里 <c>UseShellExecute=false</c> + 重定向 stdout/stderr，
/// 进程隐藏、输出捕获、可超时 / 可取消 —— 供信息面板把输出当数据解析。
/// </para>
/// <para>
/// 🔴 命令（<c>role=command</c> 的采集器）走 <see cref="RunCommandAsync"/>：<b>复用同一套捕获逻辑</b>，
/// ⛔ 不复用 <c>QuickActionService.BuildRunPlan</c>（那个是可见窗口给用户看的）。
/// </para>
/// <para>
/// 🔴 编码：<c>StandardOutputEncoding</c> 用**遇非法字节即抛**的 UTF-8 解码器；stderr 用宽容解码。
/// </para>
/// </remarks>
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
        return await RunCoreAsync(exe, args, req.WorkingDirectory, timeout, req.Environment, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 跑一条命令行（命令即采集器，NT-068 §三.11）。
    /// <para>宿主：<c>Cmd</c> → <c>cmd.exe /c</c>；<c>Pwsh</c> / <c>Auto</c> → pwsh（拿不到退回 5.1）。</para>
    /// </summary>
    public async Task<ScriptRunResult> RunCommandAsync(CommandRunRequest req, CancellationToken ct = default)
    {
        var command = (req.Command ?? "").Trim();
        if (command.Length == 0) return new ScriptRunResult { Error = "命令为空" };

        var timeout = req.TimeoutSeconds <= 0
            ? CollectorService.CommandDefaultTimeout
            : Math.Clamp(req.TimeoutSeconds, 1, CollectorService.CommandMaxTimeout);

        string exe;
        string args;
        if (string.Equals(req.Shell, QuickActionShell.Cmd, StringComparison.OrdinalIgnoreCase))
        {
            exe = "cmd.exe";
            args = "/c " + command;
        }
        else
        {
            exe = InterpreterResolver.TryResolvePwsh(_cfg, out var pwsh) ? pwsh : "powershell.exe";
            args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                   "\"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command.Replace("\"", "`\"") + "\"";
        }

        return await RunCoreAsync(exe, args, req.WorkingDirectory, timeout, req.Environment, ct).ConfigureAwait(false);
    }

    /// <summary>捕获式执行的核心（脚本 / 命令共用）。</summary>
    private static async Task<ScriptRunResult> RunCoreAsync(string exe, string args, string? workDir,
        int timeoutSeconds, IReadOnlyDictionary<string, string>? extraEnv, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 🔴 强制 UTF-8：无 BOM 的严格解码器（非法字节 → 抛 DecoderFallbackException ⇒ 判该次采集失败）
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
        };

        var wd = (workDir ?? "").Trim();
        if (wd.Length > 0) psi.WorkingDirectory = wd;

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        if (wd.Length > 0) psi.Environment["NEWTOYS_SYSINFO_DIR"] = wd;
        if (extraEnv is { } extra)
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
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

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
                    Error = "输出编码非 UTF-8（中文会乱码，请把输出改成 UTF-8）",
                };

            var exit = -1;
            try { exit = proc.ExitCode; } catch { /* 已被强杀 / 没正常退出 */ }

            if (canceled)
                return new ScriptRunResult { Canceled = true, ExitCode = exit, StdOut = stdout, StdErr = stderr };

            if (timedOut)
                return new ScriptRunResult
                {
                    TimedOut = true, ExitCode = exit, StdOut = stdout, StdErr = stderr,
                    Error = $"超时（>{timeoutSeconds}s）",
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

    /// <summary>读 stdout/stderr；解码非法（严格 UTF-8）返回 null。</summary>
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
