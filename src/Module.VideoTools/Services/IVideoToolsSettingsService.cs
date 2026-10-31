using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 「视频工具」模块设置的读写契约（v3.64，需求 §3.5 + §4.9）：**本模块配置的唯一写入点**。
/// <list type="bullet">
///   <item>数据落 <c>&lt;配置根&gt;\config\video-tools.json</c>（走 Core 的 <c>IOptionsStore</c>）。</item>
///   <item>本模块独有的可配项很少 —— <b>批处理 / 媒体信息并发数</b>（旧值在 <c>app.json</c>，无界面、只能手改文件）。</item>
///   <item>⚠️ <c>ffmpeg</c> / 外部播放器 / 导入导出目录是**全局项**（「设置 → 路径与配置」，<c>app.json</c>）：
///   媒体资产模块也在用同一份 ⇒ ⛔ 不下沉、⛔ 不另存一份（与 §4.9「复用全局项」同一条规则）。</item>
/// </list>
/// </summary>
public interface IVideoToolsSettingsService
{
    /// <summary>选项存储里的模块分区名（= 配置文件名基名）。</summary>
    string ModuleKey { get; }

    /// <summary>本模块设置文件全路径（<c>config\video-tools.json</c>）。</summary>
    string FilePath { get; }

    /// <summary>并发数（ffprobe 媒体信息 / 批处理），1~16；非法或缺省 = <see cref="DefaultMaxConcurrency"/>。</summary>
    int MaxConcurrency { get; }

    /// <summary>并发数默认值。</summary>
    int DefaultMaxConcurrency { get; }

    /// <summary>写盘成功后触发。</summary>
    event Action? Changed;

    /// <summary>写「并发数」；返回状态文案（失败 ⇒ <c>⚠</c> 开头 + 未写盘）。</summary>
    string SetMaxConcurrency(int value);

    /// <summary>
    /// 转码前收益判定的策略：<see cref="AdviceModes.Off"/>（不判）/ <see cref="AdviceModes.Suggest"/>
    /// （只标黄，照跑）/ <see cref="AdviceModes.Strict"/>（不值得就跳过）。
    /// </summary>
    string AdviceMode { get; }

    /// <summary>写判定策略；返回状态文案。</summary>
    string SetAdviceMode(string value);

    /// <summary>省幅门槛（%）：低于它就算「不值得」。</summary>
    int AdviceMinSavingPercent { get; }

    /// <summary>写省幅门槛；返回状态文案。</summary>
    string SetAdviceMinSavingPercent(int value);
}

/// <summary>收益判定策略的取值（存进 <c>video-tools.json</c> 的 <c>adviceMode</c>）。</summary>
public static class AdviceModes
{
    /// <summary>不判定（回到旧行为）。</summary>
    public const string Off = "off";

    /// <summary>只在列表 / 详情卡提示，任务照跑（默认）。</summary>
    public const string Suggest = "suggest";

    /// <summary>预估不值得的任务直接跳过（记一条原因，不算失败）。</summary>
    public const string Strict = "strict";

    /// <summary>下拉选项（<c>Value</c> = 存盘值）。</summary>
    public static IReadOnlyList<AdviceModeOption> All { get; } = new[]
    {
        new AdviceModeOption { Value = Suggest, Label = "建议（标黄提示，照跑）" },
        new AdviceModeOption { Value = Strict,  Label = "严格（不值得就跳过，不执行）" },
        new AdviceModeOption { Value = Off,     Label = "关闭（不判定）" },
    };

    public static string Normalize(string? value)
        => All.Any(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))
            ? value!.ToLowerInvariant()
            : Suggest;
}

/// <summary>「收益判定策略」下拉项。</summary>
public sealed class AdviceModeOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}
