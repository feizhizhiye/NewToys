using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>预览缩略图：一个时刻 + 那一帧的图。</summary>
public partial class FrameThumb : ObservableObject
{
    /// <summary>时间点（秒）。</summary>
    public double Time { get; set; }

    /// <summary>时间码文本（<c>00:01:23.456</c>）。</summary>
    public string TimeText => CutTask.FormatTime(Time);

    /// <summary>抽出来的那一帧（jpg 已落盘，这里是加载好的位图）。</summary>
    public Bitmap? Image { get; set; }

    /// <summary>是不是当前设定的开始点（界面上给它描个边）。</summary>
    [ObservableProperty] private bool _isStart;

    /// <summary>是不是当前设定的结束点。</summary>
    [ObservableProperty] private bool _isEnd;
}

/// <summary>
/// 「视频工具 → 视频剪辑」页（v1.35 实装）。
/// <para>
/// 用法闭环：**选文件 → 抽关键帧预览 → 点缩略图定起止 → 「➕ 加入任务」（可加多条）→ 「▶▶ 批量运行」**。
/// 任务只是"待办"，加进来不立刻跑 —— 这样才谈得上"暂存后批量运行"。
/// </para>
/// <para>
/// 预览为什么抽<b>关键帧</b>：ffmpeg 定位到关键帧几乎是瞬间完成；随机时间点要先解码到那一帧，
/// 长视频上会明显卡顿（<c>FfmpegService.ReadKeyFrameTimesAsync</c> 先用 ffprobe 列出关键帧时刻）。
/// </para>
/// <para>
/// 默认 <c>-c copy</c>（不重新编码，秒出、无损）；起止点不在关键帧上时它会就近切，
/// 想要精确就勾「重新编码」（会慢，但帧级准确）。界面如实写清这一点。
/// </para>
/// </summary>
public partial class VideoCutViewModel : ListPageViewModelBase
{
    private readonly VideoTaskStore? _store;
    private readonly IAppConfigService? _cfg;

    private CancellationTokenSource? _runCts;

    /// <summary>抽帧用的临时目录（一次预览一份；换视频时重建）。</summary>
    private string _frameDir = "";

    /// <summary>当前这批缩略图属于哪个任务（换选中项时据此判断要不要清掉旧预览）。</summary>
    private string _previewedTaskId = "";

    [ObservableProperty] private string _statusText = "选一个视频 → 抽关键帧预览 → 点缩略图定开始 / 结束 → 「➕ 加入任务」→ 「▶▶ 批量运行」。";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private CutTask? _selected;
    [ObservableProperty] private string _summaryText = "还没有任务";

    /// <summary>预览缩略图（关键帧条）。</summary>
    public ObservableCollection<FrameThumb> Frames { get; } = new();

    // ==================== 播放器式预览（v1.35） ====================

    /// <summary>当前预览的时间点（秒）—— 时间轴滑块就绑它，拖动即时抽帧。</summary>
    [ObservableProperty] private double _previewTime;

    /// <summary>当前预览的大图（时间轴拖到哪就抽哪一帧）。</summary>
    [ObservableProperty] private Bitmap? _previewImage;

    /// <summary>是否正在抽帧（拖动时丢帧用：只保留最后一次请求）。</summary>
    private bool _extracting;
    private double _pendingPreview = -1;

    /// <summary>预览图的落盘序号（避免同一个文件被反复覆盖时读半截）。</summary>
    private int _previewIndex;

    /// <summary>预设 / 自定义参数的编辑缓冲（详情卡）。</summary>
    [ObservableProperty] private string _editPresetId = CutPresets.Copy;
    [ObservableProperty] private string _editExtraArgs = "";

    /// <summary>
    /// 「开始时间」滑块绑它（秒）。
    /// <para>⚠️ 为什么要单独一层：直接绑 <c>Selected.Start</c> 的话，拖动只改了任务对象、
    /// 不会同步时间码文本框 <see cref="EditStart"/>，随后点「💾 保存」会被那个旧文本覆盖回去。
    /// 这里改一处 → 任务对象与时间码一起更新。</para>
    /// </summary>
    public double StartSeconds
    {
        get => Selected?.Start ?? 0;
        set
        {
            if (Selected == null) return;
            Selected.Start = value;
            EditStart = CutTask.FormatTime(value);
            OnPropertyChanged();
        }
    }

