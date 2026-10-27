using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace NewToys.Core.Services.DataMigrations;

/// <summary>
/// 存储分层迁移（一次性，幂等）：<c>data\</c> → <c>store\</c> + <c>cache\</c>，并把老的单一总库
/// <c>store\newtoys.db</c> 拆成每模块一个库文件（<c>store\&lt;库键&gt;\&lt;库键&gt;.db</c>）。
/// <para>
/// 🔴 目录改名用 <see cref="Directory.Move(string, string)"/>：同一配置档内换名 = 秒级 rename，⛔ 不复制数据。
/// 拆库才需要真正读一遍行（<c>ATTACH</c> + <c>INSERT INTO … SELECT</c>），每库一个事务。
/// </para>
/// <para>
/// 🔴 老库**只改名不删**：搬完落到 <c>store\legacy\newtoys-v1.db</c>，装回旧版本 / 手工核对都靠它。
/// 完成标记 <c>store\.layout-v2.done</c> —— 有标记就整段跳过，所以中途崩了下次启动会接着做。
/// </para>
/// <para>
/// ⚠️ 这里内置 4 个库的表名归属：迁移是**一次性的历史动作**，需要静态清单才能在不依赖模块加载顺序的前提下搬数据。
/// 新增表不需要改这里（新表随建表发生在各自的库里）。
/// </para>
/// </summary>
public static class StorageLayoutV2
{
    private const string MarkerFile = ".layout-v2.done";
    private const string LegacyDbName = "newtoys.db";

    /// <summary>「配置值路径重写」独立标记（与布局标记分开：布局早就搬完的用户也要补跑这次）。</summary>
    private const string PathsMarker = ".layout-v2-paths.done";

    private static readonly JsonSerializerOptions JsonIndented = new() { WriteIndented = true };

    /// <summary>旧总库里各表的归属（库键 → 文件名 → 表名）。</summary>
    private static readonly (string Key, string File, string[] Tables)[] Libraries =
    {
        ("assets", "assets.db", new[]
        {
            "storages", "assets", "file_locations", "tags", "asset_tags",
            "categories", "asset_categories", "collections", "collection_assets",
            "relations", "operation_logs", "scan_jobs", "scan_errors",
        }),
        ("video", "video.db", new[] { "video_media", "video_docs" }),
        ("web", "web.db", new[] { "web_docs" }),
        ("script", "script.db", new[] { "script_docs", "pc_observations", "pc_runs" }),
    };

    /// <summary>执行迁移（⛔ 不抛：失败只记调试日志，下次启动重试）。</summary>
    public static void Run(ConfigStoreService cfg)
    {
        try
        {
            // 配置值里的旧绝对路径独立于布局标记：目录早就搬完的用户，配置里仍指着 data\ ⇒ 每次启动先校一遍。
            RewriteLegacyConfigPaths(cfg);

            var profileRoot = cfg.ProfileRootDirectory;
            var storeDir = Path.Combine(profileRoot, "store");
            var cacheDir = Path.Combine(profileRoot, "cache");
            var marker = Path.Combine(storeDir, MarkerFile);

            if (File.Exists(marker)) return;

            // ① data\ → store\（整目录改名；已有 store\ 说明上次做到一半，逐项并入）
            var legacyData = Path.Combine(profileRoot, "data");
            if (Directory.Exists(legacyData))
            {
                if (!Directory.Exists(storeDir)) Directory.Move(legacyData, storeDir);
                else MergeInto(legacyData, storeDir);
            }

            Directory.CreateDirectory(storeDir);
            Directory.CreateDirectory(cacheDir);

            // ② 拆库（逐表判断「目标表为空才搬」，目标库已存在也照做 ⇒ 幂等）
            var legacyDb = Path.Combine(storeDir, LegacyDbName);
            if (File.Exists(legacyDb)) SplitDatabase(legacyDb, storeDir, archiveDir: Path.Combine(storeDir, "legacy"));

            // ②-b 抓取目录元数据：拆库前它存在 web_docs（抓取能力原属网络工具，后整体迁到脚本模块）
            //      ⇒ 现在归脚本库的 script_docs。web 库里那一行留着不动（无人再读写）。
            MoveScrapeTaskDocs(storeDir);

            // ③ 可重建内容归 cache\（配置了自定义缩略图目录 ⇒ 不动）
            if (!HasCustomThumbsDirectory(cfg))
                MoveIfExists(Path.Combine(storeDir, "thumbs"), Path.Combine(cacheDir, "assets", "thumbs"));
            MoveIfExists(Path.Combine(storeDir, "script", "cache"), Path.Combine(cacheDir, "script", "page"));
            MoveIfExists(Path.Combine(storeDir, "script", "downloads"), Path.Combine(cacheDir, "script", "downloads"));
            MoveIfExists(Path.Combine(storeDir, "script", "trials"), Path.Combine(cacheDir, "script", "trials"));
            MoveIfExists(Path.Combine(storeDir, "backups"), Path.Combine(cacheDir, "db-backups"));

            // ④ 老残留 store\web-tools\* → store\script\*（这两个目录早期是网络工具的，已归脚本模块）
            foreach (var sub in new[] { "rules", "flows", "runs" })
                MoveIfExists(Path.Combine(storeDir, "web-tools", sub), Path.Combine(storeDir, "script", sub));

            File.WriteAllText(marker,
                $"v2 · {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                "profile 目录已分层：store\\（用户数据）/ cache\\（可重建）。\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][migrate] 存储分层迁移失败（下次启动重试）：" + ex.Message);
        }
    }

