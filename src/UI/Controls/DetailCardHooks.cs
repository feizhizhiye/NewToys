using System;
using System.Threading.Tasks;

namespace NewToys.UI.Controls;

/// <summary>
/// 详情卡动作的「确认弹窗」钩子（NT-063）：
/// <para>
/// 🔴 为什么需要它：详情卡渲染层随控件库搬进 <c>NewToys.UI</c> 后 **⛔ 不引 Core / Shell**，
/// 而带 <c>ConfirmText</c> 的动作按钮原来直接调 <c>Core.Services.DialogHooks.ConfirmAsync</c>。
/// 解耦办法 = 与 DialogHooks 同套路：UI 侧只声明钩子，宿主（Shell）启动时把
/// <c>DialogHooks.ConfirmHandler</c> 转接进来（见 <c>App.axaml.cs</c>）。
/// </para>
/// <para>
/// ⚠️ 未注入时的兜底 = <c>false</c>（按「用户取消」处理，与 DialogHooks 口径一致）——
/// 宁可危险动作点了没反应，也⛔ 不许未经确认就执行。
/// </para>
/// </summary>
public static class DetailCardHooks
{
    /// <summary>宿主注入的确认弹窗（message → 用户是否确认）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    /// <summary>弹确认框；钩子未注入时按「取消」处理。</summary>
    public static Task<bool> ConfirmAsync(string message)
        => ConfirmHandler is null ? Task.FromResult(false) : ConfirmHandler(message);
}
