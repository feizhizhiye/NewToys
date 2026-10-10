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
public partial class PackageManagerViewModel : ObservableObject
{
    private enum ListMode { None, Search, Installed, Upgrades }

    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;

    /// <summary>当前模式下的全量结果（界面集合只放过滤 + 排序后的投影）。</summary>
    private readonly List<PackageInfo> _all = new();

    private ListMode _mode = ListMode.None;

    /// <summary>已安装包 Id 的缓存（搜索结果要靠它把「状态」列标准）。</summary>
    private HashSet<string>? _installedIds;

    private string _sortColumn = "Name";
    private bool _sortDescending;

    /// <summary>抑制"改属性 → 回写设置"的开关（批量灌值时用，避免无谓写盘与递归）。</summary>
    private bool _syncing;

    public ObservableCollection<PackageInfo> Packages { get; } = new();
    public ObservableCollection<PackageSourceOption> SourceOptions { get; } = new();

    // ==================== 页面状态 ====================

    [ObservableProperty] private bool _isSupported = true;
    [ObservableProperty] private string _supportText = "正在检测包管理器……";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _modeText = "尚未加载 —— 点「📥 已安装」或直接联网搜索";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _installedOnly;
    [ObservableProperty] private bool _silentInstall = true;
    [ObservableProperty] private bool _storeSourceEnabled = true;
    [ObservableProperty] private PackageInfo? _selected;
    [ObservableProperty] private PackageSourceOption? _selectedSource;
    [ObservableProperty] private string _detailExtra = "";

    // ==================== 表头（点一下切换排序） ====================

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _idHeader = "Id";
    [ObservableProperty] private string _versionHeader = "版本";
    [ObservableProperty] private string _sourceHeader = "源";
    [ObservableProperty] private string _statusHeader = "状态";

    // ==================== 列显隐（右键表头增删列，规范 3.8） ====================
    private const string PageKey = "pc-package";
    private readonly IColumnLayoutService? _cols;

