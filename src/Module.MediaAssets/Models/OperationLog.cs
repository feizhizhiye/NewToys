namespace NewToys.MediaAssets.Models;

/// <summary>
/// 操作日志（`operation_logs`）——**物理操作的「日志先行」凭据**（需求 §6.6 核心流程）：
/// 动磁盘前先写一条 <see cref="OperationStatuses.Pending"/>，成功改 success、失败改 failed 并留 <see cref="BeforeJson"/>。
/// <para>逻辑操作（改库）同样写日志，<see cref="Reversible"/> = 1 表示可回滚。</para>
/// </summary>
public class OperationLog
{
    public long Id { get; set; }

    /// <summary>操作类型：<c>rename</c> / <c>move</c> / <c>copy</c> / <c>delete</c> / <c>restore</c> / <c>tag</c> / <c>category</c> / <c>collection</c> / <c>relation</c> / <c>display_name</c> / <c>trash</c>。</summary>
    public string OpType { get; set; } = "";

    /// <summary>实体类型：<c>asset</c> / <c>file_location</c> / <c>tag</c> / …</summary>
    public string EntityType { get; set; } = "";

    public long? EntityId { get; set; }

    /// <summary>操作前快照（JSON）——回滚 / 校准依据。</summary>
    public string? BeforeJson { get; set; }

    /// <summary>操作后结果（JSON）。</summary>
    public string? AfterJson { get; set; }

    /// <summary>状态，取 <see cref="OperationStatuses"/>。</summary>
    public string Status { get; set; } = OperationStatuses.Pending;

    public string? Error { get; set; }

    /// <summary>是否可逆（逻辑操作 = 1）。</summary>
    public bool Reversible { get; set; }

    public string CreatedAt { get; set; } = "";
    public string? FinishedAt { get; set; }
}
