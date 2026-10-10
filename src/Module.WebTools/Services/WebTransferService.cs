namespace NewToys.WebTools.Services;

/// <summary>
/// 网络工具模块内部的「跨页传物」：右键「发送到书签 / 网页抓取」时，把当前 URL 交给目标页，
/// 目标页进入时消费一次。模块内部单例，不依赖 Shell、不引入模块间引用。
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
}
