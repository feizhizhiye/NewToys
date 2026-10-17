using System;

namespace NewToys.Shared.Services;

/// <summary>
/// 「状态栏可点击跳转」契约（v3.65 / NT-062，需求 §4.9.2 第 5 条）：页面 VM **可选实现**。
/// <para>
/// 与 <see cref="IStatusProvider"/> 同一套路 —— Shell 在切换标签时探测；实现了且
/// <see cref="StatusActionKey"/> 非空 ⇒ 底部状态栏那一行**可点击**，点击 = 打开该 Key 对应的页面 / 节
/// （典型 = 外部程序缺失时 `⚠ 未找到 ffmpeg` → 一点跳到「运行环境」节）。
/// </para>
/// <para>
/// ⚠️ 只在前缀是 `⚠`（= 警示态）时才应该给 <see cref="StatusActionKey"/>：
/// 普通成功文案不给跳转，避免状态栏到处可点、语义变糊。
/// </para>
/// </summary>
public interface IStatusActionProvider
{
    /// <summary>点击状态栏要打开的菜单 Key（null / 空 = 不可点）。</summary>
    string? StatusActionKey { get; }

    /// <summary>点击提示（tooltip）。</summary>
    string StatusActionTip { get; }
}
