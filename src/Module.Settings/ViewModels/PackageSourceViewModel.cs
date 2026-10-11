using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 「软件源」设置页 —— 软件包管理模块的源设置都在这。
/// <para>
/// <b>v1.33 起本页直接操作系统 winget 的源</b>（"与系统共用一套数据"）：
/// 列表读的是 <c>winget source list</c> 的真实结果，「停用」= <c>winget source remove</c>，
/// 「添加源」= <c>winget source add</c>，「恢复默认源」= <c>winget source reset --force</c>。
/// 也就是说：在这里改完，用户在终端里敲 <c>winget source list</c> 看到的是一模一样的东西。
/// </para>
/// <para>
/// ⚠️ 这是**会动到整台机器**的操作（不是"只影响本应用"）。删除源 / 恢复默认都走 Core 的二次确认钩子
/// （<c>PackageManagerService.ConfirmHandler</c> → Shell 的模态框），与卸载软件包同一套机制。
/// </para>
/// <para>
/// <b>落点</b>：<c>&lt;应用根&gt;\config\package.json</c> —— 但那里只存"界面偏好"
/// （默认查询源 / 静默安装），源的增删不落本地文件（真相源就是系统 winget）。
/// 放 <c>config\</c> 下是为了能被「数据管理」页备份 / 导入（规范 3.9）。
/// </para>
/// <para>
/// <b>全部即时生效、无保存按钮</b>（规范 3.7）：勾一下开关 / 点一下按钮就执行，状态行立刻反馈。
/// </para>
/// </summary>
public partial class PackageSourceViewModel : ObservableObject, IStatusProvider
{
    private readonly IPackageManagerService? _pkg;
    private readonly IPackageSettingsService? _settings;

    private bool _syncing;

    /// <summary>系统 winget 里的源（`winget source list` 的原始结果）。</summary>
    public ObservableCollection<PackageSourceInfo> Sources { get; } = new();

    /// <summary>「默认查询源」下拉的选项（第一项 = 全部源）。</summary>
    public ObservableCollection<PackageSourceOption> SourceOptions { get; } = new();

    [ObservableProperty] private bool _silentInstall = true;
    [ObservableProperty] private PackageSourceOption? _defaultSourceOption;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "正在读取系统 winget 的源……";

    // v3.9：接入 Shell 底部状态栏契约（状态文本由 Shell 统一显示）
    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>给界面用的反向属性（<c>{Binding IsEnabled}</c> 绑它，Avalonia 的 <c>!</c> 前缀在模板里不好用）。</summary>
    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _summaryText = "";

    // ---- 添加源表单（点「➕ 添加」才提交） ----

    [ObservableProperty] private string _newSourceName = "";
    [ObservableProperty] private string _newSourceArgument = "";

    public PackageSourceViewModel() { }

    public PackageSourceViewModel(IPackageManagerService pkg, IPackageSettingsService settings)
    {
        _pkg = pkg;
        _settings = settings;
        FilePath = settings.FilePath;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        if (_pkg == null || _settings == null) { StatusText = "服务未就绪。"; return; }

        try
        {
            IsBusy = true;
            _syncing = true;
            SilentInstall = _settings.Settings.SilentInstall;
            _syncing = false;

            StatusText = "正在读取系统 winget 的源列表……";
            var (ok, msg, items) = await _pkg.ListSourcesAsync();

            Sources.Clear();
            foreach (var s in items) Sources.Add(s);
            Rebuild();

            if (!ok) StatusText = "读取源列表失败：" + msg;
            else if (Sources.Count == 0) StatusText = "winget 未返回任何源（可能尚未初始化，可先执行一次任意 winget 命令，或点「↺ 恢复默认源」）。";
            else StatusText = $"系统 winget 里共 {Sources.Count} 个源（本页与系统共用同一份数据）。";
        }
        finally { IsBusy = false; }
    }

    /// <summary>重建「默认查询源」下拉（源列表变了就跟着变）。</summary>
    private void Rebuild()
    {
        _syncing = true;
        try
        {
            var keep = DefaultSourceOption?.Value ?? _settings?.Settings.DefaultSource ?? "";
            SourceOptions.Clear();
            SourceOptions.Add(new PackageSourceOption { Label = "全部源", Value = null });
            foreach (var s in Sources)
                SourceOptions.Add(new PackageSourceOption { Label = s.Name, Value = s.Name });

            DefaultSourceOption = SourceOptions.FirstOrDefault(o => string.Equals(o.Value ?? "", keep, StringComparison.OrdinalIgnoreCase))
                                  ?? SourceOptions[0];
        }
        finally { _syncing = false; }

        SummaryText = $"系统里 {Sources.Count} 个源";
        OnPropertyChanged(nameof(SummaryText));
    }

    // ==================== 源的增删（改的就是系统 winget） ====================

    /// <summary>添加源（<c>winget source add</c>）。名称 + 地址必填。</summary>
    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (_pkg == null || IsBusy) return;

        var name = (NewSourceName ?? "").Trim();
        var arg = (NewSourceArgument ?? "").Trim();
        if (name.Length == 0) { StatusText = "请先填写源名称（例如 my-source）。"; return; }
        if (arg.Length == 0) { StatusText = "请先填写源地址（URL 或以 \\\\ 开头的 UNC 路径）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = $"正在添加源 {name} ……";
            var (ok, msg) = await _pkg.AddSourceAsync(name, arg);
            StatusText = msg;
            if (ok) { NewSourceName = ""; NewSourceArgument = ""; await LoadAsync(); }
        }
        finally { IsBusy = false; }
    }

    /// <summary>删除某个源（<c>winget source remove</c>，内部二次确认）。行内「✕ 停用」按钮传的就是这一行。</summary>
    [RelayCommand]
    private async Task RemoveSourceAsync(PackageSourceInfo? source)
    {
        if (_pkg == null || source == null || IsBusy) return;

        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.RemoveSourceAsync(source.Name);
            StatusText = msg;
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>恢复默认源（<c>winget source reset --force</c>，内部二次确认）。</summary>
    [RelayCommand]
    private async Task ResetSourcesAsync()
    {
        if (_pkg == null || IsBusy) return;

        try
        {
            IsBusy = true;
            var (ok, msg) = await _pkg.ResetSourcesAsync();
            StatusText = msg;
            if (ok) await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    // ==================== 即时生效（写 package.json 的"界面偏好"） ====================

    partial void OnSilentInstallChanged(bool value)
    {
        if (_syncing || _settings == null) return;
        _settings.Settings.SilentInstall = value;
        _settings.Save();
        StatusText = value
            ? "安装 / 升级将追加 --silent（静默，不弹安装器界面）。已写入 package.json。"
            : "安装 / 升级会保留安装器界面（可看到许可协议等）。已写入 package.json。";
    }

    partial void OnDefaultSourceOptionChanged(PackageSourceOption? value)
    {
        if (_syncing || _settings == null || value == null) return;
        _settings.Settings.DefaultSource = value.Value ?? "";
        _settings.Save();
        StatusText = value.Value == null
            ? "默认查询源 = 全部源（搜索时逐个源查一遍再合并）。"
            : $"默认查询源 = {value.Value}（搜索与安装都会优先用它）。";
    }

    // ==================== 动作按钮 ====================

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settings?.FilePath ?? "") ?? "";
            if (dir.Length == 0) { StatusText = "找不到配置文件目录。"; return; }
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "打开目录失败：" + ex.Message; }
    }
}
