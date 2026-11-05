using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>进程内的跨模块消息通道。模块之间禁互引，需要把一个东西（路径 / 页面 Key / 选中项）交给另一个模块时， 一律经这里投递，⛔ 不走「模块 A 引模块 B」或「Shell 当业务中转」。（WHY: SH-043）</summary>
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

    /// <summary>订阅某个主题。<paramref name="owner"/> 按弱引用持有 ⇒ 页面 VM 被丢弃后订阅自动失效， 调用方不必注销（⛔ 也不会因为它活着而泄漏整页）。（WHY: SH-044）</summary>
    IDisposable Subscribe(string topic, object owner, Action<object?> handler);
}

/// <summary>跨模块消息的主题常量。命名 = 「域.动作」，便于将来按 <c>域.</c> 前缀批量管理。</summary>
public static class ModuleTopics
{
    /// <summary>媒体资产 → 视频工具（video-list）：待导入的视频文件。</summary>
    public const string VideoImport = "video.import";

    /// <summary>
    /// 媒体资产 → 查看器（`viewer-audio`）：直接开播一批音乐。
    /// <para>载荷类型 = <c>IReadOnlyList&lt;string&gt;</c> —— 音频文件绝对路径，数组顺序 = 播放顺序（第一首开始播）。
    /// 由发送方按音乐扩展名筛过，接收方 ⛔ 不再判断类型。</para>
    /// </summary>
    public const string AudioPlay = "viewer.audioplay";

    /// <summary>
    /// 媒体资产 → 查看器（`viewer-image`）：直接看一批图片。
    /// <para>载荷类型 = <c>IReadOnlyList&lt;string&gt;</c> —— 图片绝对路径，数组顺序 = 翻页顺序（第一张先显示）。
    /// 与「同目录翻页」不同：收到这份清单后翻页集**就是这份清单**，⛔ 不再扫目录混进没被选中的图。</para>
    /// </summary>
    public const string ImageOpen = "viewer.imageopen";
}
