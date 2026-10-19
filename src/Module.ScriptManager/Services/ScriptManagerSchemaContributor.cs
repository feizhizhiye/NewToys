using System;
using System.IO;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 脚本管理器在**总库**里的建表 + 老数据合并（NT-076）。
/// <list type="bullet">
///   <item><b>建表</b>：<c>pc_observations</c> / <c>pc_runs</c>（原 <c>ScriptDatabase.EnsureSchema</c> 搬来，DDL 一字未改）
///     + <c>script_docs</c>（第二批：脚本手工数据的文档表，见 <see cref="JsonDocStore"/>）。</item>
///   <item><b>老库合并</b>：<c>data\script\script.db</c>（NT-068 时期独立库）→ 并入总库；
///     判定 = 总库 <c>pc_observations</c> 为空才搬（幂等），搬完把老文件改名 <c>.legacy</c>（⛔ 不删）。</item>
///   <item><b>老 JSON 合并</b>（第二批）：<c>config\script\script.json</c> → <c>script_docs</c>。</item>
/// </list>
/// </summary>
public sealed class ScriptManagerSchemaContributor : ILibrarySchemaContributor
{
    private readonly SqliteDatabase _db;
    private readonly ConfigStoreService _cfg;
    private readonly JsonDocStore _docs;

    public ScriptManagerSchemaContributor(SqliteDatabase db, ConfigStoreService cfg)
    {
        _db = db;
        _cfg = cfg;
        _docs = new JsonDocStore(db, "script");
    }

    public string ModuleKey => "script-manager";

    /// <summary>v2 = NT-076 第二批（<c>script_docs</c> + script.json 入表）。</summary>
    public int SchemaVersion => 2;

    public void EnsureSchema()
    {
        if (!_db.EnsureTable(Ddl)) return;   // 库不可用 ⇒ 直接放弃（页面自己报 ⚠）
        _docs.EnsureSchema();
        MergeLegacyDatabase();
        MergeLegacyJson();
    }

    /// <summary>
    /// <c>config\script\script.json</c> 搬进 <c>script_docs</c>（幂等）。
    /// <para>⚠️ 必须在 <see cref="ScriptStoreService"/> 第一次 <c>Reload</c> 之前跑，否则会先播空种子。</para>
    /// </summary>
    private void MergeLegacyJson()
    {
        JsonDocMigration.ImportOnce(_docs, ScriptStoreService.DocName, ScriptStoreService.LegacyFile(_cfg));
    }

    /// <summary>建表 DDL（幂等）。注意 <c>pc_</c> 前缀是历史遗留（NT-068 前叫 PcTools），⛔ 不要跟表前缀纪律硬对齐，改名要迁移数据。</summary>
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS pc_observations (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            item_id     TEXT    NOT NULL,
            run_id      TEXT    NOT NULL,
            row_key     TEXT    NOT NULL,
            attr        TEXT    NOT NULL,
            value       TEXT,
            captured_at TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_pc_obs_item_row  ON pc_observations (item_id, row_key);
        CREATE INDEX IF NOT EXISTS ix_pc_obs_item_time ON pc_observations (item_id, captured_at);

        CREATE TABLE IF NOT EXISTS pc_runs (
            id            TEXT    PRIMARY KEY,
            item_id       TEXT    NOT NULL,
            started_at    TEXT    NOT NULL,
            duration_ms   INTEGER NOT NULL DEFAULT 0,
            exit_code     INTEGER NOT NULL DEFAULT 0,
            stdout_digest TEXT,
            status        TEXT    NOT NULL DEFAULT 'running',
            row_count     INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_pc_runs_item ON pc_runs (item_id, started_at);
        """;

    /// <summary>
    /// 把 <c>data\script\script.db</c> 并进总库。
    /// <para>
    /// 🔴 幂等口径：**总库 <c>pc_observations</c> 有数据就视为已迁移**（⛔ 不会重复插）。
    /// 失败（被别的实例占用 / 文件损坏）只记日志，老文件留着下次再试。
    /// </para>
    /// </summary>
    private void MergeLegacyDatabase()
    {
        var legacy = Path.Combine(_cfg.DataDir, "script", "script.db");
        if (!File.Exists(legacy)) return;

        try
        {
            using var conn = _db.Open();

            using (var probe = conn.CreateCommand())
            {
                probe.CommandText = "SELECT COUNT(*) FROM pc_observations;";
                if (Convert.ToInt32(probe.ExecuteScalar() ?? 0) > 0)
                {
                    // 已经搬过（或用户本来就有数据）⇒ 老文件只留档，不再搬
                    Archive(legacy);
                    return;
                }
            }

            var quoted = legacy.Replace("'", "''");
            using (var cmd = conn.CreateCommand())
            {
                // ATTACH 的文件名不能参数绑定 ⇒ 单引号转义后内联（路径来自自己的配置根，非用户输入）。
                cmd.CommandText = $"""
                    ATTACH DATABASE '{quoted}' AS legacy;
                    INSERT OR IGNORE INTO pc_runs SELECT * FROM legacy.pc_runs;
                    INSERT INTO pc_observations (item_id, run_id, row_key, attr, value, captured_at)
                         SELECT item_id, run_id, row_key, attr, value, captured_at
                           FROM legacy.pc_observations ORDER BY id;
                    DETACH DATABASE legacy;
                    """;
                cmd.ExecuteNonQuery();
            }

            Archive(legacy);
            System.Diagnostics.Debug.WriteLine("[NewToys][migrate] script.db 已并入总库 newtoys.db");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[NewToys][migrate] script.db 并库失败（下次启动重试）：" + ex.Message);
        }
    }

    private static void Archive(string legacy)
    {
        try { File.Move(legacy, legacy + ".legacy", overwrite: true); }
        catch { /* 改名失败不影响数据已并入 */ }
    }
}
