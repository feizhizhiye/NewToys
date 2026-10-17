using System;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 「视频工具」模块设置的读写契约（v3.64 / NT-061，需求 §3.5 + §4.9）：**本模块配置的唯一写入点**。
/// <list type="bullet">
///   <item>数据落 <c>&lt;配置根&gt;\config\video-tools.json</c>（走 Core 的 <c>IOptionsStore</c>）。</item>
///   <item>本模块独有的可配项很少 —— <b>批处理 / 媒体信息并发数</b>（原来只能手改 <c>app.json</c> 且没有界面）。</item>
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
}
