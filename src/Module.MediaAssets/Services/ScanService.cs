using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 扫描服务（需求 §6.6 核心流程·扫描）：目录遍历 → 忽略规则 → 四级匹配 → 建 / 更新资产与位置 →
/// 本次未出现的旧位置标 <c>missing</c>（⛔ 不删资产）。
/// <para>
/// 匹配顺序：① 存储源+相对路径 → ② quick_hash+size → ③ inode+device → ④ 完整 hash+size → ⑤ 全不中 = 新建。
/// ⚠️ 步骤 ③ 的 inode 读取**暂未实现**（存 null）：Windows 上要 <c>GetFileInformationByHandle</c> 的 P/Invoke，
/// 留待物理操作需要识别「移动」时再补（NT-019）；前三步已覆盖绝大多数场景。
/// </para>
/// <para>
/// ⚠️ 元数据 / 缩略图**不在本项**（NT-021）：本类只留 <c>enricher</c> 回调挂钩，扫描到新文件时调用它。
/// </para>
/// </summary>
public class ScanService
{
    private readonly AssetService _assets;

    public ScanService(AssetService assets) => _assets = assets;

    /// <summary>扫描时跳过的目录名（用户可配，见 NT-021 设置页）。</summary>
    public HashSet<string> IgnoreDirectoryNames { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "$RECYCLE.BIN", "System Volume Information",
        ".cache", "AppData", ".thumbnails"
    };

    /// <summary>扫描时跳过的扩展名（临时 / 系统文件）。</summary>
    public HashSet<string> IgnoreExtensions { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".part", ".crdownload", ".partial", ".lnk", ".ini", ".db", ".db-wal", ".db-shm"
    };

    /// <summary>quick_hash 采样大小（MB）：默认 1 MB，配得越大越准、扫描越慢。</summary>
    public int QuickHashSampleMb { get; set; } = 1;

    /// <summary>单个存储源扫描完成后才做 missing 校准；进度文本交给调用方显示。</summary>
    public bool IsAvailable => _assets.IsAvailable;

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
        if (storageId is { } sid) storages = storages.Where(s => s.Id == sid).ToList();
        else storages = storages.Where(s => s.Online).ToList();

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

                // ⚠️ 先取「本次开始时刻」：扫描收尾用它判「本次没见到」→ missing
                var scannedFrom = AssetService.Now();

                await Task.Run(() =>
                {
                    foreach (var file in EnumerateFiles(storage.RootPath, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        job.TotalFiles++;
                        try
                        {
                            IndexOne(storage, file, job, enricher, ct);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            job.ErrorCount++;
                            _assets.AddScanError(job.Id, file, "io", ex.Message);
                        }

                        if (job.TotalFiles % 50 == 0)
                            progress?.Report($"「{storage.Name}」已扫描 {job.TotalFiles} 个文件（新增 {job.AddedFiles} / 更新 {job.UpdatedFiles}）");
                    }
                }, ct).ConfigureAwait(false);

                // 收尾：本次没见到的旧位置 → missing（⛔ 不删资产）
                var missing = _assets.MarkMissingLocations(storage.Id, scannedFrom);
                job.MissingFiles += missing;
                progress?.Report($"「{storage.Name}」完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {missing}");
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

    /// <summary>
    /// 导入**指定的一批文件**（工具条「📂 导入 → 文件…」）：按所在目录各自登记 / 复用存储源后逐个入库。
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
        if (files.Count == 0)
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

                    foreach (var f in list)
                    {
                        ct.ThrowIfCancellationRequested();
                        job.TotalFiles++;
                        try
                        {
                            var info = new FileInfo(f);
                            if (!info.Exists) { job.ErrorCount++; continue; }
                            IndexOne(storage, f, job, enricher, ct);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            job.ErrorCount++;
                            _assets.AddScanError(job.Id, f, "io", ex.Message);
                        }
                        progress?.Report($"已导入 {job.TotalFiles} / {files.Count}（新增 {job.AddedFiles} / 更新 {job.UpdatedFiles}）");
                    }
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

    // ==================== 内部：单个文件入库 ====================

    private void IndexOne(StorageSource storage, string fullPath, ScanJob job,
                          Func<FileLocation, CancellationToken, Task>? enricher, CancellationToken ct)
    {
        var info = new FileInfo(fullPath);
        var size = info.Length;
        var mtime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        var relative = MakeRelative(storage.RootPath, fullPath);
        var ext = info.Extension;
        var quickHash = ComputeQuickHash(fullPath, size, info.LastWriteTimeUtc.Ticks);

        // ① 存储源 + 相对路径（同存储源内最可靠）
        var loc = _assets.FindLocationByRelativePath(storage.Id, relative);
        if (loc != null)
        {
            // 命中：刷新文件属性（大小 / mtime / 哈希），资产本身不动
            loc.Path = fullPath;
            loc.FileName = info.Name;
            loc.Size = size;
            loc.Mtime = mtime;
            loc.QuickHash = quickHash;
            loc.Ext = ext;
            loc.Status = LocationStatuses.Active;
            loc.LastSeenAt = AssetService.Now();
            _assets.UpdateLocation(loc);
            job.ScannedFiles++;
            job.UpdatedFiles++;
            return;
        }

        // ② quick_hash + size：同内容出现在新路径 → 视为**移动**（⛔ 不新建 + 不丢旧）
        loc = _assets.FindLocationByQuickHash(quickHash, size);
        if (loc != null)
        {
            ApplyMove(loc, storage, fullPath, relative, info, size, mtime, quickHash, ext);
            job.ScannedFiles++;
            job.UpdatedFiles++;
            return;
        }

        // ③ inode + device：暂未实现（见类注释）——保留判断位置，将来补上即可
        //    loc = _assets.FindLocationByInode(inode, device);

        // ④ 无匹配 → 算完整 hash 再匹配一次（同内容、不同前缀的极端情况）
        var fullHash = ComputeFullHash(fullPath);
        loc = _assets.FindLocationByHash(fullHash, size);
        if (loc != null)
        {
            ApplyMove(loc, storage, fullPath, relative, info, size, mtime, quickHash, ext);
            loc.Hash = fullHash;
            _assets.UpdateLocation(loc);
            job.ScannedFiles++;
            job.UpdatedFiles++;
            return;
        }

        // ⑤ 全新文件：建资产 + 建主位置
        var type = GuessAssetType(ext);
        var asset = new Asset
        {
            Type = type,
            Title = Path.GetFileNameWithoutExtension(info.Name),
            Status = AssetStatuses.Active
        };
        var assetId = _assets.InsertAsset(asset);
        if (assetId == 0) throw new IOException("资产写入失败（数据库不可用？）");

        var newLoc = new FileLocation
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
            Hash = fullHash,
            QuickHash = quickHash,
            IsPrimary = true,
            Status = LocationStatuses.Active,
            LastSeenAt = AssetService.Now()
        };
        newLoc.Id = _assets.InsertLocation(newLoc);

        job.ScannedFiles++;
        job.AddedFiles++;

        // 元数据 / 缩略图挂钩（NT-021 注入；传 null 即不做事）
        if (enricher != null && newLoc.Id != 0)
            enricher(newLoc, ct).GetAwaiter().GetResult();
    }

