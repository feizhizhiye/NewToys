using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>目录里的一个条目（实时枚举结果，⛔ 与索引无关）。</summary>
public sealed record FsEntry(string Path, string Name, bool IsFolder, long Size, string Mtime, string Type);

/// <summary>一次目录枚举的结果（子目录与文件分开，界面按「文件夹在前」排序）。</summary>
public sealed class BrowseDirContent
{
    public string Path { get; init; } = "";

    /// <summary>目录可读（false ⇒ 权限 / IO 异常，界面提示「无法访问」而不是空目录）。</summary>
    public bool Accessible { get; set; } = true;

    public List<FsEntry> Dirs { get; } = new();

    public List<FsEntry> Files { get; } = new();
}

/// <summary>「资产浏览」页的磁盘访问层：资源管理器口径 —— 树 = 盘符 / 共享根 + 逐层子目录（懒加载）， 右侧列表 = 该目录的实时内容。索引（AssetService.LoadBrowseEntries）只用来给条目 叠加「是否已入库」与标签 / 元数据，⛔ 不再决定能看到什么。（WHY: MA-012）</summary>
public class AssetBrowseService
{
    /// <summary>「（根目录）」显示名：详情卡里相对目录为空时的兜底文案。</summary>
    public const string RootDirName = "（根目录）";

    private readonly AssetService _assets;

    public AssetBrowseService(AssetService assets) => _assets = assets;

    /// <summary>取全部可浏览位置行（库不可用 → 空表，页面按「全部未入库」处理）。</summary>
    public List<BrowseEntry> LoadEntries() => _assets.LoadBrowseEntries();

    // ==================== 树根 / 子目录 ====================

