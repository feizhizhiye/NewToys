using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace NewToys.Core.Services;

/// <summary>
/// 「开机时启动 NewToys」的<b>真实实现</b>（v1.35）：写 / 删 Windows 当前用户的
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> 开机启动项。
/// <para>
/// 为什么用注册表而不是往「启动」文件夹里放快捷方式：建 .lnk 要走 COM（WScript.Shell），
/// 在 .NET 上既不稳又多一层依赖；注册表一行字符串就能表达，且**不需要管理员权限**（HKCU）。
/// </para>
/// <para>
/// ⛔ 非 Windows 平台一律返回「不支持」，并如实告知 —— 绝不假装生效（规范 0.3：界面不得虚报状态）。
/// </para>
/// </summary>
public class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>注册表里的值名（卸载 / 重装时按它定位）。</summary>
    private const string ValueName = "NewToys";

    /// <summary>是否支持（目前仅 Windows）。</summary>
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>上一次操作的失败原因（成功时为空）。</summary>
    public string LastError { get; private set; } = "";

    /// <summary>
    /// 写进注册表的启动命令（带引号的 exe 全路径）。
    /// <para>优先 <c>Environment.ProcessPath</c>（发布后的 NewToys.exe）；取不到时退回
    /// <c>AppContext.BaseDirectory\NewToys.exe</c>。</para>
    /// </summary>
    public string ExePath
    {
        get
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;

            var guess = Path.Combine(AppContext.BaseDirectory, "NewToys.exe");
            return File.Exists(guess) ? guess : p ?? "";
        }
    }

    /// <summary>开机启动项当前是否存在（不支持的平台一律 false）。</summary>
    public bool IsEnabled
    {
        get
        {
            if (!IsSupported) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                var v = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(v);
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
    }

    /// <summary>把「是否开机启动」设成指定值；返回 (是否成功, 说明)。</summary>
    public (bool Ok, string Message) SetEnabled(bool enable)
        => enable ? Enable() : Disable();

    /// <summary>写入开机启动项。</summary>
    public (bool Ok, string Message) Enable()
    {
        if (!IsSupported) return (false, "开机启动目前只支持 Windows。");

        var exe = ExePath;
        if (string.IsNullOrWhiteSpace(exe)) return (false, "取不到本程序的路径，无法写开机启动项。");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{exe}\"");
            LastError = "";
            // ⚠️ 别在插值串里写 `\{` —— 那是非法转义序列（CS8087/CS1009）。注册表路径用文字描述即可。
            return (true, $"已写入开机启动项（{RunKeyPath} 下的 {ValueName}）");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return (false, "写开机启动项失败：" + ex.Message);
        }
    }

    /// <summary>删除开机启动项（本来就没有也算成功）。</summary>
    public (bool Ok, string Message) Disable()
    {
        if (!IsSupported) return (false, "开机启动目前只支持 Windows。");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return (true, "开机启动项本来就不存在。");
            if (key.GetValue(ValueName) == null) return (true, "开机启动项本来就不存在。");

            key.DeleteValue(ValueName, false);
            LastError = "";
            return (true, "已取消开机启动。");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return (false, "删除开机启动项失败：" + ex.Message);
        }
    }

    /// <summary>打开「任务管理器 → 启动」页（让用户能自己去核对 / 禁用）。</summary>
    public (bool Ok, string Message) OpenStartupSettings()
    {
        if (!OperatingSystem.IsWindows()) return (false, "仅 Windows 支持。");
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe", "/0 /startup") { UseShellExecute = true });
            return (true, "已打开任务管理器的「启动」页。");
        }
        catch (Exception ex) { return (false, "打开失败：" + ex.Message); }
    }
}
