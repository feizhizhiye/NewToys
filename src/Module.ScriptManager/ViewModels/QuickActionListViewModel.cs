using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>分类下拉项（<see cref="Value"/> 为 null = 「全部分类」）。</summary>
public class ActionCategoryOption
{
    public string Label { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>「类型 / 解释器」下拉项（<see cref="Value"/> = 存进配置的值）。</summary>
public class ActionKindOption
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// 「脚本管理器」里两个<b>同类列表页</b>（「脚本库」与「命令库」）共用的 ViewModel。
/// <para>
/// 两页的布局 / 交互<b>完全一致</b>（规范 3.6）：左「可排序表头 + 列表 + 行内「▶ 运行」」，右「选中项属性卡」，
/// 列表上挂右键菜单（运行 / 收藏 / 复制命令 / 删除）。差别只有三点，由派生类给出：
/// ① <see cref="PageKind"/>：本页显示哪一批条目（**每页一份清单文件**，见 <c>FilePath</c>）；
/// ② <see cref="ColumnLayoutKey"/>：列显隐偏好落 <c>config\columnlayout.json</c> 时用的页键；
/// ③ 页面名字 <see cref="PageDisplayName"/>（提示文案用）。
/// <b>新增项的默认类型</b>由 <see cref="PageKind"/> 推出（见 <c>IQuickActionService.Add</c>）：
/// 「命令库」= 命令行命令、「脚本库」= 脚本文件（v1.33）。
/// </para>
/// <para>
/// 列显隐 / 详情栏开合 / 排序 / 多项筛选继承自 <see cref="ListPageViewModelBase"/>（v1.35 收口），
/// 本类只留「两页共有的业务」：分类过滤、运行、增删改查、属性卡编辑缓冲。
/// </para>
/// <para>
/// ⚠️ 这里的「设置」是<b>操作系统设置项</b>，不是本软件自己的设置（后者在「设置」模块，见规范 3.9）。
/// </para>
/// <para>
/// 收藏 = 每条 <see cref="QuickAction.IsFavorite"/> 上的标记（单一真相源），
/// 「收藏夹」页读的是同一批对象，因此这里右键收藏后那边天然一致。
/// </para>
/// </summary>
public abstract partial class QuickActionListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly IQuickActionService? _svc;
    private readonly IAppConfigService? _app;

    /// <summary>本模块设置（v3.64：脚本目录 + 解释器路径的唯一写入点，落 config\script-manager.json）。</summary>
    private readonly IScriptManagerSettingsService? _pc;

    /// <summary>手工数据（库列表 / 当前库 / 根目录）。</summary>
    private readonly IScriptStoreService? _store;

    /// <summary>导入 / 导出默认目录（当前配置档 <c>data\</c>）。</summary>
    public string ImportExportDefaultDir => _app?.DataDirectory ?? "";

    /// <summary>本页归属的 <see cref="QuickAction.Page"/>（<see cref="QuickActionPage"/> 之一）。</summary>
    private readonly string _pageKind;

    /// <summary>灌值 / 改过滤项时抑制「属性变化 → 立即重排」的开关，避免构造期重复计算。</summary>
    private bool _suppress;

    /// <summary>本页归属的 <see cref="QuickAction.Page"/>。</summary>
    protected abstract string PageKind { get; }

    /// <summary>列显隐偏好落盘用的页键（唯一，两页各一份）。</summary>
    protected abstract string ColumnLayoutKey { get; }

    /// <summary>页面名字（新增 / 空态等提示文案用）。</summary>
    protected abstract string PageDisplayName { get; }

    public ObservableCollection<QuickAction> Items { get; } = new();
    public ObservableCollection<ActionCategoryOption> CategoryOptions { get; } = new();

    // ==================== 页面状态 ====================

    // StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ActionCategoryOption? _selectedCategory;
    [ObservableProperty] private QuickAction? _selected;

    /// <summary>供行内按钮绑定（<c>{Binding !IsBusy}</c> 只能用于同层路径，跨 ListBox 取父级 VM 得用这个）。</summary>
    public bool IsNotBusy => !IsBusy;

    // ==================== 表头（左键点一下切换排序） ====================

    [ObservableProperty] private string _titleHeader = "名称 ▲";
    [ObservableProperty] private string _categoryHeader = "功能";
    [ObservableProperty] private string _favoriteHeader = "★";

    // ==================== 列显隐（基类管状态，这里只给 XAML 提供具名属性） ====================

    public bool IsTitleVisible => Vis("Title");
    public bool IsCategoryVisible => Vis("Category");
    public bool IsClassifyVisible => Vis("Classify");
    public bool IsDescriptionVisible => Vis("Description");
    public bool IsFavoriteVisible => Vis("Favorite");
    public bool IsCollectableVisible => Vis("Collectable");
    public bool IsActionVisible => Vis("Action");

    protected override string PageKey => ColumnLayoutKey;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。
    /// <para>最后一列 <c>Detail</c> 是**表头右侧的那个无文字小按钮**用的固定窄列（规范 3.6）：
    /// 不参与列显隐菜单、也不进 <c>columnlayout.json</c>，它只是把表头与行的最后一格对齐留出来。</para></summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // §4.8.1：每列给一条示例值（用于「示例宽 ≤ 表头宽 → 锁列宽」判定）；「操作」是按钮列 → 不给
        new("Title", "名称", "3*", sample: "清理临时文件"),
        // 「功能」= `Category` 列（维护 / 网络 …）—— 表头与详情卡同名
        new("Category", "功能", "1.3*", sample: "维护"),
        // 新增「分类」：脚本 = 脚本后缀（.ps1 …），命令行命令 = Shell 宿主（cmd / pwsh / 自动）
        new("Classify", "分类", "1.1*", sortable: false, sample: ".ps1"),
        new("Description", "简介", "4*", sortable: false, sample: "删除 %TEMP% 下的临时文件"),
        new("Favorite", "★", "0.9*", sample: "★"),
        // 「可采集」= 能不能当采集器用（✔ / ✖，✖ 悬浮看原因）；派生值，不落盘
        new("Collectable", "可采集", "0.9*", sample: "✔"),
        new("Action", "操作", "1.5*", sortable: false),
    };

