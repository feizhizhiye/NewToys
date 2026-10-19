using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「软件源」设置 —— 内嵌在「脚本管理器设置」页里（原 <c>settings-package</c> 页已并入）。
/// <para>
/// <b>本页直接操作系统 winget 的源</b>（与系统共用一套数据）：列表读 <c>winget source list</c> 的真实结果，
/// 在这里改完，终端里敲 <c>winget source list</c> 看到的一模一样。
/// </para>
/// <para>
/// <b>分类（v3.79）</b>：社区源（<c>winget</c> 及镜像）/ 字体源（<c>winget-font</c> 及镜像）/
/// 商店源（<c>msstore</c>）/ 自定义源。**只有社区源、字体源两档内部互斥**（同时最多启用一个）。
/// </para>
/// <para>
/// <b>开关语义（v3.79 重写）</b>：winget 1.29 实测**没有 <c>source enable|disable</c>**
/// ⇒ 关 = <c>source remove</c>（先把名字 / 地址 / 类型 / 显式标记记进 <c>package.json</c>），
/// 开 = 按记下的定义 <c>source add</c> 加回来。两个方向都要**管理员权限**（会弹 UAC）。
/// 🔴 商店源 <c>msstore</c> **不可切换**（用户定调）：只显示状态，开关置灰。
/// </para>
/// <para>全部即时生效、无保存按钮：拨一下开关 / 点一下按钮就执行，状态行立刻反馈。</para>
/// </summary>
public partial class PackageSourceViewModel : ObservableObject
{
    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;

    /// <summary>抑制「灌初值 → 触发开关」的递归。</summary>
    private bool _syncing;

    /// <summary>系统 winget 里**当前启用**的源（<c>winget source list</c> 的原始结果）。</summary>
    public ObservableCollection<PackageSourceInfo> Sources { get; } = new();

    /// <summary>界面显示的源行（含「系统里已移除、但定义还记着」的兜底行）。</summary>
    public ObservableCollection<PackageSourceRow> Rows { get; } = new();

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _statusText = "正在读取系统 winget 的源……";

    /// <summary>接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）。</summary>
    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>给界面用的反向属性（<c>{Binding IsNotBusy}</c>）。</summary>
    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [ObservableProperty] private string _filePath = "";

    [ObservableProperty] private string _summaryText = "";

    // ---- 添加源表单（点「➕ 添加」才提交） ----

    [ObservableProperty] private string _newSourceName = "";
    [ObservableProperty] private string _newSourceArgument = "";

    /// <summary>「添加源」表单里的**分类**：决定这个源跟谁互斥。</summary>
    [ObservableProperty] private string _newSourceCategory = PackageSources.CategoryCustom;

    /// <summary>分类下拉的可选项（社区源 / 字体源 / 商店源 / 自定义源）。</summary>
    public IReadOnlyList<string> CategoryOptions => PackageSources.AllCategories;

    public PackageSourceViewModel() { }

