using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 「软件源」设置页 —— 软件包管理模块的源设置都在这。
/// <para>
/// <b>落点</b>：<c>&lt;应用根&gt;\config\package.json</c>。放在 <c>config\</c> 下的唯一目的就是
/// **能被「数据管理」页备份 / 导入**（"设置里的数据可以备份，模块读取设置里的数据"，规范 3.9）；
/// 模块侧（PC 工具箱 → 软件包管理）只通过 <see cref="IPackageSettingsService"/> 读，不自己另存。
/// </para>
/// <para>
/// <b>全部即时生效、无保存按钮</b>（规范 3.7）：勾一下开关就写盘，状态行立刻反馈。
/// </para>
/// <para>
/// ⚠️ <b>刻意不提供"增删源"</b>：那要改系统级的包管理器配置（<c>winget source add/remove/reset</c>），
/// 属于会动到机器全局状态的破坏性操作。本页只做两件安全的事：
/// ① 读出现有源；② 决定**本应用查询时**用哪些源（关掉 Store 源 = 我们的搜索不再带 <c>--source msstore</c>）。
/// 想真正增删源，请自己在终端里用 winget 命令。
/// </para>
/// </summary>
public partial class PackageSourceViewModel : ObservableObject
{
    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;

    private readonly List<PackageSourceInfo> _allSources = new();
    private bool _syncing;

    /// <summary>界面上显示的源（<c>IsEnabled</c> 已按「是否启用 Store 源」算好）。</summary>
    public ObservableCollection<PackageSourceInfo> Sources { get; } = new();

    /// <summary>「默认查询源」下拉的选项（第一项 = 全部已启用的源）。</summary>
    public ObservableCollection<PackageSourceOption> SourceOptions { get; } = new();

    [ObservableProperty] private bool _storeSourceEnabled = true;
    [ObservableProperty] private bool _silentInstall = true;
    [ObservableProperty] private PackageSourceOption? _defaultSourceOption;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "正在读取包管理器源……";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _summaryText = "";

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
            var s = _settings.Settings;
            StoreSourceEnabled = s.StoreSourceEnabled;
            SilentInstall = s.SilentInstall;
            _syncing = false;

            StatusText = "正在读取包管理器源列表……";
            var (ok, msg, items) = await _pkg.ListSourcesAsync();

            _allSources.Clear();
            _allSources.AddRange(items);
            Rebuild();

            if (!ok) StatusText = "读取源列表失败：" + msg;
            else if (_allSources.Count == 0) StatusText = "winget 未返回任何源（可能尚未初始化，可执行一次任意 winget 命令让它自动生成）。";
            else StatusText = $"系统里共 {_allSources.Count} 个源；本应用当前查询 {EnabledSourceCount()} 个。";
        }
        finally { IsBusy = false; }
    }

    private int EnabledSourceCount()
        => StoreSourceEnabled ? _allSources.Count : _allSources.Count(x => !IsStore(x.Name));

    /// <summary>按「是否启用 Store 源」重算列表与下拉（只读快照，不写盘）。</summary>
    private void Rebuild()
    {
        _syncing = true;
        try
        {
            Sources.Clear();
            foreach (var s in _allSources)
            {
                s.IsEnabled = StoreSourceEnabled || !IsStore(s.Name);
                Sources.Add(s);
            }

            var keep = DefaultSourceOption?.Value ?? _settings?.Settings.DefaultSource ?? "";
            SourceOptions.Clear();
            SourceOptions.Add(new PackageSourceOption { Label = "全部源（已启用的）", Value = null });
            foreach (var s in _allSources.Where(x => StoreSourceEnabled || !IsStore(x.Name)))
                SourceOptions.Add(new PackageSourceOption { Label = s.Name, Value = s.Name });

            DefaultSourceOption = SourceOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", keep, StringComparison.OrdinalIgnoreCase))
                                  ?? SourceOptions[0];
        }
        finally { _syncing = false; }

        SummaryText = $"系统里 {_allSources.Count} 个源 · 本应用启用 {EnabledSourceCount()} 个";
        OnPropertyChanged(nameof(SummaryText));
    }

    private static bool IsStore(string name) => name.Contains("msstore", StringComparison.OrdinalIgnoreCase);

    // ==================== 即时生效（写 package.json） ====================

    partial void OnStoreSourceEnabledChanged(bool value)
    {
        if (_syncing || _settings == null) return;
        _settings.Settings.StoreSourceEnabled = value;
        _settings.Save();
        Rebuild();
        StatusText = value
            ? "已启用 Store 源：搜索 / 安装可命中 msstore（Microsoft Store 分发）的包。"
            : "已关闭 Store 源：本应用的搜索与安装**不再查询 msstore**。已写入 package.json，即时生效。";
    }

    partial void OnSilentInstallChanged(bool value)
    {
        if (_syncing || _settings == null) return;
        _settings.Settings.SilentInstall = value;
        _settings.Save();
        StatusText = value
            ? "安装 / 升级将追加 --silent（静默，不弹安装器界面）。已写入 package.json。"
            : "安装 / 升级会保留安装器界面（可看到许可协议等）。已写入 package.json。";
    }

    partial void OnDefaultSourceOptionChanged(PackageSourceOption? value)
    {
        if (_syncing || _settings == null || value == null) return;
        _settings.Settings.DefaultSource = value.Value ?? "";
        _settings.Save();
        StatusText = value.Value == null
            ? "默认查询源 = 全部已启用的源（搜索时逐个源查一遍再合并）。"
            : $"默认查询源 = {value.Value}（搜索与安装都会优先用它）。";
    }

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
