using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.BackupCenter.Models;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>运行记录（<c>config\backup\runs.json</c>）：只留最近 200 条，避免无限增长。</summary>
public sealed class BackupRunLog
{
    private const int MaxRecords = 200;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly BackupSettingsService _paths;

    public BackupRunLog(BackupSettingsService paths)
    {
        _paths = paths;
        Load();
    }

    public List<BackupRunRecord> Items { get; private set; } = new();

    public string FilePath => _paths.RunsFile;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Items = new(); return; }
            Items = JsonSerializer.Deserialize<List<BackupRunRecord>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Items = new(); }
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

    private void Save()
    {
        Directory.CreateDirectory(_paths.DataDir);
        AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(Items, JsonOpts));
    }
}
