using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.PcTools.ViewModels;

public partial class QuickSettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "电脑工具快捷设置";
    [ObservableProperty] private string _description = "电脑工具模块的常用配置项";
    [ObservableProperty] private string _primaryButtonText = "保存设置";
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public QuickSettingsViewModel() { }

    [RelayCommand]
    private void PrimaryAction()
    {
        StatusText = "设置已保存。";
    }

    [RelayCommand]
    private void Refresh()
    {
        StatusText = $"已刷新 · {DateTime.Now:HH:mm:ss}";
    }
}
