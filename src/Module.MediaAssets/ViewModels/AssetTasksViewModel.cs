using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「任务中心」页（NT-019）：三块 —— ① 运行态任务队列（可取消）② 最近扫描任务（可重扫）③ 操作日志。
/// <para>
/// ⚠️ 队列**不持久化**（进程退出即清，需求 §6.6）：长期记录在 `scan_jobs` / `operation_logs`，就是这页的后两块。
/// </para>
/// <para>🔴 <see cref="TaskQueue.Changed"/> 是**后台线程**抛的 → 必须 `Dispatcher.UIThread.Post` 切回 UI 再重建列表。</para>
/// </summary>
public partial class AssetTasksViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    // ==================== 声明式工具条（NT-055 三轮 / 需求 §4.8.5） ====================
    // ⚠️ 本页 VM 不是 ListPageViewModelBase ⇒ 契约成员自己实现（与 VideoPreviewViewModel 同一套路）。

    /// <summary>工具条构成变化时通知 Shell（本页工具条**静态** ⇒ 不主动触发；留契约给"项随状态增删"的将来）。</summary>
    public event EventHandler? ToolbarChanged;

    /// <summary>工具条构成变了 → 通知 Shell（⛔ 只有动态构造 <see cref="Toolbar"/> 时才需要调）。</summary>
    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 工具条：页内 ⛔ 不再自写 <c>Border.toolbar</c>，改由 Shell 渲染本描述符。
    /// <para>本页 <c>HasDetailPanel=false</c>（没有详情卡）⇒ Shell ⛔ 不显示详情开关。</para>
    /// </summary>
    public IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "✖ 取消选中任务",
            CommandPath = nameof(CancelTaskCommand),
            CommandArgPath = nameof(SelectedTask),
            ToolTip = "取消选中的那件任务（排队中的直接取消；正在跑的会请求取消）"
        },
        new()
        {
            Text = "⛔ 全部取消",
            CommandPath = nameof(CancelAllTasksCommand),
            ToolTip = "取消所有**排队中**的任务"
        },
        new()
        {
            Text = "🧹 清理已结束",
            CommandPath = nameof(ClearFinishedCommand),
            ToolTip = "清掉已完成 / 失败 / 已取消的历史项（⛔ 不动排队中与在跑的）"
        },
        new()
        {
            Text = "🔄 重读任务",
            CommandPath = nameof(RefreshTasksCommand),
            ToolTip = "重新读取任务队列 / 扫描记录 / 操作日志（⛔ 不扫盘）"
        },
    };

    private readonly TaskQueue _queue = null!;
    private readonly AssetService _assets = null!;
    private readonly ScanService _scan = null!;
    private readonly OperationLogService _opLog = null!;

    /// <summary>队列快照（最近的在最前）。</summary>
    public ObservableCollection<TaskItem> Tasks { get; } = new();

    /// <summary>最近的扫描任务（`scan_jobs`）。</summary>
    public ObservableCollection<ScanJob> ScanJobs { get; } = new();

    /// <summary>最近的操作日志（`operation_logs`，含物理操作的 pending → success/failed）。</summary>
    public ObservableCollection<OperationLog> Logs { get; } = new();

    [ObservableProperty] private string _statusText = "就绪";

    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    [ObservableProperty] private TaskItem? _selectedTask;
    [ObservableProperty] private ScanJob? _selectedScanJob;

    public bool HasTasks => Tasks.Count > 0;
    public bool HasScanJobs => ScanJobs.Count > 0;
    public bool HasLogs => Logs.Count > 0;
    public bool IsDbAvailable => _assets?.IsAvailable ?? false;

    public AssetTasksViewModel() { }

    public AssetTasksViewModel(TaskQueue queue, AssetService assets, ScanService scan, OperationLogService opLog)
    {
        _queue = queue;
        _assets = assets;
        _scan = scan;
        _opLog = opLog;

        Reload();
        StatusText = Summary();

        // 队列任何变化（入队 / 起跑 / 完成 / 取消）都刷新一次；⚠️ 回调来自后台线程
        _queue.Changed += (_, _) =>
        {
            try { Dispatcher.UIThread.Post(ReloadTasks); }
            catch { /* 设计期 / 已关闭 */ }
        };
    }

    private string Summary()
        => $"队列 {Tasks.Count} 项（{(Tasks.Any(t => t.Status == TaskStatuses.Running) ? "有任务在跑" : "空闲")}）"
           + $"｜扫描记录 {ScanJobs.Count} 条｜操作日志 {Logs.Count} 条";

    /// <summary>重读三块（工具条「🔄 重读任务」）。</summary>
    [RelayCommand]
    private void RefreshTasks()
    {
        Reload();
        StatusText = "已重读任务队列 / 扫描记录 / 操作日志。";
    }

    private void Reload()
    {
        ReloadTasks();
        ReloadScanJobs();
        ReloadLogs();
    }

    private void ReloadTasks()
    {
        Tasks.Clear();
        foreach (var t in _queue.Items) Tasks.Add(t);
        OnPropertyChanged(nameof(HasTasks));
    }

    private void ReloadScanJobs()
    {
        ScanJobs.Clear();
        if (IsDbAvailable)
            foreach (var j in _assets.ListRecentScanJobs(50)) ScanJobs.Add(j);
        OnPropertyChanged(nameof(HasScanJobs));
    }

    private void ReloadLogs()
    {
        Logs.Clear();
        if (IsDbAvailable)
            foreach (var l in _opLog.ListRecent(100)) Logs.Add(l);
        OnPropertyChanged(nameof(HasLogs));
    }

    // ==================== 队列操作 ====================

    /// <summary>取消一件任务（排队中的直接标取消；正在跑的才发取消信号 —— 见 <c>TaskQueue.Cancel</c>）。</summary>
    [RelayCommand]
    private void CancelTask(TaskItem? item)
    {
        if (item == null) { StatusText = "先选中一件任务。"; return; }
        _queue.Cancel(item.Key);
        ReloadTasks();
        StatusText = $"已请求取消「{item.Title}」。";
    }

    /// <summary>取消全部排队中的任务。</summary>
    [RelayCommand]
    private void CancelAllTasks()
    {
        _queue.CancelAll();
        ReloadTasks();
        StatusText = "已取消所有排队中的任务。";
    }

    /// <summary>清掉已完成 / 已取消的历史项（⛔ 不动排队中与在跑的）。</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        _queue.ClearFinished();
        ReloadTasks();
        StatusText = "已清理结束的任务。";
    }

    // ==================== 重试（只对扫描任务有意义） ====================

    /// <summary>
    /// 重试一次扫描任务 = 对同一个存储源**重扫一遍**（物理操作走队列）。
    /// <para>⚠️ 物理操作（rename / move / copy / delete）**不做通用重试** —— 它们是"一次用户动作"，
    /// 失败原因（占用 / 权限 / 磁盘满）要先解决再重做，凭空重试只会再失败一次。</para>
    /// </summary>
    [RelayCommand]
    private void RetryScan(ScanJob? job)
    {
        if (job == null) { StatusText = "先选中一条扫描记录。"; return; }

        var storageId = job.StorageId;
        var title = storageId.HasValue ? $"重扫存储源 #{storageId}" : "重扫全部在线存储源";
        _queue.Enqueue(title, async (ct, p) =>
        {
            var result = await _scan.ScanAsync(storageId, p, ct).ConfigureAwait(false);
            try { Dispatcher.UIThread.Post(Reload); } catch { /* 已关闭 */ }
            p.Report(result.Status == ScanStatuses.Cancelled
                ? "已取消"
                : $"完成：新增 {result.AddedFiles} / 更新 {result.UpdatedFiles} / 丢失 {result.MissingFiles} / 失败 {result.ErrorCount}");
        });

        ReloadTasks();
        StatusText = $"已把「{title}」排进任务队列。";
    }
}
