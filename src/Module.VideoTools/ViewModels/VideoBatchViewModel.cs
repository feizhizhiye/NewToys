using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// 「视频工具 → 批量处理」页（v3.8，由原「格式转码」页**改名改造**而来）。
/// <para>
/// 与原来最大的区别：**列表是「剪辑 + 转码」混排的统一队列** —— 两类任务的公共基类是
/// <see cref="VideoTaskBase"/>（状态 / 进度 / 文件名 / 输出大小都在它上面），所以直接把
/// <c>CutTasks</c> 与 <c>TranscodeTasks</c> 合并进同一个 <see cref="Items"/> 即可，
/// ⛔ **不改 `VideoTaskStore` 的存储结构**（它仍分两个集合落盘）。
/// </para>
/// <para>
/// 运行按类型分派：剪辑 → <c>BuildCutArgs</c>，转码 → <c>BuildTranscodeArgs</c>，统一走 <c>RunAsync</c>。
/// </para>
/// </summary>
public partial class VideoBatchViewModel : ListPageViewModelBase
{
    private readonly VideoTaskStore? _store;
    private readonly IAppConfigService? _cfg;

    private CancellationTokenSource? _runCts;

    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private VideoTaskBase? _selected;
    [ObservableProperty] private string _summaryText = "还没有任务";
    [ObservableProperty] private bool _isRunning;

    public bool IsNotBusy => !IsBusy && !IsRunning;
    public bool HasSelection => Selected != null;

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

    /// <summary>当前选中任务的类型名（详情卡显示）。</summary>
    public string SelectedKind => Selected == null ? "—" : KindOf(Selected);

    // ==================== 编辑缓冲（两套，按选中任务类型生效） ====================

    // 转码
    [ObservableProperty] private string _editPresetId = "h264";
    [ObservableProperty] private int _editCrf;
    [ObservableProperty] private int _editMaxHeight;

    // 剪辑
    [ObservableProperty] private string _editCutPresetId = CutPresets.Copy;
    [ObservableProperty] private string _editStart = "";
    [ObservableProperty] private string _editEnd = "";
    [ObservableProperty] private string _editExtraArgs = "";
    [ObservableProperty] private int _editCutCrf = 20;

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
        => Selected is TranscodeTask t
            ? FfmpegService.ResolveEncoder(TranscodePreset.Find(EditPresetId).VideoCodec, EditUseGpu)
            : "—";

    /// <summary>新建转码任务时用的预设 / 最大高度（工具条上选，只影响随后加入的条目）。</summary>
    [ObservableProperty] private string _defaultPresetId = "h264";
    [ObservableProperty] private int _defaultMaxHeight;

    /// <summary>当前选中项是不是剪辑任务（详情卡据此切换两套参数区）。</summary>
    public bool SelectedIsCut => Selected is CutTask;
    public bool SelectedIsTranscode => Selected is TranscodeTask;

    // ==================== 详情卡折叠态（与「视频预览」页同名同义，两页可照抄同一段 XAML） ====================

    /// <summary>「剪辑」分区是否展开。</summary>
    [ObservableProperty] private bool _isCutSectionExpanded = true;

