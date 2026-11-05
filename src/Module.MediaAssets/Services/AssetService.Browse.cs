using System;
using System.Collections.Generic;
using System.Globalization;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>「资产浏览」页的数据源：按文件位置建行（同一资产在多个目录各出现一次 = 正确语义）， 目录路径取自 file_locations.relative_path（老数据为空时用 path 减 storages.root_path 兜底）。（WHY: MA-020）</summary>
public sealed record BrowseEntry(AssetRow Row, long LocationId, string Dir);

public partial class AssetService
{
    /// <summary>
    /// 取全部可浏览的位置行（一次查询入内存；1w 行的量级远快于逐目录查库）。
    /// 组织信息（标签 / 分类 / 集合）批量补齐，口径与 <see cref="QueryPage"/> 一致。
    /// </summary>
    public List<BrowseEntry> LoadBrowseEntries()
    {
        var list = new List<BrowseEntry>();
        if (!IsAvailable) return list;

        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT a.id, a.type, a.title, a.display_name, a.status, a.rating, a.description, " +
                "       IFNULL(l.file_name,''), IFNULL(l.path,''), IFNULL(l.size,0), l.mtime, " +
                "       IFNULL(l.status,'active'), IFNULL(l.storage_id,0), IFNULL(s.name,''), a.updated_at, a.metadata_json, " +
                "       IFNULL(a.is_favorite,0), l.id, IFNULL(l.relative_path,''), IFNULL(s.root_path,'') " +
                "FROM file_locations l " +
                "JOIN assets a ON a.id = l.asset_id " +
                "JOIN storages s ON s.id = l.storage_id " +
                "WHERE l.status IN ('active','missing') AND a.status <> 'trashed' " +
                "ORDER BY l.path;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var meta = r.IsDBNull(15) ? "{}" : r.GetString(15);
                var row = new AssetRow
                {
                    Id = r.GetInt64(0),
                    Type = r.IsDBNull(1) ? AssetTypes.Other : r.GetString(1),
                    Title = r.IsDBNull(2) ? null : r.GetString(2),
                    DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
                    Status = r.IsDBNull(4) ? AssetStatuses.Active : r.GetString(4),
                    Rating = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    Description = r.IsDBNull(6) ? null : r.GetString(6),
                    FileName = r.GetString(7),
                    FilePath = r.GetString(8),
                    Size = r.GetInt64(9),
                    Mtime = r.IsDBNull(10) ? null : r.GetString(10),
                    LocationStatus = r.IsDBNull(11) ? LocationStatuses.Active : r.GetString(11),
                    StorageId = r.IsDBNull(12) ? 0 : r.GetInt64(12),
                    StorageName = r.GetString(13),
                    UpdatedAt = r.IsDBNull(14) ? null : r.GetString(14),
                    IsFavorite = !r.IsDBNull(16) && r.GetInt32(16) != 0,
                    Name = !r.IsDBNull(3) && r.GetString(3).Length > 0 ? r.GetString(3)
                        : !r.IsDBNull(2) && r.GetString(2).Length > 0 ? r.GetString(2)
                        : "资产 #" + r.GetInt64(0).ToString(CultureInfo.InvariantCulture)
                };
                ApplyVideoMeta(row, meta);

                var dir = DirOf(r.GetString(18), row.FilePath, r.GetString(19));
                list.Add(new BrowseEntry(row, r.GetInt64(17), dir));
            }
            LastQueryError = "";
        }
        catch (Exception ex) { LastQueryError = ex.Message; return list; }

        FillOrgTexts(list);
        return list;
    }

    /// <summary>三类组织信息一次性补齐（按本批 id，⛔ 不 per-row 查）。</summary>
    private void FillOrgTexts(List<BrowseEntry> list)
    {
        var ids = new HashSet<long>();
        foreach (var e in list) ids.Add(e.Row.Id);
        if (ids.Count == 0) return;

        var tags = LoadOrgLists(ids, "asset_tags", "tags");
        var cats = LoadOrgLists(ids, "asset_categories", "categories");
        var cols = LoadOrgLists(ids, "collection_assets", "collections");
        foreach (var e in list)
        {
            if (tags.TryGetValue(e.Row.Id, out var t)) e.Row.TagsText = string.Join(", ", t);
            if (cats.TryGetValue(e.Row.Id, out var c)) e.Row.CategoriesText = string.Join(", ", c);
            if (cols.TryGetValue(e.Row.Id, out var k)) e.Row.CollectionsText = string.Join(", ", k);
        }
    }

    /// <summary>
    /// 位置所在目录（相对存储源根；<c>\</c> 分隔，空串 = 根目录）。
    /// <paramref name="relativePath"/> 为空（存储分层前的老数据）⇒ 用 <paramref name="path"/> 减 <paramref name="rootPath"/> 兜底。
    /// </summary>
    public static string DirOf(string? relativePath, string path, string rootPath)
    {
        var rel = NormalizeRelative(relativePath);
        if (rel.Length == 0 && !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(rootPath))
            rel = NormalizeRelative(TrimRoot(path, rootPath));
        return ParentDir(rel);
    }

    /// <summary>相对路径归一化（分隔符统一成 <c>\</c>、去掉前后分隔符）。</summary>
    public static string NormalizeRelative(string? relativePath)
        => (relativePath ?? "").Replace('/', '\\').Trim().Trim('\\');

    /// <summary>绝对路径 → 相对 <paramref name="root"/> 的路径（不在其下 ⇒ 空串）。</summary>
    public static string TrimRoot(string path, string root)
    {
        var p = NormalizeRelative(path);
        var r = NormalizeRelative(root);
        if (r.Length == 0) return p;
        return p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) ? p[(r.Length + 1)..] : "";
    }

    /// <summary>父目录（<c>a\b</c> → <c>a</c>；<c>a</c> → 空串 = 根）。</summary>
    public static string ParentDir(string relativePath)
    {
        var rel = NormalizeRelative(relativePath);
        var i = rel.LastIndexOf('\\');
        return i <= 0 ? "" : rel[..i];
    }
}