    // ==================== 属性卡的编辑缓冲（多字段文本表单 → 显式「💾 保存」，规范 3.5） ====================

    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editCategory = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editCommand = "";
    [ObservableProperty] private bool _editNeedAdmin;

    // ---- 运行窗口行为（v3.19，仅命令行命令有意义）----

    /// <summary>编辑缓冲：是否「隐藏终端窗口」（GUI 启动类静默跑、不弹终端；管理员时失效）。</summary>
    [ObservableProperty] private bool _editRunHidden;

    /// <summary>编辑缓冲：命令行宿主（取值见 <see cref="QuickActionShell"/>，旧数据缺省 = Auto）。</summary>
    [ObservableProperty] private string _editShell = QuickActionShell.Auto;

    /// <summary>「Shell」下拉的选项（来自 <see cref="QuickActionShell.Options"/>）。</summary>
    public ObservableCollection<ActionKindOption> ShellOptions { get; } = new(
        QuickActionShell.Options.Select(o => new ActionKindOption { Label = o.Label, Value = o.Value }));

    [ObservableProperty] private ActionKindOption? _selectedShell;

    partial void OnSelectedShellChanged(ActionKindOption? value)
    {
        if (value != null) EditShell = value.Value;
    }

    // ---- 类型 / 脚本（v1.33）----

    /// <summary>编辑缓冲里的条目类型（取值见 <see cref="QuickActionKind"/>）。</summary>
    [ObservableProperty] private string _editKind = QuickActionKind.Command;

    /// <summary>编辑缓冲里的脚本文件（相对脚本目录的文件名，或绝对路径）。</summary>
    [ObservableProperty] private string _editScriptFile = "";

    /// <summary>编辑缓冲里的解释器（取值见 <see cref="ScriptInterpreter"/>，空 = 自动）。</summary>
    [ObservableProperty] private string _editInterpreter = ScriptInterpreter.Auto;

    /// <summary>「类型」下拉的选项。</summary>
    public ObservableCollection<ActionKindOption> KindOptions { get; } = new()
    {
        new ActionKindOption { Label = "命令行命令（直接跑一行命令）", Value = QuickActionKind.Command },
        new ActionKindOption { Label = "脚本文件（跑脚本目录里的文件）", Value = QuickActionKind.Script },
    };

