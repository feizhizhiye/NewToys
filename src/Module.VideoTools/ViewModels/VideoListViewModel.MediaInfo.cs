using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>读当前选中项的媒体信息（跑一次 ffprobe 并写回缓存）。</summary>
    [RelayCommand]
    private async Task LoadMediaInfoAsync()
    {
        var v = Selected;
        if (v == null) return;

        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        StatusText = $"正在用 ffprobe 读取 {v.Name} ……";
        var (ok, msg, info) = await VideoScanner.ProbeAsync(v.FullPath, probe);
        if (ok && info != null)
        {
            v.ApplyMedia(info);
            _index?.Upsert(v);
            StatusText = $"{v.Name}：{v.MediaStatus}";
        }
        else
        {
            v.MediaStatus = "读取失败：" + msg;
            StatusText = msg;
        }
    }

    /// <summary>
    /// 并发读取列表里**所有还没有媒体信息**的视频（已缓存的跳过，不重复跑）。
    /// 并发度取「设置 → 视频工具设置」里的**并发数**（v3.64：落 <c>config\video-tools.json</c>）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAllMediaInfoAsync()
    {
        if (IsBusy) return;

        var targets = _all.Where(v => !v.HasMedia).ToList();
        if (targets.Count == 0) { StatusText = "列表里的视频都已有媒体信息。"; return; }

        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        var concurrency = Math.Clamp(_vt?.MaxConcurrency ?? 2, 1, 16);

        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var ct = _probeCts.Token;

        IsBusy = true;
        var done = 0;
        StatusText = $"正在读取媒体信息（并发 {concurrency}）… 0/{targets.Count}";
        try
        {
            using var gate = new SemaphoreSlim(concurrency);
            var tasks = targets.Select(async v =>
            {
                try
                {
                    await gate.WaitAsync(ct).ConfigureAwait(true);
                    try
                    {
                        var (ok, msg, info) = await VideoScanner.ProbeAsync(v.FullPath, probe, ct);
                        if (ok && info != null)
                        {
                            v.ApplyMedia(info);
                            _index?.Upsert(v);
                        }
                        else v.MediaStatus = "读取失败：" + msg;
                    }
                    finally { gate.Release(); }

                    done++;
                    StatusText = $"正在读取媒体信息（并发 {concurrency}）… {done}/{targets.Count}";
                }
                catch (OperationCanceledException) { /* 取消：静默收尾 */ }
            }).ToList();

            await Task.WhenAll(tasks);

            ApplyView();   // 时长 / 大小等排序可能受影响，排序一次到位
            var failed = targets.Count(t => !t.HasMedia);
            StatusText = ct.IsCancellationRequested
                ? $"已取消：已读取 {done - failed}/{targets.Count} 个。"
                : $"媒体信息读取完成：{targets.Count - failed}/{targets.Count} 个成功"
                  + (failed > 0 ? $"（{failed} 个失败，可单独选中后点「🔎 读取信息」看原因）" : "");
        }
        catch (Exception ex) { StatusText = "读取媒体信息失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>🧹 清除列表（≈ 浏览器无缓存刷新）：只清界面上的列表，⛔ 不动磁盘文件、不重扫目录。</summary>
    [RelayCommand]
    private async Task ClearListAsync()
    {
        var n = _all.Count;
        if (n == 0 && Videos.Count == 0) { StatusText = "列表已经是空的。"; return; }

        var msg = $"清除当前列表里的 {n} 个视频条目？" + Environment.NewLine
                + "只清界面上的列表 —— 不会删除任何视频文件；清完可以重新导入 / 扫描。";
        if (!await NewToys.Core.Services.DialogHooks.ConfirmAsync(msg)) { StatusText = "已取消清除。"; return; }

        _scanCts?.Cancel();
        ClearFiltersCommand.Execute(null);   // 筛选条件一并归零（否则清完看着像"还有条件挡着"）
        SearchText = "";
        _all.Clear();
        Videos.Clear();
        Selected = null;
        RebuildQuickFilters();
        ApplyView();
        StatusText = $"已清除列表（{n} 个条目）；视频文件没动 —— 点「导入」重新载入。";
    }

}
