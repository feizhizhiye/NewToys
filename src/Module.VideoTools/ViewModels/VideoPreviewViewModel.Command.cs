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

    /// <summary>当前自动生成的参数文本（与手改文本比较的基准）。</summary>
    private string AutoArgsText => string.Join(" ", BuildAutoArgs());

    /// <summary>
    /// 按当前「任务类型 + 参数」拼出 ffmpeg 参数列表（⛔ **不含**输出路径 —— 由 <see cref="ResolveOutput"/> 统一补在末尾）。
    /// <para>三种情况：
    /// <list type="bullet">
    /// <item>只剪辑 → <see cref="FfmpegService.BuildCutArgs(CutTask, string?)"/></item>
    /// <item>只转码 → <see cref="FfmpegService.BuildTranscodeArgs(TranscodeTask, string?)"/>（不带区间）</item>
    /// <item>剪辑+转码 → 转码参数 + 区间（<see cref="TranscodeTask.TrimStart"/> / <see cref="TranscodeTask.TrimEnd"/>）</item>
    /// </list></para>
    /// </summary>
    private List<string> BuildAutoArgs()
    {
        if (IsTranscode)
        {
            var t = BuildTranscodeTask();
            if (IsCut) { t.TrimStart = StartSeconds; t.TrimEnd = EndSeconds; }
            return FfmpegService.BuildTranscodeArgs(t, null);
        }
        return IsCut ? FfmpegService.BuildCutArgs(BuildCutTask(), null) : new List<string>();
    }

    /// <summary>刷新命令（未手改过 → 参数文本始终跟随参数走）。</summary>
    private void RefreshCommand()
    {
        if (!IsArgsEdited)
        {
            // ⛔ 别直接写该属性背后的私有字段（MVVMTK0034）。走属性 → 由 OnEditArgsChanged 统一判定"已手改"：
            // 这里赋的就是 AutoArgsText，比较结果必然是 false，不会把自动刷新误标成手改。
            EditArgs = AutoArgsText;
        }
        OnPropertyChanged(nameof(CommandPreview));
        RefreshLint();
    }

    // ==================== 参数预检 ====================

    /// <summary>预检结果（多行文本；空 = 没有意见）。命令区上方常显，命中冲突时列出来。</summary>
    [ObservableProperty] private string _lintText = "";

    public bool HasLint => !string.IsNullOrWhiteSpace(LintText);

    partial void OnLintTextChanged(string value) => OnPropertyChanged(nameof(HasLint));

    /// <summary>重算预检。输入 = **真正会执行的那条参数**（手改过就是手改的），保证提示与运行一致。</summary>
    private void RefreshLint()
    {
        var output = ResolveOutput();
        LintText = CommandLint.Describe(CommandLint.Check(BuildExecutableArgs(), output, SourcePath,
                                                          EditSubtitlePath, EditSubtitleMode,
                                                          new LintExtra(EditCropWidth, EditCropHeight,
                                                                        EditAudioMode, EditAudioPath,
                                                                        EditTargetSizeMb, Duration)));
    }

    /// <summary>真正会执行的参数（**含**输出路径）—— 预检 / 试跑共用，与运行时同源。</summary>
    private List<string> BuildExecutableArgs()
    {
        if (IsArgsEdited && !string.IsNullOrWhiteSpace(EditArgs))
        {
            var raw = FfmpegService.SplitArgs(EditArgs);
            raw.Add(ResolveOutput());
            return raw;
        }

        var args = BuildAutoArgs();
        args.Add(ResolveOutput());
        return args;
    }

    // ==================== 试跑小样 ====================

    /// <summary>上次试跑的成品（临时目录）。</summary>
    [ObservableProperty] private string _trialFile = "";

    /// <summary>小样的一帧（右侧对比栏）。</summary>
    [ObservableProperty] private Bitmap? _trialImage;

    [ObservableProperty] private bool _isTrialing;

    public bool HasTrial => !string.IsNullOrWhiteSpace(TrialFile) && File.Exists(TrialFile);
    public bool HasTrialImage => TrialImage != null;
    public bool CanTrial => HasSource && !IsTrialing && (IsCut || IsTranscode);

    partial void OnTrialFileChanged(string value)
    {
        OnPropertyChanged(nameof(HasTrial));
        NotifyToolbarChanged();   // 「▶ 播放小样」的显隐跟着小样走
    }

    partial void OnTrialImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasTrialImage));

    partial void OnIsTrialingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTrial));
        NotifyToolbarChanged();
    }

    /// <summary>
    /// 🧪 试跑 5 秒：用**当前全部参数**（含手改命令、含 GIF）从「预览位置」起真转一小段到临时目录。
    /// <para>这是「编 → 验 → 看」的闭环入口：参数错在这里就报原文，不用等到批处理才发现。</para>
    /// </summary>
    [RelayCommand]
    private async Task RunTrialAsync()
    {
        if (!CanTrial) { StatusText = "先挑一个视频、勾一种任务类型（剪辑 / 转码），再试跑。"; return; }

        IsTrialing = true;
        try
        {
            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            var start = Math.Max(0, PreviewTime);
            var len = 5.0;
            if (Duration > 0) len = Math.Min(len, Math.Max(1, Duration - start));

            var ext = ".mp4";
            List<string> args;

            if (IsArgsEdited && !string.IsNullOrWhiteSpace(EditArgs))
            {
                // 手改过：原样用用户写的参数（只补输出路径）
                args = FfmpegService.SplitArgs(EditArgs);
                var preset = TranscodePreset.Find(EditTranscodePresetId);
                ext = IsTranscode
                    ? (FfmpegService.IsGif(preset) ? ".gif" : NormalizeExt(EditContainer, preset.Extension))
                    : ".mp4";
            }
            else if (IsTranscode)
            {
                var t = BuildTranscodeTask();
                t.TrimStart = start;
                t.TrimEnd = start + len;
                t.UseTrim = true;
                ext = FfmpegService.IsGif(t.Preset) ? ".gif" : t.ContainerDisplay;
                args = FfmpegService.BuildTranscodeArgs(t, null);
            }
            else
            {
                var c = BuildCutTask();
                c.Start = start;
                c.End = start + len;
                args = FfmpegService.BuildCutArgs(c, null);
            }

            var dir = Path.Combine(Path.GetTempPath(), "NewToys", "trial");
            Directory.CreateDirectory(dir);
            var outFile = Path.Combine(dir, $"trial_{DateTime.Now:HHmmss}{NormalizeExt(ext, ".mp4")}");
            args.Add(outFile);

            StatusText = "试跑中（最多 5 秒素材）…";
            var (ok, message, size) = await FfmpegService.RunAsync(ffmpeg, args, outFile, len, null);
            if (!ok)
            {
                StatusText = "⚠ 试跑失败：" + message;
                return;
            }

            TrialFile = outFile;
            await LoadTrialFrameAsync(ffmpeg, outFile);
            StatusText = $"试跑成功（{size / 1024.0:0} KB / {len:0.#} 秒）—— 右栏是小样画面，点「▶ 播放小样」可带声音看。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 试跑出错：" + ex.Message;
        }
        finally
        {
            IsTrialing = false;
        }
    }

    /// <summary>抽小样的第 0.05 秒当对比帧（文件名跟小样走 → 不会与上一张抢文件）。</summary>
    private async Task LoadTrialFrameAsync(string ffmpeg, string trialFile)
    {
        try
        {
            var shot = Path.ChangeExtension(trialFile, ".shot.jpg");
            if (await FfmpegService.ExtractFrameAsync(ffmpeg, trialFile, 0.05, shot) && File.Exists(shot))
            {
                try { TrialImage = new Bitmap(shot); }
                catch { /* 图坏了就保留上一张 */ }
            }
        }
        catch { /* 抽帧失败不影响「试跑成功」这个结论 */ }
    }

    /// <summary>▶ 播放小样（没有小样就播源片）—— 外部播放器优先，其次 ffplay。</summary>
    [RelayCommand]
    private void PlayTrial()
    {
        var file = HasTrial ? TrialFile : SourcePath;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) { StatusText = "没有可播放的文件。"; return; }

        var (ok, message) = FfmpegService.Play(file, _cfg?.PlayerPath, _cfg?.FfmpegPath);
        StatusText = ok ? message : "⚠ " + message;
    }

    private static double ParseOr(string? text, double fallback) => CutTask.ParseTime(text) ?? fallback;

    /// <summary>容器扩展名：确保带点，空则回退到预设值。</summary>
    private static string NormalizeExt(string? raw, string fallback)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return fallback;
        return s.StartsWith('.') ? s : "." + s;
    }

}
