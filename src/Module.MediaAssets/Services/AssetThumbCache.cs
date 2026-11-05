using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace NewToys.MediaAssets.Services;

/// <summary>「资产浏览」的缩略图缓存：只在浏览时读缓存目录（⛔ 不在浏览过程中现场生成，生成走工具条的显式命令）。（WHY: MA-032）</summary>
public class AssetThumbCache
{
    private const int MaxEntries = 300;
    private const int MaxConcurrentDecodes = 4;

    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _lru = new();
    private readonly SemaphoreSlim _gate = new(MaxConcurrentDecodes, MaxConcurrentDecodes);
    private readonly object _sync = new();

    private readonly record struct Entry(string Key, Bitmap Bitmap);

    /// <summary>缓存里的条目数（诊断 / 状态栏用）。</summary>
    public int Count { get { lock (_sync) return _index.Count; } }

    /// <summary>
    /// 取一张缩略图（缓存命中直接返回；未命中 → 后台解码并纳入 LRU）。
    /// 文件不存在 / 解码失败 → null（⛔ 不抛、⛔ 不弹窗）。
    /// </summary>
    public async Task<Bitmap?> GetAsync(string thumbPath, int decodeWidth, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(thumbPath)) return null;
        var key = thumbPath + "|" + decodeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);

        lock (_sync)
        {
            if (_index.TryGetValue(key, out var hit))
            {
                if (!ReferenceEquals(_lru.First, hit)) { _lru.Remove(hit); _lru.AddFirst(hit); }
                return hit.Value.Bitmap;
            }
        }

        try { await _gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        catch (ObjectDisposedException) { return null; }

        try
        {
            if (!File.Exists(thumbPath)) return null;

            var bmp = await Task.Run(() =>
            {
                using var fs = File.OpenRead(thumbPath);
                return Bitmap.DecodeToWidth(fs, decodeWidth);
            }, ct).ConfigureAwait(false);
            if (bmp == null) return null;

            lock (_sync)
            {
                if (_index.TryGetValue(key, out var again))
                {
                    if (!ReferenceEquals(_lru.First, again)) { _lru.Remove(again); _lru.AddFirst(again); }
                    return again.Value.Bitmap;
                }

                var node = _lru.AddFirst(new Entry(key, bmp));
                _index[key] = node;

                while (_index.Count > MaxEntries && _lru.Last is { } tail)
                {
                    _lru.RemoveLast();
                    _index.Remove(tail.Value.Key);
                }
            }
            return bmp;
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }   // 损坏的 jpg / 文件被占 / 解码失败：占位到底
        finally { _gate.Release(); }
    }

    /// <summary>清空引用（切目录时释放当前页以外的位图引用；界面解除绑定后由 GC 回收）。</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _index.Clear();
            _lru.Clear();
        }
    }
}
