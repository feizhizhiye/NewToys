using System;
using System.IO;
using NewToys.ScriptManager.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 「脚本 → (解释器 exe, 参数)」的唯一解析处（NT-067 §二.1）。
/// <para>
/// 为什么单独抽出来：<c>QuickActionService</c> 只**开可见窗口**跑脚本（不捕获输出），本页要的是
/// 「静默跑 + 拿回文本」；两者共用同一套解释器路径来源，所以把路径解析独立成 static 工具类
/// （<c>QuickActionService</c> 本期**不改**，留给后续复用 —— 见卡 §四「不做」）。
/// </para>
/// <para>
/// 路径来源 = 应用级配置（<c>app.json</c> 的「运行环境」：<c>PwshPath</c> / <c>PythonPath</c> /
/// <c>PowerShell51Path</c>）；没配就探常见安装位、再退回 PATH。
/// </para>
/// </summary>
public static class InterpreterResolver
{
    /// <summary>pwsh 的常见安装位（没配路径时按顺序探）。</summary>
    private static readonly string[] PwshCandidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PowerShell", "7", "pwsh.exe"),
    };

    /// <summary>
    /// 解析解释器。<paramref name="interpreter"/> 取 <see cref="ScriptInterpreter"/> 的常量（空 = Auto，按扩展名判）。
    /// </summary>
    /// <param name="interpreter">解释器常量；空串 = <see cref="ScriptInterpreter.Auto"/>。</param>
    /// <param name="scriptPath">脚本绝对路径（Auto 模式按扩展名挑）。</param>
    /// <param name="cfg">应用级配置（可为 null ⇒ 全部按「探安装位 / PATH」处理）。</param>
    /// <returns>Exe 为 null ⇒ 本机找不到可用的解释器（Error 给原因）。</returns>
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
            // 显式指定了 pwsh 却没装 → 退回 5.1（用户手改 manifest 写死 pwsh 时不至于整组没有数据）
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
