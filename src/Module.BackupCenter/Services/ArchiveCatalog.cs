using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NewToys.BackupCenter.Models;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 本地包清单（<c>config\backup\catalog.json</c>）。
/// <para>
/// 记录与磁盘**双向对齐**：<see cref="SyncWithDisk"/> 把目录里没登记过的 <c>.7z</c> 补进来（顺便算 SHA256），
/// 把文件已不存在的记录剔掉 —— 用户手工删包 / 拷包进来都能反映出来。
/// </para>
/// </summary>
public sealed class ArchiveCatalog
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly BackupSettingsService _paths;
    private readonly BackupPlanStore _plans;

    public ArchiveCatalog(BackupSettingsService paths, BackupPlanStore plans)
    {
        _paths = paths;
        _plans = plans;
        Load();
    }

    public List<ArchiveRecord> Items { get; private set; } = new();

    public string FilePath => _paths.CatalogFile;

    public string ArchiveDir => _paths.ArchiveDir;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Items = new(); return; }
            Items = JsonSerializer.Deserialize<List<ArchiveRecord>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Items = new(); }
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(_paths.DataDir);
        AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(Items, JsonOpts));
    }

    public void Add(ArchiveRecord record)
    {
        Items.Add(record);
        Save();
    }

    public bool Remove(ArchiveRecord record)
    {
        if (!Items.Remove(record)) return false;
        Save();
        return true;
    }

    /// <summary>与磁盘对齐并重算文件大小（SHA256 只给新出现的文件算，已有记录不重算）。</summary>
    public (int Added, int Removed) SyncWithDisk()
    {
        var dir = ArchiveDir;
        var added = 0;
        var removed = 0;

        if (!System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
            return (0, 0);
        }

        var files = System.IO.Directory.GetFiles(dir, "*.7z").ToList();

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var rec = Items[i];
            if (!File.Exists(Path.Combine(dir, rec.FileName))) { Items.RemoveAt(i); removed++; }
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (Items.Exists(r => string.Equals(r.FileName, name, StringComparison.OrdinalIgnoreCase))) continue;

            var info = new FileInfo(file);
            Items.Add(new ArchiveRecord
            {
                FileName = name,
                PlanId = GuessPlanId(name),
                PlanName = GuessPlanName(name),
                SizeBytes = info.Length,
                CreatedAt = info.LastWriteTime,
                Sha256 = ComputeSha256(file)
            });
            added++;
        }

        if (added > 0 || removed > 0) Save();
        return (added, removed);
    }

    /// <summary>整包 SHA256（大写十六进制）。</summary>
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>按文件名反查任务：<c>&lt;任务名&gt;-&lt;yyyyMMdd-HHmmss&gt;.7z</c>。</summary>
    private string GuessPlanId(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var idx = stem.LastIndexOf('-');
        if (idx <= 0) return "";
        var prefix = stem[..idx];
        var plan = _plans.Items.FirstOrDefault(p => string.Equals(p.Name, prefix, StringComparison.OrdinalIgnoreCase));
        return plan?.Id ?? "";
    }

    private string GuessPlanName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var idx = stem.LastIndexOf('-');
        return idx <= 0 ? stem : stem[..idx];
    }
}
