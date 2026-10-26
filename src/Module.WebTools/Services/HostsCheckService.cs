using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NewToys.WebTools.Models;

namespace NewToys.WebTools.Services;

/// <summary>检测参数（来自设置页的「Hosts」节）。</summary>
public class HostsCheckOptions
{
    public int TimeoutSeconds { get; init; } = 3;
    public int Concurrency { get; init; } = 8;
}

/// <summary>
/// hosts 检测：① 条目**可用性**（本机 DNS 解析 vs 映射 IP + TCP 连通 + 时延）；
/// ② **静态安全**规则；③ 源**失效**判定（跌一半）。
/// <para>
/// 🔴 能力边界：⛔ 不联网查 IP 信誉 / ASN、⛔ 不做投毒判定 —— 「安全」= 一眼能看出的静态风险，
/// 文案里必须说清这一点（⛔ 别写成「已验证安全」）。
/// </para>
/// <para>并发上限由设置页给（默认 8），全程可取消；只检测 <see cref="HostsEntry.Enabled"/> 的条目。</para>
/// </summary>
public class HostsCheckService
{
    /// <summary>并发检测一遍（结果直接写回 <paramref name="entries"/> 的条目上）。</summary>
    public async Task CheckAsync(
        IEnumerable<HostsEntry> entries,
        HostsCheckOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var targets = entries.Where(e => e.Enabled).ToList();
        var concurrency = Math.Clamp(options.Concurrency, 1, 32);
        var timeout = Math.Clamp(options.TimeoutSeconds, 1, 30);

        using var gate = new SemaphoreSlim(concurrency);
        var done = 0;

        var tasks = targets.Select(async entry =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await CheckOneAsync(entry, timeout, ct).ConfigureAwait(false);
                var n = Interlocked.Increment(ref done);
                progress?.Report(n);
            }
            catch (OperationCanceledException) { /* 取消时保留已完成的结论 */ }
            finally { gate.Release(); }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>清掉所有检测结论（重读方案 / 改条目后调用）。</summary>
    public static void ResetResults(IEnumerable<HostsEntry> entries)
    {
        foreach (var e in entries)
        {
            e.Status = HostsEntryStatus.Unknown;
            e.LatencyMs = 0;
            e.ResolvedIp = "";
            e.Port = 0;
        }
    }

    // ==================== 单条 ====================

    private static async Task CheckOneAsync(HostsEntry entry, int timeoutSeconds, CancellationToken ct)
    {
        entry.Status = HostsEntryStatus.Unknown;
        entry.LatencyMs = 0;
        entry.ResolvedIp = "";
        entry.Port = 0;

        if (!IPAddress.TryParse(entry.Ip, out var mapped))
        {
            entry.Status = HostsEntryStatus.Unreachable;
            return;
        }

        // ① 本机 DNS 解析（失败不阻断连通性测试，只是没有「是否一致」的结论）
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(entry.Host, ct).ConfigureAwait(false);
            var first = addrs.FirstOrDefault(a => a.AddressFamily == mapped.AddressFamily)
                        ?? addrs.FirstOrDefault();
            entry.ResolvedIp = first?.ToString() ?? "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* DNS 失败：留空 */ }

        // ② TCP：443 优先，失败试 80
        var (reachable, ms, timedOut, port) = await TryConnectAsync(mapped, 443, timeoutSeconds, ct).ConfigureAwait(false);
        if (!reachable && !timedOut)
            (reachable, ms, timedOut, port) = await TryConnectAsync(mapped, 80, timeoutSeconds, ct).ConfigureAwait(false);

        if (reachable)
        {
            entry.LatencyMs = ms;
            entry.Port = port;
            entry.Status = entry.ResolvedIp.Length > 0 && entry.ResolvedIp != entry.Ip
                ? HostsEntryStatus.Mismatch
                : HostsEntryStatus.Ok;
        }
        else
        {
            entry.Status = timedOut ? HostsEntryStatus.Timeout : HostsEntryStatus.Unreachable;
        }
    }

    private static async Task<(bool Ok, int Ms, bool TimedOut, int Port)> TryConnectAsync(
        IPAddress ip, int port, int timeoutSeconds, CancellationToken ct)
    {
        using var client = new TcpClient(ip.AddressFamily);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(ip, port, cts.Token).ConfigureAwait(false);
            return (true, (int)sw.ElapsedMilliseconds, false, port);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, 0, true, 0);       // 自己按超时取消
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return (false, 0, false, 0);      // 拒绝 / 不可达
        }
    }

    // ==================== 静态安全规则 ====================

    /// <summary>静态检查（纯本地计算，⛔ 不联网）。</summary>
    public static HostsSecurityReport Inspect(IReadOnlyList<HostsEntry> entries)
    {
        var report = new HostsSecurityReport();
        if (entries.Count == 0) return report;

        var blocking = new List<string>();
        var privateIps = new List<string>();
        var badHosts = new List<string>();
        var ipAsHost = new List<string>();
        var emptyHost = 0;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dups = new List<string>();

        foreach (var e in entries)
        {
            var host = (e.Host ?? "").Trim();
            if (host.Length == 0) { emptyHost++; continue; }

            if (!seen.TryAdd(host, 1)) { seen[host]++; if (seen[host] == 2) dups.Add(host); }

            if (IPAddress.TryParse(host, out _)) ipAsHost.Add(host);
            else if (!IsPlausibleHostName(host)) badHosts.Add(host);

            if (!IPAddress.TryParse(e.Ip, out var ip)) continue;

            if (IsBlocking(ip)) blocking.Add(host);
            else if (IsPrivateOrReserved(ip)) privateIps.Add(host);
        }

        if (blocking.Count > 0)
            report.Items.Add($"{blocking.Count} 条把域名指向黑洞 / 本机（0.0.0.0 / 127.0.0.1 / ::）：{Sample(blocking)}"
                             + " —— 这类写法常用于**屏蔽**站点，确认是你想要的效果");

        if (privateIps.Count > 0)
            report.Items.Add($"{privateIps.Count} 条指向内网 / 保留地址（如 192.168.x / 10.x / 169.254.x）：{Sample(privateIps)}");

        if (dups.Count > 0)
            report.Items.Add($"{dups.Count} 个域名在多行重复（同一个域名只有第一条生效）：{Sample(dups)}");

        if (ipAsHost.Count > 0)
            report.Items.Add($"{ipAsHost.Count} 条把 **IP 当主机名**（hosts 左侧必须是域名）：{Sample(ipAsHost)}");

        if (badHosts.Count > 0)
            report.Items.Add($"{badHosts.Count} 条主机名含非法字符或通配符（hosts 不支持 *，只认字母 / 数字 / - / .）：{Sample(badHosts)}");

        if (emptyHost > 0) report.Items.Add($"{emptyHost} 条没有主机名");

        // 同一个 IP 承载过多域名 ⇒ 泛解析 / 采集站特征
        var crowd = entries
            .GroupBy(e => e.Ip)
            .Where(g => g.Count() > 50)
            .Select(g => $"{g.Key}（{g.Count()} 个域名）")
            .ToList();
        if (crowd.Count > 0)
            report.Items.Add($"{crowd.Count} 个 IP 承载了 50 个以上域名（疑似泛解析 / 采集站）：{string.Join("、", crowd.Take(3))}");

        return report;
    }

    private static string Sample(IReadOnlyList<string> items)
        => string.Join("、", items.Take(3)) + (items.Count > 3 ? " …" : "");

    private static bool IsPlausibleHostName(string host)
    {
        if (host.Length > 253) return false;
        foreach (var c in host)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_') continue;
            return false;
        }
        return !host.StartsWith('.') && !host.EndsWith('.') && !host.Contains("..");
    }

    private static bool IsBlocking(IPAddress ip)
        => IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip) || IPAddress.Loopback.Equals(ip) || IPAddress.IPv6Loopback.Equals(ip);

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var b6 = ip.GetAddressBytes();
            return (b6[0] & 0xFE) == 0xFC;             // fc00::/7 唯一本地地址
        }

        var b = ip.GetAddressBytes();
        return b[0] switch
        {
            10 => true,                                        // 10/8
            127 => true,                                       // 回环
            0 => true,                                         // 0/8
            172 => b[1] is >= 16 and <= 31,                     // 172.16/12
            192 => b[1] == 168,                                 // 192.168/16
            169 => b[1] == 254,                                 // 链路本地
            >= 224 => true,                                     // 组播 / 保留 / 广播
            _ => false
        };
    }
}
