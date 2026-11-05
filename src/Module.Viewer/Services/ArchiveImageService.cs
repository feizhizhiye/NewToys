using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SharpCompress.Readers;

namespace NewToys.Viewer.Services;

/// <summary>压缩包里的一张图（只含定位信息，不解码）。</summary>
public sealed class ArchiveImageEntry
{
    /// <summary>包内相对路径（唯一键，分隔符统一为 <c>/</c>）。</summary>
    public string Key { get; init; } = "";

    /// <summary>显示用文件名。</summary>
    public string Name { get; init; } = "";

    /// <summary>未压缩大小（字节）。</summary>
    public long Size { get; init; }

    public override string ToString() => Name;
}

/// <summary>一次打开的压缩包图片会话：条目列表与取图共用同一份已打开的归档。（WHY: VW-010）</summary>
public sealed class ArchiveImageSession : IDisposable
{
    /// <summary>顺序格式（rar / 7z）取图结果的缓存字节上限：超过就整清（solid 包顺序翻页时避免反复从头解压）。</summary>
    private const long SequentialCacheLimitBytes = 512L * 1024 * 1024;

    private readonly string _path;
    private readonly ZipArchive? _zip;
    private readonly Dictionary<string, ZipArchiveEntry>? _zipMap;

    /// <summary>顺序格式是否固实（solid）压缩：固实包取第 N 张必须从头解压到 N，缓存沿途结果才有收益。</summary>
    private readonly bool _isSolid;

    /// <summary>顺序格式取图结果的 LRU 缓存（仅 solid 包启用）：键 = 条目 Key，值 = 解压出的原始字节。</summary>
    private readonly Dictionary<string, byte[]> _seqCache = new();
    private long _seqCacheBytes;

    /// <summary>串行化「从包里取一张图」。ZipArchive 的条目流共享底层流，并发读取会错位；顺序格式则要反复扫包。</summary>
    private readonly object _openGate = new();

    internal ArchiveImageSession(string path, ZipArchive? zip, Dictionary<string, ZipArchiveEntry>? zipMap,
        IReadOnlyList<ArchiveImageEntry> entries, bool isSolid = false)
    {
        _path = path;
        _zip = zip;
        _zipMap = zipMap;
        _isSolid = isSolid;
        Entries = entries;
    }

    /// <summary>压缩包路径。</summary>
    public string Path => _path;

    /// <summary>包内图片条目（按路径名排序）。</summary>
    public IReadOnlyList<ArchiveImageEntry> Entries { get; }

    /// <summary>读出包内某张图到内存流；失败返回 <c>null</c> 并给出原因。</summary>
    public Stream? Open(string key, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(key)) { error = "条目名为空。"; return null; }

        try
        {
            // 分屏各格并行取图 ⇒ 这里必须串行（只串读流，解码仍在调用方线程外并行）。
            lock (_openGate)
            {
                if (_zipMap is not null)
                {
                    if (!_zipMap.TryGetValue(key, out var entry)) { error = "压缩包里找不到：" + key; return null; }
                    using var raw = entry.Open();
                    return CopyToMemory(raw, entry.Length);
                }

                // solid 包：先查沿途缓存（顺序翻页时，前面已解出的图直接命中，避免从头再解）。
                if (_isSolid && _seqCache.TryGetValue(key, out var cached))
                {
                    return new MemoryStream(cached, writable: false);
                }

                return ReadSequential(key, out error);
            }
        }
        catch (Exception ex)
        {
            error = "解出图片失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>顺序格式（rar / 7z）取图：每次从头扫到该条目（格式所限，单包几十到几百张可接受）。</summary>
    private Stream? ReadSequential(string key, out string error)
    {
        error = "";
        try
        {
            using var stream = File.OpenRead(_path);
            using var reader = ReaderFactory.Open(stream);

            // solid 包：从头解到目标这一路上的图片条目全部缓存（解压代价已付出，结果别浪费），
            // 顺序翻页时下一张直接命中缓存。非 solid 包 MoveToNextEntry 是纯 seek 跳过，无需缓存。
            var cacheAlong = _isSolid;

            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;

                var normalized = Normalize(entry.Key);
                var isImage = ArchiveImageService.IsImageEntry(normalized);
                var isTarget = string.Equals(normalized, key, StringComparison.Ordinal);

                if (!isImage && !isTarget) continue;   // 既不是图片、也不是目标（防御）→ 跳过

                using var source = reader.OpenEntryStream();
                var buffer = CopyToBytes(source, entry.Size);

                if (isTarget) return new MemoryStream(buffer, writable: false);

                if (cacheAlong && isImage) CacheSequential(normalized, buffer);
            }

            error = "压缩包里找不到：" + key;
            return null;
        }
        catch (Exception ex)
        {
            error = "解出图片失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>把解压出的图片字节塞进 LRU 缓存；超上限时整清（solid 包翻页场景，清空重来成本可接受）。</summary>
    private void CacheSequential(string key, byte[] buffer)
    {
        _seqCache[key] = buffer;
        _seqCacheBytes += buffer.LongLength;
        if (_seqCacheBytes > SequentialCacheLimitBytes)
        {
            _seqCache.Clear();
            _seqCacheBytes = 0;
        }
    }

    private static byte[] CopyToBytes(Stream source, long hint)
    {
        using var ms = CopyToMemory(source, hint);
        return ms.ToArray();
    }

    private static MemoryStream CopyToMemory(Stream source, long hint)
    {
        var capacity = hint > 0 && hint < int.MaxValue ? (int)hint : 0;
        var buffer = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }

    internal static string Normalize(string? key) => (key ?? "").Replace('\\', '/');

    public void Dispose()
    {
        // ZipArchive(..., leaveOpen: false) 会连带关掉文件流
        _zip?.Dispose();
    }
}

/// <summary>压缩包内图片读取（zip / rar / 7z / cbz / cbr）。（WHY: VW-011）</summary>
public sealed class ArchiveImageService
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".jfif", ".avif",
    };

