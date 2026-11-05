using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>「资产浏览」→ 视频工具的交接：把选中的视频交给「视频工具 → 视频列表」页。（WHY: MA-070）</summary>
public partial class AssetBrowseViewModel
{
    /// <summary>视频扩展名白名单（与视频列表页的扫描口径一致）。</summary>
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".m4v", ".mpg", ".mpeg"
    };

    /// <summary>有选中项（含未入库）⇒ 批量按钮可用。</summary>
    public bool HasSelectedTiles => _selectedTiles.Count > 0;

    /// <summary>右键「➡ 发送到视频列表」：投递选中视频的磁盘路径，然后跳到视频列表页。</summary>
    [RelayCommand]
    private void SendToVideoTools()
    {
        var tiles = _selectedTiles.ToList();
        if (tiles.Count == 0)
        {
            StatusText = "先选中要发送的视频（Ctrl / Shift 可多选）。";
            return;
        }

        var videos = tiles
            .Where(t => !t.IsFolder && VideoExtensions.Contains(Path.GetExtension(t.AbsPath)))
            .Where(t => !string.IsNullOrWhiteSpace(t.AbsPath))
            .Select(t => (Path: t.AbsPath, MetaJson: t.Row.MetadataJson))   // 入库项带着库里的媒体信息
            .DistinctBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (videos.Count == 0)
        {
            StatusText = "选中的都不是视频，没有可发送的（视频工具只收视频文件）。";
            return;
        }

        _bus?.Post(ModuleTopics.VideoImport, (IReadOnlyList<(string Path, string MetaJson)>)videos);
        _navStore?.NavigateTo("video-list");

        var skipped = tiles.Count - videos.Count;
        StatusText = skipped > 0
            ? $"已发送 {videos.Count} 个视频到「视频工具 → 视频列表」（另有 {skipped} 个非视频 / 文件夹已跳过）。"
            : $"已发送 {videos.Count} 个视频到「视频工具 → 视频列表」。";
    }
}
