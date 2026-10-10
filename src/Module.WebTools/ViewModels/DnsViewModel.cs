using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.WebTools.ViewModels;

public partial class DnsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "DNS 检测";
    [ObservableProperty] private string _description = "检测当前系统 DNS 配置与连通性";
    [ObservableProperty] private string _targetDomain = "www.baidu.com";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DnsResultItem> Results { get; } = new();

    public DnsViewModel() { }

    [RelayCommand]
    private async System.Threading.Tasks.Task TestDns()
    {
        IsBusy = true;
        Results.Clear();
        // DNS 真实查询逻辑后续补丁实现；这里仅占位
        await System.Threading.Tasks.Task.Delay(300);
        Results.Add(new DnsResultItem { DnsServer = "119.29.29.29 (DNSPod)", Status = "（待接入实现）", LatencyMs = 0 });
        StatusText = "DNS 检测完成（功能待接入）。";
        IsBusy = false;
    }
}

public partial class DnsResultItem : ObservableObject
{
    [ObservableProperty] private string _dnsServer = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _latencyMs;
}