    /// <summary>「结束时间」滑块绑它（秒；0 = 到末尾）。同上，与时间码文本框双向同步。</summary>
    public double EndSeconds
    {
        get => Selected?.End ?? 0;
        set
        {
            if (Selected == null) return;
            Selected.End = value;
            EditEnd = value > 0 ? CutTask.FormatTime(value) : "";
            OnPropertyChanged();
        }
    }

    /// <summary>剪切预设下拉框的数据源（不写死在 XAML 里）。</summary>
    public IReadOnlyList<CutPresetOption> CutPresetOptions => CutPresets.All;

    /// <summary>
    /// **将要执行的完整 ffmpeg 命令行**（详情卡里显示，改参数即时刷新）。
    /// 与真正跑的那条同源 —— 界面上看到的就是会执行的东西，不含"隐藏参数"。
    /// </summary>
    public string CommandPreview
    {
        get
        {
            var t = Selected;
            if (t == null) return "（先在左侧选中一个任务）";
            var output = string.IsNullOrWhiteSpace(t.OutputPath)
                ? FfmpegService.DefaultOutputPath(t.SourcePath, "_cut", ".mp4")
                : t.OutputPath;
            return FfmpegService.DescribeCutCommand(t, FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath), output);
        }
    }

    public bool IsNotBusy => !IsBusy && !IsRunning;
    public bool HasSelection => Selected != null;
    public bool HasItems => CutTasks.Count > 0;
    public bool HasFrames => Frames.Count > 0;

    /// <summary>是否有任务正在跑（控制「停止」按钮）。</summary>
    [ObservableProperty] private bool _isRunning;

    /// <summary>任务清单（来自单例 <see cref="VideoTaskStore"/>，切页不丢）。</summary>
    public ObservableCollection<CutTask> CutTasks => _store?.CutTasks ?? new ObservableCollection<CutTask>();

    // ==================== 编辑缓冲（详情卡） ====================

    [ObservableProperty] private string _editStart = "00:00:00.000";
    [ObservableProperty] private string _editEnd = "";
    [ObservableProperty] private string _editOutput = "";
    [ObservableProperty] private bool _editReEncode;
    [ObservableProperty] private int _editCrf = 20;

    /// <summary>当前预览 / 编辑的源视频（未加入任务时也能先预览）。</summary>
    [ObservableProperty] private string _sourcePath = "";

    /// <summary>源视频时长（秒）；0 = 未探测。</summary>
    [ObservableProperty] private double _duration;

    public string DurationDisplay => Duration > 0 ? CutTask.FormatTime(Duration) : "—";

    /// <summary>预览一次抽多少帧。</summary>
    public int FrameCount { get; set; } = 12;

    // ==================== 列表（继承自基类：列显隐 / 排序 / 详情开合） ====================

    [ObservableProperty] private string _nameHeader = "文件 ▲";
    [ObservableProperty] private string _rangeHeader = "区间";
    [ObservableProperty] private string _lengthHeader = "时长";
    [ObservableProperty] private string _statusHeader = "状态";

    public bool IsNameVisible => Vis("Name");
    public bool IsRangeVisible => Vis("Range");
    public bool IsLengthVisible => Vis("Length");
    public bool IsStatusVisible => Vis("Status");
    public bool IsProgressVisible => Vis("Progress");
    public bool IsOutputVisible => Vis("Output");

    protected override string PageKey => "video-cut";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "文件", "3*"),
        new("Range", "区间", "2.2*"),
        new("Length", "时长", "1*"),
        new("Status", "状态", "1.3*"),
        new("Progress", "进度", "1.2*", sortable: false),
        new("Output", "输出", "3*"),
        new("Detail", "", "36", sortable: false),
    };

    public VideoCutViewModel() { }

    public VideoCutViewModel(VideoTaskStore store, IAppConfigService cfg, IColumnLayoutService cols)
        : base(cols)
    {
        _store = store;
        _cfg = cfg;
        LoadLayout();
        RefreshView();

        // 「视频列表」页多选「➡ 发送到剪切」的文件：进来时自动变成任务
        var pending = store.TakePendingCut();
        if (pending.Count > 0)
        {
            foreach (var p in pending) AddTaskFor(p);
            StatusText = $"已从「视频列表」收到 {pending.Count} 个文件，先选中一条抽帧定区间，再点「▶▶ 批量运行」。";
        }
    }

    protected override void RefreshView()
    {
        IEnumerable<CutTask> q = CutTasks;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || t.OutputPath.Contains(text, StringComparison.CurrentCultureIgnoreCase));

        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Range" => SortDescending ? q.OrderByDescending(t => t.Start) : q.OrderBy(t => t.Start),
            "Length" => SortDescending ? q.OrderByDescending(t => t.Length) : q.OrderBy(t => t.Length),
            "Status" => SortDescending ? q.OrderByDescending(t => t.Status, byText) : q.OrderBy(t => t.Status, byText),
            _ => SortDescending ? q.OrderByDescending(t => t.Name, byText) : q.OrderBy(t => t.Name, byText)
        };

        // 用 Items 承载排序 / 过滤后的结果（CutTasks 是单例清单，不能就地重排）
        Items.Clear();
        foreach (var t in q) Items.Add(t);

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        SummaryText = CutTasks.Count == 0
            ? "还没有任务"
            : $"共 {CutTasks.Count} 个任务 · 已完成 {CutTasks.Count(t => t.Status == VideoTaskStatus.Done)}"
              + (CutTasks.Count(t => t.Status == VideoTaskStatus.Failed) > 0
                  ? $" · 失败 {CutTasks.Count(t => t.Status == VideoTaskStatus.Failed)}" : "");
    }

    /// <summary>列表真正绑定的集合（排序 / 过滤后的视图）。</summary>
    public ObservableCollection<CutTask> Items { get; } = new();

    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Name" => ((CutTask)row).Name,
        "Range" => ((CutTask)row).RangeDisplay,
        "Status" => ((CutTask)row).Status,
        "Output" => ((CutTask)row).OutputPath,
        _ => null
    };

    protected override double? FieldNumber(object row, string columnKey) => columnKey switch
    {
        "Length" => ((CutTask)row).Length,
        "Range" => ((CutTask)row).Start,
        _ => null
    };

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    private void UpdateHeaders()
    {
        NameHeader = "文件" + SortArrow("Name");
        RangeHeader = "区间" + SortArrow("Range");
        LengthHeader = "时长" + SortArrow("Length");
        StatusHeader = "状态" + SortArrow("Status");
    }

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnSelectedChanged(CutTask? value)
    {
        if (value != null)
        {
            SourcePath = value.SourcePath;
            Duration = value.Duration;
            EditStart = CutTask.FormatTime(value.Start);
            EditEnd = value.End > 0 ? CutTask.FormatTime(value.End) : "";
            EditOutput = value.OutputPath;
            EditReEncode = value.ReEncode;
            EditCrf = value.Crf;
            EditPresetId = string.IsNullOrWhiteSpace(value.PresetId) ? CutPresets.Copy : value.PresetId;
            EditExtraArgs = value.ExtraArgs ?? "";
            PreviewTime = value.Start;   // 时间轴从开始点起
            OnPropertyChanged(nameof(CommandPreview));
            OnPropertyChanged(nameof(StartSeconds));   // 区间滑块是手动属性，选中项变了要手动通知
            OnPropertyChanged(nameof(EndSeconds));

            // 换成另一条任务 → 清掉上一批缩略图（那是另一个视频的帧，留着会误导）
            if (!string.Equals(_previewedTaskId, value.Id, StringComparison.Ordinal))
            {
                Frames.Clear();
                _previewedTaskId = value.Id;
            }
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasFrames));
        OnPropertyChanged(nameof(DurationDisplay));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>编辑缓冲 / 选中项变了 → 命令行预览要跟着刷新。</summary>
    partial void OnEditPresetIdChanged(string value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnEditExtraArgsChanged(string value) => OnPropertyChanged(nameof(CommandPreview));

    /// <summary>时间轴被拖动 → 抽这一帧（拖动中会丢帧，只认最后一次位置）。</summary>
    partial void OnPreviewTimeChanged(double value) => _ = UpdatePreviewAsync(value);

    /// <summary>
    /// 抽 <paramref name="time"/> 这一帧当预览大图。
    /// <para>拖动滑块会连发很多次，ffmpeg 每次要几十~几百毫秒 —— 这里做**最新值优先**：
    /// 正在抽就把新位置记为 pending，抽完再补最后一次，中间的丢弃。</para>
    /// </summary>
    private async Task UpdatePreviewAsync(double time)
    {
        var src = SourcePath;
        if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) return;

        if (_extracting) { _pendingPreview = time; return; }

        _extracting = true;
        try
        {
            do
            {
                _pendingPreview = -1;
                var dir = Path.Combine(Path.GetTempPath(), "NewToys", "preview");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"p{_previewIndex:00}.jpg");
                _previewIndex = (_previewIndex + 1) % 4;

                var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
                if (await FfmpegService.ExtractFrameAsync(ffmpeg, src, time, file) && File.Exists(file))
                {
                    try
                    {
                        var bmp = new Bitmap(file);
                        PreviewImage = bmp;
                    }
                    catch { /* 图坏了就保留上一帧 */ }
                }
                time = _pendingPreview;
            } while (time >= 0);
        }
        finally { _extracting = false; }
    }

    /// <summary>⏪ / ⏩：按秒步进（delta 可为负）。</summary>
    [RelayCommand]
    private void Step(double delta)
    {
        var t = PreviewTime + delta;
        if (t < 0) t = 0;
        if (Duration > 0 && t > Duration) t = Duration;
        PreviewTime = t;
    }

    /// <summary>⏮：跳到开始点。</summary>
    [RelayCommand]
    private void JumpStart() => PreviewTime = Selected?.Start ?? 0;

    /// <summary>⏭：跳到结束点（没设就到末尾）。</summary>
    [RelayCommand]
    private void JumpEnd()
        => PreviewTime = (Selected != null && Selected.End > 0) ? Selected.End : (Duration > 0 ? Duration : 0);

    /// <summary>把当前预览位置设为**开始时间**。</summary>
    [RelayCommand]
    private void SetStartHere()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个任务。"; return; }
        Selected.Start = PreviewTime;
        EditStart = CutTask.FormatTime(PreviewTime);
        if (Selected.End > 0 && Selected.End <= Selected.Start) { Selected.End = 0; EditEnd = ""; }
        OnPropertyChanged(nameof(StartSeconds));
        OnPropertyChanged(nameof(EndSeconds));
        MarkFrames();
        StatusText = $"开始时间 = {EditStart}";
    }

    /// <summary>把当前预览位置设为**结束时间**。</summary>
    [RelayCommand]
    private void SetEndHere()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个任务。"; return; }
        Selected.End = PreviewTime;
        EditEnd = CutTask.FormatTime(PreviewTime);
        OnPropertyChanged(nameof(StartSeconds));
        OnPropertyChanged(nameof(EndSeconds));
        MarkFrames();
        StatusText = $"结束时间 = {EditEnd}";
    }

    /// <summary>📋 复制将要执行的 ffmpeg 命令行。</summary>
    [RelayCommand]
    private async Task CopyCommandLineAsync()
    {
        var text = CommandPreview;
        if (text.StartsWith("（", StringComparison.Ordinal)) { StatusText = text; return; }
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制 ffmpeg 命令行。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    // ==================== 加任务 ====================

    /// <summary>为一个源文件建一条剪切任务（默认整段，等用户抽帧后定区间）。</summary>
    public void AddTaskFor(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (_store == null) return;

        var task = new CutTask
        {
            SourcePath = path,
            Start = 0,
            End = 0,
            PresetId = CutPresets.Copy,
            OutputPath = FfmpegService.DefaultOutputPath(path, "_cut", ".mp4")
        };
        _store.AddCut(task);
        RefreshView();
        Selected = task;
        StatusText = $"已加入任务：{task.Name}（默认整段，抽帧后点缩略图定区间）";
    }

    /// <summary>➕ 加入任务：把当前预览的源视频（或选中的文件）建成任务。</summary>
    [RelayCommand]
    private void Add()
    {
        var path = SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "先点「📂 选择视频」挑一个文件。";
            return;
        }
        AddTaskFor(path);
    }

    /// <summary>💾 保存：把详情卡里的区间 / 输出 / 编码选项写回任务并落盘。</summary>
    [RelayCommand]
    private void Save()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个任务。"; return; }

        var start = CutTask.ParseTime(EditStart);
        if (start == null) { StatusText = "开始时间格式不对（用 00:01:23.456 或 83.5 这样的秒数）。"; return; }

        double? end = null;
        if (!string.IsNullOrWhiteSpace(EditEnd))
        {
            end = CutTask.ParseTime(EditEnd);
            if (end == null) { StatusText = "结束时间格式不对（用 00:01:23.456 或 83.5；留空 = 到末尾）。"; return; }
        }

        Selected.Start = start.Value;
        Selected.End = end ?? 0;
        Selected.OutputPath = (EditOutput ?? "").Trim();
        Selected.ReEncode = EditReEncode;
        Selected.Crf = EditCrf;
        Selected.PresetId = string.IsNullOrWhiteSpace(EditPresetId) ? CutPresets.Copy : EditPresetId;
        Selected.ExtraArgs = (EditExtraArgs ?? "").Trim();
        if (Selected.Status != VideoTaskStatus.Running) Selected.Reset();   // 改了参数 → 回到等待（下次会按新参数跑）
        OnPropertyChanged(nameof(CommandPreview));

        _store?.Save();
        RefreshView();
        StatusText = $"已保存「{Selected.Name}」：{Selected.RangeDisplay}（{Selected.LengthDisplay}）";
    }

    /// <summary>🗑 删除选中任务。</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected == null || _store == null) return;
        var name = Selected.Name;
        _store.RemoveCut(Selected);
        Selected = null;
        RefreshView();
        StatusText = "已删除任务：" + name;
    }

    /// <summary>🧹 清掉已完成 / 失败的任务（等待中的保留）。</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        if (_store == null) return;
        var n = _store.ClearFinishedCut();
        Selected = null;
        RefreshView();
        StatusText = n == 0 ? "没有已结束的任务可清。" : $"已清掉 {n} 个已结束的任务。";
    }

    // ==================== 预览（关键帧） ====================

    /// <summary>
    /// 🎞 抽帧预览：先列关键帧时刻，再逐个抽成 jpg 加载进来。
    /// </summary>
    [RelayCommand]
    private async Task LoadFramesAsync()
    {
        var path = SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "先选一个视频文件。";
            return;
        }
        if (IsBusy) return;

        var ffmpeg = FfmpegService.ResolveFfmpeg(_cfg?.FfmpegPath);
        var ffprobe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);

        IsBusy = true;
        Frames.Clear();
        OnPropertyChanged(nameof(HasFrames));
        try
        {
            StatusText = "正在读取关键帧……";

            // ① 先探测时长（没有就跑一次 ffprobe）
            if (Duration <= 0)
            {
                var (ok, _, info) = await VideoScanner.ProbeAsync(path, ffprobe);
                if (ok && info?.Duration != null)
                {
                    Duration = info.Duration.Value;
                    if (Selected != null) Selected.Duration = Duration;
                }
            }
            OnPropertyChanged(nameof(DurationDisplay));

            // ② 关键帧时刻；列不出来就按总时长等分（照样能预览，只是慢一点）
            var times = await FfmpegService.ReadKeyFrameTimesAsync(ffprobe, path, FrameCount);
            if (times.Count == 0 && Duration > 0)
            {
                for (var i = 0; i < FrameCount; i++)
                    times.Add(Duration * i / FrameCount);
            }
            if (times.Count == 0)
            {
                StatusText = "读不到关键帧，也探不出时长 —— 检查 ffmpeg / ffprobe 是否可用（「设置 → 路径配置」）。";
                return;
            }

            // ③ 逐帧抽图
            _frameDir = Path.Combine(Path.GetTempPath(), "NewToys", "frames",
                                     Selected?.Id ?? Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_frameDir);

            for (var i = 0; i < times.Count; i++)
            {
                var file = Path.Combine(_frameDir, $"f{i:00}.jpg");
                var ok = await FfmpegService.ExtractFrameAsync(ffmpeg, path, times[i], file);
                if (!ok || !File.Exists(file)) continue;

                Bitmap? bmp = null;
                try { bmp = new Bitmap(file); } catch { continue; }

                Frames.Add(new FrameThumb { Time = times[i], Image = bmp });
                StatusText = $"正在抽帧… {Frames.Count}/{times.Count}";
            }

            OnPropertyChanged(nameof(HasFrames));
            MarkFrames();

            // 抽完关键帧顺手把第一帧（或当前开始点）填进大预览区，省得用户再点一次
            if (Frames.Count > 0)
            {
                PreviewTime = Selected?.Start > 0 ? Selected!.Start : Frames[0].Time;
            }

            StatusText = Frames.Count == 0
                ? "一帧都没抽出来 —— 可能是 ffmpeg 不可用或文件损坏。"
                : $"已抽出 {Frames.Count} 个关键帧：拖时间轴看画面，「设为起点 / 终点」定区间";
        }
        catch (Exception ex)
        {
            StatusText = "抽帧失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>点缩略图 = 把这一帧的时刻设为<b>开始</b>。</summary>
    [RelayCommand]
    private void SetStart(FrameThumb? frame)
    {
        if (frame == null) return;
        EditStart = frame.TimeText;
        if (Selected != null)
        {
            Selected.Start = frame.Time;
            if (Selected.End > 0 && Selected.End <= Selected.Start) Selected.End = 0;
            EditEnd = Selected.End > 0 ? CutTask.FormatTime(Selected.End) : "";
        }
        MarkFrames();
        StatusText = $"开始 = {frame.TimeText}";
    }

    /// <summary>点缩略图下的「止」= 把这一帧的时刻设为<b>结束</b>。</summary>
    [RelayCommand]
    private void SetEnd(FrameThumb? frame)
    {
        if (frame == null) return;
        EditEnd = frame.TimeText;
        if (Selected != null) Selected.End = frame.Time;
        MarkFrames();
        StatusText = $"结束 = {frame.TimeText}";
    }

    /// <summary>给缩略图标出当前起止点。</summary>
    private void MarkFrames()
    {
        if (Selected == null) return;
        foreach (var f in Frames)
        {
            f.IsStart = Math.Abs(f.Time - Selected.Start) < 0.001;
            f.IsEnd = Selected.End > 0 && Math.Abs(f.Time - Selected.End) < 0.001;
        }
    }

    /// <summary>把当前编辑缓冲里的区间挪到"整段"（起点 0、终点到末尾）。</summary>
    [RelayCommand]
    private void ResetRange()
    {
        EditStart = "00:00:00.000";
        EditEnd = "";
        if (Selected != null) { Selected.Start = 0; Selected.End = 0; }
        MarkFrames();
        StatusText = "区间已重置为整段。";
    }

    // ==================== 运行 ====================

    /// <summary>▶ 只跑当前选中的这一条。</summary>
    [RelayCommand]
    private async Task RunSelectedAsync()
        => await RunTasksAsync(Selected != null ? new List<CutTask> { Selected } : new List<CutTask>());

    /// <summary>▶▶ 批量运行：按顺序跑全部「还没成功」的任务。</summary>
    [RelayCommand]
    private async Task RunAllAsync()
        => await RunTasksAsync(CutTasks.Where(t => t.Status != VideoTaskStatus.Done).ToList());

    private async Task RunTasksAsync(List<CutTask> targets)
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

                StatusText = $"正在剪切（{i + 1}/{targets.Count}）：{t.Name}";
                t.Status = VideoTaskStatus.Running;
                t.Progress = 0;
                t.Message = "";

                var output = string.IsNullOrWhiteSpace(t.OutputPath)
                    ? FfmpegService.DefaultOutputPath(t.SourcePath, "_cut", ".mp4")
                    : t.OutputPath;
                t.OutputPath = output;

                // 时长未知时先探一下（进度条要它）
                if (t.Duration <= 0)
                {
                    var (pOk, _, info) = await VideoScanner.ProbeAsync(t.SourcePath, VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath));
                    if (pOk && info?.Duration != null) { t.Duration = info.Duration.Value; Duration = t.Duration; }
                }

                var progress = new Progress<int>(p => t.Progress = p);
                var (ok, message, size) = await FfmpegService.RunAsync(
                    ffmpeg, FfmpegService.BuildCutArgs(t, output), output,
                    t.End > t.Start ? t.End - t.Start : (t.Duration > 0 ? t.Duration - t.Start : 0),
                    progress, _runCts.Token);

                t.Status = ok ? VideoTaskStatus.Done : (message == "已取消。" ? VideoTaskStatus.Canceled : VideoTaskStatus.Failed);
                t.Message = message;
                t.OutputSize = size;
                if (ok) okCount++;

                _store.Save();
            }

            StatusText = _runCts.IsCancellationRequested
                ? $"已停止：完成 {okCount}/{targets.Count}"
                : $"批量剪切完成：{okCount}/{targets.Count} 成功";
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

    /// <summary>⏹ 停止：取消当前这一条，剩下的不再继续。</summary>
    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning) { StatusText = "当前没有在跑的任务。"; return; }
        _runCts?.Cancel();
        StatusText = "正在停止……（当前这条会被中止，输出文件已删除）";
    }

    /// <summary>📂 打开输出文件所在目录。</summary>
    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (Selected == null) return;
        var file = Selected.OutputPath;
        var dir = File.Exists(file) ? Path.GetDirectoryName(file) : Path.GetDirectoryName(Selected.SourcePath);
        try
        {
            if (string.IsNullOrEmpty(dir)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }
}
