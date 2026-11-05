using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>「网页工具」模块设置页（，需求 §4.9）：View/VM 归 Module.WebTools， 由 WebToolsModule.Initialize 自注册进本模块分组 web（Key settings-web-tools，Order 100）。（WHY: WT-017）</summary>
public partial class SettingsWebToolsViewModel : ObservableObject, IStatusProvider
{
    private readonly IWebToolsSettingsService _settings = null!;
    private readonly HostsSourceService? _hostSources;

    /// <summary>灌值时抑制「改属性 → 回写」。</summary>
    private bool _syncing;

    public SettingsWebToolsViewModel() { }

    public SettingsWebToolsViewModel(IWebToolsSettingsService settings, HostsSourceService hostSources)
    {
        _settings = settings;
        _hostSources = hostSources;

        _syncing = true;
        DefaultUserAgent = settings.DefaultUserAgent;
        ScrapeTimeoutSeconds = settings.ScrapeTimeoutSeconds;
        HostsCheckTimeoutSeconds = settings.HostsCheckTimeoutSeconds;
        HostsCheckConcurrency = settings.HostsCheckConcurrency;
        HostsBackupKeep = settings.HostsBackupKeep;
        HostsFlushDns = settings.HostsFlushDnsAfterApply;
        _syncing = false;

        LoadHostSources();
        StatusText = $"本模块设置就绪 · 写入 {settings.FilePath}";
    }

    // ==================== Hosts 开源项目源 ====================

    /// <summary>hosts 源清单（预设 + 自定义；落本模块库 <c>web_docs/hosts-sources</c>）。</summary>
    public ObservableCollection<HostsSource> HostSources { get; } = new();

    [ObservableProperty] private HostsSource? _selectedHostSource;

    public bool HasHostSources => HostSources.Count > 0;

    public bool HasHostSourceSelection => SelectedHostSource != null;

    public string HostSourceHint => SelectedHostSource?.Hint ?? "还没有源：点「新建」填名称 + 地址（多个地址用空格分隔）。";

    partial void OnSelectedHostSourceChanged(HostsSource? value)
    {
        OnPropertyChanged(nameof(HostSourceHint));
        OnPropertyChanged(nameof(HasHostSourceSelection));
    }

    private void LoadHostSources()
    {
        HostSources.Clear();
        if (_hostSources is null) return;
        foreach (var s in _hostSources.Load()) HostSources.Add(s);
        SelectedHostSource = HostSources.FirstOrDefault();
        OnPropertyChanged(nameof(HasHostSources));
        OnPropertyChanged(nameof(HostSourceHint));
    }

