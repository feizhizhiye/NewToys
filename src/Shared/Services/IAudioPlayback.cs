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

/// <summary>
/// 音频播放后端契约。
/// <para>
/// 🔴 引擎实现住在 <c>Core</c>（Windows 后端），界面控件住在 <c>NewToys.UI</c> 并由宿主注入本接口 ——
/// <c>NewToys.UI</c> ⛔ 不引 NAudio / 平台库，换后端（mpv / libvlc）只换实现。
/// </para>
/// <para>🔴 所有成员都按「可被 UI 线程随时调用」设计；实现方内部自行切线程，⛔ 不让调用方 await。</para>
/// </summary>
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
