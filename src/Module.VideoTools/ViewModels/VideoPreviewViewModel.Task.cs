using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoPreviewViewModel : ObservableObject, IStatusProvider, IDetailPanelProvider, IDetailCardSource, IToolbarSource
{

    /// <summary>输出路径：用户填了就用它，否则按类型自动命名（绝不覆盖已有文件）。</summary>
    private string ResolveOutput()
    {
        var o = (EditOutput ?? "").Trim();
        if (o.Length > 0) return o;

        if (IsTranscode)
        {
            var preset = TranscodePreset.Find(EditTranscodePresetId);
            // GIF 只认 .gif；软字幕只认 .mkv
            var ext = FfmpegService.IsGif(preset) ? ".gif"
                    : (IsSoftSubtitleEdit && !string.IsNullOrWhiteSpace(EditSubtitlePath)) ? ".mkv"
                    : NormalizeExt(EditContainer, preset.Extension);
            return FfmpegService.DefaultOutputPath(SourcePath, preset.OutputSuffix, ext);
        }
        return FfmpegService.DefaultOutputPath(SourcePath, "_cut", ".mp4");
    }

    /// <summary>按当前编辑缓冲拼一条剪辑任务。</summary>
    private CutTask BuildCutTask() => new()
    {
        SourcePath = SourcePath,
        Duration = Duration,
        Start = ParseOr(EditStart, 0),
        End = string.IsNullOrWhiteSpace(EditEnd) ? 0 : ParseOr(EditEnd, 0),
        SegmentsText = EditSegmentsText ?? "",
        PresetId = string.IsNullOrWhiteSpace(EditPresetId) ? CutPresets.Copy : EditPresetId,
        Crf = EditCrf,
        ReEncode = string.Equals(EditPresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase),
        ExtraArgs = (EditExtraArgs ?? "").Trim(),
        OutputPath = ResolveOutput()
    };

    /// <summary>按当前编辑缓冲拼一条转码任务。</summary>
    private TranscodeTask BuildTranscodeTask()
    {
        var preset = TranscodePreset.Find(EditTranscodePresetId);
        return new TranscodeTask
        {
            SourcePath = SourcePath,
            PresetId = preset.Id,
            UseGpuEncoder = EditUseGpu,
            Container = NormalizeExt(EditContainer, preset.Extension),
            TargetWidth = EditTargetWidth,
            TargetHeight = EditTargetHeight,
            MaxHeight = EditMaxHeight,
            DefaultWidth = EditDefaultWidth,
            DefaultHeight = EditDefaultHeight,
            Rotate = EditRotate,
            BitrateMode = string.IsNullOrWhiteSpace(EditBitrateMode) ? "crf" : EditBitrateMode,
            Crf = EditTranscodeCrf,
            TargetBitrate = (EditTargetBitrate ?? "").Trim(),
            FrameRate = EditFrameRate,
            GifWidth = EditGifWidth,
            SubtitlePath = (EditSubtitlePath ?? "").Trim(),
            SubtitleMode = string.IsNullOrWhiteSpace(EditSubtitleMode) ? SubtitleModes.None : EditSubtitleMode,
            SubtitleFontSize = EditSubtitleFontSize,
            SubtitleMarginV = EditSubtitleMarginV,
            CropWidth = EditCropWidth,
            CropHeight = EditCropHeight,
            CropX = EditCropX,
            CropY = EditCropY,
            AudioMode = string.IsNullOrWhiteSpace(EditAudioMode) ? AudioModes.Keep : EditAudioMode,
            VolumeDb = EditVolumeDb,
            ReplaceAudioPath = (EditAudioPath ?? "").Trim(),
            TargetSizeMb = EditTargetSizeMb,
            SourceDuration = Duration,
            OutputPath = ResolveOutput()
        };
    }

    // ==================== 操作 ====================

    /// <summary>↺ 区间重置（开始归零、结束 = 整段末尾 → 滑块停最右）。</summary>
    [RelayCommand]
    private void ResetRange()
    {
        EditStart = CutTask.FormatTime(0);
        EndSeconds = Duration > 0 ? Duration : 0;   // v3.16.1：整段 = 结束在片尾，滑块停最右（不再留空停最左）
        StatusText = "区间已重置（整段）。";
    }

    /// <summary>↻ 预览顺时针旋转 90°（纯视觉 <c>RenderTransform</c>；经 <see cref="SelectedRotate"/> 同步命令卡「旋转」字段）。v3.19。</summary>
    [RelayCommand]
    private void RotatePreviewCw()
    {
        var next = (PreviewRotation + 90) % 360;
        SelectedRotate = RotateOptions.Find(RotateOptions.FromAngle(next));
        StatusText = $"预览已旋转到 {next}°，命令卡「旋转」= {RotateOptions.Find(EditRotate).Label}。";
    }

    /// <summary>↺ 预览逆时针旋转 90°（纯视觉；同步命令卡「旋转」字段）。</summary>
    [RelayCommand]
    private void RotatePreviewCcw()
    {
        var next = (PreviewRotation + 270) % 360;
        SelectedRotate = RotateOptions.Find(RotateOptions.FromAngle(next));
        StatusText = $"预览已旋转到 {next}°，命令卡「旋转」= {RotateOptions.Find(EditRotate).Label}。";
    }

    /// <summary>➡ 发送到批处理：把当前这一条任务入队（剪辑 / 转码 / 剪辑+转码）。</summary>
    [RelayCommand]
    private void SendToBatch()
    {
        if (!HasSource || !File.Exists(SourcePath)) { StatusText = "先点「📂 选择视频」挑一个文件。"; return; }
        if (!IsCut && !IsTranscode) { StatusText = "至少勾选一种任务类型（剪辑 / 转码）。"; return; }
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }

        var output = ResolveOutput();
        var raw = IsArgsEdited ? (EditArgs ?? "").Trim() : "";

        try
        {
            if (IsTranscode)
            {
                var t = BuildTranscodeTask();
                if (IsCut) { t.TrimStart = StartSeconds; t.TrimEnd = EndSeconds; }
                if (t.HasTrim && t.TrimEnd <= t.TrimStart) { StatusText = "结束时间必须大于开始时间。"; return; }
                t.OutputPath = output;
                t.RawArgs = raw;
                _store.AddTranscode(t);
                StatusText = $"已发送到「批量处理」：{t.Name} · {(t.HasTrim ? "剪辑+转码" : "转码")} · {t.PresetName}";
            }
            else
            {
                var c = BuildCutTask();
                if (!c.IsMultiSegment && c.End > 0 && c.End <= c.Start) { StatusText = "结束时间必须大于开始时间。"; return; }
                c.OutputPath = output;
                c.RawArgs = raw;
                _store.AddCut(c);
                StatusText = $"已发送到「批量处理」：{c.Name} · 剪辑 · {(c.IsMultiSegment ? $"多段 {c.Segments.Count} 段" : c.RangeDisplay)}";
            }
        }
        catch (Exception ex) { StatusText = "发送失败：" + ex.Message; }
    }

}
