using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>「全部资产」→ 音乐播放的两条出口： <list type="bullet"> 「播放」把选中的音乐按当前列表顺序交给「查看器 → 音乐播放」并立即开播（走 IModuleBus）。</summary>
public partial class AssetAllViewModel
{
    /// <summary>
    /// 可播音频扩展名 —— 🔴 与 <c>NewToys.Viewer.Controls.ViewerHost</c> 的音频扩展名集合**必须一致**：
    /// 这里放行的文件到了查看器被判成「不认识」，就会静默少歌。
    /// </summary>
    private static readonly HashSet<string> PlayableAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".opus", ".aiff"
    };

    /// <summary>选中行里的可播音频（保持顺序、去重、跳过缺路径的）。</summary>
    private List<AssetRow> PickedAudioRows()
        => BatchRows()
            .Where(r => !string.IsNullOrWhiteSpace(r.FilePath))
            .Where(r => PlayableAudioExtensions.Contains(Path.GetExtension(r.FilePath)))
            .DistinctBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>「播放」：选中音乐 → 查看器音乐播放（顺序 = 当前列表顺序）。</summary>
    [RelayCommand]
    private void PlaySelectedInPlayer()
    {
        var rows = BatchRows();
        if (rows.Count == 0) { StatusText = "先选中要播的音乐（Ctrl / Shift 可多选）。"; return; }

        var tracks = PickedAudioRows();
        if (tracks.Count == 0)
        {
            StatusText = "选中的里没有可播的音频文件（支持 mp3 / flac / wav / m4a / aac / wma / ogg / opus / aiff）。";
            return;
        }

        _bus?.Post(ModuleTopics.AudioPlay, (IReadOnlyList<string>)tracks.Select(t => t.FilePath).ToList());
        _navStore?.NavigateTo("viewer-audio");

        var skipped = rows.Count - tracks.Count;
        StatusText = skipped > 0
            ? $"已把 {tracks.Count} 首交给「音乐播放」（另有 {skipped} 个非音频已跳过）。"
            : $"已把 {tracks.Count} 首交给「音乐播放」。";
    }

    /// <summary>「🎵 导出播放列表」：写 .m3u8（默认相对路径，换机器 / 挪盘仍可播）。</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExportPlaylistAsync()
    {
        var tracks = PickedAudioRows();
        if (tracks.Count == 0) { StatusText = "没有可导出的音频：先选中音乐行（支持 mp3 / flac / wav / m4a …）。"; return; }

        var path = await DialogHooks.SaveFileAsync("导出播放列表", ImportExportDefaultDir, "播放列表.m3u8",
            "M3U8 播放列表（*.m3u8）|*.m3u8|M3U 播放列表（*.m3u）|*.m3u").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) { StatusText = "已取消。"; return; }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            var relative = _io?.Settings.PlaylistRelativePaths ?? true;

            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var row in tracks)
            {
                var title = string.IsNullOrWhiteSpace(row.DisplayName) || row.DisplayName == row.Name
                    ? (string.IsNullOrWhiteSpace(row.Title) ? Path.GetFileNameWithoutExtension(row.Name) : row.Title)
                    : row.DisplayName!;
                var seconds = (int)Math.Round(row.DurationSeconds);
                sb.Append("#EXTINF:").Append(seconds < 0 ? 0 : seconds).Append(',');
                sb.AppendLine(title.Replace('\r', ' ').Replace('\n', ' '));
                sb.AppendLine(TrackPath(row.FilePath, dir, relative));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            StatusText = $"已导出 {tracks.Count} 首到：{path}（{(relative ? "相对路径" : "绝对路径")}）";
        }
        catch (Exception ex)
        {
            StatusText = "导出播放列表失败：" + ex.Message;
        }
    }

    private static string TrackPath(string filePath, string playlistDir, bool relative)
    {
        if (!relative || string.IsNullOrWhiteSpace(playlistDir)) return filePath;
        try
        {
            var rel = Path.GetRelativePath(playlistDir, filePath);
            return rel.StartsWith("..", StringComparison.Ordinal) ? filePath : rel;
        }
        catch { return filePath; }
    }
}
