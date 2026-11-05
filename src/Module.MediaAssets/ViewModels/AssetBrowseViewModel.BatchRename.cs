using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>「资产浏览」的批量重命名入口（文件右键 → 规则对话框）。（WHY: MA-065）</summary>
public partial class AssetBrowseViewModel
{
    /// <summary>批量重命名弹层的 VM（规则 / 预览 / 落盘）。</summary>
    public BatchRenameViewModel BatchRename { get; } = new();

    /// <summary>批量重命名的启用条件：≥2 个已入库文件被选中。</summary>
    public bool CanBatchRename => LibraryFileTiles().Count >= 2;

    [RelayCommand]
    private void BatchRenameSelected()
    {
        var tiles = LibraryFileTiles();
        if (tiles.Count < 2)
        {
            if (tiles.Count == 0) { WarnNoLibrarySelection("批量重命名"); return; }
            StatusText = "批量重命名要选中至少 2 个已入库文件（只改一个请用「重命名…」）。";
            return;
        }

        var targets = new List<BatchRenameTarget>();
        foreach (var tile in tiles)
        {
            var loc = LocationOf(tile);
            if (!File.Exists(loc.Path)) continue;
            targets.Add(new BatchRenameTarget(tile.Row, loc));
        }

        if (targets.Count < 2)
        {
            StatusText = "选中的文件里能改名的不足 2 个（文件可能已不在磁盘上）。";
            return;
        }

        BatchRename.Attach(_fileOps, _opLog);
        BatchRename.Open(targets, _selectedTiles.Count - targets.Count, "批量重命名（资产浏览）", message =>
        {
            StatusText = message;
            Reload(restoreLastFolder: false);
        });
    }
}
