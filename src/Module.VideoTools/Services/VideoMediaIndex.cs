using System;
using System.Collections.Generic;
using NewToys.Core.Services;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频媒体信息的 <b>SQLite 缓存</b>（表 <c>video_media</c>，库文件 <c>&lt;应用根&gt;\data\newtoys.db</c>）。
/// <para>
/// 为什么值得缓存：ffprobe 每个文件要起一次进程，几百个文件就是几百次进程启动，很慢；
/// 而"这个文件的分辨率 / 编码 / 码率"只要文件没被改动就永远不变。
/// 所以：扫描目录时先用 <see cref="TryApplyCached"/> 把已知的填进去（零成本），
/// 只有**没缓存过**或**文件已被改动**（大小 / 修改时间对不上）的才真去跑 ffprobe，
/// 读完再 <see cref="Upsert"/> 回库里。
/// </para>
/// <para>
/// 缓存整体是懒加载的（第一次用才读全表），单表几十万行也不是问题。
/// 库 / 表不可用时 <see cref="IsAvailable"/> 为 false，此时各方法**退化成"永远不命中 / 不报错"**，
/// 页面照常能用，只是少了加速。
/// </para>
/// </summary>
public class VideoMediaIndex
{
    private const string Table = "video_media";

    private readonly SqliteDatabase _db;

    /// <summary>内存镜像：路径（小写）→ 行。懒加载一次。</summary>
    private Dictionary<string, Row>? _cache;

    /// <summary>数据库可用性（false = 缓存不可用，功能降级但页面照常）。</summary>
    public bool IsAvailable => _db.IsAvailable;

    /// <summary>数据库文件全路径（页面提示用）。</summary>
    public string DatabasePath => _db.DatabasePath;

    public VideoMediaIndex(SqliteDatabase db)
    {
        _db = db;
        _db.EnsureTable(
            $"CREATE TABLE IF NOT EXISTS {Table} (" +
            "  path      TEXT PRIMARY KEY," +
            "  size      INTEGER NOT NULL," +
            "  mtime     INTEGER NOT NULL," +
            "  duration  REAL," +
            "  width     INTEGER," +
            "  height    INTEGER," +
            "  codec     TEXT," +
            "  bit_rate  INTEGER," +
            "  probed_at INTEGER NOT NULL" +
            ");");
    }

    private sealed record Row(long Size, long Mtime, double? Duration, int? Width, int? Height, string Codec, long? BitRate);

    private static string Key(string fullPath) => fullPath.ToLowerInvariant();

    /// <summary>
    /// 尝试用缓存填一条记录。命中（且大小 / 修改时间都对得上）返回 true，否则 false（调用方去跑 ffprobe）。
    /// </summary>
    public bool TryApplyCached(VideoItem item)
    {
        if (!IsAvailable || item == null) return false;

        var map = EnsureLoaded();
        if (map == null || !map.TryGetValue(Key(item.FullPath), out var row)) return false;

        // 文件被改过（重新压制过 / 换过文件）→ 缓存作废，重新探测
        if (row.Size != item.SizeBytes) return false;
        if (Math.Abs(row.Mtime - item.ModifiedUtcTicks) > TimeSpan.TicksPerSecond) return false;

        item.ApplyMedia(new VideoMediaInfo(row.Duration, row.Width, row.Height, row.Codec, row.BitRate));
        return true;
    }

    /// <summary>把一条已探测好的记录写回缓存（INSERT OR REPLACE，顺带更新内存镜像）。</summary>
    public void Upsert(VideoItem item)
    {
        if (!IsAvailable || item == null || !item.HasMedia) return;

        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"INSERT OR REPLACE INTO {Table} (path, size, mtime, duration, width, height, codec, bit_rate, probed_at) " +
                "VALUES ($path, $size, $mtime, $duration, $width, $height, $codec, $bitRate, $at);";
            cmd.Parameters.AddWithValue("$path", Key(item.FullPath));
            cmd.Parameters.AddWithValue("$size", item.SizeBytes);
            cmd.Parameters.AddWithValue("$mtime", item.ModifiedUtcTicks);
            cmd.Parameters.AddWithValue("$duration", (object?)item.Duration ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$width", (object?)item.Width ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$height", (object?)item.Height ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$codec", item.Codec ?? "");
            cmd.Parameters.AddWithValue("$bitRate", (object?)item.BitRate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.Ticks);
            cmd.ExecuteNonQuery();

            // ⚠️ 只更新**已经加载过**的内存镜像：若镜像还是 null（全表没读过），
            // 这里绝不能新建一个空字典 —— 否则它会被当成"全表"，之后的缓存查询全部误判为未命中。
            if (_cache != null)
                _cache[Key(item.FullPath)] = new Row(
                    item.SizeBytes, item.ModifiedUtcTicks, item.Duration, item.Width, item.Height, item.Codec ?? "", item.BitRate);
        }
        catch
        {
            // 落库失败不影响界面（下次还会重新探测一次）；不改 IsAvailable，避免偶发错误把整个缓存关掉
        }
    }

    /// <summary>清空缓存（「清空媒体信息缓存」用；下次扫描会重新探测）。</summary>
    public bool Clear()
    {
        if (!IsAvailable) return false;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {Table};";
            cmd.ExecuteNonQuery();
            _cache = new Dictionary<string, Row>(StringComparer.Ordinal);
            return true;
        }
        catch { return false; }
    }

    /// <summary>已缓存的记录条数（页面提示用）。</summary>
    public int CachedCount => EnsureLoaded()?.Count ?? 0;

    /// <summary>读全表到内存（只读一次）。失败返回 null。</summary>
    private Dictionary<string, Row> EnsureLoaded()
    {
        if (_cache != null) return _cache;
        if (!IsAvailable) return null;

        try
        {
            var map = new Dictionary<string, Row>(StringComparer.Ordinal);
            using (var conn = _db.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT path, size, mtime, duration, width, height, codec, bit_rate FROM {Table};";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (path.Length == 0) continue;
                    map[path] = new Row(
                        reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                        reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                        reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        reader.IsDBNull(6) ? "" : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetInt64(7));
                }
            }
            _cache = map;
            return _cache;
        }
        catch
        {
            return null;
        }
    }
}
