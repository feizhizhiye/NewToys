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
///         完整哈希与元数据一起交给任务队列的「补全哈希」任务；需要立即算可开
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
/// ⚠️ 元数据 / 缩略图**不在本项**：本类只留 <c>enricher</c> 回调挂钩，扫描到新文件时调用它。
/// </para>
/// </summary>
public partial class ScanService
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

    /// <summary>扫描时跳过的目录名（用户可配，见 设置页）。</summary>
    public HashSet<string> IgnoreDirectoryNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "$RECYCLE.BIN", "System Volume Information",
        ".cache", "AppData", ".thumbnails"
    };

    /// <summary>扫描时跳过的扩展名（临时 / 系统文件；用户可配，见 设置页）。</summary>
    public HashSet<string> IgnoreExtensions { get; private set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".part", ".crdownload", ".partial", ".lnk", ".ini", ".db", ".db-wal", ".db-shm"
    };

    /// <summary>忽略隐藏文件 / 目录（文件属性 Hidden；用户可配）。</summary>
    public bool IgnoreHiddenFiles { get; private set; } = true;

    /// <summary>忽略临时文件（<c>~$*</c> Office 临时副本等；用户可配）。</summary>
    public bool IgnoreTempFiles { get; private set; } = true;

    /// <summary>
    /// 新文件入库后的**增强回调**（元数据提取 + 缩略图生成）。
    /// 由模块初始化时接线（读设置的自动开关）；传 null 即跳过。
    /// ⚠️ 只在**新位置**批量落库后调（⛔ 不对"已见过"的位置重复跑）。
    /// </summary>
    public Func<FileLocation, CancellationToken, Task>? Enricher { get; set; }

    /// <summary>
    /// 把模块设置应用到扫描器（忽略规则 / 采样大小）。设置页保存后由调用方调一次。
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
    /// 正式用途 = 需要立刻得到内容指纹做去重时；常规索引交给 的异步「补全哈希」。
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
    /// <paramref name="enricher"/>：新文件入库后回调（接元数据 + 缩略图；传 null 即跳过）。
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

}
