using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;
using NewToys.UI.Controls;

namespace NewToys.Shell;

public partial class App : Application
{
    // ==================== 多实例：--profile 参数 + 每配置单例（v3.26，需求 §3.5） ====================

    /// <summary>从命令行取 <c>--profile &lt;配置名&gt;</c>；没有该参数 → null（走 active-profile.txt 全局默认）。</summary>
    private static string? ParseProfileOverride(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// 每配置单例：同一配置同时只允许一个实例（数据隔离——同配置共享 config/db 会互相覆盖）。
    /// 返回 false = 配置已在运行（已弹系统提示并退出进程）。
    /// </summary>
    private static bool AcquireProfileMutex(ConfigStoreService store)
    {
        // ⛔ 别用 Global\（多会话 / 终端服务场景误伤）；配置名大小写不敏感 → 统一小写
        var name = "Local\\NewToys_Profile_" + store.ActiveProfileName.ToLowerInvariant();
        try
        {
            var m = new Mutex(false, name, out var createdNew);
            if (createdNew)
            {
                _profileMutex = m;   // ⛔ 必须进程级持有：回收 = 互斥体立刻释放
                return true;
            }
        }
        catch (Exception ex)
        {
            // 互斥体建不出来（异常名等）→ 放行，⛔ 不让诊断机制挡住启动
            WriteStartupLog($"配置互斥体创建失败，已放行：{ex.Message}");
            return true;
        }

        ShowPlatformMessage(
            $"配置「{store.ActiveProfileName}」已在运行。\n\n"
            + "同一个配置同时只允许一个窗口（否则两边的设置与数据会互相覆盖）。\n"
            + "请切换到已打开的窗口，或换一个配置启动。",
            "NewToys");
        Environment.Exit(0);
        return false; // 到不了（Exit 不返回），写上防流程分析报警
    }

    /// <summary>追加一行启动诊断到当天的启动日志（v3.26：--profile 被拒 / 互斥体异常等；写盘失败静默吞掉）。</summary>
    private static void WriteStartupLog(string message)
    {
        try
        {
            string dir;
            var store = AppServices.Get<ConfigStoreService>();
            if (store != null)
            {
                dir = store.LogsDirectory;
            }
            else
            {
                var root = Environment.GetEnvironmentVariable("NEWTOYS_HOME");
                if (string.IsNullOrEmpty(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewToys");
                dir = Path.Combine(root, "logs");
            }

            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"startup-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}\n");
            System.Diagnostics.Debug.WriteLine($"[startup] {message}");
        }
        catch { /* 记不下来也不影响主流程 */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;

    /// <summary>
    /// 平台门控的系统提示框。
    /// <para>🔴 <c>MessageBoxW</c> 是 user32 的 P/Invoke：非 Windows 上一调就 <c>DllNotFoundException</c>。
    /// 这里统一降级为「静默跳过」（调用点都已同时写启动日志，信息不丢）。</para>
    /// </summary>
    private static void ShowPlatformMessage(string text, string caption)
    {
        try
        {
            // 内联 IsWindows()：CA1416 只认这种写法
            if (!OperatingSystem.IsWindows()) return;
            MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONINFORMATION);
        }
        catch { /* 提示失败不影响主流程 */ }
    }

}
