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

/// <summary>「前后对比」的一行（详情卡里并排显示的两列）。</summary>
public sealed class CompareRow
{
    public string Label { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
}

/// <summary>
/// 「视频工具 → 批量处理」页（v3.8）。
/// <para>
/// **列表是「剪辑 + 转码」混排的统一队列** —— 两类任务的公共基类是
/// <see cref="VideoTaskBase"/>（状态 / 进度 / 文件名 / 输出大小都在它上面），所以直接把
/// <c>CutTasks</c> 与 <c>TranscodeTasks</c> 合并进同一个 <see cref="Items"/> 即可，
/// ⛔ **不改 `VideoTaskStore` 的存储结构**（它仍分两个集合落盘）。
/// </para>
/// <para>
/// 运行按类型分派：剪辑 → <c>BuildCutArgs</c>，转码 → <c>BuildTranscodeArgs</c>，统一走 <c>RunAsync</c>。
/// </para>
/// </summary>
public partial class VideoBatchViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条 + 选择器命令（/ 需求 §4.8.5） ====================

    private const string VideoFilePatterns =
        "*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.ts;*.m4v;*.mpg;*.mpeg";

    /// <summary>📂 添加文件（可多选）→ 按工具条上选的类型建任务。</summary>
    [RelayCommand]
    private async Task PickFilesAsync()
    {
        var paths = await DialogHooks.PickFilesAsync("选择视频文件", null, true, VideoFilePatterns);
        if (paths.Count == 0) return;
        await AddFilesAsync(paths);
    }

