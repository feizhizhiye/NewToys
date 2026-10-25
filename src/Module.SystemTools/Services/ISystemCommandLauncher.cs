using System;

namespace NewToys.SystemTools.Services;

/// <summary>
/// 命令条目的<b>提权运行</b>（补充：2026-10-23 用户要求「勾了需要管理员的也能直接运行」）。
/// <para>
/// 🔴 与 Core <c>ScriptRunner</c> 的分工：那条路 = 隐藏 + 重定向 + 捕获输出（⛔ 提权不在其契约内，
/// 见 <c>IProcessRunner</c> 文档「不在本契约内的两类」）；本路 = <b>可见窗口 + <c>Verb=runas</c></b>，
/// 与 ScriptManager 的 <c>QuickActionService.StartVisible</c> 同范式（定调「winget 源命令走命令库 + UAC」）。
/// </para>
/// </summary>
public interface ISystemCommandLauncher
{
    /// <summary>
    /// 以管理员身份开<b>可见窗口</b>运行（弹 UAC；用户取消 ⇒ <c>Ok=false</c> 且不执行）。
    /// 非 Windows 平台不支持（UAC 是 Windows 概念）⇒ 返回 false + 人话。
    /// </summary>
    (bool Ok, string Message) RunElevated(Models.SystemCommand item);
}
