using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 加密 DNS（DoH）页。
/// <para>
/// **开关 / 下拉即时生效，本页没有"应用"按钮**：写的是 <see cref="IAppConfigService"/>，
/// 而 <c>EncryptedDnsService</c> 每次建连都现场读配置，所以改完下一次请求就换了解析方式。
/// </para>
/// 自定义 URL 是文本框，但只有一项、且改完立刻能"测试解析"验证，因此同样即时写盘（不为它单独设保存按钮）。
/// </summary>
public partial class DnsViewModel : ObservableObject, IStatusProvider
{
    private readonly IAppConfigService? _cfg;
    private readonly IEncryptedDnsService? _dns;

    private bool _loading;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _providerIndex;
    [ObservableProperty] private string _customUrl = "";
    [ObservableProperty] private string _testHost = "www.baidu.com";
    [ObservableProperty] private string _statusText = "";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>下拉框数据源（与 <see cref="DohProviders.All"/> 同序同长，索引即 id）。</summary>
    public IReadOnlyList<string> Providers { get; } = DohProviders.All.Select(p => p.Name).ToList();

    /// <summary>只有选到「自定义」时才允许填 URL。</summary>
    public bool CustomEnabled => CurrentProvider.Id == "custom";

    /// <summary>当前生效的 DoH 端点（实时反映配置）。</summary>
    public string Endpoint => _dns?.Endpoint ?? "";

    private DohProviders.Provider CurrentProvider => DohProviders.All[Math.Clamp(ProviderIndex, 0, DohProviders.All.Count - 1)];

    public DnsViewModel() { }

    public DnsViewModel(IAppConfigService cfg, IEncryptedDnsService dns)
    {
        _cfg = cfg;
        _dns = dns;

        _loading = true;
        Enabled = cfg.EncryptedDnsEnabled;
        ProviderIndex = Math.Max(0, DohProviders.All.ToList().FindIndex(p =>
            string.Equals(p.Id, cfg.EncryptedDnsProviderId, StringComparison.OrdinalIgnoreCase)));
        CustomUrl = cfg.EncryptedDnsCustomUrl;
        _loading = false;

        RefreshStatus();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_loading || _cfg == null) return;
        _cfg.EncryptedDnsEnabled = value;   // 立即写 app.json；EncryptedDnsService 每次建连都现场读它
        RefreshStatus();
    }

    partial void OnProviderIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CustomEnabled));
        OnPropertyChanged(nameof(Endpoint));
        if (_loading || _cfg == null) return;
        _cfg.EncryptedDnsProviderId = CurrentProvider.Id;
        RefreshStatus();
    }

    partial void OnCustomUrlChanged(string value)
    {
        if (_loading || _cfg == null) return;
        _cfg.EncryptedDnsCustomUrl = value;
        RefreshStatus();
    }

    private void RefreshStatus()
        => StatusText = _dns?.Describe() ?? "加密 DNS 服务未就绪";

    /// <summary>用当前配置真解析一次，给用户一个"立刻能验证"的反馈。</summary>
    [RelayCommand]
    private async Task TestAsync()
    {
        if (_dns == null) { TestResult = "加密 DNS 服务未就绪。"; return; }

        IsBusy = true;
        TestResult = $"正在解析 {TestHost} …";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var (ok, msg, addresses) = await _dns.ResolveAsync(TestHost, cts.Token);

            TestResult = ok
                ? $"{msg}：{string.Join("，", addresses.Take(4))}{(addresses.Length > 4 ? " …" : "")}"
                : msg;
        }
        catch (Exception ex)
        {
            TestResult = "解析失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
            OnPropertyChanged(nameof(Endpoint));
        }
    }
}
