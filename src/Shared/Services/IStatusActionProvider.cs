using System;

namespace NewToys.Shared.Services;

/// <summary>「状态栏可点击跳转」契约（，需求 §4.9.2 第 5 条）：页面 VM 可选实现。（WHY: SH-054）</summary>
public interface IStatusActionProvider
{
    /// <summary>点击状态栏要打开的菜单 Key（null / 空 = 不可点）。</summary>
    string? StatusActionKey { get; }

    /// <summary>点击提示（tooltip）。</summary>
    string StatusActionTip { get; }
}
