using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 格式转码 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class TranscodeViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "格式转码";
    [ObservableProperty] private string _description = "视频格式转换与压缩";
    [ObservableProperty] private string _primaryButtonText = "开始转码";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public TranscodeViewModel()
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
