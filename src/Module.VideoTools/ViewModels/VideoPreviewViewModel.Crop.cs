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

    /// <summary>🖼 探测黑边：跑 2 秒 cropdetect，把结果填进四个裁剪框。</summary>
    [RelayCommand]
    private async Task DetectCropAsync()
    {
        if (!HasSource || !File.Exists(SourcePath)) { StatusText = "先挑一个视频。"; return; }
        if (IsDetectingCrop) return;

        IsDetectingCrop = true;
        try
        {
            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            StatusText = "探测黑边中（取预览位置附近 2 秒）…";
            var found = await FfmpegService.DetectCropAsync(ffmpeg, SourcePath, PreviewTime);
            if (found == null)
            {
                StatusText = "⚠ 没探测到有效画面（源无视频轨 / ffmpeg 不可用？）—— 裁剪值保持不变。";
                return;
            }

            var (w, h, x, y) = found.Value;
            EditCropWidth = w;
            EditCropHeight = h;
            EditCropX = x;
            EditCropY = y;
            StatusText = w == 0 || h == 0
                ? "⚠ 探测结果异常（0×0）—— 裁剪值保持不变。"
                : $"探测到有效画面 {w}×{h} @ ({x}, {y})；四周没有黑边时就是整幅画面。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 探测失败：" + ex.Message;
        }
        finally
        {
            IsDetectingCrop = false;
        }
    }

    partial void OnEditCropWidthChanged(int value) => RefreshCropDependent();
    partial void OnEditCropHeightChanged(int value) => RefreshCropDependent();
    partial void OnEditCropXChanged(int value) => RefreshCropDependent();
    partial void OnEditCropYChanged(int value) => RefreshCropDependent();

    private void RefreshCropDependent() => RefreshCommand();

    partial void OnEditAudioModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsVolumeModeEdit));
        OnPropertyChanged(nameof(IsReplaceAudioEdit));
        RefreshCommand();
    }

    partial void OnEditVolumeDbChanged(double value) => RefreshCommand();
    partial void OnEditAudioPathChanged(string value) => RefreshCommand();

    partial void OnEditTargetSizeMbChanged(double value)
    {
        OnPropertyChanged(nameof(TargetSizeHint));
        OnPropertyChanged(nameof(HasTargetSizeHint));
        RefreshCommand();
    }

    /// <summary>多段剪切串（每行一段 <c>开始-结束</c>）。</summary>
    [ObservableProperty] private string _editSegmentsText = "";

    /// <summary>多段解析的即时反馈（空 = 没填 / 一行都没解析出来）。</summary>
    public string SegmentsHint
    {
        get
        {
            var segs = CutSegments.Parse(EditSegmentsText);
            return segs.Count == 0
                ? ""
                : $"已识别 {segs.Count} 段 · 合计 {CutTask.FormatTime(segs.Sum(s => s.Length))}"
                  + (segs.Count < 2 ? "（少于 2 段 = 忽略多段，按上面开始/结束切）" : " · 多段会强制重新编码");
        }
    }

    public bool HasSegmentsHint => SegmentsHint.Length > 0;

    partial void OnEditSegmentsTextChanged(string value)
    {
        OnPropertyChanged(nameof(SegmentsHint));
        OnPropertyChanged(nameof(HasSegmentsHint));
        RefreshCommand();
    }

    public IReadOnlyList<TranscodePreset> TranscodePresets => TranscodePreset.All;
    public IReadOnlyList<string> ContainerOptions => FfmpegService.Containers;

    /// <summary>
    /// CPU / GPU 下拉框绑它（0 = CPU、1 = GPU）。
    /// <para>⚠️ 为什么不直接绑 bool：ComboBox 的 <c>SelectedValue</c> 走布尔转换容易踩坑
    /// （XAML 里的字面量其实是字符串），绑 <c>int</c> + <c>SelectedIndex</c> 最可靠。</para>
    /// </summary>
    public int EncoderIndex
    {
        get => EditUseGpu ? 1 : 0;
        set
        {
            EditUseGpu = value == 1;
            OnPropertyChanged();
        }
    }

    /// <summary>当前会用的编码器名（详情卡里显示出来，避免以为用了 GPU 其实没用）。</summary>
    public string SelectedEncoder =>
        FfmpegService.ResolveEncoder(TranscodePreset.Find(EditTranscodePresetId).VideoCodec, EditUseGpu);

    /// <summary>码率模式下拉框绑它（0 = CRF、1 = 目标码率），理由同 <see cref="EncoderIndex"/>。</summary>
    public int BitrateModeIndex
    {
        get => string.Equals(EditBitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set
        {
            EditBitrateMode = value == 1 ? "bitrate" : "crf";
            OnPropertyChanged();
        }
    }

    /// <summary>CRF 是否用得上（crf 模式下才显示 CRF 输入框）。</summary>
    public bool UseCrf => !string.Equals(EditBitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase);

    // ==================== 输出 ====================

    [ObservableProperty] private string _editOutput = "";

    partial void OnEditOutputChanged(string value) => RefreshCommand();

    // ==================== 命令：自动生成 + 可手改 ====================

    /// <summary>
    /// **将要执行的完整 ffmpeg 命令行**（自动生成；改参数即时刷新）。
    /// 与「发送到批处理」的那条同源 —— 界面上看到的就是会执行的东西，不含隐藏参数。
    /// </summary>
    public string CommandPreview
    {
        get
        {
            if (!HasSource) return "（先点「选择视频」挑一个文件）";
            if (!EditIsCut && !EditIsTranscode) return "（至少勾选一种任务类型：剪辑 / 转码）";

            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            var args = BuildAutoArgs();
            args.Add(ResolveOutput());
            return FfmpegService.DescribeCommand(ffmpeg, args);
        }
    }

    /// <summary>
    /// 可编辑的**参数部分**（⛔ 不含 ffmpeg.exe、也**不含**输出路径 —— 输出由「输出文件」统一管理）。
    /// 用户一旦手改 → <see cref="IsArgsEdited"/> 置 true → 参数变化**不再覆盖**它，`↺ 重置为自动` 才恢复。
    /// </summary>
    [ObservableProperty] private string _editArgs = "";

    /// <summary>命令是否被手改过（true = 以手改文本为准执行）。</summary>
    [ObservableProperty] private bool _isArgsEdited;

    partial void OnEditArgsChanged(string value)
    {
        var edited = !string.Equals((value ?? "").Trim(), AutoArgsText, StringComparison.Ordinal);
        if (IsArgsEdited != edited) IsArgsEdited = edited;
        RefreshLint();
    }

}
