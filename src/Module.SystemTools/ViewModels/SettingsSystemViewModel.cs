using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.SystemTools.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>「系统工具设置」页（settings-system，）：本模块配置的唯一写入点。（WHY: ST-034）</summary>
public partial class SettingsSystemViewModel : ObservableObject, IStatusProvider
{
    private readonly ISystemToolsSettingsService? _settings;
    private readonly ISystemCommandStore? _store;
    private readonly ConfigStoreService? _cfg;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _timeoutText = "";

    public SettingsSystemViewModel() { }

    public SettingsSystemViewModel(ISystemToolsSettingsService settings, ISystemCommandStore store, ConfigStoreService cfg)
    {
        _settings = settings;
        _store = store;
        _cfg = cfg;
        TimeoutText = settings.CommandTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string ConfigPath => _settings?.FilePath ?? "";

    public string DataFilePath => _store?.FilePath ?? "";

    public string SystemDirText => _cfg is null ? "" : System.IO.Path.Combine(_cfg.ConfigDir, "system");

    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>超时失焦提交（非法值 ⇒ 状态栏红字 + ⛔ 不写盘）。</summary>
    [RelayCommand]
    private void CommitTimeout()
    {
        if (_settings is null) return;
        var raw = (TimeoutText ?? "").Trim();
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 5 || v > 300)
        {
            StatusText = "⚠ 超时要是 5~300 的整数（没写盘）。";
            TimeoutText = _settings.CommandTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        try
        {
            _settings.CommandTimeoutSeconds = v;
            StatusText = "已保存：命令超时" + v + " 秒。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ " + ex.Message;
        }
    }

    /// <summary>打开本模块的数据目录（Explorer）。</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var dir = SystemDirText;
        try
        {
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { StatusText = "⚠ 打开目录失败：" + ex.Message; }
    }
}
