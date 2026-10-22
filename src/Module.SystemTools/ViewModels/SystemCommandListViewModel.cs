using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>
/// 「系统工具 → 命令库」页（`script-commands`，NT-091）：一张**只产生副作用**的命令行清单
/// （Windows 设置面板 / 控制面板 / 管理工具入口）。
/// <para>
/// 🔴 与 ScriptManager 的列表页 VM <b>不是同一份</b>（模块禁互引 ⇒ 各持一份，NT-091 §八.2）：
/// 这里只保留命令库需要的部分 —— 搜索 / 排序 / 增改删 / ▶ 运行 / 收藏 / 复制 / 导出导入 json；
/// ⛔ 没有：集合（库）/ 可采集 / 采集器 / 类型·解释器下拉 / 分页（条目量小，一屏够）。
/// </para>
/// <para>🔴 执行一律走 Core 的 <c>ScriptRunner</c>（NT-088 上提），⛔ 不自己起进程。</para>
/// </summary>
public partial class SystemCommandListViewModel : ObservableObject, IStatusProvider, IDetailCardSource
{
    // 🔴 有设计期 / 无 DI 兜底构造（见下方无参 ctor）⇒ 用 null! 消 CS8618；使用点均已 null 安全。
    private readonly ISystemCommandStore _store = null!;
    private readonly ISystemToolsSettingsService _settings = null!;
    private readonly ScriptRunner _runner = null!;

    private readonly List<SystemCommand> _all = new();

    [ObservableProperty] private SystemCommand? _selected;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _sortKey = "Title";
    [ObservableProperty] private bool _sortDescending;

    /// <summary>设计期 / 无 DI 兜底构造（⛔ 不碰任何服务）。</summary>
    public SystemCommandListViewModel() { Card = BuildCard(); }

    public SystemCommandListViewModel(ISystemCommandStore store, ISystemToolsSettingsService settings, ScriptRunner runner)
    {
        _store = store;
        _settings = settings;
        _runner = runner;
        Card = BuildCard();
        Reload();
    }

    public ObservableCollection<SystemCommand> Items { get; } = new();

    public bool HasItems => Items.Count > 0;
    public bool IsNotBusy => !IsBusy;
    public bool HasSelection => Selected is not null;

    public string FilePath => _store?.FilePath ?? "";

    public string DataFileHint => "数据文件：" + FilePath + "（NT-091：系统工具自己的清单，⛔ 不与脚本中心共用）";

    /// <inheritdoc />
    public DetailCardDescriptor? Card { get; }

    public event EventHandler? StatusChanged;
    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSortKeyChanged(string value) => ApplyFilter();
    partial void OnSortDescendingChanged(bool value) => ApplyFilter();

