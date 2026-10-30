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

        if (EditIsTranscode)
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
        PresetId = string.IsNullOrWhiteSpace(EditCutPresetId) ? CutPresets.Copy : EditCutPresetId,
        Crf = EditCutCrf,
        ReEncode = string.Equals(EditCutPresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase),
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
        if (!EditIsCut && !EditIsTranscode) { StatusText = "至少勾选一种任务类型（剪辑 / 转码）。"; return; }
        if (_store == null) { StatusText = "任务服务未就绪。"; return; }

        var output = ResolveOutput();
        var raw = IsArgsEdited ? (EditArgs ?? "").Trim() : "";

        try
        {
            if (EditIsTranscode)
            {
                var t = BuildTranscodeTask();
                if (EditIsCut) { t.TrimStart = StartSeconds; t.TrimEnd = EndSeconds; }
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

    // ==================== 来自「批量处理」的带参预览编辑 ====================

    /// <summary>正在编辑的那条批量任务（<c>null</c> = 本页自己新建的任务）。</summary>
    private VideoTaskBase? _editTarget;

    /// <summary>当前是否在编辑一条从「批量处理」带过来的任务（工具条据此出现「✔ 保存回任务」）。</summary>
    public bool IsEditingBatchTask => _editTarget != null;

    /// <summary>
    /// 批量页「🎬 预览编辑」→ 把任务的**全部参数**灌进编辑缓冲（不只是源文件）。
    /// <para>⚠️ 顺序不能反：先 <see cref="LoadSource"/>（它会清空上一轮的编辑值），再灌参数。</para>
    /// </summary>
    public void LoadTaskForEdit(VideoTaskBase task)
    {
        if (task == null) return;

        LoadSource(task.SourcePath);
        FillFromTask(task);

        _editTarget = task;
        OnPropertyChanged(nameof(IsEditingBatchTask));
        NotifyToolbarChanged();
        StatusText = $"正在编辑批量任务「{task.Name}」—— 改完点「✔ 保存回任务」写回队列（不新增一条）；点「➡ 发送到批处理」则另存为新任务。";
    }

    /// <summary>
    /// 把任务参数灌进编辑缓冲（read 方向）。
    /// <para>🔴 与批量页 <c>VideoBatchViewModel.FillEditors</c> 同一套映射规则 —— 那边改了这里必须同步
    /// （两页的编辑属性名一致，逐行对应）。</para>
    /// </summary>
    private void FillFromTask(VideoTaskBase value)
    {
        if (value is TranscodeTask x)
        {
            EditTranscodePresetId = x.PresetId;
            EditTranscodeCrf = x.Crf;
            EditMaxHeight = x.MaxHeight;
            EditContainer = x.Container;
            EditUseGpu = x.UseGpuEncoder;
            EditTargetWidth = x.TargetWidth;
            EditTargetHeight = x.TargetHeight;
            EditDefaultWidth = x.DefaultWidth;
            EditDefaultHeight = x.DefaultHeight;
            EditBitrateMode = x.BitrateMode;
            EditTargetBitrate = x.TargetBitrate;
            EditFrameRate = x.FrameRate;
            EditGifWidth = x.GifWidth;
            SelectedRotate = RotateOptions.Find(x.Rotate);
            EditSubtitlePath = x.SubtitlePath;
            EditSubtitleMode = x.SubtitleMode;
            EditSubtitleFontSize = x.SubtitleFontSize;
            EditSubtitleMarginV = x.SubtitleMarginV;
            EditCropWidth = x.CropWidth;
            EditCropHeight = x.CropHeight;
            EditCropX = x.CropX;
            EditCropY = x.CropY;
            EditAudioMode = x.AudioMode;
            EditVolumeDb = x.VolumeDb;
            EditAudioPath = x.ReplaceAudioPath;
            EditTargetSizeMb = x.TargetSizeMb;
            EditStart = x.HasTrim ? CutTask.FormatTime(x.TrimStart) : "";
            EditEnd = x.TrimEnd > 0 ? CutTask.FormatTime(x.TrimEnd) : "";
        }
        else if (value is CutTask c)
        {
            EditCutPresetId = c.PresetId;
            EditStart = CutTask.FormatTime(c.Start);
            EditEnd = c.End > 0 ? CutTask.FormatTime(c.End) : "";
            EditSegmentsText = c.SegmentsText;
            EditExtraArgs = c.ExtraArgs;
            EditCutCrf = c.Crf;
        }

        EditOutput = value.OutputPath ?? "";
        IsArgsEdited = !string.IsNullOrWhiteSpace(value.RawArgs);
        EditArgs = IsArgsEdited ? value.RawArgs : AutoArgsText;
        OnPropertyChanged(nameof(CommandPreview));

        EditIsCut = value is CutTask || value is TranscodeTask { HasTrim: true };
        EditIsTranscode = value is TranscodeTask;

        IsCutSectionExpanded = EditIsCut;
        IsTranscodeSectionExpanded = EditIsTranscode;
        IsCropSectionExpanded = EditCropWidth > 0 || EditCropHeight > 0 || !string.IsNullOrWhiteSpace(EditSubtitlePath);
        IsAudioSectionExpanded = !string.Equals(EditAudioMode, AudioModes.Keep, StringComparison.OrdinalIgnoreCase);
        IsBitrateSectionExpanded = EditTargetSizeMb > 0 || !UseCrf || IsGifPreset;
        IsCommandSectionExpanded = IsArgsEdited;
    }

    /// <summary>✔ 保存回任务：把当前参数写回「批量处理」里那条任务（保队列位置，⛔ 不新增）。</summary>
    [RelayCommand]
    private void SaveBackToTask()
    {
        if (_editTarget == null || _store == null)
        {
            StatusText = "当前没有从「批量处理」带过来的任务 —— 用「➡ 发送到批处理」新建。";
            return;
        }
        if (!HasSource || !File.Exists(SourcePath)) { StatusText = "源文件不见了 —— 保存不了。"; return; }
        if (!EditIsCut && !EditIsTranscode) { StatusText = "至少勾选一种任务类型（剪辑 / 转码）。"; return; }

        var output = ResolveOutput();
        var raw = IsArgsEdited ? (EditArgs ?? "").Trim() : "";

        try
        {
            VideoTaskBase fresh;
            if (EditIsTranscode)
            {
                var t = BuildTranscodeTask();
                if (EditIsCut) { t.TrimStart = StartSeconds; t.TrimEnd = EndSeconds; }
                if (t.HasTrim && t.TrimEnd <= t.TrimStart) { StatusText = "结束时间必须大于开始时间。"; return; }
                t.OutputPath = output;
                t.RawArgs = raw;
                fresh = t;
            }
            else
            {
                var c = BuildCutTask();
                if (!c.IsMultiSegment && c.End > 0 && c.End <= c.Start) { StatusText = "结束时间必须大于开始时间。"; return; }
                c.OutputPath = output;
                c.RawArgs = raw;
                fresh = c;
            }

            var oldName = _editTarget.Name;
            _store.Replace(_editTarget, fresh);
            _editTarget = fresh;             // 继续编辑就作用在新对象上
            StatusText = $"已保存回批量队列：「{oldName}」（参数已更新，任务位置不变）。";
        }
        catch (Exception ex) { StatusText = "保存回任务失败：" + ex.Message; }
    }

}
