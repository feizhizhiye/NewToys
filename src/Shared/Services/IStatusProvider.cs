using System;

namespace NewToys.Shared.Services;

/// <summary>
/// 「底部状态栏」契约（v3.9）：页面 VM 实现它，**Shell 的底部状态栏**统一显示 <see cref="StatusText"/>。
/// <para>
/// 与 <c>IDetailPanelProvider</c> 同一套路：Shell 在切换标签时探测当前 VM 是否实现本接口，
/// 实现了就订阅 <see cref="StatusChanged"/> 并显示它的状态文本；没实现则底栏为空。
/// </para>
/// <para>
/// ⚠️ 列表页由 <c>ListPageViewModelBase</c> **统一实现**（一次覆盖全部列表页）；
/// 非列表页（设置页 / 视频页等）后续各自接入。
/// </para>
/// </summary>
public interface IStatusProvider
{
    /// <summary>当前状态文本（没内容时返回空串，⛔ 不返回 null）。</summary>
    string StatusText { get; }

    /// <summary>状态文本变化时通知 Shell 刷新底栏。</summary>
    event EventHandler? StatusChanged;
}
