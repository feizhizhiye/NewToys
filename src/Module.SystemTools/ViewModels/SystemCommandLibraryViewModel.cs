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
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;

namespace NewToys.SystemTools.ViewModels;

/// <summary>
/// 「系统工具 → 命令库」页（<c>script-commands</c>，对标脚本库重做）：一张<b>只产生副作用</b>的命令行清单
/// （Windows 设置面板 / 控制面板 / 管理工具 / 快捷设置 / winget）。
/// <para>
/// 🔴 类名必须与 View 成对：<c>SystemCommandLibraryView</c> ↔ <see cref="SystemCommandLibraryViewModel"/>
/// （Shell 按「XxxView → XxxViewModel」约定反查 VM，名字对不上 ⇒ DataContext = null ⇒ 整页绑定静默失效）。
/// 平移时类名没跟着改（旧名 <c>SystemCommandListViewModel</c>）⇒ 命令库页自此不渲染，改名修复。
/// </para>
/// <para>
/// 🔴 继承 <see cref="ListPageViewModelBase"/>（Shared）⇒ 与脚本库<b>同一套</b>列显隐 / 表头排序 /
/// 多项筛选 / 声明式工具条 / 详情栏开合（偏好落 <c>config\columnlayout.json</c>，页键仍是 <c>script-commands</c>）。
/// 早期精简版（固定四列 + 页内工具条 + 无详情卡）已废除。
/// </para>
/// <para>🔴 执行一律走 Core 的 <c>ScriptRunner</c>，⛔ 不自己起进程；需要管理员的项只复制命令。</para>
/// </summary>
public partial class SystemCommandLibraryViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // 🔴 有设计期 / 无 DI 兜底构造 ⇒ 用 null! 消 CS8618；使用点均已 null 安全。
    private readonly ISystemCommandStore _store = null!;
    private readonly ISystemToolsSettingsService _settings = null!;
    private readonly ScriptRunner _runner = null!;
    private readonly ISystemCommandLauncher _launcher = null!;

    private readonly List<SystemCommand> _all = new();

    [ObservableProperty] private SystemCommand? _selected;
    [ObservableProperty] private bool _isBusy;

    /// <summary>关键字（即时过滤：名称 / 功能 / 命令 / 简介）。</summary>
    [ObservableProperty] private string _searchText = "";

    // ==================== 表头文案（基类 SortArrow 拼排序箭头） ====================

    [ObservableProperty] private string _titleHeader = "名称";
    [ObservableProperty] private string _categoryHeader = "功能";
    [ObservableProperty] private string _shellHeader = "分类";
    [ObservableProperty] private string _descriptionHeader = "简介";
    [ObservableProperty] private string _favoriteHeader = "★";

    /// <summary>设计期 / 无 DI 兜底构造（⛔ 不碰任何服务）。</summary>
    public SystemCommandLibraryViewModel() : base(null, null) { }

    public SystemCommandLibraryViewModel(
        ISystemCommandStore store,
        ISystemToolsSettingsService settings,
        ScriptRunner runner,
        ISystemCommandLauncher launcher,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null)
        : base(cols, opts)
    {
        _store = store;
        _settings = settings;
        _runner = runner;
        _launcher = launcher;
        LoadLayout();     // 列显隐 + 详情栏开合（基类；页键 script-commands）
        Reload();
    }

    public ObservableCollection<SystemCommand> Items { get; } = new();

    public bool HasItems => Items.Count > 0;
    public bool IsNotBusy => !IsBusy;
    public bool HasSelection => Selected is not null;

    public string FilePath => _store?.FilePath ?? "";

    public string DataFileHint => "数据文件：" + FilePath + "（系统工具自己的命令清单，⛔ 不与脚本中心共用）";

    // ==================== 列显隐直通（XAML 只能绑具名属性；状态在基类） ====================

    public bool IsTitleVisible => Vis("Title");
    public bool IsCategoryVisible => Vis("Category");
    public bool IsShellVisible => Vis("Shell");
    public bool IsDescriptionVisible => Vis("Description");
    public bool IsFavoriteVisible => Vis("Favorite");
    public bool IsActionVisible => Vis("Action");

    protected override string PageKey => "script-commands";   // 🔴 页键落 columnlayout.json，⛔ 不改

    /// <summary>列的元数据（顺序即表头与行的列顺序）；⛔ 不加 Detail 占位列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Title", "名称", "240", sample: "网络和 Internet 设置"),
        new("Category", "功能", "120", sample: "系统设置"),
        new("Shell", "分类", "110", sample: "自动"),
        new("Description", "简介", "*", sample: "打开系统设置的「显示」页，改分辨率 / 缩放"),
        new("Favorite", "★", "90", sample: "★"),
        new("Action", "操作", "96", sample: "▶ 运行"),
    };

    /// <summary>排序列 / 筛选条件 / 关键字变化后重算列表（基类 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    // ==================== 状态变化通知 ====================

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSelectedChanged(SystemCommand? value)
    {
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditCategory));
        OnPropertyChanged(nameof(EditShell));
        OnPropertyChanged(nameof(EditDescription));
        OnPropertyChanged(nameof(EditCommand));
        OnPropertyChanged(nameof(EditNeedAdmin));
        OnPropertyChanged(nameof(EditRunHidden));
        OnPropertyChanged(nameof(ShellText));
        OnPropertyChanged(nameof(HasSelection));
        DetailChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Shell 宿主下拉：<c>auto</c> / <c>cmd</c> / <c>pwsh</c>。</summary>
    public string EditShell
    {
        get => Selected?.Shell ?? "auto";
        set
        {
            if (Selected is null || string.IsNullOrWhiteSpace(value)) return;
            Selected.Shell = value;
            Save();
            OnPropertyChanged(nameof(ShellText));
        }
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

    /// <summary>Shell 下拉的候选项（详情卡 <see cref="FieldKind.Select"/> 简单值通道）。</summary>
    public IReadOnlyList<string> ShellOptions { get; } = new[] { "auto", "cmd", "pwsh" };

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

    /// <summary>▶ 运行：普通项走 Core <c>ScriptRunner</c>（静默跑 + 捕获输出）；需要管理员的项弹 UAC 提权执行（可见窗口）。</summary>
    [RelayCommand]
    private async Task RunAsync(SystemCommand? item)
    {
        item ??= Selected;
        if (item is null || IsBusy) return;

        if (item.NeedAdmin)
        {
            // 补充（用户 2026-10-23：勾了需要管理员的也能直接运行）：弹 UAC，提权后在可见窗口里执行。
            // 想手动跑还有「📋 复制命令」（工具条 / 行右键）。
            if (_launcher is null) { StatusText = "提权运行不可用（DI 未注册）。"; return; }
            var (_, msg) = _launcher.RunElevated(item);
            StatusText = msg;
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Command))
        {
            StatusText = "这条没填命令。";
            return;
        }

        // 「打开一个窗口」类（start … / *.msc / *.cpl / ms-settings: / explorer …）：直接 shell 打开、**不等结束**。
        // 走捕获路的话 cmd 会等到窗口关闭，共用的 RunCommand 一直 IsRunning ⇒ 全表按钮变灰（见 launcher 注释）。
        if (SystemCommandLauncher.TryStartWindowCommand(item.Command, out var openMsg))
        {
            StatusText = openMsg.Length > 0 ? openMsg : $"已打开「{item.Title}」（不等待窗口关闭）。";
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

    /// <summary>📤 导出 json（写死落数据文件同目录的 <c>commands-export.json</c>，⛔ 不弹对话框）。</summary>
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

    // ==================== 列表 ====================

    /// <summary>关键字 + 多项筛选 + 排序（基类 <see cref="SortColumn"/> / <see cref="SortDescending"/>）。</summary>
    private void ApplyFilter()
    {
        IEnumerable<SystemCommand> src = _all;

        var kw = (SearchText ?? "").Trim();
        if (kw.Length > 0)
            src = src.Where(i => (i.Title ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || (i.Category ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || (i.Command ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase)
                                 || (i.Description ?? "").Contains(kw, StringComparison.CurrentCultureIgnoreCase));

        src = ApplyFilters(src);   // 基类：表头右键「🔎 筛选」加的条件（且 关系）

        src = SortColumn switch
        {
            "Category" => src.OrderBy(i => i.Category, StringComparer.CurrentCultureIgnoreCase),
            "Shell" => src.OrderBy(i => i.Shell, StringComparer.Ordinal),
            "Description" => src.OrderBy(i => i.Description, StringComparer.CurrentCultureIgnoreCase),
            "Favorite" => src.OrderByDescending(i => i.IsFavorite),
            _ => src.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
        };
        if (SortDescending) src = src.Reverse();

        Items.Clear();
        foreach (var i in src) Items.Add(i);
        OnPropertyChanged(nameof(HasItems));

        TitleHeader = "名称" + SortArrow("Title");
        CategoryHeader = "功能" + SortArrow("Category");
        ShellHeader = "分类" + SortArrow("Shell");
        DescriptionHeader = "简介" + SortArrow("Description");
        FavoriteHeader = "★" + SortArrow("Favorite");

        RebuildQuickFilters();
    }

    /// <summary>表头右键「🔎 筛选」的快捷项 = 当前出现过的「功能」分类值（随条目增删重建）。</summary>
    private void RebuildQuickFilters()
    {
        var cats = _all.Select(i => i.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SetQuickFilters(cats.Select(c => (c, "Category", c)));
    }

    private string ExportPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(FilePath) ?? ".", "commands-export.json");

    private string ExportJson() => System.Text.Json.JsonSerializer.Serialize(_all,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private async Task SetClipboardAsync(string text)
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }
        try { await win.Clipboard.SetTextAsync(text); }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 详情面板 + 声明式详情卡（L2；补上右栏渲染） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <inheritdoc />
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private static DetailCardDescriptor BuildCard()
    {
        return new DetailCardDescriptor
        {
            BodyVisiblePath = nameof(ShowDetailBody),
            EmptyText = "在左侧列表里选中一条，或点工具条「➕ 新增」。首次运行会播种一批 Windows 常用命令 / 快捷设置 / winget 命令；选中后点列表里的「▶」即执行。",
            Sections =
            {
                new DetailSection
                {
                    Header = "选中项",
                    Fields =
                    {
                        new DetailField { Label = "名称", ValuePath = nameof(EditTitle), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur },
                        new DetailField { Label = "功能", ValuePath = nameof(EditCategory), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur, Placeholder = "系统设置 / 控制面板 / 管理工具 / 快捷设置 / winget" },
                        new DetailField
                        {
                            Label = "分类（Shell 宿主）",
                            Kind = FieldKind.Select,
                            OptionsPath = nameof(ShellOptions),
                            ValuePath = nameof(EditShell),
                            Hint = "auto = 自动选 pwsh 7（本机没有才退回 5.1）；cmd = cmd.exe /c（reg / netsh / winget 用它最稳）；pwsh = 强制 pwsh 7"
                        },
                        new DetailField { Label = "简介", ValuePath = nameof(EditDescription), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur, MultiLine = true, Rows = 52 },
                        new DetailField { Label = "命令", ValuePath = nameof(EditCommand), Kind = FieldKind.Text, Commit = EditCommitMode.OnBlur, Mono = true, MultiLine = true, Rows = 84 },
                        new DetailField { Kind = FieldKind.Hint, Hint = "例：`start ms-settings:display` 打开「显示」设置；`control` 打开控制面板。▶ = 交给 Core 的执行器跑这条命令。" },
                        new DetailField { Label = "需要管理员权限（▶ 运行 = 弹 UAC，提权后在 Windows 终端里执行）", ValuePath = nameof(EditNeedAdmin), Kind = FieldKind.Check },
                        new DetailField { Label = "隐藏终端窗口（GUI 启动类）", ValuePath = nameof(EditRunHidden), Kind = FieldKind.Check },
                        new DetailField { Kind = FieldKind.Divider },
                        new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(DataFileHint) }
                    }
                }
            }
        };
    }

    // ==================== 声明式工具条（由 Shell 统一渲染，页面⛔ 不再自绘） ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new()
        {
            Text = "➕ 新增", Classes = "ghost",
            CommandPath = nameof(AddCommand),
            IsEnabledPath = "!" + nameof(IsBusy),
            ToolTip = "新增一条命令条目（在右栏详情卡里填名称 / 命令）"
        },
        new()
        {
            Text = "🗑 删除", Classes = "ghost",
            CommandPath = nameof(DeleteCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "删除选中条目（二次确认；只删清单，不碰系统）"
        },
        new()
        {
            Text = "★ 收藏", Classes = "ghost",
            CommandPath = nameof(ToggleFavoriteCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "收藏 / 取消收藏选中项 —— 收藏夹页（模块下第一个页面）同步显示"
        },
        new()
        {
            Text = "▶ 运行", Classes = "ghost",
            CommandPath = nameof(RunCommand),
            IsEnabledPath = nameof(IsNotBusy),
            ToolTip = "运行选中条目；需要管理员的项弹 UAC 提权执行（优先 Windows 终端，跑完不自动关）"
        },
        new()
        {
            Text = "📋 复制命令", Classes = "ghost",
            CommandPath = nameof(CopyCommand),
            IsEnabledPath = nameof(HasSelection),
            ToolTip = "复制选中条目的命令文本"
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 88,
            Placeholder = "搜索名称 / 功能 / 命令…",
            ValuePath = nameof(SearchText),
            ToolTip = "即时过滤（本地）"
        },
        new()
        {
            Text = "📤 导出", Classes = "ghost",
            CommandPath = nameof(ExportCommand),
            ToolTip = "把全部条目导出到数据文件同目录的 commands-export.json"
        },
        new()
        {
            Text = "📥 导入", Classes = "ghost",
            CommandPath = nameof(ImportCommand),
            ToolTip = "从同一个 commands-export.json 按 Id 合并导入"
        },
    };
}
