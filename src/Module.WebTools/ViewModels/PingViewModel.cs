using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// Ping 检测 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class PingViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "Ping 检测";
    [ObservableProperty] private string _description = "检测主机连通性与延迟";
    [ObservableProperty] private string _primaryButtonText = "开始 Ping";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public PingViewModel()
    {
    }

    [RelayCommand]
    private void PrimaryAction()
    {
        StatusText = "主操作已触发，功能将在后续版本实现。";
    }

    [RelayCommand]
    private void Refresh()
    {
        StatusText = $"已刷新 · {DateTime.Now:HH:mm:ss}";
    }
}
