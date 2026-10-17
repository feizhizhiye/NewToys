using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「脚本管理器」模块设置页（v3.64 / NT-061，需求 §4.9）：View/VM 归 <c>Module.ScriptManager</c>，
/// 由 <c>ScriptManagerModule.Initialize</c> 自注册进**本模块分组 <c>pc</c>**（Key <c>settings-script</c>，Order 100）。
/// <para>
/// 内容 = 本模块的**脚本目录**（<c>config\script-manager.json</c>）——「脚本库」页工具条那个框只是同一份值的另一个视图。
/// </para>
/// <para>
/// 🔴 v3.65 / NT-062：三个解释器路径（pwsh / python / powershell 5.1）**已回迁应用级**
/// （「设置 → 路径与配置」页，<c>app.json</c>），⛔ 本页不再有那三行。
/// </para>
/// <para>
/// 生效方式（规范 §4.9）：路径 = **即时生效**（失焦即落盘，⛔ 无保存按钮）；无效路径 ⇒ 底栏红字 + ⛔ 不写盘。
/// </para>
/// </summary>
public partial class SettingsScriptViewModel : ObservableObject, IStatusProvider
{
    private readonly IScriptManagerSettingsService _settings = null!;

    /// <summary>灌值时抑制「改属性 → 回写」（防递归 + 无谓写盘）。</summary>
    private bool _syncing;

    public SettingsScriptViewModel() { }

    public SettingsScriptViewModel(IScriptManagerSettingsService settings, PackageSourceViewModel? packageSource = null)
    {
        _settings = settings;
        PackageSource = packageSource;
        _syncing = true;
        ScriptsFolder = settings.ScriptsFolder;
        _syncing = false;
        StatusText = $"本模块设置就绪 · 写入 {settings.FilePath}";

        // v3.65 / NT-062：内嵌的「软件源」（原独立设置页）被 Shell 看到的只有本页 VM
        // ⇒ 它的状态变更要**转发**过来，否则软件源的操作结果不会出现在底部状态栏。
        if (packageSource != null)
            packageSource.StatusChanged += (_, _) => StatusText = packageSource.StatusText;
    }

    /// <summary>
    /// v3.65 / NT-062：并入本页的「软件源」（原 Key <c>settings-package</c> 已从菜单移除）——
    /// View 侧用 <c>&lt;v:PackageSourceView DataContext="{Binding PackageSource}" /&gt;</c> 内嵌。
    /// </summary>
    public PackageSourceViewModel? PackageSource { get; }

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
