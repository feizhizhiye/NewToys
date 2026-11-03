using System;
using NAudio.Wave;
using NewToys.Shared.Services;

namespace NewToys.Viewer.Services;

/// <summary>单个双二阶段（RBJ cookbook）的系数，已按 a0 归一。</summary>
internal readonly struct Biquad
{
    public readonly double B0;
    public readonly double B1;
    public readonly double B2;
    public readonly double A1;
    public readonly double A2;

    private Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        B0 = b0;
        B1 = b1;
        B2 = b2;
        A1 = a1;
        A2 = a2;
    }

    /// <summary>按段定义算系数；采样率太低（无法容纳目标频率）时返回直通。</summary>
    public static Biquad Create(in ToneStage stage, double sampleRate)
    {
        var freq = Math.Clamp(stage.Freq, 20.0, sampleRate / 2.0 * 0.9);
        var w0 = 2.0 * Math.PI * freq / sampleRate;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);

        if (stage.Kind == ToneStageKind.Peaking)
        {
            var a = Math.Pow(10, stage.GainDb / 40.0);
            var alpha = sin / (2.0 * Math.Max(0.1, stage.Q));
            var a0 = 1 + alpha / a;
            return new Biquad(
                (1 + alpha * a) / a0,
                -2 * cos / a0,
                (1 - alpha * a) / a0,
                -2 * cos / a0,
                (1 - alpha / a) / a0);
        }

        var amp = Math.Pow(10, stage.GainDb / 40.0);
        var sqrt = Math.Sqrt(amp);
        var shelfAlpha = sin / 2.0 * Math.Sqrt(2.0);   // S = 1

        if (stage.Kind == ToneStageKind.LowShelf)
        {
            var a0 = (amp + 1) + (amp - 1) * cos + 2 * sqrt * shelfAlpha;
            return new Biquad(
                amp * ((amp + 1) - (amp - 1) * cos + 2 * sqrt * shelfAlpha) / a0,
                2 * amp * ((amp - 1) - (amp + 1) * cos) / a0,
                amp * ((amp + 1) - (amp - 1) * cos - 2 * sqrt * shelfAlpha) / a0,
                -2 * ((amp - 1) + (amp + 1) * cos) / a0,
                ((amp + 1) + (amp - 1) * cos - 2 * sqrt * shelfAlpha) / a0);
        }

        var hs0 = (amp + 1) - (amp - 1) * cos + 2 * sqrt * shelfAlpha;
        return new Biquad(
            amp * ((amp + 1) + (amp - 1) * cos + 2 * sqrt * shelfAlpha) / hs0,
            -2 * amp * ((amp - 1) + (amp + 1) * cos) / hs0,
            amp * ((amp + 1) + (amp - 1) * cos - 2 * sqrt * shelfAlpha) / hs0,
            2 * ((amp - 1) - (amp + 1) * cos) / hs0,
            ((amp + 1) - (amp - 1) * cos - 2 * sqrt * shelfAlpha) / hs0);
    }
}

internal enum ToneStageKind
{
    Peaking,
    LowShelf,
    HighShelf,
}

/// <summary>一段均衡：频率 / 增益（dB）/ Q 值 / 段类型。</summary>
internal readonly record struct ToneStage(double Freq, double GainDb, double Q, ToneStageKind Kind);

/// <summary>
/// 音色预设表：键与显示名对外（<see cref="IAudioTone"/>），曲线只在本模块内定义。
/// <para>🔴 「off」必须是首项且零段 —— 界面拿它当「直通」。</para>
/// </summary>
internal static class TonePresets
{
    public static readonly AudioTonePreset[] All =
    {
        new("off", "关闭"),
        new("bass", "低音增强"),
        new("pop", "流行"),
        new("vocal", "人声"),
        new("classical", "古典"),
    };

    public static bool Exists(string key) => Array.Exists(All, p => p.Key == key);

    public static string NameOf(string key)
    {
        var index = Array.FindIndex(All, p => p.Key == key);
        return index < 0 ? All[0].Name : All[index].Name;
    }

    public static ToneStage[] StagesOf(string key) => key switch
    {
        "bass" => new[]
        {
            new ToneStage(110, 5.5, 0.7, ToneStageKind.LowShelf),
            new ToneStage(60, 2.5, 1.0, ToneStageKind.Peaking),
        },
        "pop" => new[]
        {
            new ToneStage(120, 2.0, 0.7, ToneStageKind.LowShelf),
            new ToneStage(900, -2.0, 1.0, ToneStageKind.Peaking),
            new ToneStage(9000, 3.0, 0.7, ToneStageKind.HighShelf),
        },
        "vocal" => new[]
        {
            new ToneStage(150, -1.5, 0.7, ToneStageKind.LowShelf),
            new ToneStage(2600, 4.0, 1.2, ToneStageKind.Peaking),
            new ToneStage(11000, -2.0, 0.7, ToneStageKind.HighShelf),
        },
        "classical" => new[]
        {
            new ToneStage(100, 1.5, 0.7, ToneStageKind.LowShelf),
            new ToneStage(3000, 2.0, 0.9, ToneStageKind.Peaking),
            new ToneStage(11000, 1.5, 0.7, ToneStageKind.HighShelf),
        },
        _ => Array.Empty<ToneStage>(),
    };
}

