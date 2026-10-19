using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「软件源」设置 —— 软件包模块的源设置都在这（NT-050：由 Module.Settings 迁入 Module.ScriptManager，
/// View/VM 归模块）。
/// <para>
/// 🔴 v3.65 / NT-062：本页**并入「脚本管理器设置」页**（需求 §6.5）—— 原独立菜单项
/// <c>settings-package</c> **已移除**，View 以子控件形式内嵌在 <c>SettingsScriptView</c> 里，
/// 数据仍落 <c>config\package.json</c>。
/// </para>
/// <b>v1.33 起本页直接操作系统 winget 的源</b>（"与系统共用一套数据"）：
/// 列表读的是 <c>winget source list</c> 的真实结果，「停用」= <c>winget source remove</c>，
/// 「添加源」= <c>winget source add</c>，「恢复默认源」= <c>winget source reset --force</c>。
/// 也就是说：在这里改完，用户在终端里敲 <c>winget source list</c> 看到的是一模一样的东西。
/// </para>
/// <para>
/// ⚠️ 这是**会动到整台机器**的操作（不是"只影响本应用"）。删除源 / 恢复默认都走 Core 的二次确认钩子
/// （<c>PackageManagerService.ConfirmHandler</c> → Shell 的模态框），与卸载软件包同一套机制。
/// </para>
/// <para>
/// <b>落点</b>：<c>&lt;应用根&gt;\config\package.json</c> —— 但那里只存"界面偏好"
/// （默认查询源 / 静默安装），源的增删不落本地文件（真相源就是系统 winget）。
/// 放 <c>config\</c> 下是为了能被「数据管理」页备份 / 导入（规范 3.9）。
/// </para>
/// <para>
/// <b>全部即时生效、无保存按钮</b>（规范 3.7）：勾一下开关 / 点一下按钮就执行，状态行立刻反馈。
/// </para>
/// </summary>
public partial class PackageSourceViewModel : ObservableObject, IStatusProvider
{
    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;

    private bool _syncing;

    /// <summary>系统 winget 里的源（`winget source list` 的原始结果）。</summary>
    public ObservableCollection<PackageSourceInfo> Sources { get; } = new();

    /// <summary>界面显示的源行（带种类标签 / 操作语义）。</summary>
    public ObservableCollection<PackageSourceRow> Rows { get; } = new();

