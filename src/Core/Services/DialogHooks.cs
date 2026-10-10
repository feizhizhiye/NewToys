using System;
using System.Threading.Tasks;

namespace NewToys.Core.Services;

/// <summary>
/// 宿主（Shell）注入的**通用对话框钩子**：Core 与各模块都不引用 Avalonia / Shell，
/// 需要"问用户一句"时统一走这里。
/// <para>
/// 与 <c>PackageManagerService.ConfirmHandler</c> 是同一个套路，区别只是本钩子是**公共的**：
/// 任何模块（如视频工具的"重命名 / 删除文件"、"清空缓存"）都能直接用，不必各自再定义一个静态属性。
/// </para>
/// <para>
/// ⛔ **钩子没接上时必须按"用户没同意"处理**（<see cref="ConfirmAsync"/> 返回 false、
/// <see cref="InputAsync"/> 返回 null）：宁可什么都不做，也不能在没有确认的情况下动用户的东西。
/// </para>
/// </summary>
public static class DialogHooks
{
    /// <summary>二次确认钩子（宿主注入）：返回 true = 用户点了「继续」。</summary>
    public static Func<string, Task<bool>>? ConfirmHandler { get; set; }

    /// <summary>文本输入钩子（宿主注入）：(标题, 初始值) → 用户输入；返回 null = 用户取消。</summary>
    public static Func<string, string, Task<string?>>? InputHandler { get; set; }

    /// <summary>弹一个确认框。没接钩子时返回 <c>false</c>（= 当作用户没同意）。</summary>
    public static async Task<bool> ConfirmAsync(string message)
        => ConfirmHandler != null && await ConfirmHandler(message).ConfigureAwait(false);

    /// <summary>弹一个单行输入框。没接钩子时返回 <c>null</c>（= 当作用户取消）。</summary>
    public static async Task<string?> InputAsync(string title, string initialValue)
        => InputHandler == null ? null : await InputHandler(title, initialValue).ConfigureAwait(false);
}
