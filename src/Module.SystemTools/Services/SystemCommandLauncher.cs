using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using NewToys.Core.Services;
using NewToys.Shared.Services;   // IAppConfigService 在 Shared（不在 Core）
using NewToys.SystemTools.Models;

namespace NewToys.SystemTools.Services;

/// <summary>
/// <see cref="ISystemCommandLauncher"/> 实现：<c>cmd /k</c> 或 <c>pwsh -NoExit -EncodedCommand</c>
/// + <c>Verb = "runas"</c>（UseShellExecute）⇒ 弹 UAC，提权后在可见窗口里执行，跑完窗口不自动关（留着读结果）。
/// <para>
/// 🔴 与 <c>QuickActionService.RunAsync</c> 的口径一致：<b>需要管理员时忽略「隐藏终端窗口」开关</b>
/// （提权路径必须可见，否则用户看不到 UAC 之后发生了什么）。
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

        // Shell：pwsh = 优先 pwsh（base64 传参避开引号 / & / | 破坏解析）；cmd / auto = cmd /k（永远在）。
        string exe;
        string args;
        if (string.Equals((item.Shell ?? "auto").Trim(), "pwsh", StringComparison.OrdinalIgnoreCase))
        {
            if (!InterpreterResolver.TryResolvePwsh(_cfg, out var pwsh))
                return (false, "本机未找到可用的 pwsh，无法按「Pwsh」提权运行（可把 Shell 改回「自动」或「cmd」）。");
            exe = pwsh;
            args = "-NoExit -NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));
        }
        else
        {
            exe = "cmd.exe";
            args = "/k " + cmd;
        }

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,   // 提权 / 协议解析的前提
                Verb = "runas"            // ⇒ 弹 UAC；已是管理员则不弹直接跑
            });
            if (started is null) return (false, "⚠ 提权启动失败：系统没有创建进程。");
        }
        catch (Win32Exception w) when (w.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED：用户在 UAC 弹窗点了「否」⇒ 命令没有执行，⛔ 不当成失败吼叫。
            return (false, "已取消提权（UAC 未确认）—— 命令没有执行。");
        }
        catch (Exception ex)
        {
            return (false, "⚠ 提权启动失败：" + ex.Message);
        }

        return (true, $"已请求以管理员身份运行「{item.Title}」：弹出 UAC 请点「是」；结果在新打开的窗口里（跑完窗口不会自动关）。");
    }
}
