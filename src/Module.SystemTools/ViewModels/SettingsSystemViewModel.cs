using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.SystemTools.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>
/// 「系统工具设置」页（`settings-system`，NT-091）：本模块配置的**唯一写入点**。
/// <para>🔴 写入一律经 <see cref="ISystemToolsSettingsService"/>（Core 的 <c>IOptionsStore</c>，落 <c>config\system.json</c>）；
/// 命令条目清单本身在 <c>config\system\commands.json</c>（<see cref="ISystemCommandStore"/>，页里只显示路径）。
/// <para>NT-096：「软件源（winget）」节自「脚本管理器设置」迁入 —— 源 = 系统 winget 配置，改它要 UAC，
/// 所以本页只**列命令**，一键写进本模块的命令库（<c>script-commands</c> 页）执行。</para>
/// </summary>
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
            StatusText = "已保存：命令超时 " + v + " 秒。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ " + ex.Message;
        }
    }

    // ==================== 「软件源」节：命令清单（NT-096 自脚本设置迁入） ====================

    /// <summary>
    /// 源相关命令（本页只读展示；「＋ 加入命令库」把它们写进命令库的「winget 源」分类）。
    /// <para>🔴 全要管理员权限的那几条 = <c>source add / remove / reset</c>；只读的 list / export 不需要。</para>
    /// </summary>
    public IReadOnlyList<SourceCommandInfo> SourceCommands { get; } = new[]
    {
        new SourceCommandInfo("列出全部源", "查看系统 winget 现在有哪些源、哪些是显式源（只读）",
            "winget source list", false),
        new SourceCommandInfo("导出定义备份", "把每个源的地址 / 类型导出成 json（换机或改坏了照着加回）",
            "winget source export > \"%USERPROFILE%\\winget-sources.json\"", false),
        new SourceCommandInfo("加回官方字体源", "winget-font 是显式源（不参与默认搜索，本应用搜索时会单独补查）",
            "winget source add --name winget-font --arg https://cdn.winget.microsoft.com/fonts --type Microsoft.PreIndexed.Package --explicit", true),
        new SourceCommandInfo("移除字体源", "等价于 winget source remove --name winget-font",
            "winget source remove --name winget-font", true),
        new SourceCommandInfo("切到中科大镜像", "先加镜像源，再移除官方 winget 源（社区源同一时间只留一个）",
            "winget source add --name winget-ustc --arg https://mirrors.ustc.edu.cn/winget-source --type Microsoft.PreIndexed.Package && winget source remove --name winget", true),
        new SourceCommandInfo("恢复默认源", "source reset --force：清掉自定义源，加回 winget / msstore / winget-font",
            "winget source reset --force", true),
    };

    /// <summary>
    /// 把这些命令一次性写进本模块的**命令库**（幂等：按标题去重，已存在的跳过）。
    /// <para>🔴 这是唯一会在设置页写 <c>commands.json</c> 的动作；⛔ 本页不直接执行 winget。</para>
    /// </summary>
    [RelayCommand]
    private void AddSourceCommands()
    {
        if (_store is null)
        {
            StatusText = "⚠ 命令库存储未就绪（DI 未注册）—— 无法写入命令库。";
            return;
        }

        const string categoryName = "winget 源";
        var existing = _store.Items.Select(i => i.Title).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var c in SourceCommands)
        {
            if (existing.Contains(c.Title)) continue;

            var item = _store.Add(c.Title);
            item.Category = categoryName;
            item.Description = c.Description;
            item.Command = c.Command;
            item.NeedAdmin = c.NeedAdmin;
            item.Shell = "cmd";       // 用 cmd 跑：winget 在 PATH 里，输出直接可见
            added++;
        }

        _store.Save();
        StatusText = added == 0
            ? $"命令库里已有「{categoryName}」分类，无新增条目。"
            : $"已写入命令库：{categoryName} +{added} 条 —— 去「命令库」页执行（带 UAC 的会弹提权）。";
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

/// <summary>「软件源」节的一行命令（设置页展示用；属性只读）。</summary>
public sealed class SourceCommandInfo
{
    public SourceCommandInfo(string title, string description, string command, bool needAdmin)
    {
        Title = title;
        Description = description;
        Command = command;
        NeedAdmin = needAdmin;
    }

    public string Title { get; }
    public string Description { get; }
    public string Command { get; }
    public bool NeedAdmin { get; }

    /// <summary>列表里显示「需要管理员权限」标记（设置页只读展示）。</summary>
    public string AdminText => NeedAdmin ? "（需要管理员权限）" : "";
}