/// <summary>当前生效的滤波器系数（整体替换，音频线程只读引用）。</summary>
internal sealed class ToneSettings
{
    private volatile Biquad[] _biquads = Array.Empty<Biquad>();

    public Biquad[] Biquads => _biquads;

    public void Apply(string key, double sampleRate)
    {
        var stages = TonePresets.StagesOf(key);
        if (stages.Length == 0 || sampleRate < 8000)
        {
            _biquads = Array.Empty<Biquad>();
            return;
        }

        var list = new Biquad[stages.Length];
        for (var i = 0; i < stages.Length; i++) list[i] = Biquad.Create(stages[i], sampleRate);
        _biquads = list;
    }
}

/// <summary>
/// 音色滤波链（插在解码器与声卡之间，频谱旁路在它之后 ⇒ 频谱反映实际听到的声音）。
/// <para>🔴 只处理 16 / 24 / 32 位整数与 32 位浮点 PCM；其余格式原样透传（⛔ 报错打断播放）。</para>
/// <para>🔴 <see cref="Read"/> 跑在音频线程：系数整体替换、状态数组只在该线程读写，⛔ 加锁。</para>
/// </summary>
internal sealed class ToneFilter : IWaveProvider
{
    private readonly IWaveProvider _source;
    private readonly ToneSettings _settings;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly bool _isFloat;
    private readonly bool _supported;

    private Biquad[] _active = Array.Empty<Biquad>();
    private double[][] _z1 = Array.Empty<double[]>();
    private double[][] _z2 = Array.Empty<double[]>();

    public ToneFilter(IWaveProvider source, ToneSettings settings)
    {
        _source = source;
        _settings = settings;

        var format = source.WaveFormat;
        _channels = Math.Max(1, format.Channels);
        _bytesPerSample = format.BitsPerSample / 8;
        _isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
        _supported = format.BitsPerSample is 16 or 24 or 32 && _channels <= 8;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<byte> buffer)
    {
        var read = _source.Read(buffer);
        if (read <= 0 || !_supported) return read;

        var biquads = _settings.Biquads;
        if (biquads.Length == 0) return read;
        if (!ReferenceEquals(biquads, _active)) Reset(biquads);

        var bytesPerFrame = _bytesPerSample * _channels;
        var frames = read / bytesPerFrame;

        for (var frame = 0; frame < frames; frame++)
        {
            var frameStart = frame * bytesPerFrame;
            for (var ch = 0; ch < _channels; ch++)
            {
                var offset = frameStart + ch * _bytesPerSample;
                var sample = ReadSample(buffer, offset);

                for (var s = 0; s < _active.Length; s++)
                {
                    var bq = _active[s];
                    var y = bq.B0 * sample + _z1[s][ch];
                    _z1[s][ch] = bq.B1 * sample - bq.A1 * y + _z2[s][ch];
                    _z2[s][ch] = bq.B2 * sample - bq.A2 * y;
                    sample = y;
                }

                WriteSample(buffer, offset, sample);
            }
        }

        return read;
    }

    private void Reset(Biquad[] biquads)
    {
        _active = biquads;
        _z1 = new double[biquads.Length][];
        _z2 = new double[biquads.Length][];
        for (var i = 0; i < biquads.Length; i++)
        {
            _z1[i] = new double[_channels];
            _z2[i] = new double[_channels];
        }
    }

    private double ReadSample(ReadOnlySpan<byte> buffer, int offset)
    {
        switch (_bytesPerSample)
        {
            case 2:
                return (short)(buffer[offset] | (buffer[offset + 1] << 8)) / 32768.0;
            case 3:
                var v24 = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((v24 & 0x800000) != 0) v24 |= unchecked((int)0xFF000000);
                return v24 / 8388608.0;
            default:
                if (_isFloat) return BitConverter.ToSingle(buffer.Slice(offset, 4));
                return BitConverter.ToInt32(buffer.Slice(offset, 4)) / 2147483648.0;
        }
    }

    private void WriteSample(Span<byte> buffer, int offset, double sample)
    {
        sample = Math.Clamp(sample, -1.0, 1.0);

        switch (_bytesPerSample)
        {
            case 2:
                var v16 = (short)Math.Round(sample * 32767.0);
                buffer[offset] = (byte)v16;
                buffer[offset + 1] = (byte)(v16 >> 8);
                return;
            case 3:
                var v24 = (int)Math.Round(sample * 8388607.0);
                buffer[offset] = (byte)v24;
                buffer[offset + 1] = (byte)(v24 >> 8);
                buffer[offset + 2] = (byte)(v24 >> 16);
                return;
            default:
                if (_isFloat)
                {
                    BitConverter.TryWriteBytes(buffer.Slice(offset, 4), (float)sample);
                    return;
                }
                BitConverter.TryWriteBytes(buffer.Slice(offset, 4), (int)Math.Round(sample * 2147483647.0));
                return;
        }
    }
}
