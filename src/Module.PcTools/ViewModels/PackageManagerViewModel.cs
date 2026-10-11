using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 软件包管理页。
/// <para>
/// 布局遵循规范 <b>3.8 列表页规范</b>：左边「可排序表头的列表」，右边「选中项的详情列」，
/// 列表上挂右键菜单（安装 / 升级 / 卸载 / Pin / 取消 Pin / 复制 Id / 读取详情 / 打开主页）。
/// </para>
/// <para>
/// <b>本页没有任何「保存」按钮</b>（规范 3.7）：源、静默安装、排序列都是点一下就生效并写
/// <c>config\package.json</c>（由 <see cref="IPackageSettingsService"/> 落盘）。
/// 「启用 / 关闭 Store 源」这类成体系的设置归 <b>设置 → 软件源</b> 页管；本页进入时读一次，
/// 因此设置页改完再回到本页即生效（页面每次导航都会重建 VM，见规范 6.5）。
/// </para>
/// <para>
/// ⚠️ 刻意<b>不订阅</b> <see cref="IPackageSettingsService.Changed"/>：那个服务是单例，
/// 页面 VM 是瞬态，订阅了就会把每次进入页面的 VM 都挂在单例上（内存泄漏 + 重复回调）。
/// </para>
/// </summary>
public partial class PackageManagerViewModel : ListPageViewModelBase
{
    private enum ListMode { None, Search, Installed, Upgrades }

    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;

    /// <summary>当前模式下的全量结果（界面集合只放过滤 + 排序后的投影）。</summary>
    private readonly List<PackageInfo> _all = new();

    private ListMode _mode = ListMode.None;

    /// <summary>已安装包 Id 的缓存（搜索结果要靠它把「状态」列标准）。</summary>
    private HashSet<string>? _installedIds;

    /// <summary>抑制"改属性 → 回写设置"的开关（批量灌值时用，避免无谓写盘与递归）。</summary>
    private bool _syncing;

    public ObservableCollection<PackageInfo> Packages { get; } = new();
    public ObservableCollection<PackageSourceOption> SourceOptions { get; } = new();

    // ==================== 页面状态 ====================

    [ObservableProperty] private bool _isSupported = true;
    [ObservableProperty] private string _supportText = "正在检测包管理器……";
    [ObservableProperty] private bool _isBusy;
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private string _modeText = "尚未加载 —— 点「📥 已安装」或直接联网搜索";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _installedOnly;
    [ObservableProperty] private bool _silentInstall = true;
    [ObservableProperty] private PackageInfo? _selected;
    [ObservableProperty] private PackageSourceOption? _selectedSource;
    [ObservableProperty] private string _detailExtra = "";

    // ==================== 表头（点一下切换排序） ====================

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _idHeader = "Id";
    [ObservableProperty] private string _versionHeader = "版本";
    [ObservableProperty] private string _sourceHeader = "源";
    [ObservableProperty] private string _statusHeader = "状态";
    [ObservableProperty] private string _pinnedHeader = "Pin";

    // ==================== 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ====================

    private readonly IColumnLayoutService? _cols;

    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里）
    public bool IsNameVisible => Vis("Name");
    public bool IsIdVisible => Vis("Id");
    public bool IsVersionVisible => Vis("Version");
    public bool IsSourceVisible => Vis("Source");
    public bool IsStatusVisible => Vis("Status");
    public bool IsPinnedVisible => Vis("Pinned");

