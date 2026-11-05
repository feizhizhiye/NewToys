using System;
using System.Collections.Generic;
using System.IO;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>「脚本 → (解释器 exe, 参数)」的唯一解析处（§二.1）。（WHY: CR-025）</summary>
public static class InterpreterResolver
{
    /// <summary>pwsh 的常见安装位（没配路径时按顺序探）。</summary>
    private static readonly string[] PwshCandidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PowerShell", "7", "pwsh.exe"),
    };

    /// <summary>解析解释器。<paramref name="interpreter"/> 取 ScriptInterpreter 的常量（空 = Auto，按扩展名判）。</summary>
    public static (string? Exe, string Args, string? Error) Resolve(
        string? interpreter, string scriptPath, IAppConfigService? cfg)
    {
        var mode = (interpreter ?? "").Trim();
        if (mode.Length == 0)
        {
            // Auto：按扩展名挑（.ps1→pwsh 缺则 5.1、.py→python、.bat/.cmd→cmd）
            var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
            mode = ext switch
            {
                ".py" => ScriptInterpreter.Python,
                ".bat" or ".cmd" => ScriptInterpreter.Cmd,
                // .ps1 与「不认识的扩展名」都先按 pwsh 走（拿不到 pwsh 时下面自动退回 5.1）
                _ => ScriptInterpreter.Pwsh,
            };
        }

        if (string.Equals(mode, ScriptInterpreter.Cmd, StringComparison.OrdinalIgnoreCase))
            return ("cmd.exe", $"/c \"{scriptPath}\"", null);

        if (string.Equals(mode, ScriptInterpreter.Python, StringComparison.OrdinalIgnoreCase))
        {
            var py = ConfiguredOr(cfg?.PythonPath, "python");
            return (py, $"\"{scriptPath}\"", null);
        }

        if (string.Equals(mode, ScriptInterpreter.PowerShell51, StringComparison.OrdinalIgnoreCase))
        {
            var ps51 = ConfiguredOr(cfg?.PowerShell51Path, "powershell.exe");
            return (ps51, PsArgs(scriptPath), null);
        }

        // pwsh（含 Auto 落到这里的分支）：拿不到 pwsh 就退回 Windows PowerShell 5.1
        if (TryResolvePwsh(cfg, out var pwsh))
            return (pwsh, PsArgs(scriptPath), null);

        if (string.Equals(mode, ScriptInterpreter.Pwsh, StringComparison.OrdinalIgnoreCase))
        {
            // 显式指定了 pwsh 却没装 → 退回 5.1（条目里写死 pwsh 时不至于整组没有数据）
            var ps51 = ConfiguredOr(cfg?.PowerShell51Path, "powershell.exe");
            return (ps51, PsArgs(scriptPath), null);
        }

        return (null, "", "本机找不到可用的 PowerShell（pwsh / Windows PowerShell 5.1）");
    }

    /// <summary>本机能不能拿到 pwsh：配置里填了且存在 → 用配置；没配 → 探常见安装位；再不行 → PATH 里的 <c>pwsh</c>。</summary>
    public static bool TryResolvePwsh(IAppConfigService? cfg, out string exe)
    {
        var configured = (cfg?.PwshPath ?? "").Trim();
        if (configured.Length > 0)
        {
            exe = configured;
            return File.Exists(configured);
        }

        foreach (var cand in PwshCandidates)
            if (File.Exists(cand)) { exe = cand; return true; }

        // PATH 里有 pwsh 也算能拿到（起进程时由 Win32 解析 PATH）
        exe = "pwsh";
        return true;
    }

    /// <summary>
    /// PowerShell 的统一启动参数：不进 profile、非交互、绕过执行策略，
    /// 并**先把控制台输出编码设成 UTF-8** 再 &amp; 脚本（§三.4 中文环境硬要求）。
    /// </summary>
    private static string PsArgs(string scriptPath)
        => "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
           "\"[Console]::OutputEncoding=[Text.Encoding]::UTF8; & '" + scriptPath.Replace("'", "''") + "'\"";

    /// <summary>配置里给了且文件存在就用它；否则用 <paramref name="fallback"/>（= PATH 里的名字）。</summary>
    private static string ConfiguredOr(string? configured, string fallback)
    {
        var v = (configured ?? "").Trim();
        return v.Length > 0 && File.Exists(v) ? v : fallback;
    }
}

/// <summary>脚本文件用哪个解释器（QuickAction.Interpreter 的取值）。（WHY: CR-027）</summary>
public static class ScriptInterpreter
{
    /// <summary>按扩展名自动挑（默认）。</summary>
    public const string Auto = "";

    public const string Python = "python";

    /// <summary>PowerShell 7+（pwsh）。</summary>
    public const string Pwsh = "pwsh";

    /// <summary>Windows PowerShell 5.1（powershell.exe，系统自带）。</summary>
    public const string PowerShell51 = "powershell";

    public const string Cmd = "cmd";
}

/// <summary>命令行的宿主 / 窗口行为（QuickAction.Shell 的取值，）。</summary>
public static class QuickActionShell
{
    /// <summary>自动：有输出→pwsh 可见终端（回显命令）；GUI 启动类→cmd 静默。默认。</summary>
    public const string Auto = "Auto";

    /// <summary>强制用 cmd（<c>cmd /k</c> 可见窗口）。</summary>
    public const string Cmd = "Cmd";

    /// <summary>强制用 pwsh（可见终端 + 回显命令）。</summary>
    public const string Pwsh = "Pwsh";

    /// <summary>详情卡「Shell」下拉的选项（顺序即显示顺序）。</summary>
    public static IReadOnlyList<(string Value, string Label)> Options { get; } = new[]
    {
        (Auto, "自动（有输出用 pwsh / GUI 用 cmd）"),
        (Cmd, "cmd（/k 可见窗口）"),
        (Pwsh, "pwsh（可见终端 + 回显命令）"),
    };

    /// <summary>归一化：认得的返回值，其余（含 null / 空 / 旧数据缺省）一律回退 <see cref="Auto"/>。</summary>
    public static string Normalize(string? shell)
        => string.Equals(shell, Cmd, StringComparison.OrdinalIgnoreCase) ? Cmd
         : string.Equals(shell, Pwsh, StringComparison.OrdinalIgnoreCase) ? Pwsh
         : Auto;
}
