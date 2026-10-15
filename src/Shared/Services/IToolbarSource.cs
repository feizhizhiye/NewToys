namespace NewToys.Shared.Services;

using System;
using System.Collections.Generic;
using NewToys.Shared.Models;

/// <summary>
/// 「页面工具条」契约（v3.53 / NT-055）：页面 VM **可选**实现它，
/// **Shell 的 <c>ToolbarHost</c>** 统一渲染工具条（含按容器宽度自动折叠 + 详情开关浮层）。
/// <para>
/// 🔴 与 <c>IStatusProvider</c>（底部状态栏）、<c>IDetailCardSource</c>（右详情卡）**同一套路**：
/// Shell 在切换标签时探测当前页 VM 是否实现本接口 —— 实现了就由 Shell 渲染，
/// **没有实现就是"本页没有工具条"**（Shell ⛔ 不留空行；页面继续用自己的页内工具条）。
/// </para>
/// <para>
/// 🔴 **页面⛔ 不得再自写 <c>Border.toolbar</c>**：两处同时渲染会出现两条工具条（§4.8.5 逐页 opt-in）。
/// </para>
/// </summary>
public interface IToolbarSource
{
    /// <summary>
    /// 工具条内容（**按优先级顺序**：越靠前越不会被折进「⋯」）。
    /// 返回 <c>null</c> 或空集合 = 本页无工具条。
    /// </summary>
    IReadOnlyList<ToolbarItemDescriptor>? Toolbar { get; }

    /// <summary>工具条构成变化（增删项 / 换页）时通知 Shell 重建。</summary>
    event EventHandler? ToolbarChanged;
}