    /// <summary>
    /// 把**配置里的旧绝对路径**（<c>&lt;配置根&gt;\data\…</c>）改成新位置（<c>store\</c> / <c>cache\</c>）。
    /// <para>
    /// 目录整体改名不会顺手改用户录入的绝对路径（<c>scriptsFolder</c> / <c>thumbsDirectory</c> 等），
    /// 这类值迁移后就指向不存在的目录 ⇒ 扫描 / 缩略图失效。只改「映射后的目录确实存在」的值；
    /// 其余（别的机器、离线移动盘）原样保留。
    /// </para>
    /// </summary>
    private static void RewriteLegacyConfigPaths(ConfigStoreService cfg)
    {
        string marker;
        try { marker = Path.Combine(cfg.StoreDir, PathsMarker); }
        catch { return; }
        if (File.Exists(marker)) return;

        var rewritten = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(cfg.ConfigDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    if (!text.Contains(@"\data\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (JsonNode.Parse(text) is not JsonObject root) continue;
                    if (!RewriteNode(root, cfg)) continue;

                    AtomicJsonFile.Write(file, root.ToJsonString(JsonIndented));
                    rewritten++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[NewToys][migrate] 配置路径重写失败（跳过）：{file}：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][migrate] 配置目录遍历失败：" + ex.Message);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, $"v2-paths · {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n改写 {rewritten} 个配置文件。\n");
        }
        catch { /* 标记写不出 ⇒ 下次启动再跑一遍（幂等） */ }
    }

    /// <summary>深度改写：把对象 / 数组里所有 <c>&lt;配置根&gt;\data\…</c> 字符串值换成新位置。</summary>
    private static bool RewriteNode(JsonNode node, ConfigStoreService cfg)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var value = obj[key];
                    if (value is JsonValue v && v.TryGetValue<string>(out var s) && MapLegacyPath(s, cfg) is { } mapped)
                    {
                        obj[key] = mapped;
                        changed = true;
                    }
                    else if (value != null)
                    {
                        changed |= RewriteNode(value, cfg);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var value = arr[i];
                    if (value is JsonValue v && v.TryGetValue<string>(out var s) && MapLegacyPath(s, cfg) is { } mapped)
                    {
                        arr[i] = mapped;
                        changed = true;
                    }
                    else if (value != null)
                    {
                        changed |= RewriteNode(value, cfg);
                    }
                }
                break;
        }
        return changed;
    }

