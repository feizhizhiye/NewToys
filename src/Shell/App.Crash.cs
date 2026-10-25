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
    // ==================== 全局异常捕获（诊断用） ====================

    private static bool _crashHooked;

    /// <summary>
    /// 挂上三道「最后的防线」，把 UI 线程 / 后台线程 / 未观察 Task 上的未处理异常
    /// 追加写到 <c>&lt;应用根&gt;\logs\crash-yyyyMMdd.log</c>。
    /// <para>
    /// Why：模块页里的绑定失败、异步回调异常默认只进 Debug 输出（Release 下根本看不到），
    /// 表现成「页面突然乱了 / 卡住 / 直接退出」却拿不到堆栈。落盘之后，把那个 log 发出来即可定位。
    /// </para>
    /// <para>
    /// ⚠️ 只记录、不改行为：<c>Handled</c> 保持原样（该崩还是崩），不把程序留在半坏状态里假装没事。
    /// </para>
    /// </summary>
    private static void InstallCrashHandlers()
    {
        if (_crashHooked) return;
        _crashHooked = true;

        // ① UI 线程（Avalonia Dispatcher）—— 绝大多数的「页面崩了」都在这里
        Dispatcher.UIThread.UnhandledException += (_, e) => WriteCrashLog("UI", e.Exception);

        // ② 后台线程上的未处理异常（进程级）
        AppDomain.CurrentDomain.UnhandledException += (_, e)
            => WriteCrashLog("AppDomain", e.ExceptionObject as Exception);

        // ③ fire-and-forget 的 Task 抛了异常又没人 await（抽帧 / 转码的异步回调就是这种）
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("Task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>追加一处未处理异常到当天的崩溃日志；写盘失败静默吞掉（记日志本身不能再崩）。</summary>
    private static void WriteCrashLog(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            // 优先用 ConfigStoreService 给的日志目录（只有它知道当前是哪个「配置」）；
            // 启动早期（服务还没建好）崩溃时，退回「应用根\logs」的兜底算法。
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

            var file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file,
                $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [{source}]  线程 {Environment.CurrentManagedThreadId} =====\n"
                + ex + "\n");

            System.Diagnostics.Debug.WriteLine($"[crash:{source}] {ex}");
        }
        catch { /* 记不下来也不影响主流程 */ }
    }

}
