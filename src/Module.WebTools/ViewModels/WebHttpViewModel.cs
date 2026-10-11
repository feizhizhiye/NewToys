using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// HTTP 测试 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class WebHttpViewModel : ObservableObject, NewToys.Shared.Services.IStatusProvider
{
    [ObservableProperty] private string _title = "HTTP 测试";
    [ObservableProperty] private string _description = "发送 HTTP 请求并查看响应";
    [ObservableProperty] private string _primaryButtonText = "发送请求";
    [ObservableProperty] private string _statusText = "就绪";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    [ObservableProperty] private bool _isBusy;

    public WebHttpViewModel()
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
