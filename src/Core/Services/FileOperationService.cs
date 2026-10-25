using System;
using System.IO;
using System.Runtime.InteropServices;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IFileOperationService"/> 的唯一实现 —— **纯磁盘动作**。
/// <para>
/// 迁移来源：媒体资产 <c>Module.MediaAssets/Services/FileOperationService</c> 里的磁盘部分
/// + 视频工具 <c>VideoFileService</c> 里的 <c>SHFileOperationW</c> 块（两处各写过一遍，现已合一）。
/// 库表记账 / 弹窗确认**留在原模块**，这里不认识任何数据库。
/// </para>
/// </summary>
public sealed class FileOperationService : IFileOperationService
{
    public bool IsRecycleBinSupported => OperatingSystem.IsWindows();

    // ==================== ① 改名 ====================

    public FileOpResult Rename(string fullPath, string newName, FileConflictPolicy policy = FileConflictPolicy.AppendNumber)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return FileOpResult.Fail("没有可操作的文件。");
        if (!File.Exists(fullPath)) return FileOpResult.Fail($"磁盘上找不到文件：{fullPath}");

        var name = (newName ?? "").Trim();
        if (name.Length == 0) return FileOpResult.Fail("新文件名不能为空。");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return FileOpResult.Fail("新文件名里有非法字符（⛔ 不能带路径分隔符 —— 只改名字，不搬家）。");

        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var ext = Path.GetExtension(fullPath);
        // 用户可能只写了主名 → 自动补回原扩展名（"改名"最常见的心智）
        if (Path.GetExtension(name).Length == 0 && ext.Length > 0) name += ext;

        var target = ResolveTarget(dir, name, policy, fullPath);
        if (target == null) return FileOpResult.Fail("目标文件名已存在，按「跳过」策略没做任何改动。");
        if (string.Equals(target, fullPath, StringComparison.OrdinalIgnoreCase))
            return FileOpResult.Success("文件名没变。", fullPath);

        try
        {
            File.Move(fullPath, target);
        }
        catch (Exception ex)
        {
            return FileOpResult.Fail($"改磁盘文件名失败：{ex.Message}");
        }

        return File.Exists(target)
            ? FileOpResult.Success($"已把磁盘文件改名为「{Path.GetFileName(target)}」。", target)
            : FileOpResult.Fail("磁盘动作完成但目标文件不存在 —— 请手动检查。");
    }

    // ==================== ② 移动 ====================

    public FileOpResult Move(string sourcePath, string targetDir, FileConflictPolicy policy = FileConflictPolicy.AppendNumber)
    {
        var check = ValidateTargetDir(targetDir);
        if (check != null) return FileOpResult.Fail(check);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return FileOpResult.Fail($"磁盘上找不到文件：{sourcePath}");

        var target = ResolveTarget(targetDir, Path.GetFileName(sourcePath), policy, sourcePath);
        if (target == null) return FileOpResult.Fail("目标目录里已有同名文件，按「跳过」策略没做任何改动。");

        try
        {
            File.Move(sourcePath, target);
        }
        catch (Exception ex)
        {
            return FileOpResult.Fail($"移动失败：{ex.Message}");
        }

        return File.Exists(target)
            ? FileOpResult.Success($"已移动到「{targetDir}」。", target)
            : FileOpResult.Fail("移动完成但目标文件不存在 —— 请手动检查。");
    }

    // ==================== ③ 复制 ====================

    public FileOpResult Copy(string sourcePath, string targetDir, FileConflictPolicy policy = FileConflictPolicy.AppendNumber)
    {
        var check = ValidateTargetDir(targetDir);
        if (check != null) return FileOpResult.Fail(check);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return FileOpResult.Fail($"磁盘上找不到文件：{sourcePath}");

        var target = ResolveTarget(targetDir, Path.GetFileName(sourcePath), policy, sourcePath);
        if (target == null) return FileOpResult.Fail("目标目录里已有同名文件，按「跳过」策略没做任何改动。");

        try
        {
            File.Copy(sourcePath, target, overwrite: policy == FileConflictPolicy.Overwrite);
        }
        catch (Exception ex)
        {
            return FileOpResult.Fail($"复制失败：{ex.Message}");
        }

        return File.Exists(target)
            ? FileOpResult.Success($"已复制到「{targetDir}」。", target)
            : FileOpResult.Fail("复制完成但目标文件不存在 —— 请手动检查。");
    }

    // ==================== ④ 删除（回收站 / 永久） ====================

    public FileOpResult DeleteToRecycle(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return FileOpResult.Fail("没有可操作的文件。");
        // 文件本来就不在了 → 幂等成功（避免用户卡在一条幽灵记录上）
        if (!File.Exists(fullPath)) return FileOpResult.Success("磁盘上已经没有这个文件了。", null);
        if (!IsRecycleBinSupported) return FileOpResult.Fail("移入回收站目前只在 Windows 上可用。");

        var name = Path.GetFileName(fullPath);
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = fullPath + "\0\0",   // pFrom 必须是"双 null 结尾"的多字符串
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT)
            };
            var result = SHFileOperationW(ref op);
            if (result != 0) return FileOpResult.Fail($"移入回收站失败（错误码 {result}）。");
            if (op.fAnyOperationsAborted != 0) return FileOpResult.Fail("用户在系统对话框里取消了。");
        }
        catch (Exception ex)
        {
            return FileOpResult.Fail("删除失败：" + ex.Message);
        }

        return FileOpResult.Success($"已把「{name}」移入系统回收站（可以还原）。", null);
    }

    public FileOpResult PermanentDelete(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return FileOpResult.Fail("没有可操作的文件。");
        if (!File.Exists(fullPath)) return FileOpResult.Success("磁盘上已经没有这个文件了。", null);

        try
        {
            File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            return FileOpResult.Fail($"永久删除失败：{ex.Message}");
        }

        return FileOpResult.Success("已永久删除（不进回收站，无法还原）。", null);
    }

    // ==================== ⑤ 目录 ====================

    public string EnsureDirectory(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "";
        var full = Path.GetFullPath(dir);
        if (!Directory.Exists(full)) Directory.CreateDirectory(full);
        return full;
    }

    public bool IsInside(string? root, string? path)
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

    // ==================== 内部 ====================

    /// <summary>目标目录校验：非空 / 合法 / 已存在。返回 <c>null</c> = 通过。</summary>
    private static string? ValidateTargetDir(string targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir)) return "先选一个目标目录。";
        try
        {
            var full = Path.GetFullPath(targetDir);
            return Directory.Exists(full) ? null : $"目标目录不存在：{full}";
        }
        catch (Exception ex)
        {
            return "目标路径不合法：" + ex.Message;
        }
    }

    /// <summary>解析最终目标路径（应用冲突策略）。返回 <c>null</c> = 按「跳过」策略不改动。</summary>
    private static string? ResolveTarget(string dir, string fileName, FileConflictPolicy policy, string sourcePath)
    {
        var target = Path.Combine(dir, fileName);
        if (!File.Exists(target)) return target;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        switch (policy)
        {
            case FileConflictPolicy.Skip:
                return null;

            case FileConflictPolicy.Overwrite:
                return target;

            case FileConflictPolicy.ShortHash:
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

    // ==================== Win32 互操作 ====================

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
