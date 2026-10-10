using System;

namespace NewToys.WebTools.Models;

/// <summary>一个 Ping 目标（一条主机记录）。</summary>
public class PingTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>目标主机：域名或 IPv4/IPv6（可带 :端口，仅用于显示，ping 用主机部分）。</summary>
    public string Host { get; set; } = "";
    /// <summary>该目标专属 DNS（空 = 用全局 DNS）。</summary>
    public string DnsOverride { get; set; } = "";
    /// <summary>最近一次解析出的 IP。</summary>
    public string LastIp { get; set; } = "";
    /// <summary>最近一次 ping 结果文本（"OK 12ms" / "超时" / "解析失败" / 异常信息）；未检测为 null。</summary>
    public string? LastStatus { get; set; }
    /// <summary>是否成功。</summary>
    public bool LastOk { get; set; }
    /// <summary>最近一次往返时延（ms，失败为 -1）。</summary>
    public long LastRoundtripMs { get; set; } = -1;
    public DateTime? LastTime { get; set; }
}
