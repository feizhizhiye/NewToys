using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>音色预设（键 = 稳定标识，名 = 界面显示）。</summary>
public sealed record AudioTonePreset(string Key, string Name);

/// <summary>
/// 音色（均衡）切换能力。
/// <para>
/// 🔴 与 <see cref="IAudioPlayback"/> **分开**：不是每个后端都做得出滤波（<c>NullAudioPlayback</c> 就不做），
/// 界面按 <c>Playback as IAudioTone</c> 判断 —— 拿不到就不显示音色键，⛔ 改 <see cref="IAudioPlayback"/> 逼所有后端实现。
/// </para>
/// </summary>
public interface IAudioTone
{
    /// <summary>可选预设（首个约定为「关闭 / 直通」）。</summary>
    IReadOnlyList<AudioTonePreset> Presets { get; }

    /// <summary>当前预设键。</summary>
    string CurrentPresetKey { get; }

    /// <summary>预设切换成功（播放中即时生效，⛔ 需要重开文件）。</summary>
    event EventHandler? PresetChanged;

    /// <summary>切换预设；未知键返回 false 并保持原状。</summary>
    bool ApplyPreset(string key);
}
