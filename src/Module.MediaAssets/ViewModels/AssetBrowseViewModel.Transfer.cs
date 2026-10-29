using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「资产浏览」→ 视频工具的交接：把选中的视频交给「视频工具 → 视频列表」页。
/// <para>走 <see cref="IModuleBus"/>（模块禁互引），投递完顺带导航过去。</para>
/// <para>⚠️ 取 <see cref="SelectedTiles"/> 而不是 <c>SelectedRows</c>：后者只装**已入库**项，
/// 而本页的未入库文件同样能打开，也应该能送去视频工具。</para>
/// </summary>
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
            .Select(t => t.AbsPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (videos.Count == 0)
        {
            StatusText = "选中的都不是视频，没有可发送的（视频工具只收视频文件）。";
            return;
        }

        _bus?.Post(ModuleTopics.VideoImport, (IReadOnlyList<string>)videos);
        _navStore?.NavigateTo("video-list");

        var skipped = tiles.Count - videos.Count;
        StatusText = skipped > 0
            ? $"已发送 {videos.Count} 个视频到「视频工具 → 视频列表」（另有 {skipped} 个非视频 / 文件夹已跳过）。"
            : $"已发送 {videos.Count} 个视频到「视频工具 → 视频列表」。";
    }
}