    [ObservableProperty] private bool _isNameVisible = true;
    [ObservableProperty] private bool _isIdVisible = true;
    [ObservableProperty] private bool _isVersionVisible = true;
    [ObservableProperty] private bool _isSourceVisible = true;
    [ObservableProperty] private bool _isStatusVisible = true;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。</summary>
    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "3*"),
        new("Id", "Id", "4*"),
        new("Version", "版本", "1.4*"),
        new("Source", "源", "1.2*"),
        new("Status", "状态", "1.6*"),
    };

    /// <summary>列宽规格串（可绑定）：可见列写星号宽度，隐藏列写 "0"（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串而不是 <c>ColumnDefinitions</c>：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，
    /// Avalonia XAML 不允许在它上面写 {Binding}（编译期 AVLN3000），故由 <c>ColumnGrid</c> 控件解析（规范 3.8.5）。</summary>
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
        "Name" => IsNameVisible,
        "Id" => IsIdVisible,
        "Version" => IsVersionVisible,
        "Source" => IsSourceVisible,
        "Status" => IsStatusVisible,
        _ => true,
    };

    /// <summary>右键表头勾选 / 取消勾选某列（增删表头项）。</summary>
    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        switch (key)
        {
            case "Name": IsNameVisible = !IsNameVisible; break;
            case "Id": IsIdVisible = !IsIdVisible; break;
            case "Version": IsVersionVisible = !IsVersionVisible; break;
            case "Source": IsSourceVisible = !IsSourceVisible; break;
            case "Status": IsStatusVisible = !IsStatusVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));   // 列宽规格串随之刷新（隐藏列变 0 / 恢复）
    }

    /// <summary>把当前列显隐偏好落盘（config\columnlayout.json，会被数据管理一起备份）。</summary>
    private void SaveColumnVisibility()
    {
        if (_cols == null) return;
        _cols.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Name"] = IsNameVisible, ["Id"] = IsIdVisible, ["Version"] = IsVersionVisible,
            ["Source"] = IsSourceVisible, ["Status"] = IsStatusVisible,
        });
    }

    /// <summary>进入页面时读回上次保存的列显隐偏好（未保存过的列保持默认「可见」）。</summary>
    private void LoadColumnVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(PageKey);
        if (saved.TryGetValue("Name", out var n)) IsNameVisible = n;
        if (saved.TryGetValue("Id", out var id)) IsIdVisible = id;
        if (saved.TryGetValue("Version", out var v)) IsVersionVisible = v;
        if (saved.TryGetValue("Source", out var src)) IsSourceVisible = src;
        if (saved.TryGetValue("Status", out var st)) IsStatusVisible = st;
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Packages.Count > 0;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PackageManagerViewModel() { }

    public PackageManagerViewModel(IPackageManagerService pkg, IPackageSettingsService settings, IColumnLayoutService cols)
    {
        _pkg = pkg;
        _settings = settings;
        _cols = cols;

        LoadColumnVisibility();
        _syncing = true;
        var s = settings.Settings;
        _sortColumn = string.IsNullOrWhiteSpace(s.SortColumn) ? "Name" : s.SortColumn;
        _sortDescending = s.SortDescending;
        SilentInstall = s.SilentInstall;
        StoreSourceEnabled = s.StoreSourceEnabled;
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

    /// <summary>源下拉 = 「全部（已启用的）」+ 各已启用源；被关掉的 Store 源不出现。</summary>
    private async Task LoadSourcesAsync()
    {
        if (_pkg == null) return;

        var (ok, _, items) = await _pkg.ListSourcesAsync();

        SourceOptions.Clear();
        SourceOptions.Add(new PackageSourceOption { Label = "全部源（已启用的）", Value = null });

        if (ok)
        {
            foreach (var s in items)
            {
                if (!StoreSourceEnabled && IsStore(s.Name)) continue;
                SourceOptions.Add(new PackageSourceOption { Label = s.Name, Value = s.Name });
            }
        }
        else SourceOptions.Add(new PackageSourceOption { Label = "winget", Value = "winget" });

        var want = _settings?.Settings.DefaultSource ?? "";
        _syncing = true;
        SelectedSource = SourceOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", want, StringComparison.OrdinalIgnoreCase))
                         ?? SourceOptions[0];
        _syncing = false;
    }

    /// <summary>Store 源判定：winget 的商店源固定叫 msstore（大小写不敏感）。</summary>
    private static bool IsStore(string name)
        => name.Contains("msstore", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>点表头：同一列再点一次 = 反向；换列 = 升序。排序是"点一下就看到结果"的轻设置 → 立即落盘。</summary>
    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;

        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase))
            _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }

        if (_settings != null)
        {
            _settings.Settings.SortColumn = _sortColumn;
            _settings.Settings.SortDescending = _sortDescending;
            _settings.Save();
        }
        ApplyView();
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

        var byVersion = Comparer<string>.Create(CompareVersion);
        var byText = StringComparer.CurrentCultureIgnoreCase;

        q = _sortColumn switch
        {
            "Id" => _sortDescending ? q.OrderByDescending(p => p.Id, StringComparer.OrdinalIgnoreCase)
                                    : q.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
            "Version" => _sortDescending ? q.OrderByDescending(p => p.Version, byVersion)
                                         : q.OrderBy(p => p.Version, byVersion),
            "Source" => _sortDescending ? q.OrderByDescending(p => p.Source, byText)
                                        : q.OrderBy(p => p.Source, byText),
            "Status" => _sortDescending ? q.OrderByDescending(p => p.StatusText, byText)
                                        : q.OrderBy(p => p.StatusText, byText),
            _ => _sortDescending ? q.OrderByDescending(p => p.Name, byText)
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
        var arrow = _sortDescending ? " ▼" : " ▲";
        NameHeader = "名称" + (_sortColumn == "Name" ? arrow : "");
        IdHeader = "Id" + (_sortColumn == "Id" ? arrow : "");
        VersionHeader = "版本" + (_sortColumn == "Version" ? arrow : "");
        SourceHeader = "源" + (_sortColumn == "Source" ? arrow : "");
        StatusHeader = "状态" + (_sortColumn == "Status" ? arrow : "");
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

    // 注：StoreSourceEnabled 在本页只是**只读快照**（用来过滤源下拉），
    // 开关本身归「设置 → 软件源」页管 —— 所以这里刻意没有 OnStoreSourceEnabledChanged 回写，
    // 避免两个页面同时写同一个设置项（双真相源，见规范 3.9）。
}
