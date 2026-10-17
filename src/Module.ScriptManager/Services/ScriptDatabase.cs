using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using NewToys.ScriptManager.Models;

namespace NewToys.ScriptManager.Services;

/// <summary>一条观测（一次采集里某行的某个属性）。</summary>
/// <param name="RowKey">行标识（同一采集器内稳定，用于就地改值）。</param>
/// <param name="Attr">属性名 = 列 key（与 <see cref="ScriptColumn.Key"/> 同一键空间）。</param>
/// <param name="Value">属性值（统一按字符串存，界面按列渲染）。</param>
public readonly record struct ScriptObservation(string RowKey, string Attr, string Value);

/// <summary>
/// 观测缓存库 <c>data\script\script.db</c>（NT-068 §三.3）：<c>pc_observations</c> + <c>pc_runs</c>。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>不复用</b> <c>Core</c> 的 <c>SqliteDatabase</c>（它把 <c>newtoys.db</c> 写死了，改它 = 动框架层）。
/// 本库用 <c>Microsoft.Data.Sqlite</c> 直连（Core 的包引用可传递编译）。
/// </para>
/// <para>
/// 连接生命周期 = <b>每次操作短连接</b>（SQLite 连接便宜，省掉线程 / 生命周期管理）。
/// 并发由 <c>busy_timeout</c> 兜底；写线程只有采集定时器，量级很低。
/// </para>
/// <para>
/// ⚠️ 观测与条目<b>必须分表</b>：每次刷新写新行，进 <c>script.json</c> 会把它撑爆。
/// </para>
/// </remarks>
public sealed class ScriptDatabase
{
    private readonly string _path;

    public ScriptDatabase(string databasePath)
    {
        _path = databasePath;
        EnsureSchema();
    }

    public string DatabasePath => _path;