    partial void OnSelectedChanged(SystemCommand? value)
    {
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditCategory));
        OnPropertyChanged(nameof(EditDescription));
        OnPropertyChanged(nameof(EditCommand));
        OnPropertyChanged(nameof(EditNeedAdmin));
        OnPropertyChanged(nameof(EditRunHidden));
        OnPropertyChanged(nameof(ShellText));
        OnPropertyChanged(nameof(HasSelection));
    }

    // ==================== 编辑代理（详情卡 ValuePath 绑这些） ====================

    public string EditTitle
    {
        get => Selected?.Title ?? "";
        set { if (Selected is not null) { Selected.Title = value; Save(); } }
    }

    public string EditCategory
    {
        get => Selected?.Category ?? "";
        set { if (Selected is not null) { Selected.Category = value; Save(); } }
    }

    public string EditDescription
    {
        get => Selected?.Description ?? "";
        set { if (Selected is not null) { Selected.Description = value; Save(); } }
    }

    public string EditCommand
    {
        get => Selected?.Command ?? "";
        set { if (Selected is not null) { Selected.Command = value; Save(); } }
    }

    public bool EditNeedAdmin
    {
        get => Selected?.NeedAdmin ?? false;
        set { if (Selected is not null) { Selected.NeedAdmin = value; Save(); } }
    }

    public bool EditRunHidden
    {
        get => Selected?.RunHidden ?? true;
        set { if (Selected is not null) { Selected.RunHidden = value; Save(); } }
    }

    public string ShellText => Selected?.ShellText ?? "";

    public bool ShowDetailBody => Selected is not null;

    // ==================== 命令 ====================

    [RelayCommand]
    private void Reload()
    {
        _all.Clear();
        if (_store is not null)
            foreach (var it in _store.Items) _all.Add(it);
        ApplyFilter();
        StatusText = $"共 {_all.Count} 条命令";
    }

    [RelayCommand]
    private void Add()
    {
        if (_store is null) return;
        var item = _store.Add("新命令");
        _all.Add(item);
        ApplyFilter();
        Selected = item;
        StatusText = "已新增一条 —— 在右栏填名称 / 命令，▶ 运行试试。";
    }

    [RelayCommand]
    private void Delete()
    {
        if (_store is null || Selected is null) return;
        var title = Selected.Title;
        if (!_store.Remove(Selected)) return;
        _all.Remove(Selected);
        Selected = null;
        ApplyFilter();
        StatusText = "已删除：" + title;
    }

    [RelayCommand]
    private void Save() => _store?.Save();

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Selected is null) return;
        Selected.IsFavorite = !Selected.IsFavorite;
        Save();
        StatusText = Selected.IsFavorite ? "已收藏：" + Selected.Title : "已取消收藏：" + Selected.Title;
    }

    /// <summary>▶ 运行：走 Core <c>ScriptRunner</c>（命令行命令）。需要管理员的项 ⛔ 不跑，只提示复制自跑。</summary>
    [RelayCommand]
    private async Task RunAsync(SystemCommand? item)
    {
        item ??= Selected;
        if (item is null || IsBusy) return;

        if (item.NeedAdmin)
        {
            await CopyAsync(item.Command).ConfigureAwait(true);
            StatusText = "⚠ 这条要管理员权限 —— 命令已复制，请在提权的终端里自己跑。";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Command))
        {
            StatusText = "这条没填命令。";
            return;
        }

        IsBusy = true;
        StatusText = "正在运行：" + item.Title + " …";
        try
        {
            var res = await _runner.RunCommandAsync(new CommandRunRequest
            {
                Command = item.Command,
                Shell = string.IsNullOrWhiteSpace(item.Shell) ? "auto" : item.Shell,
                TimeoutSeconds = _settings?.CommandTimeoutSeconds ?? 30
            }).ConfigureAwait(true);

            StatusText = res.Ok
                ? "已执行：" + item.Title
                : "⚠ 执行失败：" + (res.Error ?? (res.StdErr.Length > 0 ? res.StdErr : "退出码 " + res.ExitCode));
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 执行失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>📋 复制命令文本（给需要管理员的项 / 想手动跑时用）。</summary>
    [RelayCommand]
    private async Task CopyAsync(string? text)
    {
        var s = string.IsNullOrWhiteSpace(text) ? Selected?.Command ?? "" : text;
        if (s.Length == 0) { StatusText = "没有可复制的命令。"; return; }
        await SetClipboardAsync(s).ConfigureAwait(true);
        StatusText = "已复制命令。";
    }

    /// <summary>📤 导出 json（写死落数据目录下的 `commands-export.json`，⛔ 不弹对话框）。</summary>
    [RelayCommand]
    private void Export()
    {
        try
        {
            System.IO.File.WriteAllText(ExportPath, ExportJson());
            StatusText = "已导出：" + ExportPath;
        }
        catch (Exception ex) { StatusText = "⚠ 导出失败：" + ex.Message; }
    }

    /// <summary>📥 导入 json（读同一个导出文件，按 Id 合并）。</summary>
    [RelayCommand]
    private void Import()
    {
        try
        {
            if (!System.IO.File.Exists(ExportPath)) { StatusText = "没找到导出文件：" + ExportPath; return; }
            var list = System.Text.Json.JsonSerializer.Deserialize<List<SystemCommand>>(System.IO.File.ReadAllText(ExportPath));
            if (list is null) { StatusText = "导出文件里没有条目。"; return; }

            var (added, updated) = _store!.Merge(list);
            Reload();
            StatusText = $"已导入：新增 {added} 条 / 更新 {updated} 条";
        }
        catch (Exception ex) { StatusText = "⚠ 导入失败：" + ex.Message; }
    }

    /// <summary>点表头排序。</summary>
    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (string.Equals(SortKey, key, StringComparison.Ordinal)) SortDescending = !SortDescending;
        else { SortKey = key; SortDescending = false; }
    }

    private string ExportPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(FilePath) ?? ".", "commands-export.json");

    private string ExportJson() => System.Text.Json.JsonSerializer.Serialize(_all,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    // ==================== 列表 ====================

    private void ApplyFilter()
    {
        var kw = (SearchText ?? "").Trim();
        IEnumerable<SystemCommand> src = _all;
        if (kw.Length > 0)
            src = src.Where(i => (i.Title ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || (i.Category ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || (i.Command ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase));

        src = SortKey switch
        {
            "Category" => src.OrderBy(i => i.Category, StringComparer.CurrentCultureIgnoreCase),
            "Favorite" => src.OrderByDescending(i => i.IsFavorite),
            _ => src.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
        };
        if (SortDescending) src = src.Reverse();

        Items.Clear();
        foreach (var i in src) Items.Add(i);
        OnPropertyChanged(nameof(HasItems));
    }

    private async Task SetClipboardAsync(string text)
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }
        try { await win.Clipboard.SetTextAsync(text); }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 详情卡 ====================

    private static DetailCardDescriptor BuildCard()
    {
        return new DetailCardDescriptor
        {
            BodyVisiblePath = nameof(ShowDetailBody),
            EmptyText = "在左侧列表里选中一条，或点「➕ 新增」。这里放的是系统自带的设置入口 / 命令行开关；选中后点列表里的「▶」即执行。",
            Sections =
            {
                new DetailSection
                {
                    Header = "选中项",
                    Fields =
                    {
                        new DetailField { Label = "名称", ValuePath = nameof(EditTitle), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur },
                        new DetailField { Label = "功能", ValuePath = nameof(EditCategory), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur, Placeholder = "系统设置 / 打开…" },
                        new DetailField { Label = "分类", ValuePath = nameof(ShellText), Kind = FieldKind.Text, Selectable = true, Mono = true },
                        new DetailField { Label = "简介", ValuePath = nameof(EditDescription), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur, MultiLine = true, Rows = 52 },
                        new DetailField { Label = "命令", ValuePath = nameof(EditCommand), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur, Mono = true, MultiLine = true, Rows = 84 },
                        new DetailField { Kind = FieldKind.Hint, Hint = "例：`start ms-settings:display` 打开「显示」设置；`control` 打开控制面板。▶ = 交给 Core 的执行器跑这条命令。" },
                        new DetailField { Label = "需要管理员权限（⛔ 不代跑：只复制命令）", ValuePath = nameof(EditNeedAdmin), Kind = FieldKind.Check },
                        new DetailField { Label = "隐藏终端窗口（GUI 启动类）", ValuePath = nameof(EditRunHidden), Kind = FieldKind.Check },
                        new DetailField { Kind = FieldKind.Divider },
                        new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(DataFileHint) }
                    }
                }
            }
        };
    }
}
