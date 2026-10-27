using System;
using System.Collections.Generic;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 「资产浏览」页的目录树聚合：把 <see cref="AssetService.LoadBrowseEntries"/> 的位置行按
/// 「存储源 + 相对目录」建成两级结构（存储源 → 目录层级），并统计每个节点的子孙位置数。
/// <para>
/// 🔴 一次取回全部位置行后在内存建树（1w 行量级）：⛔ 不按目录逐个查库（懒查询在宽扇出目录下会变成 N 次往返）。
/// </para>
/// </summary>
public class AssetBrowseService
{
    /// <summary>「（根目录）」节点显示名：直接落在存储源根下的文件。</summary>
    public const string RootDirName = "（根目录）";

    private readonly AssetService _assets;

    public AssetBrowseService(AssetService assets) => _assets = assets;

    /// <summary>取全部可浏览位置行（库不可用 → 空表，页面按空态处理）。</summary>
    public List<BrowseEntry> LoadEntries() => _assets.LoadBrowseEntries();

    /// <summary>
    /// 建树：返回**存储源级**根节点列表（每个源一个抬头，<see cref="AssetDirNode.IsStorageRoot"/> = true）。
    /// 根节点下 = 该源的「（根目录）」节点（<c>DirPath</c> 空串）+ 各级子目录。
    /// </summary>
    public List<AssetDirNode> BuildTree(IReadOnlyList<BrowseEntry> entries, IReadOnlyList<StorageSource> storages)
    {
        var nodes = new Dictionary<(long StorageId, string Dir), AssetDirNode>();

        AssetDirNode Node(long storageId, string dir)
        {
            var key = (storageId, dir);
            if (nodes.TryGetValue(key, out var n)) return n;

            var name = dir.Length == 0 ? RootDirName : dir[(dir.LastIndexOf('\\') + 1)..];
            n = new AssetDirNode
            {
                StorageId = storageId,
                Name = name,
                DirPath = dir,
                Online = true,
                ToolTipText = dir
            };
            nodes[key] = n;
            return n;
        }

        foreach (var e in entries)
        {
            var sid = e.Row.StorageId;
            if (sid <= 0) continue;

            Node(sid, e.Dir).FileCount++;

            // 本目录 + 所有祖先的子孙计数
            var cur = e.Dir;
            while (true)
            {
                Node(sid, cur).TotalCount++;
                if (cur.Length == 0) break;
                cur = AssetService.ParentDir(cur);
            }
        }

        var roots = new List<AssetDirNode>();
        var rootByStorage = new Dictionary<long, AssetDirNode>();

        foreach (var s in storages)
        {
            var root = new AssetDirNode
            {
                StorageId = s.Id,
                Name = s.Name,
                DirPath = "",
                IsStorageRoot = true,
                Online = s.Online,
                ToolTipText = s.RootPath
            };

            roots.Add(root);
            rootByStorage[s.Id] = root;
        }

        // 挂子节点：dir 空串的挂到存储源抬头，其余挂到父目录
        foreach (var n in nodes.Values)
        {
            if (!rootByStorage.TryGetValue(n.StorageId, out var root)) continue;
            if (n.DirPath.Length == 0) { root.Children.Add(n); continue; }

            var parentKey = (n.StorageId, AssetService.ParentDir(n.DirPath));
            if (nodes.TryGetValue(parentKey, out var parent)) parent.Children.Add(n);
            else root.Children.Add(n);   // 父目录缺失（正常不会发生）⇒ 兜底挂到根，⛔ 不丢节点
        }

        foreach (var n in nodes.Values) n.Children.Sort(ByName);
        foreach (var root in roots)
        {
            root.Children.Sort(ByName);
            foreach (var child in root.Children) root.TotalCount += child.TotalCount;
        }

        return roots;

        static int ByName(AssetDirNode a, AssetDirNode b)
        {
            // 「（根目录）」固定排最前，其余按名字不区分大小写
            if (a.DirPath.Length == 0) return -1;
            if (b.DirPath.Length == 0) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>节点的持久化键（<c>存储源id|相对目录</c>；空串 = 没选过）。</summary>
    public static string KeyOf(AssetDirNode? node)
        => node == null || node.IsStorageRoot ? "" : $"{node.StorageId}|{node.DirPath}";

    /// <summary>解析持久化键（取不到 → (0, "")）。</summary>
    public static (long StorageId, string Dir) ParseKey(string? key)
    {
        var text = (key ?? "").Trim();
        if (text.Length == 0) return (0, "");
        var i = text.IndexOf('|');
        if (i <= 0) return (0, "");
        return long.TryParse(text[..i], out var sid) ? (sid, text[(i + 1)..]) : (0, "");
    }
}
