using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace NewToys.Viewer.Services;

/// <summary>压缩包里的一张图（只含定位信息，不解码）。</summary>
public sealed class ArchiveImageEntry
{
    /// <summary>包内相对路径（唯一键）。</summary>
    public string Key { get; init; } = "";

    /// <summary>显示用文件名。</summary>
    public string Name { get; init; } = "";

    /// <summary>未压缩大小（字节）。</summary>
    public long Size { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// 压缩包内图片读取（zip / rar / 7z / cbz / cbr）。
/// <para>
/// 🔴 只读、只列图片；⛔ 不解压到磁盘、⛔ 不支持加密包（遇到给出明确原因）。
/// </para>
/// <para>
/// 🔴 归档对象每次操作后立刻释放：SharpCompress 的条目流依赖归档存活，跨操作持有会把整个包句柄卡住。
/// 图片本身读进内存（普通图片几 MB），换图时旧对象自然回收。
/// </para>
/// </summary>
public sealed class ArchiveImageService
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".jfif", ".avif",
    };

    /// <summary>列出包内图片（按名称自然排序）；失败返回空并给出原因。</summary>
    public IReadOnlyList<ArchiveImageEntry> List(string archivePath, out string error)
    {
        error = "";
        var result = new List<ArchiveImageEntry>();

        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Key)) continue;

                var ext = Path.GetExtension(entry.Key).ToLowerInvariant();
                if (Array.IndexOf(ImageExtensions, ext) < 0) continue;

                result.Add(new ArchiveImageEntry
                {
                    Key = entry.Key,
                    Name = Path.GetFileName(entry.Key.Replace('\\', '/')),
                    Size = entry.Size,
                });
            }
        }
        catch (Exception ex)
        {
            error = "读取压缩包失败：" + ex.Message + "（加密包 / 分卷包暂不支持）";
            return Array.Empty<ArchiveImageEntry>();
        }

        return result
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>读出包内某张图到内存流；失败返回 <c>null</c> 并给出原因。</summary>
    public Stream? Open(string archivePath, string key, out string error)
    {
        error = "";
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entry = archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory && string.Equals(e.Key, key, StringComparison.Ordinal));

            if (entry is null)
            {
                error = "压缩包里找不到：" + key;
                return null;
            }

            using var source = entry.OpenEntryStream();
            var buffer = new MemoryStream();
            source.CopyTo(buffer);
            buffer.Position = 0;
            return buffer;
        }
        catch (Exception ex)
        {
            error = "解出图片失败：" + ex.Message;
            return null;
        }
    }
}
