namespace NewToys.Shared.Services;

using System;
using System.Collections.Generic;
using NewToys.Shared.Models;

/// <summary>「页面工具条」契约：页面 VM 可选实现它， Shell 的 ToolbarHost 统一渲染工具条（含按容器宽度自动折叠 + 详情开关浮层）。（WHY: SH-058）</summary>
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

/// <summary>「工具条的承载者是谁」契约（：细节调整）：复合页（一页多节，如应用级设置页） 用它把工具条交给某一节自己的 VM。（WHY: SH-059）</summary>
public interface IToolbarOwner
{
    /// <summary>工具条的承载者（= 描述符里各路径的解析根）；<c>null</c> = 本页没有工具条。</summary>
    object? ToolbarOwner { get; }
}