    /// <summary><c>&lt;配置根&gt;\data\&lt;rest&gt;</c> → <c>store\&lt;rest&gt;</c> / <c>cache\&lt;rest&gt;</c>（目录存在才认）。</summary>
    private static string? MapLegacyPath(string value, ConfigStoreService cfg)
    {
        try
        {
            var prefix = Path.Combine(cfg.ProfileRootDirectory, "data") + Path.DirectorySeparatorChar;
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var rest = value[prefix.Length..];
            foreach (var root in new[] { cfg.StoreDir, cfg.CacheDir })
            {
                var candidate = Path.Combine(root, rest);
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch { /* 路径非法 ⇒ 不改 */ }
        return null;
    }

    /// <summary>把 <paramref name="src"/> 下的每一项并进 <paramref name="dst"/>（同名已存在 ⇒ 旧项改名 <c>.legacy</c>）。</summary>
    private static void MergeInto(string src, string dst)
    {
        try
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.EnumerateDirectories(src))
            {
                var target = Path.Combine(dst, Path.GetFileName(dir));
                if (Directory.Exists(target)) MergeInto(dir, target);
                else Directory.Move(dir, target);
            }
            foreach (var file in Directory.EnumerateFiles(src))
            {
                var target = Path.Combine(dst, Path.GetFileName(file));
                if (File.Exists(target)) File.Move(file, target + ".legacy", overwrite: true);
                else File.Move(file, target);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewToys][migrate] 目录并入失败：{src} → {dst}：{ex.Message}");
        }
    }

