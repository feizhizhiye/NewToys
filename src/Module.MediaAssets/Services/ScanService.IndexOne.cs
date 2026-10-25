using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

public partial class ScanService
{

    /// <summary>
    /// 处理一个文件：① 相对路径（零 IO） → ② size+mtime（零 IO） → ③ quick_hash（读前 N MB，
    /// 仅当库里有同大小**且带 quick_hash** 的位置时才算） → ④ 全新（⛔ 不读内容）。
    /// </summary>
    private void IndexOne(StorageSource storage, string fullPath, StorageIndex index, ScanJob job,
                          List<FileLocation> pending, List<long> touched)
    {
        var info = new FileInfo(fullPath);
        var size = info.Length;
        var mtime = info.LastWriteTime.ToString(TimeFormat);
        var relative = MakeRelative(storage.RootPath, fullPath);
        var ext = info.Extension;

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

    private void FlushNew(List<FileLocation> pending, StorageIndex index,
                          Func<FileLocation, CancellationToken, Task>? enricher, CancellationToken ct)
    {
        if (pending.Count == 0) return;

        _assets.InsertLocations(pending);                       // 一个事务写完，回填 Id
        foreach (var l in pending) if (l.Id != 0) index.Add(l); // 立刻进内存索引（同批里的重复不会重复建）

        // 调用方没传 enricher 时用模块接线的那个（元数据 + 缩略图）
        var en = enricher ?? Enricher;
        if (en != null)
        {
            // 🔴 增强是**进程密集型**（每个文件一次 ffprobe / ffmpeg）——串行跑大目录要几分钟。
            //    这里有界并发（4）折中：⛔ 不全并行（一批 200 个会同时起几百个进程）。
            //    单个文件失败 ⛔ 不影响扫描本身（失败只是没有元数据 / 缩略图，下次重扫会补上）。
            EnrichBatchAsync(pending, en, ct).GetAwaiter().GetResult();
        }

        pending.Clear();
    }

    /// <summary>一批新位置的有界并发增强（默认 4 路；⛔ 别调大——进程数是硬资源）。</summary>
    private static async Task EnrichBatchAsync(List<FileLocation> pending,
                                               Func<FileLocation, CancellationToken, Task> enricher,
                                               CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(EnrichConcurrency);
        var tasks = pending
            .Where(l => l.Id != 0)
            .Select(async l =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try { await enricher(l, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { /* 单个文件增强失败：跳过，不中断整批 */ }
                finally { gate.Release(); }
            })
            .ToList();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* 扫描被取消：剩余增强不再等 */ }
    }

    /// <summary>增强并发度（进程密集型任务的折中值）。</summary>
    private const int EnrichConcurrency = 4;

}
