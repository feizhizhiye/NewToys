using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

public partial class ScanService
{
    /// <summary>扫单个存储源：递归遍历其根，按文件所在目录就地归属存储源（目录没有源就登记一个）→ 批量落库 → 收尾一次性校准 missing / 扫描时刻。（WHY: MA-046）</summary>
    private async Task ScanOneStorageAsync(StorageSource storage, ScanJob job, IProgress<string>? progress,
                                           CancellationToken ct)
    {
        // ⚠️ 先取「本次开始时刻」：扫描收尾用它判「本次没见到」→ missing
        var startedAt = AssetService.Now();

        var known = _assets.ListStorages();
        var byRoot = new Dictionary<string, StorageSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in known) byRoot[AssetService.PathKey(s.RootPath)] = s;

        // 子树（祖先 + 子孙）的位置一次读进内存，按源分桶 → 换目录只换桶，不再查库
        var subtreeIds = new List<long>();
        foreach (var s in known)
            if (s.Id > 0 && (AssetService.PathKey(s.RootPath) == AssetService.PathKey(storage.RootPath)
                             || AssetService.IsUnderPath(s.RootPath, storage.RootPath)
                             || AssetService.IsUnderPath(storage.RootPath, s.RootPath)))
                subtreeIds.Add(s.Id);

        var buckets = _assets.ListLocationsForStorages(subtreeIds);
        var indexes = new Dictionary<long, StorageIndex>();

        StorageIndex IndexOf(long storageId)
        {
            if (indexes.TryGetValue(storageId, out var cached)) return cached;
            var ix = new StorageIndex();
            if (buckets.TryGetValue(storageId, out var list))
                foreach (var l in list) ix.Add(l);
            indexes[storageId] = ix;
            return ix;
        }

        // 认领表：子树里所有已索引位置（跨源）→ 扫到同一路径时迁到它自己的目录源，⛔ 不重复建资产
        var shared = new Dictionary<string, FileLocation>(StringComparer.Ordinal);
        foreach (var kv in buckets)
            foreach (var l in kv.Value)
                if (!string.IsNullOrEmpty(l.Path)) shared[AssetService.PathKey(l.Path)] = l;

        progress?.Report($"「{storage.Name}」开始扫描（按文件夹归属存储源）…");

        await Task.Run(() =>
        {
            var pending = new List<FileLocation>();
            var touched = new List<long>();
            StorageIndex? index = null;
            StorageSource? cur = storage;
            var curDir = "";
            var sw = Stopwatch.StartNew();
            var lastReport = 0L;

            void CloseDir()
            {
                FlushNew(pending, index);
                FlushTouched(touched, startedAt);
                pending = new List<FileLocation>();
                touched = new List<long>();
            }

            foreach (var entry in EnumerateInfos(storage.RootPath, ct))
            {
                ct.ThrowIfCancellationRequested();

                // 同一目录的文件在遍历中连续出现 → 换目录时才切源 / 换索引
                var dir = Path.GetDirectoryName(entry.FullName) ?? storage.RootPath;
                if (!string.Equals(dir, curDir, StringComparison.OrdinalIgnoreCase))
                {
                    CloseDir();
                    curDir = dir;
                    cur = EnsureStorageForDir(byRoot, dir);
                    index = cur == null ? null : IndexOf(cur.Id);
                }

                job.TotalFiles++;
                if (cur == null || index == null)
                {
                    job.ErrorCount++;
                    continue;
                }

                try
                {
                    IndexOne(cur, entry.FullName, index, shared, job, pending, touched);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    job.ErrorCount++;
                    _assets.AddScanError(job.Id, entry.FullName, "io", ex.Message);
                }

                if (pending.Count >= BatchSize) FlushNew(pending, index);
                if (touched.Count >= BatchSize) FlushTouched(touched, startedAt);

                // 进度按**时间**节流（⛔ 不按文件数：大文件会卡住状态栏）
                if (sw.ElapsedMilliseconds - lastReport >= ProgressIntervalMs)
                {
                    lastReport = sw.ElapsedMilliseconds;
                    progress?.Report($"「{storage.Name}」已扫描 {job.TotalFiles} 个（新增 {job.AddedFiles} / 更新 {job.UpdatedFiles}）");
                }
            }

            CloseDir();
        }, ct).ConfigureAwait(false);

        // 收尾：一个事务里做完「本次没见到的 → missing」+「每个源记最近扫描」
        var ids = new List<long>();
        foreach (var s in SubtreeStorages(byRoot, storage)) ids.Add(s.Id);
        var missing = _assets.FinishStorageScans(ids, startedAt);
        job.MissingFiles += missing;
        progress?.Report($"「{storage.Name}」完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {missing} / 失败 {job.ErrorCount}");
    }

    /// <summary>
    /// 取该目录对应的存储源，没有就登记一个（名称 = 目录名）。
    /// 🔴 这是「一个文件只属于它上一级文件夹」的落地点 —— ⛔ 别改成归到祖先源。
    /// </summary>
    private StorageSource? EnsureStorageForDir(Dictionary<string, StorageSource> byRoot, string dir)
    {
        var key = AssetService.PathKey(dir);
        if (byRoot.TryGetValue(key, out var existing)) return existing;

        var trimmed = dir.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) name = trimmed;      // 盘根（D:\）取不到目录名

        var id = _assets.InsertStorageFast(name, trimmed);
        if (id == 0) return null;

        var created = new StorageSource
        {
            Id = id,
            Name = name,
            RootPath = trimmed,
            Type = StorageTypes.Local,
            Online = true
        };
        byRoot[key] = created;
        return created;
    }

    /// <summary>该根之下的全部存储源（含根本身）：收尾校准与「最近扫描」都按它们整批处理。</summary>
    private static List<StorageSource> SubtreeStorages(Dictionary<string, StorageSource> byRoot, StorageSource root)
    {
        var rootKey = AssetService.PathKey(root.RootPath);
        var list = new List<StorageSource>();
        foreach (var s in byRoot.Values)
        {
            var key = AssetService.PathKey(s.RootPath);
            if (key.Length == 0) continue;
            if (key == rootKey || AssetService.IsUnderPath(s.RootPath, root.RootPath)) list.Add(s);
        }
        return list;
    }
}
