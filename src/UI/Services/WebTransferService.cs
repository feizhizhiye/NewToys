namespace NewToys.UI.Services;

/// <summary>
/// 模块内部的「跨页传物」：右键「发送到书签 / 发送到抓取」时，把当前 URL 交给目标页，
/// 目标页进入时消费一次。
/// <para>
/// 🔴 NT-089：抓取三页从网络工具迁入脚本中心 ⇒「书签 / Ping → 抓取」变成**跨模块**传参；
/// 而模块禁互引、Shared/Core/Shell ⛔ 不动 ⇒ 落 `NewToys.UI`（两个模块都引它），
/// 两侧各注册一次单例（同一容器 ⇒ 解析到同一实例，行为与搬家前一致）。
/// </para>
/// </summary>
public sealed class WebTransferService
{
    private string? _pendingUrl;

    /// <summary>投递一个待处理的 URL（如想「用抓取页抓这个链接」）。</summary>
    public void PushUrl(string? url) => _pendingUrl = string.IsNullOrWhiteSpace(url) ? null : url;

    /// <summary>取走待处理 URL（取走即清空，保证每页只消费一次）。</summary>
    public string? TakeUrl()
    {
        var u = _pendingUrl;
        _pendingUrl = null;
        return u;
    }

    /// <summary>当前是否还有待处理的 URL。</summary>
    public bool HasPending => !string.IsNullOrWhiteSpace(_pendingUrl);

    // ==================== 规则目录 → 规则编辑器（NT-084 批 C） ====================

    private string? _pendingRuleSet;

    /// <summary>投递「要打开哪条规则集」（目录页点「✏ 编辑规则」时用）。</summary>
    public void PushRuleSet(string? name) => _pendingRuleSet = string.IsNullOrWhiteSpace(name) ? null : name;

    /// <summary>取走待打开的规则集名（取走即清空）。</summary>
    public string? TakeRuleSet()
    {
        var name = _pendingRuleSet;
        _pendingRuleSet = null;
        return name;
    }
}
