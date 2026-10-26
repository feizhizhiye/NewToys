using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// 模块自有 SQLite 库的入口：**一个模块一个库文件**，落 <c>&lt;配置根&gt;\store\&lt;库键&gt;\&lt;库键&gt;.db</c>。
/// <para>
/// 为什么放 <c>store\</c> 而不是 <c>config\</c>：<c>config\</c> 是「人手能看懂能改的 json」，
/// 而这里的表是程序写的数据（部分不可重建，如标签 / 分类 / 集合）——
/// 目录语义按「能不能删」分家：<c>store\</c> 删了就丢、<c>cache\</c> 随便删。
/// </para>
/// <para>
/// 本类只做四件事：定位库文件、建库、发连接、**按模块版本记账**（见 <see cref="ApplySchema"/>）。
/// 它**不认识任何业务表**，因此新增数据表不需要动它。
/// </para>
/// <para>
/// ⚠️ 取连接用 <see cref="Open"/>，用完即 <c>Dispose</c>（连接池由 Microsoft.Data.Sqlite 自己管）。
/// 单库写入很轻（只在 UI 线程上按批落库），不做跨线程互斥；<c>busy_timeout</c> 兜住偶发争用。
/// </para>
/// </summary>
public class SqliteDatabase
{
    private readonly ConfigStoreService _store;

    /// <summary>库键（= <c>store\</c> 下的子目录名）。</summary>
    public string ModuleKey { get; }

    /// <summary>数据库文件全路径（界面提示 / 备份 / 快照用）。</summary>
    public string DatabasePath { get; }

    /// <summary>数据库是否可用（库建不出来时为 false，依赖它的页面应给出自己的提示而不是崩）。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>建库失败的原因（<see cref="IsAvailable"/> 为 false 时有效）。</summary>
    public string LastError { get; private set; } = "";

    /// <param name="store">路径唯一决定者。</param>
    /// <param name="moduleKey">库键，同时是 <c>store\</c> 下的子目录名。</param>
    /// <param name="fileName">库文件名（含扩展名，如 <c>assets.db</c>）。</param>
    public SqliteDatabase(ConfigStoreService store, string moduleKey, string fileName)
    {
        _store = store;
        ModuleKey = moduleKey;
        DatabasePath = Path.Combine(store.StoreDir, moduleKey, fileName);
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var conn = Open();

            // 每个库独立文件，按库开 WAL（读不阻塞写）。
            // ⚠️ 副作用：db 旁边会多出 -wal / -shm，**备份必须用 VACUUM INTO**，⛔ 不能直接 File.Copy（会漏未落盘事务）。
            // 这两个 PRAGMA 是持久化 / 连接级设置，失败（只读盘 / 网络盘）不影响后续建表，故单独 try。
            try
            {
                using var pragma = conn.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[NewToys][sqlite] WAL 开启失败（按默认 journal 继续）：" + ex.Message);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS meta (" +
                "  key TEXT PRIMARY KEY," +
                "  value TEXT NOT NULL" +
                ");";
            cmd.ExecuteNonQuery();
            IsAvailable = true;
            LastError = "";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LastError = ex.Message;
        }
    }

    /// <summary>
    /// 按模块版本记账地跑一次 schema：读 <c>meta.schema.&lt;模块键&gt;</c> → 需要升级时先
    /// <c>VACUUM INTO cache\db-backups\</c> 留快照 → 执行 <paramref name="ensureTables"/> → 回写版本。
    /// <para>
    /// 🔴 快照只在「版本要变」时做：每次启动都 VACUUM 会白白写几十 MB。
    /// 库版本高于代码版本（旧软件打开新库）⇒ ⛔ 不执行 DDL、⛔ 不改版本号。
    /// </para>
    /// <para>⛔ 不抛：任何失败都归成 <see cref="SchemaOutcome.Failed"/>，由调用方汇总。</para>
    /// </summary>
    /// <param name="moduleKey">模块键（版本记录键后缀）。</param>
    /// <param name="version">代码里的目标版本。</param>
    /// <param name="ensureTables">幂等 DDL（<c>CREATE IF NOT EXISTS</c> / <c>ALTER ADD COLUMN</c>）。</param>
    public SchemaOutcome ApplySchema(string moduleKey, int version, Action ensureTables)
    {
        if (!IsAvailable) return SchemaOutcome.Failed;

        try
        {
            var metaKey = "schema." + moduleKey;
            var hasStored = int.TryParse(GetMeta(metaKey), out var stored);
            var needsUpgrade = !hasStored || stored < version;

            if (hasStored && stored > version) return SchemaOutcome.Ahead;

            // 只有「库里有旧版本数据」才值得快照；首次建库（含迁移刚拆出来的库）快照出来是空文件。
            if (hasStored && stored < version)
            {
                var snap = VacuumInto(Path.Combine(_store.CacheDir, "db-backups"),
                    $"{Path.GetFileNameWithoutExtension(DatabasePath)}-pre-v{version}");
                if (snap is null)
                    System.Diagnostics.Debug.WriteLine(
                        $"[NewToys][schema] {moduleKey} 升级前快照失败（继续执行，⛔ 请勿手工清库）");
            }

            ensureTables();

            if (needsUpgrade) SetMeta(metaKey, version.ToString());
            SetUserVersion(version);

            return needsUpgrade ? SchemaOutcome.Applied : SchemaOutcome.UpToDate;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[NewToys][schema] {moduleKey} 建表失败：{ex.Message}");
            return SchemaOutcome.Failed;
        }
    }

