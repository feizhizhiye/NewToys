using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 视频剪辑 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class VideoCutViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "视频剪辑";
    [ObservableProperty] private string _description = "快速裁剪视频片段";
    [ObservableProperty] private string _primaryButtonText = "添加视频";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public VideoCutViewModel()
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
