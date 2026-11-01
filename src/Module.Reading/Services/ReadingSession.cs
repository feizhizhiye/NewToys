namespace NewToys.Reading.Services;

/// <summary>要从别的页面带进「网上搜索」的一本书（书架 / 发现页点「继续阅读」时用）。</summary>
public sealed record PendingBook(string SourceUrl, string BookUrl, string Name);

/// <summary>
/// 阅读模块的会话内传参（页面之间不引用彼此，靠它带一次性的待处理目标）。
/// </summary>
public sealed class ReadingSession
{
    /// <summary>下次打开搜索页时要预选的书源地址（取走即清空）。</summary>
    public string? PendingSourceUrl { get; set; }

    /// <summary>下次打开搜索页时要直接打开的书（取走即清空）。</summary>
    public PendingBook? PendingBook { get; set; }

    /// <summary>取走待搜索源并清空（⛔ 避免下次进页还带着旧源）。</summary>
    public string? TakePendingSourceUrl()
    {
        var value = PendingSourceUrl;
        PendingSourceUrl = null;
        return value;
    }

    /// <summary>取走待打开的书并清空。</summary>
    public PendingBook? TakePendingBook()
    {
        var value = PendingBook;
        PendingBook = null;
        return value;
    }
}
