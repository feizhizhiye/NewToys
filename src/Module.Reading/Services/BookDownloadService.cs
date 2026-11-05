using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewToys.Reading.Models;

namespace NewToys.Reading.Services;

/// <summary>全书下载的结果：合并后的整本正文 + 失败章节说明。</summary>
public sealed class BookDownloadResult
{
    public BookDownloadResult(string text, int total, int downloaded, IReadOnlyList<string> failures)
    {
        Text = text;
        Total = total;
        Downloaded = downloaded;
        Failures = failures;
    }

    /// <summary>整本合并文本（含书名 / 作者 / 章节标题）。</summary>
    public string Text { get; }

    public int Total { get; }

    public int Downloaded { get; }

    /// <summary>失败章节（「章节名 · 原因」），上限由调用方决定怎么展示。</summary>
    public IReadOnlyList<string> Failures { get; }

    public bool IsComplete => Failures.Count == 0 && Downloaded == Total;
}

/// <summary>全书下载 + 合并成一本（legado 的「缓存全书」+ 导出）： 逐章抓正文（命中正文缓存的不再请求）→ 按「书名 / 作者 / 每章标题 + 正文」拼成一整份文本。（WHY: RD-005）</summary>
public sealed class BookDownloadService
{
    private readonly CrawlService _crawl;

    public BookDownloadService(CrawlService crawl) => _crawl = crawl;

    /// <param name="progress">(已完成章节数, 总章节数, 当前章节名)。</param>
    public async Task<BookDownloadResult> DownloadAsync(
        BookSource source, string bookName, string author, IReadOnlyList<Chapter> chapters,
        IProgress<(int Done, int Total, string Chapter)>? progress, CancellationToken ct)
    {
        var body = new StringBuilder();
        var failures = new List<string>();
        var done = 0;

        for (var i = 0; i < chapters.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var chapter = chapters[i];
            progress?.Report((done, chapters.Count, chapter.Name));

            var result = await _crawl.ContentAsync(source, chapter.Url, ct).ConfigureAwait(false);
            if (result.Error is not null)
            {
                failures.Add(chapter.Name + " · " + result.Error.Display);
                done++;
                continue;
            }

            body.AppendLine(chapter.Name);
            body.AppendLine();
            body.AppendLine((result.Value ?? "").Trim());
            body.AppendLine();
            body.AppendLine("-----");
            body.AppendLine();
            done++;
        }

        progress?.Report((done, chapters.Count, "合并中"));

        var text = new StringBuilder()
            .AppendLine(bookName)
            .AppendLine(string.IsNullOrWhiteSpace(author) ? "" : "作者：" + author)
            .AppendLine()
            .Append(body)
            .ToString();

        return new BookDownloadResult(text, chapters.Count, done - failures.Count, failures);
    }

    /// <summary>合并文本的默认文件名（去掉文件名非法字符）。</summary>
    public static string SuggestFileName(string bookName)
    {
        var name = (bookName ?? "").Trim();
        if (name.Length == 0) name = "book";

        foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(bad, '_');

        return name + ".txt";
    }

    /// <summary>前 <paramref name="count"/> 条失败原因拼成一段（给状态栏 / 错误条）。</summary>
    public static string DescribeFailures(IReadOnlyList<string> failures, int count = 4)
        => string.Join("\n", failures.Take(count));
}
