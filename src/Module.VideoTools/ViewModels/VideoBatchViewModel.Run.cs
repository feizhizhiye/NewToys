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

public partial class VideoBatchViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>
    /// 🧹 清除缓存（v3.16.1 更名 + 改语义，需求 §6.4.11）：**只清页面上的这条缓存** ——
    /// 内存里的队列清空回到空白，⛔ 磁盘上的 `video-tasks.json` **不动**（不删、也不把空队列写回去），
    /// 想再要回来就点「📥 导入任务」。⛔ 别叫"清除队列"，那是"连备份一起清"的口径。
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }

        var n = ItemsRaw.Count();
        if (n == 0) { StatusText = "列表已经是空的。"; return; }
        if (IsRunning) { StatusText = "正在跑任务 —— 先「⏹ 停止」再清除。"; return; }

        var msg = $"清除页面上的 {n} 个任务（只是不再显示它们）？" + Environment.NewLine
                + "⛔ 磁盘上的 video-tasks.json 不会动、已生成的视频文件也不会删 —— 想再要回来点「📥 导入任务」。";
        if (!await NewToys.Core.Services.DialogHooks.ConfirmAsync(msg)) { StatusText = "已取消清除。"; return; }

        _runCts?.Cancel();
        var cleared = _store.ClearCache();
        Selected = null;
        RefreshView();
        StatusText = $"已清除页面缓存（{cleared} 个任务）—— 磁盘上的 video-tasks.json 没动，点「📥 导入任务」可以再读回来；"
                   + "下次打开程序也不会自动载入它。";
    }

    // ==================== 运行 ====================

    /// <summary>▶ 只跑选中的一条。</summary>
    [RelayCommand]
    private async Task RunSelectedAsync()
        => await RunTasksAsync(Selected != null ? new List<VideoTaskBase> { Selected } : new List<VideoTaskBase>());

    /// <summary>▶▶ 批量运行：按列表顺序跑全部「还没成功」的任务（剪辑 / 转码按各自参数分派）。</summary>
    [RelayCommand]
    private async Task RunAllAsync()
        => await RunTasksAsync(ItemsRaw.Where(t => t.Status != VideoTaskStatus.Done).ToList());

    private async Task RunTasksAsync(List<VideoTaskBase> targets)
    {
        if (_store == null) return;
        if (IsRunning) { StatusText = "已经在跑了，先「⏹ 停止」再开始。"; return; }
        if (targets.Count == 0) { StatusText = "没有要跑的任务。"; return; }

        var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
        var (okCheck, msgCheck) = await FfmpegService.CheckAsync(ffmpeg);
        if (!okCheck) { StatusText = "⚠ " + msgCheck; return; }

        _runCts = new CancellationTokenSource();
        IsRunning = true;
        var okCount = 0;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (_runCts.IsCancellationRequested) break;

                StatusText = $"正在处理（{i + 1}/{targets.Count}）[{KindOf(t)}]：{t.Name}";
                t.Status = VideoTaskStatus.Running;
                t.Progress = 0;
                t.Message = "";

                // 按类型分派：剪辑 → BuildCutArgs；转码 → BuildTranscodeArgs
                // 🔴 手改过命令 → **原样执行**（RawArgs 按约定不含输出路径，这里补在末尾）
                var output = ResolveOutputOf(t);
                t.OutputPath = output;

                // 与详情卡的命令区**同一个方法** → 界面上看到的就是真正会跑的那条
                var args = BuildArgs(t);
                if (args.Count == 0) { t.Status = VideoTaskStatus.Failed; t.Message = "无法生成 ffmpeg 参数。"; continue; }

                // 进度基准时长：剪辑=区间长度；转码带区间=区间长度，否则=源时长
                var duration = t switch
                {
                    CutTask ct => (ct.End > ct.Start ? ct.Length : ct.Duration),
                    TranscodeTask x => (x.HasTrim ? x.TrimEnd - x.TrimStart : x.SourceDuration),
                    _ => 0
                };

                var progress = new Progress<int>(p => t.Progress = p);
                var (ok, message, size) = await FfmpegService.RunAsync(
                    ffmpeg, args, output, duration, progress, _runCts.Token);

                t.Status = ok ? VideoTaskStatus.Done : (message == "已取消。" ? VideoTaskStatus.Canceled : VideoTaskStatus.Failed);
                t.Message = message;
                t.OutputSize = size;

                if (ok)
                {
                    okCount++;
                    if (t is TranscodeTask tx) await FillOutputInfoAsync(tx);
                }

                _store.Save();
            }

            StatusText = _runCts.IsCancellationRequested
                ? $"已停止：完成 {okCount}/{targets.Count}"
                : $"批量处理完成：{okCount}/{targets.Count} 成功";
        }
        catch (Exception ex)
        {
            StatusText = "批量运行出错：" + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
            RefreshView();
        }
    }

    /// <summary>探测输出文件，填「处理后」的编码 / 分辨率 / 码率 / 时长。</summary>
    private async Task FillOutputInfoAsync(TranscodeTask task)
    {
        try
        {
            var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
            var (ok, _, info) = await VideoScanner.ProbeAsync(task.OutputPath, probe);
            if (!ok || info == null) return;

            task.OutputDuration = info.Duration ?? 0;
            task.OutputCodec = VideoItem.FriendlyCodecName(info.Codec);
            task.OutputResolution = info.Width is > 0 && info.Height is > 0 ? $"{info.Width}×{info.Height}" : "";
            task.OutputBitRate = info.BitRate is > 0 ? FormatBitRate(info.BitRate.Value) : "";
        }
        catch { /* 探测输出失败不影响"已成功"这个结论 */ }
    }

    /// <summary>⏹ 停止。</summary>
    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning) { StatusText = "当前没有在跑的任务。"; return; }
        _runCts?.Cancel();
        StatusText = "正在停止……（当前这条会被中止，半成品输出已删除）";
    }

    /// <summary>📂 打开输出文件所在目录。</summary>
    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (Selected == null) return;
        var dir = File.Exists(Selected.OutputPath)
            ? Path.GetDirectoryName(Selected.OutputPath)
            : Path.GetDirectoryName(Selected.SourcePath);
        try
        {
            if (string.IsNullOrEmpty(dir)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    // ==================== 反向发送 / 导入导出 ====================

    /// <summary>➡ 把选中的一条任务送进「视频预览」页（复用 EnqueueForCut 取件通道；预览页一次只编一个）。</summary>
    [RelayCommand]
    private void SendToPreview()
    {
        if (Selected == null) { StatusText = "先在列表里选中一个任务。"; return; }
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.EnqueueForCut(new[] { Selected.SourcePath });
        _nav?.NavigateTo("video-cut");
        StatusText = $"已把「{Selected.Name}」送去视频预览页（本页一次只编一个，进入后自动载入）。";
    }

    /// <summary>📤 导出任务：把当前队列（剪辑 + 转码）序列化成 JSON 文件。</summary>
    public void ExportTasks(string filePath)
    {
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        StatusText = _store.ExportToFile(filePath)
            ? $"已导出任务到：{filePath}"
            : "导出任务失败（文件写不进去？）。";
    }

    /// <summary>📥 导入任务：从导出的 JSON 里追加任务进当前队列。</summary>
    public void ImportTasks(string filePath)
    {
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        var n = _store.ImportFromFile(filePath);
        RefreshView();
        StatusText = n > 0 ? $"已导入 {n} 个任务（追加到队列末尾）。" : "没有可导入的任务（文件为空或格式不对）。";
    }
}

/// <summary>小工具：空串显示成破折号（对比表里"没有值"要看得出来）。</summary>
internal static class StringDashExt
{
    public static string NullDash(this string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

}
