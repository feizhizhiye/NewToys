using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Readers;

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
/// 🔴 走 <see cref="ReaderFactory"/>（顺序读取）而不是 ArchiveFactory：前者在所有版本里都稳定可用，
/// 且能按内容自动识别格式（不靠扩展名）。代价是「取第 N 张」要重扫一遍包头 —— 图片包里只有几十到几百条，可忽略。
/// </para>
/// <para>
/// 🔴 条目流依赖 reader 存活：一律读进 <see cref="MemoryStream"/> 再关 reader，⛔ 不要把流交给上层长期持有。
/// </para>
/// </summary>
public sealed class ArchiveImageService
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".jfif", ".avif",
    };

    /// <summary>列出包内图片（按名称排序）；失败返回空并给出原因。</summary>
    public IReadOnlyList<ArchiveImageEntry> List(string archivePath, out string error)
    {
        error = "";
        var result = new List<ArchiveImageEntry>();

        try
        {
            using var stream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.OpenReader(stream);

            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;

                var key = entry.Key;
                if (string.IsNullOrWhiteSpace(key)) continue;

                var ext = Path.GetExtension(key).ToLowerInvariant();
                if (Array.IndexOf(ImageExtensions, ext) < 0) continue;

                result.Add(new ArchiveImageEntry
                {
                    Key = key,
                    Name = Path.GetFileName(key.Replace('\\', '/')),
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
            using var stream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.OpenReader(stream);

            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory) continue;
                if (!string.Equals(entry.Key, key, StringComparison.Ordinal)) continue;

                using var source = reader.OpenEntryStream();
                var buffer = new MemoryStream();
                source.CopyTo(buffer);
                buffer.Position = 0;
                return buffer;
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
}
