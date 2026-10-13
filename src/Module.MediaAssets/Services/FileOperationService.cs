using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>目标已存在时的处理策略（需求 §6.6「同名冲突有策略」）。</summary>
public enum ConflictPolicy
{
    /// <summary>追加序号：<c>名字 (1).ext</c>、<c>名字 (2).ext</c>…</summary>
    AppendNumber,

    /// <summary>追加原路径短哈希后 6 位（稳定、可重复）。</summary>
    ShortHash,

    /// <summary>跳过（不动磁盘，也不改库）。</summary>
    Skip,

    /// <summary>覆盖（危险：目标文件直接没了）。</summary>
    Overwrite
}

/// <summary>
/// 物理文件操作（NT-019）：重命名 / 移动 / 复制 / 删除 —— **这是全模块唯一动磁盘的地方**。
/// <para>
/// 🔴 <b>日志先行</b>（需求 §6.6 核心流程）：<c>operation_logs(pending)</c> → 动磁盘 → 更新 <c>file_locations</c>
/// → 校验文件是否真在 → 改 <c>success</c> / <c>failed</c>（失败留 <c>before_json</c>）。
/// </para>
/// <para>
/// 🔴 <b>防穿越</b>：重命名只允许**改文件名**（⛔ 不许含路径分隔符）；移动 / 复制要求目标目录落在
/// **已登记存储源**的根内 —— 否则拒绝（不猜、不越界写盘）。
/// </para>
/// <para>⛔ <b>失败不自动回滚磁盘</b>：失败态写进日志，提示用户手动补偿；数据库没跟上时下次扫描按 hash / inode 校准。</para>
/// <para>⛔ 删除默认**送系统回收站**（<c>SHFileOperationW</c>，视频工具 <c>VideoFileService</c> 先例）；
/// 只有「永久删除」才真删，且必须二次确认（由调用方做）。</para>
/// </summary>
public class FileOperationService
{
    private readonly AssetService _assets;
    private readonly OperationLogService _log;

    public FileOperationService(AssetService assets, OperationLogService log, TaskQueue queue)
    {
        _assets = assets;
        _log = log;
        Queue = queue;
    }

    /// <summary>任务队列（物理操作走它 → 任务中心可见、可取消；需求 §6.6）。</summary>
    public TaskQueue Queue { get; }

    public bool IsAvailable => _assets?.IsAvailable ?? false;

    // ==================== ① 重命名（同目录改文件名） ====================

    /// <summary>
    /// 重命名磁盘文件（只改**文件名**，⛔ 不许带路径）。返回新全路径（失败时 <c>null</c>）。
    /// </summary>
    public (bool Ok, string Message, string? NewPath) Rename(FileLocation loc, string newName, ConflictPolicy policy = ConflictPolicy.AppendNumber)
    {
        if (loc == null || string.IsNullOrWhiteSpace(loc.Path)) return (false, "没有可操作的文件位置。", null);

        var oldPath = loc.Path;
        if (!File.Exists(oldPath)) return (false, $"磁盘上找不到文件：{oldPath}", null);

        newName = (newName ?? "").Trim();
        if (newName.Length == 0) return (false, "新文件名不能为空。", null);
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Contains(Path.DirectorySeparatorChar) || newName.Contains(Path.AltDirectorySeparatorChar))
            return (false, "新文件名里有非法字符（⛔ 不能带路径分隔符 —— 只改名字，不搬家）。", null);

        var dir = Path.GetDirectoryName(oldPath) ?? "";
        var ext = Path.GetExtension(oldPath);
        // 用户可能只写了主名 → 自动补回原扩展名（"改名"最常见的心智）
        if (Path.GetExtension(newName).Length == 0 && ext.Length > 0) newName += ext;

        var target = ResolveTarget(dir, newName, policy, oldPath);
        if (target == null) return (false, "目标文件名已存在，按「跳过」策略没做任何改动。", null);
        if (string.Equals(target, oldPath, StringComparison.OrdinalIgnoreCase)) return (true, "文件名没变。", oldPath);

