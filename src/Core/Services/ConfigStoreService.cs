using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>「应用根 → 配置（profile）→ config/store/cache/logs」路径的唯一决定者。（WHY: CR-008）</summary>
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
        // ⛔ 构造函数只算路径、不碰磁盘：一次性迁移 / 清理都走 InitializeDeferred（启动热路径上省 IO）。
    }

    private bool _deferredDone;

    /// <summary>
    /// 一次性磁盘整理（首帧之后由 <c>ModuleLoader.InitializeAllDeferred</c> 调用，幂等）：
    /// 框架级小文件并入 <c>shell.json</c> / 模块设置归位 <c>config\modules\</c> / 清掉空壳配置。
    /// <para>⚠️ 内部 try/catch：失败只记调试日志，读方各自的兜底路径仍能工作，⛔ 不拦启动。</para>
    /// </summary>
    public void InitializeDeferred()
    {
        if (_deferredDone) return;
        _deferredDone = true;

        EnsureShellMigrated();        // 框架级小文件 → shell.json（读方也会兜一次）
        MigrateModuleConfigFiles();   // 模块设置 → config\modules\（纯整理，只搬文件）
        PruneEmptyConfigFiles();      // 清掉内容等价于「没有配置」的空壳文件

        // 数据目录分层：data\ → store\ + cache\，并把旧总库拆成每模块一个（幂等，重入安全）
        DataMigrations.StorageLayoutV2.Run(this);
    }

    private bool _shellMigrated;

    /// <summary>把 5 份框架级小文件并进 shell.json（幂等，只跑一次）。（WHY: CR-009）</summary>
    public bool EnsureShellMigrated()
    {
        if (_shellMigrated) return false;
        _shellMigrated = true;
        return MigrateLegacyShellFiles();
    }

    /// <summary>模块设置文件名（不含扩展名）—— 全部迁进 <c>config\modules\</c>。</summary>
    private static readonly string[] ModuleConfigNames =
    {
        "script-manager", "media-assets", "video-tools", "web-tools",
        // ⚠️ package.json 是「软件包管理」（script-manager 模块）的设置数据，同属模块级。
        "package",
    };

    /// <summary>一次性迁移：config\&lt;模块&gt;.json → config\modules\&lt;模块&gt;.json。（WHY: CR-010）</summary>
    private void MigrateModuleConfigFiles()
    {
        try
        {
            var src = ConfigDir;
            var dst = ModulesConfigDir;

            foreach (var name in ModuleConfigNames)
            {
                var from = Path.Combine(src, name + ".json");
                if (!File.Exists(from)) continue;

                var to = Path.Combine(dst, name + ".json");
                if (File.Exists(to))
                {
                    File.Move(from, from + ".legacy", overwrite: true);   // ⛔ 不覆盖目标，旧文件留档
                    continue;
                }

                File.Move(from, to);
                System.Diagnostics.Debug.WriteLine($"[NewToys][迁移] 模块设置归位：{from} → {to}");
            }
        }
        catch (Exception ex)
        {
            // 搬不动（被占用 / 只读）不影响启动：读方在旧位置还能读到（见各服务的 FirstExisting 兜底）
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] 模块设置归位失败（下次启动重试）：" + ex.Message);
        }
    }

    /// <summary>清掉等价于默认值的配置文件（内容只有 {} / [] / null / 空串）。（WHY: CR-011）</summary>
    private void PruneEmptyConfigFiles()
    {
        try
        {
            var dir = ConfigDir;
            if (!Directory.Exists(dir)) return;

            foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var rel = Path.GetRelativePath(dir, f);
                    var segments = rel.Split(Path.DirectorySeparatorChar, '/');
                    var hidden = false;
                    foreach (var seg in segments)
                        if (seg.Length > 0 && seg[0] == '.') { hidden = true; break; }
                    if (hidden) continue;

                    if (AtomicJsonFile.IsEmpty(File.ReadAllText(f))) File.Delete(f);
                }
                catch { /* 单个文件失败（被占用 / 只读）⇒ 跳过，下次启动再试 */ }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][清理] 空配置文件清理失败：" + ex.Message);
        }
    }

    /// <summary>一次性迁移：把 5 份框架级小文件并进 shell.json（每份一个节）。（WHY: CR-012）</summary>
    private bool MigrateLegacyShellFiles()
    {
        try
        {
            var dir = ConfigDir;
            var target = Path.Combine(dir, "shell.json");
            var doc = LoadShellDoc(target);   // 已有就接着用（这样「导入旧备份」之后也能把旧文件补并进来）
            var changed = false;

            var sources = new (string File, string Section)[]
            {
                ("theme.json", "theme"),
                ("window.json", "window"),
                ("favorites.json", "favorites"),
                ("columnlayout.json", "columnLayout"),
                ("ui.json", "ui"),
            };

            foreach (var (file, section) in sources)
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) continue;

                if (doc.ContainsKey(section))
                {
                    // 新格式里已有这一节（迁移过 / 用户改过）⇒ 旧文件只是残留：归档，⛔ 不覆盖
                    ArchiveLegacyFile(path);
                    continue;
                }

                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(path), nodeOptions: null,
                        documentOptions: new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip
                        });
                    if (node is null) continue;

                    doc[section] = node;
                    changed = true;
                    ArchiveLegacyFile(path);
                }
                catch { /* 坏文件不搬：原样留着，由用户自查 */ }
            }

            if (!changed) return false;

            AtomicJsonFile.Write(target, doc.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] 框架级配置已并入 shell.json");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][迁移] shell.json 合并失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>读已有 <c>shell.json</c>（不存在 / 坏了 ⇒ 空文档，⛔ 不抛）。</summary>
    private static JsonObject LoadShellDoc(string path)
    {
        try
        {
            if (!File.Exists(path)) return new JsonObject();
            if (JsonNode.Parse(File.ReadAllText(path), nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    }) is JsonObject obj)
                return obj;
        }
        catch { /* 坏文件：按空文档处理，下次保存会覆盖（内容已在 .bak 里） */ }
        return new JsonObject();
    }

    /// <summary>旧配置文件读完后改名 <c>.legacy</c>（⛔ 不删；失败不影响主流程）。</summary>
    private static void ArchiveLegacyFile(string path)
    {
        try { File.Move(path, path + ".legacy", overwrite: true); }
        catch { /* 改名失败不影响新文件已写出，下次启动再试 */ }
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

    /// <summary>&lt;配置根&gt;\config —— 本配置档的配置（shell.json / 各模块设置 …；应用行为在应用根 app.json）。
    /// <para>⚠️ v3.65：<c>app.json</c>（运行环境）**不在**这里 —— 它上提到应用根（跨配置共享）。</para></summary>
    public string ConfigDir => EnsureDir("config");

    /// <summary>
    /// &lt;配置根&gt;\store —— **模块数据**（各模块自有库 + 规则 / 流程 / 运行记录 / 脚本实体）。
    /// <para>语义 = 删了不可恢复 ⇒ 「设置 → 数据管理」默认进备份。</para>
    /// </summary>
    public string StoreDir => EnsureDir("store");

    /// <summary>
    /// &lt;配置根&gt;\cache —— **可重建**（缩略图 / 页面缓存 / 下载中间产物 / 库升级前快照 / 恢复副本）。
    /// <para>语义 = 随便删 ⇒ 不进备份。</para>
    /// </summary>
    public string CacheDir => EnsureDir("cache");

    /// <summary>&lt;配置根&gt;\config\modules —— 模块设置的统一落点。</summary>
    public string ModulesConfigDir => EnsureModulesDir();

    private string EnsureModulesDir()
    {
        var d = Path.Combine(ConfigDir, ModulesSubDirName);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>模块设置子目录名（<c>config\modules</c>）。</summary>
    private const string ModulesSubDirName = "modules";

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

    // ==================== 内置三档已撤销（v3.66：2026-10-18） ====================
    // 「配置」= **纯根目录**：一个配置 = 一套独立的 config / data / logs，由用户自建自删。
    // ⛔ 不再有 debug / default / release 的档位语义（原 的播种与「重置为初始态」全部删除），
    //    ⛔ 也不自动创建任何配置目录 —— 除了首次启动缺省的那个（ResolveProfileRoot 负责）。
    // 依据：档位语义会让"切换配置"变成两件事（换根 + 换身份），而真正需要的只是换根。

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

    /// <summary>决定本次用哪个配置目录；并在首次升级那一次，把旧的 &lt;应用根&gt;\config|data|logs 整体搬进 profiles\default\。（WHY: CR-014）</summary>
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

    /// <summary>导入 v1 旧格式（单 JSON）备份 —— 只保留只读兼容，新备份走 BackupService 的 zip 分节格式。（WHY: CR-015）</summary>
    public (int ok, int skipped, string? backupDir) ImportBackup(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Files", out var filesEl) && !root.TryGetProperty("files", out filesEl))
            throw new InvalidDataException("备份文件缺少 Files 节点");

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
            var rel = (GetString(e, "RelativePath") ?? GetString(e, "path") ?? "")
                .Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(rel)) { skipped++; continue; }
            var full = Path.GetFullPath(Path.Combine(ConfigDir, rel));
            // 路径穿越防御（⚠️ 必须比到 `config\` 这一级：只 StartsWith(ConfigDir) 会放过 `config2\...`）
            if (!full.StartsWith(ConfigDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
            var content = GetString(e, "Content") ?? GetString(e, "content") ?? "";
            // 恢复也走原子写（每个文件独立 rename，中断时最多剩一份半成品tmp）
            AtomicJsonFile.Write(full, content);
            ok++;
        }
        return (ok, skipped, bak);
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
