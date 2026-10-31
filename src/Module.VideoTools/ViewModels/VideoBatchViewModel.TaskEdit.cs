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

    // ==================== 添加文件 ====================

    /// <summary>把一批文件按当前"新建类型"建成任务（剪辑 / 转码）。</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (NewAsCut) await AddCutFilesAsync(paths);
        else await AddTranscodeFilesAsync(paths);
    }

    /// <summary>为一批文件建**转码**任务（先并发探测源信息，界面上立刻能看到对比基线）。</summary>
    public async Task AddTranscodeFilesAsync(IEnumerable<string> paths)
    {
        if (_store == null) return;

        var files = paths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).Distinct().ToList();
        if (files.Count == 0) { StatusText = "没有可用的视频文件。"; return; }

        IsBusy = true;
        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        var preset = TranscodePreset.Find(string.IsNullOrWhiteSpace(EditTranscodePresetId) ? "h265" : EditTranscodePresetId);
        var added = 0;
        try
        {
            foreach (var path in files)
            {
                var task = new TranscodeTask
                {
                    SourcePath = path,
                    PresetId = preset.Id,
                    MaxHeight = DefaultMaxHeight,
                    OutputPath = FfmpegService.DefaultOutputPath(path, "_" + preset.Id, preset.Extension),
                    SourceSize = new FileInfo(path).Length
                };

                var (ok, _, info) = await VideoScanner.ProbeAsync(path, probe);
                if (ok && info != null) task.ApplySourceMedia(info, AdviceMinSavingPercent);

                _store.AddTranscode(task);
                added++;
                StatusText = $"正在生成转码任务… {added}/{files.Count}";
            }

            RefreshView();
            StatusText = $"已生成 {added} 个转码任务（预设：{preset.Name}）；可逐条改参数，再点「▶▶ 批量运行」。";
        }
        catch (Exception ex) { StatusText = "生成任务失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>为一批文件建**剪辑**任务（默认整段 → 到末尾，具体区间进详情卡改）。</summary>
    public async Task AddCutFilesAsync(IEnumerable<string> paths)
    {
        if (_store == null) return;

        var files = paths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).Distinct().ToList();
        if (files.Count == 0) { StatusText = "没有可用的视频文件。"; return; }

        IsBusy = true;
        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        var added = 0;
        try
        {
            foreach (var path in files)
            {
                var task = new CutTask
                {
                    SourcePath = path,
                    PresetId = CutPresets.Copy,
                    Start = 0,
                    End = 0,                                   // 0 = 到末尾
                    OutputPath = FfmpegService.DefaultOutputPath(path, "_cut", ".mp4")
                };

                var (ok, _, info) = await VideoScanner.ProbeAsync(path, probe);
                if (ok && info != null) task.Duration = info.Duration ?? 0;

                _store.AddCut(task);
                added++;
                StatusText = $"正在生成剪辑任务… {added}/{files.Count}";
            }

            RefreshView();
            StatusText = $"已生成 {added} 个剪辑任务；进详情卡填区间（默认整段），再点「▶▶ 批量运行」。";
        }
        catch (Exception ex) { StatusText = "生成任务失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    private static string FormatBitRate(long bps)
        => bps >= 1_000_000 ? $"{bps / 1_000_000.0:0.0} Mbps" : $"{bps / 1000.0:0} kbps";

    /// <summary>🗑 删除选中任务（可多选；⛔ 不动磁盘上的源 / 输出文件）。</summary>
    [RelayCommand]
    private void Delete()
    {
        var rows = SelectionList();
        if (rows.Count == 0 || _store == null) { StatusText = "先选中要删除的任务。"; return; }
        if (IsRunning) { StatusText = "正在跑任务 —— 先「⏹ 停止」再删。"; return; }

        foreach (var t in rows)
        {
            if (t is CutTask c) _store.RemoveCut(c);
            else if (t is TranscodeTask x) _store.RemoveTranscode(x);
        }

        Selected = null;
        RefreshView();
        StatusText = rows.Count == 1
            ? "已删除任务：" + rows[0].Name
            : $"已删除 {rows.Count} 个任务。";
    }

    /// <summary>💾 保存：把详情卡里的参数写回任务（改完自动回到"等待中"，下次按新参数跑）。</summary>
    /// <para>⚠️ 「显式改参数 ⇒ 丢弃手改命令」这条规则**只对参数生效**：命令区是详情卡里最下面那一块，
    /// 用户在那里手改的参数串（<see cref="IsArgsEdited"/>）**原样写回 <c>RawArgs</c>**，不会被这里顶掉。</para>
    [RelayCommand]
    private void Save()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个任务。"; return; }

        var output = (EditOutput ?? "").Trim();
        var raw = IsArgsEdited ? (EditArgs ?? "").Trim() : "";

        if (Selected is TranscodeTask x)
        {
            x.PresetId = string.IsNullOrWhiteSpace(EditTranscodePresetId) ? "h265" : EditTranscodePresetId;
            x.Crf = EditTranscodeCrf;
            x.MaxHeight = EditMaxHeight;
            x.Container = x.IsSoftSubtitle ? ".mkv"
                        : FfmpegService.IsGif(x.Preset) ? ".gif"
                        : NormalizeExt(EditContainer, x.Preset.Extension);
            x.UseGpuEncoder = EditUseGpu;
            x.TargetWidth = EditTargetWidth;
            x.TargetHeight = EditTargetHeight;
            x.DefaultWidth = EditDefaultWidth;
            x.DefaultHeight = EditDefaultHeight;
            x.BitrateMode = string.IsNullOrWhiteSpace(EditBitrateMode) ? "crf" : EditBitrateMode;
            x.TargetBitrate = (EditTargetBitrate ?? "").Trim();
            x.FrameRate = EditFrameRate;
            x.GifWidth = EditGifWidth;
            x.Rotate = EditRotate ?? "";
            x.SubtitlePath = (EditSubtitlePath ?? "").Trim();
            x.SubtitleMode = string.IsNullOrWhiteSpace(EditSubtitleMode) ? SubtitleModes.None : EditSubtitleMode;
            x.SubtitleFontSize = EditSubtitleFontSize;
            x.SubtitleMarginV = EditSubtitleMarginV;
            x.CropWidth = EditCropWidth;
            x.CropHeight = EditCropHeight;
            x.CropX = EditCropX;
            x.CropY = EditCropY;
            x.AudioMode = string.IsNullOrWhiteSpace(EditAudioMode) ? AudioModes.Keep : EditAudioMode;
            x.VolumeDb = EditVolumeDb;
            x.ReplaceAudioPath = (EditAudioPath ?? "").Trim();
            x.TargetSizeMb = EditTargetSizeMb;

            // 区间（v3.16）：**以「✂ 剪辑」勾选框为准** —— 没勾就把区间归零，
            // ⛔ 否则会出现"框上没勾、命令里却带着 -ss"这种对不上的情况（HasTrim 以前只看终点>起点）。
            var tStart = CutTask.ParseTime(EditStart) ?? 0;
            var tEnd = string.IsNullOrWhiteSpace(EditEnd) ? 0 : (CutTask.ParseTime(EditEnd) ?? 0);
            if (tEnd <= tStart) tEnd = 0;
            x.UseTrim = EditIsCut;
            x.TrimStart = EditIsCut ? tStart : 0;
            x.TrimEnd = EditIsCut ? tEnd : 0;

            x.RawArgs = raw;                  // 手改的命令原样保留；没手改就交给参数生成
            var preset = x.Preset;
            if (output.Length == 0) output = FfmpegService.DefaultOutputPath(x.SourcePath, preset.OutputSuffix, FfmpegService.OutputExtension(x));
            // 软字幕只能是 .mkv：老输出名（.mp4）跟着改后缀，否则跑起来必报错
            if (x.IsSoftSubtitle && output.Length > 0
                && !string.Equals(Path.GetExtension(output), ".mkv", StringComparison.OrdinalIgnoreCase))
            {
                output = Path.ChangeExtension(output, ".mkv");
            }
            x.OutputPath = output;
            if (x.Status != VideoTaskStatus.Running) x.Reset();
            StatusText = $"已保存「{x.Name}」：{preset.Name}"
                         + (x.HasTrim ? $" · 区间 {x.TrimDisplay}" : "")
                         + (x.MaxHeight > 0 ? $" · 缩到 {x.MaxHeight}p" : "")
                         + (x.Crf > 0 ? $" · CRF {x.Crf}" : "");
        }
        else if (Selected is CutTask c)
        {
            var start = CutTask.ParseTime(EditStart) ?? c.Start;
            var end = string.IsNullOrWhiteSpace(EditEnd) ? 0 : (CutTask.ParseTime(EditEnd) ?? 0);
            if (c.Duration > 0 && end > c.Duration) end = c.Duration;

            // 多段（≥2 段）时单段的开始 / 结束不参与，不校验
            var segs = CutSegments.Parse(EditSegmentsText);
            if (segs.Count < 2 && end > 0 && end <= start) { StatusText = "结束时间必须大于开始时间。"; return; }

            c.Start = start;
            c.End = end;
            c.SegmentsText = EditSegmentsText ?? "";
            c.PresetId = string.IsNullOrWhiteSpace(EditCutPresetId) ? CutPresets.Copy : EditCutPresetId;
            c.ExtraArgs = EditExtraArgs ?? "";
            c.ReEncode = c.PresetId == CutPresets.ReEncode;
            c.Crf = EditCutCrf;
            c.RawArgs = raw;                    // 同上

            if (output.Length == 0) output = FfmpegService.DefaultOutputPath(c.SourcePath, "_cut", ".mp4");
            c.OutputPath = output;
            if (c.Status != VideoTaskStatus.Running) c.Reset();
            StatusText = $"已保存「{c.Name}」：{(c.IsMultiSegment ? $"多段 {c.Segments.Count} 段" : c.RangeDisplay)} · {c.PresetName}";
        }

        EditOutput = output;
        _store?.Save();
        RefreshView();
        RefreshCommand();
    }

    /// <summary>容器扩展名：确保带点，空则回退到预设值。</summary>
    private static string NormalizeExt(string? raw, string fallback)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return fallback;
        return s.StartsWith('.') ? s : "." + s;
    }

    /// <summary>🧹 清掉已结束的任务（两类一起清）。</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        if (_store == null) return;
        var n = _store.ClearFinishedCut() + _store.ClearFinishedTranscode();
        Selected = null;
        RefreshView();
        StatusText = n == 0 ? "没有已结束的任务可清。" : $"已清掉 {n} 个已结束的任务。";
    }

}
