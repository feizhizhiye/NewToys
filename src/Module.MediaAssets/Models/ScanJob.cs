namespace NewToys.MediaAssets.Models;

/// <summary>一次扫描任务（`scan_jobs`）：进度与结果计数（状态栏报「新增 N / 更新 M / 丢失 K」）。</summary>
public class ScanJob
{
    public long Id { get; set; }

    /// <summary>目标存储源（NULL = 全部在线的存储源）。</summary>
    public long? StorageId { get; set; }

    /// <summary>模式：<c>full</c> / <c>incremental</c>。</summary>
    public string Mode { get; set; } = "incremental";

    /// <summary>状态，取 <see cref="ScanStatuses"/>。</summary>
    public string Status { get; set; } = ScanStatuses.Pending;

    public int TotalFiles { get; set; }
    public int ScannedFiles { get; set; }
    public int AddedFiles { get; set; }
    public int UpdatedFiles { get; set; }
    public int MissingFiles { get; set; }
    public int ErrorCount { get; set; }

    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public string CreatedAt { get; set; } = "";
}

/// <summary>扫描过程中单个文件的错误（`scan_errors`）：权限 / 占用 / 读失败等，⛔ 不中断整次扫描。</summary>
public class ScanError
{
    public long Id { get; set; }
    public long ScanJobId { get; set; }
    public string? Path { get; set; }

    /// <summary>错误类别（<c>access</c> / <c>io</c> / <c>hash</c> / <c>meta</c> / <c>thumb</c>）。</summary>
    public string? ErrorType { get; set; }

    public string? Message { get; set; }
    public string CreatedAt { get; set; } = "";
}
