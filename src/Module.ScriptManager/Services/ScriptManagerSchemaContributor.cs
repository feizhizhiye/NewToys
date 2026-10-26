using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 脚本管理器在自有库（<c>store\script\script.db</c>）里的建表。
/// <list type="bullet">
///   <item><b>建表</b>：<c>pc_observations</c> / <c>pc_runs</c>（历史前缀 <c>pc_</c>，⛔ 不改名：改要迁移数据）
///     + <c>script_docs</c>（脚本手工数据的文档表）。</item>
///   <item><b>老 JSON 合并</b>：<c>config\script\script.json</c> → <c>script_docs</c>（幂等）。</item>
/// </list>
/// </summary>
public sealed class ScriptManagerSchemaContributor : ILibrarySchemaContributor
{
    /// <summary>模块键（版本记录键 <c>schema.script-manager</c>）。</summary>
    public const string Key = "script-manager";

    /// <summary>本模块 schema 版本（v2 = <c>script_docs</c> + script.json 入表）。</summary>
    public const int Version = 2;

    private readonly SqliteDatabase _db;
    private readonly ConfigStoreService _cfg;
    private readonly JsonDocStore _docs;

    public ScriptManagerSchemaContributor(ScriptDb db, ConfigStoreService cfg)
    {
        _db = db;
        _cfg = cfg;
        _docs = new JsonDocStore(db, "script");
    }

    public string ModuleKey => Key;

    public string DatabaseKey => ScriptDb.Key;

    public int SchemaVersion => Version;

    public SchemaOutcome EnsureSchema()
    {
        var outcome = _db.ApplySchema(Key, Version, () =>
        {
            _db.EnsureTable(Ddl);
            _docs.EnsureSchema();
        });

        if (outcome == SchemaOutcome.Failed) return outcome;   // 库不可用 ⇒ 直接放弃（页面自己报 ⚠）

        MergeLegacyJson();
        return outcome;
    }

    /// <summary>
    /// <c>config\script\script.json</c> 搬进 <c>script_docs</c>（幂等）。
    /// <para>⚠️ 必须在 <see cref="ScriptStoreService"/> 第一次 <c>Reload</c> 之前跑，否则会先播空种子。</para>
    /// </summary>
    private void MergeLegacyJson()
        => JsonDocMigration.ImportOnce(_docs, ScriptStoreService.DocName, ScriptStoreService.LegacyFile(_cfg));

    /// <summary>建表 DDL（幂等）。</summary>
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
}
