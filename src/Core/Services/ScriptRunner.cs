using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

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

    /// <summary>超时（秒；≤0 用 <see cref="ScriptRunner.CommandDefaultTimeout"/>，上限 <see cref="ScriptRunner.CommandMaxTimeout"/>）。</summary>
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
/// 🔴 NT-088：本类从 <c>Module.ScriptManager</c> 上提 <c>NewToys.Core</c> —— system 系统工具模块
/// （收藏夹 / 快捷命令）要执行条目，而**模块禁互引**（§3.1），执行契约只能放框架层。
/// 行为零变化，纯搬家；<c>InterpreterResolver</c> 与解释器常量 <c>ScriptInterpreter</c> /
/// <c>QuickActionShell</c> 同批上提（否则 Core 会反向依赖模块）。
/// </para>
/// <para>
/// 🔴 与 <c>QuickActionService</c> 的根本差别：这里 <c>UseShellExecute=false</c> + 重定向 stdout/stderr，
/// 进程隐藏、输出捕获、可超时 / 可取消 —— 供信息面板把输出当数据解析。
/// </para>
/// <para>
/// 🔴 命令（<c>role=command</c> 的采集器）走 <see cref="RunCommandAsync"/>：<b>复用同一套捕获逻辑</b>，
/// ⛔ 不复用 <c>QuickActionService.BuildRunPlan</c>（那个是可见窗口给用户看的）。
/// </para>
/// <para>
/// 🔴 进程执行本体在 NT-081 步骤 3 起下沉 <see cref="IProcessRunner"/>（Core）：编码 / 超时 / 杀进程树
/// 与 <c>PackageManagerService</c> 共用一份；本类只留脚本特有部分（解释器解析、环境变量、结果语义）。
/// </para>
/// </remarks>
public sealed class ScriptRunner
{
    private readonly IAppConfigService? _cfg;
    private readonly IProcessRunner _runner;

    public ScriptRunner(IAppConfigService? cfg = null, IProcessRunner? runner = null)
    {
        _cfg = cfg;
        _runner = runner ?? new ProcessRunner();
    }

    /// <summary>默认超时（秒）。</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>超时上限（秒）。</summary>
    public const int MaxTimeoutSeconds = 300;

    /// <summary>命令型执行的默认超时（秒）（NT-088：从 <c>CollectorService</c> 上提，沿用原名）。</summary>
    public const int CommandDefaultTimeout = 15;

    /// <summary>命令型执行的超时上限（秒）（NT-088：从 <c>CollectorService</c> 上提，沿用原名）。</summary>
    public const int CommandMaxTimeout = 120;

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
            ? CommandDefaultTimeout
            : Math.Clamp(req.TimeoutSeconds, 1, CommandMaxTimeout);

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

    /// <summary>捕获式执行的核心（脚本 / 命令共用）：进程本体走 <see cref="IProcessRunner"/>，这里只做环境与结果语义。</summary>
    private async Task<ScriptRunResult> RunCoreAsync(string exe, string args, string? workDir,
        int timeoutSeconds, IReadOnlyDictionary<string, string>? extraEnv, CancellationToken ct)
    {
        var wd = (workDir ?? "").Trim();

        // 脚本执行的环境约定（采集器 / 规则脚本都吃这一套）
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONUTF8"] = "1"
        };
        if (wd.Length > 0) env["NEWTOYS_SYSINFO_DIR"] = wd;
        if (extraEnv is { } extra)
            foreach (var kv in extra) env[kv.Key] = kv.Value;

        var r = await _runner.RunAsync(new ProcessRunRequest
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = wd.Length > 0 ? wd : null,
            Environment = env,
            StrictOutput = true
        }, TimeSpan.FromSeconds(timeoutSeconds), ct).ConfigureAwait(false);

        if (!r.Started)
            return new ScriptRunResult { Error = r.Error ?? "进程启动失败" };

        if (r.OutputDecodeFailed)
            return new ScriptRunResult { TimedOut = r.TimedOut, Canceled = r.Canceled, StdErr = r.StdErr, Error = r.Error };

        if (r.Canceled)
            return new ScriptRunResult { Canceled = true, ExitCode = r.ExitCode, StdOut = r.StdOut, StdErr = r.StdErr };

        if (r.TimedOut)
            return new ScriptRunResult
            {
                TimedOut = true, ExitCode = r.ExitCode, StdOut = r.StdOut, StdErr = r.StdErr,
                Error = $"超时（>{timeoutSeconds}s）"
            };

        return new ScriptRunResult
        {
            Ok = r.ExitCode == 0,
            ExitCode = r.ExitCode,
            StdOut = r.StdOut,
            StdErr = r.StdErr,
            Error = r.ExitCode == 0 ? null : $"退出码 {r.ExitCode}"
        };
    }
}
