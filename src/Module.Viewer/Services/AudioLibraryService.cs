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
}
