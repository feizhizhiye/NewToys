using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewToys.Reading.Services;

/// <summary>朗读进度：正在读第几句（0 基）/ 共几句。</summary>
public sealed record TtsProgress(int Index, int Count);

/// <summary>听书：把一章正文切成句子，交给系统语音（Windows 自带 SAPI SAPI.SpVoice）逐句朗读。（WHY: RD-029）</summary>
public sealed class TtsService : IDisposable
{
    private object? _voice;
    private readonly object _speakGate = new();
    private CancellationTokenSource? _cts;
    private IReadOnlyList<string>? _voices;
    private volatile bool _paused;
    private volatile bool _speaking;
    private int _index;
    private int _count;

    /// <summary>语音合成是否可用（第一次朗读时探测；不可用后一直是 false）。</summary>
    public bool IsAvailable { get; private set; } = true;

    /// <summary>不可用的原因（可用时为空）。</summary>
    public string UnavailableReason { get; private set; } = "";

    /// <summary>正在朗读（含暂停中）。</summary>
    public bool IsSpeaking => _speaking;

    /// <summary>已暂停（句子边界停住）。</summary>
    public bool IsPaused => _paused;

    /// <summary>当前句索引（0 基）。</summary>
    public int Index => _index;

    /// <summary>总句数。</summary>
    public int Count => _count;

    /// <summary>朗读状态变了（开始 / 暂停 / 结束 / 失败）。</summary>
    public event EventHandler? StateChanged;

    /// <summary>换到某一句（后台线程触发，界面自己要切回 UI 线程）。</summary>
    public event EventHandler<TtsProgress>? SegmentStarted;

    /// <summary>开始 / 继续朗读 <paramref name="sentences"/>（从 <paramref name="from"/> 句起）。返回空串 = 已开始。</summary>
    public string Start(IReadOnlyList<string> sentences, int from, int rate, int volume, string? voiceName)
    {
        if (sentences.Count == 0) return "本章没有可朗读的正文。";
        if (!IsAvailable)
            return "⚠ " + (UnavailableReason.Length > 0 ? UnavailableReason : "听书不可用。");

        Stop();

        var cts = new CancellationTokenSource();
        _cts = cts;
        _count = sentences.Count;
        _index = Math.Clamp(from, 0, sentences.Count - 1);
        _paused = false;
        _speaking = true;
        RaiseState();

        var start = _index;
        _ = Task.Run(() => RunAsync(sentences, start, rate, volume, voiceName, cts), cts.Token);
        return "";
    }

    /// <summary>暂停（当前句读完后停住）。</summary>
    public void Pause()
    {
        if (!_speaking || _paused) return;
        _paused = true;
        RaiseState();
    }

    /// <summary>继续。</summary>
    public void Resume()
    {
        if (!_speaking || !_paused) return;
        _paused = false;
        RaiseState();
    }

