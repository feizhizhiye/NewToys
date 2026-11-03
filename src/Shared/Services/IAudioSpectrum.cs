namespace NewToys.Shared.Services;

/// <summary>
/// 频谱数据源（可选能力）：播放后端在实现 <see cref="IAudioPlayback"/> 之余**额外**实现本接口，
/// 界面据此绘制分频柱；不实现则界面隐藏频谱区。
/// <para>
/// 🔴 与 <see cref="IAudioPlayback"/> 分开的原因：频谱是锦上添花，⛔ 不该逼所有后端（含
/// <c>NullAudioPlayback</c>）都去背 FFT 与 PCM 旁路。
/// </para>
/// <para>
/// 🔴 界面线程会按帧（≤30 fps）调用 <see cref="ReadSpectrum"/>：实现方内部自加锁，⛔ 阻塞音频线程。
/// </para>
/// </summary>
public interface IAudioSpectrum
{
    /// <summary>建议的频段数（界面按此分配柱宽）。</summary>
    int BandCount { get; }

    /// <summary>
    /// 取一帧频谱：把 <paramref name="bands"/> 每格填 0~1 的幅度（低频在前）。
    /// <para>返回实际填充的段数；未在播放返回 0（界面据此隐藏频谱）。</para>
    /// </summary>
    int ReadSpectrum(float[] bands);
}
