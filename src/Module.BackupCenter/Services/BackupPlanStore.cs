using System;
using System.Collections.Generic;
using System.Text.Json;
using NewToys.BackupCenter.Models;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 备份任务清单（SQLite <c>backup.db</c> 的 <c>plans</c> 表）：只存配置，⛔ 不存密码。
/// <para>源目录存成 JSON 数组：数量少、每次整体读写，拆表反而多一层拼装。</para>
/// </summary>
public sealed class BackupPlanStore
{
    private const string LegacyMigration = "plans-json";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly BackupDb _db;
    private readonly BackupSettingsService _paths;

    public BackupPlanStore(BackupDb db, BackupSettingsService paths)
    {
        _db = db;
        _paths = paths;
        Load();
    }

    public List<BackupPlan> Items { get; private set; } = new();

    /// <summary>库文件全路径（界面显示用）。</summary>
    public string FilePath => _db.DatabasePath;

    public void Load()
    {
        Items = _db.IsReady ? ReadAll() : new();
        MigrateLegacyJson();
    }

    /// <summary>全量写回（任务数量级小，整表重写比逐条 diff 便宜）。</summary>
    public void Save()
    {
        if (!_db.IsReady) return;
        try
        {
            using var conn = _db.Open();
            using var tx = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM plans;";
                del.ExecuteNonQuery();
            }
            for (var i = 0; i < Items.Count; i++)
            {
                var p = Items[i];
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO plans (id, name, sources, excludes, keep_count, note, sort_order, deliver) " +
                    "VALUES ($id, $name, $sources, $excludes, $keep, $note, $order, $deliver);";
                cmd.Parameters.AddWithValue("$id", p.Id);
                cmd.Parameters.AddWithValue("$name", p.Name);
                cmd.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(p.Sources, JsonOpts));
                cmd.Parameters.AddWithValue("$excludes", p.Excludes ?? "");
                cmd.Parameters.AddWithValue("$keep", p.KeepCount);
                cmd.Parameters.AddWithValue("$note", p.Note ?? "");
                cmd.Parameters.AddWithValue("$order", i);
                cmd.Parameters.AddWithValue("$deliver", p.Deliver.ToString());
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 任务清单写入失败：" + ex.Message);
        }
    }

    /// <summary>新建一个任务（名称去重：重名自动加序号）。</summary>
    public BackupPlan Add(string name)
    {
        var plan = new BackupPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = UniqueName(string.IsNullOrWhiteSpace(name) ? "新备份任务" : name.Trim()),
            KeepCount = _paths.DefaultKeepCount
        };
        Items.Add(plan);
        Save();
        return plan;
    }

    public bool Remove(BackupPlan plan)
    {
        if (!Items.Remove(plan)) return false;
        Save();
        return true;
    }

    public BackupPlan? Find(string id) => Items.Find(p => p.Id == id);

    private List<BackupPlan> ReadAll()
    {
        var list = new List<BackupPlan>();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, name, sources, excludes, keep_count, note, deliver FROM plans ORDER BY sort_order, name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BackupPlan
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Sources = ParseSources(reader.GetString(2)),
                    Excludes = reader.GetString(3),
                    KeepCount = reader.GetInt32(4),
                    Note = reader.GetString(5),
                    Deliver = Enum.TryParse<BackupDeliver>(reader.IsDBNull(6) ? "" : reader.GetString(6), out var d)
                        ? d : BackupDeliver.Local
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] 任务清单读取失败：" + ex.Message);
        }
        return list;
    }

    private static List<string> ParseSources(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    /// <summary>首次启动把旧 <c>plans.json</c> 导进库（导完标记，⛔ 旧文件不删）。</summary>
    private void MigrateLegacyJson()
    {
        if (!_db.IsReady || _db.IsMigrated(LegacyMigration)) return;
        try
        {
            if (System.IO.File.Exists(_paths.PlansFile))
            {
                var loaded = JsonSerializer.Deserialize<List<BackupPlan>>(
                    System.IO.File.ReadAllText(_paths.PlansFile));
                if (loaded is { Count: > 0 })
                {
                    Items = loaded;
                    Save();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][backup] plans.json 迁移失败（保持空清单）：" + ex.Message);
        }
        _db.MarkMigrated(LegacyMigration);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var i = 2;
        while (Items.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = baseName + " (" + i++ + ")";
        return name;
    }
}
