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

/// <summary>
/// 「应用根 → 配置（profile）→ config/data/logs」三级路径的唯一决定者。
/// <para>
/// v3.5 起支持**多配置**：<c>&lt;应用根&gt;\profiles\&lt;配置名&gt;\{config,data,logs}</c>；
/// 当前配置名写在 <c>&lt;应用根&gt;\active-profile.txt</c>（**故意放在 profiles 之外** ——
/// 否则切换配置时会把自己也换掉）。
/// </para>
/// <para>
/// ⚠️ 全工程的服务都只向本类要路径（<see cref="ConfigDir"/> / <see cref="DataDir"/> /
/// <see cref="LogsDirectory"/>），所以「换配置」只需改变这里的计算 —— 不必逐个服务改造。
/// </para>
/// <para>
/// 环境变量 <c>NEWTOYS_HOME</c> 可把**整个应用根**换掉（测试 / 便携版），且不做迁移。
/// </para>
/// </summary>
public class ConfigStoreService
{
    private const string ProfilesFolderName = "profiles";
    private const string ActiveProfileFileName = "active-profile.txt";

    /// <summary>默认配置名（旧结构迁移后就叫它）。</summary>
    public const string DefaultProfileName = "default";

    private readonly string _appRoot;
    private readonly string _profileRoot;

    /// <summary>启动参数 <c>--profile</c> 的覆盖值（v3.26，需求 §3.5）：合法且配置目录已存在时才生效。</summary>
    public string? ProfileOverride { get; }

    /// <summary><c>--profile</c> 参数被拒绝的原因（非 null = 启动日志记一行；实际生效配置看 <see cref="ActiveProfileName"/>）。</summary>
    public string? ProfileOverrideError { get; }

    public ConfigStoreService() : this(null) { }

    /// <param name="profileOverride">启动参数 <c>--profile</c> 的值；非法 / 配置不存在 → 忽略并回退默认。</param>
    public ConfigStoreService(string? profileOverride)
    {
        _appRoot = ResolveAppRoot();
        var (overrideName, error) = ValidateProfileOverride(_appRoot, profileOverride);
        ProfileOverride = overrideName;
        ProfileOverrideError = error;
        _profileRoot = ResolveProfileRoot(_appRoot);
    }

    /// <summary>--profile 覆盖校验：名字合法 + 配置目录已存在，两个都满足才生效（防手滑打错名建出空配置）。</summary>
    private static (string? Override, string? Error) ValidateProfileOverride(string appRoot, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        if (!IsValidProfileName(raw)) return (null, $"--profile 参数非法：{raw}");
        if (!Directory.Exists(Path.Combine(appRoot, ProfilesFolderName, raw)))
            return (null, $"--profile 指定的配置不存在：{raw}");
        return (raw, null);
    }

    // ==================== 路径 ====================

    /// <summary>应用根（`NEWTOYS_HOME` 可覆盖）；`profiles\` 与 `active-profile.txt` 都在这层。</summary>
    public string AppRootDirectory => _appRoot;

