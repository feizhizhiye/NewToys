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

/// <summary>
/// 扫描服务（需求 §6.6 核心流程·扫描）：目录遍历 → 忽略规则 → 分级匹配 → 建 / 更新资产与位置 →
/// 本次未出现的旧位置标 <c>missing</c>（⛔ 不删资产）。
/// <para>
/// <b>🔴 性能策略（v3.29 重写，实测教训：第一版扫一个目录几分钟不动、硬盘狂响）</b>
/// <list type="number">
///   <item><b>零 IO 优先</b>：① 存储源+相对路径 → ② size + mtime，两级都**不读文件内容**；</item>
///   <item><b>quick_hash（读前 N MB）只在"库里存在同大小且已有 quick_hash 的位置"时才算</b>
///         —— 否则算了也没处匹配。**首次扫描因此完全不读文件内容**（纯元数据遍历）；</item>
///   <item><b>新文件⛔ 不算完整内容哈希</b>（那会把每个文件整读一遍，是之前慢的根因）。
///         完整哈希与元数据一起归 NT-021 的「补全哈希」任务；需要立即算可开
///         <see cref="ComputeFullHashForNewFiles"/>；</item>
///   <item><b>匹配全在内存做</b>：扫描前把该存储源的现有位置**一次**读进 <see cref="StorageIndex"/>，
///         ⛔ 不 per-file SELECT（1 万文件 = 4 万次查询 → 4 次）；</item>
///   <item><b>批量落库</b>：新位置攒够 <see cref="BatchSize"/> 条用一个事务提交；没变化的位置
///         只记 id、批量 <c>UPDATE last_seen_at</c>，⛔ 不逐条写。</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ <b>已知降级</b>：首次入库的位置没有 quick_hash → 之后若文件被移动**且** mtime 也变了，
/// ①② 都不命中，会当成新资产（旧的留 missing）。内容完全一样的重复由将来「补全哈希」校准。
/// </para>
/// <para>
/// ⚠️ 元数据 / 缩略图**不在本项**（NT-021）：本类只留 <c>enricher</c> 回调挂钩，扫描到新文件时调用它。
/// </para>
/// </summary>
public class ScanService
{
    private readonly AssetService _assets;

    /// <summary>时间戳格式（可排序文本；与 <see cref="AssetService.Now"/> 一致）。</summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    public ScanService(AssetService assets) => _assets = assets;

