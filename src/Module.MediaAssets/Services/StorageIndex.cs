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
/// 一个存储源的**内存匹配索引**（扫描前一次性建好，之后所有匹配都在内存里做）。
/// <para>⛔ 没有它就得每个文件查 3~4 次库 —— 1 万文件 = 4 万次查询，是上一版慢的另一半原因。</para>
/// </summary>
internal sealed class StorageIndex
{
    /// <summary>相对路径 → 位置（同存储源内的主匹配键）。</summary>
    public Dictionary<string, FileLocation> ByRelPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>（大小, 修改时间）→ 位置列表（零 IO 的"移动"识别；⛔ 多值，相同 (size,mtime) 可能是不同文件 → 不覆盖）。</summary>
    public Dictionary<(long Size, string Mtime), List<FileLocation>> BySizeMtime { get; } = new();

    /// <summary>（大小, quick_hash）→ 位置（需要读前 N MB 才能匹配）。</summary>
    public Dictionary<(long Size, string QuickHash), FileLocation> ByQuickHash { get; } = new();

    /// <summary>哪些**大小**在库里已有带 quick_hash 的位置（决定要不要花那 1MB 读盘）。</summary>
    private readonly HashSet<long> _sizesWithQuickHash = new();

    /// <summary>库里这个存储源已有多少位置（0 = 首次扫描）。</summary>
    public int Count => ByRelPath.Count;

    public void Add(FileLocation l)
    {
        if (!string.IsNullOrEmpty(l.RelativePath)) ByRelPath[l.RelativePath] = l;
        if (!string.IsNullOrEmpty(l.Mtime))
        {
            var key = (l.Size, l.Mtime!);
            if (!BySizeMtime.TryGetValue(key, out var list)) BySizeMtime[key] = list = new List<FileLocation>();
            list.Add(l);
        }
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

    /// <summary>
    /// 按 (大小, 修改时间) 取**唯一**候选；碰撞（同一 (size,mtime) 有多条位置，常见于复制且保留时间戳）或缺失 → null。
    /// ⛔ 多候选时一律不当作"移动"，回落到"新文件"，避免把原文件的位置误改写到副本上。
    /// </summary>
    public FileLocation? TryGetSingleBySizeMtime(long size, string mtime)
    {
        if (string.IsNullOrEmpty(mtime)) return null;
        return BySizeMtime.TryGetValue((size, mtime), out var list) && list.Count == 1 ? list[0] : null;
    }
}