    /// <summary>所有配置的父目录 <c>&lt;应用根&gt;\profiles</c>。</summary>
    public string ProfilesRootDirectory
    {
        get
        {
            var d = Path.Combine(_appRoot, ProfilesFolderName);
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>当前生效的配置名（v3.26：启动参数 --profile 优先；其次读 active-profile.txt；缺失 / 非法 → default）。</summary>
    public string ActiveProfileName => ProfileOverride ?? ReadActiveProfileName(_appRoot);

    /// <summary>全局默认指针（`active-profile.txt`）指向的配置名；⛔ 不受 --profile 覆盖影响（给重命名判指针用）。</summary>
    public string PointerProfileName => ReadActiveProfileName(_appRoot);

    /// <summary>当前配置的根目录 <c>&lt;应用根&gt;\profiles\&lt;当前配置&gt;</c>。</summary>
    public string ProfileRootDirectory => _profileRoot;

    /// <summary>旧结构迁移失败时的原因（非 null → 本次退回旧结构运行，仅在「设置 → 配置」提示）。</summary>
    public string? ProfileMigrationError { get; private set; }

    /// <summary>&lt;配置根&gt;\config —— 应用级配置（app.json / theme.json / launch.json …）。</summary>
    public string ConfigDir => EnsureDir("config");

    /// <summary>&lt;配置根&gt;\data —— 可重建的缓存（`newtoys.db` 等），不进备份。</summary>
    public string DataDir => EnsureDir("data");

    /// <summary>&lt;配置根&gt;\logs —— 崩溃日志等。</summary>
    public string LogsDirectory => EnsureDir("logs");

    private string EnsureDir(string name)
    {
        var d = Path.Combine(_profileRoot, name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ==================== 配置管理（供 IProfileService 用） ====================

    /// <summary>列出全部配置名（`profiles\` 下的目录名）。</summary>
    public List<string> ListProfiles()
    {
        var root = Path.Combine(_appRoot, ProfilesFolderName);
        var list = new List<string>();
        if (Directory.Exists(root))
            foreach (var d in new DirectoryInfo(root).GetDirectories())
                if (!d.Name.StartsWith('.')) list.Add(d.Name);
        if (list.Count == 0) list.Add(ActiveProfileName);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>把当前配置指向 <paramref name="name"/>（**只写指针文件**，不重启、不迁移）。</summary>
    public void SetActiveProfile(string name)
    {
        if (!IsValidProfileName(name)) throw new ArgumentException("非法的配置名", nameof(name));
        Directory.CreateDirectory(Path.Combine(_appRoot, ProfilesFolderName, name));
        File.WriteAllText(Path.Combine(_appRoot, ActiveProfileFileName), name);
    }

    /// <summary>某个配置的根目录（不依赖“当前”）。</summary>
    public string GetProfileDirectory(string name)
        => Path.Combine(_appRoot, ProfilesFolderName, name);

    /// <summary>
    /// 配置名合法性：非空、非 `.`/`..`、不含路径非法字符。
    /// ⚠️ 这是**防路径穿越**的第一道闸（`profiles\..\..` 这种名字必须被挡住）。
    /// </summary>
    public static bool IsValidProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var t = name.Trim();
        if (t == "." || t == "..") return false;
        if (t != name) return false;                       // 首尾空格直接判非法，避免出现两个"看起来一样"的配置
        if (t.Contains('\\') || t.Contains('/')) return false;
        foreach (var c in Path.GetInvalidFileNameChars())
            if (t.IndexOf(c) >= 0) return false;
        return true;
    }

    // ==================== 内部：根目录 / 配置目录 / 迁移 ====================

    private static string ResolveAppRoot()
    {
        // 环境变量 NEWTOYS_HOME 可把「应用根」整体换掉（测试环境 / 便携版 / 只要换数据）。
        var env = Environment.GetEnvironmentVariable("NEWTOYS_HOME");
        if (!string.IsNullOrEmpty(env)) return env;

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewToys");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 决定本次用哪个配置目录；并在**首次升级**那一次，把旧的
    /// <c>&lt;应用根&gt;\config|data|logs</c> 整体搬进 <c>profiles\default\</c>。
    /// <para>
    /// ⚠️ 迁移失败**绝不删数据**：退回旧结构继续跑，并在「设置 → 配置」提示；
    /// 且**不创建 `profiles\` 目录**，以便下次启动重试（自愈）。
    /// </para>
    /// </summary>
    private string ResolveProfileRoot(string appRoot)
    {
        var profilesRoot = Path.Combine(appRoot, ProfilesFolderName);
        var legacyConfig = Path.Combine(appRoot, "config");

        if (!Directory.Exists(profilesRoot) && Directory.Exists(legacyConfig))
        {
            try
            {
                var target = Path.Combine(profilesRoot, DefaultProfileName);
                Directory.CreateDirectory(target);
                foreach (var sub in new[] { "config", "data", "logs" })
                {
                    var src = Path.Combine(appRoot, sub);
                    if (!Directory.Exists(src)) continue;
                    var dst = Path.Combine(target, sub);
                    if (Directory.Exists(dst)) continue;   // 目标已有 → 不覆盖
                    Directory.Move(src, dst);              // 同盘 rename，秒级
                }
                File.WriteAllText(Path.Combine(appRoot, ActiveProfileFileName), DefaultProfileName);
            }
            catch (Exception ex)
            {
                ProfileMigrationError = ex.Message;
                return appRoot;                            // 退回旧结构（config/data/logs 就在应用根下）
            }
        }

        var name = ProfileOverride ?? ReadActiveProfileName(appRoot);
        var dir = Path.Combine(profilesRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ReadActiveProfileName(string appRoot)
    {
        try
        {
            var f = Path.Combine(appRoot, ActiveProfileFileName);
            if (File.Exists(f))
            {
                var name = File.ReadAllText(f).Trim();
                if (IsValidProfileName(name)) return name;
            }
        }
        catch { /* 读不到就用默认 */ }
        return DefaultProfileName;
    }

    // ==================== 备份 / 导入（沿用 v1.x） ====================

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
