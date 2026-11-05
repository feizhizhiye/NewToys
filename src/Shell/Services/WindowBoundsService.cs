using System;
using System.Text.Json;
using NewToys.Core.Services;

namespace NewToys.Shell.Services;

/// <summary>主窗口大小 / 位置的记忆（，需求 §四）：落 config\window.json。（WHY: SHL-027）</summary>
public class WindowBoundsService
{
    private readonly ShellConfigStore _shell;

    public WindowBoundsService(ShellConfigStore shell) => _shell = shell;

    /// <summary>⚠️ 起单位统一为外框物理像素（X / Y / Width / Height 都是 GetWindowRect 那一套）： 存取一律走外框 + 用 SetWindowPos 直接设外框 = 绕开 Avalonia 的「外框 = 客户区 + 边框」换算 → 闭合且稳定（若存 ClientSize，恢复时会被再套一圈边框、窗口每次启动都变大）。</summary>
    public sealed class WindowBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary><c>Normal</c> / <c>Maximized</c>。</summary>
        public string State { get; set; } = "Normal";
    }

    /// <summary>读上次的窗口边界（<c>shell.json</c> 的 <c>window</c> 节）；节不存在 / 读坏 → <c>null</c>（用默认窗口）。</summary>
    public WindowBounds? Load()
    {
        try
        {
            return _shell.Get("window")?.Deserialize<WindowBounds>();
        }
        catch { return null; }
    }

    /// <summary>存窗口边界；写盘失败静默吞掉（记窗口位置而已，不能影响关闭流程）。</summary>
    public void Save(WindowBounds bounds)
    {
        try
        {
            _shell.Set("window", JsonSerializer.SerializeToNode(bounds));
        }
        catch { /* 记不下来不影响主流程 */ }
    }
}
