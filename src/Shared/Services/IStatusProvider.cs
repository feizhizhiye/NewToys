using System;

namespace NewToys.Shared.Services;

/// <summary>「底部状态栏」契约：页面 VM 实现它，Shell 的底部状态栏统一显示 StatusText。（WHY: SH-055）</summary>
public interface IStatusProvider
{
    /// <summary>当前状态文本（没内容时返回空串，⛔ 不返回 null）。</summary>
    string StatusText { get; }

    /// <summary>状态文本变化时通知 Shell 刷新底栏。</summary>
    event EventHandler? StatusChanged;
}
