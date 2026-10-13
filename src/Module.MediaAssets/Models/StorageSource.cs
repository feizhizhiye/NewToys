using System;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 存储源（`storages`）：扫描的根，如某个本地磁盘目录 / 移动硬盘 / NAS 挂载点。
/// <para>
/// ⚠️ 类名叫 <c>StorageSource</c> 而不是 <c>Storage</c>：后者会与 Avalonia 的
/// <c>Avalonia.Platform.Storage</c>（命名空间）在同一文件里撞名（存储源页要选目录）。
/// </para>
/// <para>**移除存储源只移索引，⛔ 不删磁盘文件**（需求 §6.6）。</para>
/// </summary>
public class StorageSource
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>根目录绝对路径。</summary>
    public string RootPath { get; set; } = "";

    /// <summary>类型，取 <see cref="StorageTypes"/>。</summary>
    public string Type { get; set; } = StorageTypes.Local;

    /// <summary>在线状态：移动硬盘 / NAS 离线时置 0 —— 扫描**不动**其下位置，重新上线再校准。</summary>
    public bool Online { get; set; } = true;

    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// 存储源统计（NT-020 存储源页详情卡）：文件位置数 / 已用大小 / 缺失数 / 按资产类型分布。
/// </summary>
public sealed class StorageStats
{
    public long StorageId { get; init; }

    /// <summary>该存储源下的文件位置数（不是去重资产数——一个资产可有多份位置）。</summary>
    public int FileCount { get; set; }

    /// <summary>已用大小（字节；位置表 size 求和）。</summary>
    public long TotalSize { get; set; }

    /// <summary>状态为 <c>missing</c> 的位置数（文件被外部删掉）。</summary>
    public int MissingCount { get; set; }

    /// <summary>按资产类型分布（类型值 / 位置数 / 字节数；按位置数降序）。</summary>
    public List<(string Type, int Count, long Size)> ByType { get; } = new();

    public string FileCountText => FileCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " 个";

    public string SizeText => AssetRow.FormatSize(TotalSize);

    public string MissingText => MissingCount == 0 ? "无缺失" : $"{MissingCount} 个缺失";
}

/// <summary>
/// 存储源页的一行（NT-020）：库里的 <see cref="StorageSource"/> + 页面显示的统计 / 时间文本。
/// ⚠️ 纯展示模型（没有逐行交互态）→ 普通类即可；刷新整表重建。
/// </summary>
public sealed class StorageRow
{
    public long Id { get; init; }
    public string Name { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string Type { get; set; } = StorageTypes.Local;
    public bool Online { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    /// <summary>文件位置数（统计查询一次带上）。</summary>
    public int FileCount { get; set; }

    /// <summary>最近一次扫描开始时间（原始文本；没有过扫描为空）。</summary>
    public string LastScanAt { get; set; } = "";

    public string TypeText => Type switch
    {
        StorageTypes.Local => "本地",
        StorageTypes.Removable => "移动",
        StorageTypes.Nas => "NAS",
        _ => Type
    };

    public string StatusText => Online ? "在线" : "离线";

    public string FileCountText => FileCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>「最近扫描」列文本：没有扫描过 → 「从未扫描」。</summary>
    public string LastScanText
    {
        get
        {
            if (string.IsNullOrEmpty(LastScanAt)) return "从未扫描";
            return DateTime.TryParse(LastScanAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : LastScanAt;
        }
    }
}
