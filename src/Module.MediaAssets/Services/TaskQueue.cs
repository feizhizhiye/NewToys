using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产的**串行任务队列**（需求 §6.6 任务中心）：一次只跑一件（磁盘 IO 任务并行只会互相拖慢），
/// 支持取消 / 进度文本 / 失败隔离（一件失败不影响后续），供「任务中心」页展示、扫描 / 物理操作入队。
/// <para>
/// ⛔ 不做持久化：任务队列是**运行态**，进程退出即清（长期记录在 <c>scan_jobs</c> / <c>operation_logs</c>）。
/// </para>
/// </summary>
public class TaskQueue
{
    private readonly List<TaskItem> _items = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;

    /// <summary>当前正在跑的任务 Key（Cancel 靠它判断"该不该发取消信号"）；volatile —— 跨线程读。</summary>
    private volatile string? _currentKey;

    /// <summary>队列内容或状态变化（页面订阅后刷新列表；⛔ 回调在后台线程，UI 侧自行 Dispatcher 切回）。</summary>
    public event EventHandler? Changed;

    /// <summary>当前队列（最近的在最前；返回的是快照副本，UI 不持有内部表）。</summary>
    public IReadOnlyList<TaskItem> Items
    {
        get { lock (_items) return _items.OrderByDescending(i => i.CreatedAt).ToList(); }
    }

    /// <summary>是否还有待跑 / 在跑的任务。</summary>
    public bool IsBusy
    {
        get { lock (_items) return _items.Any(i => i.Status == TaskStatuses.Pending || i.Status == TaskStatuses.Running); }
    }

    /// <summary>
    /// 入队一件工作：<paramref name="work"/> 收到取消令牌与进度报告器（报文本即可，进度是给人看的）。
    /// 返回任务 Id（取消 / 查询用）。
    /// </summary>
    public string Enqueue(string title, Func<CancellationToken, IProgress<string>, Task> work)
    {
        var item = new TaskItem
        {
            Key = Guid.NewGuid().ToString("N"),
            Title = title,
            Status = TaskStatuses.Pending,
            ProgressText = "排队中…",
            CreatedAt = DateTime.Now
        };
        lock (_items) _items.Add(item);
        Raise();
        _ = PumpAsync(item, work);
        return item.Key;
    }

    /// <summary>
    /// 取消指定任务：排队中的直接标取消；**只有它正好在跑**才发取消信号
    /// （⛔ 别因为取消一件排队任务而误杀正在跑的另一件）。
    /// </summary>
    public void Cancel(string key)
    {
        var cancelRunning = false;
        lock (_items)
        {
            var it = _items.FirstOrDefault(i => i.Key == key);
            if (it == null) return;
            if (it.Status == TaskStatuses.Pending)
            {
                it.Status = TaskStatuses.Cancelled;
                it.ProgressText = "已取消（排队中）";
                it.FinishedAt = DateTime.Now;
            }
            cancelRunning = it.Status == TaskStatuses.Running && _currentKey == key;
        }
        if (cancelRunning) _cts?.Cancel();
        Raise();
    }

    /// <summary>取消全部（进程退出 / 用户点「全部取消」）。</summary>
    public void CancelAll()
    {
        lock (_items)
            foreach (var it in _items.Where(i => i.Status == TaskStatuses.Pending))
            {
                it.Status = TaskStatuses.Cancelled;
                it.ProgressText = "已取消（排队中）";
                it.FinishedAt = DateTime.Now;
            }
        _cts?.Cancel();
        Raise();
    }

    /// <summary>清掉已完成 / 已取消的历史项（⛔ 不动排队中与在跑的）。</summary>
    public void ClearFinished()
    {
        lock (_items) _items.RemoveAll(i => i.Status is TaskStatuses.Success or TaskStatuses.Failed
                                                       or TaskStatuses.Cancelled);
        Raise();
    }

    private async Task PumpAsync(TaskItem item, Func<CancellationToken, IProgress<string>, Task> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (item.Status == TaskStatuses.Cancelled) return;   // 排队期间被取消

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _currentKey = item.Key;
            item.Status = TaskStatuses.Running;
            item.StartedAt = DateTime.Now;
            item.ProgressText = "开始…";
            Raise();

            var progress = new Progress<string>(text =>
            {
                item.ProgressText = text;
                Raise();
            });

            try
            {
                await work(ct, progress).ConfigureAwait(false);
                item.Status = ct.IsCancellationRequested ? TaskStatuses.Cancelled : TaskStatuses.Success;
                if (item.Status == TaskStatuses.Success) item.ProgressText = "已完成";
            }
            catch (OperationCanceledException)
            {
                item.Status = TaskStatuses.Cancelled;
                item.ProgressText = "已取消";
            }
            catch (Exception ex)
            {
                // 失败隔离：一件失败不拖累后续任务
                item.Status = TaskStatuses.Failed;
                item.Error = ex.Message;
                item.ProgressText = "失败：" + ex.Message;
            }
            finally
            {
                item.FinishedAt = DateTime.Now;
                _cts?.Dispose();
                _cts = null;
                _currentKey = null;
            }
        }
        finally
        {
            _gate.Release();
            Raise();
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 队列里的一项（任务中心页一行）。
/// <para>
/// 继承 <c>ObservableObject</c>：状态 / 进度是**边跑边变**的，用普通属性则列表行不会刷新
/// （页面靠 <see cref="TaskQueue.Changed"/> 只能整表重建，滚动位置与选中都会丢）。
/// </para>
/// </summary>
public partial class TaskItem : ObservableObject
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>取 <see cref="TaskStatuses"/>。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _status = TaskStatuses.Pending;

    /// <summary>给人看的进度文本（如「已扫描 1234 / 5000」）。</summary>
    [ObservableProperty] private string _progressText = "";

    [ObservableProperty] private string? _error;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string StatusText => Status switch
    {
        TaskStatuses.Pending => "排队中",
        TaskStatuses.Running => "进行中",
        TaskStatuses.Success => "已完成",
        TaskStatuses.Failed => "失败",
        TaskStatuses.Cancelled => "已取消",
        _ => Status
    };
}

/// <summary>队列任务状态（与 <see cref="OperationStatuses"/> 同词表，便于任务中心混排显示）。</summary>
public static class TaskStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
