using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>「加入资产库」的结果（状态栏文案 + 计数）。</summary>
public sealed record AddToLibraryResult(int Added, int Skipped, string Message);

/// <summary>
/// 「资产浏览」的**入库入口**：把磁盘上的文件 / 文件夹加进资产库。
/// <para>
/// 🔴 两条与嵌套目录相关的硬规则：
/// ① **祖先已登记**（目标在某存储源之内）⇒ 不另建源，文件归到那个源（<see cref="AssetService.CommitRows"/> 自己会做）；
/// ② **子孙已登记**（目标目录里含别的存储源）⇒ 那部分文件**跳过**，⛔ 不重复索引，也不为父目录另建重叠源。
/// </para>
/// <para>⛔ 一律只写索引，不动磁盘文件；大批量走调用方的任务队列（本服务只提供可取消的异步方法）。</para>
/// </summary>
public class AssetLibraryAddService
{
    private readonly AssetService _assets;
    private readonly ScanService _scan;

    public AssetLibraryAddService(AssetService assets, ScanService scan)
    {
        _assets = assets;
        _scan = scan;
    }

    /// <summary>把若干**单个文件**加入资产库（不可读 / 已不存在的跳过）。</summary>
    public Task<AddToLibraryResult> AddFilesAsync(IReadOnlyList<string> paths, CancellationToken ct)
        => Task.Run(() =>
        {
            var rows = new List<AssetRow>();
            foreach (var p in paths)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    if (!File.Exists(p)) continue;
                    var info = new FileInfo(p);
                    rows.Add(RowOf(info));
                }
                catch { /* 单个文件读不了 ⇒ 跳过 */ }
            }

            if (rows.Count == 0) return new AddToLibraryResult(0, 0, "没有可加入的文件（都不存在或不可读）。");
            return Commit(rows, 0);
        }, ct);

    /// <summary>
    /// 把一整个**文件夹**（递归）加入资产库。落在已登记源之内的文件归该源；
    /// 落在**别的**存储源里的文件跳过（子孙源已登记的情形）。
    /// </summary>
    public async Task<AddToLibraryResult> AddFolderAsync(string dir, IProgress<string>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new AddToLibraryResult(0, 0, $"目录不存在：{dir}");

        // 预览扫描 = 只读文件信息、不写库（最快，且可随时取消）
        var rows = await _scan.PreviewAsync(dir, recursive: true, progress, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (rows.Count == 0) return new AddToLibraryResult(0, 0, "该目录下没有可入库的文件（排除项见「媒体资产设置」）。");

        var known = _assets.ListStorages();
        var target = AssetService.FindCoveringStorage(known, dir);

        // ① 目标目录本身没被任何源覆盖 ⇒ 只有「完全没被覆盖」的文件留下（否则会重复索引子孙源）
        // ② 目标目录已在某源内 ⇒ 只留属于该源的文件（别的源里的文件跳过）
        var kept = new List<AssetRow>(rows.Count);
        var skipped = 0;
        foreach (var r in rows)
        {
            var cover = AssetService.FindCoveringStorage(known, r.FilePath);
            if (cover == null || (target != null && cover.Id == target.Id)) kept.Add(r);
            else skipped++;
        }

        if (kept.Count == 0)
            return new AddToLibraryResult(0, skipped,
                $"这 {rows.Count} 个文件都已属于其它已登记的存储源，本次没有新增（跳过 {skipped} 个）。");

        var result = Commit(kept, skipped);
        var note = target == null
            ? "（已把该文件夹登记为新的存储源）"
            : $"（归入已登记的存储源「{target.Name}」）";
        return result with
        {
            Message = $"已加入 {result.Added} 个文件{note}" +
                      (result.Skipped > 0 ? $"，跳过 {result.Skipped} 个已在其它存储源里的文件。" : "。")
        };
    }

    /// <summary>落库（一个事务）+ 文案。</summary>
    private AddToLibraryResult Commit(List<AssetRow> rows, int skipped)
    {
        if (!_assets.IsAvailable) return new AddToLibraryResult(0, skipped, "资产库不可用（数据库打不开）——没法入库。");

        var added = _assets.CommitRows(rows);
        return added > 0
            ? new AddToLibraryResult(added, skipped, $"已加入资产库 {added} 个文件。")
            : new AddToLibraryResult(0, skipped + rows.Count, "这些文件都已在资产库里（按路径去重），没有新增。");
    }

    /// <summary>文件信息 → 入库行（字段口径与「全部资产」的预览扫描一致）。</summary>
    private static AssetRow RowOf(FileInfo info)
    {
        var name = Path.GetFileNameWithoutExtension(info.Name);
        var dir = Path.GetDirectoryName(info.FullName) ?? "";
        return new AssetRow
        {
            Name = name,
            Title = name,
            FileName = info.Name,
            FilePath = info.FullName,
            Type = ScanService.GuessAssetType(info.Extension),
            Size = info.Length,
            Mtime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
            StorageName = Path.GetFileName(dir.TrimEnd('\\', '/'))
        };
    }
}
