using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>「加入资产库」的结果（状态栏文案 + 计数）。</summary>
public sealed record AddToLibraryResult(int Added, int Skipped, string Message);

/// <summary>「资产浏览」的入库入口：把磁盘上的文件 / 文件夹加进资产库。（WHY: MA-015）</summary>
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
    /// 把一整个**文件夹**（递归）加入资产库。每个文件归到它**上一级文件夹**对应的源
    /// （该目录没有源就登记一个）；已在库里的同一路径按目录去重，⛔ 不重复建资产。
    /// </summary>
    public async Task<AddToLibraryResult> AddFolderAsync(string dir, IProgress<string>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new AddToLibraryResult(0, 0, $"目录不存在：{dir}");

        // 预览扫描 = 只读文件信息、不写库（最快，且可随时取消）
        var rows = await _scan.PreviewAsync(dir, recursive: true, progress, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (rows.Count == 0) return new AddToLibraryResult(0, 0, "该目录下没有可入库的文件（排除项见「媒体资产设置」）。");

        var result = Commit(rows, 0);
        var skipped = Math.Max(0, rows.Count - result.Added);
        return result with
        {
            Skipped = skipped,
            Message = $"已加入 {result.Added} 个文件（每个子文件夹登记为各自的存储源）" +
                      (skipped > 0 ? $"，{skipped} 个已在库里、跳过。" : "。")
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
