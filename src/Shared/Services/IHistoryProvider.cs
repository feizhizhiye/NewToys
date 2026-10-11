using System;

namespace NewToys.Shared.Services;

/// <summary>
/// 「标签页历史栈」契约（v3.1 新增）：页面 VM <b>可选实现</b>。
/// <para>
/// ⚠️ <b>本轮（v3.1）只定义契约、无页面实现</b> → 标题栏前进/后退按钮<b>一律灰显</b>。
/// 下一版由 <c>video-list</c>（目录切换）等页面实现后自然可用。
/// </para>
/// <para>页面通过 <see cref="StatePushed"/> 通知 Shell 把当前 <see cref="CaptureState"/> 压入后退栈。</para>
/// </summary>
public interface IHistoryProvider
{
    /// <summary>当前页面内的导航状态快照（返回 null 表示这次不进历史栈）。</summary>
    object? CaptureState();

    /// <summary>恢复到一个历史快照。</summary>
    void RestoreState(object state);

    /// <summary>通知 Shell：当前状态发生了变化、应压入历史栈。</summary>
    event EventHandler? StatePushed;
}
