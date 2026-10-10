using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// 「视频工具 → 格式转码」页（v1.35 实装）。
/// <para>
/// 用法闭环：**加文件（批量）→ 每条单独调参数 → 「▶▶ 批量运行」→ 看前后对比**。
/// 与剪切页一样，加进来只是建任务，不立刻跑；任务清单由单例 <see cref="VideoTaskStore"/> 持有，
/// 落 <c>config\video-tasks.json</c>，关掉软件再回来还在。
/// </para>
/// <para>
/// <b>前后对比</b>：加任务时用 ffprobe 记下源信息（大小 / 编码 / 分辨率 / 码率 / 时长），
/// 跑完再探测输出文件 —— 详情卡里并排列出来，一眼看出"到底省了多少、画质参数变成什么"。
/// </para>
/// </summary>
public partial class TranscodeViewModel : ListPageViewModelBase
{
    private readonly VideoTaskStore? _store;
    private readonly IAppConfigService? _cfg;
    private readonly VideoMediaIndex? _index;

    private CancellationTokenSource? _runCts;

    [ObservableProperty] private string _statusText = "加一批文件（可多选）→ 每条单独调参数 → 「▶▶ 批量运行」。";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private TranscodeTask? _selected;
    [ObservableProperty] private string _summaryText = "还没有任务";
    [ObservableProperty] private bool _isRunning;

    public bool IsNotBusy => !IsBusy && !IsRunning;
    public bool HasSelection => Selected != null;
    public bool HasItems => TranscodeTasks.Count > 0;

    /// <summary>任务清单（单例，切页不丢）。</summary>
    public ObservableCollection<TranscodeTask> TranscodeTasks => _store?.TranscodeTasks ?? new ObservableCollection<TranscodeTask>();

    /// <summary>排序 / 过滤后的列表（真正绑到 ListBox 的那个）。</summary>
    public ObservableCollection<TranscodeTask> Items { get; } = new();

    /// <summary>详情卡里的「前 → 后」对比表。</summary>
    public ObservableCollection<CompareRow> CompareRows { get; } = new();

    /// <summary>预设下拉框的数据源（不写死在 XAML 里）。</summary>
    public IReadOnlyList<TranscodePreset> Presets => TranscodePreset.All;

    // ==================== 编辑缓冲 ====================

    [ObservableProperty] private string _editPresetId = "h264";
    [ObservableProperty] private int _editCrf;
    [ObservableProperty] private int _editMaxHeight;
    [ObservableProperty] private string _editOutput = "";

    /// <summary>新建任务时用的预设（工具条上选，只影响随后加入的条目）。</summary>
    [ObservableProperty] private string _defaultPresetId = "h264";

    /// <summary>新建任务时的最大高度（0 = 不改分辨率）。</summary>
    [ObservableProperty] private int _defaultMaxHeight;

    // ==================== 列表 ====================

    [ObservableProperty] private string _nameHeader = "文件 ▲";
    [ObservableProperty] private string _presetHeader = "预设";
    [ObservableProperty] private string _beforeHeader = "转码前";
    [ObservableProperty] private string _afterHeader = "转码后";
    [ObservableProperty] private string _statusHeader = "状态";

    public bool IsNameVisible => Vis("Name");
    public bool IsPresetVisible => Vis("Preset");
    public bool IsBeforeVisible => Vis("Before");
    public bool IsAfterVisible => Vis("After");
    public bool IsStatusVisible => Vis("Status");
    public bool IsProgressVisible => Vis("Progress");