    /// <summary>停止（当前句读完即止）。</summary>
    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null) return;

        try { cts.Cancel(); } catch { /* 已结束 */ }

        _paused = false;
        _speaking = false;
        RaiseState();
    }

    /// <summary>枚举系统音色（中文音色排前面）。COM 对象在后台线程用完即释放。</summary>
    public Task<IReadOnlyList<string>> LoadVoicesAsync()
    {
        if (_voices is { } cached) return Task.FromResult(cached);

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var list = new List<string>();
            object? voice = null;
            try
            {
                voice = CreateVoice(out _);
                if (voice is null) return Array.Empty<string>();

                var tokens = Call(voice, "GetVoices", "", "")!;
                var count = Convert.ToInt32(Get(tokens, "Count"));
                for (var i = 0; i < count; i++)
                {
                    var token = Call(tokens, "Item", i)!;
                    if (Call(token, "GetDescription", 0) is string desc && desc.Trim().Length > 0) list.Add(desc.Trim());
                }
            }
            catch { /* 枚举失败 ⇒ 下拉里只有「系统默认」 */ }
            finally { Release(voice); }

            // 中文音色优先：多数人装的中文包名字里带 Chinese / 中文 / 拼音音色名
            list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            _voices = list;
            return list;

            static int Rank(string name)
                => name.Contains("Chinese", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("中文", StringComparison.Ordinal)
                   || name.Contains("Huihui", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Kangkang", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Yaoyao", StringComparison.OrdinalIgnoreCase)
                    ? 0 : 1;
        });
    }

    /// <summary>把正文切成朗读用的句子（按行 + 句末标点；过长再按逗号切，过短并回上一句）。</summary>
    public static IReadOnlyList<string> SplitSentences(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var buffer = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            foreach (var ch in line)
            {
                buffer.Append(ch);
                if (ch is '。' or '！' or '？' or '!' or '?' or '；' or ';' or '…') Flush();
            }

            buffer.Append(' ');
            Flush();
        }

        Flush();
        return result;

        void Flush()
        {
            var sentence = buffer.ToString().Trim().TrimStart('\u3000');
            buffer.Clear();
            if (sentence.Length == 0) return;

            // 太短（「他说。」）并进上一句，朗读才连贯
            if (sentence.Length < 8 && result.Count > 0)
            {
                result[^1] += sentence;
                return;
            }

            if (sentence.Length <= 90)
            {
                result.Add(sentence);
                return;
            }

            // 超长句按逗号再切（每块 ≤90 字），避免一次 Speak 太久、暂停失灵
            var chunk = new StringBuilder();
            foreach (var piece in sentence.Split('，', '、', ','))
            {
                if (chunk.Length > 0 && chunk.Length + piece.Length > 90)
                {
                    result.Add(chunk.ToString().Trim());
                    chunk.Clear();
                }

                chunk.Append(piece).Append('，');
            }

            if (chunk.Length > 0) result.Add(chunk.ToString().TrimEnd('，'));
        }
    }

    private void RunAsync(IReadOnlyList<string> sentences, int from, int rate, int volume, string? voiceName, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var voice = GetOrCreateVoice(out var reason);
            if (voice is null)
            {
                IsAvailable = false;
                UnavailableReason = reason;
                return;
            }

            // 🔴 同一个朗读对象不能两个线程同时 Speak（改语速会 Stop 后立刻 Start）⇒ 串行进这把锁；
            // 旧任务发现 token 取消会很快退出，新任务等一下即可
            lock (_speakGate)
            {
                UnavailableReason = "";
                ApplySettings(voice, rate, volume, voiceName);

                for (var i = from; i < sentences.Count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    while (_paused && !token.IsCancellationRequested) Thread.Sleep(120);
                    if (token.IsCancellationRequested) break;

                    _index = i;
                    SegmentStarted?.Invoke(this, new TtsProgress(i, sentences.Count));

                    Call(voice, "Speak", sentences[i], 0);
                }
            }
        }
        catch (Exception ex)
        {
            UnavailableReason = "朗读失败：" + ex.Message;
        }
        finally
        {
            // 只有「当前这一轮」才收尾：Stop 后立刻 Start 会起新任务，旧任务的 finally ⛔ 不能把新状态清掉
            if (ReferenceEquals(_cts, cts))
            {
                _speaking = false;
                _paused = false;
                Interlocked.CompareExchange(ref _cts, null, cts);
            }

            RaiseState();
        }
    }

    /// <summary>朗读对象跨句复用（同一线程创建、同一线程调用 ⇒ 不涉及跨 apartment 编排）。</summary>
    private object? GetOrCreateVoice(out string reason)
    {
        if (_voice is not null)
        {
            reason = "";
            return _voice;
        }

        _voice = CreateVoice(out reason);
        return _voice;
    }

    private static object? CreateVoice(out string reason)
    {
        reason = "";
        if (!OperatingSystem.IsWindows())
        {
            reason = "听书用 Windows 自带语音，当前系统不是 Windows。";
            return null;
        }

        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null)
            {
                reason = "系统没有注册语音合成（SAPI.SpVoice）。到「设置 → 时间和语言 → 语音」装一个语音包再试。";
                return null;
            }

            var voice = Activator.CreateInstance(type);
            if (voice is null) reason = "语音合成对象创建失败。";
            return voice;
        }
        catch (Exception ex)
        {
            reason = "语音合成不可用：" + ex.Message;
            return null;
        }
    }

    private static void ApplySettings(object voice, int rate, int volume, string? voiceName)
    {
        TrySet(voice, "Rate", Math.Clamp(rate, -10, 10));
        TrySet(voice, "Volume", Math.Clamp(volume, 0, 100));

        if (string.IsNullOrWhiteSpace(voiceName)) return;

        try
        {
            var tokens = Call(voice, "GetVoices", "", "")!;
            var count = Convert.ToInt32(Get(tokens, "Count"));
            for (var i = 0; i < count; i++)
            {
                var token = Call(tokens, "Item", i)!;
                var desc = Call(token, "GetDescription", 0) as string;
                if (!string.Equals(desc?.Trim(), voiceName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

                Call(voice, "SetVoice", token);
                return;
            }
        }
        catch { /* 音色名对不上 ⇒ 用系统默认音色 */ }
    }

    private static void TrySet(object target, string name, object value)
    {
        try { target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }); }
        catch { /* 个别引擎只读某些属性 */ }
    }

    private static object? Call(object target, string name, params object?[] args)
        => target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name)
        => target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void Release(object? comObject)
    {
        if (comObject is null || !OperatingSystem.IsWindows()) return;

        try
        {
            if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject);
        }
        catch { /* 已释放 */ }
    }

    private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Stop();
        Release(_voice);
        _voice = null;
        StateChanged = null;
        SegmentStarted = null;
    }
}