    protected override string PageKey => "pc-package";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "3*"),
        new("Id", "Id", "3.6*"),
        new("Version", "版本", "1.3*"),
        new("Source", "源", "1.1*"),
        new("Status", "状态", "1.5*"),
        new("Pinned", "Pin", "0.8*"),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Packages.Count > 0) Selected = Packages[0];
    }

    /// <summary>筛选取值（多项筛选用）：按列键拿这一行该列的文本。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Name" => ((PackageInfo)row).Name,
        "Id" => ((PackageInfo)row).Id,
        "Version" => ((PackageInfo)row).Version,
        "Source" => ((PackageInfo)row).Source,
        "Status" => ((PackageInfo)row).StatusText,
        "Pinned" => ((PackageInfo)row).PinnedMark,
        _ => null
    };

    public bool HasSelection => Selected != null;
    public bool HasItems => Packages.Count > 0;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PackageManagerViewModel() { }

    public PackageManagerViewModel(IPackageManagerService pkg, IPackageSettingsService settings, IColumnLayoutService cols)
        : base(cols)
    {
        _pkg = pkg;
        _settings = settings;
        _cols = cols;

        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        _syncing = true;
        var s = settings.Settings;
        SetSort(string.IsNullOrWhiteSpace(s.SortColumn) ? "Name" : s.SortColumn, s.SortDescending);
        SilentInstall = s.SilentInstall;
        _syncing = false;
        UpdateHeaders();

        _ = InitAsync();
    }

    // ==================== 初始化 ====================

    private async Task InitAsync()
    {
        if (_pkg == null || _settings == null)
        {
            IsSupported = false;
            SupportText = "包管理服务未就绪（DI 未注册）。";
            return;
        }

        try
        {
            var (ok, msg) = await _pkg.ProbeAsync();
            IsSupported = ok;
            SupportText = ok ? $"✅ {msg}" : $"⚠️ {msg}";
            if (!ok) { StatusText = msg; ModeText = "当前环境下不可用"; return; }
            await LoadSourcesAsync();
        }
        catch (Exception ex)
        {
            IsSupported = false;
            SupportText = "检测包管理器时出错：" + ex.Message;
        }
    }

    /// <summary>
    /// 源下拉 = 「全部（系统 winget 里的）」+ 各源。
    /// <para>
    /// ⚠️ v1.33 起源完全来自**系统 winget**（<c>winget source list</c>），本应用不再自己维护
    /// "启用 / 关闭某个源"的开关 —— 要增删源请去「设置 → 软件源」页（那里改的也是系统 winget）。
    /// </para>
    /// </summary>
    private async Task LoadSourcesAsync()
    {
        if (_pkg == null) return;

        var (ok, _, items) = await _pkg.ListSourcesAsync();

        SourceOptions.Clear();
        SourceOptions.Add(new PackageSourceOption { Label = "全部源", Value = null });

        if (ok)
        {
            foreach (var s in items)
                SourceOptions.Add(new PackageSourceOption { Label = s.Name, Value = s.Name });
        }
        else SourceOptions.Add(new PackageSourceOption { Label = "winget", Value = "winget" });

        var want = _settings?.Settings.DefaultSource ?? "";
        _syncing = true;
        SelectedSource = SourceOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", want, StringComparison.OrdinalIgnoreCase))
                         ?? SourceOptions[0];
        _syncing = false;
    }

    // ==================== 加载列表 ====================

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_pkg == null || IsBusy) return;
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            StatusText = "请输入搜索关键字（例如 git / Docker / ffmpeg）。";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "正在搜索……";

            var source = SelectedSource?.Value;
            if (source == null)
            {
                // 「全部源」= 对**每个已启用的源**各查一遍再合并。
                // 这样「关掉 Store 源」就是真的不查它（而不是查完再过滤）。
                var merged = new List<PackageInfo>();
                var errs = new List<string>();
                foreach (var opt in SourceOptions.Where(o => !string.IsNullOrEmpty(o.Value)).ToList())
                {
                    var (ok, msg, items) = await _pkg.SearchAsync(SearchText, opt.Value);
                    if (ok) merged.AddRange(items);
                    else errs.Add($"{opt.Label}: {msg}");
                }
                BeginList(merged, ListMode.Search);
                ApplyView();
                StatusText = merged.Count == 0
                    ? "没有匹配的包" + (errs.Count > 0 ? "｜" + string.Join("；", errs) : "")
                    : $"找到 {merged.Count} 个包（已合并 {SourceOptions.Count - 1} 个源）";
            }
            else
            {
                var (ok, msg, items) = await _pkg.SearchAsync(SearchText, source);
                BeginList(items.ToList(), ListMode.Search);
                ApplyView();
                StatusText = msg;
            }

            await MarkInstalledAsync();
            ApplyView();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadInstalledAsync()
    {
        if (_pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = "正在读取已安装列表（首次可能稍慢）……";
            var (ok, msg, items) = await _pkg.ListInstalledAsync();
            BeginList(items.ToList(), ListMode.Installed);
            BuildInstalledCache();
            await ApplyPinnedAsync();
            ApplyView();
            StatusText = msg;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadUpgradesAsync()
    {
        if (_pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = "正在检查可升级的包……";
            var (ok, msg, items) = await _pkg.ListUpgradesAsync();
            BeginList(items.ToList(), ListMode.Upgrades);
            BuildInstalledCache();
            await ApplyPinnedAsync();
            ApplyView();
            StatusText = msg;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _installedIds = null;
        switch (_mode)
        {
            case ListMode.Search: await SearchAsync(); break;
            case ListMode.Upgrades: await LoadUpgradesAsync(); break;
            default: await LoadInstalledAsync(); break;
        }
    }

    private void BeginList(List<PackageInfo> items, ListMode mode)
    {
        _all.Clear();
        _all.AddRange(items);
        _mode = mode;
        ModeText = mode switch
        {
            ListMode.Search => $"搜索结果：{SearchText}",
            ListMode.Installed => "已安装（本机全部包）",
            ListMode.Upgrades => "可升级",
            _ => "尚未加载"
        };
        if (mode != ListMode.Search && SearchText.Length > 0) SearchText = "";
    }

    /// <summary>搜索结果没有"是否已安装"的信息 → 用已安装集合补上，让「状态」列说实话。</summary>
    private async Task MarkInstalledAsync()
    {
        if (_pkg == null) return;
        if (_installedIds == null)
        {
            var (ok, _, items) = await _pkg.ListInstalledAsync();
            if (ok) _installedIds = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
        }
        if (_installedIds == null) return;

        foreach (var p in _all) p.IsInstalled = _installedIds.Contains(p.Id);
        await ApplyPinnedAsync();
    }

    private void BuildInstalledCache()
        => _installedIds = new HashSet<string>(_all.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);

    /// <summary>把 pin 状态贴回列表（pin = 暂停更新，界面上必须看得出来）。</summary>
    private async Task ApplyPinnedAsync()
    {
        if (_pkg == null) return;
        var (ok, _, pinned) = await _pkg.ListPinnedAsync();
        if (!ok) return;
        var set = new HashSet<string>(pinned.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var p in _all) p.IsPinned = set.Contains(p.Id);
    }

    // ==================== 过滤 / 排序 ====================

    partial void OnSearchTextChanged(string value)
    {
        // 已安装 / 可升级 是本地列表 → 搜索框直接当过滤器（不联网）
        if (_mode != ListMode.Search && _mode != ListMode.None) ApplyView();
    }

    partial void OnInstalledOnlyChanged(bool value) => ApplyView();

    partial void OnSelectedChanged(PackageInfo? value)
    {
        DetailExtra = "";
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ApplyView()
    {
        IEnumerable<PackageInfo> q = _all;

        if (InstalledOnly && _mode == ListMode.Search)
            q = q.Where(p => p.IsInstalled);

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0 && _mode != ListMode.Search)
            q = q.Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || p.Id.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        // 排序是"点一下就看到结果"的轻设置 → 变了就落盘（config\package.json）
        if (_settings != null && !_syncing
            && (!string.Equals(_settings.Settings.SortColumn, SortColumn, StringComparison.OrdinalIgnoreCase)
                || _settings.Settings.SortDescending != SortDescending))
        {
            _settings.Settings.SortColumn = SortColumn;
            _settings.Settings.SortDescending = SortDescending;
            _settings.Save();
        }

        var byVersion = Comparer<string>.Create(CompareVersion);
        var byText = StringComparer.CurrentCultureIgnoreCase;

        q = SortColumn switch
        {
            "Id" => SortDescending ? q.OrderByDescending(p => p.Id, StringComparer.OrdinalIgnoreCase)
                                    : q.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
            "Version" => SortDescending ? q.OrderByDescending(p => p.Version, byVersion)
                                         : q.OrderBy(p => p.Version, byVersion),
            "Source" => SortDescending ? q.OrderByDescending(p => p.Source, byText)
                                        : q.OrderBy(p => p.Source, byText),
            "Status" => SortDescending ? q.OrderByDescending(p => p.StatusText, byText)
                                        : q.OrderBy(p => p.StatusText, byText),
            "Pinned" => SortDescending ? q.OrderByDescending(p => p.IsPinned)
                                        : q.OrderBy(p => p.IsPinned),
            _ => SortDescending ? q.OrderByDescending(p => p.Name, byText)
                                 : q.OrderBy(p => p.Name, byText)
        };

        _syncing = true;                                  // 重建集合期间不要再触发回写
        try
        {
            Packages.Clear();
            foreach (var p in q) Packages.Add(p);
        }
        finally { _syncing = false; }

        if (Selected != null && !Packages.Contains(Selected)) Selected = null;

        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        IdHeader = "Id" + SortArrow("Id");
        VersionHeader = "版本" + SortArrow("Version");
        SourceHeader = "源" + SortArrow("Source");
        StatusHeader = "状态" + SortArrow("Status");
        PinnedHeader = "Pin" + SortArrow("Pinned");
    }

    /// <summary>版本比较：能解析成 <see cref="System.Version"/> 就按版本比（1.10 &gt; 1.9），否则退回字符串比较。</summary>
    private static int CompareVersion(string a, string b)
        => System.Version.TryParse(a, out var va) && System.Version.TryParse(b, out var vb)
            ? va.CompareTo(vb)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    // ==================== 右键菜单 / 详情列的动作 ====================

    [RelayCommand]
    private async Task InstallAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在安装 {p.Id} ……（较大的包会久一点，请勿关闭窗口）";
            var (ok, msg) = await _pkg.InstallAsync(p.Id, string.IsNullOrEmpty(p.Source) ? SelectedSource?.Value : p.Source, SilentInstall);
            StatusText = msg;
            if (ok) { _installedIds = null; await MarkInstalledAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UpgradeAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在升级 {p.Id} ……";
            var (ok, msg) = await _pkg.UpgradeAsync(p.Id, SilentInstall);
            StatusText = msg;
            if (ok && _mode == ListMode.Upgrades) await LoadUpgradesAsync();
            else ApplyView();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            StatusText = $"正在卸载 {p.Id} ……";
            // ⚠️ 二次确认在 IPackageManagerService 内部完成（宿主把钩子接到了模态对话框上，见 App.axaml.cs）
            var (ok, msg) = await _pkg.UninstallAsync(p.Id);
            StatusText = msg;
            if (ok) { _installedIds = null; await RefreshAsync(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PinAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.PinAsync(p.Id);
            StatusText = msg;
            if (ok) { await ApplyPinnedAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UnpinAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null || IsBusy) return;
        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.UnpinAsync(p.Id);
            StatusText = msg;
            if (ok) { await ApplyPinnedAsync(); ApplyView(); }
        }
        finally { IsBusy = false; }
    }

    /// <summary>按需拉一次 <c>winget show</c>，把长描述 / 发布者 / 主页 / 协议补进详情列。</summary>
    [RelayCommand]
    private async Task LoadDetailAsync()
    {
        var p = Selected;
        if (p == null || _pkg == null) return;

        StatusText = "正在读取详情（winget show）……";
        var (ok, msg, detail) = await _pkg.ShowAsync(p.Id);
        if (!ok || detail == null) { StatusText = msg; return; }

        if (!string.IsNullOrWhiteSpace(detail.Name)) p.Name = detail.Name;
        if (!string.IsNullOrWhiteSpace(detail.Version)) p.Version = detail.Version;
        p.Publisher = detail.Publisher;
        p.Description = detail.Description;
        p.Homepage = detail.Homepage;
        p.License = detail.License;
        p.InstallLocation = detail.InstallLocation;

        // 列表项不是 ObservableObject（只有 Id 等快照字段）→ 整体重建最省事
        ApplyView();
        DetailExtra = detail.Description;
        StatusText = msg;
    }

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        var p = Selected;
        if (p == null) return;
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(p.Id);
            StatusText = $"已复制 Id：{p.Id}";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        var url = Selected?.Homepage;
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText = "这个包没有主页信息；可先点「🔎 读取详情」。";
            return;
        }
        LaunchRunner.OpenUrlDefault(url);
        StatusText = "已用默认浏览器打开：" + url;
    }

    /// <summary>「更多功能」里的一项：在 winget.run 上查这个包（网页版 winget 索引）。</summary>
    [RelayCommand]
    private void OpenInWingetRun()
    {
        var id = Selected?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        LaunchRunner.OpenUrlDefault("https://winget.run/pkg/" + id.Replace('.', '/'));
        StatusText = "已在浏览器打开 winget.run 的包页面。";
    }

    // ==================== 即时写盘（无保存按钮，规范 3.7） ====================

    partial void OnSelectedSourceChanged(PackageSourceOption? value)
    {
        if (_syncing || _settings == null || value == null) return;
        _settings.Settings.DefaultSource = value.Value ?? "";
        _settings.Save();
        StatusText = value.Value == null
            ? "已切到「全部源（已启用的）」，下次联网搜索会逐个源查一遍。"
            : $"已切到源：{value.Value}";
    }

    partial void OnSilentInstallChanged(bool value)
    {
        if (_syncing || _settings == null) return;
        _settings.Settings.SilentInstall = value;
        _settings.Save();
        StatusText = value ? "安装 / 升级将使用 --silent（静默）。" : "安装 / 升级会保留安装器界面。";
    }

    // 注：源的下拉只是**只读快照**（进入本页读一次系统 winget 的源）。
    // 要增删源请去「设置 → 软件源」页 —— 那里改的也是系统 winget，两边共用一套数据（v1.33）。
}
