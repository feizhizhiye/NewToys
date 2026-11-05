using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.ScriptManager.Models;
using NewToys.UI.Services;   // 跨页传物（书签 / Ping → 抓取）已上提 NewToys.UI
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>抓取规则 · 规则目录（web-scrape，/B）。（WHY: SM-066）</summary>
public partial class RuleCatalogViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条 ====================
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "＋ 新建规则集", Classes = "ghost", CommandPath = nameof(NewRuleSetCommand) },
        // 站点探测（输入主页地址 → 试各预设的探测路径 → 推荐预设；🔴 只推荐不代选）
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Width = 150,
            Placeholder = "站点网址…",
            ValuePath = nameof(ProbeUrl),
            EnterCommandPath = nameof(ProbeSiteCommand),
            ToolTip = "站点主页地址（如 example.com，可省略 https:/）。「探测」按站点根路径试各预设的探测路径，以「能解析出条目」为准（⛔ 不看 200）"
        },
        new() { Text = "探测", IconKey = "IconSearch", CommandPath = nameof(ProbeSiteCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        // 从预设创建（选中即创建；⛔ 不覆盖同名，先「探测」看适合哪种）
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 132,
            ItemsPath = nameof(PresetOptions),
            DisplayPath = nameof(CategoryOption.Label),
            SelectedItemPath = nameof(SelectedPreset),
            ToolTip = "选一个预设即按它新建规则集（⛔ 不覆盖同名）；先「探测」看站点适合哪种预设"
        },
        // 分类筛选下拉 —— 按文件里实际出现的值动态生成（⛔ 不硬编码五档）
        new()
        {
            Kind = ToolbarItemKind.Combo,
            Width = 108,
            ItemsPath = nameof(CategoryOptions),
            DisplayPath = nameof(CategoryOption.Label),
            SelectedItemPath = nameof(SelectedCategory),
            ToolTip = "按分类筛选（分类值来自各规则文件的 # category:，按实际出现的值动态生成）"
        },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索名称 / 域名…", ValuePath = nameof(SearchText) },
        new() { Text = "切换启用", IconKey = "IconCheckAll", CommandPath = nameof(ToggleEnabledCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Text = "重读规则", IconKey = "IconRefresh", CommandPath = nameof(RefreshCommand), ToolTip = "重新扫一遍规则目录（⛔ 不运行任何规则集）" }
    };

    private readonly RuleCatalogService _catalog = null!;
    private readonly RuleFileService _files = null!;
    private readonly RuleRunService _run = null!;
    private readonly WebTransferService _transfer = null!;
    private readonly RunRecordService _records = null!;
    private readonly FlowService _flows = null!;
    private readonly INavigationService? _nav;
    private readonly List<RuleSetMeta> _all = new();

    public ObservableCollection<RuleSetMeta> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private RuleSetMeta? _selected;
    [ObservableProperty] private string _searchText = "";

    /// <summary>工具条「分类 ▾」数据源（按文件里实际出现的分类值动态生成）。</summary>
    public ObservableCollection<CategoryOption> CategoryOptions { get; } = new();

    /// <summary>当前选中的分类筛选（空值 = 全部分类）。</summary>
    [ObservableProperty] private CategoryOption? _selectedCategory;

    /// <summary>「预设 ▾」数据源（七预设 + 首位提示项）。</summary>
    public ObservableCollection<CategoryOption> PresetOptions { get; } = new();

    /// <summary>当前选中的预设（选中真预设 ⇒ 立即创建；提示项 / null = 不动）。</summary>
    [ObservableProperty] private CategoryOption? _selectedPreset;

    /// <summary>站点探测用的网址（工具条输入框，回车 = 探测）。</summary>
    [ObservableProperty] private string _probeUrl = "";

    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _baseUrlHeader = "域名";
    [ObservableProperty] private string _categoryHeader = "分类";
    [ObservableProperty] private string _descHeader = "说明";
    [ObservableProperty] private string _enabledHeader = "状态";
    [ObservableProperty] private string _lastRunHeader = "最近运行";

    // 列显隐（真正的状态在基类，落 config\columnlayout.json）
    public bool IsNameVisible => Vis("Name");
    public bool IsBaseUrlVisible => Vis("BaseUrl");
    public bool IsCategoryVisible => Vis("Category");
    public bool IsDescVisible => Vis("Desc");
    public bool IsEnabledVisible => Vis("Enabled");
    public bool IsLastRunVisible => Vis("LastRun");

    protected override string PageKey => "web-scrape";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "2*", sample: "示例站"),
        new("BaseUrl", "域名", "3*", sample: "https://example.com"),
        new("Category", "分类", "1*", sample: "图书"),
        new("Desc", "说明", "2.6*", sample: "抓目录与正文"),
        new("Enabled", "状态", "1*", sample: "✓ 启用"),
        new("LastRun", "最近运行", "1.4*", sample: "2026-10-19 21:00"),
    };

    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>选中的规则集是否有运行记录（导出类动作的可用条件）。</summary>
    public bool HasRun => Selected != null && _records.Latest(Selected.Name) is not null;

    /// <summary>
    /// 另存为选择器（View 的 code-behind 注入；🔴 VM ⛔ 不直接碰 StorageProvider）。
    /// 参数 = (标题, 建议文件名, 默认扩展名)；取消 / 失败返回 null。
    /// </summary>
    public Func<string, string, string, Task<string?>>? PickSaveFile { get; set; }

    /// <summary>详情卡数据源。</summary>
    public RuleSetMeta? DetailItem => Selected;

    /// <summary>详情卡正文是否显示。</summary>
    public bool ShowDetailBody => HasSelection;

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;
    public event EventHandler? DetailChanged;

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    // ==================== 详情卡（声明式，） ====================

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一条规则集",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField { Kind = FieldKind.Hint, Prefix = "名称：", ValuePath = "DetailItem.Name" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "域名：", ValuePath = "DetailItem.BaseUrl" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "编码：", ValuePath = "DetailItem.Encoding" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "说明：", ValuePath = "DetailItem.DescText" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "分类：", ValuePath = "DetailItem.CategoryText" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "角色：", ValuePath = "DetailItem.RoleText" },
                    new DetailField { Kind = FieldKind.Hint, Prefix = "适用站点：", ValuePath = "DetailItem.WorksText" },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(RunSummary), VisibleWhen = nameof(ShowDetailBody) },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(FileSummary), VisibleWhen = nameof(ShowDetailBody) },
                    new DetailField
                    {
                        Label = "文件路径",
                        ValuePath = "DetailItem.FilePath",
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "🧩 调试集合",
                Classes = "primary",
                ToolTip = "打开调试集合（五段槽位装配流水线 + 试跑推荐 + HTML 源码面板）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = EditRuleSetCommand
            },
            new DetailAction
            {
                Text = "打开文件", IconKey = "IconEdit",
                Classes = "ghost",
                ToolTip = "用记事本打开 .py 规则文件（⛔ 不用「运行方式」，避免双击就执行）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenFileCommand
            },
            new DetailAction
            {
                Text = "打开目录", IconKey = "IconOpen",
                Classes = "ghost",
                ToolTip = "在资源管理器里打开规则目录",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenFolderCommand
            },
            new DetailAction
            {
                Text = "运行记录", IconKey = "IconOpen",
                Classes = "ghost",
                ToolTip = "打开该规则集的运行记录目录（runs\\<名>\\，保留最近 10 次；导出在右键菜单）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = OpenRunsFolderCommand
            },
            new DetailAction
            {
                Text = "删除", IconKey = "IconTrash",
                Classes = "ghost",
                ToolTip = "删除这条规则集的文件（不可恢复）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

    /// <summary>详情卡「最近运行」行。</summary>
    public string RunSummary => DetailItem?.LastRunSummary ?? "";

    /// <summary>详情卡「文件」行。</summary>
    public string FileSummary => DetailItem?.FileSummary ?? "";

    private void NotifyDetail()
    {
        OnPropertyChanged(nameof(RunSummary));
        OnPropertyChanged(nameof(FileSummary));
    }

    // ==================== 构造 ====================

    public RuleCatalogViewModel() { }

    public RuleCatalogViewModel(RuleCatalogService catalog, RuleFileService files,
        IColumnLayoutService cols, WebTransferService transfer, RuleRunService run,
        RunRecordService records, FlowService flows, INavigationService? nav = null)
        : base(cols)
    {
        _catalog = catalog;
        _files = files;
        _transfer = transfer;
        _run = run;
        _records = records;
        _flows = flows;
        _nav = nav;
        LoadLayout();
        BuildPresetOptions();
        Refresh();

        // 跨页「🕷 发送到网页抓取」：消费待处理 URL，建一条以它命名的规则集（⛔ 不弹窗）。
        var pending = _transfer.TakeUrl();
        if (!string.IsNullOrWhiteSpace(pending))
        {
            var name = SuggestNameFromUrl(pending);
            if (_files.Create(name, pending))
            {
                Refresh();
                Selected = Items.FirstOrDefault(i => i.Name == name);
                StatusText = $"已从其他页收到网址，建好规则集「{name}」，去编辑器补命令。";
                return;
            }
        }

        SetCountStatus();
    }

    /// <summary>从 URL 猜一个规则集名（域名去掉 www）；失败退化为「新规则集」。</summary>
    private static string SuggestNameFromUrl(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
            return RuleFileService.SafeName(string.IsNullOrWhiteSpace(host) ? "新规则集" : host);
        }
        catch { return "新规则集"; }
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void Refresh()
    {
        _all.Clear();
        foreach (var m in _catalog.Load()) _all.Add(m);
        RebuildCategoryOptions();
        ApplyView();
        SetCountStatus();
    }

    /// <summary>
    /// 按 <see cref="_all"/> 里**实际出现的分类值**重建「分类 ▾」（⛔ 不硬编码五档）：
    /// 首项固定「全部分类」，其余按分类值去重排序；之前选的值没了就回「全部分类」。
    /// </summary>
    private void RebuildCategoryOptions()
    {
        var previous = SelectedCategory?.Value ?? "";
        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryOption { Label = "全部分类", Value = "" });
        foreach (var cat in _all
            .Select(m => m.Category)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
        {
            CategoryOptions.Add(new CategoryOption { Label = RuleSetMeta.CategoryLabel(cat), Value = cat });
        }

        SelectedCategory = CategoryOptions.FirstOrDefault(
            o => string.Equals(o.Value, previous, StringComparison.OrdinalIgnoreCase)) ?? CategoryOptions[0];
    }

    /// <summary>状态栏计数 + 脚本头脏值警告汇总（⚠ 前缀，最多列 2 条）。</summary>
    private void SetCountStatus()
    {
        var warns = _all.SelectMany(m => m.HeaderWarnings).ToList();
        StatusText = warns.Count == 0
            ? $"共 {Items.Count} 条规则集"
            : $"共 {Items.Count} 条规则集；⚠ " + string.Join("；", warns.Take(2))
              + (warns.Count > 2 ? $"；等共 {warns.Count} 处" : "");
    }

    [RelayCommand]
    private void NewRuleSet()
    {
        var name = "新规则集";
        for (var i = 2; _files.Exists(name); i++) name = "新规则集" + i;

        if (!_files.Create(name, "https://"))
        {
            StatusText = "新建失败（文件已存在或目录不可写）。";
            return;
        }

        Refresh();
        Selected = Items.FirstOrDefault(i => i.Name == name);
        IsDetailVisible = true;
        SaveDetailFlag();
        StatusText = "已新建规则集，点「📝 打开文件」填域名与命令。";
    }

    // ==================== 预设库 + 站点探测 ====================

    /// <summary>建「预设 ▾」数据源：七预设 + 首位提示项（Value 空 = 不动）。</summary>
    private void BuildPresetOptions()
    {
        PresetOptions.Add(new CategoryOption { Label = "（选预设创建）", Value = "" });
        foreach (var p in PresetInfo.All)
            PresetOptions.Add(new CategoryOption { Label = p.Title, Value = p.Id });

        SelectedPreset = PresetOptions[0];
    }

    /// <summary>选中真预设 ⇒ 立即创建（提示项 / 空 = 不动）；创建后复位到提示项。</summary>
    partial void OnSelectedPresetChanged(CategoryOption? value)
    {
        if (value is null || value.Value.Length == 0) return;

        CreateFromPreset(value.Value);

        // 复位（触发本方法但 Value 空 ⇒ 直接返回，⛔ 不会循环）
        SelectedPreset = PresetOptions.FirstOrDefault(o => o.Value.Length == 0);
    }

    /// <summary>从预设创建规则集（复制预设文件 + 写探测网址为 base；⛔ 不覆盖同名）。</summary>
    private void CreateFromPreset(string presetId)
    {
        var preset = PresetInfo.Find(presetId);
        if (preset is null) return;

        var name = preset.NewNameBase;
        for (var i = 2; _files.Exists(name); i++) name = preset.NewNameBase + " " + i;

        var baseUrl = (ProbeUrl ?? "").Trim();
        if (!_files.CreateFromPreset(preset, name, baseUrl))
        {
            StatusText = _files.PresetExists(preset.Id)
                ? "⚠ 从预设创建失败（目录不可写？）。"
                : "⚠ 预设文件不在（被删过 —— 随包预设只播种一次，不会复活；可从备份恢复 rules\\ 目录）。";
            return;
        }

        Refresh();
        Selected = Items.FirstOrDefault(i => i.Name == name);
        IsDetailVisible = true;
        SaveDetailFlag();
        StatusText = baseUrl.Length > 0
            ? $"已从预设「{preset.Title}」创建「{name}」（域名已填 {baseUrl}）—— 点「🧩 调试集合」抓页试跑。"
            : $"已从预设「{preset.Title}」创建「{name}」—— 去调试集合填起始网址。";
    }

    /// <summary>
    /// 站点探测：试各预设的探测路径，以「能解析出条目」为准；
    /// 🔴 只推荐不代选 —— 结果与推荐写状态栏，创建仍由「预设 ▾」显式触发。
    /// </summary>
    [RelayCommand]
    private async Task ProbeSiteAsync()
    {
        if (IsBusy) return;

        var url = (ProbeUrl ?? "").Trim();
        if (url.Length == 0) { StatusText = "先填站点网址（如 example.com）。"; return; }

        IsBusy = true;
        try
        {
            StatusText = "探测中…";
            var result = await _run.ProbeSiteAsync(url, PresetInfo.All);

            if (result.Error.Length > 0)
            {
                StatusText = "⚠ 探测失败：" + result.Error;
                return;
            }

            var hits = result.Matches.Take(3).ToList();
            if (hits.Count == 0)
            {
                StatusText = "没探测出可用预设（各探测路径都没解析出条目）—— 兜底可直接用「页面链接」预设。";
                return;
            }

            var best = hits[0];
            var parts = hits.Select(h => $"✓ {h.PresetTitle} · {h.Path} · {h.Count} 条");
            var more = result.Matches.Count() > hits.Count ? "；等" : "";
            StatusText = string.Join("；", parts) + more
                         + $" → 推荐「{best.PresetTitle}」（只推荐，⛔ 不代选；在「预设 ▾」里选它创建）";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 探测出错：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleEnabled()
    {
        if (Selected == null) { StatusText = "先选中一条规则集。"; return; }

        Selected.Enabled = !Selected.Enabled;
        PersistMeta();
        ApplyView();
        StatusText = Selected.Enabled ? $"已启用「{Selected.Name}」。" : $"已停用「{Selected.Name}」。";
    }

    /// <summary>跳「规则编辑器」并带上当前规则集（经 <see cref="WebTransferService"/> 传参）。</summary>
    [RelayCommand]
    private void EditRuleSet()
    {
        if (Selected == null) return;
        _transfer.PushRuleSet(Selected.Name);
        _nav?.NavigateTo("web-rule-editor");
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (Selected == null) return;
        try
        {
            // 🔴 明确用记事本：`ShellExecute` 打开 .py 会**执行脚本**，不是编辑
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Selected.FilePath + "\"")
            {
                UseShellExecute = false
            });
            StatusText = "已用记事本打开，改完回来点「重读规则」。";
        }
        catch (Exception ex) { StatusText = "⚠ 打不开文件：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_files.RulesDir) { UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = "⚠ 打不开目录：" + ex.Message; }
    }

    // ==================== 运行记录 ====================

    /// <summary>打开选中规则集的运行记录目录（runs\&lt;名&gt;\；没跑过给指引）。</summary>
    [RelayCommand]
    private void OpenRunsFolder()
    {
        if (Selected == null) return;

        var dir = _records.ScriptDir(Selected.Name);
        if (!Directory.Exists(dir))
        {
            StatusText = "还没运行过这个规则集 —— 点「🧩 调试集合」→「跑这段」跑一次就有记录。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            StatusText = "已打开运行记录目录（每脚本保留最近 10 次，⛔ 不进备份）。";
        }
        catch (Exception ex) { StatusText = "⚠ 打不开目录：" + ex.Message; }
    }

    /// <summary>导出最近一次运行记录为 CSV（字段汇总 + 前 3 条预览）。</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (Selected == null) return;

        var rec = _records.Latest(Selected.Name);
        if (rec is null) { StatusText = "还没运行过这个规则集 —— 先「跑这段」跑一次再导出。"; return; }

        var path = PickSaveFile is null
            ? null
            : await PickSaveFile("导出运行记录（CSV）",
                RuleFileService.SafeName(Selected.Name) + "-" + rec.At.ToString("yyyyMMdd-HHmmss") + ".csv", ".csv");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            // 🔴 带 BOM：Excel 靠它认 UTF-8 中文
            File.WriteAllText(path, _records.ToCsv(rec), new UTF8Encoding(true));
            StatusText = "已导出 CSV：" + path;
        }
        catch (Exception ex) { StatusText = "⚠ 导出失败：" + ex.Message; }
    }

    /// <summary>导出最近一次运行记录为 JSON（result.json 原样拷贝）。</summary>
    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Selected == null) return;

        var rec = _records.Latest(Selected.Name);
        if (rec is null) { StatusText = "还没运行过这个规则集 —— 先「跑这段」跑一次再导出。"; return; }

        var src = Path.Combine(rec.Dir, "result.json");
        if (!File.Exists(src)) { StatusText = "⚠ 记录文件不在（可能被手工删过）：" + rec.Dir; return; }

        var path = PickSaveFile is null
            ? null
            : await PickSaveFile("导出运行记录（JSON）",
                RuleFileService.SafeName(Selected.Name) + "-" + rec.At.ToString("yyyyMMdd-HHmmss") + ".json", ".json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.Copy(src, path, overwrite: true);
            StatusText = "已导出 JSON：" + path;
        }
        catch (Exception ex) { StatusText = "⚠ 导出失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected == null) return;

        var name = Selected.Name;

        // 被流水线引用的脚本 ⛔ 不静默删（删了 flow 会缺段，且用户多半忘了挂在哪）
        var refs = _flows.ReferencesOf(name);
        if (refs.Count > 0)
        {
            var msg = $"「{name}」被 {refs.Count} 条流水线引用（{string.Join("、", refs.Take(5))}"
                     + (refs.Count > 5 ? " 等" : "") + "）。\n删除后这些流水线会缺段，下载 / 调试会报错。确定删除？";
            if (!await NewToys.Core.Services.DialogHooks.ConfirmAsync(msg).ConfigureAwait(true))
            {
                StatusText = "已取消删除（脚本被流水线引用）。";
                return;
            }
        }

        _catalog.Delete(name);
        _all.RemoveAll(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        Selected = null;
        ApplyView();
        await Task.CompletedTask;
        StatusText = $"已删除「{name}」。";
    }

    private void PersistMeta() => _catalog.Save(_all);

    // ==================== 列表 ====================

    private void ApplyView()
    {
        IEnumerable<RuleSetMeta> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || t.BaseUrl.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 分类筛选（工具条下拉；值来自文件 # category:）
        var category = SelectedCategory?.Value ?? "";
        if (category.Length > 0)
            q = q.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));

        q = ApplyFilters(q);

        q = SortDescending
            ? q.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);

        Items.Clear();
        foreach (var m in q.ToList()) Items.Add(m);

        if (Selected != null && !Items.Contains(Selected)) Selected = null;
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders() => NameHeader = "名称" + SortArrow("Name");

    partial void OnSelectedChanged(RuleSetMeta? value)
    {
        OnPropertyChanged(nameof(DetailItem));
        NotifyDetail();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasRun));
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    partial void OnSelectedCategoryChanged(CategoryOption? value) => ApplyView();
}
