using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NewToys.Core.Services;

/// <summary>
/// 工程内**唯一的 SQLite 入口**：所有结构化本地数据（当前是视频库的媒体信息索引，
/// 后续更多配置项也会陆续搬进来）都落同一个库文件 <c>&lt;应用根&gt;\data\newtoys.db</c>。
/// <para>
/// 为什么放 <c>data\</c> 而不是 <c>config\</c>：<c>config\</c> 下的 <c>*.json</c> 会被
/// 「设置 → 数据管理」整份备份 / 导入，而这里的索引是**可重建的缓存**（文件没了重新探测即可），
/// 不该跟着备份来回搬（见规范 §9 对"设置数据 / 功能数据"的划分）。
/// </para>
/// <para>
/// 本类只做三件事：定位库文件、建库（schema 由各功能自己建，见 <see cref="EnsureTable"/>）、
/// 发连接。它**不认识任何业务表**，因此新增数据表不需要动它。
/// </para>
/// <para>
/// ⚠️ 取连接用 <see cref="Open"/>，用完即 <c>Dispose</c>（连接池由 Microsoft.Data.Sqlite 自己管）。
/// 并发写入很轻（只在 UI 线程上按批落库），所以不做跨线程互斥；<c>busy_timeout</c> 兜住偶发争用。
/// </para>
/// </summary>
public class SqliteDatabase
{
    private readonly ConfigStoreService _store;

    /// <summary>数据库文件全路径（界面提示用）。</summary>
    public string DatabasePath => Path.Combine(_store.DataDir, "newtoys.db");

    /// <summary>数据库是否可用（库建不出来时为 false，依赖它的页面应给出自己的提示而不是崩）。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>建库失败的原因（<see cref="IsAvailable"/> 为 false 时有效）。</summary>
    public string LastError { get; private set; } = "";

    public SqliteDatabase(ConfigStoreService store)
    {
        _store = store;
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Directory.CreateDirectory(_store.DataDir);
            using var conn = Open();

            // NT-076：总库由多模块共用 ⇒ 开 WAL（读不阻塞写，单库并发下必须）。
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

    /// <summary>写 <c>PRAGMA user_version</c>（外部工具看的总 schema 版本；分模块版本在 <c>meta</c> 里）。</summary>
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
    /// 把当前库**完整快照**到 <paramref name="directory"/> 下的一个新文件（NT-076 第二批）。
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
    /// 🔴 全工程唯一的「把库写成另一个 .db」实现：备份数据节（`DataDbBackupSection`）与
    /// 资产页「💿 另存库文件」都走它 —— 以前两处各写一份 SQL，口径容易漂。
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
