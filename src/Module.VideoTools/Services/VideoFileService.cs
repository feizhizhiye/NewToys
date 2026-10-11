using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NewToys.Core.Services;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频文件本身的两个"改 / 删"操作（右键菜单里的 CRUD 部分）。
/// <para>
/// 要不要确认、新名字填什么，交给宿主注入的 <see cref="DialogHooks"/>（Core 里的公共钩子）——
/// <c>Core</c> / 模块都不引用 Avalonia，也不引用 Shell，只能这样把"弹窗"外抛
/// （与 <c>PackageManagerService.ConfirmHandler</c> 同款思路，见规范 §9）。
/// 钩子没接上时**一律按"用户取消"处理**：宁可什么都不做，也不能在没有确认的情况下动用户的文件。
/// </para>
/// </summary>
public static class VideoFileService
{
    /// <summary>重命名（同目录内改名，不动内容）。</summary>
    public static async Task<(bool Ok, string Message)> RenameAsync(string fullPath, string currentName)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return (false, "文件已不存在。");

        var input = await DialogHooks.InputAsync("重命名文件", currentName).ConfigureAwait(true);
        if (input == null) return (false, "已取消。");

        var newName = input.Trim();
        if (newName.Length == 0) return (false, "新文件名不能为空。");
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return (false, "新文件名里有非法字符。");
        if (string.Equals(newName, currentName, StringComparison.Ordinal)) return (false, "名字没变。");

        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var target = Path.Combine(dir, newName);

        // 防路径穿越：必须还在同一个目录里
        var full = Path.GetFullPath(target);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            return (false, "只能在同一目录内改名。");

        if (File.Exists(full)) return (false, $"已存在同名文件：{newName}");

        try
        {
            File.Move(fullPath, full);
            return (true, $"已重命名为：{newName}");
        }
        catch (Exception ex) { return (false, "重命名失败：" + ex.Message); }
    }

    /// <summary>
    /// v3.15：详情卡内**直接改名**（不弹输入框，新名字由调用方给）。校验同 <see cref="RenameAsync"/>，
    /// 成功返回新完整路径（调用方据此同步内存里的 <c>VideoItem</c> 与视频库 path）。
    /// </summary>
    public static async Task<(bool Ok, string Message, string? NewPath)> RenameToAsync(string fullPath, string newName)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return (false, "文件已不存在。", null);

        var name = (newName ?? "").Trim();
        if (name.Length == 0) return (false, "新文件名不能为空。", null);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return (false, "新文件名里有非法字符。", null);

        var currentName = Path.GetFileName(fullPath);
        if (string.Equals(name, currentName, StringComparison.Ordinal)) return (false, "名字没变。", null);

        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var target = Path.Combine(dir, name);

        // 防路径穿越：必须还在同一个目录里
        var full = Path.GetFullPath(target);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            return (false, "只能在同一目录内改名。", null);

        if (File.Exists(full)) return (false, $"已存在同名文件：{name}", null);

        try
        {
            File.Move(fullPath, full);
            return (true, $"已重命名为：{name}", full);
        }
        catch (Exception ex) { return (false, "重命名失败：" + ex.Message, null); }
    }

    /// <summary>
    /// 删除视频文件 —— **送进回收站**（不是永久删除），并且必须先过二次确认。
    /// <para>
    /// 用 <c>SHFileOperationW</c>（<c>FO_DELETE | FOF_ALLOWUNDO</c>）而不是 <c>File.Delete</c>：
    /// 这个页面是给"看过信息后决定删掉"用的，误删必须能捡回来。
    /// </para>
    /// </summary>
    public static async Task<(bool Ok, string Message)> DeleteAsync(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return (false, "文件已不存在。");
        if (!OperatingSystem.IsWindows()) return (false, "移入回收站目前只在 Windows 上可用。");

        var name = Path.GetFileName(fullPath);
        if (!await DialogHooks.ConfirmAsync($"确定把「{name}」删除吗？\n文件会被移入**回收站**（可以还原），不会永久删除。")
                .ConfigureAwait(true))
            return (false, "已取消。");

        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = fullPath + "\0\0",   // pFrom 必须是"双 null 结尾"的多字符串
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT)
            };
            var result = SHFileOperationW(ref op);
            if (result != 0) return (false, $"移入回收站失败（错误码 {result}）。");
            if (op.fAnyOperationsAborted != 0) return (false, "已取消。");
            return (true, $"已把「{name}」移入回收站。");
        }
        catch (Exception ex)
        {
            return (false, "删除失败：" + ex.Message);
        }
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
