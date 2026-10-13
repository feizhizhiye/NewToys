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
