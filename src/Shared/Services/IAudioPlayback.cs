using System;

namespace NewToys.Shared.Services;

/// <summary>音频播放状态。</summary>
public enum AudioPlayState
{
    /// <summary>未开始 / 已停止。</summary>
    Stopped = 0,
    /// <summary>正在播放。</summary>
    Playing = 1,
    /// <summary>已暂停（位置保留）。</summary>
    Paused = 2,
}

/// <summary>音频播放后端契约。（WHY: SH-028）</summary>
public interface IAudioPlayback : IDisposable
{
    /// <summary>当前状态。</summary>
    AudioPlayState State { get; }

    /// <summary>播放位置（调用方轮询，用于刷新进度条）。</summary>
    TimeSpan Position { get; }

    /// <summary>总时长；未知为 <see cref="TimeSpan.Zero"/>。</summary>
    TimeSpan Duration { get; }

    /// <summary>音量 0~1。</summary>
    float Volume { get; set; }

    /// <summary>状态变化（播放 / 暂停 / 停止）。</summary>
    event EventHandler? StateChanged;

    /// <summary>自然播完（用于「下一首」）。⛔ 暂停 / 停止不触发。</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>装载一个音频文件；失败返回 false 并给出原因（界面 ⛔ 静默失败）。</summary>
    bool Open(string path, out string error);

    /// <summary>开始 / 继续播放。</summary>
    void Play();

    /// <summary>暂停（位置保留）。</summary>
    void Pause();

    /// <summary>停止并回到 0。</summary>
    void Stop();

    /// <summary>跳转到指定位置（越界自动夹到 0~Duration）。</summary>
    void Seek(TimeSpan position);
}
