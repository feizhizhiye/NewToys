using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using NewToys.Core.Services;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>一次 ping 的结果。</summary>
public readonly struct PingResult
{
    public bool Ok { get; init; }
    public string Message { get; init; }
    public string Ip { get; init; }
    public long RoundtripMs { get; init; }
}

/// <summary>
/// Ping 服务：解析（可选**指定 DNS 服务器**）+ ICMP 回显。
/// <para>
/// 「切换 DNS 进行 ping」：若主机是域名，先用 <see cref="DnsResolver"/> 向**指定 DNS 服务器**解析 A 记录，
/// 再对解析出的 IP 发 ICMP；若未指定 DNS 则回退系统解析。主机已是 IP 时直接 ping。
/// </para>
/// </summary>
public class PingService
{
    private readonly ConfigStoreService _cfg;
    private readonly DnsResolver _dns;

    public PingService(ConfigStoreService cfg, DnsResolver dns)
    {
        _cfg = cfg;
        _dns = dns;
    }

    private string FilePath => Path.Combine(_cfg.ConfigDir, "ping-targets.json");

    public IReadOnlyList<PingTarget> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<PingTarget>();
            var list = JsonSerializer.Deserialize<List<PingTarget>>(File.ReadAllText(FilePath));
            return list ?? new List<PingTarget>();
        }
        catch { return new List<PingTarget>(); }
    }

    public void Save(IEnumerable<PingTarget> items)
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不打断 UI */ }
    }

    /// <summary>解析 + ping。dnsServer 为 null / 空 → 使用系统 DNS。</summary>
    public async Task<PingResult> PingAsync(string host, string? dnsServer, int timeoutMs = 2000)
    {
        var bare = StripPort(host);
        if (string.IsNullOrWhiteSpace(bare)) return new PingResult { Ok = false, Message = "空主机", Ip = "" };

        string? ip = null;
        if (IPAddress.TryParse(bare, out _))
        {
            ip = bare;                                  // 已是 IP，无需解析
        }
        else if (!string.IsNullOrWhiteSpace(dnsServer))
        {
            var addrs = await _dns.ResolveAAsync(bare, dnsServer, timeoutMs).ConfigureAwait(false);
            ip = addrs.Count > 0 ? addrs[0].ToString() : null;
        }
        else
        {
            try { ip = (await Dns.GetHostAddressesAsync(bare).ConfigureAwait(false)).FirstOrDefault()?.ToString(); }
            catch { ip = null; }
        }

        if (ip == null) return new PingResult { Ok = false, Message = "解析失败", Ip = "" };

        using var pinger = new Ping();
        try
        {
            var reply = await pinger.SendPingAsync(ip, timeoutMs).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success)
                return new PingResult { Ok = true, Message = $"OK {reply.RoundtripTime}ms", Ip = ip, RoundtripMs = reply.RoundtripTime };
            return new PingResult { Ok = false, Message = reply.Status.ToString(), Ip = ip };
        }
        catch (PingException ex) { return new PingResult { Ok = false, Message = ex.Message, Ip = ip }; }
        catch (Exception ex) { return new PingResult { Ok = false, Message = ex.Message, Ip = ip }; }
    }

    /// <summary>去掉 "host:port" 的端口部分，只留主机。</summary>
    private static string StripPort(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return host;
        // IPv6 形如 [::1]:80 —— 这里只简单处理，IPv6 直接地址场景较少
        var idx = host.LastIndexOf(':');
        if (idx > 0 && host.Count(c => c == ':') == 1) return host[..idx];
        return host;
    }
}
