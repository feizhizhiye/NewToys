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

    /// <summary>
    /// v3.29「💾 加入库」：把**预览扫描**出来的内存行批量落库（一个事务，全部成功或全部不写）。
    /// <para>
    /// 行可能来自多个目录（预览一个目录 / 多选文件都走这里）→ 按所在目录分组，
    /// 各自登记或复用存储源（<c>root_path</c> 去重）。⛔ 只建索引，⛔ 不碰磁盘文件。
    /// </para>
    /// </summary>
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

                var storage = FindStorageByRoot(dir);
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
                }

                foreach (var r in list)
                {
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
                    lCmd.Parameters.AddWithValue("$rel", Path.GetRelativePath(trimmed, r.FilePath));
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
