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

    /// <summary>扫单个存储源：载入内存索引 → 遍历 → 批量落库 → 收尾 missing 校准。</summary>
    private async Task ScanOneStorageAsync(StorageSource storage, ScanJob job, IProgress<string>? progress,
                                           CancellationToken ct, Func<FileLocation, CancellationToken, Task>? enricher)
    {
        // ⚠️ 先取「本次开始时刻」：扫描收尾用它判「本次没见到」→ missing
        var startedAt = AssetService.Now();

        // 🔴 一次查询把该存储源的现有位置读进内存 → 之后**匹配全在内存做**（⛔ 不 per-file SELECT）
        var index = LoadStorageIndex(storage.Id);
        progress?.Report(index.Count == 0
            ? $"「{storage.Name}」首次扫描：只读文件信息，不读内容（很快）"
            : $"「{storage.Name}」已在库 {index.Count} 个位置，开始校准…");

        await Task.Run(() =>
        {
            var pending = new List<FileLocation>();
            var touched = new List<long>();
            var sw = Stopwatch.StartNew();
            var lastReport = 0L;

            foreach (var file in EnumerateFiles(storage.RootPath, ct))
            {
                ct.ThrowIfCancellationRequested();
                job.TotalFiles++;
                try
                {
                    IndexOne(storage, file, index, job, pending, touched);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    job.ErrorCount++;
                    _assets.AddScanError(job.Id, file, "io", ex.Message);
                }

                if (pending.Count >= BatchSize) FlushNew(pending, index, enricher, ct);
                if (touched.Count >= BatchSize) FlushTouched(touched, startedAt);

                // 进度按**时间**节流（⛔ 不按文件数：大文件会卡住状态栏）
                if (sw.ElapsedMilliseconds - lastReport >= ProgressIntervalMs)
                {
                    lastReport = sw.ElapsedMilliseconds;
                    progress?.Report($"「{storage.Name}」已扫描 {job.TotalFiles} 个（新增 {job.AddedFiles} / 更新 {job.UpdatedFiles}）");
                }
            }

            FlushNew(pending, index, enricher, ct);
            FlushTouched(touched, startedAt);
        }, ct).ConfigureAwait(false);

        // 收尾：本次没见到的旧位置 → missing（⛔ 不删资产）
        var missing = _assets.MarkMissingLocations(storage.Id, startedAt);
        job.MissingFiles += missing;
        progress?.Report($"「{storage.Name}」完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {missing} / 失败 {job.ErrorCount}");
    }

    // ==================== 导入指定的一批文件 ====================

}
