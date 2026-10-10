using System.Net.Http;

namespace NewToys.Shared.Services
{
    /// <summary>
    /// **加密 DNS（DNS over HTTPS）**：只作用于**本软件自己**发起的网络请求（书签检测、网址爬取等），
    /// 不改系统 DNS、不影响浏览器。未启用时走系统解析。
    ///
    /// 实现用 DoH 的 **JSON 接口**（`Accept: application/dns-json`），所以「自定义」端点必须是支持 JSON 的 DoH 服务。
    /// </summary>
    public interface IEncryptedDnsService
    {
        /// <summary>当前是否启用（来自配置）。</summary>
        bool IsEnabled { get; }

        /// <summary>当前生效的 DoH 端点。</summary>
        string Endpoint { get; }

        /// <summary>解析一个主机名（走 DoH；未启用时走系统解析）。返回 A/AAAA 地址。</summary>
        Task<(bool Ok, string Message, string[] Addresses)> ResolveAsync(string host, CancellationToken ct);

        /// <summary>
        /// 造一个 HTTP 客户端：启用加密 DNS 时，它建立连接前的域名解析走 DoH；未启用时就是普通客户端。
        /// </summary>
        HttpClient CreateClient(int timeoutSeconds = 15, string? userAgent = null);

        /// <summary>清掉本服务自己的 DoH 解析缓存（「清除 DNS 缓存」会一起调）。</summary>
        void ClearCache();

        /// <summary>当前配置的中文描述（界面上显示）。</summary>
        string Describe();
    }

    /// <summary>内置的 DoH 服务商（默认国内的 DNSPod / 阿里）。</summary>
    public static class DohProviders
    {
        public sealed record Provider(string Id, string Name, string Endpoint);

        public static IReadOnlyList<Provider> All { get; } = new List<Provider>
        {
            new("dnspod", "腾讯 DNSPod（doh.pub）", "https://doh.pub/dns-query"),
            new("aliyun", "阿里云公共 DNS（dns.alidns.com）", "https://dns.alidns.com/resolve"),
            new("cloudflare", "Cloudflare（cloudflare-dns.com）", "https://cloudflare-dns.com/dns-query"),
            new("google", "Google（dns.google）", "https://dns.google/resolve"),
            new("custom", "自定义（下面填 URL）", "")
        };

        public static Provider Find(string? id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}