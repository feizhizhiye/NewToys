using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NewToys.BackupCenter.Models;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>任务清单（<c>config\backup\plans.json</c>）：只存配置，⛔ 不存密码。</summary>
public sealed class BackupPlanStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly BackupSettingsService _paths;

    public BackupPlanStore(BackupSettingsService paths)
    {
        _paths = paths;
        Load();
    }

    public List<BackupPlan> Items { get; private set; } = new();

    public string FilePath => _paths.PlansFile;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Items = new(); return; }
            Items = JsonSerializer.Deserialize<List<BackupPlan>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Items = new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(_paths.DataDir);
        AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(Items, JsonOpts));
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

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var i = 2;
        while (Items.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = baseName + " (" + i++ + ")";
        return name;
    }
}
