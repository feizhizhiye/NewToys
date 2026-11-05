using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>「全部资产」详情卡里的音乐属性：标题 / 艺术家 / 专辑 / 年份 / 流派 + 封面。（WHY: MA-062）</summary>
public partial class AssetAllViewModel
{
    private readonly MusicTagService? _musicTags;
    private CancellationTokenSource? _musicCts;

    [ObservableProperty] private string _editMusicTitle = "";
    [ObservableProperty] private string _editMusicArtist = "";
    [ObservableProperty] private string _editMusicAlbum = "";
    [ObservableProperty] private string _editMusicYear = "";
    [ObservableProperty] private string _editMusicGenre = "";
    [ObservableProperty] private string _musicStatusText = "";

    /// <summary>封面文件感知（只读）：外挂图按「同名 → 宽松同名 → 固定名 → 上一级固定名」找；找不到给「未找到」文案。</summary>
    [ObservableProperty] private string _musicCoverInfo = "";

    /// <summary>歌词文件感知（只读）：同名 .lrc → 标题组合 .lrc → 译文 .lrc。</summary>
    [ObservableProperty] private string _musicLyricsInfo = "";

    /// <summary>联网查回来的封面（点保存才写进文件）。</summary>
    private byte[]? _pendingMusicCover;

    /// <summary>
    /// 音乐专属字段是否显示：筛选类型 = 音乐，**或**当前选中的就是音乐资产
    /// （「全部类型」下点中一首歌也能直接改标签，不必先切筛选）。
    /// </summary>
    public bool IsMusicType => HasMusicSelection
        || string.Equals(FilterType?.Value, AssetTypes.Music, StringComparison.Ordinal);

    /// <summary>选中的是音乐资产（详情卡字段可用）。</summary>
    public bool HasMusicSelection => MusicRow != null;

    private AssetRow? MusicRow
        => Selected is { } row && string.Equals(row.Type, AssetTypes.Music, StringComparison.OrdinalIgnoreCase)
            ? row
            : null;

    /// <summary>把选中行的标签读进编辑框（选中变化时调；读不到就清空 + 说明原因）。</summary>
    private void LoadMusicTags()
    {
        EditMusicTitle = "";
        EditMusicArtist = "";
        EditMusicAlbum = "";
        EditMusicYear = "";
        EditMusicGenre = "";
        MusicStatusText = "";
        MusicCoverInfo = "";
        MusicLyricsInfo = "";
        _pendingMusicCover = null;

        var row = MusicRow;
        if (row == null) return;
        if (_musicTags == null) { MusicStatusText = "标签服务不可用。"; return; }

        var path = row.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MusicStatusText = "文件不在磁盘上（先「🔎 校验文件」）。";
            return;
        }

        if (!_musicTags.TryRead(path, out var tags, out var error))
        {
            MusicStatusText = error;
            return;
        }

        EditMusicTitle = tags.Title;
        EditMusicArtist = tags.Artist;
        EditMusicAlbum = tags.Album;
        EditMusicYear = tags.Year;
        EditMusicGenre = tags.Genre;
        _pendingMusicCover = tags.Cover;

        // ===== 封面 / 歌词文件感知（只读，不碰文件内容）——匹配规则与查看器播放器一致 =====
        try
        {
            var dir = Path.GetDirectoryName(path);
            var cover = MusicFileProbe.FindCover(path);
            if (cover is null)
            {
                MusicCoverInfo = "未找到（无同名 / 固定名外挂图片）";
            }
            else
            {
                var inParent = !string.IsNullOrEmpty(dir)
                    && !string.Equals(Path.GetDirectoryName(cover), dir, StringComparison.OrdinalIgnoreCase);
                MusicCoverInfo = Path.GetFileName(cover) + (inParent ? "（上级目录）" : "");
            }

            var (lrc, lrcTrans) = MusicFileProbe.FindLyrics(path, tags.Title, tags.Artist);
            MusicLyricsInfo = lrc is null
                ? "未找到（无同名 / 标题 .lrc）"
                : Path.GetFileName(lrc) + (lrcTrans is null ? "" : " + " + Path.GetFileName(lrcTrans));
        }
        catch { MusicCoverInfo = "无法读取目录"; MusicLyricsInfo = "无法读取目录"; }

        MusicStatusText = tags.Cover is { Length: > 0 } ? "已读取标签（含封面）。" : "已读取标签（无封面）。";
    }

    /// <summary>「写入文件」：把编辑框内容（含联网取到的封面）写回音乐文件的标签。</summary>
    [RelayCommand]
    private void SaveMusicTags()
    {
        var row = MusicRow;
        if (row == null) { StatusText = "先选中一条音乐资产。"; return; }
        if (_musicTags == null) { StatusText = "标签服务不可用。"; return; }

        var path = row.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { StatusText = "文件不在磁盘上，写不了标签。"; return; }

        var tags = new MusicTagService.MusicTags
        {
            Title = EditMusicTitle ?? "",
            Artist = EditMusicArtist ?? "",
            Album = EditMusicAlbum ?? "",
            Year = EditMusicYear ?? "",
            Genre = EditMusicGenre ?? "",
            Cover = _pendingMusicCover,
        };

        if (!_musicTags.TryWrite(path, tags, out var error))
        {
            MusicStatusText = error;
            StatusText = error;
            return;
        }

        _opLog.Log("asset", "asset", row.Id, afterJson: "{\"musicTags\":\"write\"}");
        MusicStatusText = "已写入文件标签。";
        StatusText = $"已把标签写进《{row.Name}》（⛔ 音频数据未动）。";
    }

    /// <summary>「🌐 联网补全」：按标题 + 艺术家查 MusicBrainz，结果先填进编辑框。</summary>
    [RelayCommand]
    private async Task FetchMusicOnlineAsync()
    {
        var row = MusicRow;
        if (row == null) { StatusText = "先选中一条音乐资产。"; return; }
        if (_musicTags == null) { StatusText = "标签服务不可用。"; return; }

        var title = (EditMusicTitle ?? "").Trim();
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(row.Name);

        _musicCts?.Cancel();
        _musicCts = new CancellationTokenSource();
        MusicStatusText = "正在联网查询…";
        StatusText = "正在联网查询" + title;

        var (tags, message) = await _musicTags.FetchOnlineAsync(title, EditMusicArtist, _musicCts.Token)
            .ConfigureAwait(true);

        if (tags == null)
        {
            MusicStatusText = message;
            StatusText = message;
            return;
        }

        if (!string.IsNullOrWhiteSpace(tags.Title)) EditMusicTitle = tags.Title;
        if (!string.IsNullOrWhiteSpace(tags.Artist)) EditMusicArtist = tags.Artist;
        if (!string.IsNullOrWhiteSpace(tags.Album)) EditMusicAlbum = tags.Album;
        if (!string.IsNullOrWhiteSpace(tags.Year)) EditMusicYear = tags.Year;
        if (!string.IsNullOrWhiteSpace(tags.Genre)) EditMusicGenre = tags.Genre;
        if (tags.Cover is { Length: > 0 }) _pendingMusicCover = tags.Cover;

        MusicStatusText = message + " —— 确认无误后点「写入文件」。";
        StatusText = MusicStatusText;
    }
}
