using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 音乐文件标签（ID3v2 / FLAC / MP4）的**读写**与**联网补全**。
/// <para>
/// 🔴 写操作只改标签，⛔ 不动音频数据；播放器只读不写（编辑归资产库，见「查看器 → 音乐播放」的只读契约）。
/// </para>
/// <para>
/// 联网源：MusicBrainz 查元数据 + Cover Art Archive 取封面。两家都免费但要 UA、限速 ~1 请求/秒，
/// 所以一次查询只发一个 GET，⛔ 不并发轰炸。
/// </para>
/// </summary>
public class MusicTagService
{
    /// <summary>一首歌的可编辑标签。</summary>
    public sealed class MusicTags
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Year { get; set; } = "";
        public string Genre { get; set; } = "";
        public byte[]? Cover { get; set; }
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // MusicBrainz 要求带可识别的 UA（否则 403）
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NewToys/1.0 (local asset manager)");
        return client;
    }

    // ==================== 本地读写 ====================

    /// <summary>读一首的标签（读不到返回 false + 原因；不算异常）。</summary>
    public bool TryRead(string path, out MusicTags tags, out string error)
    {
        tags = new MusicTags();
        error = "";

        if (string.IsNullOrWhiteSpace(path)) { error = "路径为空。"; return false; }

        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;

            tags = new MusicTags
            {
                Title = tag.Title ?? "",
                Artist = tag.FirstPerformer ?? tag.FirstAlbumArtist ?? "",
                Album = tag.Album ?? "",
                Year = tag.Year > 0 ? tag.Year.ToString() : "",
                Genre = tag.FirstGenre ?? "",
            };

            var pictures = tag.Pictures;
            if (pictures is { Length: > 0 })
            {
                var data = pictures[0].Data?.Data;
                if (data is { Length: > 0 }) tags.Cover = data;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "读标签失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>把标签写回文件（只写非空字段；封面有就一起写）。</summary>
    public bool TryWrite(string path, MusicTags tags, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path)) { error = "路径为空。"; return false; }
        if (tags == null) { error = "没有要写入的内容。"; return false; }

        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;

            if (!string.IsNullOrWhiteSpace(tags.Title)) tag.Title = tags.Title.Trim();
            if (!string.IsNullOrWhiteSpace(tags.Artist)) tag.Performers = new[] { tags.Artist.Trim() };
            if (!string.IsNullOrWhiteSpace(tags.Album)) tag.Album = tags.Album.Trim();
            if (!string.IsNullOrWhiteSpace(tags.Genre)) tag.Genres = new[] { tags.Genre.Trim() };
            if (uint.TryParse((tags.Year ?? "").Trim(), out var year) && year > 0) tag.Year = year;

            if (tags.Cover is { Length: > 0 })
            {
                tag.Pictures = new TagLib.IPicture[]
                {
                    new TagLib.Picture(new TagLib.ByteVector(tags.Cover)) { Type = TagLib.PictureType.FrontCover }
                };
            }

            file.Save();
            return true;
        }
        catch (Exception ex)
        {
            error = "写标签失败（文件被占用或格式不支持？）：" + ex.Message;
            return false;
        }
    }

    // ==================== 联网补全 ====================

    /// <summary>
    /// 按「标题 + 艺术家」查 MusicBrainz，命中则连同封面（Cover Art Archive）一起返回。
    /// <para>查不到不算错误：返回 <c>null</c> 标签 + 说明文案。</para>
    /// </summary>
    public async Task<(MusicTags? Tags, string Message)> FetchOnlineAsync(string title, string artist, CancellationToken ct)
    {
        var queryTitle = (title ?? "").Trim();
        if (queryTitle.Length == 0) return (null, "先填标题（或先用文件名当标题）再联网查询。");

        try
        {
            var query = new StringBuilder("recording:\"").Append(queryTitle).Append('"');
            var artistText = (artist ?? "").Trim();
            if (artistText.Length > 0) query.Append(" AND artist:\"").Append(artistText).Append('"');

            var url = "https://musicbrainz.org/ws/2/recording/?fmt=json&limit=1&query="
                      + Uri.EscapeDataString(query.ToString());

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var json = await Http.GetStringAsync(url, cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("recordings", out var recordings)
                || recordings.GetArrayLength() == 0)
                return (null, $"MusicBrainz 没查到「{queryTitle}」—— 换个标题 / 补上艺术家再试，或手动填。");

            var rec = recordings[0];
            var tags = new MusicTags { Title = queryTitle };

            if (rec.TryGetProperty("title", out var t)) tags.Title = t.GetString() ?? queryTitle;
            if (rec.TryGetProperty("artist-credit", out var credits) && credits.GetArrayLength() > 0
                && credits[0].TryGetProperty("name", out var an))
                tags.Artist = an.GetString() ?? "";

            string? releaseGroupId = null;
            string? releaseId = null;
            if (rec.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
            {
                var release = releases[0];
                if (release.TryGetProperty("title", out var rt)) tags.Album = rt.GetString() ?? "";
                if (release.TryGetProperty("date", out var rd))
                {
                    var date = rd.GetString() ?? "";
                    tags.Year = date.Length >= 4 ? date[..4] : date;
                }
                if (release.TryGetProperty("id", out var rid)) releaseId = rid.GetString();
                if (release.TryGetProperty("release-group", out var rg) && rg.TryGetProperty("id", out var rgid))
                    releaseGroupId = rgid.GetString();
            }

            if (rec.TryGetProperty("tags", out var recTags) && recTags.GetArrayLength() > 0
                && recTags[0].TryGetProperty("name", out var gn))
                tags.Genre = gn.GetString() ?? "";

            // 封面：优先 release-group，其次 release
            var coverId = releaseGroupId ?? releaseId;
            if (!string.IsNullOrWhiteSpace(coverId))
            {
                tags.Cover = await TryGetCoverAsync(releaseGroupId, releaseId, cts.Token).ConfigureAwait(false);
            }

            return (tags, $"已从 MusicBrainz 取到：{tags.Title} · {tags.Artist} · {tags.Album}"
                          + (tags.Cover is { Length: > 0 } ? "（含封面）" : "（无封面）"));
        }
        catch (OperationCanceledException)
        {
            return (null, "联网查询超时（MusicBrainz 有时很慢，稍后再试或手动填）。");
        }
        catch (Exception ex)
        {
            return (null, "联网查询失败：" + ex.Message);
        }
    }

    private static async Task<byte[]?> TryGetCoverAsync(string? releaseGroupId, string? releaseId, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(releaseGroupId))
            candidates.Add($"https://coverartarchive.org/release-group/{releaseGroupId}/front-500");
        if (!string.IsNullOrWhiteSpace(releaseId))
            candidates.Add($"https://coverartarchive.org/release/{releaseId}/front-500");

        foreach (var url in candidates)
        {
            try
            {
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length > 0) return bytes;
            }
            catch { /* 没封面不影响其它字段 */ }
        }
        return null;
    }
}
