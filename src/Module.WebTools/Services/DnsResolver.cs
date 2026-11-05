using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NewToys.WebTools.Services;

/// <summary>极简 DNS 解析器：向指定 DNS 服务器发一个 A 记录查询，返回解析出的 IPv4 地址。（WHY: WT-004）</summary>
public sealed class DnsResolver
{
    /// <summary>向 <paramref name="dnsServer"/> 解析 <paramref name="name"/> 的 A 记录；超时返回空列表。</summary>
    public async Task<IReadOnlyList<IPAddress>> ResolveAAsync(string name, string dnsServer, int timeoutMs = 3000)
    {
        var results = new List<IPAddress>();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dnsServer)) return results;

        try
        {
            var server = IPAddress.Parse(dnsServer);
            var query = BuildQuery(name);

            using var udp = new UdpClient();
            udp.Client.SendTimeout = timeoutMs;
            udp.Client.ReceiveTimeout = timeoutMs;
            await udp.SendAsync(query, query.Length, new IPEndPoint(server, 53)).ConfigureAwait(false);

            var remote = new IPEndPoint(server, 53);
            var response = udp.Receive(ref remote);
            if (response == null || response.Length < 12) return results;

            results.AddRange(ParseAnswers(response));
        }
        catch
        {
            // 解析失败（服务器不可达 / 超时 / 格式异常）→ 当作无结果，交由调用方给出状态
        }

        return results;
    }

    /// <summary>拼一个最小 DNS 查询报文（header + 1 个问题；递归期望）。</summary>
    private static byte[] BuildQuery(string name)
    {
        var id = (ushort)Environment.TickCount;   // 仅用于回显匹配，不需要强随机
        var header = new byte[]
        {
            (byte)(id >> 8), (byte)(id & 0xFF),
            0x01, 0x00,                         // QR=0, OPCODE=0, RD=1
            0x00, 0x01,                         // QDCOUNT=1
            0x00, 0x00,                         // ANCOUNT=0
            0x00, 0x00,                         // NSCOUNT=0
            0x00, 0x00                          // ARCOUNT=0
        };

        var qname = new List<byte>();
        foreach (var label in name.Split('.'))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);
            qname.Add((byte)bytes.Length);
            qname.AddRange(bytes);
        }
        qname.Add(0x00);                        // 根标签

        var question = new List<byte>(qname);
        question.Add(0x00); question.Add(0x01); // QTYPE = A
        question.Add(0x00); question.Add(0x01); // QCLASS = IN

        var msg = new List<byte>(header);
        msg.AddRange(question);
        return msg.ToArray();
    }

    /// <summary>跳过问题段，解析回答段的 A 记录（处理 0xC0 名字压缩指针）。</summary>
    private static IEnumerable<IPAddress> ParseAnswers(byte[] msg)
    {
        // 问题段长度：从 offset 12 起，每个标签到 0 结束，再加 QTYPE(2)+QCLASS(2)
        var off = 12;
        while (off < msg.Length && msg[off] != 0) off += msg[off] + 1;
        off += 1 + 4; // 根标签(1) + QTYPE(2) + QCLASS(2)

        var anCount = (msg[6] << 8) | msg[7];
        for (var i = 0; i < anCount && off + 12 <= msg.Length; i++)
        {
            // 名字：0xC0 指针占 2 字节，否则走到 0 结束
            if ((msg[off] & 0xC0) == 0xC0) off += 2;
            else { while (off < msg.Length && msg[off] != 0) off += msg[off] + 1; off += 1; }

            var type = (msg[off] << 8) | msg[off + 1];
            off += 8; // TYPE(2) + CLASS(2) + TTL(4)
            var rdLen = (msg[off] << 8) | msg[off + 1];
            off += 2;

            if (type == 1 && rdLen == 4 && off + 4 <= msg.Length)
            {
                yield return new IPAddress(new byte[] { msg[off], msg[off + 1], msg[off + 2], msg[off + 3] });
            }
            off += rdLen;
        }
    }
}
