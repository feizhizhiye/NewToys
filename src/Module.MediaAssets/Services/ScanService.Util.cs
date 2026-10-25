using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

public partial class ScanService
{

    private void FlushTouched(List<long> touched, string seenAt)
    {
        if (touched.Count == 0) return;
        _assets.TouchLocations(touched, seenAt);
        touched.Clear();
    }

    /// <summary>把一个存储源的现有位置**一次**读进内存索引（⛔ 不 per-file 查库）。</summary>
    private StorageIndex LoadStorageIndex(long storageId)
    {
        var index = new StorageIndex();
        if (!_assets.IsAvailable) return index;
        foreach (var l in _assets.ListStorageLocations(storageId)) index.Add(l);
        return index;
    }

    // ==================== 内部：遍历 / 忽略 / 哈希 ====================

    /// <summary>
    /// 手写遍历（⛔ 不用 <c>Directory.EnumerateFiles(..., AllDirectories)</c>：它遇到**一个**没权限的
    /// 子目录就整体抛异常，整次扫描会断）。这里逐目录 try-catch：读不了的目录跳过，其余照常。
    /// <para>
    /// ⚠️ 用 <c>EnumerateFileSystemEntries</c> 一次拿**全部条目**再自己分流目录 / 文件：
    /// 比 <c>GetDirectories</c> + <c>GetFiles</c> 少一半系统调用（大目录上很明显）。
    /// </para>
    /// </summary>
    private IEnumerable<string> EnumerateFiles(string root, CancellationToken ct, bool recursive = true)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch { continue; }                       // 目录读不了 → 跳过

            foreach (var entry in entries)
            {
                var isDir = false;
                try
                {
                    var attrs = File.GetAttributes(entry);
                    // 隐藏 / 系统目录跳过（回收站、卷影等）；⛔ 不停在这类目录上浪费时间
                    if (attrs.HasFlag(FileAttributes.System)) continue;
                    if (IgnoreHiddenFiles && attrs.HasFlag(FileAttributes.Hidden)) continue;
                    isDir = attrs.HasFlag(FileAttributes.Directory);
                }
                catch { continue; }

                if (isDir)
                {
                    if (IgnoreDirectoryNames.Contains(Path.GetFileName(entry))) continue;
                    if (recursive) stack.Push(entry);   // v3.29：关掉「含子目录」就只取当前这一层
                }
                else
                {
                    if (IgnoreExtensions.Contains(Path.GetExtension(entry))) continue;
                    // Office 等程序的临时副本（~$开头）—— 扩展名管不到它
                    if (IgnoreTempFiles && Path.GetFileName(entry).StartsWith("~$", StringComparison.Ordinal)) continue;
                    yield return entry;
                }
            }
        }
    }

    /// <summary>相对路径（存储源根之下）；根就是它自己时回退文件名。</summary>
    private static string MakeRelative(string root, string fullPath)
    {
        try
        {
            var rel = Path.GetRelativePath(root, fullPath);
            return string.IsNullOrEmpty(rel) ? Path.GetFileName(fullPath) : rel;
        }
        catch { return Path.GetFileName(fullPath); }
    }

    /// <summary>
    /// quick_hash = **前 N MB 采样** + 文件长度 + 最后写入时间（UTC ticks）的 SHA1。
    /// 采样缓冲与 SHA1 实例**复用**（字段）：整库扫描动辄几万文件，每文件 new 一个 1MB 数组
    /// 会给 GC 添无谓压力。⚠️ 因此**只在单线程扫描里用**（扫描已串在 <c>Task.Run</c> 里）。
    /// </summary>
    private string ComputeQuickHash(string path, long size, long mtimeUtcTicks)
    {
        try
        {
            _sha1.Initialize();
            var wanted = (int)Math.Min(size, SampleBytes);
            if (wanted > 0)
            {
                EnsureSampleBuffer();
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, SampleBytes, FileOptions.SequentialScan);
                var read = 0;
                while (read < wanted)
                {
                    var n = fs.Read(_sampleBuffer!, read, wanted - read);
                    if (n <= 0) break;
                    read += n;
                }
                _sha1.TransformBlock(_sampleBuffer!, 0, read, null, 0);
            }
            var tail = System.Text.Encoding.UTF8.GetBytes($":{size}:{mtimeUtcTicks}");
            _sha1.TransformFinalBlock(tail, 0, tail.Length);
            return "q:" + Convert.ToHexString(_sha1.Hash!).ToLowerInvariant();
        }
        catch { _sha1.Initialize(); return ""; }
    }

    /// <summary>采样字节数 = <see cref="QuickHashSampleMb"/> MB（配置改了下一次扫描生效）。</summary>
    private int SampleBytes => Math.Max(0, QuickHashSampleMb) * 1024 * 1024;

    private void EnsureSampleBuffer()
    {
        var need = SampleBytes;
        if (_sampleBuffer == null || _sampleBuffer.Length < need)
            _sampleBuffer = need == 0 ? Array.Empty<byte>() : new byte[need];
    }

    /// <summary>复用的采样缓冲 / SHA1（⛔ 单线程专用，见 <see cref="ComputeQuickHash"/> 注释）。</summary>
    private byte[]? _sampleBuffer;
    private readonly SHA1 _sha1 = SHA1.Create();

    /// <summary>完整内容哈希（SHA1，前缀 <c>sha1:</c>）：只有显式打开 <see cref="ComputeFullHashForNewFiles"/> 才用。</summary>
    private static string ComputeFullHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA1.Create();
            return "sha1:" + Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        catch { return ""; }
    }

    /// <summary>按扩展名猜资产类型（拿不准归 other；用户可在库内改）。</summary>
    public static string GuessAssetType(string? ext) => (ext ?? "").ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".ts" or ".mpg" or ".mpeg" or ".rmvb" => AssetTypes.Video,
        ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a" or ".ogg" or ".wma" or ".ape" or ".opus" => AssetTypes.Music,
        ".epub" or ".mobi" or ".azw3" or ".pdf" or ".djvu" or ".cbz" or ".cbr" or ".txt" or ".md" => AssetTypes.Book,
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".svg" or ".heic" => AssetTypes.Image,
        ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".odt" or ".ods" or ".rtf" => AssetTypes.Document,
        _ => AssetTypes.Other
    };

    /// <summary>按扩展名给个粗 MIME（够列表显示；拿不准留空）。</summary>
    public static string? GuessMimeType(string? ext) => (ext ?? "").ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4", ".mkv" => "video/x-matroska", ".avi" => "video/x-msvideo", ".webm" => "video/webm",
        ".mp3" => "audio/mpeg", ".flac" => "audio/flac", ".wav" => "audio/wav", ".m4a" => "audio/mp4",
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp",
        ".pdf" => "application/pdf", ".epub" => "application/epub+zip",
        _ => null
    };

}
