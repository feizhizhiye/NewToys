using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.WebTools.Models;

/// <summary>hosts 里的一行：<c>IP  主机名  # 注释</c>。</summary>
public class HostsEntry
{
    public string Ip { get; set; } = "";
    public string Host { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // ==================== 检测结果（⛔ 不落库：重读方案即清空，避免陈旧结论） ====================

    /// <summary>可用性检测结论。</summary>
    [JsonIgnore] public HostsEntryStatus Status { get; set; } = HostsEntryStatus.Unknown;

    /// <summary>TCP 连接耗时（毫秒；未检出 = 0）。</summary>
    [JsonIgnore] public int LatencyMs { get; set; }

    /// <summary>本机 DNS 解析出的真实 IP（与 <see cref="Ip"/> 不一致即「解析不一致」）。</summary>
    [JsonIgnore] public string ResolvedIp { get; set; } = "";

    /// <summary>连上的端口（443 优先，失败回退 80；0 = 没连上）。</summary>
    [JsonIgnore] public int Port { get; set; }

    /// <summary>行的检测摘要（列表「状态」列 / 详情卡用）。</summary>
    [JsonIgnore]
    public string StatusText => Status switch
    {
        HostsEntryStatus.Ok => LatencyMs > 0 ? $"✅ 可达 {LatencyMs}ms" : "✅ 可达",
        HostsEntryStatus.Mismatch => string.IsNullOrEmpty(ResolvedIp) ? "⚠ 解析不一致" : $"⚠ 解析不一致（本机 {ResolvedIp}）",
        HostsEntryStatus.Unreachable => "❌ 不可达",
        HostsEntryStatus.Timeout => "⏱ 超时",
        _ => "—"
    };

    /// <summary>是否有问题（未检测不算问题）。</summary>
    [JsonIgnore] public bool IsProblem => Status is HostsEntryStatus.Mismatch or HostsEntryStatus.Unreachable or HostsEntryStatus.Timeout;

    /// <summary>渲染成一行 hosts 文本（禁用行加 # 前缀）。</summary>
    public string ToLine() => HostsText.RenderEntry(Ip, Host, Comment, Enabled);
}

/// <summary>
/// hosts 文件里的**一行**（页面投影，⛔ 不落库）：条目行带 IP / 主机名 / 注释，
/// 非条目行（空行 / 纯注释 / 受管块标记）只保留原文，写回时原样输出。
/// </summary>
public partial class HostsLine : ObservableObject
{
    public int No { get; set; }

    /// <summary>原始行文本（非条目行写回时用）。</summary>
    [ObservableProperty] private string _raw = "";

    /// <summary>能否解析成条目（空行 / 纯注释 / 非法行 = false）。</summary>
    [ObservableProperty] private bool _isEntry;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _ip = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _comment = "";

    /// <summary>位于本程序的受管块区间内。</summary>
    [ObservableProperty] private bool _inManaged;

    /// <summary>与另一侧的差异标记：<c>＋</c> 仅本侧 / <c>－</c> 对方缺失 / <c>≠</c> IP 不同。</summary>
    [ObservableProperty] private string _mark = "";

    public string IpHost => IsEntry ? $"{Ip,-16}{Host}" : Raw.Trim();

    public string StateText => !IsEntry ? "" : Enabled ? "" : "禁用";

    public string ManagedText => InManaged ? "受管" : "";

    partial void OnIpChanged(string value) => OnPropertyChanged(nameof(IpHost));

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(IpHost));

    partial void OnRawChanged(string value) => OnPropertyChanged(nameof(IpHost));

    partial void OnIsEntryChanged(bool value) => OnPropertyChanged(nameof(IpHost));

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(StateText));
}

/// <summary>把条目渲染成一行 hosts 文本的**唯一**口径（条目行 / 行模型 / 受管块共用）。</summary>
public static class HostsText
{
    public static string RenderEntry(string ip, string host, string? comment, bool enabled)
    {
        var body = $"{ip,-18}{host}";
        if (!string.IsNullOrWhiteSpace(comment)) body += "  # " + comment.Trim();
        return enabled ? body : "# " + body;
    }
}

/// <summary>一条 hosts 条目的可用性结论。</summary>
public enum HostsEntryStatus
{
    /// <summary>没检测过。</summary>
    Unknown,

    /// <summary>本机解析与映射一致且端口可达。</summary>
    Ok,

    /// <summary>端口可达，但本机 DNS 给出的 IP 与映射的 IP 不同。</summary>
    Mismatch,

    /// <summary>端口连不上。</summary>
    Unreachable,

    /// <summary>连接超时。</summary>
    Timeout
}

