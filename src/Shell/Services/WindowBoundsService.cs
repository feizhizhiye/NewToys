using System;
using System.IO;
using System.Text.Json;
using NewToys.Core.Services;

namespace NewToys.Shell.Services;

/// <summary>
/// 主窗口大小 / 位置的记忆（v3.26，需求 §四）：落 <c>config\window.json</c>。
/// <para>
/// ⚠️ 按**配置**隔离（路径取 <see cref="ConfigStoreService.ConfigDir"/>）——多实例各记各的，
/// 「一半屏放本窗口」的分屏位置重启不丢。
/// </para>
/// <para>
/// 本服务只做 JSON 读写；「夹进屏幕工作区」这类判定留在窗口侧（<c>Screens</c> 是 Avalonia API，只有 Shell 引）。
/// </para>
/// </summary>
public class WindowBoundsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConfigStoreService _cfg;

    public WindowBoundsService(ConfigStoreService cfg) => _cfg = cfg;

    private string FilePath => Path.Combine(_cfg.ConfigDir, "window.json");

    /// <summary>窗口边界（逻辑坐标 = Avalonia 的 Position / ClientSize；<see cref="State"/> 存字符串）。</summary>
    public sealed class WindowBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary><c>Normal</c> / <c>Maximized</c>。</summary>
        public string State { get; set; } = "Normal";
    }

    /// <summary>读上次的窗口边界；文件不存在 / 读坏 → <c>null</c>（用默认窗口）。</summary>
    public WindowBounds? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(FilePath));
        }
        catch { return null; }
    }

    /// <summary>存窗口边界；写盘失败静默吞掉（记窗口位置而已，不能影响关闭流程）。</summary>
    public void Save(WindowBounds bounds)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(bounds, JsonOpts));
        }
        catch { /* 记不下来不影响主流程 */ }
    }
}
