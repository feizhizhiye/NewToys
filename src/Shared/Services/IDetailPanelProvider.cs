using System;

namespace NewToys.Shared.Services;

/// <summary>
/// 「右侧详情面板」契约（v3.1 新增）：页面 VM <b>可选实现</b>，由 Shell 的右详情列统一消费。
/// <para>
/// 列表页把原先**自绘的 340px 右列内容**通过 <see cref="DetailContent"/> 交给 Shell 承载
/// （Shell 在标签切换时探测当前 VM 是否实现本接口）。是否展开由 Shell 统一落
/// <c>config\columnlayout.json</c>（<c>FlagDetailPanel</c>，按页面 Key 分别记忆）。
/// </para>
/// </summary>
public interface IDetailPanelProvider
{
    /// <summary>该页是否有详情面板（false 时 Shell 不显示右列）。</summary>
    bool HasDetailPanel { get; }

    /// <summary>当前是否展开（Shell 据此决定右列可见性，并持久化）。</summary>
    bool IsDetailOpen { get; }

    /// <summary>详情内容（Shell 用 ContentControl 承载；列表页通常返回详情视图或选中项）。</summary>
    object? DetailContent { get; }

    /// <summary>详情开合 / 内容变化时通知 Shell 刷新。</summary>
    event EventHandler? DetailChanged;

    /// <summary>表头 <c>▤</c> 按钮调这个：开合切换。</summary>
    void ToggleDetail();

    /// <summary>右键菜单首项「▤ 显示详情栏」调这个：展开并通知。</summary>
    void OpenDetail();
}
