using System;

namespace NewToys.Shared.Services;

/// <summary>「标签页历史栈」契约：页面 VM 可选实现。（WHY: SH-040）</summary>
public interface IHistoryProvider
{
    /// <summary>当前页面内的导航状态快照（返回 null 表示这次不进历史栈）。</summary>
    object? CaptureState();

    /// <summary>恢复到一个历史快照。</summary>
    void RestoreState(object state);

    /// <summary>通知 Shell：当前状态发生了变化、应压入历史栈。</summary>
    event EventHandler? StatePushed;
}
