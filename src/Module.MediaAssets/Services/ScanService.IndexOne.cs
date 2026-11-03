using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

public partial class ScanService
{

    /// <summary>
    /// 处理一个文件：⓪ 重叠源按完整路径认领（同一物理文件已有位置） → ① 相对路径（零 IO） →
    /// ② size+mtime（零 IO） → ③ quick_hash（读前 N MB，仅当库里有同大小**且带 quick_hash** 的位置时才算） →
    /// ④ 全新（⛔ 不读内容）。
    /// </summary>
    private void IndexOne(StorageSource storage, string fullPath, StorageIndex index,
                          Dictionary<string, FileLocation> shared, ScanJob job,
                          List<FileLocation> pending, List<long> touched)
    {
        var info = new FileInfo(fullPath);
        var size = info.Length;
        var mtime = info.LastWriteTime.ToString(TimeFormat);
        var relative = MakeRelative(storage.RootPath, fullPath);
        var ext = info.Extension;

        // ⓪ 历史归属认领：同一物理路径已有位置、但挂在其它的源（祖先 / 兄弟目录）下 →
        //    改归当前目录源，⛔ 不新建资产。已是本源的位置交给 ① 走批量 touch。
        if (shared.Count > 0 && shared.TryGetValue(AssetService.PathKey(fullPath), out var adopted))
        {
            shared.Remove(AssetService.PathKey(fullPath));
            if (adopted.StorageId != storage.Id)
            {
                job.ScannedFiles++;
                ApplyMove(adopted, storage, fullPath, relative, info, size, mtime, ext, null);
                _assets.UpdateLocation(adopted);
                index.Reindex(adopted);
                job.UpdatedFiles++;
                return;
            }
        }

        // ① 存储源 + 相对路径（同存储源内最可靠；零 IO）
        if (index.ByRelPath.TryGetValue(relative, out var loc))
        {
            job.ScannedFiles++;
            if (TouchOrUpdate(loc, fullPath, relative, info, size, mtime, ext, touched)) job.UpdatedFiles++;
            return;
        }

        // ② size + mtime（零 IO）：内容没动、路径变了 → 视为**移动**
        //    ⛔ 守卫：相同 (size,mtime) 可能是不同文件（复制且保留时间戳）→ 只认**唯一**候选；
        //    多候选视为歧义，回落到"新文件"，避免把原文件位置误改写到副本上。
        var bySizeMtime = index.TryGetSingleBySizeMtime(size, mtime);
        if (bySizeMtime != null)
        {
            job.ScannedFiles++;
            ApplyMove(bySizeMtime, storage, fullPath, relative, info, size, mtime, ext, null);
            _assets.UpdateLocation(bySizeMtime);
            index.Reindex(bySizeMtime);
            job.UpdatedFiles++;
            return;
        }

        // ③ quick_hash + size（**只读前 N MB**）
        //    ⛔ 守卫：库里没有"同大小且已带 quick_hash"的位置时**直接跳过** —— 算了也无处匹配。
        //    这一条守卫就是首次扫描**完全不读文件内容**的关键。
        if (index.HasQuickHashForSize(size))
        {
            var quickHash = ComputeQuickHash(fullPath, size, info.LastWriteTimeUtc.Ticks);
            if (quickHash.Length > 0 && index.ByQuickHash.TryGetValue((size, quickHash), out loc))
            {
                job.ScannedFiles++;
                ApplyMove(loc, storage, fullPath, relative, info, size, mtime, ext, quickHash);
                _assets.UpdateLocation(loc);
                index.Reindex(loc);
                job.UpdatedFiles++;
                return;
            }
        }

        // ④ 全新文件：建资产 + 排队等批量写入
        //    ⛔ **不读文件内容**（完整哈希延后到 ）—— 这是 v3.29 性能重修的核心。
        var asset = new Asset
        {
            Type = GuessAssetType(ext),
            Title = Path.GetFileNameWithoutExtension(info.Name),
            Status = AssetStatuses.Active
        };
        var assetId = _assets.InsertAsset(asset);
        if (assetId == 0) throw new IOException("资产写入失败（数据库不可用？）");

        pending.Add(new FileLocation
        {
            AssetId = assetId,
            StorageId = storage.Id,
            Path = fullPath,
            RelativePath = relative,
            FileName = info.Name,
            Ext = ext,
            MimeType = GuessMimeType(ext),
            Size = size,
            Mtime = mtime,
            Hash = ComputeFullHashForNewFiles ? ComputeFullHash(fullPath) : null,
            QuickHash = null,
            IsPrimary = true,
            Status = LocationStatuses.Active,
            LastSeenAt = AssetService.Now()
        });

        job.ScannedFiles++;
        job.AddedFiles++;
    }

    /// <summary>
    /// 位置还在原处：字段**全没变**就只记 id（批量 touch，⛔ 不写库）；有变化才 UPDATE。
    /// 返回是否有实际改动（给 <c>UpdatedFiles</c> 计数用）。
    /// </summary>
    private bool TouchOrUpdate(FileLocation loc, string fullPath, string relative, FileInfo info,
                               long size, string mtime, string ext, List<long> touched)
    {
        var unchanged = loc.Size == size
                        && string.Equals(loc.Mtime, mtime, StringComparison.Ordinal)
                        && string.Equals(loc.FileName, info.Name, StringComparison.Ordinal)
                        && string.Equals(loc.RelativePath, relative, StringComparison.Ordinal)
                        && loc.Status == LocationStatuses.Active;

        if (unchanged)
        {
            touched.Add(loc.Id);
            return false;
        }

        loc.Path = fullPath;
        loc.FileName = info.Name;
        loc.RelativePath = relative;
        loc.Ext = ext;
        loc.Size = size;
        loc.Mtime = mtime;
        loc.Status = LocationStatuses.Active;
        loc.LastSeenAt = AssetService.Now();
        _assets.UpdateLocation(loc);
        return true;
    }

    /// <summary>把已有位置改到新路径（识别为移动 / 改名）：更新路径字段 + 复位 missing。</summary>
    private static void ApplyMove(FileLocation loc, StorageSource storage, string fullPath, string relative,
                                  FileInfo info, long size, string mtime, string ext, string? quickHash)
    {
        loc.StorageId = storage.Id;
        loc.Path = fullPath;
        loc.RelativePath = relative;
        loc.FileName = info.Name;
        loc.Ext = ext;
        loc.Size = size;
        loc.Mtime = mtime;
        if (quickHash != null) loc.QuickHash = quickHash;
        loc.Status = LocationStatuses.Active;
        loc.LastSeenAt = AssetService.Now();
    }

    // ==================== 批量落库 ====================

    private void FlushNew(List<FileLocation> pending, StorageIndex? index)
    {
        if (pending.Count == 0) return;

        _assets.InsertLocations(pending);                       // 一个事务写完，回填 Id
        if (index != null)
            foreach (var l in pending) if (l.Id != 0) index.Add(l); // 立刻进内存索引（同批里的重复不会重复建）

        // 新位置交给后台（元数据 / 缩略图），⛔ 不在这里同步跑 —— 那是扫描慢的根因
        var added = new List<FileLocation>(pending.Count);
        foreach (var l in pending) if (l.Id != 0) added.Add(l);
        pending.Clear();
        if (added.Count > 0) OnNewLocations?.Invoke(added);
    }
}
