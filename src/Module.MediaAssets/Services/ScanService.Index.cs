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
    /// 导入**指定的一批文件**（工具条「📄 导入文件」）：按所在目录各自登记 / 复用存储源后逐个入库。
    /// <para>
    /// 🔴 与 <see cref="ScanAsync"/> 的关键差别：**不做 missing 校准** —— 用户只选了几个文件，
    /// 同目录的其它文件没被选上，不能把它们判成「丢失」。
    /// </para>
    /// </summary>
    public async Task<ScanJob> IndexFilesAsync(IReadOnlyList<string> files,
                                               IProgress<string>? progress,
                                               CancellationToken ct,
                                               Func<FileLocation, CancellationToken, Task>? enricher = null)
    {
        var job = new ScanJob { Mode = "files", Status = ScanStatuses.Running, StartedAt = AssetService.Now() };

        if (!_assets.IsAvailable)
        {
            job.Status = ScanStatuses.Failed;
            job.FinishedAt = AssetService.Now();
            progress?.Report("资产库不可用（数据库打不开）——导入未执行。");
            return job;
        }
        if (files == null || files.Count == 0)
        {
            job.Status = ScanStatuses.Success;
            job.FinishedAt = AssetService.Now();
            return job;
        }

        job.Id = _assets.CreateScanJob(null, job.Mode);

        try
        {
            await Task.Run(() =>
            {
                // 同目录的文件共用一个存储源 → 先按目录分组（O(n) 建组，⛔ 不重复 Upsert）
                var byDir = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    var dir = Path.GetDirectoryName(f);
                    if (string.IsNullOrEmpty(dir)) continue;
                    if (!byDir.TryGetValue(dir, out var list)) byDir[dir] = list = new List<string>();
                    list.Add(f);
                }

                var sw = Stopwatch.StartNew();
                var lastReport = 0L;

                foreach (var (dir, list) in byDir)
                {
                    ct.ThrowIfCancellationRequested();

                    var trimmed = dir.TrimEnd('\\', '/');
                    // 盘根目录（如 C:\）取不到目录名 → 回退用路径本身，⛔ 别让存储源名为空
                    var dirName = Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(dirName)) dirName = trimmed;

                    var storage = _assets.FindCoveringStorage(dir) ?? new StorageSource
                    {
                        Name = dirName,
                        RootPath = dir,
                        Type = StorageTypes.Local
                    };
                    if (storage.Id == 0) _assets.UpsertStorage(storage);
                    if (storage.Id == 0)                       // 登记失败（库不可用）→ 整组跳过
                    {
                        job.ErrorCount += list.Count;
                        _assets.AddScanError(job.Id, dir, "storage", "存储源登记失败");
                        continue;
                    }

                    var index = LoadStorageIndex(storage.Id);
                    var shared = _assets.LoadOverlappingLocations(storage);
                    var pending = new List<FileLocation>();
                    var touched = new List<long>();

                    foreach (var f in list)
                    {
                        ct.ThrowIfCancellationRequested();
                        job.TotalFiles++;
                        try
                        {
                            if (!File.Exists(f)) { job.ErrorCount++; continue; }
                            IndexOne(storage, f, index, shared, job, pending, touched);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            job.ErrorCount++;
                            _assets.AddScanError(job.Id, f, "io", ex.Message);
                        }

                        if (sw.ElapsedMilliseconds - lastReport >= ProgressIntervalMs)
                        {
                            lastReport = sw.ElapsedMilliseconds;
                            progress?.Report($"已导入 {job.TotalFiles} / {files.Count}（新增 {job.AddedFiles} / 更新 {job.UpdatedFiles}）");
                        }
                    }

                    FlushNew(pending, index, enricher, ct);
                    FlushTouched(touched, AssetService.Now());
                }
            }, ct).ConfigureAwait(false);

            job.Status = ScanStatuses.Success;
            progress?.Report($"导入完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 失败 {job.ErrorCount}");
        }
        catch (OperationCanceledException)
        {
            job.Status = ScanStatuses.Cancelled;
            progress?.Report("导入已取消。");
        }
        catch (Exception ex)
        {
            job.Status = ScanStatuses.Failed;
            _assets.AddScanError(job.Id, null, "import", ex.Message);
            progress?.Report("导入失败：" + ex.Message);
        }
        finally
        {
            job.FinishedAt = AssetService.Now();
            _assets.UpdateScanJob(job);
        }
        return job;
    }

    // ==================== 单个文件：分级匹配 ====================

}
