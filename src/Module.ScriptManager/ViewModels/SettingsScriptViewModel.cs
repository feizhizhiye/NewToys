using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
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
/// 🔴 v3.80（2026-10-19 用户定调）：**源 = 系统 winget 的配置**（<c>winget source …</c>，每条都要 UAC），
/// 本应用不再提供源开关 UI ⇒ 改为「命令库」里的命令条目执行；本页「软件源」节只做**说明 + 一键加入命令库**。
/// </para>
/// <para>
/// 生效方式（规范 §4.9）：路径 = **即时生效**（失焦即落盘，⛔ 无保存按钮）；无效路径 ⇒ 底栏红字 + ⛔ 不写盘。
/// </para>
/// </summary>
public partial class SettingsScriptViewModel : ObservableObject, IStatusProvider
{
    private readonly IScriptManagerSettingsService _settings = null!;
    private readonly IQuickActionService? _quick;
    private readonly IScriptStoreService? _store;

    /// <summary>灌值时抑制「改属性 → 回写」（防递归 + 无谓写盘）。</summary>
    private bool _syncing;

    public SettingsScriptViewModel() { }

    public SettingsScriptViewModel(IScriptManagerSettingsService settings,
        IQuickActionService? quick = null, IScriptStoreService? store = null)
    {
        _settings = settings;
        _quick = quick;
        _store = store;

        _syncing = true;
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

    // ==================== 「软件源」节：命令清单（v3.80） ====================

    /// <summary>
    /// 源相关命令（设置页只读展示；「＋ 加入命令库」把它们写进命令库的「winget 源」集合）。
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
    /// 把这些命令一次性写进**命令库**的「winget 源」集合（幂等：按标题去重，已存在的跳过）。
    /// <para>🔴 这是唯一会在设置页写 <c>script.json</c> 的动作；⛔ 本页不直接执行 winget。</para>
    /// </summary>
    [RelayCommand]
    private void AddSourceCommands()
    {
        if (_quick == null || _store == null)
        {
            StatusText = "⚠ 命令库服务未就绪（DI 未注册）—— 无法写入命令库。";
            return;
        }

        const string collectionName = "winget 源";
        var col = _store.Data.Collections.FirstOrDefault(
            c => c.Kind == ScriptCollectionKind.Command && c.Name == collectionName);
        if (col == null)
        {
            col = new ScriptCollection
            {
                Id = "col-" + Guid.NewGuid().ToString("N"),
                Name = collectionName,
                Kind = ScriptCollectionKind.Command,
                Icon = "📦",
                Description = "winget 系统源的查看 / 备份 / 增删（改源要 UAC）",
                Order = 100,
            };
            _store.Data.Collections.Add(col);
        }

        var existing = _store.Data.Items
            .Where(i => i.Collections.Contains(col.Id))
            .Select(i => i.Title)
            .ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var c in SourceCommands)
        {
            if (existing.Contains(c.Title)) continue;

            _store.Data.Items.Add(new ScriptItem
            {
                Id = "item-" + Guid.NewGuid().ToString("N"),
                Role = ScriptItemRole.Command,
                Title = c.Title,
                Category = collectionName,
                Description = c.Description,
                Target = c.Command,
                NeedAdmin = c.NeedAdmin,
                Shell = QuickActionShell.Cmd,      // 用 cmd 跑：winget 在 PATH 里，输出直接可见
                Interpreter = ScriptInterpreter.Auto,
                Collectable = false,
                Collections = new List<string> { col.Id },
            });
            added++;
        }

        _store.Save();
        _quick.Reload();     // 让「命令库」页的内存镜像跟上

        StatusText = added == 0
            ? $"命令库里已有「{collectionName}」集合，无新增条目。"
            : $"已写入命令库：集合「{collectionName}」+{added} 条 —— 去「命令库」页执行（带 UAC 的会弹提权）。";
    }

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

    /// <summary>界面上标是否要管理员（「＋ 加入命令库」会把它写成条目的「需要管理员权限」）。</summary>
    public string AdminText => NeedAdmin ? "需要管理员" : "只读，不需要管理员";
}