    /// <summary>建库建表（幂等）。库目录不存在时自建。</summary>
    public void EnsureSchema()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
        cmd.ExecuteNonQuery();
    }

    // ==================== runs ====================

    /// <summary>开始一次采集：先落一条 <c>running</c> 记录，返回 run id。</summary>
    public string BeginRun(string itemId)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO pc_runs (id, item_id, started_at, status) VALUES ($id, $item, $at, 'running');";
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.Parameters.AddWithValue("$item", itemId);
        cmd.Parameters.AddWithValue("$at", Now());
        cmd.ExecuteNonQuery();
        return runId;
    }

    /// <summary>结束一次采集（状态 = ok / warn / error）。</summary>
    public void CompleteRun(string runId, string status, int exitCode, long durationMs, int rowCount, string? stdoutDigest = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE pc_runs
               SET status = $status, exit_code = $code, duration_ms = $ms, row_count = $rows, stdout_digest = $digest
             WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$code", exitCode);
        cmd.Parameters.AddWithValue("$ms", durationMs);
        cmd.Parameters.AddWithValue("$rows", rowCount);
        cmd.Parameters.AddWithValue("$digest", (object?)stdoutDigest ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.ExecuteNonQuery();
    }

    // ==================== observations ====================

    /// <summary>
    /// 一次采集的全部观测（单事务）+ 按保留策略清理；返回实际写入的行数。
    /// </summary>
    /// <param name="item">条目（决定保留策略；只读条目强制 <c>latest</c>）。</param>
    public int WriteObservations(string itemId, string runId, IReadOnlyList<ScriptObservation> rows, ScriptItem? item)
    {
        if (rows.Count == 0) return 0;

        var at = Now();
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO pc_observations (item_id, run_id, row_key, attr, value, captured_at)
                VALUES ($item, $run, $row, $attr, $value, $at);
                """;
            var pItem = cmd.Parameters.Add("$item", SqliteType.Text);
            var pRun = cmd.Parameters.Add("$run", SqliteType.Text);
            var pRow = cmd.Parameters.Add("$row", SqliteType.Text);
            var pAttr = cmd.Parameters.Add("$attr", SqliteType.Text);
            var pValue = cmd.Parameters.Add("$value", SqliteType.Text);
            var pAt = cmd.Parameters.Add("$at", SqliteType.Text);
            pItem.Value = itemId;
            pRun.Value = runId;
            pAt.Value = at;

            foreach (var r in rows)
            {
                pRow.Value = r.RowKey;
                pAttr.Value = r.Attr;
                pValue.Value = r.Value ?? "";
                cmd.ExecuteNonQuery();
            }
        }

        PruneCore(conn, tx, itemId, runId, item);
        tx.Commit();
        return rows.Count;
    }

    /// <summary>读某条目最近一次采集的观测（按 <c>attr</c> 出现序返回，行按 row_key 分组由调用方做）。</summary>
    public IReadOnlyList<ScriptObservation> LatestObservations(string itemId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT row_key, attr, value
              FROM pc_observations
             WHERE item_id = $item
               AND run_id = (SELECT id FROM pc_runs WHERE item_id = $item AND row_count > 0
                              ORDER BY started_at DESC LIMIT 1)
             ORDER BY row_key, id;
            """;
        cmd.Parameters.AddWithValue("$item", itemId);

        var list = new List<ScriptObservation>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new ScriptObservation(rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2)));
        return list;
    }

    /// <summary>某条目当前观测行数（验收 §五.11：<c>latest</c> 下不随刷新增长）。</summary>
    public int CountObservations(string itemId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pc_observations WHERE item_id = $item;";
        cmd.Parameters.AddWithValue("$item", itemId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    // ==================== 保留策略 ====================

    /// <summary>对单条目执行保留策略（写入后已自动做；这里是手动入口）。</summary>
    public void Prune(string itemId, ScriptItem? item)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        PruneCore(conn, tx, itemId, currentRunId: null, item);
        tx.Commit();
    }

    /// <summary>启动兜底：对所有 <c>days</c> 模式的条目清一遍超龄观测。</summary>
    public void PruneAllDays(IEnumerable<ScriptItem> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var item in items)
        {
            if (ScriptRetentionMode.Normalize(item.RetentionMode) != ScriptRetentionMode.Days) continue;
            PruneCore(conn, tx, item.Id, currentRunId: null, item);
        }
        tx.Commit();
    }

    /// <summary>
    /// 保留策略（NT-068 §三.10）：
    /// <list type="bullet">
    ///   <item><c>latest</c>：删同条目的旧观测，只留 <paramref name="currentRunId"/> 这一批（只读条目强制本模式）；</item>
    ///   <item><c>count</c>：只留最近 <c>retentionN</c> 次 run 的观测；</item>
    ///   <item><c>days</c>：删 <c>captured_at</c> 早于 N 天的观测。</item>
    /// </list>
    /// </summary>
    private static void PruneCore(SqliteConnection conn, SqliteTransaction tx, string itemId, string? currentRunId, ScriptItem? item)
    {
        var mode = ScriptRetentionMode.Normalize(item?.RetentionMode);
        // 只读条目一律 latest（§三.10 硬约束）。
        if (item?.Readonly == true) mode = ScriptRetentionMode.Latest;
        var n = item?.RetentionN ?? 0;

        string sql;
        switch (mode)
        {
            case ScriptRetentionMode.Count when n > 0:
                sql = """
                    DELETE FROM pc_observations
                     WHERE item_id = $item
                       AND run_id NOT IN (
                             SELECT id FROM pc_runs
                              WHERE item_id = $item
                              ORDER BY started_at DESC
                              LIMIT $n);
                    """;
                break;

            case ScriptRetentionMode.Days when n > 0:
                sql = "DELETE FROM pc_observations WHERE item_id = $item AND captured_at < $cutoff;";
                break;

            default:
                // latest：只留本次（手动清理且无 run 上下文时不删，避免把唯一一份数据删光）。
                if (string.IsNullOrEmpty(currentRunId)) return;
                sql = "DELETE FROM pc_observations WHERE item_id = $item AND run_id <> $run;";
                break;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$item", itemId);
        if (sql.Contains("$run")) cmd.Parameters.AddWithValue("$run", currentRunId!);
        if (sql.Contains("$n")) cmd.Parameters.AddWithValue("$n", n);
        if (sql.Contains("$cutoff"))
            cmd.Parameters.AddWithValue("$cutoff", DateTime.Now.AddDays(-n).ToString("yyyy-MM-ddTHH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    // ==================== 基础设施 ====================

    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // 默认（Private）连接缓存：只本进程用，配合下面每次操作的短连接足够。
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=3000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
