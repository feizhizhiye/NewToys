using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using NAudio.Wave;
using NewToys.Shared.Services;

namespace NewToys.Viewer.Services;

/// <summary>
/// 音频播放后端（NAudio）：Media Foundation 解码 + WaveOut 输出。
/// <para>
/// 🔴 解码优先 <see cref="MediaFoundationReader"/>（mp3 / m4a / aac / wma / flac，Win10 自带解码器），
/// 失败回退 <see cref="AudioFileReader"/>（wav / aiff / mp3）—— 后者是纯托管路径，覆盖面兜底。
/// </para>
/// <para>
/// 🔴 <see cref="IAudioPlayback"/> 是跨平台契约，本类是 **Windows 平台底座**（`[SupportedOSPlatform]`）：
/// 非 Windows 由 <see cref="NullAudioPlayback"/> 顶替，换后端（mpv / libvlc）只换这个类。
/// </para>
/// <para>
/// 🔴 本类同时实现 <see cref="IAudioSpectrum"/>：采样链上挂一层旁路（`SpectrumTap`）抄一份样本，
/// FFT 与分频都在这里算完 —— 界面只拿 32 个 0~1 的数，⛔ 让 UI 库认识 NAudio。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NAudioPlayback : IAudioPlayback, IAudioSpectrum, IAudioTone
{
    private const int FftSize = 1024;
    private const int RingSize = 8192;

    private readonly object _gate = new();
    private readonly object _spectrumGate = new();
    private readonly float[] _ring = new float[RingSize];
    private readonly float[] _bands = new float[32];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];

    /// <summary>音色滤波系数（挂解码器之后、频谱旁路之前）。</summary>
    private readonly ToneSettings _tone = new();
    private string _toneKey = "off";

    private WaveStream? _reader;
    private IWavePlayer? _output;
    private float _volume = 0.8f;
    private bool _stopping;
    private int _ringPos;
    private long _ringFilled;
    private int _sampleRate = 44100;

    /// <summary>频谱段数（界面按此分配柱宽）。</summary>
    public int BandCount => _bands.Length;

    /// <summary>当前状态。</summary>
    public AudioPlayState State { get; private set; } = AudioPlayState.Stopped;

    /// <summary>播放位置。</summary>
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                try { return _reader?.CurrentTime ?? TimeSpan.Zero; }
                catch { return TimeSpan.Zero; }
            }
        }
    }

    /// <summary>总时长。</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                try { return _reader?.TotalTime ?? TimeSpan.Zero; }
                catch { return TimeSpan.Zero; }
            }
        }
    }

    /// <summary>音量 0~1。</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            lock (_gate)
            {
                if (_output is not null)
                {
                    try { _output.Volume = _volume; } catch { /* 设备已失效 */ }
                }
            }
        }
    }

    /// <summary>状态变化。</summary>
    public event EventHandler? StateChanged;

    /// <summary>自然播完。</summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>装载文件。</summary>
    public bool Open(string path, out string error)
    {
        error = "";
        if (!File.Exists(path)) { error = "文件不存在。"; return false; }

        lock (_gate)
        {
            Cleanup();

            WaveStream reader;
            try
            {
                reader = new MediaFoundationReader(path);
            }
            catch (Exception first)
            {
                try
                {
                    reader = new AudioFileReader(path);
                }
                catch (Exception second)
                {
                    error = "解码失败：" + first.Message + " / " + second.Message;
                    return false;
                }
            }

            try
            {
                _sampleRate = reader.WaveFormat.SampleRate;
                ResetSpectrum();

                var output = new WaveOut { BufferMilliseconds = 120, NumberOfBuffers = 3 };
                // 采样链 = 解码器 → 音色滤波 → 频谱旁路 → 声卡（频谱看到的是实际输出的声音）
                _tone.Apply(_toneKey, _sampleRate);
                output.Init(new SpectrumTap(new ToneFilter(reader, _tone), this));
                output.Volume = _volume;
                output.PlaybackStopped += OnPlaybackStopped;

                _reader = reader;
                _output = output;
                State = AudioPlayState.Stopped;
                return true;
            }
            catch (Exception ex)
            {
                try { reader.Dispose(); } catch { }
                error = "音频设备初始化失败：" + ex.Message;
                return false;
            }
        }
    }

    /// <summary>播放 / 继续。</summary>
    public void Play()
    {
        lock (_gate)
        {
            if (_output is null) return;
            try
            {
                // 已在末尾（自然播完）⇒ 不回零就 Play，声卡拿不到样本会立刻再报一次「播完」
                if (_reader is not null && _reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(200))
                    _reader.CurrentTime = TimeSpan.Zero;

                _stopping = false;
                _output.Play();
                State = AudioPlayState.Playing;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { /* 设备被拔 / 被独占 */ }
        }
    }

    /// <summary>暂停。</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_output is null) return;
            try
            {
                _output.Pause();
                State = AudioPlayState.Paused;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }
    }

    /// <summary>停止并回到开头。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_output is null) return;
            try
            {
                _stopping = true;
                _output.Stop();
                if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero;
                State = AudioPlayState.Stopped;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }
    }

    /// <summary>跳转。</summary>
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_reader is null) return;
            try
            {
                var max = _reader.TotalTime;
                var target = position < TimeSpan.Zero ? TimeSpan.Zero : (position > max ? max : position);
                _reader.CurrentTime = target;
            }
            catch { }
        }
    }

    /// <summary>释放播放器与解码器。</summary>
    public void Dispose()
    {
        lock (_gate) Cleanup();
    }

    // ==================== 音色（IAudioTone） ====================

    /// <summary>音色预设。</summary>
    public IReadOnlyList<AudioTonePreset> Presets => TonePresets.All;

    /// <summary>当前音色预设键。</summary>
    public string CurrentPresetKey => _toneKey;

    /// <summary>音色切换（播放中即时生效）。</summary>
    public event EventHandler? PresetChanged;

    /// <summary>切换音色；未知键返回 <c>false</c> 且保持原样。</summary>
    public bool ApplyPreset(string key)
    {
        if (!TonePresets.Exists(key)) return false;
        if (key == _toneKey) return true;

        _toneKey = key;
        _tone.Apply(key, _sampleRate > 0 ? _sampleRate : 44100);
        PresetChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // ==================== 频谱（IAudioSpectrum） ====================

    /// <summary>
    /// 取一帧频谱：最近 1024 帧 × Hamming 窗 → FFT → 对数分频到 <see cref="BandCount"/> 段。
    /// <para>🔴 只在播放中出数据：暂停 / 停止返回 0，界面据此隐藏频谱区。</para>
    /// </summary>
    public int ReadSpectrum(float[] bands)
    {
        if (bands is null || bands.Length < _bands.Length) return 0;

        if (State != AudioPlayState.Playing)
        {
            lock (_spectrumGate) ClearBands(bands);
            return 0;
        }

        lock (_spectrumGate)
        {
            if (_ringFilled < FftSize) { ClearBands(bands); return 0; }

            var start = _ringPos - FftSize;
            if (start < 0) start += RingSize;
            for (var i = 0; i < FftSize; i++)
            {
                var window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
                _re[i] = _ring[(start + i) % RingSize] * window;
                _im[i] = 0;
            }
        }

        ComputeSpectrum();

        var sampleRate = _sampleRate > 0 ? _sampleRate : 44100;
        const double minHz = 60.0;
        var maxHz = Math.Min(16000.0, sampleRate / 2.0);
        if (maxHz <= minHz) maxHz = minHz * 2;

        var ratioPerBand = Math.Pow(maxHz / minHz, 1.0 / _bands.Length);

        lock (_spectrumGate)
        {
            for (var b = 0; b < _bands.Length; b++)
            {
                var lo = minHz * Math.Pow(ratioPerBand, b);
                var hi = lo * ratioPerBand;
                var from = Math.Max(1, (int)Math.Floor(lo * FftSize / sampleRate));
                var to = Math.Min(FftSize / 2, (int)Math.Ceiling(hi * FftSize / sampleRate));
                if (to <= from) to = Math.Min(from + 1, FftSize / 2);

                var peak = 0.0;
                for (var i = from; i < to; i++)
                {
                    var magnitude = Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]);
                    if (magnitude > peak) peak = magnitude;
                }

                // 幅度 → dB → 0~1（-60 dB 以下当静音）
                var db = 20 * Math.Log10(peak / (FftSize / 4.0) + 1e-9);
                var value = (float)Math.Clamp((db + 60) / 60, 0, 1);

                // 回落衰减：取「新值」与「旧值 ×0.82」中的大者，避免柱高闪烁
                var decayed = _bands[b] * 0.82f;
                _bands[b] = value > decayed ? value : decayed;
                bands[b] = _bands[b];
            }
        }

        return _bands.Length;
    }

    /// <summary>
    /// 原位基 2 FFT（迭代版）：结果留在 <see cref="_re"/> / <see cref="_im"/>。
    /// <para>🔴 自己写而不是拿 NAudio.Dsp：那套 API 随版本改动命名空间，⛔ 让「能不能编译」押在它身上。</para>
    /// </summary>
    private void ComputeSpectrum()
    {
        var j = 0;
        for (var i = 1; i < FftSize; i++)
        {
            var bit = FftSize >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;

            if (i >= j) continue;
            (_re[i], _re[j]) = (_re[j], _re[i]);
            (_im[i], _im[j]) = (_im[j], _im[i]);
        }

        for (var len = 2; len <= FftSize; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            var half = len / 2;

            for (var i = 0; i < FftSize; i += len)
            {
                double curRe = 1, curIm = 0;

                for (var k = 0; k < half; k++)
                {
                    var aRe = _re[i + k];
                    var aIm = _im[i + k];
                    var bRe = _re[i + k + half] * curRe - _im[i + k + half] * curIm;
                    var bIm = _re[i + k + half] * curIm + _im[i + k + half] * curRe;

                    _re[i + k] = aRe + bRe;
                    _im[i + k] = aIm + bIm;
                    _re[i + k + half] = aRe - bRe;
                    _im[i + k + half] = aIm - bIm;

                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }

    /// <summary>音频线程侧：把刚解码的一块字节样本抄进环形缓冲（多声道混成单声道）。</summary>
    private void PushSamples(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        var blockAlign = format.BlockAlign;
        if (blockAlign <= 0) return;

        var channels = format.Channels <= 0 ? 1 : format.Channels;
        var frames = buffer.Length / blockAlign;
        if (frames <= 0) return;

        var perChannel = blockAlign / channels;
        if (perChannel <= 0) return;

        lock (_spectrumGate)
        {
            for (var f = 0; f < frames; f++)
            {
                var baseIndex = f * blockAlign;

                var sum = 0f;
                var ok = true;
                for (var c = 0; c < channels; c++)
                {
                    if (!TryReadSample(buffer, baseIndex + c * perChannel, format, out var sample))
                    {
                        ok = false;
                        break;
                    }
                    sum += sample;
                }
                if (!ok) break;

                _ring[_ringPos] = sum / channels;
                _ringPos = _ringPos + 1 == RingSize ? 0 : _ringPos + 1;
            }
            _ringFilled += frames;
        }
    }

    /// <summary>按位深把一个样本读成 -1~1 的 float（不认识的格式返回 false，频谱自动退化为不出数据）。</summary>
    private static bool TryReadSample(ReadOnlySpan<byte> buffer, int index, WaveFormat format, out float value)
    {
        value = 0f;

        switch (format.BitsPerSample)
        {
            case 16:
                if (index + 2 > buffer.Length) return false;
                value = (short)(buffer[index] | (buffer[index + 1] << 8)) / 32768f;
                return true;

            case 24:
                if (index + 3 > buffer.Length) return false;
                var v24 = buffer[index] | (buffer[index + 1] << 8) | (buffer[index + 2] << 16);
                if ((v24 & 0x800000) != 0) v24 |= unchecked((int)0xFF000000);
                value = v24 / 8388608f;
                return true;

            case 32:
                if (index + 4 > buffer.Length) return false;
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    value = BitConverter.ToSingle(buffer.Slice(index, 4));
                    return true;
                }
                value = BitConverter.ToInt32(buffer.Slice(index, 4)) / 2147483648f;
                return true;

            case 8:
                if (index + 1 > buffer.Length) return false;
                value = (buffer[index] - 128) / 128f;
                return true;

            default:
                return false;
        }
    }

    private void ResetSpectrum()
    {
        lock (_spectrumGate)
        {
            Array.Clear(_ring, 0, _ring.Length);
            Array.Clear(_bands, 0, _bands.Length);
            _ringPos = 0;
            _ringFilled = 0;
        }
    }

    private void ClearBands(float[] bands)
    {
        for (var i = 0; i < bands.Length; i++) bands[i] = 0;
        Array.Clear(_bands, 0, _bands.Length);
    }

    /// <summary>采样旁路：声卡拿到的每一块字节样本顺手抄一份给频谱，返回值原样透传（格式不变）。</summary>
    private sealed class SpectrumTap : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly NAudioPlayback _owner;

        public SpectrumTap(IWaveProvider source, NAudioPlayback owner)
        {
            _source = source;
            _owner = owner;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(Span<byte> buffer)
        {
            var read = _source.Read(buffer);
            if (read > 0) _owner.PushSamples(buffer[..read], WaveFormat);
            return read;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool natural;

        lock (_gate)
        {
            natural = !_stopping;
            _stopping = false;
            State = AudioPlayState.Stopped;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        if (natural) PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void Cleanup()
    {
        if (_output is not null)
        {
            try
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
                _output.Dispose();
            }
            catch { }
            _output = null;
        }

        if (_reader is not null)
        {
            try { _reader.Dispose(); } catch { }
            _reader = null;
        }

        State = AudioPlayState.Stopped;
        ResetSpectrum();
    }
}
