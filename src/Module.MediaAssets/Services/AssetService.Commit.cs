using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Models;

namespace NewToys.MediaAssets.Services;

public partial class AssetService
{

    /// <summary>
    /// 只改显示名（`display_name`）—— 详情卡改名走这里。
    /// ⛔ **绝不动磁盘文件**：物理改名是显式操作，由文件操作服务负责。
    /// </summary>
    public void SetDisplayName(long assetId, string? name)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET display_name=$n, updated_at=$u WHERE id=$id;";
            cmd.Parameters.AddWithValue("$n", string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim());
            cmd.Parameters.AddWithValue("$u", Now());
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>「加入库」：把预览扫描出来的内存行批量落库（一个事务，全部成功或全部不写）。（WHY: MA-021）</summary>
    public int CommitRows(IReadOnlyList<AssetRow> rows)
    {
        if (!IsAvailable || rows.Count == 0) return 0;

        // ① 按目录分组（同目录共用一个存储源）
        var groups = new Dictionary<string, List<AssetRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var dir = Path.GetDirectoryName(r.FilePath);
            if (string.IsNullOrEmpty(dir)) continue;
            if (!groups.TryGetValue(dir, out var list)) groups[dir] = list = new List<AssetRow>();
            list.Add(r);
        }
        if (groups.Count == 0) return 0;

        // 事务外先读一次存储源清单：事务内另开连接会和写事务抢锁；它同时用来把子目录归到覆盖它的源
        var known = ListStorages();

        var done = 0;
        try
        {
            using var conn = _db.Db.Open();
            using var tx = conn.BeginTransaction();

            foreach (var (dir, list) in groups)
            {
                var trimmed = dir.TrimEnd('\\', '/');
                var dirName = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(dirName)) dirName = trimmed;

                // 🔴 一个文件只属于它**上一级文件夹**对应的源：按目录精确匹配，
                //    ⛔ 不归到祖先源（否则同一批文件混在父文件夹名下，列表里分不清来源）
                var storage = FindExactStorage(known, dir);
                var storageIsNew = storage == null;
                if (storage == null)
                {
                    // ⚠️ 内联登记（⛔ 不能调 UpsertStorage —— 它自己开连接，会跑出本事务）
                    using var sCmd = conn.CreateCommand();
                    sCmd.Transaction = tx;
                    sCmd.CommandText =
                        "INSERT INTO storages(name,root_path,type,online,created_at,updated_at) " +
                        "VALUES($n,$p,'local',1,$c,$u); SELECT last_insert_rowid();";
                    sCmd.Parameters.AddWithValue("$n", dirName);
                    sCmd.Parameters.AddWithValue("$p", trimmed);
                    sCmd.Parameters.AddWithValue("$c", Now());
                    sCmd.Parameters.AddWithValue("$u", Now());
                    storage = new StorageSource { Id = Convert.ToInt64(sCmd.ExecuteScalar()), RootPath = trimmed, Name = dirName };
                    known.Add(storage);          // 同批后续目录若在它之下 → 复用，⛔ 不再建源
                }

                // 同一存储源内「路径」只许入库一次：重复点「加入库」/ 重复扫描预览 ⛔ 不再产生重复资产。
                // 只在已有存储源上读一次本源的路径集合（新源必然为空，⛔ 别为此多跑一次查询）。
                var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!storageIsNew)
                {
                    using var pCmd = conn.CreateCommand();
                    pCmd.Transaction = tx;
                    pCmd.CommandText = "SELECT path FROM file_locations WHERE storage_id=$sid;";
                    pCmd.Parameters.AddWithValue("$sid", storage.Id);
                    using var pr = pCmd.ExecuteReader();
                    while (pr.Read())
                        if (!pr.IsDBNull(0)) existingPaths.Add(pr.GetString(0));
                }

                foreach (var r in list)
                {
                    // 已存在（含本批内重复）⇒ 跳过：不改库、不复活回收站里的行
                    if (string.IsNullOrEmpty(r.FilePath) || !existingPaths.Add(r.FilePath)) continue;

                    using var aCmd = conn.CreateCommand();
                    aCmd.Transaction = tx;
                    aCmd.CommandText =
                        "INSERT INTO assets(type,title,display_name,status,rating,metadata_json,created_at,updated_at) " +
                        "VALUES($type,$title,$disp,'active',0,'{}',$c,$u); SELECT last_insert_rowid();";
                    aCmd.Parameters.AddWithValue("$type", r.Type);
                    aCmd.Parameters.AddWithValue("$title", (object?)r.Title ?? DBNull.Value);
                    aCmd.Parameters.AddWithValue("$disp", (object?)r.DisplayName ?? DBNull.Value);
                    aCmd.Parameters.AddWithValue("$c", Now());
                    aCmd.Parameters.AddWithValue("$u", Now());
                    var assetId = Convert.ToInt64(aCmd.ExecuteScalar());

                    var ext = Path.GetExtension(r.FileName);
                    using var lCmd = conn.CreateCommand();
                    lCmd.Transaction = tx;
                    lCmd.CommandText =
                        "INSERT INTO file_locations(asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime," +
                        "is_primary,status,last_seen_at,created_at,updated_at) " +
                        "VALUES($aid,$sid,$path,$rel,$fn,$ext,$mime,$size,$mtime,1,'active',$seen,$c,$u);";
                    lCmd.Parameters.AddWithValue("$aid", assetId);
                    lCmd.Parameters.AddWithValue("$sid", storage.Id);
                    lCmd.Parameters.AddWithValue("$path", r.FilePath);
                    lCmd.Parameters.AddWithValue("$rel", Path.GetRelativePath(storage.RootPath, r.FilePath));
                    lCmd.Parameters.AddWithValue("$fn", r.FileName);
                    lCmd.Parameters.AddWithValue("$ext", (object?)ext ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("$mime", (object?)ScanService.GuessMimeType(ext) ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("$size", r.Size);
                    lCmd.Parameters.AddWithValue("$mtime", (object?)r.Mtime ?? DBNull.Value);
                    lCmd.Parameters.AddWithValue("$seen", Now());
                    lCmd.Parameters.AddWithValue("$c", Now());
                    lCmd.Parameters.AddWithValue("$u", Now());
                    lCmd.ExecuteNonQuery();

                    r.Id = assetId;
                    r.StorageId = storage.Id;
                    r.StorageName = storage.Name;
                    done++;
                }
            }

            tx.Commit();
        }
        catch { /* 事务已回滚：返回实际提交数（0 = 一条没进） */ }
        return done;
    }

    // ==================== 扫描任务 / 错误 ====================

}