    /// <summary>
    /// 树根 = 所有可访问盘符 + **不在任何盘符下的**存储源根（UNC 共享等）。
    /// 盘符枚举失败（无权限 / 光驱空）⇒ 跳过该盘，⛔ 不抛。
    /// </summary>
    public static List<string> ListRootDirectories(IReadOnlyList<StorageSource> storages)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            var p = NormalizeDir(path);
            if (p.Length == 0) return;
            if (seen.Add(p)) roots.Add(p);
        }

        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType == DriveType.NoRootDirectory) continue;
                    Add(d.RootDirectory.FullName);
                }
                catch { /* 单盘读取失败（空光驱 / 断开的映射盘）⇒ 跳过 */ }
            }
        }
        catch { /* 枚举盘符整体失败 ⇒ 只留存储源根 */ }

        foreach (var s in storages)
        {
            var p = NormalizeDir(s.RootPath);
            if (p.Length == 0) continue;
            // 已经在某个盘符之下（D:\Media）⇒ 不另立一个根，走那棵盘符树
            if (roots.Any(r => IsUnder(p, r))) continue;
            Add(p);
        }

        roots.Sort(StringComparer.OrdinalIgnoreCase);
        return roots;
    }

    /// <summary>
    /// 枚举一层子目录（按名称排序）。返回 null 表示不可访问（权限 / IO / 掉线）。
    /// </summary>
    public static List<string>? LoadChildDirectories(string dir)
    {
        var abs = NormalizeDir(dir);
        if (abs.Length == 0) return null;
        try
        {
            var list = Directory.EnumerateDirectories(abs).ToList();
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }
        catch { return null; }
    }

    /// <summary>
    /// 枚举一个目录的**直接内容**（子目录 + 文件）。不可访问 ⇒ <see cref="BrowseDirContent.Accessible"/> = false。
    /// </summary>
    public static BrowseDirContent LoadDirectory(string dir)
    {
        var abs = NormalizeDir(dir);
        var content = new BrowseDirContent { Path = abs };
        if (abs.Length == 0) { content.Accessible = false; return content; }

        try
        {
            var info = new DirectoryInfo(abs);
            foreach (var e in info.EnumerateFileSystemInfos())
            {
                try
                {
                    var isFolder = (e.Attributes & FileAttributes.Directory) != 0;
                    var mtime = e.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    if (isFolder)
                    {
                        content.Dirs.Add(new FsEntry(e.FullName, e.Name, true, 0, mtime, AssetTypes.Other));
                    }
                    else
                    {
                        var size = e is FileInfo fi ? fi.Length : 0;
                        content.Files.Add(new FsEntry(e.FullName, e.Name, false, size, mtime,
                            ScanService.GuessAssetType(e.Extension)));
                    }
                }
                catch { /* 单个条目信息读不出（断链 / 权限）⇒ 跳过它，不影响整目录 */ }
            }

            content.Dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            content.Files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        catch { content.Accessible = false; }

        return content;
    }

    // ==================== 路径工具 ====================

    /// <summary>绝对目录 = 存储源根 + 相对目录（统一 <c>\</c> 分隔、去尾分隔符、盘符大写）。</summary>
    public static string AbsDir(string? rootPath, string relDir)
    {
        var root = NormalizeDir(rootPath);
        if (string.IsNullOrEmpty(relDir)) return root;

        var rel = relDir.Replace('/', '\\').Trim('\\');
        if (rel.Length == 0) return root;
        if (root.Length == 0) return rel;
        return root.EndsWith('\\') ? root + rel : root + "\\" + rel;
    }

    /// <summary>规范化目录路径（<c>D:</c> / <c>D:\</c> → <c>D:\</c>；去尾 <c>\</c>；斜杠统一）。</summary>
    public static string NormalizeDir(string? path)
    {
        var p = (path ?? "").Trim().Replace('/', '\\');
        if (p.Length == 0) return "";
        if (p.Length == 2 && p[1] == ':') return char.ToUpperInvariant(p[0]) + ":\\";

        p = p.TrimEnd('\\');
        if (p.Length == 0) return "";
        if (p.Length == 2 && p[1] == ':') return char.ToUpperInvariant(p[0]) + ":\\";
        return p;
    }

    /// <summary>上一级目录（盘符根 / 共享根 → null）。</summary>
    public static string? ParentDir(string absDir)
    {
        var p = NormalizeDir(absDir);
        if (p.Length == 0 || p.EndsWith('\\')) return null;

        var i = p.LastIndexOf('\\');
        if (i <= 0) return null;

        var parent = p[..i];
        if (parent.Length == 2 && parent[1] == ':') return char.ToUpperInvariant(parent[0]) + ":\\";
        // UNC：`\\NAS\share` 就是根，再往上没有东西
        if (parent.StartsWith("\\\\", StringComparison.Ordinal) && parent.IndexOf('\\', 2) < 0) return null;
        return parent;
    }

    /// <summary><paramref name="path"/> 是否在 <paramref name="root"/> 之内（含相等，忽略大小写）。</summary>
    public static bool IsUnder(string? path, string? root)
    {
        var p = NormalizeDir(path);
        var r = NormalizeDir(root);
        if (p.Length == 0 || r.Length == 0) return false;
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;

        var prefix = r.EndsWith('\\') ? r : r + "\\";
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 目录 → 已入库子孙文件数（含自身）。只用于树上的计数徽标：
    /// 一个文件算在它的直接目录上，再逐级加到祖先（⛔ 不递归扫盘，纯字符串累加）。
    /// </summary>
    public static Dictionary<string, int> CountByAncestor(IEnumerable<string> fileDirs)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in fileDirs)
        {
            for (string? cur = NormalizeDir(dir); cur != null; cur = ParentDir(cur))
            {
                counts.TryGetValue(cur, out var n);
                counts[cur] = n + 1;
            }
        }
        return counts;
    }

    /// <summary>节点持久化键（绝对目录路径）。</summary>
    public static string KeyOf(AssetDirNode? node) => node?.AbsPath ?? "";

    /// <summary>
    /// 解析持久化键：新格式 = 绝对路径；旧格式（<c>存储源id|相对目录</c>，本页早期版本写入）
    /// 在有 <paramref name="storageRoots"/> 时换算成绝对路径，⛔ 别让老配置变成「找不到目录」。
    /// </summary>
    public static string ResolveKey(string? key, IReadOnlyDictionary<long, string>? storageRoots = null)
    {
        var text = (key ?? "").Trim();
        if (text.Length == 0) return "";

        var i = text.IndexOf('|');
        if (i <= 0) return NormalizeDir(text);

        if (storageRoots == null
            || !long.TryParse(text[..i], out var sid)
            || !storageRoots.TryGetValue(sid, out var root))
            return "";
        return AbsDir(root, text[(i + 1)..]);
    }
}
