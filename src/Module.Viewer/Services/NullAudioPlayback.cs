using System;
using NewToys.Shared.Services;

namespace NewToys.Viewer.Services;

/// <summary>无音频后端（非 Windows 平台顶替 NAudioPlayback）。（WHY: VW-016）</summary>
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

    /// <summary>本平台不会播完（永远打不开），订阅方拿不到任何通知。</summary>
    public event EventHandler? PlaybackEnded { add { } remove { } }

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
