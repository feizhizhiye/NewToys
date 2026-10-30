using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 批量运行期间**阻止系统进入睡眠 / 休眠**（批量转码可能连续跑几十分钟，人不在电脑前）。
/// <para>
/// 「系统必需」这个需求挂在**调用 <c>SetThreadExecutionState</c> 的那条线程**上，所以专门起一条后台线程把它挂住，
/// <see cref="Dispose"/> 时才清标志并退出线程 —— 用 UI 线程调会在 await 之后因线程切换而失效。
/// </para>
/// <para>
/// 非 Windows 平台返回 <c>null</c>（不引用任何平台 API，调用方 <c>using var scope = KeepAwakeScope.Acquire();</c> 即可）。
/// 只拦睡眠，不拦熄屏（熄屏不影响转码，也没必要把人叫回来）。
/// </para>
/// </summary>
internal sealed class KeepAwakeScope : IDisposable
{
    private const uint EsContinuous = 0x8000_0000;
    private const uint EsSystemRequired = 0x0000_0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);

    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;

    private KeepAwakeScope() { }

    /// <summary>开始保持唤醒；不支持的平台返回 <c>null</c>（调用方照常 using）。</summary>
    public static KeepAwakeScope? Acquire()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var scope = new KeepAwakeScope();
        scope._thread = new Thread(scope.Run) { IsBackground = true, Name = "NewToys.KeepAwake" };
        scope._thread.Start();
        return scope;
    }

    private void Run()
    {
        try
        {
            SetThreadExecutionState(EsContinuous | EsSystemRequired);
            _stop.Wait();
        }
        catch { /* 拦不住睡眠不是致命错误：任务照跑 */ }
        finally
        {
            try { SetThreadExecutionState(EsContinuous); } catch { /* 同上 */ }
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _stop.Dispose();
    }
}