        var logId = _log.Begin("rename", "file_location", loc.Id, beforeJson: $"{{\"path\":\"{Esc(oldPath)}\"}}");
        try
        {
            File.Move(oldPath, target);
        }
        catch (Exception ex)
        {
            _log.Finish(logId, false, ex.Message);
            return (false, $"改磁盘文件名失败：{ex.Message}", null);
        }

        if (!File.Exists(target))
        {
            _log.Finish(logId, false, "磁盘动作后目标文件不存在");
            return (false, "磁盘动作完成但目标文件不存在 —— 请手动检查。", null);
        }

        UpdateLocationRow(loc, target);
        _log.Finish(logId, true, null, $"{{\"path\":\"{Esc(target)}\"}}");
        return (true, $"已把磁盘文件改名为「{Path.GetFileName(target)}」，库里的路径同步好了。", target);
    }

    // ==================== ② 移动 / ③ 复制（换目录） ====================

    /// <summary>移动到另一个目录（必须是**已登记存储源**内的目录）。返回新全路径。</summary>
    public (bool Ok, string Message, string? NewPath) Move(FileLocation loc, string targetDir, ConflictPolicy policy = ConflictPolicy.AppendNumber)
    {
        var check = ResolveTargetDir(loc, targetDir, out var storageId, out var root);
        if (!check.Ok) return (false, check.Message, null);
        var src = loc!.Path;
        if (!File.Exists(src)) return (false, $"磁盘上找不到文件：{src}", null);

        var target = ResolveTarget(targetDir, Path.GetFileName(src), policy, src);
        if (target == null) return (false, "目标目录里已有同名文件，按「跳过」策略没做任何改动。", null);

        var logId = _log.Begin("move", "file_location", loc.Id,
            beforeJson: $"{{\"path\":\"{Esc(src)}\",\"storage\":{loc.StorageId}}}");
        try
        {
            File.Move(src, target);
        }
        catch (Exception ex)
        {
            _log.Finish(logId, false, ex.Message);
            return (false, $"移动失败：{ex.Message}", null);
        }

        if (!File.Exists(target))
        {
            _log.Finish(logId, false, "磁盘动作后目标文件不存在");
            return (false, "移动完成但目标文件不存在 —— 请手动检查。", null);
        }

        UpdateLocationRow(loc, target, storageId, root);
        _log.Finish(logId, true, null, $"{{\"path\":\"{Esc(target)}\",\"storage\":{storageId}}}");
        return (true, $"已移动到「{targetDir}」。", target);
    }

    /// <summary>
    /// 复制到另一个目录 —— **新增一个文件位置**（⛔ 不搬走原文件；转码/整理常用）。
    /// 返回新位置 id（0 = 失败）。
    /// </summary>
    public (bool Ok, string Message, long NewLocationId) Copy(FileLocation loc, string targetDir, ConflictPolicy policy = ConflictPolicy.AppendNumber)
    {
        var check = ResolveTargetDir(loc, targetDir, out var storageId, out var root);
        if (!check.Ok) return (false, check.Message, 0);
        var src = loc!.Path;
        if (!File.Exists(src)) return (false, $"磁盘上找不到文件：{src}", 0);

        var target = ResolveTarget(targetDir, Path.GetFileName(src), policy, src);
        if (target == null) return (false, "目标目录里已有同名文件，按「跳过」策略没做任何改动。", 0);

        var logId = _log.Begin("copy", "file_location", loc.Id, beforeJson: $"{{\"path\":\"{Esc(src)}\"}}");
        try
        {
            File.Copy(src, target, overwrite: policy == ConflictPolicy.Overwrite);
        }
        catch (Exception ex)
        {
            _log.Finish(logId, false, ex.Message);
            return (false, $"复制失败：{ex.Message}", 0);
        }

        // 新位置（同一资产多一份拷贝；⛔ 不是主位置）
        var info = new FileInfo(target);
        var newLoc = new FileLocation
        {
            AssetId = loc.AssetId,
            StorageId = storageId,
            Path = target,
            FileName = info.Name,
            Ext = info.Extension,
            Size = info.Length,
            Mtime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
            RelativePath = SafeRelative(root, target),
            IsPrimary = false,
            Status = LocationStatuses.Active,
            LastSeenAt = AssetService.Now()
        };
        var newId = _assets.InsertLocation(newLoc);

        _log.Finish(logId, newId > 0, newId > 0 ? null : "写入 file_locations 失败",
            $"{{\"path\":\"{Esc(target)}\",\"location\":{newId}}}");
        return newId > 0
            ? (true, $"已复制到「{targetDir}」并登记为同一资产的新位置。", newId)
            : (false, "文件复制过去了，但登记新位置失败（下次扫描会补上）。", 0);
    }

    // ==================== ④ 删除 ====================

    /// <summary>
    /// 把磁盘文件**送进系统回收站**（<c>SHFileOperationW | FOF_ALLOWUNDO</c>），
    /// 并把该位置标成 <c>deleted</c>（⛔ 不删数据库行）。二次确认由调用方负责。
    /// </summary>
    public (bool Ok, string Message) DeleteToRecycle(FileLocation loc)
    {
        if (loc == null || string.IsNullOrWhiteSpace(loc.Path)) return (false, "没有可操作的文件位置。");
        var path = loc.Path;
        if (!File.Exists(path))
        {
            // 文件本来就不在了 → 只把库里状态标掉，不算失败（避免用户卡在一条幽灵记录上）
            MarkDeleted(loc);
            return (false, $"磁盘上已经没有这个文件了（已把库里位置标成「已删除」）。");
        }
        if (!OperatingSystem.IsWindows()) return (false, "移入回收站目前只在 Windows 上可用。");

        var name = Path.GetFileName(path);
        var logId = _log.Begin("delete", "file_location", loc.Id, beforeJson: $"{{\"path\":\"{Esc(path)}\"}}");
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",   // pFrom 必须是"双 null 结尾"的多字符串
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT)
            };
            var result = SHFileOperationW(ref op);
            if (result != 0)
            {
                var msg = $"移入回收站失败（错误码 {result}）。";
                _log.Finish(logId, false, msg);
                return (false, msg);
            }
            if (op.fAnyOperationsAborted != 0)
            {
                _log.Finish(logId, false, "用户在系统对话框里取消了");
                return (false, "已取消。");
            }
        }
        catch (Exception ex)
        {
            _log.Finish(logId, false, ex.Message);
            return (false, "删除失败：" + ex.Message);
        }

        MarkDeleted(loc);
        _log.Finish(logId, true, null, "{\"status\":\"deleted\"}");
        return (true, $"已把「{name}」移入系统回收站（库里的位置标成「已删除」）。");
    }

    /// <summary>
    /// **永久删除**（真删，不进回收站）—— 只在回收站的「永久删除 / 清空」里用，且必须二次确认。
    /// </summary>
    public (bool Ok, string Message) PermanentDelete(FileLocation loc)
    {
        if (loc == null || string.IsNullOrWhiteSpace(loc.Path)) return (false, "没有可操作的文件位置。");
        var path = loc.Path;

        var logId = _log.Begin("purge", "file_location", loc.Id, beforeJson: $"{{\"path\":\"{Esc(path)}\"}}");
        if (!File.Exists(path))
        {
            MarkDeleted(loc);
            _log.Finish(logId, true, null, "{\"note\":\"文件已不存在\"}");
            return (true, "磁盘上已经没有这个文件了（已把库里位置标成「已删除」）。");
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _log.Finish(logId, false, ex.Message);
            return (false, $"永久删除失败：{ex.Message}");
        }

        MarkDeleted(loc);
        _log.Finish(logId, true, null, "{\"status\":\"deleted\",\"permanent\":true}");
        return (true, "已永久删除（不进回收站，无法还原）。");
    }

    // ==================== 内部 ====================

    private void MarkDeleted(FileLocation loc)
    {
        loc.Status = LocationStatuses.Deleted;
        loc.LastSeenAt = AssetService.Now();
        _assets.UpdateLocation(loc);
    }

    /// <summary>把磁盘上的新路径同步进 <c>file_locations</c>（路径 / 相对路径 / 文件名 / 扩展名 / 大小 / mtime）。</summary>
    private void UpdateLocationRow(FileLocation loc, string newPath, long? newStorageId = null, string? root = null)
    {
        var info = new FileInfo(newPath);
        var storageId = newStorageId ?? loc.StorageId;
        var storageRoot = root ?? _assets.ListStorages().FirstOrDefault(s => s.Id == storageId)?.RootPath;

        loc.Path = newPath;
        loc.FileName = info.Name;
        loc.Ext = info.Extension;
        loc.Size = info.Length;
        loc.Mtime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        loc.StorageId = storageId;
        loc.RelativePath = SafeRelative(storageRoot, newPath);
        loc.Status = LocationStatuses.Active;
        loc.LastSeenAt = AssetService.Now();
        _assets.UpdateLocation(loc);
    }

    /// <summary>目标目录校验：必须落在**已登记存储源**根内（防穿越）。</summary>
    private (bool Ok, string Message) ResolveTargetDir(FileLocation loc, string targetDir, out long storageId, out string root)
    {
        storageId = 0;
        root = "";
        if (loc == null) return (false, "没有可操作的文件位置。");
        if (string.IsNullOrWhiteSpace(targetDir)) return (false, "先选一个目标目录。");

        string full;
        try { full = Path.GetFullPath(targetDir); }
        catch (Exception ex) { return (false, "目标路径不合法：" + ex.Message); }

        if (!Directory.Exists(full)) return (false, $"目标目录不存在：{full}");

        var hit = _assets.ListStorages()
            .FirstOrDefault(s => IsInside(s.RootPath, full));
        if (hit == null)
            return (false, $"目标目录不在任何已登记存储源内（{full}）—— 先在「全部资产」页把它登记进来，⛔ 我不越界写盘。");

        if (!hit.Online) return (false, $"目标存储源「{hit.Name}」当前离线。");

        storageId = hit.Id;
        root = hit.RootPath;
        return (true, "");
    }

    /// <summary>路径是否落在根目录内（比字符串前缀更严：带分隔符 + 忽略大小写）。</summary>
    public static bool IsInside(string? root, string? path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return false;
        string r, p;
        try
        {
            r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return false; }

        if (string.Equals(r, p, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(r + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeRelative(string? root, string path)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        try { return Path.GetRelativePath(root!, path); }
        catch { return null; }
    }

    /// <summary>
    /// 解析最终目标路径（应用冲突策略）。返回 <c>null</c> = 按"跳过"策略不改动。
    /// </summary>
    private static string? ResolveTarget(string dir, string fileName, ConflictPolicy policy, string sourcePath)
    {
        var target = Path.Combine(dir, fileName);
        if (!File.Exists(target)) return target;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        switch (policy)
        {
            case ConflictPolicy.Skip:
                return null;

            case ConflictPolicy.Overwrite:
                return target;

            case ConflictPolicy.ShortHash:
                var h = Fnv(sourcePath).ToString("x8");
                var hTarget = Path.Combine(dir, $"{stem}_{h}{ext}");
                return File.Exists(hTarget) ? null : hTarget;

            default:
                for (var i = 1; i < 1000; i++)
                {
                    var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                    if (!File.Exists(candidate)) return candidate;
                }
                return null;
        }
    }

    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (var ch in s) { h ^= ch; h *= 16777619; }
        return h;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "'");

    // ==================== Win32 互操作（与视频工具 VideoFileService 同一套写法） ====================

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);
}
