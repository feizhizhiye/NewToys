using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NewToys.Core.Services;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频收藏（表 <c>video_favorites</c>，与 <c>video_catalog</c> 同库 <c>data\newtoys.db</c>）—— 需求 §6.4.9。
/// <para>
/// 只存三列：<c>path</c>（视频完整路径，主键）/ <c>thumb</c>（缩略图**相对** <see cref="ThumbsDir"/> 的文件名，可空）/ <c>added_at</c>。
/// 名字 / 大小 / 分辨率这些**不存**：一律从 <c>path</c> 现读（文件可能已移动 / 改名，存快照只会骗人）。
/// </para>
/// <para>
/// 缩略图落在磁盘 <c>data\thumbs\{sha1(path)}.jpg</c>（⛔ 不进 DB blob；DB 只记相对文件名），
/// 由「视频预览」页的「📸 取当前帧」用 ffmpeg 抽出来（§6.4.2）。
/// </para>
/// <para>
/// ⚠️ 本期**不做**独立 DB 文件（"多个数据库文件"只是后续方向）。
/// </para>
/// </summary>
public class VideoFavoritesService
{
    private const string Table = "video_favorites";

    private readonly SqliteDatabase _db;

    public VideoFavoritesService(SqliteDatabase db)
    {
        _db = db;
        _db.EnsureTable(
            $"CREATE TABLE IF NOT EXISTS {Table} (" +
            "  path     TEXT PRIMARY KEY," +
            "  thumb    TEXT," +
            "  added_at INTEGER NOT NULL" +
            ");");

        // 缩略图目录跟着库文件走（库在 <应用根>\data 下），这样 NEWTOYS_HOME 整根切换时两边一起搬。
        var dir = Path.GetDirectoryName(db.DatabasePath);
        ThumbsDir = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "thumbs");
    }

    /// <summary>数据库可用性。</summary>
    public bool IsAvailable => _db.IsAvailable;

    /// <summary>缩略图存放目录（<c>&lt;应用根&gt;\data\thumbs</c>）。</summary>
    public string ThumbsDir { get; }

    /// <summary>缩略图文件名（<c>{sha1(path)}.jpg</c>）—— 同一个视频永远同一张图，路径改了也不重复抽。</summary>
    public static string ThumbFileName(string path)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(path ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant() + ".jpg";
    }

    /// <summary>由缩略图文件名拼出磁盘全路径（给界面加载图片用）。</summary>
    public string ThumbFullPath(string? thumbFileName)
        => string.IsNullOrWhiteSpace(thumbFileName) ? "" : Path.Combine(ThumbsDir, thumbFileName);

    /// <summary>是否已收藏（视频列表的「已收藏」列用）。查不到 / 库不可用一律 false。</summary>
    public bool Contains(string path)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {Table} WHERE path = $path LIMIT 1;";
            cmd.Parameters.AddWithValue("$path", path);
            return cmd.ExecuteScalar() != null;
        }
        catch { return false; }
    }

    /// <summary>加入收藏（已存在则只更新缩略图）。返回是否成功。</summary>
    public bool Add(string path, string? thumbFileName = null)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"INSERT INTO {Table} (path, thumb, added_at) VALUES ($path, $thumb, $at) " +
                "ON CONFLICT(path) DO UPDATE SET thumb = COALESCE(excluded.thumb, thumb);";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$thumb", (object?)thumbFileName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.Ticks);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch { return false; }
    }

    /// <summary>更新缩略图（「📸 取当前帧」抽出图后调用）。</summary>
    public bool SetThumb(string path, string thumbFileName) => Add(path, thumbFileName);

    /// <summary>取消收藏；同时删掉它的缩略图文件（库记录没了，图留着只会变成垃圾）。</summary>
    public bool Remove(string path)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string? thumb = null;
            using (var conn = _db.Open())
            {
                // ⚠️ 先读缩略图名再删行：删完再查必然查不到（缩略图文件会永远残留）
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT thumb FROM {Table} WHERE path = $path;";
                    cmd.Parameters.AddWithValue("$path", path);
                    thumb = cmd.ExecuteScalar() as string;
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"DELETE FROM {Table} WHERE path = $path;";
                    cmd.Parameters.AddWithValue("$path", path);
                    cmd.ExecuteNonQuery();
                }
            }
            // 删图失败无所谓（记录已经删了）
            if (!string.IsNullOrWhiteSpace(thumb))
            {
                try { File.Delete(ThumbFullPath(thumb)); } catch { /* 图本来就没有 / 被占用：忽略 */ }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// v3.15：源文件改名后同步收藏行的 path（详情卡内改名走 <c>VideoFileService.RenameToAsync</c>，
    /// 若不跟着改，收藏页会留一条指向"已经不存在的文件"的记录）。
    /// <para>缩略图名是 <c>sha1(path)</c>，路径变了名也要变 → 顺手把磁盘上的图文件一起改名。</para>
    /// </summary>
    public bool MovePath(string oldPath, string newPath)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return false;
        try
        {
            using var conn = _db.Open();

            string? oldThumb;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT thumb FROM {Table} WHERE path = $old;";
                cmd.Parameters.AddWithValue("$old", oldPath);
                oldThumb = cmd.ExecuteScalar() as string;
            }
            if (oldThumb == null) return false;      // 没收藏过 → 不用管

            var newThumb = oldThumb;
            if (!string.IsNullOrWhiteSpace(oldThumb))
            {
                try
                {
                    Directory.CreateDirectory(ThumbsDir);
                    var from = ThumbFullPath(oldThumb);
                    var to = ThumbFullPath(ThumbFileName(newPath));
                    if (File.Exists(from))
                    {
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(from, to);
                        newThumb = ThumbFileName(newPath);
                    }
                    // 图本来就不在磁盘上：沿用旧名（反正都没有图），别记一个必然不存在的名字
                }
                catch { /* 改图失败不影响收藏记录本身 */ }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"UPDATE {Table} SET path = $new, thumb = $thumb WHERE path = $old;";
                cmd.Parameters.AddWithValue("$old", oldPath);
                cmd.Parameters.AddWithValue("$new", newPath);
                cmd.Parameters.AddWithValue("$thumb", (object?)newThumb ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>读出全部收藏（按加入时间倒序）。<c>Thumb</c> 是**文件名**，调用方用 <see cref="ThumbFullPath"/> 拼全路径。</summary>
    public List<VideoFavoriteRow> All()
    {
        var list = new List<VideoFavoriteRow>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT path, thumb, added_at FROM {Table} ORDER BY added_at DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new VideoFavoriteRow
                {
                    Path = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Thumb = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    AddedAtUtcTicks = reader.IsDBNull(2) ? 0 : reader.GetInt64(2)
                });
            }
        }
        catch { /* 读不到就返回空 */ }
        return list;
    }
}

/// <summary>收藏表里的一行（纯数据搬运，不含 UI）。</summary>
public sealed class VideoFavoriteRow
{
    public string Path { get; set; } = "";
    public string Thumb { get; set; } = "";
    public long AddedAtUtcTicks { get; set; }

    public DateTime AddedAt => AddedAtUtcTicks <= 0
        ? default
        : new DateTime(AddedAtUtcTicks, DateTimeKind.Utc).ToLocalTime();

    public string AddedAtDisplay => AddedAtUtcTicks <= 0 ? "—" : AddedAt.ToString("yyyy-MM-dd HH:mm");
}