    /// <summary>读 <c>meta</c> 表（键不存在 / 库不可用 / 读失败 ⇒ null，⛔ 不抛）。</summary>
    public string? GetMeta(string key)
    {
        if (!IsAvailable) return null;
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT \"value\" FROM meta WHERE \"key\" = $k;";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        catch { return null; }
    }

    /// <summary>写 <c>meta</c> 表（upsert）。库不可用 ⇒ 静默跳过；其余异常向上抛，由调用方决定。</summary>
    public void SetMeta(string key, string value)
    {
        if (!IsAvailable) return;
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO meta (\"key\", \"value\") VALUES ($k, $v) " +
            "ON CONFLICT(\"key\") DO UPDATE SET \"value\" = excluded.\"value\";";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>写 <c>PRAGMA user_version</c>（外部工具看的库版本；按模块的版本在 <c>meta</c> 里）。</summary>
    public void SetUserVersion(int version)
    {
        if (!IsAvailable || version <= 0) return;
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {version};";   // PRAGMA 不支持参数绑定，int 已强类型
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][sqlite] user_version 写入失败：" + ex.Message);
        }
    }

    /// <summary>打开一个连接（调用方负责 Dispose）。</summary>
    public SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=3000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>
    /// 把当前库**完整快照**到 <paramref name="directory"/> 下的一个新文件。
    /// <para>
    /// 🔴 用 <c>VACUUM INTO</c> 而不是 <see cref="System.IO.File"/> 复制：WAL 模式下未 checkpoint 的事务
    /// 还在 <c>-wal</c> 里，直接拷 <c>.db</c> 拿到的是**旧快照**（甚至半截）。<c>VACUUM INTO</c> 由 SQLite 保证一致性。
    /// </para>
    /// </summary>
    /// <param name="directory">目标目录（不存在则建）。</param>
    /// <param name="prefix">文件名前缀。</param>
    /// <returns>快照全路径（失败 ⇒ null，⛔ 不抛）。</returns>
    public string? VacuumInto(string directory, string prefix = "pre-migrate")
    {
        if (!IsAvailable) return null;
        try
        {
            Directory.CreateDirectory(directory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(directory, $"{prefix}-{stamp}.db");
            for (var i = 1; File.Exists(path) && i < 100; i++)     // VACUUM INTO ⛔ 不能写已存在的文件
                path = Path.Combine(directory, $"{prefix}-{stamp}-{i}.db");

            return VacuumIntoFile(path, out _) ? path : null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            System.Diagnostics.Debug.WriteLine("[NewToys][sqlite] 快照失败（VACUUM INTO）：" + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 把当前库**在线另存**到指定文件（`VACUUM INTO`，一致快照、不锁库）。
    /// <para>
    /// 🔴 全工程唯一的「把库写成另一个 .db」实现：备份数据节与资产页「另存库文件」都走它。
    /// </para>
    /// <para>⚠️ 目标文件**必须不存在**（SQLite 限制）—— 调用方自己决定要不要先删 / 改名。</para>
    /// </summary>
    /// <returns>成功 true；失败 false 且 <paramref name="error"/> 给出原因（⛔ 不抛）。</returns>
    public bool VacuumIntoFile(string targetPath, out string error)
    {
        error = "";
        if (!IsAvailable) { error = "数据库不可用：" + LastError; return false; }
        if (string.IsNullOrWhiteSpace(targetPath)) { error = "未指定目标文件。"; return false; }

        try
        {
            var full = Path.GetFullPath(targetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $p;";      // 目标路径用参数绑定（不用拼字符串）
            cmd.Parameters.AddWithValue("$p", full);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            LastError = ex.Message;
            System.Diagnostics.Debug.WriteLine("[NewToys][sqlite] 快照失败（VACUUM INTO）：" + ex.Message);
            return false;
        }
    }

    /// <summary>建表 / 建索引等 DDL（幂等语句，功能服务在构造时调一次即可）。</summary>
    public bool EnsureTable(string ddl)
    {
        if (!IsAvailable) return false;
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}
