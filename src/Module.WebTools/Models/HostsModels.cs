using System.Collections.Generic;

namespace NewToys.WebTools.Models;

/// <summary>hosts 里的一行：<c>IP  主机名  # 注释</c>。</summary>
public class HostsEntry
{
    public string Ip { get; set; } = "";
    public string Host { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>渲染成一行 hosts 文本（禁用行加 # 前缀）。</summary>
    public string ToLine()
    {
        var body = $"{Ip,-18}{Host}";
        if (Comment.Length > 0) body += "  # " + Comment;
        return Enabled ? body : "# " + body;
    }
}

/// <summary>一份 hosts 配置（一个可切换的「方案」）。</summary>
public class HostsProfile
{
    public string Name { get; set; } = "";
    /// <summary>来源：空 = 本地新建；"network:&lt;url&gt;" = 网络导入。</summary>
    public string Source { get; set; } = "";
    public List<HostsEntry> Entries { get; set; } = new();
}