    /// <summary>「解释器」下拉的选项（空 = 按扩展名自动）。</summary>
    public ObservableCollection<ActionKindOption> InterpreterOptions { get; } = new()
    {
        new ActionKindOption { Label = "自动（按扩展名）", Value = ScriptInterpreter.Auto },
        new ActionKindOption { Label = "Python（.py）", Value = ScriptInterpreter.Python },
        new ActionKindOption { Label = "PowerShell 7+ / pwsh（.ps1）", Value = ScriptInterpreter.Pwsh },
        new ActionKindOption { Label = "Windows PowerShell 5.1（.ps1）", Value = ScriptInterpreter.PowerShell51 },
        new ActionKindOption { Label = "cmd（.bat / .cmd）", Value = ScriptInterpreter.Cmd },
    };

    [ObservableProperty] private ActionKindOption? _selectedKind;
    [ObservableProperty] private ActionKindOption? _selectedInterpreter;

    partial void OnSelectedKindChanged(ActionKindOption? value)
    {
        if (value != null) EditKind = value.Value;
    }

    partial void OnSelectedInterpreterChanged(ActionKindOption? value)
    {
        if (value != null) EditInterpreter = value.Value;
    }

    partial void OnEditKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsScriptKind));
        OnPropertyChanged(nameof(IsCommandKind));
        OnPropertyChanged(nameof(ShowCommandRunOptions));
    }

    /// <summary>当前编辑的是"脚本文件"类型 → 详情卡显示脚本字段、隐藏命令框（反之亦然）。</summary>
    public bool IsScriptKind => string.Equals(EditKind, QuickActionKind.Script, StringComparison.Ordinal);

    public bool IsCommandKind => !IsScriptKind;

    /// <summary>是否显示 Shell / 隐藏终端窗口 两个运行选项（v3.32：详情卡正文显示时且是命令行命令）。</summary>
    public bool ShowCommandRunOptions => ShowDetailBody && IsCommandKind;

    // ==================== 库选择 ====================

    /// <summary>
    /// 本页的库 kind（<c>script</c> / <c>command</c>）；空 = 本页不启用库选择（脚本收集 / 命令收集仍按全量清单）。
    /// </summary>
    protected virtual string LibraryKind => "";

    /// <summary>库下拉：第一项「全部库」（<c>Value = ""</c>），其余 = 本 kind 的集合。</summary>
    public ObservableCollection<CollectionFilterOption> LibraryOptions { get; } = new();

    private CollectionFilterOption? _selectedLibraryOption;

    /// <summary>当前选中的库（「全部库」时 Value 为空串）。</summary>
    public CollectionFilterOption? SelectedLibraryOption
    {
        get => _selectedLibraryOption;
        set
        {
            if (!SetProperty(ref _selectedLibraryOption, value)) return;
            if (_suppress) return;
            OnLibrarySelectionChanged();
        }
    }

    /// <summary>当前库 id（空 = 全部库）。</summary>
    public string CurrentLibraryId => SelectedLibraryOption?.Value ?? "";

    /// <summary>当前库名（空 = 全部库）。</summary>
    public string CurrentLibraryName => SelectedLibraryOption?.Label ?? "全部库";

    /// <summary>重建库下拉（保持上次选中的库；集合被删 ⇒ 回到「全部库」）。</summary>
    private void RefreshLibraries()
    {
        if (_store == null || LibraryKind.Length == 0) return;

        var keep = _store.GetLastCollectionId(LibraryKind);
        var wasSuppressed = _suppress;
        _suppress = true;

        LibraryOptions.Clear();
        LibraryOptions.Add(new CollectionFilterOption { Label = "全部库", Value = "" });
        foreach (var c in _store.Data.Collections
                    .Where(c => !c.Archived && ScriptCollectionKind.Normalize(c.Kind) == LibraryKind)
                    .OrderByDescending(c => c.Pinned)
                    .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            LibraryOptions.Add(new CollectionFilterOption { Label = c.Name, Value = c.Id });

        _selectedLibraryOption = LibraryOptions.FirstOrDefault(o => o.Value == keep) ?? LibraryOptions[0];
        _suppress = wasSuppressed;
        OnPropertyChanged(nameof(SelectedLibraryOption));
        OnPropertyChanged(nameof(CurrentLibraryId));
        OnPropertyChanged(nameof(CurrentLibraryName));
    }

    /// <summary>切库：记「在用」+ 重算分类与列表 + 脚本目录提示跟着变。</summary>
    private void OnLibrarySelectionChanged()
    {
        if (_store != null)
        {
            _store.SetLastCollectionId(LibraryKind, CurrentLibraryId);
            _store.Save();
        }

        OnPropertyChanged(nameof(CurrentLibraryId));
        OnPropertyChanged(nameof(CurrentLibraryName));
        OnPropertyChanged(nameof(ScriptsDirectory));

        RebuildCategories();
        ApplyView();
    }

    /// <summary>本条是否属于当前库（「全部库」= 全通过；未启用库选择的页 = 全通过）。</summary>
    protected virtual bool AcceptLibrary(QuickAction item)
    {
        var id = CurrentLibraryId;
        if (LibraryKind.Length == 0 || id.Length == 0) return true;
        return item.CollectionIds.Contains(id);
    }

    // ==================== 库级导入导出 ====================

    /// <summary>导出当前库的**索引包**（json：定义 + 成员 + 根目录）。</summary>
    [RelayCommand]
    private Task ExportLibraryAsync() => ExportLibraryCoreAsync(bundle: false);

    /// <summary>导出当前库的**全量包**（zip：索引包 + 脚本文件本体）。</summary>
    [RelayCommand]
    private Task ExportLibraryBundleAsync() => ExportLibraryCoreAsync(bundle: true);

    private async Task ExportLibraryCoreAsync(bool bundle)
    {
        if (_store == null) return;
        if (CurrentLibraryId.Length == 0)
        {
            StatusText = "先在工具条「库」里选一个库再导出（「全部库」不能整体导出）。";
            return;
        }

        var col = _store.FindCollection(CurrentLibraryId);
        if (col == null) return;

        var filter = bundle ? "*.zip" : "*.json";
        var name = $"library-{col.Name}" + (bundle ? ".zip" : ".json");
        var path = await DialogHooks.SaveFileAsync(bundle ? "导出库（含脚本文件）" : "导出库（索引包）",
            ImportExportDefaultDir, name, filter);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var result = ScriptLibraryPackageService.Export(_store, col, path, bundle);
            StatusText = result;
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>
    /// 导入库：自动识别索引包 / 全量包；全量包先解到用户选的目录，再建库。
    /// </summary>
    [RelayCommand]
    private async Task ImportLibraryAsync()
    {
        if (_store == null) return;
        if (LibraryKind.Length == 0) { StatusText = "本页不支持库导入。"; return; }

        var files = await DialogHooks.PickFilesAsync("选择库包（json 索引包 / zip 全量包）",
            ImportExportDefaultDir, false);
        if (files.Count == 0) return;

        var path = files[0];
        try
        {
            var isBundle = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            string targetDir = "";
            if (isBundle)
            {
                targetDir = await DialogHooks.PickFolderAsync("选择解压目录（全量包会解到这里，库根目录 = 这个目录）",
                    ImportExportDefaultDir) ?? "";
                if (string.IsNullOrWhiteSpace(targetDir)) return;
            }

            var (ok, message) = ScriptLibraryPackageService.Import(_store, LibraryKind, path, isBundle, targetDir);
            StatusText = message;
            if (!ok) return;

            _store.Save();
            RefreshLibraries();
            RebuildCategories();
            ApplyView();
        }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

    // ==================== 脚本目录（v3.64：唯一写入点 = 本模块设置页；这里的框只是同一份值的另一个视图） ====================

    /// <summary>脚本目录（生效值），详情卡里提示"文件放哪"。
    /// <para>选了具体库时 = 该库的 <c>rootPath</c>（空则回全局目录）；「全部库」= 全局目录。</para></summary>
    public virtual string ScriptsDirectory
    {
        get
        {
            var root = LibraryKind.Length > 0 && _store != null ? _store.RootOf(CurrentLibraryId) : "";
            return root.Length > 0 ? root : (_svc?.ScriptsDirectory ?? "");
        }
    }

    /// <summary>脚本目录的默认值（配置留空时用的那个）。</summary>
    public string DefaultScriptFolder => _pc?.DefaultScriptsFolder ?? "";

    /// <summary>脚本目录当前是不是"用默认值"。</summary>
    public bool IsDefaultScriptFolder => string.IsNullOrWhiteSpace(_pc?.ScriptsFolder);

    /// <summary>
    /// 工具条「脚本目录」框的**编辑态草稿**（v3.64）：框 TwoWay 绑到它，
    /// 回车 / 失焦时由 <see cref="CommitScriptsFolderCommand"/> 一次提交 —— ⛔ 不每敲一个字就写盘。
    /// </summary>
    [ObservableProperty] private string _scriptsFolderDraft = "";

}