    /// <summary>
    /// **预览扫描**（v3.29，用户要的操作逻辑：先扫出来看看 → 满意再「💾 加入库」、不满意「🧹 清除列表」换文件夹重来）。
    /// <para>
    /// 🔴 与 <see cref="ScanAsync"/> 的根本差别：**一个字节都不写库**（⛔ 不建 assets / file_locations、
    /// ⛔ 不算任何哈希）→ 纯元数据遍历，最快，且随时可以丢弃。落库由 <c>AssetService.CommitRows</c> 显式做。
    /// </para>
    /// </summary>
    public Task<List<AssetRow>> PreviewAsync(string rootPath, bool recursive,
                                             IProgress<string>? progress, CancellationToken ct)
        => Task.Run(() =>
        {
            var rows = new List<AssetRow>();
            var sw = Stopwatch.StartNew();
            var lastReport = 0L;
            var rootName = Path.GetFileName(rootPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(rootName)) rootName = rootPath;

            foreach (var file in EnumerateFiles(rootPath, ct, recursive))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    var name = Path.GetFileNameWithoutExtension(info.Name);
                    rows.Add(new AssetRow
                    {
                        Name = name,
                        Title = name,
                        FileName = info.Name,
                        FilePath = file,
                        Type = GuessAssetType(info.Extension),
                        Size = info.Length,
                        Mtime = info.LastWriteTime.ToString(TimeFormat),
                        Status = AssetStatuses.Active,
                        LocationStatus = LocationStatuses.Active,
                        // ⚠️ ModifiedText / SizeText / TypeText 都是**只读派生属性**，⛔ 不能在这里赋值
                        StorageName = rootName
                    });
                }
                catch { /* 单个文件读不了 → 跳过，不中断整次预览 */ }

                if (sw.ElapsedMilliseconds - lastReport >= ProgressIntervalMs)
                {
                    lastReport = sw.ElapsedMilliseconds;
                    progress?.Report($"已扫描 {rows.Count} 个…");
                }
            }
            return rows;
        }, ct);

    /// <summary>扫描时跳过的目录名（用户可配，见 NT-021 设置页）。</summary>
    public HashSet<string> IgnoreDirectoryNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "$RECYCLE.BIN", "System Volume Information",
        ".cache", "AppData", ".thumbnails"
    };

    /// <summary>扫描时跳过的扩展名（临时 / 系统文件；用户可配，见 NT-021 设置页）。</summary>
    public HashSet<string> IgnoreExtensions { get; private set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".part", ".crdownload", ".partial", ".lnk", ".ini", ".db", ".db-wal", ".db-shm"
    };

    /// <summary>NT-021：忽略隐藏文件 / 目录（文件属性 Hidden；用户可配）。</summary>
    public bool IgnoreHiddenFiles { get; private set; } = true;

    /// <summary>NT-021：忽略临时文件（<c>~$*</c> Office 临时副本等；用户可配）。</summary>
    public bool IgnoreTempFiles { get; private set; } = true;

    /// <summary>
    /// NT-021：新文件入库后的**增强回调**（元数据提取 + 缩略图生成）。
    /// 由模块初始化时接线（读设置的自动开关）；传 null 即跳过。
    /// ⚠️ 只在**新位置**批量落库后调（⛔ 不对"已见过"的位置重复跑）。
    /// </summary>
    public Func<FileLocation, CancellationToken, Task>? Enricher { get; set; }

    /// <summary>
    /// NT-021：把模块设置应用到扫描器（忽略规则 / 采样大小）。设置页保存后由调用方调一次。
    /// </summary>
    public void ApplySettings(MediaAssetsSettings s)
    {
        IgnoreDirectoryNames = new HashSet<string>(MediaAssetsSettings.ParseList(s.IgnoreDirectories), StringComparer.OrdinalIgnoreCase);
        IgnoreExtensions = new HashSet<string>(
            MediaAssetsSettings.ParseList(s.IgnoreExtensions).Select(MediaAssetsSettings.NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);
        IgnoreHiddenFiles = s.IgnoreHiddenFiles;
        IgnoreTempFiles = s.IgnoreTempFiles;
        QuickHashSampleMb = Math.Clamp(s.QuickHashSampleMb, 0, 64);
    }

    /// <summary>quick_hash 采样大小（MB）：默认 1 MB，配得越大越准、扫描越慢。</summary>
    public int QuickHashSampleMb { get; set; } = 1;

    /// <summary>
    /// 新入库的文件是否**立即**算完整内容哈希。默认 <c>false</c>。
    /// 🔴 打开它会把每个新文件整读一遍 —— 首次扫描立刻退化成"几分钟扫不完一个目录 + 硬盘狂响"。
    /// 正式用途 = 需要立刻得到内容指纹做去重时；常规索引交给 NT-021 的异步「补全哈希」。
    /// </summary>
    public bool ComputeFullHashForNewFiles { get; set; }

    /// <summary>新位置的批量提交阈值（条）。事务提交比逐条 INSERT 快一个数量级。</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// 进度上报的最小间隔（毫秒）。
    /// ⛔ **别按文件数上报**：大文件一个就卡住几十秒，状态栏看着像死机（用户实测踩到）。
    /// </summary>
    public int ProgressIntervalMs { get; set; } = 200;

    public bool IsAvailable => _assets.IsAvailable;

    // ==================== 扫描（可指定存储源 / 全部在线） ====================

    /// <summary>
    /// 扫描（异步 / 可取消 / 带进度文本）。<paramref name="storageId"/> = null 表示**全部在线存储源**。
    /// <paramref name="enricher"/>：新文件入库后回调（NT-021 接元数据 + 缩略图；传 null 即跳过）。
    /// 返回已回填计数的 <see cref="ScanJob"/>（也写回了 <c>scan_jobs</c> 表）。
    /// </summary>
    public async Task<ScanJob> ScanAsync(long? storageId,
                                        IProgress<string>? progress,
                                        CancellationToken ct,
                                        Func<FileLocation, CancellationToken, Task>? enricher = null)
    {
        var job = new ScanJob
        {
            StorageId = storageId,
            Mode = "incremental",
            Status = ScanStatuses.Running,
            StartedAt = AssetService.Now()
        };

        if (!_assets.IsAvailable)
        {
            job.Status = ScanStatuses.Failed;
            job.FinishedAt = AssetService.Now();
            progress?.Report("资产库不可用（数据库打不开）——扫描未执行。");
            return job;
        }

        // 目标存储源：指定的那一个 / 全部在线的
        var storages = _assets.ListStorages();
        storages = storageId is { } sid
            ? storages.Where(s => s.Id == sid).ToList()
            : storages.Where(s => s.Online).ToList();

        if (storages.Count == 0)
        {
            job.Status = ScanStatuses.Success;
            job.FinishedAt = AssetService.Now();
            progress?.Report(storageId == null ? "没有在线的存储源——请先在「存储源」页添加。" : "指定的存储源不存在。");
            return job;
        }

        job.Id = _assets.CreateScanJob(storageId, job.Mode);

        try
        {
            foreach (var storage in storages)
            {
                ct.ThrowIfCancellationRequested();

                // 离线存储源：⛔ 不动它下面的位置（重新上线后扫描再校准）
                if (!storage.Online)
                {
                    progress?.Report($"「{storage.Name}」离线，已跳过。");
                    continue;
                }
                if (!Directory.Exists(storage.RootPath))
                {
                    _assets.AddScanError(job.Id, storage.RootPath, "access", "存储源根目录不存在（离线？）");
                    job.ErrorCount++;
                    progress?.Report($"「{storage.Name}」根目录不存在，已跳过。");
                    continue;
                }

                await ScanOneStorageAsync(storage, job, progress, ct, enricher).ConfigureAwait(false);
            }

            job.Status = ScanStatuses.Success;
        }
        catch (OperationCanceledException)
        {
            job.Status = ScanStatuses.Cancelled;
            progress?.Report("扫描已取消。");
        }
        catch (Exception ex)
        {
            job.Status = ScanStatuses.Failed;
            _assets.AddScanError(job.Id, null, "scan", ex.Message);
            progress?.Report("扫描失败：" + ex.Message);
        }
        finally
        {
            job.FinishedAt = AssetService.Now();
            _assets.UpdateScanJob(job);
        }

        return job;
    }

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

                    var storage = _assets.FindStorageByRoot(dir) ?? new StorageSource
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
                    var pending = new List<FileLocation>();
                    var touched = new List<long>();

                    foreach (var f in list)
                    {
                        ct.ThrowIfCancellationRequested();
                        job.TotalFiles++;
                        try
                        {
                            if (!File.Exists(f)) { job.ErrorCount++; continue; }
                            IndexOne(storage, f, index, job, pending, touched);
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
        if (index.BySizeMtime.TryGetValue((size, mtime), out loc))
        {
            job.ScannedFiles++;
            ApplyMove(loc, storage, fullPath, relative, info, size, mtime, ext, null);
            _assets.UpdateLocation(loc);
            index.Reindex(loc);
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
        //    ⛔ **不读文件内容**（完整哈希延后到 NT-021）—— 这是 v3.29 性能重修的核心。
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

        // NT-021：调用方没传 enricher 时用模块接线的那个（元数据 + 缩略图）
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

    private void FlushTouched(List<long> touched, string seenAt)
    {
        if (touched.Count == 0) return;
        _assets.TouchLocations(touched, seenAt);
        touched.Clear();
    }

    /// <summary>把一个存储源的现有位置**一次**读进内存索引（⛔ 不 per-file 查库）。</summary>
    private StorageIndex LoadStorageIndex(long storageId)
    {
        var index = new StorageIndex();
        if (!_assets.IsAvailable) return index;
        foreach (var l in _assets.ListStorageLocations(storageId)) index.Add(l);
        return index;
    }

    // ==================== 内部：遍历 / 忽略 / 哈希 ====================

    /// <summary>
    /// 手写遍历（⛔ 不用 <c>Directory.EnumerateFiles(..., AllDirectories)</c>：它遇到**一个**没权限的
    /// 子目录就整体抛异常，整次扫描会断）。这里逐目录 try-catch：读不了的目录跳过，其余照常。
    /// <para>
    /// ⚠️ 用 <c>EnumerateFileSystemEntries</c> 一次拿**全部条目**再自己分流目录 / 文件：
    /// 比 <c>GetDirectories</c> + <c>GetFiles</c> 少一半系统调用（大目录上很明显）。
    /// </para>
    /// </summary>
    private IEnumerable<string> EnumerateFiles(string root, CancellationToken ct, bool recursive = true)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch { continue; }                       // 目录读不了 → 跳过

            foreach (var entry in entries)
            {
                var isDir = false;
                try
                {
                    var attrs = File.GetAttributes(entry);
                    // 隐藏 / 系统目录跳过（回收站、卷影等）；⛔ 不停在这类目录上浪费时间
                    if (attrs.HasFlag(FileAttributes.System)) continue;
                    if (IgnoreHiddenFiles && attrs.HasFlag(FileAttributes.Hidden)) continue;
                    isDir = attrs.HasFlag(FileAttributes.Directory);
                }
                catch { continue; }

                if (isDir)
                {
                    if (IgnoreDirectoryNames.Contains(Path.GetFileName(entry))) continue;
                    if (recursive) stack.Push(entry);   // v3.29：关掉「含子目录」就只取当前这一层
                }
                else
                {
                    if (IgnoreExtensions.Contains(Path.GetExtension(entry))) continue;
                    // NT-021：Office 等程序的临时副本（~$开头）—— 扩展名管不到它
                    if (IgnoreTempFiles && Path.GetFileName(entry).StartsWith("~$", StringComparison.Ordinal)) continue;
                    yield return entry;
                }
            }
        }
    }

    /// <summary>相对路径（存储源根之下）；根就是它自己时回退文件名。</summary>
    private static string MakeRelative(string root, string fullPath)
    {
        try
        {
            var rel = Path.GetRelativePath(root, fullPath);
            return string.IsNullOrEmpty(rel) ? Path.GetFileName(fullPath) : rel;
        }
        catch { return Path.GetFileName(fullPath); }
    }

    /// <summary>
    /// quick_hash = **前 N MB 采样** + 文件长度 + 最后写入时间（UTC ticks）的 SHA1。
    /// 采样缓冲与 SHA1 实例**复用**（字段）：整库扫描动辄几万文件，每文件 new 一个 1MB 数组
    /// 会给 GC 添无谓压力。⚠️ 因此**只在单线程扫描里用**（扫描已串在 <c>Task.Run</c> 里）。
    /// </summary>
    private string ComputeQuickHash(string path, long size, long mtimeUtcTicks)
    {
        try
        {
            _sha1.Initialize();
            var wanted = (int)Math.Min(size, SampleBytes);
            if (wanted > 0)
            {
                EnsureSampleBuffer();
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, SampleBytes, FileOptions.SequentialScan);
                var read = 0;
                while (read < wanted)
                {
                    var n = fs.Read(_sampleBuffer!, read, wanted - read);
                    if (n <= 0) break;
                    read += n;
                }
                _sha1.TransformBlock(_sampleBuffer!, 0, read, null, 0);
            }
            var tail = System.Text.Encoding.UTF8.GetBytes($":{size}:{mtimeUtcTicks}");
            _sha1.TransformFinalBlock(tail, 0, tail.Length);
            return "q:" + Convert.ToHexString(_sha1.Hash!).ToLowerInvariant();
        }
        catch { _sha1.Initialize(); return ""; }
    }

    /// <summary>采样字节数 = <see cref="QuickHashSampleMb"/> MB（配置改了下一次扫描生效）。</summary>
    private int SampleBytes => Math.Max(0, QuickHashSampleMb) * 1024 * 1024;

    private void EnsureSampleBuffer()
    {
        var need = SampleBytes;
        if (_sampleBuffer == null || _sampleBuffer.Length < need)
            _sampleBuffer = need == 0 ? Array.Empty<byte>() : new byte[need];
    }

    /// <summary>复用的采样缓冲 / SHA1（⛔ 单线程专用，见 <see cref="ComputeQuickHash"/> 注释）。</summary>
    private byte[]? _sampleBuffer;
    private readonly SHA1 _sha1 = SHA1.Create();

    /// <summary>完整内容哈希（SHA1，前缀 <c>sha1:</c>）：只有显式打开 <see cref="ComputeFullHashForNewFiles"/> 才用。</summary>
    private static string ComputeFullHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA1.Create();
            return "sha1:" + Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        catch { return ""; }
    }

    /// <summary>按扩展名猜资产类型（拿不准归 other；用户可在库内改）。</summary>
    public static string GuessAssetType(string? ext) => (ext ?? "").ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".ts" or ".mpg" or ".mpeg" or ".rmvb" => AssetTypes.Video,
        ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a" or ".ogg" or ".wma" or ".ape" or ".opus" => AssetTypes.Music,
        ".epub" or ".mobi" or ".azw3" or ".pdf" or ".djvu" or ".cbz" or ".cbr" or ".txt" or ".md" => AssetTypes.Book,
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".svg" or ".heic" => AssetTypes.Image,
        ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".odt" or ".ods" or ".rtf" => AssetTypes.Document,
        _ => AssetTypes.Other
    };

    /// <summary>按扩展名给个粗 MIME（够列表显示；拿不准留空）。</summary>
    public static string? GuessMimeType(string? ext) => (ext ?? "").ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4", ".mkv" => "video/x-matroska", ".avi" => "video/x-msvideo", ".webm" => "video/webm",
        ".mp3" => "audio/mpeg", ".flac" => "audio/flac", ".wav" => "audio/wav", ".m4a" => "audio/mp4",
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp",
        ".pdf" => "application/pdf", ".epub" => "application/epub+zip",
        _ => null
    };
}

