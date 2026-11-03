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

/// <summary>
/// 一次打开的压缩包图片会话：条目列表与取图共用同一份已打开的归档。
/// <para>
/// 🔴 zip / cbz 走 <see cref="ZipArchive"/>：只读中央目录，不解压任何数据，2 GB 包也能秒开；
/// 取单张按条目名直接定位（O(1)）。rar / 7z / cbr 没有等价的中央目录 API，退回 SharpCompress 顺序扫。
/// </para>
/// <para>
/// 🔴 会话持有文件句柄：宿主换文件 / 关闭页面时必须 <see cref="Dispose"/>，⛔ 不要跨打开缓存条目对象。
/// </para>
/// </summary>
public sealed class ArchiveImageSession : IDisposable
{
    private readonly string _path;
    private readonly ZipArchive? _zip;
    private readonly Dictionary<string, ZipArchiveEntry>? _zipMap;

    /// <summary>串行化「从包里取一张图」。ZipArchive 的条目流共享底层流，并发读取会错位；顺序格式则要反复扫包。</summary>
    private readonly object _openGate = new();

    internal ArchiveImageSession(string path, ZipArchive? zip, Dictionary<string, ZipArchiveEntry>? zipMap,
        IReadOnlyList<ArchiveImageEntry> entries)
    {
        _path = path;
        _zip = zip;
        _zipMap = zipMap;
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
            using var reader = ReaderFactory.OpenReader(stream);

            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;
                if (!string.Equals(Normalize(entry.Key), key, StringComparison.Ordinal)) continue;

                using var source = reader.OpenEntryStream();
                return CopyToMemory(source, entry.Size);
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

/// <summary>
/// 压缩包内图片读取（zip / rar / 7z / cbz / cbr）。
/// <para>
/// 🔴 只读、只列图片；⛔ 不解压到磁盘、⛔ 不支持加密包（遇到给出明确原因）。
/// </para>
/// <para>
/// 🔴 打开开销决定体验：<see cref="ZipArchive"/> 读中央目录（zip 家族），其余格式用 SharpCompress 顺序扫。
/// 返回的 <see cref="ArchiveImageSession"/> 必须由调用方持有并释放，⛔ 不要每翻一张重开归档。
/// </para>
/// </summary>
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
            using var reader = ReaderFactory.OpenReader(stream);

            var entries = new List<ArchiveImageEntry>();
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;

                var key = ArchiveImageSession.Normalize(entry.Key);
                if (string.IsNullOrWhiteSpace(key) || !IsImageEntry(key)) continue;

                entries.Add(new ArchiveImageEntry { Key = key, Name = Path.GetFileName(key), Size = entry.Size });
            }

            entries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return new ArchiveImageSession(path, zip: null, zipMap: null, entries);
        }
        catch (Exception ex)
        {
            error = "读取压缩包失败：" + ex.Message + "（加密包 / 分卷包暂不支持）";
            return null;
        }
    }
}
