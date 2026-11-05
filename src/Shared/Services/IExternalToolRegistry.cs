using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 一个**外部程序**的探测结果（v3.65，需求 §4.9.2 第 5 条）。
/// </summary>
public sealed class ExternalToolStatus
{
    /// <summary>稳定键（= <c>app.json</c> 里的字段名，如 <c>FfmpegPath</c>）。</summary>
    public string Key { get; init; } = "";

    /// <summary>显示名（如「ffmpeg」）。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>谁在用（状态栏提示里带上，让人知道为什么要管它）。</summary>
    public string UsedBy { get; init; } = "";

    /// <summary>是不是**应用硬依赖**：缺失时功能真的不可用（如 ffmpeg）。</summary>
    public bool IsRequired { get; init; }

    /// <summary>用户**显式配了路径但那个文件不在**（比"留空"严重 —— 留空 = 用系统默认）。</summary>
    public bool ConfiguredButMissing { get; init; }

    /// <summary>生效值（留空走系统默认时给出候选名，仅作展示）。</summary>
    public string EffectivePath { get; init; } = "";

    /// <summary>能否解析到可用程序。</summary>
    public bool Found { get; init; }

    /// <summary>是否要提醒用户（= 硬依赖缺失，或配了不存在的路径）。</summary>
    public bool NeedsAttention => !Found && (IsRequired || ConfiguredButMissing);

    /// <summary>状态栏里的提醒短语。</summary>
    public string WarnText => IsRequired
        ? $"{DisplayName}（{UsedBy}）"
        : $"{DisplayName} 路径不存在";
}

/// <summary>「外部程序注册表」契约（，需求 §4.9.2 第 5 条）。（WHY: SH-037）</summary>
public interface IExternalToolRegistry
{
    /// <summary>探测全部外部程序（结果缓存；⛔ 首次调用是唯一一次真实解析）。</summary>
    IReadOnlyList<ExternalToolStatus> ProbeAll();

    /// <summary>上一次 <see cref="ProbeAll"/> 的**需要注意**项（还没探测过 = 空集合）。</summary>
    IReadOnlyList<ExternalToolStatus> NeedsAttention { get; }

    /// <summary>是否已经探测过（状态栏 / 页面据此决定要不要显示"未探测"以外的文案）。</summary>
    bool HasProbed { get; }
}