    public PackageSourceViewModel(IPackageManagerService pkg, IPackageSettingsService settings)
    {
        _pkg = pkg;
        _settings = settings;
        FilePath = settings.FilePath;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    // ==================== 读 + 重建 ====================

    private async Task LoadAsync()
    {
        if (_pkg == null || _settings == null) { StatusText = "服务未就绪。"; return; }

        try
        {
            IsBusy = true;
            _syncing = true;   // 下面 Rebuild() 会自己复位；这里只是挡住期间任何开关回调

            StatusText = "正在读取系统 winget 的源列表……";
            var (ok, msg, items) = await _pkg.ListSourcesAsync();

            Sources.Clear();
            foreach (var s in items) Sources.Add(s);

            // 定义快照（source export）：关掉某个源之后还能按原样加回来
            await MergeDefinitionsAsync();

            Rebuild();

            if (!ok) StatusText = "读取源列表失败：" + msg;
            else if (Sources.Count == 0) StatusText = "winget 未返回任何源（可能尚未初始化，可先执行一次任意 winget 命令，或点「↺ 恢复默认源」）。";
            else StatusText = $"系统 winget 里共 {Sources.Count} 个源（本页与系统共用同一份数据）。";
        }
        catch (Exception ex)
        {
            // 服务层设计上不抛（内部全 catch），真抛了也让用户看到原因（⛔ 不静默）
            StatusText = "读取源列表出错：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 把 <c>winget source export</c> 的定义并进设置（源名 → 地址 / 类型 / 显式）。
    /// <para>这是「关掉还能开回来」的唯一依据 —— winget 自己记不住被 remove 掉的源。</para>
    /// </summary>
    private async Task MergeDefinitionsAsync()
    {
        if (_pkg == null || _settings == null) return;

        var (ok, _, defs) = await _pkg.ExportSourceDefinitionsAsync();
        if (!ok) return;

        var map = _settings.Settings.SourceDefinitions ??= new Dictionary<string, PackageSourceDefinition>();
        var changed = false;
        foreach (var (name, def) in defs)
        {
            if (map.TryGetValue(name, out var old)
                && string.Equals(old.Argument, def.Argument, StringComparison.OrdinalIgnoreCase)
                && string.Equals(old.Type, def.Type, StringComparison.OrdinalIgnoreCase)
                && old.Explicit == def.Explicit) continue;

            map[name] = def;
            changed = true;
        }
        if (changed) _settings.Save();
    }

    /// <summary>设置里存的**自定义源分类**（源名 → 分类）；没存过 ⇒ <c>null</c>（按地址猜）。</summary>
    private string? StoredCategory(string name)
    {
        var map = _settings?.Settings.SourceCategories;   // 手改过的 package.json 可能把它写成 null
        return map != null && map.TryGetValue(name, out var c) ? c : null;
    }

    private void AddRow(PackageSourceRow row)
    {
        row.Toggled += OnRowToggled;
        Rows.Add(row);
    }

    /// <summary>重建源行：先系统里**启用中**的源，再「定义还记着但当前没启用」的兜底行（开关在关位）。</summary>
    private void Rebuild()
    {
        _syncing = true;
        try
        {
            Rows.Clear();
            foreach (var s in Sources) AddRow(new PackageSourceRow(s, enabled: true, category: StoredCategory(s.Name)));

            foreach (var def in KnownDefinitions())
            {
                AddRow(new PackageSourceRow(new PackageSourceInfo
                {
                    Name = def.Name,
                    Argument = def.Argument,
                    Type = def.Type,
                    Explicit = def.Explicit
                }, enabled: false, category: StoredCategory(def.Name)));
            }
        }
        finally { _syncing = false; }

        SummaryText = $"{Sources.Count} 个已启用 · 共 {Rows.Count} 个源";
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    /// 「系统里没有、但我们知道定义」的源：设置里记过的 ∪ 内置源的默认定义。
    /// <para>它们补一行（开关在关位），拨开 = <c>winget source add</c> 加回来。</para>
    /// </summary>
    private IEnumerable<PackageSourceDefinition> KnownDefinitions()
    {
        var map = _settings?.Settings.SourceDefinitions ?? new Dictionary<string, PackageSourceDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Sources) seen.Add(s.Name);      // 已启用的不再补行

        foreach (var builtin in new[] { PackageSources.MainName, PackageSources.FontName, PackageSources.StoreName })
        {
            var name = builtin;
            var def = map.TryGetValue(name, out var stored) && !string.IsNullOrWhiteSpace(stored.Argument)
                ? stored
                : PackageSources.DefaultDefinition(name);
            if (def == null || !seen.Add(def.Name)) continue;
            yield return def;
        }

        foreach (var (name, def) in map)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Argument)) continue;
            if (!seen.Add(name)) continue;
            def.Name = name;
            yield return def;
        }
    }

