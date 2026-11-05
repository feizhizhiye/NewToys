using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>音色预设（键 = 稳定标识，名 = 界面显示）。</summary>
public sealed record AudioTonePreset(string Key, string Name);

/// <summary>音色（均衡）切换能力。（WHY: SH-030）</summary>
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