    /// <summary>📤 导出任务：把当前队列（剪辑 + 转码）序列化成 JSON 文件。</summary>
    [RelayCommand]
    private async Task ExportTasksAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出任务为 JSON 文件", ImportExportDefaultDir, "video-tasks.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        ExportTasks(path);
    }

    /// <summary>📥 导入任务：从导出的 JSON 里追加任务进当前队列（⛔ 不覆盖已有）。</summary>
    [RelayCommand]
    private async Task ImportTasksAsync()
    {
        var files = await DialogHooks.PickFilesAsync("导入任务 JSON 文件", ImportExportDefaultDir, false, "*.json");
        var path = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;
        ImportTasks(path);
    }

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📂 添加文件", Classes = "ghost", CommandPath = nameof(PickFilesCommand), ToolTip = "按左边选的类型建任务；只是建任务，不立刻跑" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "按文件名 / 输出过滤…", ValuePath = nameof(SearchText) },
        new() { Kind = ToolbarItemKind.Separator },
        new() { Kind = ToolbarItemKind.Combo, Width = 220, ItemsPath = nameof(Presets), DisplayPath = "Name", SelectedValuePath = nameof(DefaultPresetId), SelectedValueBindingPath = "Id", ToolTip = "只影响随后加入的**转码**任务；已存在的任务在右侧详情卡里单独改" },
        new() { Text = "▶▶ 批量运行", Classes = "ghost", CommandPath = nameof(RunAllCommand), IsEnabledPath = nameof(IsNotBusy), ToolTip = "按列表顺序依次处理（剪辑 / 转码各按自己的参数），跑完自动探测输出文件" },
        new() { Text = "▶ 只跑选中", CommandPath = nameof(RunSelectedCommand), IsEnabledPath = nameof(IsNotBusy) },
        new() { Text = "⏹ 停止", CommandPath = nameof(StopCommand), IsEnabledPath = nameof(IsRunning) },
        new() { Kind = ToolbarItemKind.Separator },
        new() { Text = "💾 保存修改", CommandPath = nameof(SaveCommand), IsEnabledPath = nameof(HasSelection), ToolTip = "把详情卡里改的参数写回选中的任务" },
        new() { Text = "🗑 删除", CommandPath = nameof(DeleteCommand), IsEnabledPath = nameof(IsNotBusy) },
        new() { Text = "📂 输出目录", CommandPath = nameof(OpenOutputFolderCommand), IsEnabledPath = nameof(HasSelection) },
        new() { Text = "📋 复制命令", CommandPath = nameof(CopyCommandLineCommand), IsEnabledPath = nameof(HasSelection) },
        new() { Text = "↺ 重置为自动", CommandPath = nameof(ResetCommandCommand), IsEnabledPath = nameof(IsArgsEdited), IsVisiblePath = nameof(IsArgsEdited), ToolTip = "丢弃手改的命令，恢复成由参数自动生成" },
        new() { Kind = ToolbarItemKind.Separator },
        new() { Text = "📤 导出任务", CommandPath = nameof(ExportTasksCommand), ToolTip = "把当前队列（剪辑 + 转码）导出成 JSON 文件，可用外部脚本批量加工后再导入" },
        new() { Text = "📥 导入任务", CommandPath = nameof(ImportTasksCommand), ToolTip = "从导出的 JSON 里追加任务进当前队列（不覆盖已有）" },
        new() { Text = "🧹 清理已结束", CommandPath = nameof(ClearFinishedCommand), IsEnabledPath = nameof(IsNotBusy) },
        new() { Text = "🧹 清除缓存", CommandPath = nameof(ClearCacheCommand), IsEnabledPath = nameof(IsNotBusy), ToolTip = "只清页面上的这条列表（不再显示它们）：⛔ 磁盘上的 config\\video-tasks.json 不会动，点「📥 导入任务」可以再读回来" },
        new() { Kind = ToolbarItemKind.Separator },
        new() { Text = "➡ 送预览", Classes = "ghost", CommandPath = nameof(SendToPreviewCommand), IsEnabledPath = nameof(HasSelection), ToolTip = "把选中的任务源文件送进「视频预览」页（一次只编一个，进入后自动载入）" },
    };

    private readonly VideoTaskStore? _store;
    private readonly IAppConfigService? _cfg;
    private readonly INavigationService? _nav;

    private CancellationTokenSource? _runCts;

    // StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private VideoTaskBase? _selected;
    [ObservableProperty] private string _summaryText = "还没有任务";
    [ObservableProperty] private bool _isRunning;

    public bool IsNotBusy => !IsBusy && !IsRunning;
    public bool HasSelection => Selected != null;

    /// <summary>全局导入/导出默认目录（当前配置档 <c>data\</c>）。</summary>
    public string ImportExportDefaultDir => _cfg?.DataDirectory ?? "";

    /// <summary>队列里有没有任务。
    /// ⚠️ <see cref="ItemsRaw"/> 是 <c>IEnumerable</c>，只有 LINQ 的 <c>Count()</c> 方法、**没有** <c>Count</c> 属性
    /// （写 <c>.Count &gt; 0</c> 会 CS0019「运算符无法应用于方法组」）→ 用 <c>Any()</c>。</summary>
    public bool HasItems => ItemsRaw.Any();

    /// <summary>原始两个集合（单例持有，切页不丢）。</summary>
    private ObservableCollection<CutTask> CutTasks => _store?.CutTasks ?? new ObservableCollection<CutTask>();
    private ObservableCollection<TranscodeTask> TranscodeTasks => _store?.TranscodeTasks ?? new ObservableCollection<TranscodeTask>();
    private IEnumerable<VideoTaskBase> ItemsRaw => CutTasks.Cast<VideoTaskBase>().Concat(TranscodeTasks);

    /// <summary>排序 / 过滤后的统一队列（真正绑到 ListBox 的那个）。</summary>
    public ObservableCollection<VideoTaskBase> Items { get; } = new();

    /// <summary>详情卡里的「前 → 后」对比表。</summary>
    public ObservableCollection<CompareRow> CompareRows { get; } = new();

    /// <summary>预设下拉框的数据源（不写死在 XAML 里）。</summary>
    public IReadOnlyList<TranscodePreset> Presets => TranscodePreset.All;
    public IReadOnlyList<CutPresetOption> CutPresetOptions => CutPresets.All;
    public IReadOnlyList<string> ContainerOptions => FfmpegService.Containers;

    /// <summary>「＋ 添加文件」时新建哪一类任务。</summary>
    [ObservableProperty] private bool _newAsCut;

    /// <summary>反向视图（RadioButton 两向绑定用：勾「转码任务」= NewAsCut 为 false）。</summary>
    public bool NewAsTranscode
    {
        get => !NewAsCut;
        set => NewAsCut = !value;
    }

    /// <summary>当前任务的类型名（详情卡显示）。</summary>
    public string SelectedKind => DetailTask == null ? "—" : KindOf(DetailTask);

    // ==================== 编辑缓冲（两套，按选中任务类型生效） ====================

    // 转码
    [ObservableProperty] private string _editPresetId = "h265";
    [ObservableProperty] private int _editCrf;
    [ObservableProperty] private int _editMaxHeight;

    // 默认宽 / 高上限（v3.19；0 = 不用，源更大才缩、否则保原始）
    [ObservableProperty] private int _editDefaultWidth = 1920;
    [ObservableProperty] private int _editDefaultHeight = 1080;

    // 剪辑
    [ObservableProperty] private string _editCutPresetId = CutPresets.Copy;
    [ObservableProperty] private string _editStart = "";
    [ObservableProperty] private string _editEnd = "";
    [ObservableProperty] private string _editExtraArgs = "";
    [ObservableProperty] private int _editCutCrf = 20;

    /// <summary>多段剪切串（每行一段 <c>开始-结束</c>）。</summary>
    [ObservableProperty] private string _editSegmentsText = "";

    /// <summary>多段解析的即时反馈（详情卡提示行；空 = 没填 / 一行都没解析出来）。</summary>
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

    // 字幕
    [ObservableProperty] private string _editSubtitlePath = "";
    [ObservableProperty] private string _editSubtitleMode = SubtitleModes.None;
    [ObservableProperty] private int _editSubtitleFontSize = 22;
    [ObservableProperty] private int _editSubtitleMarginV = 24;

    /// <summary>「字幕方式」下拉选项。</summary>
    public IReadOnlyList<SubtitleModeOption> SubtitleModeChoices => SubtitleModes.All;

    /// <summary>硬字幕的两个样式字段是否显示。</summary>
    public bool IsHardSubtitleEdit => string.Equals(EditSubtitleMode, SubtitleModes.Hard, StringComparison.OrdinalIgnoreCase);

    /// <summary>软字幕的说明行是否显示。</summary>
    public bool IsSoftSubtitleEdit => string.Equals(EditSubtitleMode, SubtitleModes.Soft, StringComparison.OrdinalIgnoreCase);

    /// <summary>软字幕说明文案（详情卡的提示行）。</summary>
    public string SoftSubtitleHint
        => "软字幕 = 视频 / 音频原样 copy，字幕单独成轨（秒完成、播放器里可开关）；输出固定 .mkv。" +
           "此时编码 / 分辨率 / 码率设置不生效。";

    partial void OnEditSubtitleModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsHardSubtitleEdit));
        OnPropertyChanged(nameof(IsSoftSubtitleEdit));
        RefreshCommand();
    }

    partial void OnEditSubtitlePathChanged(string value) => RefreshCommand();
    partial void OnEditSubtitleFontSizeChanged(int value) => RefreshCommand();
    partial void OnEditSubtitleMarginVChanged(int value) => RefreshCommand();

    // ==================== 裁剪 / 音轨 / 目标体积（~ ） ====================

    /// <summary>裁剪宽（0 = 不裁剪）。</summary>
    [ObservableProperty] private int _editCropWidth;

    /// <summary>裁剪高。</summary>
    [ObservableProperty] private int _editCropHeight;

    /// <summary>裁剪左上角 X。</summary>
    [ObservableProperty] private int _editCropX;

    /// <summary>裁剪左上角 Y。</summary>
    [ObservableProperty] private int _editCropY;

    /// <summary>音频方式（取值见 <see cref="AudioModes"/>）。</summary>
    [ObservableProperty] private string _editAudioMode = AudioModes.Keep;

    /// <summary>音量增益（dB）。</summary>
    [ObservableProperty] private double _editVolumeDb;

    /// <summary>替换用的音频文件。</summary>
    [ObservableProperty] private string _editAudioPath = "";

    /// <summary>目标体积（MB；0 = 不用）。</summary>
    [ObservableProperty] private double _editTargetSizeMb;

    /// <summary>「音频方式」下拉选项。</summary>
    public IReadOnlyList<AudioModeOption> AudioModeChoices => AudioModes.All;

    public bool IsVolumeModeEdit => string.Equals(EditAudioMode, AudioModes.Volume, StringComparison.OrdinalIgnoreCase);
    public bool IsReplaceAudioEdit => string.Equals(EditAudioMode, AudioModes.Replace, StringComparison.OrdinalIgnoreCase);

    /// <summary>目标体积反算提示（时长取选中任务的源时长）。</summary>
    public string TargetSizeHint
    {
        get
        {
            if (EditTargetSizeMb <= 0) return "";
            var duration = (DetailTask as TranscodeTask)?.SourceDuration ?? 0;
            if (duration <= 0) return "⚠ 这条任务没有源时长 —— 算不出码率（重新加一次任务让它探测）。";

            var probe = new TranscodeTask
            {
                PresetId = EditPresetId,
                AudioMode = string.IsNullOrWhiteSpace(EditAudioMode) ? AudioModes.Keep : EditAudioMode,
                TargetSizeMb = EditTargetSizeMb,
                SourceDuration = duration
            };
            var kbps = FfmpegService.AutoBitrateKbps(probe);
            return kbps > 0
                ? $"按 {EditTargetSizeMb:0.#} MB ÷ {CutTask.FormatTime(duration)} ≈ 视频 {kbps} kbps（已扣音频与容器开销）。"
                : "";
        }
    }

    public bool HasTargetSizeHint => TargetSizeHint.Length > 0;

    partial void OnEditCropWidthChanged(int value) => RefreshCommand();
    partial void OnEditCropHeightChanged(int value) => RefreshCommand();
    partial void OnEditCropXChanged(int value) => RefreshCommand();
    partial void OnEditCropYChanged(int value) => RefreshCommand();

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

    /// <summary>🖼 探测选中任务的黑边：结果只填编辑缓冲，点「💾 保存」才写回任务。</summary>
    [RelayCommand]
    private async Task DetectCropAsync()
    {
        var t = Selected;
        if (t == null) { StatusText = "先在左侧选中一个任务。"; return; }
        if (string.IsNullOrWhiteSpace(t.SourcePath) || !System.IO.File.Exists(t.SourcePath))
        {
            StatusText = "源文件不存在 —— 探测不了黑边。";
            return;
        }

        try
        {
            var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
            var at = t is TranscodeTask { HasTrim: true } x ? x.TrimStart : 0;
            StatusText = "探测黑边中…";
            var found = await FfmpegService.DetectCropAsync(ffmpeg, t.SourcePath, at);
            if (found == null)
            {
                StatusText = "⚠ 没探测到有效画面 —— 裁剪值保持不变。";
                return;
            }

            var (w, h, cx, cy) = found.Value;
            EditCropWidth = w;
            EditCropHeight = h;
            EditCropX = cx;
            EditCropY = cy;
            StatusText = w > 0 && h > 0
                ? $"探测到有效画面 {w}×{h} @ ({cx}, {cy}) —— 别忘了点「💾 保存」。"
                : "⚠ 探测结果异常（0×0）—— 裁剪值保持不变。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 探测失败：" + ex.Message;
        }
    }

    // 公共
    [ObservableProperty] private string _editOutput = "";

    /// <summary>转码的容器扩展名（带点）；留空 = 用预设的。</summary>
    [ObservableProperty] private string _editContainer = "";

    /// <summary>是否走 GPU 编码器（NVENC）。</summary>
    [ObservableProperty] private bool _editUseGpu;

    /// <summary>目标宽 / 高（0 = 不约束）。</summary>
    [ObservableProperty] private int _editTargetWidth;
    [ObservableProperty] private int _editTargetHeight;

    /// <summary>码率模式：<c>crf</c> / <c>bitrate</c>。</summary>
    [ObservableProperty] private string _editBitrateMode = "crf";

    /// <summary>目标码率文本（<c>bitrate</c> 模式才用）。</summary>
    [ObservableProperty] private string _editTargetBitrate = "";

    /// <summary>GIF 帧率（只对 GIF 预设生效）。</summary>
    [ObservableProperty] private int _editFrameRate = 12;

    /// <summary>GIF 宽度（0 = 保持原宽）。</summary>
    [ObservableProperty] private int _editGifWidth = 480;

    /// <summary>当前编辑的转码预设是不是 GIF（详情卡据此显隐两个 GIF 字段）。</summary>
    public bool IsGifPreset => FfmpegService.IsGif(TranscodePreset.Find(EditPresetId));

    // ---- 旋转 / 翻转（v3.19）----

    /// <summary>旋转 / 翻转编辑缓冲（取值见 <see cref="RotateOptions"/>；空 = 不旋转）。</summary>
    [ObservableProperty] private string _editRotate = "";

    /// <summary>「旋转」下拉的选项（不写死在 XAML 里）。</summary>
    public IReadOnlyList<RotateOption> RotateChoices => RotateOptions.All;

    /// <summary>「旋转」下拉选中项（变化经 <see cref="OnSelectedRotateChanged"/> 回写 <see cref="EditRotate"/>）。</summary>
    [ObservableProperty] private RotateOption? _selectedRotate;

    partial void OnSelectedRotateChanged(RotateOption? value)
    {
        if (value != null) EditRotate = value.Value;
    }

    partial void OnEditRotateChanged(string value) => RefreshCommand();

    /// <summary>
    /// CPU / GPU 下拉框绑它（0 = CPU、1 = GPU）。
    /// <para>⚠️ 与「视频预览」页同名同义：不直接绑 <c>bool</c>，<c>ComboBox.SelectedIndex</c> 绑 <c>int</c> 最可靠
    /// （XAML 里的字面量其实是字符串，绑 <c>bool</c> 容易踩转换坑）。</para>
    /// </summary>
    public int EncoderIndex
    {
        get => EditUseGpu ? 1 : 0;
        set { EditUseGpu = value == 1; OnPropertyChanged(); }
    }

    /// <summary>码率模式下拉框绑它（0 = CRF、1 = 目标码率），理由同 <see cref="EncoderIndex"/>。</summary>
    public int BitrateModeIndex
    {
        get => string.Equals(EditBitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set { EditBitrateMode = value == 1 ? "bitrate" : "crf"; OnPropertyChanged(); }
    }

    /// <summary>CRF 输入框是否用得上（crf 模式下才显示）。</summary>
    public bool UseCrf => !string.Equals(EditBitrateMode, "bitrate", StringComparison.OrdinalIgnoreCase);

    /// <summary>剪切分区里 CRF 输入框是否用得上（选了「重新编码」才有意义）。</summary>
    public bool IsCutReEncode => string.Equals(EditCutPresetId, CutPresets.ReEncode, StringComparison.OrdinalIgnoreCase);

    /// <summary>当前会用的视频编码器名（CPU / GPU 一眼可见，避免以为用了 GPU 其实没用）。</summary>
    public string SelectedEncoder
        => DetailTask is TranscodeTask
            ? FfmpegService.ResolveEncoder(TranscodePreset.Find(EditPresetId).VideoCodec, EditUseGpu)
            : "—";

    /// <summary>新建转码任务时用的预设 / 最大高度（工具条上选，只影响随后加入的条目）。</summary>
    [ObservableProperty] private string _defaultPresetId = "h265";
    [ObservableProperty] private int _defaultMaxHeight;

    /// <summary>当前选中项是不是剪辑任务（详情卡据此切换两套参数区）。</summary>
    public bool SelectedIsCut => Selected is CutTask;
    public bool SelectedIsTranscode => Selected is TranscodeTask;

    // ==================== 勾选（v3.16 §6.4.10：行首勾选框 + 表头 ☑ 的右键菜单） ====================

    /// <summary>已勾选的任务数（勾选只做**多选标记**，⛔ 不参与命令生成）。</summary>
    public int CheckedCount => ItemsRaw.Count(t => t.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    /// <summary>表头勾选框的三态（v3.16.1）：null = 部分勾选（显示为实心方块）。</summary>
    public bool? AllCheckedState
    {
        get
        {
            var total = ItemsRaw.Count();
            if (total == 0) return false;
            var c = CheckedCount;
            return c == 0 ? false : (c >= total ? true : null);
        }
    }

}