    /// <summary>新建一个源（预设 / 自定义同格式；⛔ 不校验地址可达性，用「测试」验）。</summary>
    [RelayCommand]
    private async Task AddHostSourceAsync()
    {
        if (_hostSources is null) return;

        var name = await DialogHooks.InputAsync("新建 hosts 源", "开源项目名");
        if (string.IsNullOrWhiteSpace(name)) return;

        var urls = await DialogHooks.InputAsync("源地址（多个用空格分隔，第一个为主地址）", "https://");
        if (string.IsNullOrWhiteSpace(urls)) return;

        var source = HostsSourceService.NewCustom(name, urls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (source.Urls.Count == 0) { StatusText = "⚠ 没填有效地址，未添加。"; return; }

        HostSources.Add(source);
        _hostSources.Save(HostSources);
        SelectedHostSource = source;
        OnPropertyChanged(nameof(HasHostSources));
        StatusText = $"已添加源「{source.Name}」（{source.Urls.Count} 个地址）。去「Hosts 管理」页点「拉取」验证。";
    }

    /// <summary>改名称 / 地址（预设源也能改，改完仍是预设的「≥20 条」判定口径）。</summary>
    [RelayCommand]
    private async Task EditHostSourceAsync()
    {
        if (_hostSources is null || SelectedHostSource is not { } source) return;

        var name = await DialogHooks.InputAsync("源名称", source.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var urls = await DialogHooks.InputAsync("源地址（多个用空格分隔）", string.Join(' ', source.Urls));
        if (string.IsNullOrWhiteSpace(urls)) return;

        source.Name = name.Trim();
        source.Urls = urls.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        _hostSources.Save(HostSources);
        OnPropertyChanged(nameof(HostSourceHint));
        StatusText = $"已更新源「{source.Name}」（{source.Urls.Count} 个地址）。";
    }

    [RelayCommand]
    private async Task DeleteHostSourceAsync()
    {
        if (_hostSources is null || SelectedHostSource is not { } source) return;

        var ok = await DialogHooks.ConfirmAsync(
            $"删除源「{source.Name}」？\n" +
            (source.Builtin ? "这是随包预设：删掉后不会自动恢复（重启也不回来）。" : "只删源清单，已拉取出的方案不受影响。"));
        if (!ok) return;

        HostSources.Remove(source);
        _hostSources.Save(HostSources);
        SelectedHostSource = HostSources.FirstOrDefault();
        OnPropertyChanged(nameof(HasHostSources));
        StatusText = $"已删除源「{source.Name}」。";
    }

    /// <summary>试拉一次：验证地址可达 + 条目数（⛔ 不写方案、不动系统 hosts）。</summary>
    [RelayCommand]
    private async Task TestHostSourceAsync()
    {
        if (_hostSources is null || SelectedHostSource is not { } source) return;

        StatusText = $"正在测试「{source.Name}」…";
        var result = await _hostSources.FetchAsync(source);
        if (!result.Ok) { StatusText = "⚠ " + result.Message; return; }

        _hostSources.Save(HostSources);
        OnPropertyChanged(nameof(HostSourceHint));
        StatusText = result.Message;
    }

    // ==================== 抓取默认项（即时生效） ====================

    [ObservableProperty] private string _defaultUserAgent = "";
    [ObservableProperty] private int _scrapeTimeoutSeconds = WebToolsSettingsService.DefaultTimeout;

    partial void OnDefaultUserAgentChanged(string value)
    {
        if (_syncing) return;
        StatusText = _settings.SetDefaultUserAgent(value);
        OnPropertyChanged(nameof(EffectiveUserAgent));
    }

    partial void OnScrapeTimeoutSecondsChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetScrapeTimeoutSeconds(value);
    }

    // ==================== Hosts 检测与备份 ====================

    [ObservableProperty] private int _hostsCheckTimeoutSeconds = WebToolsSettingsService.DefaultHostsTimeout;
    [ObservableProperty] private int _hostsCheckConcurrency = WebToolsSettingsService.DefaultHostsConcurrency;
    [ObservableProperty] private int _hostsBackupKeep = WebToolsSettingsService.DefaultHostsKeep;
    [ObservableProperty] private bool _hostsFlushDns = true;

    partial void OnHostsCheckTimeoutSecondsChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetHostsCheckTimeoutSeconds(value);
    }

    partial void OnHostsCheckConcurrencyChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetHostsCheckConcurrency(value);
    }

    partial void OnHostsBackupKeepChanged(int value)
    {
        if (_syncing) return;
        StatusText = _settings.SetHostsBackupKeep(value);
    }

    partial void OnHostsFlushDnsChanged(bool value)
    {
        if (_syncing) return;
        StatusText = _settings.SetHostsFlushDnsAfterApply(value);
    }

    /// <summary>Hosts 各项的取值范围说明（与服务里的钳位口径一致）。</summary>
    public string HostsRangesHint =>
        $"检测超时 {WebToolsSettingsService.MinHostsTimeout}~{WebToolsSettingsService.MaxHostsTimeout} 秒 · " +
        $"并发 {WebToolsSettingsService.MinHostsConcurrency}~{WebToolsSettingsService.MaxHostsConcurrency} · " +
        $"备份保留 {WebToolsSettingsService.MinHostsKeep}~{WebToolsSettingsService.MaxHostsKeep} 份。";

    // ==================== 只读展示 ====================

    /// <summary>生效的 UA（留空时回落内置那一串）。</summary>
    public string EffectiveUserAgent => _settings?.EffectiveUserAgent ?? "";

    /// <summary>超时取值范围说明（与服务里的钳位口径一致）。</summary>
    public string TimeoutHint =>
        $"范围 {WebToolsSettingsService.MinTimeout}~{WebToolsSettingsService.MaxTimeout} 秒，" +
        $"默认 {WebToolsSettingsService.DefaultTimeout}。";

    /// <summary>本模块设置文件全路径。</summary>
    public string ConfigFilePath => _settings?.FilePath ?? "";

    /// <summary>默认 UA 恢复内置（清空用户值）。</summary>
    [RelayCommand]
    private void ResetUserAgent() => DefaultUserAgent = "";

    // ==================== 状态栏（IStatusProvider） ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
