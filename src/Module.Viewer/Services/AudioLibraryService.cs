using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.UI.Viewers;

namespace NewToys.Viewer.Services;

/// <summary>
/// 音频标签读取（标题 / 艺术家 / 专辑 / 时长 / 封面）。
/// <para>
/// 🔴 只读标签，⛔ 不回写文件 —— 标签编辑属于「媒体资产」的活，查看器只看不改。
/// </para>
/// <para>🔴 读不到标签不算失败：退回文件名 + 时长 0，播放照常。</para>
/// </summary>
public sealed class AudioLibraryService
{
    /// <summary>把一个音频文件读成播放列表条目。</summary>
    public AudioTrackItem Read(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        try
        {
            using var file = TagLib.File.Create(path);

            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? name : file.Tag.Title;
            var artist = file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist ?? "";
            var album = file.Tag.Album ?? "";

            byte[]? cover = null;
            var pictures = file.Tag.Pictures;
            if (pictures is { Length: > 0 })
            {
                var data = pictures[0].Data?.Data;
                if (data is { Length: > 0 }) cover = data;
            }

            return new AudioTrackItem
            {
                Path = path,
                Title = title,
                Artist = artist,
                Album = album,
                Duration = file.Properties?.Duration ?? TimeSpan.Zero,
                Cover = cover,
            };
        }
        catch
        {
            return new AudioTrackItem
            {
                Path = path,
                Title = name,
                Artist = "",
                Album = "",
                Duration = TimeSpan.Zero,
                Cover = null,
            };
        }
    }

    /// <summary>批量读（拖入整个文件夹 / 多选文件）。</summary>
    public IReadOnlyList<AudioTrackItem> ReadMany(IEnumerable<string> paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Read).ToList();

    /// <summary>读同名 LRC 歌词文本（没有返回 <c>null</c>）。</summary>
    public string? ReadLyrics(string audioPath)
    {
        try
        {
            var lrc = Path.ChangeExtension(audioPath, ".lrc");
            return File.Exists(lrc) ? File.ReadAllText(lrc) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 读同名**译文**歌词：优先 <c>歌曲.zh.lrc</c>，其次 <c>歌曲.trans.lrc</c>、<c>歌曲.translation.lrc</c>。
    /// <para>译文按时间戳与原文对齐（由播放器合并），没有就返回 <c>null</c> —— 单语歌词照常显示。</para>
    /// </summary>
    public string? ReadTranslation(string audioPath)
    {
        var baseName = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? "",
            Path.GetFileNameWithoutExtension(audioPath));

        foreach (var suffix in TranslationSuffixes)
        {
            try
            {
                var path = baseName + suffix;
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch { /* 译文是附属产物，读不到就当没有 */ }
        }
        return null;
    }

    private static readonly string[] TranslationSuffixes = { ".zh.lrc", ".trans.lrc", ".translation.lrc", ".cn.lrc" };

    /// <summary>播放列表扩展名（m3u / m3u8）。</summary>
    public static bool IsPlaylist(string path)
    {
        var ext = Path.GetExtension(path ?? "");
        return ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 m3u / m3u8：返回其中**磁盘上确实存在**的音频路径（顺序 = 文件里的顺序）。
    /// <para>相对路径按播放列表所在目录解析；<c>#EXTINF</c> / 注释行跳过；去重。</para>
    /// </summary>
    public IReadOnlyList<string> ReadPlaylist(string playlistPath)
    {
        var result = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(playlistPath)) ?? "";
            foreach (var raw in File.ReadAllLines(playlistPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                var path = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(dir, line));
                if (!File.Exists(path)) continue;
                if (!ViewerHost.IsAudio(path)) continue;
                if (result.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(path);
            }
        }
        catch { /* 坏列表当空处理，界面提示「列表里没有可播的音频」 */ }
        return result;
    }
}