/// <summary>一份 hosts 配置（一个可切换的「方案」）。</summary>
public class HostsProfile
{
    public string Name { get; set; } = "";

    /// <summary>来源：空 = 本地新建；<c>network:&lt;url&gt;</c> = 网络导入；<c>source:&lt;id&gt;</c> = 源订阅。</summary>
    public string Source { get; set; } = "";

    public List<HostsEntry> Entries { get; set; } = new();

    /// <summary>上次从源拉取成功的时间（源订阅方案才有）。</summary>
    public DateTime? LastFetchedAt { get; set; }

    /// <summary>上次从源拉取到的条目数（判断「源失效」的基准）。</summary>
    public int LastFetchedCount { get; set; }

    /// <summary>列表「健康」列的汇总（未检测 = 「—」）。</summary>
    [JsonIgnore]
    public string HealthText
    {
        get
        {
            if (Entries.Count == 0) return "—";
            if (Entries.All(e => e.Status == HostsEntryStatus.Unknown)) return "—";
            var ok = Entries.Count(e => e.Status == HostsEntryStatus.Ok);
            var bad = Entries.Count(e => e.IsProblem);
            return $"✅{ok} ⚠{bad}";
        }
    }
}

/// <summary>一个 hosts 源（预设 / 自定义）：主地址 + 镜像地址，逐个 fallback。</summary>
public class HostsSource
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>地址列表：第一个是主地址，其余是镜像（顺序即尝试顺序）。</summary>
    public List<string> Urls { get; set; } = new();

    public bool Enabled { get; set; } = true;

    /// <summary>随包预设（用户可删，删了不再播种）。</summary>
    public bool Builtin { get; set; }

    /// <summary>上次拉取成功的时间 / 条目数 / 用的哪个地址。</summary>
    public DateTime? LastFetchedAt { get; set; }
    public int LastCount { get; set; }
    public string LastOkUrl { get; set; } = "";

    /// <summary>下拉里显示的名字（自定义源加后缀便于区分）。</summary>
    [JsonIgnore] public string Display => Builtin ? Name : Name + "（自定义）";

    /// <summary>悬停说明：地址数 / 上次拉取情况。</summary>
    [JsonIgnore]
    public string Hint
    {
        get
        {
            var urls = Urls.Count == 0 ? "无地址" : Urls.Count == 1 ? Urls[0] : $"{Urls[0]}（备 {Urls.Count - 1} 个镜像）";
            var last = LastFetchedAt is null ? "未拉取过" : $"上次 {LastFetchedAt:MM-dd HH:mm} · {LastCount} 条";
            return $"{urls} · {last}";
        }
    }
}

/// <summary>页面左栏「目标」的数据源：当前系统 hosts，或某一份备份快照。</summary>
public sealed class HostsTargetSource
{
    public bool IsCurrent { get; init; }
    public HostsBackupInfo? Backup { get; init; }

    public string Display => IsCurrent
        ? "当前系统 hosts"
        : $"备份 {Backup!.TimeText}（{Backup.SizeText}）";

    public static HostsTargetSource Current() => new() { IsCurrent = true };
    public static HostsTargetSource Of(HostsBackupInfo backup) => new() { Backup = backup };
}

/// <summary>一份系统 hosts 备份。</summary>
public class HostsBackupInfo{
    public string Path { get; set; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public DateTime Time { get; set; }

    /// <summary>备份内容（完整系统 hosts 原文）。</summary>
    public long Bytes { get; set; }

    public string TimeText => Time.ToString("MM-dd HH:mm:ss");
    public string SizeText => Bytes < 1024 ? $"{Bytes} B" : $"{Bytes / 1024.0:0.0} KB";
}

/// <summary>方案 vs 当前系统受管块的差异（应用前预检用）。</summary>
public class HostsDiff
{
    public int Added { get; set; }
    public int Removed { get; set; }
    public int Changed { get; set; }

    /// <summary>当前系统 hosts 里没有受管块。</summary>
    public bool NoManagedBlock { get; set; }

    public bool HasChange => Added + Removed + Changed > 0;

    public string Summary => NoManagedBlock
        ? "系统 hosts 里没有受管块 ⇒ 应用会新增一整段"
        : HasChange
            ? $"新增 {Added} · 删除 {Removed} · 改动 {Changed}"
            : "与当前受管块一致（应用后无变化）";
}

/// <summary>静态安全检查结果（⛔ 不联网：只查「能一眼看出的风险」，不等于无毒）。</summary>
public class HostsSecurityReport
{
    public List<string> Items { get; } = new();

    public int RiskCount => Items.Count;
    public bool IsSafe => Items.Count == 0;

    public string Summary => Items.Count == 0 ? "🛡 未发现静态风险" : $"⚠ {Items.Count} 项风险";
}
