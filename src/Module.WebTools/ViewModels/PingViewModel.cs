using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 网络 Ping 页（列表 + 详情）。
/// <para>
/// 支持：导入目标文件、全局 / 逐条「切换 DNS 进行解析 + ICMP」、批量 ping、表头排序、
/// 右键「发送到书签 / 网页抓取」跨页传 URL。列显隐 / 详情开合偏好落 <c>config\columnlayout.json</c>。
/// </para>
/// </summary>
public partial class PingViewModel : ObservableObject
{
    private const string PageKey = "web-ping";
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly PingService _svc = null!;
    private readonly IColumnLayoutService? _cols;
    private readonly INavigationService? _nav;
    private readonly WebTransferService _transfer = null!;
    private readonly List<PingTarget> _all = new();
    private string _sortColumn = "Host";
    private bool _sortDescending;

    /// <summary>DNS 预设（"系统默认" 表示回退系统解析）。</summary>
    public IReadOnlyList<string> DnsPresets { get; } = new List<string>
    {
        "系统默认",
        "8.8.8.8 (Google)",
        "1.1.1.1 (Cloudflare)",
        "9.9.9.9 (Quad9)",
        "223.5.5.5 (阿里)",
        "119.29.29.29 (腾讯)",
    };

    public ObservableCollection<PingTarget> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private PingTarget? _selected;
    [ObservableProperty] private string _selectedDns = "系统默认";
    [ObservableProperty] private string _searchText = "";

    // ============ 表头 ============
    [ObservableProperty] private string _hostHeader = "主机 ▲";
    [ObservableProperty] private string _ipHeader = "解析 IP";
    [ObservableProperty] private string _statusHeader = "状态";
    [ObservableProperty] private string _roundtripHeader = "时延(ms)";
    [ObservableProperty] private string _dnsHeader = "DNS";

