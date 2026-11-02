using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace NewToys.Reading.Services;

/// <summary>
/// 书封下载 + 内存缓存：同一地址只下一次，失败也缓存（<c>null</c>），避免反复重试。
/// <para>返回的 <see cref="Bitmap"/> 由缓存持有，调用方 ⛔ 不要 Dispose。</para>
/// </summary>
public sealed class CoverLoader : IDisposable
{
    /// <summary>缓存条目上限，超了就整清（书封不是关键数据，重下即可）。</summary>
    private const int MaxEntries = 120;

    /// <summary>单张最大 6MB，超过直接放弃（书源里混进大图/视频文件时保护内存）。</summary>
    private const long MaxBytes = 6L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Dictionary<string, Task<Bitmap?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public CoverLoader(ReadingSettingsService settings)
    {
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        if (settings is not null)
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", settings.UserAgent);
    }

    /// <summary>取一张书封；地址无效 / 下载失败 / 非图片都返回 <c>null</c>（界面转占位块）。</summary>
    public Task<Bitmap?> LoadAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<Bitmap?>(null);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return Task.FromResult<Bitmap?>(null);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return Task.FromResult<Bitmap?>(null);

        lock (_sync)
        {
            if (_cache.TryGetValue(url, out var hit)) return hit;
            if (_cache.Count >= MaxEntries) _cache.Clear();
            var task = DownloadAsync(uri);
            _cache[url] = task;
            return task;
        }
    }

    private async Task<Bitmap?> DownloadAsync(Uri uri)
    {
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes) return null;
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0) return null;
            buffer.Position = 0;
            return new Bitmap(buffer);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_sync) _cache.Clear();
        _http.Dispose();
    }
}
