using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using NewToys.Core.Services;
using NewToys.Shared.Services;   // IAppConfigService 在 Shared（不在 Core）
using NewToys.SystemTools.Models;

namespace NewToys.SystemTools.Services;

/// <summary>
/// <see cref="ISystemCommandLauncher"/> 实现：<b>优先 Windows Terminal</b>（用户 2026-10-23：要在 Windows 终端里执行），
/// 本机没装 WT 才回退系统终端（conhost）。
/// <para>
/// 提权方式 = <c>UseShellExecute=true</c> + <c>Verb="runas"</c> ⇒ 弹 UAC；整个 wt / cmd 进程提权，
/// 其标签页里执行的命令同样是提权的。窗口 / 标签页<b>跑完不自动关</b>（留着读结果）。
/// </para>
/// <para>
/// 🔴 wt 用法 = 官方「向默认 shell 传命令」口径：<c>wt.exe &lt;命令行&gt;</c>（在默认配置文件的标签页里跑）——
/// locale 无关，⛔ 不用 <c>-p 配置文件名</c>（中文系统里叫「命令提示符」，写死就废）。
/// ⚠️ wt 把 <c>;</c> 当标签页分隔符：命令里含分号会多开标签页（预设无分号；用户自建条目注意）。
/// </para>
/// <para>
/// 🔴 Shell 口径与捕获路（<c>ScriptRunner</c>）一致：<b>auto / pwsh = pwsh 7</b>
/// （配置路径 → <c>Program Files\PowerShell\7</c> → PATH；<c>-EncodedCommand</c> base64 传参避开引号 / &amp; / | 被二次解析）；
/// <b>cmd = cmd /k</b>（reg / netsh / winget / start / .cpl 用它最稳，预设全部是它）。
/// </para>
/// </summary>
public sealed class SystemCommandLauncher : ISystemCommandLauncher
{
    private readonly IAppConfigService? _cfg;

    public SystemCommandLauncher(IAppConfigService? cfg = null) => _cfg = cfg;

    public (bool Ok, string Message) RunElevated(SystemCommand item)
    {
        if (item is null) return (false, "未选择项目。");
        if (!OperatingSystem.IsWindows())
            return (false, "提权运行（UAC）目前只在 Windows 上可用 —— 可先「📋 复制命令」自己在提权终端里跑。");

        var cmd = (item.Command ?? "").Trim();
        if (cmd.Length == 0) return (false, "这条还没有命令内容。");

        // ---- 选宿主（与 ScriptRunner 捕获路同口径：cmd = cmd；auto / pwsh = pwsh 7） ----
        string exe;
        string args;
        if (string.Equals((item.Shell ?? "auto").Trim(), "cmd", StringComparison.OrdinalIgnoreCase))
        {
            exe = "cmd.exe";
            args = "/k " + cmd;
        }
        else
        {
            if (!InterpreterResolver.TryResolvePwsh(_cfg, out var pwsh))
                return (false, "本机未找到可用的 pwsh，无法提权运行（可把 Shell 改成「cmd」）。");
            exe = pwsh;
            // -NoExit：跑完窗口 / 标签页留着看结果；base64 避开一切 shell 二次解析
            args = "-NoExit -NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));
        }

        // ---- ① 优先 Windows Terminal ----
        if (TryResolveWt(out var wt))
        {
            var exeToken = exe.Contains(' ') ? "\"" + exe + "\"" : exe;   // 路径带空格 ⇒ 给 wt 的命令行加引号
            var (state, err) = StartElevated(wt, exeToken + " " + args);
            switch (state)
            {
                case LaunchState.Ok:
                    return (true, $"已请求以管理员身份运行「{item.Title}」：弹出 UAC 请点「是」；命令在 Windows 终端里执行（跑完标签页不会自动关）。");
                case LaunchState.Canceled:
                    return (false, err ?? "已取消提权。");   // UAC 点了「否」⇒ 直接返回，⛔ 不回退（回退会再弹一次）
                default:
                    break;                  // wt 起 ⇒ 起不来（坏了 / 被删）⇒ 落去系统终端兜底
            }
        }

        // ---- ② 回退：系统终端（conhost：cmd /k 或 pwsh -NoExit） ----
        var (state2, err2) = StartElevated(exe, args);
        return state2 switch
        {
            LaunchState.Ok => (true, $"已请求以管理员身份运行「{item.Title}」：弹出 UAC 请点「是」；结果在打开的窗口里（未找到 Windows Terminal，用系统终端打开；跑完窗口不会自动关）。"),
            LaunchState.Canceled => (false, err2 ?? "已取消提权。"),
            _ => (false, "⚠ 提权启动失败：" + (err2 ?? "未知原因"))
        };
    }

    private enum LaunchState { Ok, Canceled, Failed }

    /// <summary>ShellExecute + runas 起进程；UAC 取消（Win32 1223）单独归一类。</summary>
    private static (LaunchState State, string? Error) StartElevated(string fileName, string arguments)
    {
        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,   // 提权 / 协议解析的前提
                Verb = "runas"            // ⇒ 弹 UAC；已是管理员则不弹直接跑
            });
            if (started is null) return (LaunchState.Failed, "系统没有创建进程");
            return (LaunchState.Ok, null);
        }
        catch (Win32Exception w) when (w.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED：用户在 UAC 弹窗点了「否」⇒ 命令没有执行，⛔ 不当成失败吼叫。
            return (LaunchState.Canceled, "已取消提权（UAC 未确认）—— 命令没有执行。");
        }
        catch (Exception ex)
        {
            return (LaunchState.Failed, ex.Message);
        }
    }

    /// <summary>
    /// 本机有没有 Windows Terminal：先认 Store 装的标准位（App Execution Alias 的真身），
    /// 认不出就给名字 <c>wt.exe</c> 让 ShellExecute 按 PATH / 别名解析（真没有 ⇒ 启动失败走回退）。
    /// </summary>
    private static bool TryResolveWt(out string wt)
    {
        var store = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(store)) { wt = store; return true; }
        wt = "wt.exe";
        return true;
    }
}
