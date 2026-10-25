using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.ViewModels;

public partial class PackageViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public PackageViewModel() { }

    public PackageViewModel(IPackageManagerService pkg, IPackageSettingsService settings, IColumnLayoutService cols, IAppConfigService app, IOptionsStore? opts = null)
        : base(cols, opts)
    {
        _app = app;
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
            await LoadExplicitSourcesAsync();
        }
        catch (Exception ex)
        {
            IsSupported = false;
            SupportText = "检测包管理器时出错：" + ex.Message;
        }
    }

    /// <summary>
    /// 读系统 winget 的**显式源**名单（<c>winget source list</c> 的「显式」列）。
    /// <para>🔴 v3.80：源选择框已删 ⇒ 这里只留搜索需要的那点信息 —— 显式源（如 <c>winget-font</c>）
    /// 不参与默认搜索，所以「搜全部源」时要单独补查一遍，否则永远搜不到。</para>
    /// </summary>
    private async Task LoadExplicitSourcesAsync()
    {
        if (_pkg == null) return;

        var (ok, _, items) = await _pkg.ListSourcesAsync();

        _explicitSources.Clear();
        if (!ok) return;
        foreach (var s in items)
            if (s.Explicit) _explicitSources.Add(s.Name);
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

            // v3.80：源选择框已删 ⇒ 固定「全部源」行为：
            //   ① 先不传 --source（winget 用自己的默认源集合，自动排除显式源）；
            //   ② 再把系统里的**显式源**（如 winget-font）逐个补查合并 —— 否则字体源永远搜不到。
            var merged = new List<PackageInfo>();
            var errs = new List<string>();
            var queried = 0;

            var (ok0, msg0, items0) = await _pkg.SearchAsync(SearchText, null);
            queried++;
            if (ok0) merged.AddRange(items0); else errs.Add("默认源集合: " + msg0);

            foreach (var name in _explicitSources)
            {
                var (ok, msg, items) = await _pkg.SearchAsync(SearchText, name);
                queried++;
                if (ok) { StampSource(items, name); merged.AddRange(items); }
                else errs.Add($"{name}: {msg}");
            }

            // 同一个包可能同时出现在默认源集合与显式源里（按 源 + Id 去重）
            var deduped = merged
                .GroupBy(p => (p.Source ?? "") + "|" + p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            BeginList(deduped, ListMode.Search);
            ApplyView();
            StatusText = deduped.Count == 0
                ? "没有匹配的包" + (errs.Count > 0 ? "｜" + string.Join("；", errs) : "")
                : $"找到 {deduped.Count} 个包（已查 {queried} 个源，含全部默认源）";

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
            // v3.19：明确「已 Pin 的不出现在可升级列表」是 winget 行为、非漏报（需求 §6.2.3）。
            StatusText = $"{msg} · 已 Pin 的不会出现在此列表（winget 行为）";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _installedIds = null;
        _installedList = null;
        switch (_mode)
        {
            case ListMode.Search: await SearchAsync(); break;
            case ListMode.Upgrades: await LoadUpgradesAsync(); break;
            case ListMode.Pinned: await LoadPinnedAsync(); break;   // 按当前列表重跑
            default: await LoadInstalledAsync(); break;
        }
    }

    private void BeginList(List<PackageInfo> items, ListMode mode)
    {
        // v3.37：旧行先解绑勾选监听，新行挂上（⛔ 别直接 AddRange，否则计数不更新）
        foreach (var p in _all) p.PropertyChanged -= OnRowPropertyChanged;
        _all.Clear();
        foreach (var p in items) p.PropertyChanged += OnRowPropertyChanged;
        _all.AddRange(items);
        _mode = mode;
        ModeText = mode switch
        {
            ListMode.Search => $"搜索结果：{SearchText}",
            ListMode.Installed => "已安装（本机全部包）",
            ListMode.Upgrades => "可升级",
            ListMode.Pinned => "已锁定（winget pin list）",
            _ => "尚未加载"
        };
        if (mode != ListMode.Search && SearchText.Length > 0) SearchText = "";
        RefreshChecked();
    }

    /// <summary>搜索结果没有"是否已安装"的信息 → 用已安装集合补上，让「状态」列说实话。</summary>
    private async Task MarkInstalledAsync()
    {
        if (_pkg == null) return;
        if (_installedIds == null)
        {
            var (ok, _, items) = await _pkg.ListInstalledAsync();
            if (ok)
            {
                _installedIds = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
                _installedList = items.ToList();      // 同一次调用的完整记录（截断 Id 修复要用 Name / Version）
            }
        }
        if (_installedIds == null) return;

        var known = new HashSet<string>(_all.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var p in _all) p.IsInstalled = IsInstalledId(p.Id);
        await ApplyPinnedAsync();

        // 🔴 查询词本身是个**残缺 Id**（如 `edg`）时，补一条"已安装版本"的真相：
        //    搜索结果的 Id 来自本地化显示名截断（`Microsoft Edg` → `e`），用它对不上 `winget list`
        //    的完整 Id（`Microsoft.Edge`）。这里按「Id 前缀 + 名称前缀」认出它，版本改用已装版本。
        if (await ResolveTruncatedIdAsync(known)) ApplyView();

        // 承接方式（IsInstalledId）：Id 互相包含，或前 4 字符 Jaro 式贴合 —— 只解决"名字截断导致 Id 残缺"这一种。
        bool IsInstalledId(string id)
        {
            if (_installedIds.Contains(id)) return true;
            return id.Length >= 3 && KeyName(id).Length >= 3 && _installedIds.Any(inst =>
                inst.Contains(id, StringComparison.OrdinalIgnoreCase)
                || (KeyName(id) == KeyName(inst)));
        }
    }

    /// <summary>
    /// 把「被截断的解析结果」修回完整的已安装 Id（见 <see cref="MarkInstalledAsync"/> 的说明）。
    /// <para>只在**当前列表里确实存在装不上的残缺 Id** 时触发一次 <c>winget list</c>，不影响正常搜索。</para>
    /// </summary>
    /// <returns>真改了任何行 → true（调用方需要重算列表）。</returns>
    private async Task<bool> ResolveTruncatedIdAsync(HashSet<string> knownIds)
    {
        if (_pkg == null || _installedList == null) return false;
        var candidates = _all
            .Where(p => !_installedIds!.Contains(p.Id) && p.Id.Length >= 1 && KeyName(p.Name).Length >= 2)
            .ToList();
        if (candidates.Count == 0) return false;

        var changed = false;
        foreach (var p in candidates)
        {
            var nameKey = KeyName(p.Name);
            var inst = _installedList.FirstOrDefault(i =>
                // ⚠️ 残缺 Id（`e` / `edg`）必须**紧贴**完整 Id 的头部 —— 放宽到任意子串会把 `7zip` 认成 `Microsoft.Something.Zip`。
                (p.Id.Length <= 4
                    ? i.Id.StartsWith(p.Id, StringComparison.OrdinalIgnoreCase)
                    : (i.Id.StartsWith(p.Id, StringComparison.OrdinalIgnoreCase)
                       || i.Id.Contains("." + p.Id + ".", StringComparison.OrdinalIgnoreCase)))
                && KeyName(i.Name).StartsWith(nameKey, StringComparison.OrdinalIgnoreCase));

            if (inst == null) continue;

            // 同名同源可能真有多个包（如两个「Microsoft Edge」）⇒ 目标 Id 已在列表里就不合并
            if (knownIds.Contains(inst.Id)) continue;

            knownIds.Remove(p.Id);
            knownIds.Add(inst.Id);

            p.Id = inst.Id;
            if (!string.IsNullOrWhiteSpace(inst.Name)) p.Name = inst.Name;
            p.Version = inst.Version;
            p.Available = "";
            p.Source = inst.Source;
            p.IsInstalled = true;
            changed = true;
        }
        return changed;
    }

    /// <summary>比较用的名字键：只留字母数字、转小写，用来对齐被截断的显示名与完整名称。</summary>
    private static string KeyName(string? s)
        => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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

    /// <summary>
    /// 📌 Pin 列表（v3.22，需求 §6.2.3）：跑 `winget pin list`，把结果**并入 `winget list` 的已安装列表**一起显示。
    /// <list type="bullet">
    ///   <item>不在已安装列表上时，先跑一次 <see cref="LoadInstalledAsync"/>（拿 `winget list` 当底子）；</item>
    ///   <item>pin 项已在列表里 → 贴 📌（`IsPinned = true`）；不在 → 追加成一行（pin list 只给包名，版本/名称可能空）。</item>
    /// </list>
    /// ⛔ 本页**没有「仅看 Pin」勾选框**（v3.22 去掉：过滤语义说不清，改成"把 pin 项加进列表"这个真命令）。
    /// </summary>
    [RelayCommand]
    private async Task LoadPinnedAsync()
    {
        if (_pkg == null || IsBusy) return;

        try
        {
            IsBusy = true;
            StatusText = "正在读取 Pin 列表（winget pin list）……";
            var (ok, msg, pinned) = await _pkg.ListPinnedAsync();
            if (!ok) { StatusText = "读取 Pin 列表失败：" + msg; return; }

            // 🔴 点「🔒 已锁定」= **换一个列表**（⛔ 不清空就是"把 pin 项塞进上一次的结果"，
            // 用户看不出哪些才是被锁定的）。pin list 只给 Id / 版本 ⇒ 再跑一次 winget list 补齐名称 / 源 / 状态。
            var ids = pinned.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = new List<PackageInfo>();
            var missed = 0;

            if (ids.Count > 0)
            {
                var (okList, _, installed) = await _pkg.ListInstalledAsync();
                if (okList)
                {
                    foreach (var p in installed)
                    {
                        if (!ids.Contains(p.Id)) continue;
                        p.IsPinned = true;
                        p.PinnedVersion = pinned.FirstOrDefault(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))?.PinnedVersion ?? "";
                        rows.Add(p);
                    }
                }
                foreach (var p in pinned)
                    if (!rows.Any(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        // 已安装列表里没有（离线 / 解析差异）⇒ 仍给一行，至少 Id 和锁定的版本是对的
                        rows.Add(new PackageInfo { Id = p.Id, Version = p.Version, PinnedVersion = p.PinnedVersion, IsPinned = true });
                        missed++;
                    }
            }

            BeginList(rows, ListMode.Pinned);     // 清掉原有列表，只留被锁定的
            ApplyView();

            StatusText = ids.Count == 0
                ? "没有被 Pin 的包（winget pin list 为空）。"
                : $"已锁定 {rows.Count} 个包（winget pin list）；" + (missed > 0 ? $"{missed} 个在已安装列表里没匹配到，只显示 Id。" : "");
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedChanged(PackageInfo? value)
    {
        DetailExtra = "";
        OnPropertyChanged(nameof(DetailItem));
        OnPropertyChanged(nameof(HasSelection));
        NotifyActionVisibility();
    }

    private void ApplyView()
    {
        IEnumerable<PackageInfo> q = _all;

        if (InstalledOnly && _mode == ListMode.Search)
            q = q.Where(p => p.IsInstalled);

        // v3.19：可升级列表**不显示已 Pin 的**（winget 会跳过它们 —— 需求 §6.2.3；也让它更贴近终端 winget upgrade 的条数）
        if (_mode == ListMode.Upgrades)
            q = q.Where(p => !p.IsPinned);

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

        // ---- 分页切片（数据已在内存 ⇒ 只切不回查；选「全部」= 不切） ----
        var ordered = q.ToList();
        TotalCount = ordered.Count;

        var maxPage = Math.Max(1, PageCount);          // 用基类口径（AllPageSize 下 = 1）
        if (PageIndex > maxPage)
        {
            PageIndex = maxPage;                       // setter → OnPageIndexChanged → RefreshView ⇒ 再来一次（⛔ 不在这里继续）
            return;
        }

        var page = PageSize >= AllPageSize
            ? ordered
            : ordered.Skip((PageIndex - 1) * PageSize).Take(PageSize).ToList();

        _syncing = true;                                  // 重建集合期间不要再触发回写
        try
        {
            Packages.Clear();
            foreach (var p in page) Packages.Add(p);
        }
        finally { _syncing = false; }

        if (Selected != null && !Packages.Contains(Selected)) Selected = null;

        UpdateHeaders();
        RefreshChecked();                         // v3.37：列表重建 → 勾选计数 / 表头三态跟上
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        NotifyActionVisibility();                 // 列表重建可能把选中项换掉 → 动作组合跟着重算
    }

    /// <summary>
    /// 详情卡动作组合的可见 / 可用性（安装 vs 升级、Pin vs 取消 Pin、卸载）都派生自 <see cref="Selected"/>，
    /// 选中变化或忙碌态变化时统一通知（⛔ 别散落各处）。
    /// </summary>
    private void NotifyActionVisibility()
    {
        OnPropertyChanged(nameof(ShowInstallAction));
        OnPropertyChanged(nameof(ShowUpgradeAction));
        OnPropertyChanged(nameof(CanUpgradeSelected));
        OnPropertyChanged(nameof(ShowPinAction));
        OnPropertyChanged(nameof(ShowUnpinAction));
        OnPropertyChanged(nameof(ShowUninstallAction));
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        IdHeader = "Id" + SortArrow("Id");
        VersionHeader = "版本" + SortArrow("Version");
        SourceHeader = "源" + SortArrow("Source");
        StatusHeader = "状态" + SortArrow("Status");
        PinnedHeader = "锁定" + SortArrow("Pinned");
    }

    /// <summary>版本比较：能解析成 <see cref="System.Version"/> 就按版本比（1.10 &gt; 1.9），否则退回字符串比较。</summary>
    private static int CompareVersion(string a, string b)
        => System.Version.TryParse(a, out var va) && System.Version.TryParse(b, out var vb)
            ? va.CompareTo(vb)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

}
