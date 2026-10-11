using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewToys.Settings.ViewModels;

public partial class DataViewModel : ObservableObject, NewToys.Shared.Services.IStatusProvider
{
    public ObservableCollection<ConfigFileDisplay> ConfigFiles { get; } = new();

    [ObservableProperty] private string _statusText = "就绪";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    [ObservableProperty] private string _lastBackupPath = "";
    [ObservableProperty] private bool _isBusy;

    public DataViewModel()
    {
        RefreshFileList();
    }

    [RelayCommand]
    private void RefreshFileList()
    {
        // 真正的文件枚举由 DataView.axaml.cs 在加载时调用 Refresh 命令完成；
        // 这里先清空展示，等待 code-behind 填充。
        ConfigFiles.Clear();
        StatusText = $"配置列表已刷新 · {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void BackupNow()
    {
        StatusText = "请点击「备份到文件」按钮选择保存位置。";
    }

    [RelayCommand]
    private void ImportNow()
    {
        StatusText = "请点击「从备份导入」按钮选择备份文件。";
    }
}

public partial class ConfigFileDisplay : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private DateTime _lastWrite;
    public string SizeDisplay
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:0.0} KB";
            return $"{SizeBytes / 1024.0 / 1024.0:0.1} MB";
        }
    }
}
