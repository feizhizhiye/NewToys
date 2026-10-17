using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「脚本管理器 → 信息面板」页（NT-067；原 `script-info`「系统信息」）。
/// <para>
/// 🔴 定位变了：本页**不再写死 C# 采集**，而是「脚本输出呈现器」——
/// <c>sysinfo\sysinfo.manifest.json</c> 定义列与脚本，脚本只输出**按列对齐的值数组**，页面渲染成表。
/// 切「组合脚本」= 切 manifest。<c>script-info</c> 菜单 Key 不变（收藏 / 跳转按 Key 寻址）。
/// </para>
/// <para>
/// 唯一的 C# 采集器是 <c>builtin</c>（「应用路径」组）：那些值是**注入服务**（<see cref="IAppConfigService"/> /
/// <see cref="ConfigStoreService"/> / 本模块设置），脚本读不到 profiles 上下文（卡 §三.6 / D3）。
/// </para>
/// <para>
/// 🔴 列机制：<see cref="ListPageViewModelBase"/> 的列是「元数据 + 具名 <c>IsXxxVisible</c> + XAML 静态列」，
/// 列数可变 ⇒ 页内自绘动态列（<c>Controls/SysInfoGrid</c>），⛔ 不改框架层（卡 §三.5 / D1）。
/// 代价：列宽不接拖拽，改宽走「⚙ 列设置」写回 manifest。
/// </para>
/// </summary>
public partial class ScriptInfoViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条（NT-055 / 需求 §4.8.5） ====================
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "🔄 重新检测", Classes = "ghost", CommandPath = nameof(DetectCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Kind = ToolbarItemKind.TextField, Width = 96, Placeholder = "搜索…", ValuePath = nameof(SearchText) },
        new() { Text = "📋 复制全部", CommandPath = nameof(CopyAllCommand) },
        new() { Text = "📂 脚本目录", CommandPath = nameof(OpenScriptDirCommand), ToolTip = "打开 sysinfo\\ 脚本集合目录" },
        new() { Text = "✏ 编辑 manifest", CommandPath = nameof(EditManifestCommand), ToolTip = "用系统默认程序打开 sysinfo.manifest.json" },
        new() { Text = "⚙ 列设置", CommandPath = nameof(ToggleColumnSettingsCommand), ToolTip = "列显隐 / 列宽（宽度保存后写回 manifest）" },
        new()
        {
            Kind = ToolbarItemKind.TextField, Width = 74, Placeholder = "间隔(秒)", ValuePath = nameof(IntervalText),
            EnterCommandPath = nameof(ApplyIntervalCommand),
            ToolTip = "定时刷新间隔，只作用于 manifest 里 auto:true 的脚本；0 = 关闭（回车确认）",
        },
        new() { Text = "📦 导出集合", CommandPath = nameof(ExportPackCommand), ToolTip = "把整个 sysinfo\\ 目录打包成 zip（含 manifest）" },
        new() { Text = "📂 导入集合", CommandPath = nameof(ImportPackCommand), ToolTip = "从 zip 恢复脚本集合（逐条目校验，已存在文件先备份为 .bak）" },
    };

    // ==================== 依赖 ====================
    private readonly IAppConfigService? _cfg;
    private readonly ConfigStoreService? _store;
    private readonly IScriptManagerSettingsService? _pc;
    private readonly ISysInfoManifestService? _manifests;
    private readonly ScriptRunner? _runner;

    /// <summary>脚本集合目录（<c>&lt;脚本目录&gt;\sysinfo</c>）。</summary>
    private string _sysInfoDir = "";

    // ==================== 状态 ====================
    private List<SysInfoScript> _scripts = new();
    private List<SysInfoColumn> _manifestColumns = new();
    private IReadOnlyList<ListColumn> _columns = Array.Empty<ListColumn>();

    /// <summary>列位 → 该列在当前 <c>columns[]</c> 数组里的下标（排序 / 取值用）。</summary>
    private readonly Dictionary<int, int> _indexByKey = new();

    /// <summary>按脚本归舱的行（⛔ 不按完成顺序追加 —— 异步多脚本结果乱序）。</summary>
    private readonly Dictionary<string, List<SysInfoRow>> _rowsByScript = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>脚本展示顺序（manifest 顺序 + 末尾 <c>builtin</c>）。</summary>
    private readonly List<string> _scriptOrder = new();

    private readonly Dictionary<string, DateTime> _lastRun = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    /// <summary>并发上限 4（卡 §三.8）。</summary>
    private readonly SemaphoreSlim _gate = new(4, 4);

    private DispatcherTimer? _timer;
    private string? _manifestError;
    private string _seedNote = "";
    private DateTime _manifestStamp = DateTime.MinValue;

    // ==================== 可绑定状态 ====================
    public ObservableCollection<SysInfoRow> Items { get; } = new();

    /// <summary>表头格（表头按钮 / 列显隐右键菜单 / 「⚙ 列设置」面板共用一份）。</summary>
    public ObservableCollection<SysInfoHeaderCell> HeaderCells { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SysInfoRow? _selected;

    /// <summary>「⚙ 列设置」面板是否展开。</summary>
    [ObservableProperty] private bool _isColumnSettingsOpen;

    /// <summary>工具条「间隔(秒)」输入值（回车生效，只动 <c>auto:true</c> 的脚本）。</summary>
    [ObservableProperty] private string _intervalText = "0";

    public bool IsNotBusy => !IsBusy;
    public bool HasSelection => Selected != null;
    public bool HasItems => Items.Count > 0;

    /// <summary>空态文案：manifest 读坏时给修法，否则给「怎么出数据」的引导（卡 §五.9）。</summary>
    public string EmptyMessage => _manifestError is null
        ? "还没有采集到信息。点「🔄 重新检测」跑一遍 sysinfo\\ 下的脚本（首次打开会自动播种默认脚本）。"
        : "⚠ " + _manifestError + "（可点「✏ 编辑 manifest」修正后重新检测）";

    /// <summary>汇总文案（几个脚本 / 多少行；状态栏用）。</summary>
    public string SummaryText
        => Items.Count == 0 ? "" : $"{Items.Count} 行 · {_scriptOrder.Count} 个来源";

    // ==================== 基类契约 ====================
    protected override string PageKey => "script-info";

    /// <summary>列元数据由 manifest 决定（顺序 = <c>columns[]</c> 数组顺序，键 = <c>c&lt;key&gt;</c>）。</summary>
    protected override IReadOnlyList<ListColumn> Columns => _columns;

    // ==================== 详情卡 ====================
    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;
    public event EventHandler? DetailChanged;

    /// <summary>详情卡正文是否显示（有选中项）。</summary>
    public bool ShowDetailBody => HasSelection;

    /// <summary>选中行所属脚本（内置采集器显示 <c>builtin</c>）。</summary>
    public string DetailScript => Selected?.ScriptId ?? "";

    /// <summary>选中行的行键。</summary>
    public string DetailRowKey => Selected?.RowKey ?? "";

    /// <summary>选中行的状态（本期不着色）。</summary>
    public string DetailStatus => Selected?.Status ?? "";

    /// <summary>选中行的各列值（详情卡里的只读列表，列标题 → 值）。</summary>
    public IReadOnlyList<SysInfoCell> DetailCells
        => Selected?.Cells ?? (IReadOnlyList<SysInfoCell>)Array.Empty<SysInfoCell>();

    private DetailCardDescriptor? _card;

    /// <summary>本页详情卡：3 个固定字段（来源 / 行键 / 状态）+ 一列只读「列 = 值」。</summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "在左侧选中一行，这里显示它的全部列值；右键列表可重跑该脚本 / 复制该行。",
        Sections =
        {
            new DetailSection
            {
                Header = "选中项",
                Fields =
                {
                    new DetailField { Label = "来源", ValuePath = nameof(DetailScript), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "行键", ValuePath = nameof(DetailRowKey), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "状态", ValuePath = nameof(DetailStatus), Kind = FieldKind.Text },
                    new DetailField
                    {
                        Kind = FieldKind.List,
                        OptionsPath = nameof(DetailCells),
                        ItemLabelPath = "Header",
                        ItemTextPath = "Text",
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "值来自 sysinfo\\ 下的脚本输出；「应用路径」组由程序内置采集（读的是本机配置，不是脚本）。",
                    }
                }
            }
        },
        Actions =
        {
            new DetailAction
            {
                Text = "📋 复制「列 = 值」",
                Classes = "primary",
                ToolTip = "把选中行的各列按「列 = 值」复制到剪贴板",
                IsVisiblePath = nameof(HasSelection),
                Command = CopySelectedCommand
            },
            new DetailAction
            {
                Text = "📂 脚本目录",
                Classes = "ghost",
                ToolTip = "打开 sysinfo\\ 脚本集合目录",
                Command = OpenScriptDirCommand
            }
        }
    };

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

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名 private [RelayCommand]，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    // ==================== 构造 ====================
    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ScriptInfoViewModel() { }

    public ScriptInfoViewModel(IAppConfigService cfg, ConfigStoreService store, IColumnLayoutService cols,
                           IScriptManagerSettingsService pc, ISysInfoManifestService manifests, ScriptRunner runner)
        : base(cols)
    {
        _cfg = cfg;
        _store = store;
        _pc = pc;
        _manifests = manifests;
        _runner = runner;

        _sysInfoDir = Path.Combine(pc.EffectiveScriptsFolder, SysInfoManifestService.SubFolderName);

        // 首次打开本页（或 manifest 缺失）时从随包默认值播种一次（只加不覆盖）
        _seedNote = SysInfoSeeder.Seed(_sysInfoDir);

        LoadManifest();
        LoadLayout();       // ⚠️ 必须在 Columns 就绪之后（基类约定）
        SyncHeaderVisibility();
        RefreshView();

        // 打开即全跑一次（不依赖定时器出首屏数据）
        _ = ReloadAllAsync();
    }

    // ==================== manifest / 列 ====================

    /// <summary>读 manifest → 建 <see cref="Columns"/> / <see cref="HeaderCells"/> / 脚本清单。</summary>
    private void LoadManifest()
    {
        _scripts = new List<SysInfoScript>();
        _manifestColumns = new List<SysInfoColumn>();
        _columns = Array.Empty<ListColumn>();
        _indexByKey.Clear();
        HeaderCells.Clear();
        _manifestError = null;

        if (_manifests is null || _pc is null) { _manifestError = "manifest 服务未就绪"; return; }

        var result = _manifests.Load(_pc.EffectiveScriptsFolder);
        _manifestStamp = SafeStamp(result.ManifestPath);

        if (!result.Ok || result.Manifest is null)
        {
            _manifestError = result.Error ?? "manifest 不可用";
            return;
        }

        _manifestColumns = result.Manifest.Columns;
        _scripts = result.Manifest.Scripts;

        var list = new List<ListColumn>(_manifestColumns.Count);
        for (var i = 0; i < _manifestColumns.Count; i++)
        {
            var c = _manifestColumns[i];
            var key = "c" + c.Key;
            _indexByKey[c.Key] = i;
            list.Add(new ListColumn(key, c.Title, c.Width));

            var header = new SysInfoHeaderCell(key, c.Key, c.Title, c.Width, c.Visible);
            // 每项自带命令：列显隐菜单 / 勾选框在弹出树或模板里，取不到页面 VM 的命令
            header.ToggleCommand = new RelayCommand(() => ToggleColumnVisCommand.Execute(header.ColumnKey));
            HeaderCells.Add(header);
        }
        _columns = list;

        // 展示顺序：manifest 脚本顺序 + 内置采集器排最后
        _scriptOrder.Clear();
        foreach (var s in _scripts) _scriptOrder.Add(s.ScriptId);
        if (!_scriptOrder.Contains("builtin", StringComparer.OrdinalIgnoreCase)) _scriptOrder.Add("builtin");
    }

    private static DateTime SafeStamp(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
    }

    /// <summary>让表头格的可见性与基类的列显隐（落盘偏好）对齐。</summary>
    private void SyncHeaderVisibility()
    {
        foreach (var h in HeaderCells) h.IsVisible = Vis(h.ColumnKey);
        UpdateHeaderTexts();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>表头文案 = 列标题 + 排序箭头（箭头归基类的排序状态管）。</summary>
    private void UpdateHeaderTexts()
    {
        foreach (var h in HeaderCells) h.Text = h.Title + SortArrow(h.ColumnKey);
    }

    // ==================== 列表刷新 ====================

    protected override void RefreshView()
    {
        UpdateHeaderTexts();

        IEnumerable<SysInfoRow> q = OrderedRows();

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(r => r.Group.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || r.RowKey.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                          || r.Cells.Any(c => c.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        q = ApplyFilters(q);

        // 排序：默认保持脚本 / 行序（manifest 与脚本输出的原始顺序最好读）
        var sort = SortColumn;
        if (sort.Length > 1 && sort[0] == 'c' && int.TryParse(sort.AsSpan(1), out var key)
            && _indexByKey.TryGetValue(key, out var idx))
        {
            var cmp = StringComparer.CurrentCultureIgnoreCase;
            q = SortDescending ? q.OrderByDescending(r => r[idx], cmp) : q.OrderBy(r => r[idx], cmp);
        }

        MergeInto(Items, q.ToList());

        if (Selected != null && !Items.Contains(Selected)) Selected = null;

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>按脚本顺序拍平全部行（每个脚本内部保持脚本输出顺序）。</summary>
    private IEnumerable<SysInfoRow> OrderedRows()
    {
        foreach (var id in _scriptOrder)
            if (_rowsByScript.TryGetValue(id, out var rows))
                foreach (var r in rows) yield return r;
    }

    /// <summary>
    /// 行集合**增量合并**（卡 §五.5：定时刷新时选中项与详情卡不丢、列表不重建、界面不闪）。
    /// 已有行对象一律复用，只移动位置 / 增删差集。
    /// </summary>
    private static void MergeInto(ObservableCollection<SysInfoRow> target, List<SysInfoRow> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(target[i])) target.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            var want = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], want)) continue;

            var at = target.IndexOf(want);
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, want);
        }
    }

    /// <summary>筛选取值：列键 <c>c&lt;n&gt;</c> → 该行第 n 列文本（卡 §三.5 的键口径）。</summary>
    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not SysInfoRow r) return null;
        if (string.Equals(columnKey, "Group", StringComparison.OrdinalIgnoreCase)) return r.Group;
        if (columnKey.Length < 2 || columnKey[0] != 'c') return null;
        if (!int.TryParse(columnKey.AsSpan(1), out var key) || !_indexByKey.TryGetValue(key, out var idx)) return null;
        return r[idx];
    }

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Items.Count > 0) Selected = Items[0];
    }

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSelectedChanged(SysInfoRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailScript));
        OnPropertyChanged(nameof(DetailRowKey));
        OnPropertyChanged(nameof(DetailStatus));
        OnPropertyChanged(nameof(DetailCells));
    }

    // ==================== 跑脚本 ====================

    /// <summary>一次脚本执行的结果（成功 / 失败 / 被跳过）。</summary>
    private sealed class ScriptOutcome
    {
        public List<SysInfoParsedRow>? Rows { get; init; }

        /// <summary>stderr 非空 / 退出码非 0 —— 只记 warn、不上报（卡 §三.2）。</summary>
        public bool Warn { get; init; }

        public string? Error { get; init; }

        public bool Canceled { get; init; }
    }

    /// <summary>🔄 重新检测：全跑（先取消在跑的定时任务，避免同一 <c>ScriptId</c> 双写）。</summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        await ReloadAllAsync();
    }

    private async Task ReloadAllAsync()
    {
        if (IsBusy) return;
        if (_runner is null) { StatusText = "⚠ 脚本执行器未就绪"; return; }

        IsBusy = true;
        try
        {
            // manifest 被改过（或上次读坏了）⇒ 重新加载列与脚本清单
            var stamp = SafeStamp(_manifests?.GetManifestPath(_pc?.EffectiveScriptsFolder ?? "") ?? "");
            if (_manifestError != null || stamp != _manifestStamp)
            {
                LoadManifest();
                LoadLayout();
                SyncHeaderVisibility();
            }

            if (_manifestError != null)
            {
                ClearAllRows();
                RefreshView();
                StatusText = "⚠ " + _manifestError + (_seedNote.Length > 0 ? "（" + _seedNote + "）" : "");
                return;
            }

            StatusText = "正在运行脚本…";
            var outcomes = await RunScriptsAsync(_scripts, CancellationToken.None).ConfigureAwait(true);

            var failed = new List<string>();
            // 手工建表（⛔ 不用 ToDictionary：两个不同扩展名的同名脚本会让它抛异常，整次检测全废）
            var byId = new Dictionary<string, SysInfoScript>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _scripts) byId[s.ScriptId] = s;
            foreach (var kv in outcomes)
            {
                var group = byId.TryGetValue(kv.Key, out var s) ? s.Group : "";
                var name = ApplyOutcome(kv.Key, group, kv.Value);
                if (name != null) failed.Add(name);
            }

            ApplyBuiltin();
            RebuildQuickFilters();
            RefreshView();

            var total = Items.Count;
            if (failed.Count > 0)
                StatusText = $"⚠ 脚本失败：{string.Join("、", failed)}" + (_seedNote.Length > 0 ? " · " + _seedNote : "");
            else if (_seedNote.StartsWith("⚠", StringComparison.Ordinal))
                StatusText = _seedNote;
            else
                StatusText = $"已重新检测 · {DateTime.Now:HH:mm:ss} · {total} 行"
                           + (_seedNote.Length > 0 ? " · " + _seedNote : "");
            _seedNote = "";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 检测失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>并发跑一批脚本（上限 4；同一 <c>ScriptId</c> 正在跑 ⇒ 直接跳过、不排队不叠加）。</summary>
    private async Task<ConcurrentDictionary<string, ScriptOutcome>> RunScriptsAsync(
        IReadOnlyList<SysInfoScript> scripts, CancellationToken ct)
    {
        var results = new ConcurrentDictionary<string, ScriptOutcome>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<Task>();

        foreach (var s in scripts)
        {
            var id = s.ScriptId;
            if (id.Length == 0) continue;
            if (!TryBeginRun(id)) continue;
            _lastRun[id] = DateTime.UtcNow;
            tasks.Add(RunOneAsync(s, results, ct));
        }

        if (tasks.Count > 0) await Task.WhenAll(tasks).ConfigureAwait(true);
        return results;
    }

    private async Task RunOneAsync(SysInfoScript script, ConcurrentDictionary<string, ScriptOutcome> sink, CancellationToken ct)
    {
        var id = script.ScriptId;
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(true);
            try
            {
                var path = script.ResolvePath(_sysInfoDir);
                if (path.Length == 0 || !File.Exists(path))
                {
                    sink[id] = new ScriptOutcome { Error = "找不到脚本文件" };
                    return;
                }

                var res = await _runner!.RunAsync(new ScriptRunRequest
                {
                    ScriptPath = path,
                    Interpreter = ScriptInterpreter.Auto,
                    WorkingDirectory = _sysInfoDir,
                    TimeoutSeconds = script.Timeout,
                }, ct).ConfigureAwait(true);

                if (res.Canceled) { sink[id] = new ScriptOutcome { Canceled = true }; return; }

                // 整批失败：进程没起来 / 超时 / 输出编码非法 / 退出码非 0 且 stdout 空
                if (res.StdOut.Trim().Length == 0)
                {
                    sink[id] = new ScriptOutcome { Error = res.Error ?? "没有任何输出" };
                    return;
                }

                if (!SysInfoOutputParser.TryParse(res.StdOut, _manifestColumns.Count, out var rows, out var parseError))
                {
                    sink[id] = new ScriptOutcome { Error = res.Error ?? parseError };
                    return;
                }

                sink[id] = new ScriptOutcome
                {
                    Rows = rows,
                    Warn = !res.Ok || res.StdErr.Trim().Length > 0,
                    Error = res.Ok ? null : res.Error,   // 退出码非 0 但输出可解析 → 只 warn
                };
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { sink[id] = new ScriptOutcome { Canceled = true }; }
        catch (Exception ex) { sink[id] = new ScriptOutcome { Error = ex.Message }; }
        finally { EndRun(id); }
    }

    private bool TryBeginRun(string id)
    {
        lock (_stateLock) { return _running.Add(id); }
    }

    private void EndRun(string id)
    {
        lock (_stateLock) { _running.Remove(id); }
    }

    /// <summary>
    /// 把一次执行结果合并进「按脚本归舱」的行表：按 <see cref="SysInfoRow.RowKey"/> 就地改值 / 新增追加 / 消失删除；
    /// 失败 ⇒ 保留上次值、整组标 <c>error</c>。
    /// </summary>
    /// <returns>失败时的脚本名（给人看的短名），成功返回 null。</returns>
    private string? ApplyOutcome(string scriptId, string group, ScriptOutcome outcome)
    {
        if (outcome.Canceled) return null;

        if (outcome.Error != null && outcome.Rows is null)
        {
            // 保留上次值并标 error（整页不炸，卡 §二.9）
            if (_rowsByScript.TryGetValue(scriptId, out var old))
                foreach (var r in old) r.Status = SysInfoStatus.Error;
            return scriptId;
        }

        if (outcome.Rows is null) return null;

        // 同一 ScriptId 内 RowKey 必须唯一：后者覆盖前者（卡 §三.3）
        var order = new List<string>();
        var byKey = new Dictionary<string, SysInfoParsedRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in outcome.Rows)
        {
            if (!byKey.ContainsKey(p.RowKey)) order.Add(p.RowKey);
            byKey[p.RowKey] = p;
        }

        _rowsByScript.TryGetValue(scriptId, out var current);
        var existing = new Dictionary<string, SysInfoRow>(StringComparer.OrdinalIgnoreCase);
        if (current != null)
            foreach (var r in current)
                if (!existing.ContainsKey(r.RowKey)) existing[r.RowKey] = r;

        var next = new List<SysInfoRow>(order.Count);
        foreach (var key in order)
        {
            var p = byKey[key];
            if (existing.TryGetValue(key, out var row))
            {
                ApplyValues(row, p.Values);
                row.Status = outcome.Warn ? SysInfoStatus.Warn : p.Status;
            }
            else
            {
                row = BuildRow(scriptId, group, key, p.Values, outcome.Warn ? SysInfoStatus.Warn : p.Status);
            }
            next.Add(row);
        }

        _rowsByScript[scriptId] = next;
        return null;
    }

    /// <summary>就地改一行的值（⛔ 不换行对象 ⇒ 选中项 / 详情卡不丢）。</summary>
    private void ApplyValues(SysInfoRow row, string[] values)
    {
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var key = row.Cells[i].Key;
            if (key - 1 < values.Length) row.Cells[i].Text = values[key - 1];
        }
        if (ReferenceEquals(row, Selected)) OnPropertyChanged(nameof(DetailCells));
    }

    private SysInfoRow BuildRow(string scriptId, string group, string rowKey, string[] values, string status)
    {
        var cells = new List<SysInfoCell>(_manifestColumns.Count);
        foreach (var c in _manifestColumns)
        {
            var text = c.Key - 1 < values.Length ? values[c.Key - 1] : "";
            cells.Add(new SysInfoCell("c" + c.Key, c.Key, c.Title, text, Vis("c" + c.Key)));
        }
        return new SysInfoRow(scriptId, rowKey, group, cells) { Status = status };
    }

    private void ClearAllRows()
    {
        _rowsByScript.Clear();
        _scriptOrder.Clear();
        Items.Clear();
    }

    /// <summary>
    /// 内置采集器（<c>ScriptId="builtin"</c>，组 = 「应用路径」，卡 §三.6 / D3）：
    /// 这些值是注入服务的**生效值**，脚本读不到 profiles 上下文，因此保留 C# 采集。
    /// </summary>
    private void ApplyBuiltin()
    {
        const string id = "builtin";
        const string group = "应用路径";

        var rows = new List<SysInfoParsedRow>();

        void Add(string name, string value, string note)
            => rows.Add(new SysInfoParsedRow
            {
                RowKey = name,
                Status = SysInfoStatus.Ok,
                Values = new[]
                {
                    group, name,
                    string.IsNullOrWhiteSpace(value) ? "（留空 = 用系统默认）" : value,
                    note,
                },
            });

        if (_cfg is { } cfg)
        {
            Add("ffmpeg 路径", cfg.FfmpegPath, "留空或写 ffmpeg 则走 PATH；ffprobe 由它推导");
            Add("外部播放器", cfg.PlayerPath, "视频列表「▶ 播放」用它；留空 = 系统默认关联程序");
            Add("Python 路径", cfg.PythonPath, "信息面板页里 .py 脚本的解释器；在「设置 → 路径与配置」里配");
            Add("PowerShell 7+（pwsh）", cfg.PwshPath, ".ps1 优先用它；在「设置 → 路径与配置」里配");
            Add("Windows PowerShell 5.1", cfg.PowerShell51Path, "没配 pwsh 时 .ps1 用它；在「设置 → 路径与配置」里配");
            Add("数据目录", cfg.DataDirectory, "newtoys.db 等；环境变量 NEWTOYS_HOME 可整根切换");
            Add("更新检查地址", cfg.UpdateUrl, "「设置 → 通用」用它检查更新；留空 = 不检查");
            Add("配置文件", cfg.ConfigFilePath, "app.json 全路径");
        }
        Add("脚本目录", _pc?.EffectiveScriptsFolder ?? "", "本页脚本集合在它下面的 sysinfo\\ 里");
        Add("配置目录", _store?.ConfigDir ?? "", "本配置档的 config 目录：各模块设置 / columnlayout.json …");

        // 走同一条合并路径 ⇒ 定时 / 重复检测时行对象复用（选中项不丢）
        ApplyOutcome(id, group, new ScriptOutcome { Rows = rows });
    }

    /// <summary>快捷筛选：每个脚本的 <c>group</c> → 「类型：&lt;分组&gt;」（映射到第 1 列的取值）。</summary>
    private void RebuildQuickFilters()
    {
        if (_manifestColumns.Count == 0) return;
        var firstKey = "c" + _manifestColumns[0].Key;
        SetQuickFilters(OrderedRows()
            .Select(r => r.Group)
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Label: "类型：" + g, ColumnKey: firstKey, Value: g)));
    }

    // ==================== 定时刷新（per-script） ====================

    /// <summary>页面进入可视树时调（本页只在**当前激活**时工作：切走即停，回来补一次到期脚本）。</summary>
    public void StartAutoRefresh()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <summary>页面离开可视树时调（暂停定时器；在跑的脚本不强杀 —— 让它们自然结束）。</summary>
    public void StopAutoRefresh()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e) => _ = TickAsync();

    /// <summary>1s tick：只跑「<c>auto:true</c> 且 <c>interval&gt;0</c> 且到期」的脚本（无人值守白名单）。</summary>
    private async Task TickAsync()
    {
        if (IsBusy || _runner is null || _scripts.Count == 0) return;

        var now = DateTime.UtcNow;
        var due = new List<SysInfoScript>();
        foreach (var s in _scripts)
        {
            if (!s.Auto || s.Interval < SysInfoScript.MinIntervalSeconds) continue;
            if (_lastRun.TryGetValue(s.ScriptId, out var last) && (now - last).TotalSeconds < s.Interval) continue;

            lock (_stateLock) { if (_running.Contains(s.ScriptId)) continue; }
            due.Add(s);
        }
        if (due.Count == 0) return;

        var outcomes = await RunScriptsAsync(due, CancellationToken.None).ConfigureAwait(true);

        var failed = 0;
        var byId = new Dictionary<string, SysInfoScript>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in due) byId[s.ScriptId] = s;
        foreach (var kv in outcomes)
        {
            var group = byId.TryGetValue(kv.Key, out var s) ? s.Group : "";
            if (ApplyOutcome(kv.Key, group, kv.Value) != null) failed++;
        }

        RefreshView();
        if (failed > 0) StatusText = $"⚠ 脚本失败（定时刷新）：{failed} 个";
    }

    // ==================== 列显隐 / 列设置 ====================

    /// <summary>
    /// 列显隐右键菜单 / 「⚙ 列设置」面板的勾选动作。
    /// <para>先走基类的 <c>ToggleColumnCommand</c>（翻转 + 落 <c>columnlayout.json</c>），再同步表头与行内单元格的可见性。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleColumnVis(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        ToggleColumnCommand.Execute(key);
        SyncHeaderVisibility();
        ApplyCellVisibility();
    }

    /// <summary>列显隐变化后同步行内单元格（行对象不重建，只翻 flag）。</summary>
    private void ApplyCellVisibility()
    {
        foreach (var row in OrderedRows())
            foreach (var c in row.Cells)
                c.IsVisible = Vis(c.ColumnKey);
    }

    /// <summary>「⚙ 列设置」展开 / 收起（列宽也在那个面板里改，保存即写回 manifest）。</summary>
    [RelayCommand]
    private void ToggleColumnSettings() => IsColumnSettingsOpen = !IsColumnSettingsOpen;

    /// <summary>💾 把面板里的列宽写回 manifest（宽度唯一来源 = manifest，卡 §三.5）。</summary>
    [RelayCommand]
    private void SaveColumnWidths()
    {
        if (_manifests is null || _pc is null) { StatusText = "⚠ manifest 服务未就绪"; return; }

        var widths = HeaderCells.ToDictionary(h => h.Key, h => h.Width ?? "");
        StatusText = _manifests.SaveColumnWidths(_pc.EffectiveScriptsFolder, widths);

        // 写回成功 ⇒ 重新加载列宽（规格串由 Columns 驱动，必须重建列元数据）
        LoadManifest();
        SyncHeaderVisibility();
        RefreshView();
    }

    // ==================== 操作 ====================

    /// <summary>📋 复制选中行的「列 = 值」。</summary>
    [RelayCommand]
    private async Task CopySelectedAsync()
    {
        if (Selected is not { } row) { StatusText = "先在左侧选中一行。"; return; }
        await CopyTextAsync(RowText(row));
    }

    /// <summary>📋 复制全部（制表符分隔，可直接粘进表格）。</summary>
    [RelayCommand]
    private async Task CopyAllAsync()
    {
        if (Items.Count == 0) { StatusText = "还没有数据。"; return; }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join('\t', HeaderCells.Select(h => h.Title)));
        foreach (var r in Items) sb.AppendLine(string.Join('\t', r.Cells.Select(c => c.Text)));
        await CopyTextAsync(sb.ToString());
    }

    /// <summary>复制某一行（右键菜单）。</summary>
    [RelayCommand]
    private async Task CopyRowAsync(SysInfoRow? row)
    {
        if (row is null) return;
        await CopyTextAsync(RowText(row));
    }

    private static string RowText(SysInfoRow row)
        => string.Join(Environment.NewLine, row.Cells.Where(c => c.IsVisible).Select(c => $"{c.Header} = {c.Text}"));

    private async Task CopyTextAsync(string text)
    {
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>该行的脚本（<c>builtin</c> 没有脚本文件）。</summary>
    private SysInfoScript? FindScript(string scriptId)
        => _scripts.FirstOrDefault(s => string.Equals(s.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase));

    /// <summary>🔄 重跑该脚本（右键菜单；只刷新这个脚本的行）。</summary>
    [RelayCommand]
    private async Task RerunScriptAsync(SysInfoRow? row)
    {
        if (row is null || _runner is null) return;

        if (string.Equals(row.ScriptId, "builtin", StringComparison.OrdinalIgnoreCase))
        {
            ApplyBuiltin();
            RefreshView();
            StatusText = "已刷新内置采集（应用路径）。";
            return;
        }

        var script = FindScript(row.ScriptId);
        if (script is null) { StatusText = "⚠ manifest 里没有这个脚本"; return; }

        var outcomes = await RunScriptsAsync(new[] { script }, CancellationToken.None).ConfigureAwait(true);
        var reported = outcomes.TryGetValue(script.ScriptId, out var outcome)
            ? ApplyOutcome(script.ScriptId, script.Group, outcome)
            : null;
        RefreshView();

        StatusText = reported != null
            ? $"⚠ 脚本失败：{script.ScriptId}"
            : $"已重跑 {script.ScriptId} · {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>✏ 编辑该行对应的脚本（右键菜单；<c>builtin</c> 没有文件）。</summary>
    [RelayCommand]
    private void EditScript(SysInfoRow? row)
    {
        if (row is null) return;
        var script = FindScript(row.ScriptId);
        if (script is null) { StatusText = "⚠ 内置采集器没有脚本文件"; return; }
        OpenWithShell(script.ResolvePath(_sysInfoDir), "脚本");
    }

    /// <summary>📂 打开脚本集合目录。</summary>
    [RelayCommand]
    private void OpenScriptDir()
    {
        try
        {
            if (!Directory.Exists(_sysInfoDir)) Directory.CreateDirectory(_sysInfoDir);
            Process.Start(new ProcessStartInfo("explorer.exe", _sysInfoDir) { UseShellExecute = true });
            StatusText = "已打开脚本目录：" + _sysInfoDir;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>✏ 编辑 manifest（用系统默认程序打开 json）。</summary>
    [RelayCommand]
    private void EditManifest()
    {
        var path = _manifests?.GetManifestPath(_pc?.EffectiveScriptsFolder ?? "") ?? "";
        if (path.Length == 0 || !File.Exists(path)) { StatusText = "⚠ 找不到 sysinfo.manifest.json"; return; }
        OpenWithShell(path, "manifest");
    }

    private void OpenWithShell(string path, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = $"已打开{what}：{path}";
        }
        catch (Exception ex) { StatusText = $"打开{what}失败：" + ex.Message; }
    }

    /// <summary>⏱ 工具条「间隔(秒)」：只改 manifest 里 <c>auto:true</c> 的脚本（本次会话生效，落盘仍以 manifest 为准）。</summary>
    [RelayCommand]
    private void ApplyInterval()
    {
        if (!int.TryParse((IntervalText ?? "").Trim(), out var seconds) || seconds < 0)
        {
            StatusText = "⚠ 间隔要填秒数（0 = 关闭定时刷新）";
            return;
        }

        var count = 0;
        foreach (var s in _scripts)
        {
            if (!s.Auto) continue;
            s.Interval = seconds == 0 ? 0 : Math.Max(seconds, SysInfoScript.MinIntervalSeconds);
            count++;
        }

        StatusText = count == 0
            ? "⚠ manifest 里没有 auto:true 的脚本（无人值守只跑它们）"
            : $"已把 {count} 个自动脚本的间隔设为 {seconds}s（本次会话生效；要长期生效请改 manifest 的 interval）";
    }

    // ==================== 导出 / 导入脚本集合 ====================

    /// <summary>📦 导出：把整个 <c>sysinfo\</c> 目录打包成 zip（含 manifest）。</summary>
    [RelayCommand]
    private async Task ExportPackAsync()
    {
        if (!Directory.Exists(_sysInfoDir)) { StatusText = "⚠ 脚本目录还不存在。"; return; }

        var path = await DialogHooks.SaveFileAsync("导出信息面板脚本集合", _sysInfoDir, "sysinfo.zip", "*.zip");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(_sysInfoDir))
                {
                    var fileName = Path.GetFileName(file);
                    // 跳过本页自己的标记 / 备份文件（否则导出的包再导入会被自己的校验规则拒绝）
                    if (fileName.StartsWith('.')) continue;
                    if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    archive.CreateEntryFromFile(file, fileName, CompressionLevel.Optimal);
                }
            }

            StatusText = "已导出脚本集合：" + path;
        }
        catch (Exception ex) { StatusText = "⚠ 导出失败：" + ex.Message; }
    }

    /// <summary>
    /// 📂 导入：从 zip 恢复脚本集合。**逐条目校验**（拒绝绝对路径 / <c>..</c> / 非白名单扩展名），
    /// 已存在的文件先备份为 <c>*.bak</c>；任一条目不合格 ⇒ 整包拒绝（一个文件都不落盘）。
    /// </summary>
    [RelayCommand]
    private async Task ImportPackAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择信息面板脚本集合 zip", _sysInfoDir, false, "*.zip");
        var src = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(src)) return;

        try
        {
            using var zip = ZipFile.OpenRead(src);
            var plans = new List<(ZipArchiveEntry Entry, string Target)>();
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    continue;   // 目录条目

                var name = entry.FullName.Replace('\\', '/');

                // ① 路径合法性先判（含 ../ 的条目必须整包拒绝，⛔ 不能被下面的"跳过"规则吞掉）
                if (name.Contains("..", StringComparison.Ordinal) || name.StartsWith("/", StringComparison.Ordinal)
                    || name.Contains(':', StringComparison.Ordinal))
                {
                    StatusText = $"⚠ 导入被拒绝：条目「{entry.FullName}」路径非法";
                    return;
                }

                // ② 本页自己的标记 / 备份：静默跳过（不是攻击）
                if (string.Equals(name, SysInfoSeeder.MarkerFileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Contains('/', StringComparison.Ordinal))
                {
                    StatusText = $"⚠ 导入被拒绝：条目「{entry.FullName}」在子目录里（脚本集合只允许平铺）";
                    return;
                }

                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not (".json" or ".ps1" or ".py" or ".bat" or ".cmd"))
                {
                    StatusText = $"⚠ 导入被拒绝：条目「{entry.FullName}」扩展名 {ext} 不在允许列表";
                    return;
                }

                plans.Add((entry, Path.Combine(_sysInfoDir, name)));
            }

            if (plans.Count == 0) { StatusText = "⚠ 这个 zip 里没有可导入的文件。"; return; }

            Directory.CreateDirectory(_sysInfoDir);
            var restored = 0;
            foreach (var (entry, target) in plans)
            {
                if (File.Exists(target))
                {
                    var backup = target + ".bak";
                    try { File.Delete(backup); } catch { /* 旧备份删不掉就覆盖失败，下面会报 */ }
                    File.Move(target, backup);
                }
                entry.ExtractToFile(target, overwrite: true);
                restored++;
            }

            LoadManifest();
            SyncHeaderVisibility();
            await ReloadAllAsync().ConfigureAwait(true);
            StatusText = $"已导入 {restored} 个文件（原有文件备份为 *.bak）· {StatusText}";
        }
        catch (Exception ex) { StatusText = "⚠ 导入失败：" + ex.Message; }
    }
}
