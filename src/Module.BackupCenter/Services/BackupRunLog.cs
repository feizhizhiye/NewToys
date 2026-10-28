using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 运行记录（SQLite <c>backup.db</c> 的 <c>runs</c> 表）。
/// <para>保留策略：表里最多留最近 200 条 —— 日历页要「哪天备份过」的长历史，但不需要无限增长。</para>
/// </summary>
public sealed class BackupRunLog
{
    private const int MaxRecords = 200;
    private const string LegacyMigration = "runs-json";

    private readonly BackupDb _db;
    private readonly BackupSettingsService _paths;

    public BackupRunLog(BackupDb db, BackupSettingsService paths)
    {
        _db = db;
        _paths = paths;
        Load();
    }

    public List<BackupRunRecord> Items { get; private set; } = new();

    /// <summary>库文件全路径（界面显示用）。</summary>
    public string FilePath => _db.DatabasePath;

    public void Load()
    {
        Items = _db.IsReady ? ReadAll() : new();
        MigrateLegacyJson();
    }

    public void Append(BackupRunRecord record)
    {
        Items.Insert(0, record);
        if (Items.Count > MaxRecords) Items.RemoveRange(MaxRecords, Items.Count - MaxRecords);
        Save();
    }

    /// <summary>某个任务最近的 N 条（新的在前）。</summary>
    public IEnumerable<BackupRunRecord> Recent(string planId, int count)
        => Items.Where(r => r.PlanId == planId).OrderByDescending(r => r.StartedAt).Take(count);

    /// <summary>某一天的记录（按本地日期）；日历页与日历详情用。</summary>
    public IEnumerable<BackupRunRecord> OnDate(DateTime day)
        => Items.Where(r => r.StartedAt.Date == day.Date).OrderByDescending(r => r.StartedAt);

    private void Save()
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM runs;";
                del.ExecuteNonQuery();
            }
            foreach (var r in Items)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO runs (plan_id, started_at, status, archive_file, message, skipped) " +
                    "VALUES ($pid, $started, $status, $file, $msg, $skipped);";
                cmd.Parameters.AddWithValue("$pid", r.PlanId ?? "");
                cmd.Parameters.AddWithValue("$started", r.StartedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$status", r.Status ?? "");
                cmd.Parameters.AddWithValue("$file", r.ArchiveFile ?? "");
                cmd.Parameters.AddWithValue("$msg", r.Message ?? "");
                cmd.Parameters.AddWithValue("$skipped", r.SkippedFiles);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 运行记录写入失败：" + ex.Message);
        }
    }

    private List<BackupRunRecord> ReadAll()
    {
        var list = new List<BackupRunRecord>();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT plan_id, started_at, status, archive_file, message, skipped " +
                "FROM runs ORDER BY started_at DESC LIMIT " + MaxRecords + ";";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BackupRunRecord
                {
                    PlanId = reader.GetString(0),
                    StartedAt = DateTime.TryParse(reader.GetString(1), out var t) ? t : default,
                    Status = reader.GetString(2),
                    ArchiveFile = reader.GetString(3),
                    Message = reader.GetString(4),
                    SkippedFiles = reader.GetInt32(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 运行记录读取失败：" + ex.Message);
        }
        return list;
    }

    /// <summary>首次启动把旧 <c>runs.json</c> 导进库（导完标记，⛔ 旧文件不删）。</summary>
    private void MigrateLegacyJson()
    {
        if (!_db.IsReady || _db.IsMigrated(LegacyMigration)) return;
        try
        {
            if (File.Exists(_paths.RunsFile))
            {
                var loaded = JsonSerializer.Deserialize<List<BackupRunRecord>>(
                    File.ReadAllText(_paths.RunsFile));
                if (loaded is { Count: > 0 })
                {
                    Items = loaded.Take(MaxRecords).ToList();
                    Save();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] runs.json 迁移失败（保持空记录）：" + ex.Message);
        }
        _db.MarkMigrated(LegacyMigration);
    }
}
