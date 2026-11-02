using System;
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
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NAudioPlayback : IAudioPlayback
{
    private readonly object _gate = new();

    private WaveStream? _reader;
    private IWavePlayer? _output;
    private float _volume = 0.8f;
    private bool _stopping;

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
                var output = new WaveOut { BufferMilliseconds = 120, NumberOfBuffers = 3 };
                output.Init(reader);
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
    }
}