    /// <summary>「转码」分区是否展开。</summary>
    [ObservableProperty] private bool _isTranscodeSectionExpanded = true;

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
    private List<string> BuildArgs(VideoTaskBase t, bool includeRaw = true)
    {
        var output = ResolveOutputOf(t);

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
            TranscodeTask x => FfmpegService.DefaultOutputPath(x.SourcePath, "_" + x.PresetId, x.ContainerDisplay),
            _ => ""
        };
    }

    partial void OnEditArgsChanged(string value)
    {
        var edited = !string.IsNullOrWhiteSpace(value)
                     && !string.Equals(value.Trim(), AutoArgsText, StringComparison.Ordinal);
        if (IsArgsEdited != edited) IsArgsEdited = edited;
    }

    /// <summary>刷新命令（未手改过 → 参数文本始终跟随参数走）。</summary>
    private void RefreshCommand()
    {
        if (!IsArgsEdited) EditArgs = AutoArgsText;
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnEditPresetIdChanged(string value) => RefreshCommand();
    partial void OnEditCrfChanged(int value) => RefreshCommand();
    partial void OnEditMaxHeightChanged(int value) => RefreshCommand();
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

    // ==================== 列表 ====================

    [ObservableProperty] private string _kindHeader = "类型";
    [ObservableProperty] private string _nameHeader = "文件 ▲";
    [ObservableProperty] private string _paramHeader = "参数";
    [ObservableProperty] private string _beforeHeader = "处理前";
    [ObservableProperty] private string _afterHeader = "处理后";
    [ObservableProperty] private string _statusHeader = "状态";

    public bool IsKindVisible => Vis("Kind");
    public bool IsNameVisible => Vis("Name");
    public bool IsParamVisible => Vis("Param");
    public bool IsBeforeVisible => Vis("Before");
    public bool IsAfterVisible => Vis("After");
    public bool IsStatusVisible => Vis("Status");
    public bool IsProgressVisible => Vis("Progress");

    protected override string PageKey => "video-batch";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Kind", "类型", "1.0*"),
        new("Name", "文件", "2.6*"),
        new("Param", "参数", "2.2*"),
        new("Before", "处理前", "1.5*"),
        new("After", "处理后", "1.5*"),
        new("Status", "状态", "1.2*"),
        new("Progress", "进度", "1.1*", sortable: false),
    };

    public VideoBatchViewModel() { }

    public VideoBatchViewModel(VideoTaskStore store, IAppConfigService cfg, IColumnLayoutService cols)
        : base(cols)
    {
        _store = store;
        _cfg = cfg;
        LoadLayout();
        RefreshView();
    }

    /// <summary>
    /// 进入本页时把「视频列表」页送过来的待处理文件建成任务（两类都收）。
    /// ⚠️ 必须在 VM 构造后由 View 调一次（构造函数里做会碰到"UI 还没 ready"）。
    /// </summary>
    public async Task ConsumePendingAsync()
    {
        if (_store == null) return;

        var transcode = _store.TakePendingTranscode();
        var cut = _store.TakePendingCut();
        if (transcode.Count == 0 && cut.Count == 0) return;

        if (cut.Count > 0) await AddCutFilesAsync(cut);
        if (transcode.Count > 0) await AddTranscodeFilesAsync(transcode);

        RefreshView();
    }

    protected override void RefreshView()
    {
        IEnumerable<VideoTaskBase> q = ItemsRaw;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || (t.OutputPath ?? "").Contains(text, StringComparison.CurrentCultureIgnoreCase));

        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Kind" => SortDescending ? q.OrderByDescending(KindOf, byText) : q.OrderBy(KindOf, byText),
            "Param" => SortDescending ? q.OrderByDescending(ParamOf, byText) : q.OrderBy(ParamOf, byText),
            "Before" => SortDescending ? q.OrderByDescending(BeforeSizeOf) : q.OrderBy(BeforeSizeOf),
            "After" => SortDescending ? q.OrderByDescending(t => t.OutputSize) : q.OrderBy(t => t.OutputSize),
            "Status" => SortDescending ? q.OrderByDescending(t => t.Status, byText) : q.OrderBy(t => t.Status, byText),
            _ => SortDescending ? q.OrderByDescending(t => t.Name, byText) : q.OrderBy(t => t.Name, byText)
        };

        Items.Clear();
        foreach (var t in q) Items.Add(t);

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));

        var all = ItemsRaw.ToList();
        SummaryText = all.Count == 0
            ? "还没有任务"
            : $"共 {all.Count} 个任务（剪辑 {all.Count(t => t is CutTask)} · 转码 {all.Count(t => t is TranscodeTask)}）"
              + $" · 已完成 {all.Count(t => t.Status == VideoTaskStatus.Done)}"
              + (all.Count(t => t.Status == VideoTaskStatus.Failed) > 0
                  ? $" · 失败 {all.Count(t => t.Status == VideoTaskStatus.Failed)}" : "");
    }

    /// <summary>任务类型（列表「类型」列 / 排序用）：剪辑 / 转码 / 剪辑+转码。</summary>
    public static string KindOf(VideoTaskBase t) => t.KindDisplay;

    /// <summary>参数摘要（列表「参数」列）：剪辑看区间，转码看预设。</summary>
    public static string ParamOf(VideoTaskBase t) => t switch
    {
        CutTask c => c.RangeDisplay,
        TranscodeTask x => x.PresetName,
        _ => ""
    };

    private static long BeforeSizeOf(VideoTaskBase t) => t switch
    {
        TranscodeTask x => x.SourceSize,
        CutTask c => c.Duration > 0 ? (long)c.Duration : 0,
        _ => 0
    };

    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Kind" => KindOf((VideoTaskBase)row),
        "Name" => ((VideoTaskBase)row).Name,
        "Param" => ParamOf((VideoTaskBase)row),
        "Status" => ((VideoTaskBase)row).Status,
        "Before" => ((VideoTaskBase)row) is TranscodeTask x ? x.SourceSizeDisplay : ((VideoTaskBase)row).OutputSize <= 0 ? "" : "",
        "After" => ((VideoTaskBase)row).OutputSizeDisplay,
        _ => null
    };

    protected override double? FieldNumber(object row, string columnKey) => columnKey switch
    {
        "Before" => BeforeSizeOf((VideoTaskBase)row),
        "After" => ((VideoTaskBase)row).OutputSize,
        _ => null
    };

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    private void UpdateHeaders()
    {
        KindHeader = "类型" + SortArrow("Kind");
        NameHeader = "文件" + SortArrow("Name");
        ParamHeader = "参数" + SortArrow("Param");
        BeforeHeader = "处理前" + SortArrow("Before");
        AfterHeader = "处理后" + SortArrow("After");
        StatusHeader = "状态" + SortArrow("Status");
    }

    partial void OnSearchTextChanged(string value) => RefreshView();
    partial void OnNewAsCutChanged(bool value) => OnPropertyChanged(nameof(NewAsTranscode));

    partial void OnSelectedChanged(VideoTaskBase? value)
    {
        if (value is TranscodeTask x)
        {
            EditPresetId = x.PresetId;
            EditCrf = x.Crf;
            EditMaxHeight = x.MaxHeight;
            EditContainer = x.Container;
            EditUseGpu = x.UseGpuEncoder;
            EditTargetWidth = x.TargetWidth;
            EditTargetHeight = x.TargetHeight;
            EditBitrateMode = x.BitrateMode;
            EditTargetBitrate = x.TargetBitrate;
            EditStart = x.HasTrim ? CutTask.FormatTime(x.TrimStart) : "";
            EditEnd = x.TrimEnd > 0 ? CutTask.FormatTime(x.TrimEnd) : "";
        }
        else if (value is CutTask c)
        {
            EditCutPresetId = c.PresetId;
            EditStart = CutTask.FormatTime(c.Start);
            EditEnd = c.End > 0 ? CutTask.FormatTime(c.End) : "";
            EditExtraArgs = c.ExtraArgs;
            EditCutCrf = c.Crf;
        }
        EditOutput = value?.OutputPath ?? "";

        // 手改过的命令按原样载入编辑框（否则用户会看到"参数生成值"覆盖了自己的手改）
        IsArgsEdited = !string.IsNullOrWhiteSpace(value?.RawArgs);
        EditArgs = IsArgsEdited ? value!.RawArgs : AutoArgsText;
        OnPropertyChanged(nameof(CommandPreview));

        BuildCompareRows();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIsCut));
        OnPropertyChanged(nameof(SelectedIsTranscode));
        OnPropertyChanged(nameof(SelectedHasTrim));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedEncoder));
        OnPropertyChanged(nameof(IsCutReEncode));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>重建「前 → 后」对比表（选中项变了 / 跑完了都要刷）。</summary>
    private void BuildCompareRows()
    {
        CompareRows.Clear();
        CompareRows.Add(new CompareRow { Label = "类型", Before = Selected == null ? "—" : KindOf(Selected), After = "—" });

        switch (Selected)
        {
            case TranscodeTask t:
                CompareRows.Add(new CompareRow { Label = "大小", Before = t.SourceSizeDisplay, After = t.HasResult ? t.OutputSizeDisplay : "—" });
                CompareRows.Add(new CompareRow { Label = "编码", Before = t.SourceCodec.NullDash(), After = t.HasResult ? t.OutputCodec.NullDash() : "—" });
                CompareRows.Add(new CompareRow { Label = "分辨率", Before = t.SourceResolution.NullDash(), After = t.HasResult ? t.OutputResolution.NullDash() : "—" });
                CompareRows.Add(new CompareRow { Label = "码率", Before = t.SourceBitRate.NullDash(), After = t.HasResult ? t.OutputBitRate.NullDash() : "—" });
                CompareRows.Add(new CompareRow { Label = "时长", Before = t.SourceDurationDisplay, After = t.HasResult ? t.OutputDurationDisplay : "—" });
                CompareRows.Add(new CompareRow { Label = "体积变化", Before = "—", After = t.HasResult ? t.SavingDisplay : "—" });
                break;

            case CutTask c:
                CompareRows.Add(new CompareRow { Label = "源时长", Before = c.DurationDisplay, After = "—" });
                CompareRows.Add(new CompareRow { Label = "区间", Before = c.RangeDisplay, After = "—" });
                CompareRows.Add(new CompareRow { Label = "片段长", Before = c.LengthDisplay, After = "—" });
                CompareRows.Add(new CompareRow { Label = "输出大小", Before = "—", After = c.OutputSize > 0 ? c.OutputSizeDisplay : "—" });
                break;
        }
    }

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
        var added = 0;
        try
        {
            foreach (var path in files)
            {
                var preset = TranscodePreset.Find(DefaultPresetId);
                var task = new TranscodeTask
                {
                    SourcePath = path,
                    PresetId = preset.Id,
                    MaxHeight = DefaultMaxHeight,
                    OutputPath = FfmpegService.DefaultOutputPath(path, "_" + preset.Id, preset.Extension),
                    SourceSize = new FileInfo(path).Length
                };

                var (ok, _, info) = await VideoScanner.ProbeAsync(path, probe);
                if (ok && info != null)
                {
                    task.SourceDuration = info.Duration ?? 0;
                    task.SourceCodec = VideoItem.FriendlyCodecName(info.Codec);
                    task.SourceResolution = info.Width is > 0 && info.Height is > 0 ? $"{info.Width}×{info.Height}" : "";
                    task.SourceBitRate = info.BitRate is > 0 ? FormatBitRate(info.BitRate.Value) : "";
                }

                _store.AddTranscode(task);
                added++;
                StatusText = $"正在生成转码任务… {added}/{files.Count}";
            }

            RefreshView();
            StatusText = $"已生成 {added} 个转码任务（预设：{TranscodePreset.Find(DefaultPresetId).Name}）；可逐条改参数，再点「▶▶ 批量运行」。";
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

    /// <summary>🗑 删除选中任务。</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected == null || _store == null) return;
        var name = Selected.Name;

        if (Selected is CutTask c) _store.RemoveCut(c);
        else if (Selected is TranscodeTask t) _store.RemoveTranscode(t);

        Selected = null;
        RefreshView();
        StatusText = "已删除任务：" + name;
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
            x.PresetId = string.IsNullOrWhiteSpace(EditPresetId) ? "h264" : EditPresetId;
            x.Crf = EditCrf;
            x.MaxHeight = EditMaxHeight;
            x.Container = NormalizeExt(EditContainer, x.Preset.Extension);
            x.UseGpuEncoder = EditUseGpu;
            x.TargetWidth = EditTargetWidth;
            x.TargetHeight = EditTargetHeight;
            x.BitrateMode = string.IsNullOrWhiteSpace(EditBitrateMode) ? "crf" : EditBitrateMode;
            x.TargetBitrate = (EditTargetBitrate ?? "").Trim();

            // 区间：结束留空 / 不大于开始 = 不裁剪（HasTrim 由 TrimEnd > TrimStart 决定）
            var tStart = CutTask.ParseTime(EditStart) ?? 0;
            var tEnd = string.IsNullOrWhiteSpace(EditEnd) ? 0 : (CutTask.ParseTime(EditEnd) ?? 0);
            if (tEnd <= tStart) tEnd = 0;
            x.TrimStart = tStart;
            x.TrimEnd = tEnd;

            x.RawArgs = raw;                  // 手改的命令原样保留；没手改就交给参数生成
            var preset = x.Preset;
            if (output.Length == 0) output = FfmpegService.DefaultOutputPath(x.SourcePath, "_" + preset.Id, preset.Extension);
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
            if (end > 0 && end <= start) { StatusText = "结束时间必须大于开始时间。"; return; }

            c.Start = start;
            c.End = end;
            c.PresetId = string.IsNullOrWhiteSpace(EditCutPresetId) ? CutPresets.Copy : EditCutPresetId;
            c.ExtraArgs = EditExtraArgs ?? "";
            c.ReEncode = c.PresetId == CutPresets.ReEncode;
            c.Crf = EditCutCrf;
            c.RawArgs = raw;                    // 同上

            if (output.Length == 0) output = FfmpegService.DefaultOutputPath(c.SourcePath, "_cut", ".mp4");
            c.OutputPath = output;
            if (c.Status != VideoTaskStatus.Running) c.Reset();
            StatusText = $"已保存「{c.Name}」：{c.RangeDisplay} · {c.PresetName}";
        }

        EditOutput = output;
        _store?.Save();
        RefreshView();
        BuildCompareRows();
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

                if (ReferenceEquals(t, Selected)) BuildCompareRows();
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
}

/// <summary>小工具：空串显示成破折号（对比表里"没有值"要看得出来）。</summary>
internal static class StringDashExt
{
    public static string NullDash(this string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
}
