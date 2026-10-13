using System;
using System.Collections.Generic;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 操作日志（`operation_logs`，NT-018 / NT-019）—— 模块内**唯一**写日志的入口。
/// <para>
/// <b>两种用法</b>：
/// <list type="number">
///   <item><b>逻辑操作</b>（改库，不动磁盘）：直接 <see cref="Log"/> 一条 <c>success</c>，<c>reversible=1</c>；</item>
///   <item><b>物理操作</b>（动磁盘）：<see cref="Begin"/> 写 <c>pending</c> → 做磁盘动作 → <see cref="Finish"/> 改
///        <c>success</c> / <c>failed</c>（失败留 <c>before_json</c>，需求 §6.6「日志先行」）。</item>
/// </list>
/// </para>
/// <para>⛔ 库不可用时全部方法静默降级（返回 0 / 空列表），⛔ 不抛。</para>
/// </summary>
public class OperationLogService
{
    private readonly AssetDatabase _db;

    public OperationLogService(AssetDatabase db) => _db = db;

    public bool IsAvailable => _db.IsAvailable;

    /// <summary>写一条 <c>pending</c>（物理操作**动磁盘之前**必须先调它）。返回日志 id（0 = 写失败）。</summary>
    public long Begin(string opType, string entityType, long? entityId, string? beforeJson = null, bool reversible = false)
        => Insert(opType, entityType, entityId, beforeJson, null, OperationStatuses.Pending, null, reversible, finishedAt: null);

    /// <summary>把 <c>pending</c> 收口：成功 → <c>success</c>，失败 → <c>failed</c>（带错误原因与 after_json）。</summary>
    public void Finish(long logId, bool ok, string? error = null, string? afterJson = null)
    {
        if (logId <= 0 || !IsAvailable) return;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE operation_logs
                                SET status=$s, error=$e, after_json=COALESCE($a, after_json), finished_at=$f
                                WHERE id=$id;";
            cmd.Parameters.AddWithValue("$s", ok ? OperationStatuses.Success : OperationStatuses.Failed);
            cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$a", (object?)afterJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$f", AssetService.Now());
            cmd.Parameters.AddWithValue("$id", logId);
            cmd.ExecuteNonQuery();
        }
        catch { /* 日志写不进去不影响业务本身 */ }
    }

    /// <summary>一步写完（逻辑操作用）：直接落一条 <c>success</c>。</summary>
    public long Log(string opType, string entityType, long? entityId,
        string? beforeJson = null, string? afterJson = null, bool reversible = true)
        => Insert(opType, entityType, entityId, beforeJson, afterJson, OperationStatuses.Success, null, reversible, AssetService.Now());

    /// <summary>最近的操作日志（任务中心 / 详情卡「操作日志」用）。</summary>
    public List<OperationLog> ListRecent(int limit = 200) => List("", 0, limit);

    /// <summary>某个实体（资产 / 标签 / …）的操作日志。</summary>
    public List<OperationLog> ListFor(string entityType, long entityId, int limit = 50)
        => List(entityType, entityId, limit);

    // ==================== 内部 ====================

    private List<OperationLog> List(string entityType, long entityId, int limit)
    {
        var list = new List<OperationLog>();
        if (!IsAvailable) return list;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            var where = string.IsNullOrEmpty(entityType) ? "" : "WHERE entity_type=$t AND entity_id=$i";
            cmd.CommandText = $@"SELECT id,op_type,entity_type,entity_id,before_json,after_json,status,error,reversible,created_at,finished_at
                                 FROM operation_logs {where} ORDER BY id DESC LIMIT $lim;";
            if (!string.IsNullOrEmpty(entityType))
            {
                cmd.Parameters.AddWithValue("$t", entityType);
                cmd.Parameters.AddWithValue("$i", entityId);
            }
            cmd.Parameters.AddWithValue("$lim", limit);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new OperationLog
                {
                    Id = r.GetInt64(0),
                    OpType = r.GetString(1),
                    EntityType = r.GetString(2),
                    EntityId = r.IsDBNull(3) ? null : r.GetInt64(3),
                    BeforeJson = r.IsDBNull(4) ? null : r.GetString(4),
                    AfterJson = r.IsDBNull(5) ? null : r.GetString(5),
                    Status = r.GetString(6),
                    Error = r.IsDBNull(7) ? null : r.GetString(7),
                    Reversible = !r.IsDBNull(8) && r.GetInt64(8) != 0,
                    CreatedAt = r.GetString(9),
                    FinishedAt = r.IsDBNull(10) ? null : r.GetString(10)
                });
            }
        }
        catch { /* 库不可用 / 表缺失 → 返回已读到的 */ }
        return list;
    }

    private long Insert(string opType, string entityType, long? entityId, string? beforeJson, string? afterJson,
        string status, string? error, bool reversible, string? finishedAt)
    {
        if (!IsAvailable) return 0;
        try
        {
            using var conn = _db.Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO operation_logs(op_type,entity_type,entity_id,before_json,after_json,status,error,reversible,created_at,finished_at)
                                VALUES($op,$et,$eid,$b,$a,$st,$err,$rev,$c,$f);
                                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$op", opType);
            cmd.Parameters.AddWithValue("$et", entityType);
            cmd.Parameters.AddWithValue("$eid", (object?)entityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$b", (object?)beforeJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$a", (object?)afterJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rev", reversible ? 1 : 0);
            cmd.Parameters.AddWithValue("$c", AssetService.Now());
            cmd.Parameters.AddWithValue("$f", (object?)finishedAt ?? DBNull.Value);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }
        catch { return 0; }
    }
}
