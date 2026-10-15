using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
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
public partial class PingViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条 + 选择器命令（NT-055 / 需求 §4.8.5） ====================

    /// <summary>📥 导入文件：弹文件选择器（宿主经 <c>Core.DialogHooks</c> 注入）→ 交给 VM 解析。</summary>
    [RelayCommand]
    private async Task ImportTargetsAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择一行一个主机的文本文件", ImportExportDefaultDir, false, "*.txt");
        var file = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(file)) return;
        await ImportFromFileAsync(file);
    }

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "📥 导入文件", Classes = "ghost", CommandPath = nameof(ImportTargetsCommand), ToolTip = "选择一行一个主机的文本文件" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索主机…", ValuePath = nameof(SearchText) },
        // v3.60：原 Label「右键列表项：…」删除（工具条⛔ 不放说明文字）—— 右键能力本来就写在各右键菜单项上。
        new() { Text = "➕ 新建", CommandPath = nameof(NewTargetCommand) },
        new() { Text = "📡 批量 Ping", CommandPath = nameof(PingAllCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "🔄 重读目标", CommandPath = nameof(RefreshCommand), ToolTip = "重新读一遍本地的目标清单（⛔ 不发起 Ping）" },
        new() { Kind = ToolbarItemKind.Combo, Width = 200, ItemsPath = nameof(DnsPresets), SelectedItemPath = nameof(SelectedDns), ToolTip = "切换 DNS：批量 Ping 与未单独设置 DNS 的目标都按此解析" }
    };

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
        // v3.22 §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）
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

    /// <summary>详情卡的数据源：有选中项时显示它（v3.60 §4.8.1：⛔ 没选中不填示例值）。</summary>
    public PingTarget? DetailItem => Selected;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;
    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    /// <summary>「最近结果」块是否要显示（原 XAML 用 <c>ObjectConverters.IsNotNull</c>，描述符只认布尔路径）。</summary>
    public bool HasLastStatus => !string.IsNullOrWhiteSpace(DetailItem?.LastStatus);

    /// <summary>解析出的 IP 展示串（原 XAML 走 <c>StringFormat</c>，描述符不支持格式化 ⇒ 这里先拼好）。</summary>
    public string LastIpDisplay => DetailItem?.LastIp ?? "";

    /// <summary>检测时间展示串（同上）。</summary>
    public string LastTimeDisplay => DetailItem?.LastTime is { } t ? t.ToString("yyyy-MM-dd HH:mm") : "";

    private void NotifyLastInfo()
    {
        OnPropertyChanged(nameof(HasLastStatus));
        OnPropertyChanged(nameof(LastIpDisplay));
        OnPropertyChanged(nameof(LastTimeDisplay));
    }

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片对应：主机 / 专属 DNS + 最近结果块 + 三个卡内动作（Ping / 保存 / 删除）。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧列表里选中一个目标",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "主机（域名或 IP）",
                        ValuePath = "DetailItem.Host",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "专属 DNS（留空 = 用全局 DNS）",
                        ValuePath = "DetailItem.DnsOverride",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Placeholder = "如 8.8.8.8"
                    },
                    new DetailField
                    {
                        Label = "最近结果",
                        ValuePath = "DetailItem.LastStatus",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        VisibleWhen = nameof(HasLastStatus)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Prefix = "解析 IP：",
                        ValuePath = nameof(LastIpDisplay),
                        VisibleWhen = nameof(HasLastStatus)
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Prefix = "检测时间：",
                        ValuePath = nameof(LastTimeDisplay),
                        VisibleWhen = nameof(HasLastStatus)
                    }
                }
            }
        },
        Actions =
        {
            // 原来这三个按钮是页内卡里的 WrapPanel；声明式卡里进 Actions 区。
            new DetailAction
            {
                Text = "📡 Ping",
                Classes = "primary",
                ToolTip = "对这台主机做一次连通性检测（用专属 DNS 或全局 DNS）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = PingCommand
            },
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "ghost",
                ToolTip = "把当前内容写回目标清单",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这个目标",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

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
        NotifyLastInfo();
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
        finally { IsBusy = false; ApplyView(); NotifyLastInfo(); }
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
        OnPropertyChanged(nameof(DetailItem));
        NotifyLastInfo();       // 切选中项 → 「最近结果」块跟着换
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
