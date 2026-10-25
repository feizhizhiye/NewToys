using System;
using System.IO;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频文件本身的两个"改 / 删"操作（右键菜单里的 CRUD 部分）。
/// <para>
/// 要不要确认、新名字填什么，交给宿主注入的 <see cref="DialogHooks"/>（Core 里的公共钩子）——
/// <c>Core</c> / 模块都不引用 Avalonia，也不引用 Shell，只能这样把"弹窗"外抛
/// （与 <c>PackageManagerService.ConfirmHandler</c> 同款思路，见规范 §9）。
/// 钩子没接上时**一律按"用户取消"处理**：宁可什么都不做，也不能在没有确认的情况下动用户的文件。
/// </para>
/// <para>
/// **磁盘动作全部下沉到 <see cref="IFileOperationService"/>**（Core）——
/// 这里只剩「弹窗 + 校验 + 文案」。以前这一层自带一份 <c>SHFileOperationW</c> 互操作，
/// 和媒体资产各写一遍，现已删掉，回收站只有一份实现。
/// </para>
/// </summary>
public static class VideoFileService
{
    /// <summary>共享磁盘动作服务（DI 未初始化时为 <c>null</c>，此时一律按失败处理、⛔ 不直接动盘）。</summary>
    private static IFileOperationService? Ops => AppServices.Get<IFileOperationService>();

    /// <summary>重命名（同目录内改名，不动内容）。</summary>
    public static async Task<(bool Ok, string Message)> RenameAsync(string fullPath, string currentName)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return (false, "文件已不存在。");

        var input = await DialogHooks.InputAsync("重命名文件", currentName).ConfigureAwait(true);
        if (input == null) return (false, "已取消。");

        var (ok, msg, _) = RenameTo(fullPath, input);
        return (ok, msg);
    }

    /// <summary>
    /// 详情卡内**直接改名**（不弹输入框，新名字由调用方给）。成功返回新完整路径
    /// （调用方据此同步内存里的 <c>VideoItem</c>）。
    /// </summary>
    public static Task<(bool Ok, string Message, string? NewPath)> RenameToAsync(string fullPath, string newName)
        => Task.FromResult(RenameTo(fullPath, newName));

    /// <summary>
    /// 删除视频文件 —— **送进回收站**（不是永久删除），并且必须先过二次确认。
    /// <para>回收站实现见 <see cref="IFileOperationService.DeleteToRecycle"/>（<c>FOF_ALLOWUNDO</c>，可还原）。</para>
    /// </summary>
    public static async Task<(bool Ok, string Message)> DeleteAsync(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return (false, "文件已不存在。");

        var name = Path.GetFileName(fullPath);
        if (!await DialogHooks.ConfirmAsync($"确定把「{name}」删除吗？\n文件会被移入**回收站**（可以还原），不会永久删除。")
                .ConfigureAwait(true))
            return (false, "已取消。");

        var ops = Ops;
        if (ops == null) return (false, "文件服务未就绪，已取消（没有动磁盘）。");

        var r = ops.DeleteToRecycle(fullPath);
        return r.Ok ? (true, $"已把「{name}」移入回收站。") : (false, r.Message);
    }

    // ==================== 内部 ====================

    private static (bool Ok, string Message, string? NewPath) RenameTo(string fullPath, string newName)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return (false, "文件已不存在。", null);

        var name = (newName ?? "").Trim();
        if (name.Length == 0) return (false, "新文件名不能为空。", null);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return (false, "新文件名里有非法字符。", null);

        var currentName = Path.GetFileName(fullPath);
        if (string.Equals(name, currentName, StringComparison.Ordinal)) return (false, "名字没变。", null);

        // 同名提示留在这一层（共享服务在「跳过」策略下的文案是给批量场景用的）
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        if (File.Exists(Path.Combine(dir, name))) return (false, $"已存在同名文件：{name}", null);

        var ops = Ops;
        if (ops == null) return (false, "文件服务未就绪，已取消（没有动磁盘）。", null);

        var r = ops.Rename(fullPath, name, FileConflictPolicy.Skip);
        // 成功文案保持本页原样（共享服务的文案是给批量场景用的通用措辞）
        return r.Ok ? (true, $"已重命名为：{name}", r.Path) : (false, r.Message, null);
    }
}
