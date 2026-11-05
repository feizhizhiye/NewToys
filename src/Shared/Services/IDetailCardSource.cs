using NewToys.Shared.Details;

namespace NewToys.Shared.Services;

/// <summary>「详情卡声明式来源」契约（需求 §4.5.1）：页面 VM 可选实现，与 IDetailPanelProvider 并列（一个页面通常两个都实现）。（WHY: SH-035）</summary>
public interface IDetailCardSource
{
    /// <summary>本页的详情卡描述（null = 本页不走声明式）。</summary>
    DetailCardDescriptor? Card { get; }
}
