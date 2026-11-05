using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>「脚本管理器」模块设置页（，需求 §4.9）：View/VM 归 Module.ScriptManager， 由 ScriptManagerModule.Initialize 自注册进本模块分组 pc（Key settings-script，Order 100）。（WHY: SM-071）</summary>
public partial class SettingsScriptViewModel : ObservableObject, IStatusProvider
{
    private readonly IScriptManagerSettingsService _settings = null!;

    /// <summary>灌值时抑制「改属性 → 回写」（防递归 + 无谓写盘）。</summary>
    private bool _syncing;

    public SettingsScriptViewModel() { }

    public SettingsScriptViewModel(IScriptManagerSettingsService settings)
    {
        _settings = settings;

        _syncing = true;
        // 先让生效值跑一遍旧布局自愈（<配置根>\data\… → store\ / cache\），再取用户值显示，
        // 否则文本框里会留着一个已经不存在的旧路径。
        _ = settings.EffectiveScriptsFolder;
        ScriptsFolder = settings.ScriptsFolder;
        _syncing = false;
        StatusText = $"本模块设置就绪 · 写入 {settings.FilePath}";
    }

    // ==================== 路径（脚本目录，即时生效） ====================

    [ObservableProperty] private string _scriptsFolder = "";

    partial void OnScriptsFolderChanged(string value)
    {
        if (_syncing) return;
        StatusText = _settings.SetScriptsFolder(value);
        OnPropertyChanged(nameof(ScriptsFolderEffective));
        OnPropertyChanged(nameof(IsDefaultScriptsFolder));
    }

    // ==================== 只读展示 ====================

    /// <summary>脚本目录**生效值**（留空时 = 默认目录）。</summary>
    public string ScriptsFolderEffective => _settings?.EffectiveScriptsFolder ?? "";

    /// <summary>脚本目录的默认值（<c>&lt;应用根&gt;\scripts</c>，多配置共享）。</summary>
    public string DefaultScriptsFolder => _settings?.DefaultScriptsFolder ?? "";

    public bool IsDefaultScriptsFolder => string.IsNullOrWhiteSpace(ScriptsFolder);

    /// <summary>本模块设置文件全路径（页脚展示，方便手改 / 备份）。</summary>
    public string ConfigFilePath => _settings?.FilePath ?? "";

    // ==================== 动作 ====================

    [RelayCommand]
    private async Task PickScriptsFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择脚本目录", _settings.EffectiveScriptsFolder);
        if (string.IsNullOrWhiteSpace(dir)) return;
        ScriptsFolder = dir.Trim();     // 触发 OnScriptsFolderChanged → 校验 + 落盘 + 状态文案
    }

    /// <summary>脚本目录恢复默认（清空用户值 ⇒ 用 <c>&lt;应用根&gt;\scripts</c>）。</summary>
    [RelayCommand]
    private void ResetScriptsFolder() => ScriptsFolder = "";

    // ==================== 状态栏（IStatusProvider） ====================

    [ObservableProperty] private string _statusText = "";

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;
}
