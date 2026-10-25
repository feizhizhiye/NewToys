using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace NewToys.Shared.Services;

/// <summary>
/// 备份节的**归属层级**：决定它落在哪个入口的默认勾选里。
/// <list type="bullet">
///   <item><see cref="Env"/> —— 机器级（绝对路径 / 外部程序），跨机恢复有副作用 ⇒ 默认不勾；</item>
///   <item><see cref="Shell"/> —— 框架偏好（<c>config\shell.json</c>）⇒ 默认勾；</item>
///   <item><see cref="Module"/> —— 模块设置（<c>config\modules\&lt;模块&gt;.json</c>）⇒ 默认勾；</item>
///   <item><see cref="Data"/> —— 数据（库 / 缩略图 / 脚本文件），可能几百 MB ⇒ 默认不勾。</item>
/// </list>
/// </summary>
public enum BackupScope
{
    Env,
    Shell,
    Module,
    Data,
}

/// <summary>
/// 一份可**独立打包 / 独立恢复**的备份节（需求 §3.5）。
/// <para>
/// 🔴 容器是 zip（<c>System.IO.Compression</c>，⛔ 不引第三方压缩库）：
/// db / 缩略图放不进 JSON，zip 还能做到「只恢复其中一节」。
/// </para>
/// <para>
/// 🔴 **每个节自己写一个内层 zip**：<see cref="ExportTo"/> 收到的流是外层 zip 里
/// <c>&lt;id&gt;.zip</c> 这个 entry 的流；<see cref="RestoreFrom"/> 收到的 entry 也是它。
/// 这样「一节 = 多个文件 / 一整个目录」和「一节 = 单个文件」用的是同一套实现，
/// 不必给接口加目录级 API。外层用 <see cref="CompressionLevel.NoCompression"/>，压缩交给节自己决定。
/// </para>
/// <para>
/// ⚠️ 实现放哪：框架节在 Shell（<c>ShellBackupSection</c> / <c>EnvBackupSection</c> /
/// <c>DataBackupSection</c>），模块节在各自 <c>Module.*</c> 里 —— 模块只引 Shared，
/// 经 <c>RegisterServices</c> 注册 <c>services.AddSingleton&lt;IBackupSection, XxxBackupSection&gt;()</c>。
/// </para>
/// </summary>
public interface IBackupSection
{
    /// <summary>节 ID（zip 内 entry 前缀 / manifest 里的键）。发布后⛔ 不改：<c>shell</c> / <c>env</c> / <c>module.&lt;模块键&gt;</c> / <c>data.db</c>。</summary>
    string Id { get; }

    /// <summary>界面标题（「应用与模块设置」这种用户语言）。</summary>
    string Title { get; }

    /// <summary>一句话说明（界面副标题：这一节里到底有什么）。</summary>
    string Description { get; }

    /// <summary>归属层级（决定默认勾选与入口分组）。</summary>
    BackupScope Scope { get; }

    /// <summary>排序（Env 10 / Shell 20 / Module 30+ / Data 90+）。</summary>
    int Order { get; }

    /// <summary>默认是否勾选（Env / Data 一律 false）。</summary>
    bool DefaultSelected { get; }

    /// <summary>本机相关（含绝对路径）⇒ 恢复时提示「这些路径可能在本机失效」。</summary>
    bool IsMachineBound { get; }

    /// <summary>节内容格式版本（manifest 里记录；备份里的版本**高于**当前 ⇒ 拒绝该节，提示升级软件）。</summary>
    int SchemaVersion { get; }

    /// <summary>估算字节数（界面显示用；不要求精确，⛔ 别为此扫全库）。</summary>
    long EstimateBytes();

    /// <summary>把本节内容写成一个**内层 zip**（每个文件一个 entry，entry 名用 <c>/</c> 分隔的相对路径）。</summary>
    void ExportTo(Stream stream);

    /// <summary>从外层 zip 的 entry（= 导出时写的内层 zip）恢复本节。</summary>
    /// <returns>实际写入 / 跳过的条目数。</returns>
    (int Ok, int Skipped) RestoreFrom(ZipArchiveEntry entry);
}