    /// <summary>
    /// 「本应用默认查询源」的**多选**选项（v3.78）。
    /// <para>与软件包页工具条的源多选框是**同一个设置项**（逗号分隔存 <c>package.json</c> 的
    /// <c>defaultSource</c>），这里全都勾 = 那边的「全部源」。</para>
    /// </summary>
    public ObservableCollection<PackageSourceOption> QuerySources { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "正在读取系统 winget 的源……";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>给界面用的反向属性（<c>{Binding IsEnabled}</c> 绑它，Avalonia 的 <c>!</c> 前缀在模板里不好用）。</summary>
    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _summaryText = "";

    // ---- 添加源表单（点「➕ 添加」才提交） ----

    [ObservableProperty] private string _newSourceName = "";
    [ObservableProperty] private string _newSourceArgument = "";

    /// <summary>「添加源」表单里的**分类**（NT-076）：决定这个源跟谁互斥。</summary>
    [ObservableProperty] private string _newSourceCategory = PackageSources.CategoryCustom;

    /// <summary>分类下拉的可选项（NT-076）。</summary>
    public IReadOnlyList<string> CategoryOptions => PackageSources.AllCategories;

    /// <summary>抑制"拨开关 → 又触发一次拨开关"的递归（拨回原位时用）。</summary>
    private bool _applying;

    // ==================== 镜像源切换（v3.77） ====================

    /// <summary>微软官方 winget 源地址（添加源输入框的水印用它当使用指引）。</summary>
    public static string MicrosoftSourceUrl => PackageSources.MainUrl;

    /// <summary>中科大 winget 镜像源地址。</summary>
    public static string UstcSourceUrl => PackageSources.UstcUrl;

    /// <summary>切换动的是系统里那个名叫 <c>winget</c> 的源（名字不变 ⇒ 搜索 / 安装不用跟着改）。</summary>
    private static string MainSourceName => PackageSources.MainName;

    /// <summary>当前 <c>winget</c> 源指向中科大镜像？</summary>
    public bool IsUstcMirror => Sources.Any(s => string.Equals(s.Name, MainSourceName, StringComparison.OrdinalIgnoreCase)
                                                 && s.Argument.Contains("mirrors.ustc.edu.cn", StringComparison.OrdinalIgnoreCase));

    /// <summary>切换按钮文案（按当前状态来回切）。</summary>
    public string MirrorButtonText => IsUstcMirror ? "↔ 切回微软官方" : "↔ 切到中科大镜像";

    /// <summary>切换按钮下面那行说明。</summary>
    public string MirrorStatusText => IsUstcMirror
        ? $"当前 {MainSourceName} 源 = 中科大镜像（{UstcSourceUrl}）—— 国内通常更快"
        : $"当前 {MainSourceName} 源 = 微软官方（{MicrosoftSourceUrl}）";

    // ==================== 商店源 msstore（v3.78） ====================

    /// <summary>系统 winget 里有没有启用商店源（<c>msstore</c>）。</summary>
    public bool HasStoreSource => Sources.Any(s => PackageSources.IsStore(s.Name));

    /// <summary>商店源当前状态的一行说明。</summary>
    public string StoreStatusText => HasStoreSource
        ? $"已启用 —— 在「软件包」页勾上 {PackageSources.StoreName}，就能搜到 Microsoft Store 里的应用。"
        : $"未启用 —— 系统 winget 里没有 {PackageSources.StoreName} 源，商店里的应用搜不到、也装不了。";

    /// <summary>开关按钮的文案（按当前状态来回切）。</summary>
    public string StoreButtonText => HasStoreSource ? "⏻ 停用商店源" : "✅ 启用商店源";

    /// <summary>
    /// 启用 / 停用商店源（v3.78）：优先 <c>winget source enable|disable</c>（winget 1.9+，⛔ 不删源定义）；
    /// 老版本回退成 <c>remove</c>（带二次确认）/ <c>add</c>（用官方地址加回）。
    /// </summary>
    [RelayCommand]
    private async Task ToggleStoreAsync()
    {
        if (_pkg == null || IsBusy) return;

        try
        {
            IsBusy = true;
            StatusText = HasStoreSource ? "正在停用商店源 msstore ……" : "正在启用商店源 msstore ……";
            var (ok, msg) = await _pkg.SetSourceEnabledAsync(PackageSources.StoreName, !HasStoreSource);
            StatusText = msg;
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 源列表右侧开关：**拨一下立刻生效**（NT-076 —— ⛔ 本页没有「生效」按钮）。
    /// <para>
    /// 开 = 内置源 <c>source enable</c>（已经在系统里的源不用动）；
    /// 关 = 内置源 <c>source disable</c> / 自定义源 <c>source remove</c>（带二次确认）。
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ToggleRowAsync(PackageSourceRow? row)
    {
        if (_pkg == null || row == null) return;

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

    /// <summary>开关被拨动（<see cref="PackageSourceRow.Toggled"/>）⇒ 立刻执行。</summary>
    private void OnRowToggled(PackageSourceRow row)
    {
        if (_applying || _syncing) return;
        _ = ToggleRowAsync(row);
    }

    /// <summary>把开关拨回原位（⛔ 不再次触发 <see cref="PackageSourceRow.Toggled"/>）。</summary>
    private void RevertRow(PackageSourceRow row)
    {
        _applying = true;
        row.IsEnabled = row.WasEnabled;
        _applying = false;
    }

    /// <summary>开：内置源 <c>source enable</c>（已在系统里则不动），然后处理同分类互斥。</summary>
    private async Task<(bool Ok, string Msg)> TurnOnAsync(PackageSourceRow row)
    {
        var inSystem = Sources.Any(s => string.Equals(s.Name, row.Name, StringComparison.OrdinalIgnoreCase));

        if (!inSystem)
        {
            if (!row.IsBuiltin)
                return (false, $"源 {row.Name} 不在系统 winget 里 —— 请到下面「添加源」重新添加。");

            var (ok, msg) = await _pkg!.SetSourceEnabledAsync(row.Name, true);
            if (!ok) return (ok, msg);
        }

        var (ok2, extra) = await EnforceExclusiveAsync(row);
        return (ok2, (inSystem ? $"源 {row.Name} 已在系统 winget 里启用" : $"已启用源 {row.Name}") + extra);
    }

    /// <summary>关：内置源 <c>source disable</c> / 自定义源 <c>source remove</c>。</summary>
    private async Task<(bool Ok, string Msg)> TurnOffAsync(PackageSourceRow row)
        => row.IsBuiltin
            ? await _pkg!.SetSourceEnabledAsync(row.Name, false)
            : await _pkg!.RemoveSourceAsync(row.Name);

    /// <summary>
    /// **互斥**（NT-076）：<paramref name="keep"/> 启用之后，把**同分类**里其它已启用的源关掉 ——
    /// 这就是「添加的镜像源与内置源 <c>winget</c> 互斥」的落点（切镜像 = 开一个、自动关掉另一个）。
    /// <para>返回给状态栏的补充文案；没有竞争对手 ⇒ 空串。</para>
    /// </summary>
    private async Task<(bool Ok, string Msg)> EnforceExclusiveAsync(PackageSourceRow keep)
    {
        if (!keep.IsExclusive) return (true, "");

        var victims = Rows.Where(r => !ReferenceEquals(r, keep)
                                      && r.IsEnabled
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

    /// <summary>
    /// 把系统里的 <c>winget</c> 源在「微软官方 ↔ 中科大镜像」之间来回切（v3.77）：
    /// <c>winget source remove winget</c> → <c>winget source add winget &lt;地址&gt;</c>（名字不变）。
    /// <para>⚠️ 会动整台机器的 winget 配置 ⇒ 删除走 Core 的二次确认钩子；中途失败会提示点「↺ 恢复默认源」。</para>
    /// </summary>
    [RelayCommand]
    private async Task SwitchMirrorAsync()
    {
        if (_pkg == null || IsBusy) return;

        var toUstc = !IsUstcMirror;
        var url = toUstc ? UstcSourceUrl : MicrosoftSourceUrl;

        try
        {
            IsBusy = true;
            StatusText = toUstc ? "正在把 winget 源切到中科大镜像……" : "正在把 winget 源切回微软官方……";

            var (removed, rmsg) = await _pkg.RemoveSourceAsync(MainSourceName);
            if (!removed) { StatusText = "移除旧源失败，已中止（源未改动）：" + rmsg; return; }

            var (ok, msg) = await _pkg.AddSourceAsync(MainSourceName, url);
            StatusText = ok ? msg : msg + "（winget 源已被移除，请点「↺ 恢复默认源」）";
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// **另存一份**中科大镜像源（名叫 <c>winget-ustc</c>）：⛔ 不动原来的 winget 源，
    /// 想两头都在就点它（软件包页的源多选框里会多出这一项）。
    /// </summary>
    [RelayCommand]
    private async Task AddUstcSourceAsync()
    {
        if (_pkg == null || IsBusy) return;

        try
        {
            IsBusy = true;
            StatusText = "正在添加中科大镜像源 winget-ustc ……";
            var (ok, msg) = await _pkg.AddSourceAsync("winget-ustc", UstcSourceUrl);
            StatusText = msg;
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

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

    private async Task LoadAsync()
    {
        if (_pkg == null || _settings == null) { StatusText = "服务未就绪。"; return; }

        try
        {
            IsBusy = true;
            _syncing = true;

            StatusText = "正在读取系统 winget 的源列表……";
            var (ok, msg, items) = await _pkg.ListSourcesAsync();

            Sources.Clear();
            foreach (var s in items) Sources.Add(s);
            Rebuild();

            if (!ok) StatusText = "读取源列表失败：" + msg;
            else if (Sources.Count == 0) StatusText = "winget 未返回任何源（可能尚未初始化，可先执行一次任意 winget 命令，或点「↺ 恢复默认源」）。";
            else StatusText = $"系统 winget 里共 {Sources.Count} 个源（本页与系统共用同一份数据）。";
        }
        catch (Exception ex)
        {
            // 防御：服务层设计上不抛（内部全 catch），真抛了也让用户看到原因（⛔ 不静默）
            StatusText = "读取源列表出错：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>设置里存的**自定义源分类**（源名 → 分类）；没存过 ⇒ <c>null</c>（按地址猜）。</summary>
    private string? StoredCategory(string name)
    {
        var map = _settings?.Settings.SourceCategories;   // 手改过的 package.json 可能把它写成 null
        return map != null && map.TryGetValue(name, out var c) ? c : null;
    }

    /// <summary>加一行并挂上「开关拨动 ⇒ 立即生效」的回调。</summary>
    private void AddRow(PackageSourceRow row)
    {
        row.Toggled += OnRowToggled;
        Rows.Add(row);
    }

    /// <summary>重建源行 + 「默认查询源」多选项（源列表变了就跟着变）。</summary>
    private void Rebuild()
    {
        _syncing = true;
        try
        {
            Rows.Clear();
            foreach (var s in Sources) AddRow(new PackageSourceRow(s, enabled: true, category: StoredCategory(s.Name)));

            // NT-074：内置源（winget / msstore）系统里没启用时补一行 —— 开关在「关」位，
            // 打开它 = winget source enable（NT-076：拨一下立刻生效，⛔ 不用再点「生效」）。
            foreach (var builtin in new[] { PackageSources.MainName, PackageSources.StoreName })
            {
                if (Sources.Any(x => string.Equals(x.Name, builtin, StringComparison.OrdinalIgnoreCase))) continue;
                AddRow(new PackageSourceRow(new PackageSourceInfo
                {
                    Name = builtin,
                    Argument = PackageSources.IsStore(builtin) ? PackageSources.StoreUrl : PackageSources.MainUrl,
                    Type = ""
                }, enabled: false));
            }

            // 本应用默认查询源：与软件包页共用 package.json 的 defaultSource（逗号分隔）
            var keep = (_settings?.Settings.DefaultSource ?? "")
                       .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

            QuerySources.Clear();
            foreach (var s in Sources)
                QuerySources.Add(new PackageSourceOption
                {
                    Label = s.Name,
                    Value = s.Name,
                    IsSelected = keep.Count == 0 || keep.Contains(s.Name, StringComparer.OrdinalIgnoreCase)
                });
        }
        finally { _syncing = false; }

        SummaryText = $"系统里 {Sources.Count} 个源";
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsUstcMirror));
        OnPropertyChanged(nameof(MirrorButtonText));
        OnPropertyChanged(nameof(MirrorStatusText));
        OnPropertyChanged(nameof(HasStoreSource));
        OnPropertyChanged(nameof(StoreStatusText));
        OnPropertyChanged(nameof(StoreButtonText));
    }

    /// <summary>
    /// 勾一下「默认查询源」就写盘（v3.78）：全勾 / 全不勾都等价于「全部源」，
    /// 与软件包页的源多选框共用**同一个设置项**（<c>package.json → defaultSource</c>）。
    /// </summary>
    [RelayCommand]
    private void SaveQuerySources(PackageSourceOption? opt)
    {
        if (_syncing || _settings == null) return;   // 宿主已经把 IsSelected 翻好了 ⇒ 直接读全部

        var picked = QuerySources.Where(o => o.IsSelected && !string.IsNullOrEmpty(o.Value))
                                 .Select(o => o.Value!).ToList();
        var all = QuerySources.Count(o => !string.IsNullOrEmpty(o.Value));

        _settings.Settings.DefaultSource = picked.Count >= all ? "" : string.Join(",", picked);
        _settings.Save();

        StatusText = picked.Count >= all || picked.Count == 0
            ? "默认查询源 = 全部源（「软件包」页打开时全部勾上）。"
            : $"默认查询源 = {string.Join(" / ", picked)}（「软件包」页打开时按这个勾）。";
    }

    // ==================== 源的增删（改的就是系统 winget） ====================

    /// <summary>添加源（<c>winget source add</c>）。名称 + 地址必填。</summary>
    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (_pkg == null || IsBusy) return;

        var name = (NewSourceName ?? "").Trim();
        var arg = (NewSourceArgument ?? "").Trim();
        if (name.Length == 0) { StatusText = "请先填写源名称（例如 my-source）。"; return; }
        if (arg.Length == 0) { StatusText = "请先填写源地址（URL 或以 \\\\ 开头的 UNC 路径）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = $"正在添加源 {name} ……";
            var (ok, msg) = await _pkg.AddSourceAsync(name, arg);

            if (ok)
            {
                // 分类是**本应用的概念**（winget 没有）⇒ 存 package.json；它决定这个源跟谁互斥。
                if (_settings != null)
                {
                    _settings.Settings.SourceCategories ??= new Dictionary<string, string>();
                    _settings.Settings.SourceCategories[name] = PackageSources.NormalizeCategory(NewSourceCategory);
                    _settings.Save();
                }

                NewSourceName = "";
                NewSourceArgument = "";
                await LoadAsync();

                // 互斥：同分类里已有启用的源（内置 winget / msstore）⇒ 自动关掉它。
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

    /// <summary>删除某个源（<c>winget source remove</c>，内部二次确认）。行内「✕ 停用」按钮传的就是这一行。</summary>
    [RelayCommand]
    private async Task RemoveSourceAsync(PackageSourceInfo? source)
    {
        if (_pkg == null || source == null || IsBusy) return;

        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.RemoveSourceAsync(source.Name);
            StatusText = msg;
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>恢复默认源（<c>winget source reset --force</c>，内部二次确认）。</summary>
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

    // ==================== 即时生效（写 package.json 的"界面偏好"） ====================

    // ==================== 动作按钮 ====================

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settings?.FilePath ?? "") ?? "";
            if (dir.Length == 0) { StatusText = "找不到配置文件目录。"; return; }
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "打开目录失败：" + ex.Message; }
    }
}
