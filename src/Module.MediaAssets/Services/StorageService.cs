using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 存储源服务（NT-020）：在线探测 + 统计编排。
/// <para>
/// 数据查询一律走 <see cref="AssetService"/>（数据层唯一入口），本类只做"页面要的组装"：
/// ① 根目录可访问性探测（⛔ 不抛异常，读不到 = 离线）；② 探测结果写回 <c>storages.online</c>。
/// </para>
/// <para>
/// 🔴 **离线语义**（需求 §6.6 外部变更）：移动硬盘 / NAS 拔掉 → <c>online=0</c> →
/// <see cref="ScanService"/> 扫描时**跳过**它，其下 file_locations ⛔ 不会被标 missing；
/// 重新上线（根目录又可访问）+ 扫描 → 位置状态恢复正常。
/// </para>
/// </summary>
public class StorageService
{
    private readonly AssetService _assets;

    public StorageService(AssetService assets) => _assets = assets;

    public bool IsAvailable => _assets.IsAvailable;

    /// <summary>
    /// 探测一个存储源根目录当前是否可访问（⛔ 不抛异常）。
    /// 网络路径（NAS）在不可达时 <see cref="Directory.Exists"/> 会阻塞到系统超时——可接受（用户点一下就知道）。
    /// </summary>
    public static bool ProbeOnline(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return false;
        try { return Directory.Exists(rootPath); }
        catch { return false; }
    }

    /// <summary>
    /// 重读全部存储源并**逐个体探测在线状态**（页面打开 / 「🔄 重读存储源」时调）。
    /// 探测结果与库里的 <c>online</c> 不一致时写回（移动硬盘拔了/插了都要能看出来）。
    /// </summary>
    public List<StorageRow> ListRows()
    {
        var rows = new List<StorageRow>();
        if (!IsAvailable) return rows;

        foreach (var s in _assets.ListStorages())
        {
            var online = ProbeOnline(s.RootPath);
            if (online != s.Online) _assets.SetStorageOnline(s.Id, online);

            rows.Add(new StorageRow
            {
                Id = s.Id,
                Name = s.Name,
                RootPath = s.RootPath,
                Type = s.Type,
                Online = online,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                FileCount = _assets.GetStorageStats(s.Id).FileCount,
                LastScanAt = _assets.LastScanText(s.Id)
            });
        }
        return rows;
    }

    /// <summary>单个存储源的统计（详情卡）。</summary>
    public StorageStats Stats(long storageId) => _assets.GetStorageStats(storageId);

    /// <summary>单个存储源最近的扫描错误（详情卡；新的在前）。</summary>
    public List<(string Path, string Message, string CreatedAt)> RecentErrors(long storageId, int limit = 5)
        => _assets.ListStorageScanErrors(storageId, limit);

    /// <summary>上线 / 下线（右键切换）：写回 <c>online</c> 并返回新状态。</summary>
    public bool SetOnline(long storageId, bool online)
    {
        _assets.SetStorageOnline(storageId, online);
        return online;
    }

    /// <summary>
    /// 移除存储源（二次确认在 VM 做）：**只删索引**，⛔ 绝不碰磁盘文件。
    /// 返回该存储源下有多少个文件位置将随之删除（确认对话框要给这个数）。
    /// </summary>
    public int CountLocations(long storageId) => _assets.GetStorageStats(storageId).FileCount;
}