    /// <summary>某个源「关掉之后」还能用来加回来的定义。</summary>
    private PackageSourceDefinition? DefinitionOf(PackageSourceRow row)
    {
        var map = _settings?.Settings.SourceDefinitions;
        if (map != null && map.TryGetValue(row.Name, out var stored) && !string.IsNullOrWhiteSpace(stored.Argument))
            return stored;

        if (!string.IsNullOrWhiteSpace(row.Argument))
            return new PackageSourceDefinition
            {
                Name = row.Name,
                Argument = row.Argument,
                Type = row.Info.Type,
                Explicit = row.Info.Explicit
            };

        return PackageSources.DefaultDefinition(row.Name);
    }

    /// <summary>把定义写进设置（关掉源之前先留一份，⛔ 不依赖 export 是否成功）。</summary>
    private void SaveDefinition(PackageSourceDefinition? def)
    {
        if (_settings == null || def == null || string.IsNullOrWhiteSpace(def.Argument)) return;

        var map = _settings.Settings.SourceDefinitions ??= new Dictionary<string, PackageSourceDefinition>();
        if (map.TryGetValue(def.Name, out var old)
            && string.Equals(old.Argument, def.Argument, StringComparison.OrdinalIgnoreCase)
            && string.Equals(old.Type, def.Type, StringComparison.OrdinalIgnoreCase)
            && old.Explicit == def.Explicit) return;

        map[def.Name] = def;
        _settings.Save();
    }

    // ==================== 开关（拨一下立刻生效） ====================

    /// <summary>开关被拨动（<see cref="PackageSourceRow.Toggled"/>）⇒ 立刻执行。</summary>
    private void OnRowToggled(PackageSourceRow row)
    {
        if (_applying || _syncing) return;
        _ = ToggleRowAsync(row);
    }

    private bool _applying;

    /// <summary>把开关拨回原位（⛔ 不再次触发 <see cref="PackageSourceRow.Toggled"/>）。</summary>
    private void RevertRow(PackageSourceRow row)
    {
        _applying = true;
        row.IsEnabled = row.WasEnabled;
        _applying = false;
    }

    [RelayCommand]
    private async Task ToggleRowAsync(PackageSourceRow? row)
    {
        if (_pkg == null || row == null) return;

        if (!row.CanToggle)
        {
            RevertRow(row);
            StatusText = "商店源（msstore）由 winget 统一管理，不能在这里启用 / 停用；丢了就点「↺ 恢复默认源」。";
            return;
        }

        if (IsBusy)
        {
            RevertRow(row);
            StatusText = "正在处理上一个操作，请稍候。";
            return;
        }

        try
        {
            IsBusy = true;
            var (ok, msg) = row.IsEnabled ? await TurnOnAsync(row) : await TurnOffAsync(row);
            StatusText = msg;
            // 成功 ⇒ 重读（开关回到与系统一致）；失败 ⇒ 只重建，把开关拨回原位
            if (ok) await LoadAsync(); else Rebuild();
        }
        catch (Exception ex)
        {
            StatusText = "改源出错：" + ex.Message;
            Rebuild();
        }
        finally { IsBusy = false; }
    }

    /// <summary>开 = 按记下的定义 <c>winget source add</c>（要管理员权限），然后处理同分类互斥。</summary>
    private async Task<(bool Ok, string Msg)> TurnOnAsync(PackageSourceRow row)
    {
        var def = DefinitionOf(row);
        if (def == null) return (false, $"没有记下源 {row.Name} 的地址，无法加回来 —— 请用「添加源」重新添加。");

        var (ok, msg) = await _pkg!.SetSourceEnabledAsync(row.Name, true, def);
        if (!ok) return (false, msg);

        var (ok2, extra) = await EnforceExclusiveAsync(row);
        return (ok2, $"已启用源 {row.Name}" + extra);
    }

