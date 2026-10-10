using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 网络工具 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class WebHomeViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "网络工具";
    [ObservableProperty] private string _description = "HTTP/Ping/书签/DNS/抓取等网络工具";
    [ObservableProperty] private string _primaryButtonText = "打开工具";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public WebHomeViewModel()
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
