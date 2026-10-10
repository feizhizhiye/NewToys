using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Settings.ViewModels;

/// <summary>
/// 路径配置页。
/// <para>
/// <b>本页保留「💾 保存」按钮</b>（符合规范 3.7 的分界）：这里是若干自由文本字段（程序路径 + 启动参数），
/// 属于"复杂输入"—— 边打边落盘没有意义，也可能写进半截路径，所以由用户显式确认一次性提交。
/// </para>
/// <para>
/// ⚠️ v1.19 起**浏览器路径与参数已移出本页**：它们与按钮名、网址清单同属"一键按钮"的配置，
/// 统一在 <b>「设置 → 一键按钮」</b> 页维护（落 <c>launch.json</c>）。
/// 本页管与"运行环境"有关的项：ffmpeg、外部播放器、视频库目录，
/// 以及 v1.33 新增的<b>脚本运行环境</b>（python / pwsh / PowerShell 5.1 路径 + 脚本目录）。
/// </para>
/// </summary>
public partial class PathViewModel : ObservableObject
{
    private readonly IAppConfigService? _cfg;
    private readonly ConfigStoreService? _store;

    [ObservableProperty] private string _ffmpegPath = "";
    [ObservableProperty] private string _playerPath = "";
    [ObservableProperty] private string _videoFolder = "";
    [ObservableProperty] private string _statusText = "改完点右上角「保存」写入配置。";

    // ==================== 脚本运行环境（v1.33：「设置与脚本」页的脚本靠它们跑起来） ====================

    [ObservableProperty] private string _pythonPath = "";
    [ObservableProperty] private string _pwshPath = "";
    [ObservableProperty] private string _powerShell51Path = "";
    [ObservableProperty] private string _scriptFolder = "";

    /// <summary>脚本目录的默认值（提示文案用）：<c>&lt;应用根&gt;\scripts</c>。</summary>
    public string DefaultScriptFolder => _cfg?.DefaultScriptFolder ?? "";

    public PathViewModel() { }

    public PathViewModel(IAppConfigService cfg, ConfigStoreService store)
    {
        _cfg = cfg;
        _store = store;

        FfmpegPath = cfg.FfmpegPath;
        PlayerPath = cfg.PlayerPath;
        VideoFolder = cfg.DefaultVideoFolder;

        PythonPath = cfg.PythonPath;
        PwshPath = cfg.PwshPath;
        PowerShell51Path = cfg.PowerShell51Path;
        ScriptFolder = cfg.ScriptFolder;
    }

    [RelayCommand]
    private void Save()
    {
        if (_cfg == null) { StatusText = "配置服务未就绪。"; return; }
        try
        {
            _cfg.FfmpegPath = FfmpegPath.Trim();
            _cfg.PlayerPath = PlayerPath.Trim();
            _cfg.DefaultVideoFolder = VideoFolder.Trim();
            _cfg.PythonPath = PythonPath.Trim();
            _cfg.PwshPath = PwshPath.Trim();
            _cfg.PowerShell51Path = PowerShell51Path.Trim();
            _cfg.ScriptFolder = ScriptFolder.Trim();
            _cfg.Save();                                     // 上面每个 setter 已各自落盘，这里是收口
            StatusText = $"已保存 · {DateTime.Now:HH:mm:ss}（下一轮启动/调用即生效）";
        }
        catch (Exception ex)
        {
            StatusText = "保存失败：" + ex.Message;
        }
    }

    /// <summary>打开脚本目录（不存在就先建出来）—— 「设置与脚本」页里引用的脚本文件都放这里。</summary>
    [RelayCommand]
    private void OpenScriptFolder()
    {
        try
        {
            var dir = (_cfg?.ScriptFolder ?? "").Trim();
            if (dir.Length == 0) dir = _cfg?.DefaultScriptFolder ?? "";
            if (dir.Length == 0) { StatusText = "找不到脚本目录。"; return; }

            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "无法打开脚本目录：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = _store?.ConfigDir ?? Path.Combine(AppContext.BaseDirectory, "config");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { StatusText = "无法打开目录。"; }
    }
}
