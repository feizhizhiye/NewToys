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

    public long CreateScanJob(long? storageId, string mode)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO scan_jobs(storage_id,mode,status,started_at,created_at) VALUES($sid,$mode,'running',$s,$c); " +
                "SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$sid", (object?)storageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mode", mode);
            cmd.Parameters.AddWithValue("$s", Now());
            cmd.Parameters.AddWithValue("$c", Now());
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    /// <summary>回填任务进度 / 结果（结束时传 status = success / cancelled / failed）。</summary>
    public void UpdateScanJob(ScanJob job)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE scan_jobs SET status=$st, total_files=$t, scanned_files=$s, added_files=$a, " +
                "updated_files=$u, missing_files=$m, error_count=$e, finished_at=$f WHERE id=$id;";
            cmd.Parameters.AddWithValue("$st", job.Status);
            cmd.Parameters.AddWithValue("$t", job.TotalFiles);
            cmd.Parameters.AddWithValue("$s", job.ScannedFiles);
            cmd.Parameters.AddWithValue("$a", job.AddedFiles);
            cmd.Parameters.AddWithValue("$u", job.UpdatedFiles);
            cmd.Parameters.AddWithValue("$m", job.MissingFiles);
            cmd.Parameters.AddWithValue("$e", job.ErrorCount);
            cmd.Parameters.AddWithValue("$f", (object?)job.FinishedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", job.Id);
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>记一条扫描错误（单个文件失败 ⛔ 不中断整次扫描）。</summary>
    public void AddScanError(long jobId, string? path, string errorType, string message)
    {
        if (!IsAvailable || jobId == 0) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO scan_errors(scan_job_id,path,error_type,message,created_at) VALUES($j,$p,$t,$m,$c);";
            cmd.Parameters.AddWithValue("$j", jobId);
            cmd.Parameters.AddWithValue("$p", (object?)path ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$t", errorType);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.Parameters.AddWithValue("$c", Now());
            cmd.ExecuteNonQuery();
        }
        catch { /* 忽略 */ }
    }

    public List<ScanJob> ListRecentScanJobs(int limit = 50)
    {
        var list = new List<ScanJob>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id,storage_id,mode,status,total_files,scanned_files,added_files,updated_files," +
                "missing_files,error_count,started_at,finished_at,created_at FROM scan_jobs ORDER BY id DESC LIMIT $lim;";
            cmd.Parameters.AddWithValue("$lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ScanJob
                {
                    Id = r.GetInt64(0),
                    StorageId = r.IsDBNull(1) ? null : r.GetInt64(1),
                    Mode = r.GetString(2),
                    Status = r.GetString(3),
                    TotalFiles = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    ScannedFiles = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    AddedFiles = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    UpdatedFiles = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                    MissingFiles = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                    ErrorCount = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                    StartedAt = r.IsDBNull(10) ? null : r.GetString(10),
                    FinishedAt = r.IsDBNull(11) ? null : r.GetString(11),
                    CreatedAt = r.IsDBNull(12) ? "" : r.GetString(12)
                });
            }
        }
        catch { /* 忽略 */ }
        return list;
    }

    // ==================== 内部：列清单 / 绑定 / 映射 ====================

    private const string LocationColumns =
        "id,asset_id,storage_id,path,relative_path,file_name,ext,mime_type,size,mtime,hash,quick_hash,inode,device," +
        "is_primary,status,last_seen_at,created_at,updated_at";

    private static void BindLocation(SqliteCommand cmd, FileLocation l)
    {
        cmd.Parameters.AddWithValue("$aid", l.AssetId);
        cmd.Parameters.AddWithValue("$sid", l.StorageId);
        cmd.Parameters.AddWithValue("$path", l.Path);
        cmd.Parameters.AddWithValue("$rel", (object?)l.RelativePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fn", l.FileName);
        cmd.Parameters.AddWithValue("$ext", (object?)l.Ext ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mime", (object?)l.MimeType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", l.Size);
        cmd.Parameters.AddWithValue("$mtime", (object?)l.Mtime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", (object?)l.Hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$qhash", (object?)l.QuickHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inode", (object?)l.Inode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dev", (object?)l.Device ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prim", l.IsPrimary ? 1 : 0);
        cmd.Parameters.AddWithValue("$st", l.Status);
        cmd.Parameters.AddWithValue("$seen", (object?)l.LastSeenAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", string.IsNullOrEmpty(l.CreatedAt) ? Now() : l.CreatedAt);
        cmd.Parameters.AddWithValue("$u", Now());
    }

    private static FileLocation MapLocation(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        AssetId = r.GetInt64(1),
        StorageId = r.GetInt64(2),
        Path = r.GetString(3),
        RelativePath = r.IsDBNull(4) ? null : r.GetString(4),
        FileName = r.GetString(5),
        Ext = r.IsDBNull(6) ? null : r.GetString(6),
        MimeType = r.IsDBNull(7) ? null : r.GetString(7),
        Size = r.IsDBNull(8) ? 0 : r.GetInt64(8),
        Mtime = r.IsDBNull(9) ? null : r.GetString(9),
        Hash = r.IsDBNull(10) ? null : r.GetString(10),
        QuickHash = r.IsDBNull(11) ? null : r.GetString(11),
        Inode = r.IsDBNull(12) ? null : r.GetString(12),
        Device = r.IsDBNull(13) ? null : r.GetString(13),
        IsPrimary = !r.IsDBNull(14) && r.GetInt64(14) != 0,
        Status = r.IsDBNull(15) ? LocationStatuses.Active : r.GetString(15),
        LastSeenAt = r.IsDBNull(16) ? null : r.GetString(16),
        CreatedAt = r.IsDBNull(17) ? "" : r.GetString(17),
        UpdatedAt = r.IsDBNull(18) ? "" : r.GetString(18)
    };

    private static Asset MapAsset(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Type = r.GetString(1),
        Title = r.IsDBNull(2) ? null : r.GetString(2),
        DisplayName = r.IsDBNull(3) ? null : r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        Status = r.IsDBNull(5) ? AssetStatuses.Active : r.GetString(5),
        Rating = r.IsDBNull(6) ? 0 : r.GetInt32(6),
        MetadataJson = r.IsDBNull(7) ? "{}" : r.GetString(7),
        CreatedAt = r.IsDBNull(8) ? "" : r.GetString(8),
        UpdatedAt = r.IsDBNull(9) ? "" : r.GetString(9)
    };

    /// <summary>根路径规范化：去尾部分隔符（⛔ 不解析符号链接 —— 那要读盘，扫描时再按需做）。</summary>
    private static string NormalizeRoot(string rootPath)
    {
        var p = (rootPath ?? "").Trim();
        if (p.Length > 3) p = p.TrimEnd('\\', '/');
        return p;
    }

}
