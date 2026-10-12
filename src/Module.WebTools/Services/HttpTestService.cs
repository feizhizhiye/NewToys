using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewToys.WebTools.Services;

/// <summary>一次 HTTP 测试的结果。</summary>
/// <param name="Ok">请求是否成功走完（false = 抛异常，<paramref name="Error"/> 有原因）。</param>
/// <param name="StatusCode">HTTP 状态码（失败时为 0）。</param>
/// <param name="ReasonPhrase">状态短语（如 OK / Not Found）。</param>
/// <param name="ElapsedMs">耗时（毫秒）。</param>
/// <param name="ResponseHeaders">响应头（每行一个 Key: Value）。</param>
/// <param name="ResponseBody">响应体文本。</param>
/// <param name="Error">失败原因（成功为 null）。</param>
public sealed record HttpTestResult(
    bool Ok, int StatusCode, string ReasonPhrase, long ElapsedMs,
    string ResponseHeaders, string ResponseBody, string? Error);

/// <summary>
/// HTTP 测试（v3.19 / NT-001）：用 <see cref="System.Net.Http.HttpClient"/>（栈锁定，⛔ 不引第三方 HTTP 库）发一次请求，
/// 返回状态码 / 耗时 / 响应头 / 响应体 / 可读错误。异常一律捕获成 <see cref="HttpTestResult.Error"/>（不抛给 UI）。
/// </summary>
public class HttpTestService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<HttpTestResult> SendAsync(string method, string url, string headersText, string bodyText, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var m = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(m), url);

            // 请求体（GET / HEAD 不带体）
            if (!string.IsNullOrEmpty(bodyText) && m is not ("GET" or "HEAD"))
                req.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

            // 请求头：一行一个 "Key: Value"；先试请求头，放不进去（内容头）再试内容头
            foreach (var raw in (headersText ?? "").Split('\n'))
            {
                var line = raw.Trim();
                var i = line.IndexOf(':');
                if (i <= 0) continue;
                var k = line[..i].Trim();
                var v = line[(i + 1)..].Trim();
                if (k.Length == 0) continue;
                if (!req.Headers.TryAddWithoutValidation(k, v))
                    req.Content?.Headers.TryAddWithoutValidation(k, v);
            }

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            sw.Stop();

            var headers = string.Join("\n",
                resp.Headers.Concat(resp.Content.Headers).Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));

            return new HttpTestResult(true, (int)resp.StatusCode, resp.ReasonPhrase ?? "",
                                      sw.ElapsedMilliseconds, headers, body, null);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new HttpTestResult(false, 0, "", sw.ElapsedMilliseconds, "", "", "请求超时（30 秒）或被取消。");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HttpTestResult(false, 0, "", sw.ElapsedMilliseconds, "", "", ex.Message);
        }
    }
}