/// <summary>
/// 备份节的公共小工具：路径防御 + 内层 zip 的读写。
/// <para>
/// 🔴 **校验顺序固定**（历史坑）：先判 <c>..</c> / 绝对路径（**拒绝**）→ 再跳过
/// <c>.seeded</c> / <c>.bak</c> / <c>.legacy</c> 这类归档产物。顺序反了，<c>../evil.ps1</c>
/// 会被「跳过后缀」的逻辑静默放过。
/// </para>
/// </summary>
public static class BackupPaths
{
    /// <summary>相对路径是否可安全落到目标目录内（见类型注释里的顺序要求）。</summary>
    public static bool IsSafeRelativePath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return false;

        // ① 绝对路径 / 盘符 / UNC —— 直接拒绝
        if (relative[0] is '/' or '\\') return false;
        if (relative.Length >= 2 && relative[1] == ':') return false;
        if (Path.IsPathRooted(relative)) return false;

        var segments = relative.Split('/', '\\');
        foreach (var seg in segments)
        {
            // ② 路径穿越 —— 先于任何"跳过"判断
            if (seg == ".." || seg == ".") return false;
            if (seg.Length == 0) return false;      // "a//b" 这类畸形分隔
        }

        // ③ 归档 / 种子产物（导入时不该被当成正式数据）
        foreach (var seg in segments)
        {
            if (seg.StartsWith(".bak", StringComparison.OrdinalIgnoreCase)) return false;
            if (seg.StartsWith(".", StringComparison.Ordinal)) return false;          // .legacy 目录 / 隐藏文件
            if (seg.EndsWith(".seeded", StringComparison.OrdinalIgnoreCase)) return false;
            if (seg.EndsWith(".legacy", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    /// <summary>把目录相对路径转成 zip entry 名（统一用 <c>/</c>）。</summary>
    public static string ToEntryName(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');

    /// <summary>把 zip entry 名转成当前平台的相对路径。</summary>
    public static string ToLocalPath(string entryName)
        => entryName.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// 把一个文件写进内层 zip。⛔ 不压缩大文件时传 <see cref="CompressionLevel.NoCompression"/>。
    /// </summary>
    public static void AddFile(ZipArchive zip, string entryName, string sourcePath, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(ToEntryName(entryName), level);
        using var target = entry.Open();
        using var source = File.OpenRead(sourcePath);
        source.CopyTo(target);
    }

    /// <summary>把一段文本写进内层 zip（UTF-8 无 BOM）。</summary>
    public static void AddText(ZipArchive zip, string entryName, string text, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(ToEntryName(entryName), level);
        using var target = entry.Open();
        using var writer = new StreamWriter(target, new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>递归列出目录下的文件（跳过隐藏目录 / <c>.bak*</c> 归档）；目录不存在 ⇒ 空。</summary>
    public static IEnumerable<string> EnumerateFiles(string root, string pattern = "*")
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file);
            if (!IsSafeRelativePath(rel)) continue;
            result.Add(file);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    // ==================== 恢复会话目录 ====================

    // 一次「恢复」里多个数据节要落到**同一个** restore-<时间戳> 目录（否则 db 和缩略图会被拆成两三个同级目录）。
    // 会话由 BackupService.Restore 开 / 关；节只管取。ThreadStatic 够用：Restore 在同一个后台线程里顺序跑各节。
    [ThreadStatic] private static string? _restoreDir;

    /// <summary>
    /// 开一次恢复会话（返回的对象 Dispose 时清空）。
    /// <para>⚠️ **懒建目录**：只备好路径，真正有数据节要落盘时才由 <see cref="CurrentRestoreDir"/> 建 ——
    /// 否则「只恢复设置」也会在 <c>data\</c> 里留一个空目录。</para>
    /// </summary>
    public static IDisposable BeginRestore(string dataDirectory)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(dataDirectory, $"restore-{stamp}");
        for (var i = 1; Directory.Exists(dir) && i < 100; i++)
            dir = Path.Combine(dataDirectory, $"restore-{stamp}-{i}");
        _restoreDir = dir;
        return new RestoreScope();
    }

    /// <summary>本次恢复的落点目录（没开会话 ⇒ 现算一个）。返回前保证目录已存在。</summary>
    public static string CurrentRestoreDir(string dataDirectory)
    {
        var dir = _restoreDir;
        if (string.IsNullOrEmpty(dir))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            dir = Path.Combine(dataDirectory, $"restore-{stamp}");
            for (var i = 1; Directory.Exists(dir) && i < 100; i++)
                dir = Path.Combine(dataDirectory, $"restore-{stamp}-{i}");
            _restoreDir = dir;
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class RestoreScope : IDisposable
    {
        public void Dispose() => _restoreDir = null;
    }
}
