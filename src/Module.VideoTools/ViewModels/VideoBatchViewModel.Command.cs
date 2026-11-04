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
    /// 勾选框变了 → 任务在「剪辑 / 转码 / 剪辑+转码」之间转换（需求 §6.4.1 第 5 条）。
    /// <para>两段都取消 = 空任务，⛔ 不允许 → 自动保留「转码」并提示。</para>
    /// </summary>
    private void ApplyTaskKind()
    {
        if (_converting || Selected == null || _store == null) return;

        if (!EditIsCut && !EditIsTranscode)
        {
            _converting = true;
            EditIsTranscode = true;
            _converting = false;
            StatusText = "任务至少要有一段参与 —— 已保留「转码」。";
            return;
        }

        var wantBoth = EditIsCut && EditIsTranscode;
        var wantCutOnly = EditIsCut && !EditIsTranscode;

        VideoTaskBase? fresh = null;
        var kind = "";

        if (Selected is CutTask c)
        {
            if (wantCutOnly) return;                       // 已经是纯剪辑，不用动
            fresh = new TranscodeTask
            {
                Id = c.Id,
                Created = c.Created,
                SourcePath = c.SourcePath,
                SourceDuration = c.Duration,
                UseTrim = true,                            // 区间带过来，任务变成"剪辑+转码"
                TrimStart = c.Start,
                TrimEnd = c.End
            };
            kind = "剪辑+转码";
        }
        else if (Selected is TranscodeTask x)
        {
            if (wantCutOnly)
            {
                fresh = new CutTask
                {
                    Id = x.Id,
                    Created = x.Created,
                    SourcePath = x.SourcePath,
                    Duration = x.SourceDuration,
                    Start = x.TrimStart,
                    End = x.TrimEnd
                };
                kind = "剪辑";
            }
            else
            {
                if (x.UseTrim == wantBoth) return;         // 已经是想要的组合
                x.UseTrim = wantBoth;
                if (!wantBoth) { x.TrimStart = 0; x.TrimEnd = 0; }   // 取消剪辑段 → 区间归零
                if (x.Status != VideoTaskStatus.Running) x.Reset();
                kind = wantBoth ? "剪辑+转码" : "转码";
                FinishKindChange(null, x, kind);
                return;
            }
        }

        if (fresh == null) return;
        if (fresh is TranscodeTask nt) nt.RefreshAdvice(AdviceMinSavingPercent);
        FinishKindChange(Selected, fresh, kind);
    }

    /// <summary>收尾：替换队列里的任务、重选它、重建对比表与命令预览。</summary>
    private void FinishKindChange(VideoTaskBase? old, VideoTaskBase fresh, string kind)
    {
        if (old != null) _store!.Replace(old, fresh);

        // 🔴 顺序不能颠倒：RefreshView() 里 Items.Clear() 会把 ListView 的选中项清空（TwoWay 回写 Selected = null），
        //    ★ 如果先 Selected = fresh 再 RefreshView()，fresh 会被这次 Clear 冲掉 → OnSelectedChanged(null)
        //      → FillEditors(null) → 详情卡掉回「未选中」空态（用户看到的现象：勾选框一点，卡就变空白）。
        RefreshView();

        _converting = true;
        Selected = fresh;                 // 触发 OnSelectedChanged → 重填编辑缓冲与两个勾选框
        _converting = false;

        IsCutSectionExpanded = EditIsCut;
        IsTranscodeSectionExpanded = EditIsTranscode;
        RefreshCommand();
        StatusText = $"「{fresh.Name}」已改为「{kind}」任务 —— 参数回到该类型的默认值，改完点「保存修改」。";
    }

    // ==================== 详情卡折叠态（与「视频预览」页同名同义，两页可照抄同一段 XAML） ====================

    /// <summary>「剪辑」分区是否展开。</summary>
    [ObservableProperty] private bool _isCutSectionExpanded = true;

    /// <summary>「转码」分区是否展开。</summary>
    [ObservableProperty] private bool _isTranscodeSectionExpanded = true;

    /// <summary>「画面（裁剪 / 字幕）」分区是否展开 —— 切换任务时按「有没有裁剪 / 字幕」自动定。</summary>
    [ObservableProperty] private bool _isCropSectionExpanded;

    /// <summary>「音轨」分区是否展开 —— 音频不是「保持原样」才自动展开。</summary>
    [ObservableProperty] private bool _isAudioSectionExpanded;

    /// <summary>「码率与体积」分区是否展开 —— 目标体积 / 目标码率 / GIF 预设命中才自动展开。</summary>
    [ObservableProperty] private bool _isBitrateSectionExpanded;

    /// <summary>「ffmpeg 参数」分区是否展开（手改命令用）。</summary>
    [ObservableProperty] private bool _isCommandSectionExpanded;

    /// <summary>选中的是不是"既剪又转"的任务（决定剪辑分区的开始 / 结束要不要显示）。</summary>
    public bool SelectedHasTrim => Selected is TranscodeTask { HasTrim: true };

    // ==================== 命令：与「视频预览」页同源（自动生成 + 可手改） ====================

    /// <summary>
    /// **选中任务将要执行的完整 ffmpeg 命令行**（自动生成；改参数即时刷新）。
    /// <para>⚠️ 手改过（<see cref="IsArgsEdited"/>）时这里显示的**就是手改那条** —— 界面看到的就是会跑的东西。</para>
    /// </summary>
    public string CommandPreview
    {
        get
        {
            if (Selected == null) return "（在左侧选中一个任务）";

            var args = BuildArgs(Selected);
            if (args.Count == 0) return "（无法生成 ffmpeg 参数）";

            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            return FfmpegService.DescribeCommand(ffmpeg, args);
        }
    }

    /// <summary>
    /// 可编辑的**参数部分**（⛔ 不含 ffmpeg.exe、也不含输出路径）。
    /// <para>与「视频预览」页同名同义 → 两页详情卡的命令区可以照抄同一段 XAML。</para>
    /// </summary>
    [ObservableProperty] private string _editArgs = "";

    /// <summary>命令是否被手改过（true = 以手改文本为准执行）。</summary>
    [ObservableProperty] private bool _isArgsEdited;

    /// <summary>当前自动生成的参数文本（与手改文本比较的基准）。</summary>
    private string AutoArgsText
    {
        get
        {
            if (Selected == null) return "";
            return string.Join(" ", BuildArgs(Selected, includeRaw: false));
        }
    }

    /// <summary>
    /// 按选中任务拼参数。<paramref name="includeRaw"/> 为 true 时优先用手改的 <c>RawArgs</c>（= 真正会执行的那条）。
    /// </summary>
    private List<string> BuildArgs(VideoTaskBase t, bool includeRaw = true, string? overrideOutput = null)
    {
        var output = string.IsNullOrWhiteSpace(overrideOutput) ? ResolveOutputOf(t) : overrideOutput;

        if (includeRaw && !string.IsNullOrWhiteSpace(t.RawArgs))
        {
            var raw = FfmpegService.SplitArgs(t.RawArgs);
            raw.Add(output);
            return raw;
        }
        return t switch
        {
            CutTask c => FfmpegService.BuildCutArgs(c, output),
            TranscodeTask x => FfmpegService.BuildTranscodeArgs(x, output),
            _ => new List<string>()
        };
    }

    /// <summary>任务输出路径：空 = 按类型自动命名（绝不覆盖已有文件）。</summary>
    private static string ResolveOutputOf(VideoTaskBase t)
    {
        if (!string.IsNullOrWhiteSpace(t.OutputPath)) return t.OutputPath;
        return t switch
        {
            CutTask c => FfmpegService.DefaultOutputPath(c.SourcePath, "_cut", ".mp4"),
            TranscodeTask x => FfmpegService.DefaultOutputPath(x.SourcePath, "_" + x.PresetId, FfmpegService.OutputExtension(x)),
            _ => ""
        };
    }

    partial void OnEditArgsChanged(string value)
    {
        var edited = !string.IsNullOrWhiteSpace(value)
                     && !string.Equals(value.Trim(), AutoArgsText, StringComparison.Ordinal);
        if (IsArgsEdited != edited) IsArgsEdited = edited;
        RefreshLint();
    }

    /// <summary>刷新命令（未手改过 → 参数文本始终跟随参数走）。</summary>
    private void RefreshCommand()
    {
        if (!IsArgsEdited) EditArgs = AutoArgsText;
        OnPropertyChanged(nameof(CommandPreview));
        RefreshLint();
    }

    // ==================== 参数预检（与「视频预览」页同源） ====================

    /// <summary>预检结果（多行；空 = 没意见）。选中任务变化 / 参数变化都会重算。</summary>
    [ObservableProperty] private string _lintText = "";

    public bool HasLint => !string.IsNullOrWhiteSpace(LintText);

    partial void OnLintTextChanged(string value) => OnPropertyChanged(nameof(HasLint));

    private void RefreshLint()
    {
        var task = Selected;
        if (task == null) { LintText = ""; return; }

        var args = BuildArgs(task);                 // 手改过就是手改那条
        var sub = task as TranscodeTask;
        // ~ 命令是由**任务上的值**生成的，所以补充上下文也取任务的值（⛔ 别取编辑缓冲，否则提示与命令对不上）
        var extra = sub == null
            ? null
            : new LintExtra(sub.CropWidth, sub.CropHeight, sub.AudioMode, sub.ReplaceAudioPath,
                            sub.TargetSizeMb, sub.SourceDuration);
        LintText = CommandLint.Describe(CommandLint.Check(args, ResolveOutputOf(task), task.SourcePath,
                                                          sub?.SubtitlePath, sub?.SubtitleMode, extra));
    }

    // ==================== 试跑选中 ====================

    /// <summary>
    /// 🧪 试跑选中：用小样验证「这条任务」的参数 —— 有区间就只跑区间（最多截 5 秒由区间决定），
    /// 没区间就跑前 5 秒；成功直接交给外部播放器 / ffplay。
    /// <para>⛔ 不改任务状态、不写输出路径（小样落在临时目录），纯粹是「看一眼再决定跑不跑」。</para>
    /// </summary>
    [RelayCommand]
    private async Task TrialSelectedAsync()
    {
        var t = Selected;
        if (t == null) { StatusText = "先在左侧选中一个任务。"; return; }
        if (IsRunning) { StatusText = "正在跑批处理 —— 等它跑完再试跑。"; return; }

        var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
        var (okCheck, msgCheck) = await FfmpegService.CheckAsync(ffmpeg);
        if (!okCheck) { StatusText = "⚠ " + msgCheck; return; }

        var dir = Path.Combine(Path.GetTempPath(), "NewToys", "trial");
        Directory.CreateDirectory(dir);
        var ext = t switch
        {
            TranscodeTask x => x.ContainerDisplay,
            _ => ".mp4"
        };
        var outFile = Path.Combine(dir, $"trial_{DateTime.Now:HHmmss}{NormalizeExt(ext, ".mp4")}");

        var args = BuildArgs(t, includeRaw: true, overrideOutput: outFile);
        if (args.Count == 0) { StatusText = "无法生成 ffmpeg 参数。"; return; }

        // 没有区间 → 只转前 5 秒（插在输出路径之前 = 输出选项）
        var limited = args.Any(a => string.Equals(a, "-t", StringComparison.Ordinal)
                                    || string.Equals(a, "-to", StringComparison.Ordinal));
        if (!limited && args.Count > 1)
        {
            args.Insert(args.Count - 1, "-t");
            args.Insert(args.Count - 1, "5");
        }

        var len = t switch
        {
            CutTask c => c.Length > 0 ? c.Length : 5,
            TranscodeTask x => x.HasTrim ? Math.Max(1, x.TrimEnd - x.TrimStart) : 5,
            _ => 5
        };

        StatusText = $"试跑「{t.Name}」…";
        var (ok, message, size) = await FfmpegService.RunAsync(ffmpeg, args, outFile, len, null);
        if (!ok) { StatusText = "⚠ 试跑失败：" + message; return; }

        var (played, playMsg) = FfmpegService.Play(outFile, _cfg?.PlayerPath, _cfg?.FfmpegPath);
        StatusText = played
            ? $"试跑成功（{size / 1024.0:0} KB）—— {playMsg}"
            : $"试跑成功（{size / 1024.0:0} KB）：{outFile} · {playMsg}";
    }

    partial void OnEditTranscodePresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsGifPreset));
        RefreshCommand();
    }

    partial void OnEditFrameRateChanged(int value) => RefreshCommand();
    partial void OnEditGifWidthChanged(int value) => RefreshCommand();
    partial void OnEditTranscodeCrfChanged(int value) => RefreshCommand();
    partial void OnEditMaxHeightChanged(int value) => RefreshCommand();
    partial void OnEditDefaultWidthChanged(int value) => RefreshCommand();
    partial void OnEditDefaultHeightChanged(int value) => RefreshCommand();
    partial void OnEditContainerChanged(string value) => RefreshCommand();
    partial void OnEditUseGpuChanged(bool value)
    {
        OnPropertyChanged(nameof(EncoderIndex));
        OnPropertyChanged(nameof(SelectedEncoder));
        RefreshCommand();
    }
    partial void OnEditTargetWidthChanged(int value) => RefreshCommand();
    partial void OnEditTargetHeightChanged(int value) => RefreshCommand();
    partial void OnEditBitrateModeChanged(string value) => RefreshCommand();
    partial void OnEditTargetBitrateChanged(string value) => RefreshCommand();
    partial void OnEditCutPresetIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsCutReEncode));
        RefreshCommand();
    }
    partial void OnEditStartChanged(string value) => RefreshCommand();
    partial void OnEditEndChanged(string value) => RefreshCommand();
    partial void OnEditExtraArgsChanged(string value) => RefreshCommand();
    partial void OnEditCutCrfChanged(int value) => RefreshCommand();
    partial void OnEditOutputChanged(string value) => RefreshCommand();

    /// <summary>📋 复制选中任务的 ffmpeg 命令行（手改过就选手改的那条）。</summary>
    [RelayCommand]
    private async Task CopyCommandLineAsync()
    {
        var text = CommandPreview;
        if (Selected == null) { StatusText = "先在左侧选中一个任务。"; return; }

        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制 ffmpeg 命令行。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>↺ 把命令恢复成「由参数自动生成」（丢弃手改）。</summary>
    [RelayCommand]
    private void ResetCommand()
    {
        if (Selected == null) return;
        IsArgsEdited = false;
        Selected.RawArgs = "";
        RefreshCommand();
        StatusText = "命令已恢复为由参数自动生成。";
    }

    /// <summary>
    /// 💾 保存命令：只把「⌨ ffmpeg 参数」框里的内容写回选中任务的 <c>RawArgs</c>（⛔ 不动其它编辑缓冲）。
    /// <para>与工具条「保存修改」的分工：那个是全量提交（顺手把命令一起写回），这个是改完参数串就地落一次。</para>
    /// </summary>
    [RelayCommand]
    private void SaveCommandLine()
    {
        var t = Selected;
        if (t == null) { StatusText = "先在左侧选中一个任务。"; return; }

        var args = (EditArgs ?? "").Trim();
        var edited = IsArgsEdited && args.Length > 0;
        t.RawArgs = edited ? args : "";
        _store?.Save();

        RefreshView();          // 让列表「命令摘要」列跟着变（选中项由 RefreshView 自己保住）
        RefreshCommand();
        StatusText = edited
            ? $"已保存「{t.Name}」的手改命令 —— 跑的时候以它为准。"
            : $"「{t.Name}」的参数与自动生成一致，已按自动生成执行。";
    }

    // ==================== 列表 ====================

    [ObservableProperty] private string _kindHeader = "类型";
    [ObservableProperty] private string _nameHeader = "文件 ▲";
    [ObservableProperty] private string _paramHeader = "参数";
    [ObservableProperty] private string _beforeHeader = "处理前";
    [ObservableProperty] private string _afterHeader = "处理后";
    [ObservableProperty] private string _statusHeader = "状态";
    // 扩充列（§6.4.8）
    [ObservableProperty] private string _sourceHeader = "源文件";
    [ObservableProperty] private string _commandHeader = "命令摘要";
    [ObservableProperty] private string _createdHeader = "创建时间";
    [ObservableProperty] private string _finishedHeader = "完成时间";

    public bool IsKindVisible => Vis("Kind");
    public bool IsNameVisible => Vis("Name");
    public bool IsParamVisible => Vis("Param");
    public bool IsBeforeVisible => Vis("Before");
    public bool IsAfterVisible => Vis("After");
    public bool IsStatusVisible => Vis("Status");
    public bool IsProgressVisible => Vis("Progress");
    // 扩充列
    public bool IsSourceVisible => Vis("Source");
    public bool IsCommandVisible => Vis("Command");
    public bool IsCreatedVisible => Vis("Created");
    public bool IsFinishedVisible => Vis("Finished");

    protected override string PageKey => "video-batch";

    /// <summary>v3.22 §4.8.1：每列一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Kind", "类型", "1.0*", sample: "剪辑+转码"),
        new("Name", "文件", "2.6*", sample: "示例视频 2026.mp4"),
        new("Param", "参数", "2.2*", sample: "00:00:10 - 00:01:30"),
        new("Before", "处理前", "1.5*", sample: "1.5 GB"),
        new("After", "处理后", "1.5*", sample: "820.5 MB"),
        new("Status", "状态", "1.2*", sample: "已完成"),
        new("Progress", "进度", "1.1*", sample: "100%", sortable: false),
        // §6.4.8 扩充（源文件 / 命令摘要 / 创建时间；类型 / 状态 / 进度 / 输出大小已有）
        new("Source", "源文件", "2.2*", sample: @"D:\Videos\示例视频 2026.mp4"),
        new("Command", "命令摘要", "2.4*", sample: "-c:v libx265 -crf 23"),
        new("Created", "创建时间", "1.3*", sample: "2026-10-13 15:00"),
        // 运行记录：跑完一个任务写一行（完成时间 + 耗时），也是"这条已完成、别重复跑"的凭据
        new("Finished", "完成时间", "1.5*", sample: "10-30 15:32 · 1m20s"),
    };

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public VideoTaskBase? DetailTask => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（的 L2 打样页，需求 §4.5.1） ====================

    /// <summary>
    /// 📂 选输出文件。平台文件选择器**只能在 View 层用**（VM 不引 Avalonia）⇒ 由 <c>VideoBatchView</c>
    /// 在 <c>OnDataContextChanged</c> 里注入实现（声明式卡的动作按钮没有本页 code-behind 可挂）。
    /// </summary>
    public ICommand? PickOutputCommand { get; set; }

    /// <summary>📝 选择字幕文件（code-behind 挂真正实现）。</summary>
    public ICommand? PickSubtitleCommand { get; set; }

    /// <summary>🎵 选择替换用的音频文件（code-behind 挂真正实现）。</summary>
    public ICommand? PickAudioCommand { get; set; }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

}
