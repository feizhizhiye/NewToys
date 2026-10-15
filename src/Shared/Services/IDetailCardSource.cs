using NewToys.Shared.Details;

namespace NewToys.Shared.Services;

/// <summary>
/// 「详情卡声明式来源」契约（NT-054，需求 §4.5.1）：页面 VM **可选实现**，与
/// <see cref="IDetailPanelProvider"/> **并列**（一个页面通常两个都实现）。
/// <para>
/// <see cref="IDetailPanelProvider"/> 管「有没有右列 / 开合状态 / 通知刷新」，
/// 本接口管「右列里那张卡**长什么样**」：
/// <list type="bullet">
///   <item><see cref="Card"/> 非 null → Shell 用 <c>DetailCardHost</c> **统一渲染**，
///         ⛔ 不再取 <see cref="IDetailPanelProvider.DetailContent"/>（那条老路保留给尚未迁移的页）；</item>
///   <item><see cref="Card"/> 为 null → 退回老的 <c>DetailContent</c> 自绘视图。</item>
/// </list>
/// </para>
/// <para>
/// 🔴 描述符走 **C# 对象字面量**（⛔ 不做运行时 JSON / XAML 加载）；绑定路径是字符串，
/// 必须跑 <c>skill/scripts/check-detail-paths.ps1</c> 做静态兜底。
/// </para>
/// </summary>
public interface IDetailCardSource
{
    /// <summary>本页的详情卡描述（null = 本页不走声明式）。</summary>
    DetailCardDescriptor? Card { get; }
}