    // ============ 列显隐 ============
    [ObservableProperty] private bool _isHostVisible = true;
    [ObservableProperty] private bool _isIpVisible = true;
    [ObservableProperty] private bool _isStatusVisible = true;
    [ObservableProperty] private bool _isRoundtripVisible = true;
    [ObservableProperty] private bool _isDnsVisible = true;

    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Host", "主机", "2.4*"),
        new("Ip", "解析 IP", "1.6*"),
        new("Status", "状态", "1.4*"),
        new("Roundtrip", "时延(ms)", "1*"),
        new("Dns", "DNS", "1.2*"),
        new("Detail", "", "36", sortable: false),
    };

    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                parts[i] = IsVisibleFor(Columns[i].Key) ? Columns[i].Width : "0";
            return string.Join(",", parts);
        }
    }

    private bool IsVisibleFor(string key) => key switch
    {
        "Host" => IsHostVisible, "Ip" => IsIpVisible, "Status" => IsStatusVisible,
        "Roundtrip" => IsRoundtripVisible, "Dns" => IsDnsVisible, _ => true
    };

    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (key == null) return;
        switch (key)
        {
            case "Host": IsHostVisible = !IsHostVisible; break;
            case "Ip": IsIpVisible = !IsIpVisible; break;
            case "Status": IsStatusVisible = !IsStatusVisible; break;
            case "Roundtrip": IsRoundtripVisible = !IsRoundtripVisible; break;
            case "Dns": IsDnsVisible = !IsDnsVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    private void SaveColumnVisibility()
    {
        _cols?.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Host"] = IsHostVisible, ["Ip"] = IsIpVisible, ["Status"] = IsStatusVisible,
            ["Roundtrip"] = IsRoundtripVisible, ["Dns"] = IsDnsVisible
        });
    }

    private void LoadColumnVisibility()
    {
        var saved = _cols?.LoadPage(PageKey);
        if (saved == null) return;
        if (saved.TryGetValue("Host", out var h)) IsHostVisible = h;
        if (saved.TryGetValue("Ip", out var ip)) IsIpVisible = ip;
        if (saved.TryGetValue("Status", out var s)) IsStatusVisible = s;
        if (saved.TryGetValue("Roundtrip", out var r)) IsRoundtripVisible = r;
        if (saved.TryGetValue("Dns", out var d)) IsDnsVisible = d;
    }

    // ============ 详情开合 ============
    [ObservableProperty] private bool _isDetailVisible;

    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible && Selected == null && Items.Count > 0) Selected = Items[0];
        SaveDetailFlag();
    }

    [RelayCommand]
    private void ShowDetail()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
        IsDetailVisible = true;
        SaveDetailFlag();
    }

    private void SaveDetailFlag() => _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    public PingViewModel() { }

    public PingViewModel(PingService svc, IColumnLayoutService cols, INavigationService? nav, WebTransferService transfer)
    {
        _svc = svc;
        _cols = cols;
        _nav = nav;
        _transfer = transfer;
        LoadColumnVisibility();
        IsDetailVisible = cols.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel);
        Refresh();

        // 跨页「发送到此页」：消费待处理 URL，直接建一条待 ping 的目标并打开详情。
        var pending = _transfer.TakeUrl();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            var host = StripScheme(pending);
            var draft = new PingTarget { Host = host };
            _all.Add(draft);
            Items.Add(draft);
            Selected = draft;
            IsDetailVisible = true;
            SaveDetailFlag();
            StatusText = "已从其他页收到网址，点「Ping」开始检测。";
        }
    }

    // ============ 加载 ============
    [RelayCommand]
    private void Refresh()
    {
        _all.Clear();
        foreach (var t in _svc.Load()) _all.Add(t);
        ApplyView();
        StatusText = $"共 {Items.Count} 个目标";
    }

    // ============ 导入文件（由 View 的「导入」按钮触发文件选择后调用） ============
    public async Task ImportFromFileAsync(string path)
    {
        try
        {
            IsBusy = true;
            var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            var added = 0;
            var known = new HashSet<string>(_all.Select(t => t.Host), StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                var host = raw.Trim();
                if (host.Length == 0 || host.StartsWith("#", StringComparison.Ordinal)) continue;
                if (known.Contains(host)) continue;
                _all.Add(new PingTarget { Host = host });
                known.Add(host);
                added++;
            }
            ApplyView();
            await PersistAsync();
            StatusText = added == 0 ? "没有新的目标（可能都已存在）。" : $"已导入 {added} 个目标。";
        }
        catch (Exception ex)
        {
            StatusText = "导入失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ============ 增删改 ============
    [RelayCommand]
    private void NewTarget()
    {
        var t = new PingTarget { Host = "example.com" };
        _all.Add(t);
        Items.Add(t);
        Selected = t;
        IsDetailVisible = true;
        SaveDetailFlag();
        StatusText = "已新建目标，填写主机后点「Ping」。";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        if (string.IsNullOrWhiteSpace(Selected.Host)) { StatusText = "主机不能为空。"; return; }
        await PersistAsync();
        StatusText = $"已保存「{Selected.Host}」。";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected == null) return;
        _all.Remove(Selected);
        Items.Remove(Selected);
        await PersistAsync();
        Selected = null;
        ApplyView();
        StatusText = "已删除目标。";
    }

    private async Task PersistAsync() => await Task.Run(() => _svc.Save(_all.ToList()));

    // ============ Ping ============
    [RelayCommand]
    private async Task PingAsync()
    {
        if (Selected == null) return;
        await PingOneAsync(Selected);
        ApplyView();
    }

    [RelayCommand]
    private async Task PingAllAsync()
    {
        if (Items.Count == 0) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在批量 ping {Items.Count} 个目标（DNS：{SelectedDns}）……";
            var sem = new System.Threading.SemaphoreSlim(8);
            await Task.WhenAll(Items.Select(async t =>
            {
                await sem.WaitAsync();
                try { await PingOneAsync(t); }
                finally { sem.Release(); }
            }));
            StatusText = "批量 ping 完成。";
        }
        finally { IsBusy = false; ApplyView(); }
    }

    private async Task PingOneAsync(PingTarget t)
    {
        var dns = !string.IsNullOrWhiteSpace(t.DnsOverride) ? t.DnsOverride : ExtractDnsIp(SelectedDns);
        var r = await _svc.PingAsync(t.Host, dns).ConfigureAwait(false);
        t.LastIp = r.Ip;
        t.LastOk = r.Ok;
        t.LastStatus = r.Message;
        t.LastRoundtripMs = r.RoundtripMs;
        t.LastTime = DateTime.UtcNow;
    }

    /// <summary>从预设串里抽出 DNS IP；"系统默认" / 空 → null（回退系统解析）。</summary>
    private static string? ExtractDnsIp(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset) || preset == "系统默认") return null;
        var sp = preset.IndexOf(' ');
        return (sp > 0 ? preset[..sp] : preset).Trim();
    }

    // ============ 跨页：发送到书签 / 网页抓取 ============
    [RelayCommand]
    private void SendToBookmark()
    {
        if (Selected == null) return;
        _transfer.PushUrl(EnsureHttp(Selected.Host));
        _nav?.NavigateTo("web-bookmark");
    }

    [RelayCommand]
    private void SendToScrape()
    {
        if (Selected == null) return;
        _transfer.PushUrl(EnsureHttp(Selected.Host));
        _nav?.NavigateTo("web-scrape");
    }

    // ============ 排序 ============
    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase)) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }
        ApplyView();
    }

    private void ApplyView()
    {
        IEnumerable<PingTarget> q = _all;
        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Host.Contains(text, StringComparison.OrdinalIgnoreCase));

        q = _sortColumn switch
        {
            "Ip" => _sortDescending ? q.OrderByDescending(t => t.LastIp) : q.OrderBy(t => t.LastIp),
            "Status" => _sortDescending ? q.OrderByDescending(t => t.LastOk) : q.OrderBy(t => t.LastOk),
            "Roundtrip" => _sortDescending ? q.OrderByDescending(t => t.LastRoundtripMs) : q.OrderBy(t => t.LastRoundtripMs),
            "Dns" => _sortDescending ? q.OrderByDescending(t => t.DnsOverride) : q.OrderBy(t => t.DnsOverride),
            _ => _sortDescending ? q.OrderByDescending(t => t.Host, StringComparer.CurrentCultureIgnoreCase)
                                 : q.OrderBy(t => t.Host, StringComparer.CurrentCultureIgnoreCase)
        };

        var ordered = q.ToList();
        Items.Clear();
        foreach (var t in ordered) Items.Add(t);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        var arrow = _sortDescending ? " ▼" : " ▲";
        HostHeader = "主机" + (_sortColumn == "Host" ? arrow : "");
        IpHeader = "解析 IP" + (_sortColumn == "Ip" ? arrow : "");
        StatusHeader = "状态" + (_sortColumn == "Status" ? arrow : "");
        RoundtripHeader = "时延(ms)" + (_sortColumn == "Roundtrip" ? arrow : "");
        DnsHeader = "DNS" + (_sortColumn == "Dns" ? arrow : "");
    }

    partial void OnSelectedChanged(PingTarget? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnSearchTextChanged(string value) => ApplyView();

    private static string StripScheme(string url)
    {
        var i = url.IndexOf("://", StringComparison.Ordinal);
        return i >= 0 ? url[(i + 3)..] : url;
    }

    private static string EnsureHttp(string host)
        => host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? host : "http://" + host;
}