    /// <summary>关 = <c>winget source remove</c>（先把定义记下来，随时能加回）。</summary>
    private async Task<(bool Ok, string Msg)> TurnOffAsync(PackageSourceRow row)
    {
        var def = DefinitionOf(row);
        SaveDefinition(def);
        return await _pkg!.SetSourceEnabledAsync(row.Name, false, def);
    }

    /// <summary>
    /// **互斥**：<paramref name="keep"/> 启用之后，把**同分类**里其它已启用的源关掉
    /// —— 社区源之间（官方 ↔ 镜像）、字体源之间各算一档。商店源 / 自定义源不参与。
    /// </summary>
    private async Task<(bool Ok, string Msg)> EnforceExclusiveAsync(PackageSourceRow keep)
    {
        if (!keep.IsExclusive) return (true, "");

        var victims = Rows.Where(r => !ReferenceEquals(r, keep)
                                      && r.IsEnabled
                                      && r.CanToggle
                                      && string.Equals(r.Category, keep.Category, StringComparison.OrdinalIgnoreCase))
                          .ToList();
        if (victims.Count == 0) return (true, "");

        var done = new List<string>();
        var errs = new List<string>();
        foreach (var v in victims)
        {
            var (ok, msg) = await TurnOffAsync(v);
            if (ok) done.Add(v.Name); else errs.Add($"{v.Name}：{msg}");
        }

        var text = (done.Count > 0 ? $"｜同分类（{keep.Category}）已自动关掉 {string.Join(" / ", done)}" : "")
                 + (errs.Count > 0 ? "｜失败：" + string.Join("；", errs) : "");
        return (errs.Count == 0, text);
    }

    // ==================== 添加源 / 恢复默认 ====================

    /// <summary>添加源（<c>winget source add</c>，需要管理员权限）。名称 + 地址必填。</summary>
    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (_pkg == null || IsBusy) return;

        var name = (NewSourceName ?? "").Trim();
        var arg = (NewSourceArgument ?? "").Trim();
        if (name.Length == 0) { StatusText = "请先填写源名称（例如 my-source）。"; return; }
        if (arg.Length == 0) { StatusText = "请先填写源地址（URL 或以 \\\\ 开头的 UNC 路径）。"; return; }

        var category = PackageSources.NormalizeCategory(NewSourceCategory);
        // 字体源一律加成「显式源」：它不参与默认搜索，只有勾选时才查（与微软自己的 winget-font 一致）
        var explicitSource = string.Equals(category, PackageSources.CategoryFont, StringComparison.Ordinal);

        try
        {
            IsBusy = true;
            StatusText = $"正在添加源 {name} ……（会弹 UAC）";
            var (ok, msg) = await _pkg.AddSourceAsync(name, arg, PackageSources.GuessType(arg), explicitSource);

            if (ok)
            {
                // 分类是**本应用的概念**（winget 没有）⇒ 存 package.json；它决定这个源跟谁互斥。
                if (_settings != null)
                {
                    _settings.Settings.SourceCategories ??= new Dictionary<string, string>();
                    _settings.Settings.SourceCategories[name] = category;
                    _settings.Save();
                }

                NewSourceName = "";
                NewSourceArgument = "";
                await LoadAsync();

                // 互斥：同分类里已有启用的源（如内置 winget）⇒ 自动关掉它。
                var added = Rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                if (added != null)
                {
                    var (_, extra) = await EnforceExclusiveAsync(added);
                    if (extra.Length > 0)
                    {
                        StatusText = msg + " " + extra;
                        await LoadAsync();
                        return;
                    }
                }
            }

            StatusText = msg;
        }
        finally { IsBusy = false; }
    }

    /// <summary>恢复默认源（<c>winget source reset --force</c>，内部二次确认 + UAC）。</summary>
    [RelayCommand]
    private async Task ResetSourcesAsync()
    {
        if (_pkg == null || IsBusy) return;

        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.ResetSourcesAsync();
            StatusText = msg;
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }
}
