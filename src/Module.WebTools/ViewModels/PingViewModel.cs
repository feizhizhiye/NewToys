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
public partial class PingViewModel : ListPageViewModelBase
{
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly PingService _svc = null!;
    private readonly INavigationService? _nav;
    private readonly WebTransferService _transfer = null!;
    private readonly List<PingTarget> _all = new();

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
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private PingTarget? _selected;
    [ObservableProperty] private string _selectedDns = "系统默认";
    [ObservableProperty] private string _searchText = "";

    // ============ 表头 ============
    [ObservableProperty] private string _hostHeader = "主机 ▲";
    [ObservableProperty] private string _ipHeader = "解析 IP";
    [ObservableProperty] private string _statusHeader = "状态";
    [ObservableProperty] private string _roundtripHeader = "时延(ms)";
    [ObservableProperty] private string _dnsHeader = "DNS";

    // ============ 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ============
    //
    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里，落 config\columnlayout.json）。

    public bool IsHostVisible => Vis("Host");
    public bool IsIpVisible => Vis("Ip");
    public bool IsStatusVisible => Vis("Status");
    public bool IsRoundtripVisible => Vis("Roundtrip");
    public bool IsDnsVisible => Vis("Dns");

    protected override string PageKey => "web-ping";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.32 §4.8.3：每列给一条示例值（空态水印行 + 列宽判定）
        new("Host", "主机", "2.4*", sample: "github.com"),
        new("Ip", "解析 IP", "1.6*", sample: "20.205.243.166"),
        new("Status", "状态", "1.4*", sample: "可达"),
        new("Roundtrip", "时延(ms)", "1*", sample: "38"),
        new("Dns", "DNS", "1.2*", sample: "系统默认"),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    // ==================== v3.32 §4.8.3：列表示例行 + 详情卡示例态 ====================

    /// <summary>详情卡 / 列表示例行用的**示例目标**。</summary>
    private static readonly PingTarget SampleItem = new()
    {
        Host = "github.com",
        LastIp = "20.205.243.166",
        LastStatus = "可达（HTTP/ICMP 通）",
        LastOk = true,
        LastRoundtripMs = 38,
        LastTime = new DateTime(2026, 10, 13, 15, 0, 0)
    };

    /// <summary>列表为空时显示**示例行**（⛔ 不进 <c>Items</c>、不参与统计 / 筛选 / 排序）。</summary>
    public bool ShowSampleRow => HasSampleRow && Items.Count == 0;

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例目标**。</summary>
    public PingTarget DetailItem => Selected ?? SampleItem;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    private readonly IAppConfigService? _app;

    /// <summary>导入 / 导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.19 / NT-006 全页统一）。</summary>
    public string ImportExportDefaultDir => _app?.VideoDbFolder ?? "";

    public PingViewModel() { }

    public PingViewModel(PingService svc, IColumnLayoutService cols, INavigationService? nav, WebTransferService transfer, IAppConfigService app)
        : base(cols)
    {
        _svc = svc;
        _nav = nav;
        _transfer = transfer;
        _app = app;
        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        UpdateDetailSample(false);        // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
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
    private void ApplyView()
    {
        IEnumerable<PingTarget> q = _all;
        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Host.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        q = SortColumn switch
        {
            "Ip" => SortDescending ? q.OrderByDescending(t => t.LastIp) : q.OrderBy(t => t.LastIp),
            "Status" => SortDescending ? q.OrderByDescending(t => t.LastOk) : q.OrderBy(t => t.LastOk),
            "Roundtrip" => SortDescending ? q.OrderByDescending(t => t.LastRoundtripMs) : q.OrderBy(t => t.LastRoundtripMs),
            "Dns" => SortDescending ? q.OrderByDescending(t => t.DnsOverride) : q.OrderBy(t => t.DnsOverride),
            _ => SortDescending ? q.OrderByDescending(t => t.Host, StringComparer.CurrentCultureIgnoreCase)
                                 : q.OrderBy(t => t.Host, StringComparer.CurrentCultureIgnoreCase)
        };

        var ordered = q.ToList();
        Items.Clear();
        foreach (var t in ordered) Items.Add(t);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.32：有真实数据后示例行消失
        UpdateDetailSample(Selected != null);
    }

    private void UpdateHeaders()
    {
        HostHeader = "主机" + SortArrow("Host");
        IpHeader = "解析 IP" + SortArrow("Ip");
        StatusHeader = "状态" + SortArrow("Status");
        RoundtripHeader = "时延(ms)" + SortArrow("Roundtrip");
        DnsHeader = "DNS" + SortArrow("Dns");
    }

    partial void OnSelectedChanged(PingTarget? value)
    {
        UpdateDetailSample(value != null);   // v3.32 §4.8.3：没选中项 → 详情卡改用示例目标
        OnPropertyChanged(nameof(DetailItem));
        OnPropertyChanged(nameof(HasSelection));
    }
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
