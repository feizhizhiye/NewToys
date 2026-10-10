using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.PcTools.ViewModels;

/// <summary>
/// 系统信息 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class PcInfoViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "系统信息";
    [ObservableProperty] private string _description = "硬件、操作系统与运行环境概览";
    [ObservableProperty] private string _primaryButtonText = "重新检测";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public PcInfoViewModel()
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