    /// <summary>是否包内图片条目。</summary>
    public static bool IsImageEntry(string keyOrPath)
    {
        var ext = Path.GetExtension(keyOrPath ?? "").ToLowerInvariant();
        return Array.IndexOf(ImageExtensions, ext) >= 0;
    }

    /// <summary>打开压缩包并列出图片；失败返回 <c>null</c> 并给出原因。</summary>
    public ArchiveImageSession? OpenSession(string archivePath, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(archivePath)) { error = "路径为空。"; return null; }
        if (!File.Exists(archivePath)) { error = "文件不存在：" + archivePath; return null; }

        var zipSession = TryOpenZip(archivePath, out var zipError);
        if (zipSession is not null) return zipSession;

        var sequential = TryListSequential(archivePath, out var sequentialError);
        if (sequential is not null) return sequential;

        error = string.IsNullOrWhiteSpace(zipError)
            ? sequentialError
            : "读取压缩包失败：" + zipError + "（加密包 / 分卷包暂不支持）";
        return null;
    }

    private static ArchiveImageSession? TryOpenZip(string path, out string error)
    {
        error = "";
        try
        {
            using var probe = File.OpenRead(path);

            // 先用最小流探测魔数：非 zip 直接放弃，避免在 2 GB 包上白读尾部
            Span<byte> magic = stackalloc byte[2];
            if (probe.Read(magic) < 2 || magic[0] != 0x50 || magic[1] != 0x4B) return null;

            var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read, leaveOpen: false);
            try
            {
                var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                var entries = new List<ArchiveImageEntry>();

                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;   // 目录项
                    var key = ArchiveImageSession.Normalize(entry.FullName);
                    if (!IsImageEntry(key)) continue;

                    map[key] = entry;
                    entries.Add(new ArchiveImageEntry { Key = key, Name = Path.GetFileName(key), Size = entry.Length });
                }

                entries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                return new ArchiveImageSession(path, zip, map, entries);
            }
            catch
            {
                zip.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static ArchiveImageSession? TryListSequential(string path, out string error)
    {
        error = "";
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = ReaderFactory.Open(stream);

            var entries = new List<ArchiveImageEntry>();
            var isSolid = false;
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;

                var key = ArchiveImageSession.Normalize(entry.Key);
                if (string.IsNullOrWhiteSpace(key) || !IsImageEntry(key)) continue;

                if (!isSolid) isSolid = entry.IsSolid;   // 包是否固实（首个图片条目即可判定）
                entries.Add(new ArchiveImageEntry { Key = key, Name = Path.GetFileName(key), Size = entry.Size });
            }

            entries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return new ArchiveImageSession(path, zip: null, zipMap: null, entries, isSolid);
        }
        catch (Exception ex)
        {
            error = "读取压缩包失败：" + ex.Message + "（加密包 / 分卷包暂不支持）";
            return null;
        }
    }
}