    /// <summary>把已有位置改到新路径（识别为移动 / 改名）：更新路径字段 + 复位 missing。</summary>
    private void ApplyMove(FileLocation loc, StorageSource storage, string fullPath, string relative,
                           FileInfo info, long size, string mtime, string quickHash, string ext)
    {
        loc.StorageId = storage.Id;
        loc.Path = fullPath;
        loc.RelativePath = relative;
        loc.FileName = info.Name;
        loc.Ext = ext;
        loc.Size = size;
        loc.Mtime = mtime;
        loc.QuickHash = quickHash;
        loc.Status = LocationStatuses.Active;
        loc.LastSeenAt = AssetService.Now();
        _assets.UpdateLocation(loc);
    }

    // ==================== 内部：遍历 / 忽略 / 哈希 ====================

    /// <summary>
    /// 手写遍历（⛔ 不用 <c>Directory.EnumerateFiles(..., AllDirectories)</c>：它遇到**一个**没权限的
    /// 子目录就整体抛异常，整次扫描会断）。这里逐目录 try-catch：读不了的目录跳过，其余照常。
    /// </summary>
    private IEnumerable<string> EnumerateFiles(string root, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { continue; }                       // 目录读不了 → 跳过

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (IgnoreDirectoryNames.Contains(name)) continue;
                // 隐藏 / 系统目录跳过（回收站、卷影等）
                try
                {
                    var attrs = File.GetAttributes(sub);
                    if (attrs.HasFlag(FileAttributes.System)) continue;
                }
                catch { continue; }
                stack.Push(sub);
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var f in files)
            {
                if (IgnoreExtensions.Contains(Path.GetExtension(f))) continue;
                yield return f;
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
    /// 为什么采样：整库扫描时全量读盘不可接受；同内容文件只要前缀一致就必然撞上（采样大小可配）。
    /// <para>
    /// ⚠️ 采样缓冲与 SHA1 实例**复用**（字段）：整库扫描动辄几万文件，每文件 new 一个 1MB 数组
    /// 会给 GC 添无谓压力。⚠️ 因此**只在单线程扫描里用**（<see cref="ScanAsync"/> 已串在 <c>Task.Run</c> 里）。
    /// </para>
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
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

    /// <summary>完整内容哈希（SHA1，前缀 <c>sha1:</c>）：只在 ①②③ 都没命中时算，代价高。</summary>
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
