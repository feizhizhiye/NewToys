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
    /// 🧹 清空队列：内存队列清空**并写进库**（库里那条文档一起删）。
    /// <para>必须落盘：软件启动时会自动恢复上次的队列，只清内存的话重开就全回来了。
    /// 想再要回来用导出的 JSON 走「📥 导入任务」；磁盘上的源 / 输出文件一个都不动。</para>
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }

        var n = ItemsRaw.Count();
        if (n == 0) { StatusText = "队列已经是空的。"; return; }
        if (IsRunning) { StatusText = "正在跑任务 —— 先「⏹ 停止」再清空。"; return; }

        var msg = $"清空队列里的 {n} 个任务？（重开软件也不会再回来）" + Environment.NewLine
                + "⛔ 磁盘上的源文件 / 已生成的视频一个都不删。";
        if (!await NewToys.Core.Services.DialogHooks.ConfirmAsync(msg)) { StatusText = "已取消。"; return; }

        _runCts?.Cancel();
        var cleared = _store.ClearCache();
        Selected = null;
        RefreshView();
        StatusText = $"已清空队列（{cleared} 个任务，库里也删了）—— 磁盘上的文件没动。";
    }

    // ==================== 运行 ====================

    /// <summary>▶ 只跑选中：跑选中行（可多选，Ctrl 增选 / Shift 连选；已完成的不重复跑）。</summary>
    [RelayCommand]
    private async Task RunSelectedAsync()
        => await RunTasksAsync(SelectionList().ToList());

    /// <summary>▶▶ 批量运行：按列表顺序跑全部「还没成功」的任务（剪辑 / 转码按各自参数分派）。</summary>
    [RelayCommand]
    private async Task RunAllAsync()
        => await RunTasksAsync(ItemsRaw.Where(t => t.Status != VideoTaskStatus.Done).ToList());

    private async Task RunTasksAsync(List<VideoTaskBase> targets)
    {
        if (_store == null) return;
        if (IsRunning) { StatusText = "已经在跑了，先「⏹ 停止」再开始。"; return; }

        // 🔴 已完成的任务**不再执行**（`Status` 就是运行记录）：重新开始只续跑没完成的。
        //    想重跑某条 ⇒ 选中它右键「↺ 重新排队」（清掉状态 = 明确表示要再跑一次）。
        targets = targets.Where(t => t.Status != VideoTaskStatus.Done).ToList();
        if (targets.Count == 0)
        {
            StatusText = "没有要跑的任务 —— 已完成的不重复跑；想重跑先选中它右键「↺ 重新排队」。";
            return;
        }

        var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
        var (okCheck, msgCheck) = await FfmpegService.CheckAsync(ffmpeg);
        if (!okCheck) { StatusText = "⚠ " + msgCheck; return; }

        _runCts = new CancellationTokenSource();
        IsRunning = true;
        var okCount = 0;
        // 跑批期间别让系统睡着（人离开了转码还在继续）
        using var keepAwake = KeepAwakeScope.Acquire();
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
                var watch = Stopwatch.StartNew();

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

                // 运行记录：跑完（成功 / 失败 / 取消）都记一行完成时间 + 耗时，落盘后关掉软件也还在
                t.FinishedAt = DateTime.Now;
                t.ElapsedSeconds = (int)Math.Round(watch.Elapsed.TotalSeconds);

                if (ok)
                {
                    okCount++;
                    if (t is TranscodeTask tx) await FillOutputInfoAsync(tx);
                }

                _store.Save();
            }

            StatusText = _runCts.IsCancellationRequested
                ? $"已停止：本轮完成 {okCount}/{targets.Count} —— 剩下没跑完的留着，再点「▶▶ 批量运行」只跑它们（已完成的不会重跑）。"
                : $"批量处理完成：{okCount}/{targets.Count} 成功（完成时间 / 耗时已记进列表）。";
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

    /// <summary>📂 打开输出文件所在目录（多选时取第一条）。</summary>
    [RelayCommand]
    private void OpenOutputFolder()
    {
        var t = SelectionList().FirstOrDefault();
        if (t == null) return;
        var dir = File.Exists(t.OutputPath)
            ? Path.GetDirectoryName(t.OutputPath)
            : Path.GetDirectoryName(t.SourcePath);
        try
        {
            if (string.IsNullOrEmpty(dir)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    // ==================== 反向发送 / 导入导出 ====================

    /// <summary>🎬 预览编辑：把选中的任务（**连参数**）送进「视频预览」页，改完可用「✔ 保存回任务」写回原位置。</summary>
    [RelayCommand]
    private void SendToPreview()
    {
        if (Selected == null) { StatusText = "先在列表里选中一个任务。"; return; }
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.AttachPreviewTarget(Selected);
        _nav?.NavigateTo("video-cut");
        StatusText = $"已带着参数把「{Selected.Name}」送去视频预览页 —— 改完点「✔ 保存回任务」写回本队列（不新增一条）。";
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
