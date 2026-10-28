using System;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 备份中心自有库（<c>store\backup\backup.db</c>）：任务清单 / 归档包清单 / 运行记录 / 待办 / 密码库条目。
/// <para>
/// 为什么进 SQLite 而不是继续用四个 JSON：日历页要按天聚合「哪天备份了几次」，
/// 那是 SQL 的一句 <c>GROUP BY strftime</c>；JSON 只能每次全量读进内存再 LINQ。
/// </para>
/// <para>
/// 早期的 <c>plans.json / catalog.json / runs.json / vault.json</c>（<c>config\backup\</c>）由各服务
/// 在首次加载时**一次性**导入，导入后写 <c>meta</c> 标记（<c>migrated.&lt;名字&gt;</c>）；
/// ⛔ 不删旧文件 —— 迁移出错时还能手工翻查。
/// </para>
/// </summary>
public sealed class BackupDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "backup";

    /// <summary>库文件名。</summary>
    public const string FileName = "backup.db";

    /// <summary>代码里的 schema 目标版本（加表 / 加列时 +1）。</summary>
    private const int SchemaVersion = 1;

    public BackupDb(ConfigStoreService store) : base(store, Key, FileName)
    {
        Outcome = ApplySchema(Key, SchemaVersion, EnsureTables);
    }

    /// <summary>本次 schema 检查的结果（<see cref="SchemaOutcome.Failed"/> ⇒ 界面上要如实说不可用）。</summary>
    public SchemaOutcome Outcome { get; }

    /// <summary>库可用且表已就绪。</summary>
    public bool IsReady => IsAvailable && Outcome != SchemaOutcome.Failed;

    /// <summary>某个一次性迁移是否已经做过。</summary>
    public bool IsMigrated(string name) => GetMeta("migrated." + name) == "1";

    /// <summary>标记一次性迁移已完成。</summary>
    public void MarkMigrated(string name)
    {
        try { SetMeta("migrated." + name, "1"); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[NewToys][backup] 迁移标记写入失败：" + ex.Message); }
    }

    private void EnsureTables()
    {
        const string ddl =
            // 备份任务：sources 存 JSON 数组（源目录数量少、只整体读写，不值得再拆一张表）
            "CREATE TABLE IF NOT EXISTS plans (" +
            "  id TEXT PRIMARY KEY, name TEXT NOT NULL, sources TEXT NOT NULL DEFAULT '[]'," +
            "  excludes TEXT NOT NULL DEFAULT '', keep_count INTEGER NOT NULL DEFAULT 5," +
            "  note TEXT NOT NULL DEFAULT '', sort_order INTEGER NOT NULL DEFAULT 0);" +

            // 归档包清单
            "CREATE TABLE IF NOT EXISTS archives (" +
            "  file_name TEXT PRIMARY KEY, plan_id TEXT NOT NULL DEFAULT '', plan_name TEXT NOT NULL DEFAULT ''," +
            "  size_bytes INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, sha256 TEXT NOT NULL DEFAULT '');" +
            "CREATE INDEX IF NOT EXISTS ix_archives_created ON archives(created_at);" +

            // 运行记录（日历页按 started_at 的日期分组）
            "CREATE TABLE IF NOT EXISTS runs (" +
            "  id INTEGER PRIMARY KEY AUTOINCREMENT, plan_id TEXT NOT NULL DEFAULT '', started_at TEXT NOT NULL," +
            "  status TEXT NOT NULL DEFAULT '', archive_file TEXT NOT NULL DEFAULT ''," +
            "  message TEXT NOT NULL DEFAULT '', skipped INTEGER NOT NULL DEFAULT 0);" +
            "CREATE INDEX IF NOT EXISTS ix_runs_started ON runs(started_at);" +

            // 日历待办：due_date 存 yyyy-MM-dd（本地日期，按天聚合不需要时区）
            "CREATE TABLE IF NOT EXISTS todos (" +
            "  id INTEGER PRIMARY KEY AUTOINCREMENT, plan_id TEXT NOT NULL DEFAULT '', due_date TEXT NOT NULL," +
            "  title TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '', done_at TEXT NOT NULL DEFAULT '');" +
            "CREATE INDEX IF NOT EXISTS ix_todos_due ON todos(due_date);" +

            // 密码库：参数（盐 / 迭代次数 / 校验块）+ 条目（AES-GCM 密文）
            "CREATE TABLE IF NOT EXISTS vault_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS vault_items (id TEXT PRIMARY KEY, blob TEXT NOT NULL);";

        EnsureTable(ddl);
    }
}
