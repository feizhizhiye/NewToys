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

/// <summary>
/// 「工具条的**承载者**是谁」契约（v3.66 / NT-062 细节调整）：**复合页**（一页多节，如应用级设置页）
/// 用它把工具条交给**某一节自己的 VM**。
/// <para>
/// 🔴 为什么必须有这一层：<see cref="ToolbarItemDescriptor"/> 里的 <c>CommandPath</c> / <c>IsEnabledPath</c>
/// 等路径是**相对节 VM** 解析的（<c>ToolbarHost</c> 用 <c>Binding { Source = 传入对象 }</c> 与
/// <c>src.GetType().GetProperty(path)</c>）；复合页若自己当承载者，<c>SaveCommand</c> 这类路径全落空
/// ⇒ 工具条"画得出来、点了没反应"。
/// </para>
/// <para>
/// Shell 规则：当前页 VM 实现了本接口 ⇒ 用 <see cref="ToolbarOwner"/> 当工具条承载者与绑定根；
/// 否则沿用当前页 VM 自己（普通页零改动）。
/// </para>
/// </summary>
public interface IToolbarOwner
{
    /// <summary>工具条的承载者（= 描述符里各路径的解析根）；<c>null</c> = 本页没有工具条。</summary>
    object? ToolbarOwner { get; }
}