/// <summary>
/// 一个存储源的**内存匹配索引**（扫描前一次性建好，之后所有匹配都在内存里做）。
/// <para>⛔ 没有它就得每个文件查 3~4 次库 —— 1 万文件 = 4 万次查询，是上一版慢的另一半原因。</para>
/// </summary>
internal sealed class StorageIndex
{
    /// <summary>相对路径 → 位置（同存储源内的主匹配键）。</summary>
    public Dictionary<string, FileLocation> ByRelPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>（大小, 修改时间）→ 位置（零 IO 的"移动"识别）。</summary>
    public Dictionary<(long Size, string Mtime), FileLocation> BySizeMtime { get; } = new();

    /// <summary>（大小, quick_hash）→ 位置（需要读前 N MB 才能匹配）。</summary>
    public Dictionary<(long Size, string QuickHash), FileLocation> ByQuickHash { get; } = new();

    /// <summary>哪些**大小**在库里已有带 quick_hash 的位置（决定要不要花那 1MB 读盘）。</summary>
    private readonly HashSet<long> _sizesWithQuickHash = new();

    /// <summary>库里这个存储源已有多少位置（0 = 首次扫描）。</summary>
    public int Count => ByRelPath.Count;

    public void Add(FileLocation l)
    {
        if (!string.IsNullOrEmpty(l.RelativePath)) ByRelPath[l.RelativePath] = l;
        if (!string.IsNullOrEmpty(l.Mtime)) BySizeMtime[(l.Size, l.Mtime!)] = l;
        if (!string.IsNullOrEmpty(l.QuickHash))
        {
            ByQuickHash[(l.Size, l.QuickHash!)] = l;
            _sizesWithQuickHash.Add(l.Size);
        }
    }

    /// <summary>位置被改写后（移动 / 更新）→ 重建它涉及的索引项。</summary>
    public void Reindex(FileLocation l) => Add(l);

    /// <summary>这个大小在库里有"带 quick_hash"的位置吗（没有就别读那 1MB）。</summary>
    public bool HasQuickHashForSize(long size) => _sizesWithQuickHash.Contains(size);
}
