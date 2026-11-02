using System;
using NewToys.Shared.Services;

namespace NewToys.Viewer.Services;

/// <summary>
/// 无音频后端（非 Windows 平台顶替 <see cref="NAudioPlayback"/>）。
/// <para>
/// 🔴 作用有两条：① 让「跨平台中立」在类型层成立（非 Windows 也能编译 / 装配）；
/// ② 用户点音频文件时给出**明确原因**而不是静默无声。
/// </para>
/// </summary>
public sealed class NullAudioPlayback : IAudioPlayback
{
    /// <summary>恒为停止。</summary>
    public AudioPlayState State => AudioPlayState.Stopped;

    /// <summary>恒为 0。</summary>
    public TimeSpan Position => TimeSpan.Zero;

    /// <summary>恒为 0。</summary>
    public TimeSpan Duration => TimeSpan.Zero;

    /// <summary>音量（保存值，无实际作用）。</summary>
    public float Volume { get; set; } = 0.8f;

    public event EventHandler? StateChanged;
    public event EventHandler? PlaybackEnded;

    /// <summary>永远失败，并说明原因。</summary>
    public bool Open(string path, out string error)
    {
        error = "当前平台没有可用的音频后端（Windows 用 NAudio；其它平台待接 mpv / libvlc）。";
        return false;
    }

    public void Play() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Pause() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Stop() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Seek(TimeSpan position) { }

    public void Dispose() { }
}
