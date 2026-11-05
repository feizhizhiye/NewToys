using System;

namespace NewToys.SystemTools.Services;

/// <summary>命令条目的提权运行。（WHY: ST-011）</summary>
public interface ISystemCommandLauncher
{
    /// <summary>
    /// 以管理员身份开<b>可见窗口</b>运行（弹 UAC；用户取消 ⇒ <c>Ok=false</c> 且不执行）。
    /// 非 Windows 平台不支持（UAC 是 Windows 概念）⇒ 返回 false + 人话。
    /// </summary>
    (bool Ok, string Message) RunElevated(Models.SystemCommand item);
}