    protected override string PageKey => "video-transcode";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "文件", "2.8*"),
        new("Preset", "预设", "2.2*"),
        new("Before", "转码前", "1.8*"),
        new("After", "转码后", "1.8*"),
        new("Status", "状态", "1.2*"),
        new("Progress", "进度", "1.1*", sortable: false),
        new("Detail", "", "36", sortable: false),
    };

    public TranscodeViewModel() { }

    public TranscodeViewModel(VideoTaskStore store, IAppConfigService cfg,
                              IColumnLayoutService cols, VideoMediaIndex index)
        : base(cols)
    {
        _store = store;
        _cfg = cfg;
        _index = index;
        LoadLayout();

        // 「视频列表」页多选「➡ 发送到转码」的文件：进来时自动批量生成任务
        var pending = store.TakePendingTranscode();
        if (pending.Count > 0)
        {
            _ = AddFilesAsync(pending);
            StatusText = $"已从「视频列表」收到 {pending.Count} 个文件，正在生成任务……";
        }

        RefreshView();
    }

    protected override void RefreshView()
    {
        IEnumerable<TranscodeTask> q = TranscodeTasks;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || t.OutputPath.Contains(text, StringComparison.CurrentCultureIgnoreCase));

        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Preset" => SortDescending ? q.OrderByDescending(t => t.PresetName, byText) : q.OrderBy(t => t.PresetName, byText),
            "Before" => SortDescending ? q.OrderByDescending(t => t.SourceSize) : q.OrderBy(t => t.SourceSize),
            "After" => SortDescending ? q.OrderByDescending(t => t.OutputSize) : q.OrderBy(t => t.OutputSize),
            "Status" => SortDescending ? q.OrderByDescending(t => t.Status, byText) : q.OrderBy(t => t.Status, byText),
            _ => SortDescending ? q.OrderByDescending(t => t.Name, byText) : q.OrderBy(t => t.Name, byText)
        };

        Items.Clear();
        foreach (var t in q) Items.Add(t);

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        SummaryText = TranscodeTasks.Count == 0
            ? "还没有任务"
            : $"共 {TranscodeTasks.Count} 个任务 · 已完成 {TranscodeTasks.Count(t => t.Status == VideoTaskStatus.Done)}"
              + (TranscodeTasks.Count(t => t.Status == VideoTaskStatus.Failed) > 0
                  ? $" · 失败 {TranscodeTasks.Count(t => t.Status == VideoTaskStatus.Failed)}" : "");
    }

    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Name" => ((TranscodeTask)row).Name,
        "Preset" => ((TranscodeTask)row).PresetName,
        "Status" => ((TranscodeTask)row).Status,
        "Before" => ((TranscodeTask)row).SourceSizeDisplay,
        "After" => ((TranscodeTask)row).OutputSizeDisplay,
        _ => null
    };

    protected override double? FieldNumber(object row, string columnKey) => columnKey switch
    {
        "Before" => ((TranscodeTask)row).SourceSize,
        "After" => ((TranscodeTask)row).OutputSize,
        _ => null
    };

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    private void UpdateHeaders()
    {
        NameHeader = "文件" + SortArrow("Name");
        PresetHeader = "预设" + SortArrow("Preset");
        BeforeHeader = "转码前" + SortArrow("Before");
        AfterHeader = "转码后" + SortArrow("After");
        StatusHeader = "状态" + SortArrow("Status");
    }

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnSelectedChanged(TranscodeTask? value)
    {
        if (value != null)
        {
            EditPresetId = value.PresetId;
            EditCrf = value.Crf;
            EditMaxHeight = value.MaxHeight;
            EditOutput = value.OutputPath;
        }
        BuildCompareRows();
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>重建「前 → 后」对比表（选中项变了 / 跑完了都要刷）。</summary>
    private void BuildCompareRows()
    {
        CompareRows.Clear();
        var t = Selected;
        if (t == null) return;

        CompareRows.Add(new CompareRow { Label = "大小", Before = t.SourceSizeDisplay, After = t.HasResult ? t.OutputSizeDisplay : "—" });
        CompareRows.Add(new CompareRow { Label = "编码", Before = t.SourceCodec.NullDash(), After = t.HasResult ? t.OutputCodec.NullDash() : "—" });
        CompareRows.Add(new CompareRow { Label = "分辨率", Before = t.SourceResolution.NullDash(), After = t.HasResult ? t.OutputResolution.NullDash() : "—" });
        CompareRows.Add(new CompareRow { Label = "码率", Before = t.SourceBitRate.NullDash(), After = t.HasResult ? t.OutputBitRate.NullDash() : "—" });
        CompareRows.Add(new CompareRow { Label = "时长", Before = t.SourceDurationDisplay, After = t.HasResult ? t.OutputDurationDisplay : "—" });
        CompareRows.Add(new CompareRow { Label = "体积变化", Before = "—", After = t.HasResult ? t.SavingDisplay : "—" });
    }

    // ==================== 批量建任务 ====================

    /// <summary>
    /// 为一批文件建转码任务（先并发探测源信息填「转码前」，界面上立刻能看到对比基线）。
    /// </summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (_store == null) return;

        var files = paths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).Distinct().ToList();
        if (files.Count == 0) { StatusText = "没有可用的视频文件。"; return; }

        IsBusy = true;
        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        var added = 0;
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var path = files[i];
                var preset = TranscodePreset.Find(DefaultPresetId);

                var task = new TranscodeTask
                {
                    SourcePath = path,
                    PresetId = preset.Id,
                    MaxHeight = DefaultMaxHeight,
                    OutputPath = FfmpegService.DefaultOutputPath(path, "_" + preset.Id, preset.Extension),
                    SourceSize = new FileInfo(path).Length
                };

                // 源信息：先吃 SQLite 缓存，没有再跑一次 ffprobe（并发安全）
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
                StatusText = $"正在生成任务… {added}/{files.Count}";
            }

            RefreshView();
            if (Items.Count > 0 && Selected == null) Selected = Items[0];
            StatusText = $"已生成 {added} 个转码任务（预设：{TranscodePreset.Find(DefaultPresetId).Name}）；可逐条改参数，再点「▶▶ 批量运行」。";
        }
        catch (Exception ex)
        {
            StatusText = "生成任务失败：" + ex.Message;
        }
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
        _store.RemoveTranscode(Selected);
        Selected = null;
        RefreshView();
        StatusText = "已删除任务：" + name;
    }

    /// <summary>💾 保存：把详情卡里的参数写回任务（改完自动回到"等待中"，下次按新参数跑）。</summary>
    [RelayCommand]
    private void Save()
    {
        if (Selected == null) { StatusText = "先在左侧选中一个任务。"; return; }

        Selected.PresetId = string.IsNullOrWhiteSpace(EditPresetId) ? "h264" : EditPresetId;
        Selected.Crf = EditCrf;
        Selected.MaxHeight = EditMaxHeight;

        var output = (EditOutput ?? "").Trim();
        var preset = Selected.Preset;
        if (output.Length == 0)
            output = FfmpegService.DefaultOutputPath(Selected.SourcePath, "_" + preset.Id, preset.Extension);
        Selected.OutputPath = output;
        EditOutput = output;

        if (Selected.Status != VideoTaskStatus.Running) Selected.Reset();
        _store?.Save();
        RefreshView();
        BuildCompareRows();
        StatusText = $"已保存「{Selected.Name}」：{preset.Name}"
                     + (Selected.MaxHeight > 0 ? $" · 缩到 {Selected.MaxHeight}p" : "")
                     + (Selected.Crf > 0 ? $" · CRF {Selected.Crf}" : "");
    }

    /// <summary>🧹 清掉已结束的任务。</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        if (_store == null) return;
        var n = _store.ClearFinishedTranscode();
        Selected = null;
        RefreshView();
        StatusText = n == 0 ? "没有已结束的任务可清。" : $"已清掉 {n} 个已结束的任务。";
    }

    // ==================== 运行 ====================

    /// <summary>▶ 只跑选中的一条。</summary>
    [RelayCommand]
    private async Task RunSelectedAsync()
        => await RunTasksAsync(Selected != null ? new List<TranscodeTask> { Selected } : new List<TranscodeTask>());

    /// <summary>▶▶ 批量运行：按顺序跑全部「还没成功」的任务。</summary>
    [RelayCommand]
    private async Task RunAllAsync()
        => await RunTasksAsync(TranscodeTasks.Where(t => t.Status != VideoTaskStatus.Done).ToList());

    private async Task RunTasksAsync(List<TranscodeTask> targets)
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

                StatusText = $"正在转码（{i + 1}/{targets.Count}）：{t.Name}";
                t.Status = VideoTaskStatus.Running;
                t.Progress = 0;
                t.Message = "";

                var progress = new Progress<int>(p => t.Progress = p);
                var (ok, message, size) = await FfmpegService.RunAsync(
                    ffmpeg, FfmpegService.BuildTranscodeArgs(t, t.OutputPath), t.OutputPath,
                    t.SourceDuration, progress, _runCts.Token);

                t.Status = ok ? VideoTaskStatus.Done : (message == "已取消。" ? VideoTaskStatus.Canceled : VideoTaskStatus.Failed);
                t.Message = message;
                t.OutputSize = size;

                // 跑完 → 探测输出文件，把「转码后」那半边填上（对比表这才完整）
                if (ok)
                {
                    okCount++;
                    await FillOutputInfoAsync(t);
                }

                if (ReferenceEquals(t, Selected)) BuildCompareRows();
                _store.Save();
            }

            StatusText = _runCts.IsCancellationRequested
                ? $"已停止：完成 {okCount}/{targets.Count}"
                : $"批量转码完成：{okCount}/{targets.Count} 成功";
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

    /// <summary>探测输出文件，填「转码后」的编码 / 分辨率 / 码率 / 时长。</summary>
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
        catch { /* 探测输出失败不影响"已转码成功"这个结论 */ }
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