    /// <summary>目录搬家：目标已存在 ⇒ 逐项并入；不存在 ⇒ 整目录 erase-free 改名。</summary>
    private static void MoveIfExists(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from)) return;
            if (!Directory.Exists(to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                Directory.Move(from, to);
                return;
            }
            MergeInto(from, to);                 // 目标已存在 ⇒ 逐项并入
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewToys][migrate] 目录搬家失败：{from} → {to}：{ex.Message}");
        }
    }

    /// <summary>用户给媒体资产配了自定义缩略图目录 ⇒ 默认那套不搬（免得搬走正在用的图）。</summary>
    private static bool HasCustomThumbsDirectory(ConfigStoreService cfg)
    {
        try
        {
            var path = Path.Combine(cfg.ModulesConfigDir, "media-assets.json");
            if (!File.Exists(path)) path = Path.Combine(cfg.ConfigDir, "media-assets.json");
            if (!File.Exists(path)) return false;

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject obj) return false;
            var node = obj["ThumbsDirectory"] ?? obj["thumbsDirectory"];
            return node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s);
        }
        catch { return false; }
    }

    /// <summary>
    /// 把旧总库里的表按归属复制进各模块库。
    /// <para>
    /// 🔴 以**新库**为主连接、<c>ATTACH</c> 旧库来读：这样 DDL 与索引天然建在新库里，
    /// 不需要给每条语句手工加 schema 前缀（反过来做会把索引建回旧库）。
    /// </para>
    /// </summary>
    private static void SplitDatabase(string legacyDb, string storeDir, string archiveDir)
    {
        foreach (var (key, file, tables) in Libraries)
        {
            var target = Path.Combine(storeDir, key, file);
            // ⚠️ 目标库已存在也照做：模块服务可能在迁移之前就把空库建出来了。
            // 逐表判断「目标表为空才搬」⇒ 幂等，既不会丢数据也不会重复插。

            SqliteConnection.ClearAllPools();           // 后面要改旧库名，先放开所有句柄

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = target,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                }.ToString());
                conn.Open();

                using (var setup = conn.CreateCommand())
                {
                    setup.CommandText = $"PRAGMA busy_timeout=5000; ATTACH DATABASE '{legacyDb.Replace("'", "''")}' AS old;";
                    setup.ExecuteNonQuery();
                }

                foreach (var table in tables)
                {
                    var ddl = ReadDdl(conn, table);
                    if (ddl is null) continue;           // 旧库里没这张表 ⇒ 跳过

                    using (var create = conn.CreateCommand())
                    {
                        create.CommandText = MakeIdempotent(ddl);
                        create.ExecuteNonQuery();
                    }

                    if (CountRows(conn, $"main.\"{table}\"") > 0) continue;   // 已经搬过 ⇒ 不重复插

                    using (var copy = conn.CreateCommand())
                    {
                        copy.CommandText = $"INSERT INTO main.\"{table}\" SELECT * FROM old.\"{table}\";";
                        copy.ExecuteNonQuery();
                    }

                    foreach (var indexDdl in ReadIndexDdl(conn, table))
                    {
                        using var createIndex = conn.CreateCommand();
                        createIndex.CommandText = MakeIdempotent(indexDdl);
                        try { createIndex.ExecuteNonQuery(); } catch { /* 索引已存在 / 非法 ⇒ 不阻断搬迁 */ }
                    }
                }

                using (var detach = conn.CreateCommand())
                {
                    detach.CommandText = "DETACH DATABASE old;";
                    detach.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NewToys][migrate] 拆库失败（{key}）：{ex.Message}");
            }
        }

        // 老库只改名不删（装回旧版本 / 手工核对靠它）
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(archiveDir);
            File.Move(legacyDb, Path.Combine(archiveDir, LegacyDbName), overwrite: true);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var side = legacyDb + suffix;
                if (File.Exists(side)) File.Move(side, Path.Combine(archiveDir, LegacyDbName + suffix), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][migrate] 旧总库归档失败（不影响新库）：" + ex.Message);
        }
    }

    /// <summary>从 ATTACH 的旧库取建表 DDL（表不存在 ⇒ null）。</summary>
    private static string? ReadDdl(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM old.sqlite_master WHERE type='table' AND name=$n;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>表行数（表不存在 ⇒ 0）。</summary>
    private static long CountRows(SqliteConnection conn, string qualifiedTable)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {qualifiedTable};";
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>把 <c>CREATE TABLE / CREATE [UNIQUE] INDEX</c> 改写成带 <c>IF NOT EXISTS</c> 的幂等语句。</summary>
    private static string MakeIdempotent(string ddl)
    {
        foreach (var (from, to) in new[]
                 {
                     ("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS "),
                     ("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS "),
                     ("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS "),
                 })
        {
            if (ddl.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase)) return ddl;
            var i = ddl.IndexOf(from, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return ddl[..i] + to + ddl[(i + from.Length)..];
        }
        return ddl;
    }

    /// <summary>从 ATTACH 的旧库取该表的索引 DDL（自动索引没有 sql，跳过）。</summary>
    private static List<string> ReadIndexDdl(SqliteConnection conn, string table)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM old.sqlite_master WHERE type='index' AND tbl_name=$n AND sql IS NOT NULL;";
        cmd.Parameters.AddWithValue("$n", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(0) is { Length: > 0 } sql) list.Add(sql);
        }
        return list;
    }

    /// <summary>
    /// 把抓取目录元数据（文档名 <c>scrape-tasks</c>）从 <c>web.db</c> 的 <c>web_docs</c>
    /// 复制到 <c>script.db</c> 的 <c>script_docs</c>（表结构相同，只差表名）。
    /// <para>幂等：<c>INSERT OR IGNORE</c>，重复启动不会覆盖脚本库里已有的一份。</para>
    /// </summary>
    private static void MoveScrapeTaskDocs(string storeDir)
    {
        var webDb = Path.Combine(storeDir, "web", "web.db");
        var scriptDb = Path.Combine(storeDir, "script", "script.db");
        if (!File.Exists(webDb) || !File.Exists(scriptDb)) return;

        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = scriptDb,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
            conn.Open();

            using (var setup = conn.CreateCommand())
            {
                setup.CommandText =
                    "CREATE TABLE IF NOT EXISTS script_docs (name TEXT PRIMARY KEY, payload TEXT NOT NULL, updated_at TEXT NOT NULL); " +
                    $"ATTACH DATABASE '{webDb.Replace("'", "''")}' AS w;";
                setup.ExecuteNonQuery();
            }

            using (var copy = conn.CreateCommand())
            {
                copy.CommandText =
                    "INSERT OR IGNORE INTO main.script_docs (name, payload, updated_at) " +
                    "SELECT name, payload, updated_at FROM w.web_docs WHERE name = 'scrape-tasks';";
                copy.ExecuteNonQuery();
            }

            using var detach = conn.CreateCommand();
            detach.CommandText = "DETACH DATABASE w;";
            detach.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][migrate] 抓取目录元数据搬家失败（不影响启动）：" + ex.Message);
        }
    }
}
