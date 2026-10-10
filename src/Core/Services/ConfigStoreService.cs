using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewToys.Core.Services;

public class ConfigFileEntry
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    [JsonIgnore] public string? Content { get; set; }
}

public class ConfigStoreService
{
    public string ConfigDir => EnsureDir();
    public string DataDir
    {
        get
        {
            var d = Path.Combine(HomeDir, "data");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private string HomeDir
    {
        get
        {
            // 环境变量 NEWTOYS_HOME 可把「应用根」整体换掉（测试环境 / 便携版 / 只要换数据），
            // 设了它 config / data / logs 三层一起隔离，且不做任何迁移。
            var env = Environment.GetEnvironmentVariable("NEWTOYS_HOME");
            if (!string.IsNullOrEmpty(env)) return env;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewToys");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private string EnsureDir()
    {
        // <应用根>\config —— 应用级配置（app.json / theme.json / launch.json …）
        var d = Path.Combine(HomeDir, "config");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>应用根目录（NEWTOYS_HOME 可覆盖）。</summary>
    public string AppRootDirectory => HomeDir;

    /// <summary>日志目录 <c>&lt;应用根&gt;\logs</c>。</summary>
    public string LogsDirectory
    {
        get
        {
            var d = Path.Combine(HomeDir, "logs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public List<ConfigFileEntry> ListConfigFiles()
    {
        var result = new List<ConfigFileEntry>();
        if (!Directory.Exists(ConfigDir)) return result;
        foreach (var f in new DirectoryInfo(ConfigDir).GetFiles("*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ConfigDir, f.FullName);
            if (rel.StartsWith(".bak")) continue;
            result.Add(new ConfigFileEntry
            {
                FileName = f.Name,
                RelativePath = rel,
                FileSize = f.Length,
                LastWriteTimeUtc = f.LastWriteTimeUtc
            });
        }
        return result;
    }

    public string ExportBackup()
    {
        var files = new List<ConfigFileEntry>();
        foreach (var f in new DirectoryInfo(ConfigDir).GetFiles("*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ConfigDir, f.FullName);
            if (rel.StartsWith(".bak")) continue;
            files.Add(new ConfigFileEntry
            {
                FileName = f.Name,
                RelativePath = rel,
                FileSize = f.Length,
                LastWriteTimeUtc = f.LastWriteTimeUtc,
                Content = File.ReadAllText(f.FullName)
            });
        }
        var pkg = new { Version = 1, ExportedAt = DateTime.UtcNow, Files = files };
        return JsonSerializer.Serialize(pkg, new JsonSerializerOptions { WriteIndented = true });
    }

    public (int ok, int skipped, string? backupDir) ImportBackup(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Files", out var filesEl)) throw new InvalidDataException("备份文件缺少 Files 节点");

        var bak = Path.Combine(ConfigDir, $".bak-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(bak);
        foreach (var f in new DirectoryInfo(ConfigDir).GetFiles("*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ConfigDir, f.FullName);
            if (rel.StartsWith(".bak")) continue;
            var dst = Path.Combine(bak, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f.FullName, dst, true);
        }

        int ok = 0, skipped = 0;
        foreach (var e in filesEl.EnumerateArray())
        {
            var rel = (e.GetProperty("RelativePath").GetString() ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(rel)) { skipped++; continue; }
            var full = Path.GetFullPath(Path.Combine(ConfigDir, rel));
            if (!full.StartsWith(ConfigDir, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; } // 路径穿越防御
            var content = e.GetProperty("Content").GetString() ?? "";
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            ok++;
        }
        return (ok, skipped, bak);
    }
}