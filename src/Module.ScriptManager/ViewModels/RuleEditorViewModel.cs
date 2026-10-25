using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;   // 🔴 SetTextAsync / GetTextAsync 是 IClipboard 的**扩展方法**，漏了这行会报 CS1061
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Services;   // 跨页传物（书签 / Ping → 抓取）已上提 NewToys.UI
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 调试集合（<c>web-rule-editor</c>，重构）—— **装配流水线**的页：
/// 左 = 五段槽位 + 试跑推荐 ／ 中 = HTML 源码（查找 / 定位命中）／ 右 = 选中段脚本的表达式检验。
/// <para>
/// 🔴 范式：脚本从「一个 <c>.py</c> = 一个站点」降为「一个 <c>.py</c> = **一段**」，本页把段装成 flow，
/// 跑通后交给面板批量下载。**本页 ⛔ 不做批量下载**。
/// </para>
/// <para>
/// 🔴 槽位一律**可选、可留空**（<c>toc</c> 留空 = 链式模式）；<c>save</c> 是内置下载器，⛔ 不为下载写脚本。
/// </para>
/// <para>
/// 🔴 段间契约：入口 / 中间段输出**必须含 <c>url</c>**；缺了要显式报出来，⛔ 不静默。
/// </para>
/// <para>
/// 🔴 错误回显：Python 侧的 error / traceback 必须搬到界面上，⛔ 禁止静默显示「0 条」。
/// </para>
/// </summary>
public partial class RuleEditorViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    private readonly RuleFileService _files = null!;
    private readonly RuleRunService _run = null!;
    private readonly FlowService _flows = null!;
    private readonly WebTransferService _transfer = null!;
    private readonly RunRecordService _records = null!;
    private readonly INavigationService? _nav;

    [ObservableProperty] private string _startUrl = "";
    [ObservableProperty] private string _encoding = "utf8";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private FlowSlot? _selectedSlot;
    [ObservableProperty] private CandidateResult? _selectedCandidate;
    [ObservableProperty] private RuleCommand? _selectedCommand;
    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private string _flowName = "";
    [ObservableProperty] private string? _selectedFlowName;

    // ==================== 集合 ====================

    /// <summary>五段槽位（含内置 <c>save</c>）。</summary>
    public ObservableCollection<FlowSlot> Slots { get; } = new();

    /// <summary>试跑推荐结果（按「条数 &gt; 0 且字段非空率最高」排序）。</summary>
    public ObservableCollection<CandidateResult> Candidates { get; } = new();

    /// <summary>已有流水线名（供工具条下拉载入）。</summary>
    public ObservableCollection<string> FlowNames { get; } = new();

    /// <summary>右栏命令（<see cref="Keyword"/> 过滤后的视图）。</summary>
    public ObservableCollection<RuleCommand> Commands { get; } = new();

    /// <summary>命令主表（保存 / 增删都动它）。</summary>
    private readonly List<RuleCommand> _allCommands = new();

    /// <summary>脚本库（按 <c># role:</c> 过滤成各槽位的候选）。</summary>
    private readonly List<RuleSetMeta> _scripts = new();

    public bool HasHtml => HtmlText.Length > 0;
    public bool HasCandidates => Candidates.Count > 0;
    public bool HasCommands => Commands.Count > 0;

    /// <summary>右栏抬头：当前槽位 + 脚本名。</summary>
    public string HeaderTitle => SelectedSlot is null
        ? "（未选段位）"
        : SelectedSlot.Label + " · " + (SelectedSlot.ScriptName.Length == 0 ? "留空" : SelectedSlot.ScriptName);

    // ==================== 工具条（Shell 统一承载） ====================

    public IReadOnlyList<ToolbarItemDescriptor> Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "🌐 抓页", Classes = "ghost", CommandPath = nameof(FetchHtmlCommand),
                IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "抓起始网址并落共享 HTML（试跑推荐的前提：候选脚本对同一份 HTML 求值）" },
        new() { Text = "🔍 试跑推荐", Classes = "ghost", CommandPath = nameof(RecommendCommand),
                IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "把当前槽位的候选脚本对同一份 HTML 全求值，按条数 / 字段完整度排序" },
        new() { Text = "▶ 跑这段", Classes = "ghost", CommandPath = nameof(RunSegmentCommand),
                IsEnabledPath = "!" + nameof(IsBusy),
                ToolTip = "跑当前槽位选中的脚本，回填命中数 / 预览 / 错误" },
        new()
        {
            Kind = ToolbarItemKind.Combo, Width = 120,
            ItemsPath = nameof(FlowNames),
            SelectedItemPath = nameof(SelectedFlowName),
            ToolTip = "载入已保存的流水线（flows\\<名>.json）"
        },
        new() { Kind = ToolbarItemKind.TextField, Width = 120, Placeholder = "流水线名…", ValuePath = nameof(FlowName) },
        new() { Text = "💾 保存流水线", Classes = "ghost", CommandPath = nameof(SaveFlowCommand),
                ToolTip = "把五段槽位写成 flows\\<名>.json" },
        new() { Text = "🗑 删流水线", Classes = "ghost", CommandPath = nameof(DeleteFlowCommand),
                ToolTip = "删除工具条下拉里选中的流水线" },
        new() { Text = "◀ 返回目录", Classes = "ghost", CommandPath = nameof(BackToCatalogCommand),
                ToolTip = "回「抓取规则」目录页" }
    };

    public event EventHandler? ToolbarChanged;

    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedSlotChanged(FlowSlot? value)
    {
        OnPropertyChanged(nameof(HeaderTitle));
        _ = LoadCommandsAsync();
    }

    // ==================== HTML 面板：查找 / 定位（收尾，保留） ====================

    [ObservableProperty] private string _findText = "";
    [ObservableProperty] private string _findHint = "";
    [ObservableProperty] private string _lineInfo = "";
    [ObservableProperty] private int _htmlSelStart;
    [ObservableProperty] private int _htmlSelEnd;
    [ObservableProperty] private string _htmlText = "";
    [ObservableProperty] private string _htmlInfo = "还没抓 HTML。填起始网址后点「🌐 抓页」。";

    private const int MaxHtmlChars = 400_000;
    private const int MaxHtmlLines = 8000;

    // ==================== 构造 ====================

    public RuleEditorViewModel() { }

    public RuleEditorViewModel(RuleFileService files, RuleRunService run, FlowService flows,
                               WebTransferService transfer, RunRecordService records, INavigationService? nav = null)
    {
        _files = files;
        _run = run;
        _flows = flows;
        _transfer = transfer;
        _records = records;
        _nav = nav;

        StatusText = "从「抓取规则」页点「🧩 调试集合」进来；本页只装配流水线，⛔ 不跑批量下载。";

        BuildSlots();
        RefreshScripts();
        RefreshFlows();

        HtmlText = _run.LoadCachedHtml("_debug");
        UpdateHtmlInfo();

        // 目录页点「🧩 调试集合」→ 这里消费一次性参数
        var pending = _transfer?.TakeRuleSet();
        if (!string.IsNullOrWhiteSpace(pending)) ApplyPendingRule(pending!);
    }

    // ==================== 槽位 ====================

    /// <summary>造五段槽位（<c>save</c> 内置 ⛔ 不可选）。</summary>
    private void BuildSlots()
    {
        Slots.Clear();
        for (var i = 0; i < FlowConfig.StageKeys.Length; i++)
        {
            Slots.Add(new FlowSlot
            {
                Key = FlowConfig.StageKeys[i],
                Label = FlowConfig.StageLabels[i],
                Hint = FlowConfig.StageHints[i]
            });
        }

        Slots.Add(new FlowSlot
        {
            Key = "save",
            Label = "落盘",
            Hint = "内置下载器（并发 / 限速 / 去重 / 续跑 / 取消 / 进度）—— 写操作 ⛔ 不脚本化",
            BuiltIn = true,
            BuiltInText = "内置下载器"
        });

        SelectedSlot = Slots.FirstOrDefault();
    }

    /// <summary>重扫脚本库并按 <c>role</c> 重填每个槽位的候选（保留已选值）。</summary>
    private void RefreshScripts()
    {
        _scripts.Clear();
        try
        {
            _scripts.AddRange(_files.Scan().OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
        }
        catch { /* 目录读不出来就当空库，⛔ 不让整页挂掉 */ }

        foreach (var slot in Slots)
        {
            if (slot.BuiltIn) continue;

            var keep = slot.ScriptName;
            slot.Options.Clear();
            slot.Options.Add(new SlotOption { Value = "", Label = "（留空 · 跳过）" });

            foreach (var m in _scripts.Where(m => RoleMatches(m, slot.Key)))
                slot.Options.Add(new SlotOption { Value = m.Name, Label = m.Name });

            slot.Selected = slot.Options.FirstOrDefault(o => o.Value == keep) ?? slot.Options[0];
        }
    }

    /// <summary>
    /// 段位 → 候选脚本的 <c>role</c> 匹配：<c>entry</c> 槽位收 <c>role=entry</c> 与**无 role 的 pipeline**
    /// （整站脚本只能当入口，这是本期的假设，可回退）；其余槽位严格按 role。
    /// </summary>
    private static bool RoleMatches(RuleSetMeta meta, string stage)
    {
        var role = (meta.Role ?? "").Trim().ToLowerInvariant();
        if (role.Length == 0) return stage == "entry";
        return role == stage;
    }

}
