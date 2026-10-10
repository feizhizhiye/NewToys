using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 书签管理 页面 ViewModel。
/// 当前为页面骨架：业务逻辑后续补丁逐步实现。
/// </summary>
public partial class BookmarkViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "书签管理";
    [ObservableProperty] private string _description = "批量检测书签可用性与导入导出";
    [ObservableProperty] private string _primaryButtonText = "检测全部";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public BookmarkViewModel()
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
