using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 进程内的跨模块消息通道。模块之间禁互引，需要把一个东西（路径 / 页面 Key / 选中项）交给另一个模块时，
/// 一律经这里投递，⛔ 不走「模块 A 引模块 B」或「Shell 当业务中转」。
/// <para>
/// 投递与接收**不要求同时存在**，两条路径都覆盖才不会丢消息：
/// 目标页尚未创建 ⇒ 消息进信箱，页面创建时取走；目标页已经打开 ⇒ 订阅者当场收到回调。
/// </para>
/// </summary>
public interface IModuleBus
{
    /// <summary>投递：有订阅者则立刻派发（不入信箱），无订阅者则入信箱等接收方来取。</summary>
    void Post(string topic, object? payload);

    /// <summary>取走信箱里最早一条（取走即移除）；没有则返回 <c>null</c>。</summary>
    object? Take(string topic);

    /// <summary>取走信箱里全部消息并清空（按投递顺序）。</summary>
    IReadOnlyList<object?> TakeAll(string topic);

    /// <summary>信箱里还有几条没被取走。</summary>
    int PendingCount(string topic);

    /// <summary>
    /// 订阅某个主题。<paramref name="owner"/> 按**弱引用**持有 ⇒ 页面 VM 被丢弃后订阅自动失效，
    /// 调用方不必注销（⛔ 也不会因为它活着而泄漏整页）。
    /// <para>🔴 <paramref name="handler"/> 必须是 <paramref name="owner"/> 的**实例方法**，⛔ 别传捕获变量的 lambda
    /// —— 弱引用绑的是 owner，闭包委托绑不回去，派发时会被当作已失效丢弃。</para>
    /// </summary>
    IDisposable Subscribe(string topic, object owner, Action<object?> handler);
}

/// <summary>跨模块消息的主题常量。命名 = 「域.动作」，便于将来按 <c>域.</c> 前缀批量管理。</summary>
public static class ModuleTopics
{
    /// <summary>
    /// 媒体资产 → 视频工具（`video-list`）：待导入的视频文件。
    /// <para>载荷类型 = <c>IReadOnlyList&lt;(string Path, string MetaJson)&gt;</c> —— 绝对路径（已按视频扩展名筛过）
    /// + 资产库那边的 <c>metadata_json</c>，对方直接套用、不再重跑 ffprobe；
    /// <c>MetaJson</c> 为空表示"没读过媒体信息"。</para>
    /// </summary>
    public const string VideoImport = "video.import";
}
