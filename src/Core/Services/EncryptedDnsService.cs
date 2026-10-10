using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services
{
    /// <summary>
    /// 加密 DNS（DoH）实现：用 **JSON 接口**（`?name=x&amp;type=A` + `Accept: application/dns-json`）查 A/AAAA，
    /// 并把它接到 <see cref="SocketsHttpHandler.ConnectCallback"/> 上——于是本软件发出的 HTTP 请求在**建连之前**
    /// 的域名解析走的是加密 DNS（系统 DNS、浏览器都不受影响）。
    ///
    /// 几个关键点：
    /// - 解析 DoH 端点**自身**的域名必须用系统 DNS（bootstrap），否则递归。
    /// - `ConnectCallback` 跑在**线程池线程**上，里面只能做纯后台的事（这里只查 DNS + 连 socket），
    ///   并且一律 `ConfigureAwait(false)`；绝不在这里碰界面。
    /// - 带 TTL 的缓存（上限 5 分钟），`ClearCache()` 供「清除 DNS 缓存」调用。
    /// </summary>
    public class EncryptedDnsService : IEncryptedDnsService
    {
        private const int MaxCacheSeconds = 300;

        private readonly IAppConfigService _config;
        private readonly ConcurrentDictionary<string, (string[] Addresses, DateTime Expires)> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HttpClient _bootstrap;

        public EncryptedDnsService(IAppConfigService config)
        {
            _config = config;

            // bootstrap 客户端：只用来访问 DoH 端点本身（用系统 DNS 解析端点域名），永远不接 DoH 回调
            _bootstrap = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(6) })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _bootstrap.DefaultRequestHeaders.UserAgent.ParseAdd("NewToys/1.0 (+doh)");
        }

        public bool IsEnabled => _config.EncryptedDnsEnabled;

        public string Endpoint
        {
            get
            {
                var provider = DohProviders.Find(_config.EncryptedDnsProviderId);
                var endpoint = provider.Id == "custom" ? _config.EncryptedDnsCustomUrl : provider.Endpoint;
                return (endpoint ?? "").Trim();
            }
        }

        public string Describe()
        {
            if (!IsEnabled) return "未启用（用系统 DNS 解析）";
            if (Endpoint.Length == 0) return "已启用，但没填 DoH 端点（会退化成系统 DNS）";

            var provider = DohProviders.Find(_config.EncryptedDnsProviderId);
            return provider.Id == "custom" ? $"已启用：自定义 {Endpoint}" : $"已启用：{provider.Name}";
        }

        // ==================== 解析 ====================

        public async Task<(bool Ok, string Message, string[] Addresses)> ResolveAsync(string host, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(host)) return (false, "主机名为空", Array.Empty<string>());

            if (!IsEnabled || Endpoint.Length == 0)
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(host.Trim(), ct).ConfigureAwait(false);
                    return (true, $"系统 DNS 解析 {host} 成功（{addresses.Length} 个地址）",
                        addresses.Select(a => a.ToString()).ToArray());
                }
                catch (Exception ex)
                {
                    return (false, "系统 DNS 解析失败：" + ex.Message, Array.Empty<string>());
                }
            }

            try
            {
                var addresses = await QueryAsync(host.Trim(), ct).ConfigureAwait(false);
                return addresses.Length == 0
                    ? (false, $"DoH 没有返回 {host} 的地址", Array.Empty<string>())
                    : (true, $"加密 DNS 解析 {host} 成功（{addresses.Length} 个地址）", addresses);
            }
            catch (Exception ex)
            {
                return (false, "加密 DNS 解析失败：" + ex.Message, Array.Empty<string>());
            }
        }

        /// <summary>查 A + AAAA（JSON 接口）。</summary>
        private async Task<string[]> QueryAsync(string host, CancellationToken ct)
        {
            var results = new List<string>();

            foreach (var type in new[] { "A", "AAAA" })
            {
                var url = $"{Endpoint}{(Endpoint.Contains('?') ? '&' : '?')}name={Uri.EscapeDataString(host)}&type={type}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");

                using var response = await _bootstrap.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (type == "A") throw new HttpRequestException($"DoH 端点返回 {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                results.AddRange(ParseAnswers(json, type == "A" ? 1 : 28));
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>解析 <c>{ "Answer":[{"type":1,"data":"1.2.3.4","TTL":300}] }</c>。</summary>
        public static List<string> ParseAnswers(string json, int wantedType, out int ttlSeconds)
        {
            var values = new List<string>();
            ttlSeconds = MaxCacheSeconds;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("Status", out var status) && status.TryGetInt32(out var statusCode) && statusCode != 0)
            {
                throw new InvalidOperationException($"DoH 返回状态码 {statusCode}");
            }

            if (!root.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array) return values;

            foreach (var answer in answers.EnumerateArray())
            {
                if (!answer.TryGetProperty("type", out var typeElement) || !typeElement.TryGetInt32(out var type) || type != wantedType) continue;
                if (!answer.TryGetProperty("data", out var dataElement)) continue;

                var data = dataElement.GetString();
                if (!string.IsNullOrWhiteSpace(data)) values.Add(data.Trim());

                if (answer.TryGetProperty("TTL", out var ttlElement) && ttlElement.TryGetInt32(out var ttl) && ttl > 0)
                {
                    ttlSeconds = Math.Min(ttl, MaxCacheSeconds);
                }
            }

            return values;
        }

        private static List<string> ParseAnswers(string json, int wantedType) => ParseAnswers(json, wantedType, out _);

        private async Task<string[]> ResolveCachedAsync(string host, CancellationToken ct)
        {
            if (_cache.TryGetValue(host, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Addresses;

            var addresses = await QueryAsync(host, ct).ConfigureAwait(false);
            if (addresses.Length > 0)
            {
                _cache[host] = (addresses, DateTime.UtcNow.AddSeconds(MaxCacheSeconds));
            }

            return addresses;
        }

        public void ClearCache() => _cache.Clear();

        // ==================== HTTP 客户端 ====================

        public HttpClient CreateClient(int timeoutSeconds = 15, string? userAgent = null)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(3, timeoutSeconds));

            HttpClient client;
            if (!IsEnabled || Endpoint.Length == 0)
            {
                client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, ConnectTimeout = TimeSpan.FromSeconds(6) })
                {
                    Timeout = timeout
                };
            }
            else
            {
                var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 5,
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    ConnectCallback = async (context, ct) =>
                    {
                        var host = context.DnsEndPoint.Host;

                        // IPv4/IPv6 字面量直接连，不用解析
                        if (IPAddress.TryParse(host, out var literal))
                        {
                            return await ConnectAsync(literal, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                        }

                        var addresses = await ResolveCachedAsync(host, ct).ConfigureAwait(false);
                        if (addresses.Length == 0) throw new HttpRequestException($"加密 DNS 未能解析 {host}");

                        Exception? last = null;
                        foreach (var text in addresses)
                        {
                            if (!IPAddress.TryParse(text, out var address)) continue;

                            try
                            {
                                return await ConnectAsync(address, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                last = ex;
                            }
                        }

                        throw last ?? new HttpRequestException($"加密 DNS 解析到 {host} 的地址都无法连接");
                    }
                };

                client = new HttpClient(handler) { Timeout = timeout };
            }

            if (!string.IsNullOrWhiteSpace(userAgent)) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        private static async Task<Stream> ConnectAsync(IPAddress address, int port, CancellationToken ct)
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}