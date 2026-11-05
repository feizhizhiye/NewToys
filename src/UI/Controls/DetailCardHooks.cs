using System;
using System.Threading.Tasks;

namespace NewToys.UI.Controls;

/// <summary>详情卡动作的「确认弹窗」钩子：。（WHY: UI-003）</summary>
public static class DetailCardHooks
{
    /// <summary>宿主注入的确认弹窗（message → 用户是否确认）。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    /// <summary>弹确认框；钩子未注入时按「取消」处理。</summary>
    public static Task<bool> ConfirmAsync(string message)
        => ConfirmHandler is null ? Task.FromResult(false) : ConfirmHandler(message);
}
