using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「全部资产」→ 视频工具的交接：把选中的视频交给「视频工具 → 视频列表」页去定剪辑 / 转码参数。
/// <para>走 <see cref="IModuleBus"/>（模块禁互引），投递完顺带导航过去。</para>
/// </summary>
public partial class AssetAllViewModel
{
    /// <summary>右键「➡ 发送到视频工具」：投递选中视频的磁盘路径，然后跳到视频列表页。</summary>
    [RelayCommand]
    private void SendToVideoTools()
    {
        var rows = BatchRows();
        if (rows.Count == 0)
        {
            StatusText = "先选中要发送的视频（Ctrl / Shift 可多选）。";
            return;
        }

        var videos = rows
            .Where(r => string.Equals(r.Type, AssetTypes.Video, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.FilePath)
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

        var skipped = rows.Count - videos.Count;
        StatusText = skipped > 0
            ? $"已发送 {videos.Count} 个视频到「视频工具 → 视频列表」（另有 {skipped} 个非视频已跳过）。"
            : $"已发送 {videos.Count} 个视频到「视频工具 → 视频列表」。";
    }
}
