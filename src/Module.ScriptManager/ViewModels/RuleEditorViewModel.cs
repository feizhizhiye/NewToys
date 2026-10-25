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

    /// <summary>目录页带过来的规则集：填起始网址 + 按 role 落到对应槽位。</summary>
    private void ApplyPendingRule(string name)
    {
        var meta = _scripts.FirstOrDefault(m => m.Name == name)
                ?? _files.Scan().FirstOrDefault(m => m.Name == name);
        if (meta is null) { StatusText = "找不到规则集：" + name; return; }

        if (meta.BaseUrl.Length > 0) StartUrl = meta.BaseUrl;
        Encoding = meta.Encoding;

        var stage = FlowConfig.StageKeys.FirstOrDefault(k => k == meta.Role.ToLowerInvariant()) ?? "entry";
        var slot = Slots.FirstOrDefault(s => s.Key == stage);
        if (slot is not null)
        {
            var opt = slot.Options.FirstOrDefault(o => o.Value == name);
            if (opt is null)
            {
                opt = new SlotOption { Value = name, Label = name };
                slot.Options.Add(opt);
            }
            slot.Selected = opt;
            SelectedSlot = slot;
        }

        StatusText = "已带入「" + name + "」→ " + stage + " 段。点「🌐 抓页」后再「🔍 试跑推荐」。";
    }

    // ==================== 抓取 / 跑 / 推荐 ====================

    [RelayCommand]
    private async Task FetchHtmlAsync()
    {
        if (StartUrl.Length == 0) { StatusText = "先填起始网址（或从目录页带一个规则集进来）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在抓取 " + StartUrl + " ……";
            var (ok, html, error) = await _run.FetchIntoAsync(StartUrl, Encoding, _run.DebugHtmlPath).ConfigureAwait(true);
            if (!ok) { StatusText = "⚠ " + error; return; }

            HtmlText = html;
            UpdateHtmlInfo();
            StatusText = $"已抓取 {html.Length} 字符（共享 HTML，试跑推荐用它）。";
        }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>跑当前槽位选中的脚本（用共享 HTML），回填命中 / 预览 / 错误。</summary>
    [RelayCommand]
    private async Task RunSegmentAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.ScriptName.Length == 0) { StatusText = "先给这个槽位选一个脚本。"; return; }
        if (HtmlText.Length == 0) { StatusText = "先点「🌐 抓页」（没有 HTML 无从求值）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在跑「" + slot.ScriptName + "」……";
            var rows = await _run.RunScriptAsync(slot.ScriptName, _run.DebugHtmlPath, StartUrl).ConfigureAwait(true);

            MergeCommands(rows);
            slot.HitText = Summarize(rows, out var missUrl);

            // 落一条运行记录（⛔ 记录失败不打断本次运行）
            try { _records.Save(slot.ScriptName, StartUrl, rows, _run.DebugHtmlPath); }
            catch { /* 运行记录是附属产物，失败只当没记 */ }

            // 🔴 段间契约：入口 / 中间段输出必须含 url，缺了显式报出来（⛔ 不静默）
            if (missUrl && slot.Key != "content")
                StatusText = $"⚠ 「{slot.ScriptName}」的输出里没有 url 字段 —— 下一段拿不到要抓的地址。";
            else
                StatusText = $"「{slot.ScriptName}」跑完：{slot.HitText}。";
        }
        catch (Exception ex) { StatusText = "⚠ 运行失败：" + FirstLine(ex.Message); }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>
    /// 试跑推荐：当前槽位的**全部候选**对同一份 HTML 求值 → 排序展示。
    /// ⛔ 只推荐不代选（用户点「✅ 用它」才写进槽位）。
    /// </summary>
    [RelayCommand]
    private async Task RecommendAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.BuiltIn) { StatusText = "先选一个可选脚本的段位。"; return; }
        if (HtmlText.Length == 0) { StatusText = "先点「🌐 抓页」——推荐要对同一份 HTML 求值。"; return; }

        var names = slot.Options.Where(o => o.Value.Length > 0).Select(o => o.Value).ToList();
        if (names.Count == 0)
        {
            StatusText = "这个段位没有候选脚本（脚本头要写 # role: " + slot.Key + "）。";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "正在对 " + names.Count + " 个候选求值……";
            var list = await _run.RunProbeAsync(
                _run.DebugHtmlPath,
                StartUrl,
                names.Select(n => (n, _files.PathOf(n)))).ConfigureAwait(true);

            Candidates.Clear();
            foreach (var c in list) Candidates.Add(c);
            SelectedCandidate = Candidates.FirstOrDefault();

            var bad = list.Count(c => c.HasError);
            var top = list.FirstOrDefault(c => c.Count > 0);
            StatusText = top is null
                ? $"没有一个候选在这份 HTML 上出条目（{names.Count} 个候选，{bad} 个报错）—— 页面可能要换起始网址。"
                : $"推荐「{top.Name}」：{top.Count} 条 · 完整度 {top.FillText}" + (bad > 0 ? $"（{bad} 个候选报错）" : "");
        }
        catch (Exception ex) { StatusText = "⚠ 试跑失败：" + FirstLine(ex.Message); }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>把推荐结果里选中的那条写进当前槽位（⛔ 不自动选，必须用户点）。</summary>
    [RelayCommand]
    private void ApplyCandidate()
    {
        var slot = SelectedSlot;
        var cand = SelectedCandidate;
        if (slot is null || cand is null) { StatusText = "先在推荐列表里选一条。"; return; }

        var opt = slot.Options.FirstOrDefault(o => o.Value == cand.Name)
               ?? new SlotOption { Value = cand.Name, Label = cand.Name };
        if (!slot.Options.Contains(opt)) slot.Options.Add(opt);
        slot.Selected = opt;
        StatusText = "已把「" + cand.Name + "」填进 " + slot.Label + " 段。";
    }

    /// <summary>右栏：载入当前槽位脚本的命令列表（<c>RULES</c>）。</summary>
    private async Task LoadCommandsAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.ScriptName.Length == 0)
        {
            _allCommands.Clear();
            ApplyFilter();
            return;
        }

        try
        {
            var list = await _run.ListCommandsAsync(slot.ScriptName).ConfigureAwait(true);
            ReplaceCommands(list);
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 读取命令失败：" + FirstLine(ex.Message);
        }
        finally { NotifyEmpty(); }
    }

    /// <summary>就地合并运行结果（⛔ 不重建集合，否则选中项丢失）。</summary>
    private void MergeCommands(IEnumerable<RuleCommand> rows)
    {
        foreach (var row in rows)
        {
            var exist = _allCommands.FirstOrDefault(c => c.Key == row.Key);
            if (exist is null) { _allCommands.Add(row); continue; }
            exist.Expr = row.Expr;
            exist.HitText = row.HitText;
            exist.Preview = row.Preview;
            exist.Error = row.Error;
        }

        ApplyFilter();
    }

    /// <summary>命中摘要「12 条 · 3/3 字段」，并回报是否缺 <c>url</c>。</summary>
    private static string Summarize(IReadOnlyList<RuleCommand> rows, out bool missUrl)
    {
        missUrl = false;
        if (rows.Count == 0) return "没有命令";

        var url = rows.FirstOrDefault(r => r.Key.Equals("url", StringComparison.OrdinalIgnoreCase));
        var count = url is not null ? ParseCount(url.HitText) : rows.Max(r => ParseCount(r.HitText));
        var filled = rows.Count(r => ParseCount(r.HitText) > 0);
        missUrl = url is null || ParseCount(url.HitText) == 0;
        return $"{count} 条 · {filled}/{rows.Count} 字段";
    }

    private static int ParseCount(string hitText)
    {
        var t = (hitText ?? "").Replace(" 条", "");
        return int.TryParse(t, out var n) ? n : 0;
    }

    // ==================== 流水线（flow） ====================

    private void RefreshFlows()
    {
        var keep = SelectedFlowName;
        FlowNames.Clear();
        foreach (var n in _flows.List()) FlowNames.Add(n);
        SelectedFlowName = keep is not null && FlowNames.Contains(keep) ? keep : null;
        NotifyToolbarChanged();
    }

    partial void OnSelectedFlowNameChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) LoadFlow(value);
    }

    /// <summary>载入一条流水线到槽位（工具条下拉选中即载入）。</summary>
    private void LoadFlow(string name)
    {
        var cfg = _flows.Load(name);
        FlowName = cfg.Name;
        if (cfg.UrlTemplate.Length > 0) StartUrl = cfg.UrlTemplate;

        foreach (var slot in Slots)
        {
            if (slot.BuiltIn) continue;
            var want = cfg.Slot(slot.Key);
            var opt = slot.Options.FirstOrDefault(o => o.Value == want);
            if (opt is null && want.Length > 0)
            {
                opt = new SlotOption { Value = want, Label = want + "（缺脚本）" };
                slot.Options.Add(opt);
            }
            slot.Selected = opt ?? slot.Options[0];
        }

        StatusText = "已载入流水线「" + cfg.Name + "」· " + cfg.Summary;
    }

    [RelayCommand]
    private void SaveFlow()
    {
        var name = (FlowName ?? "").Trim();
        if (name.Length == 0) { StatusText = "先在工具条输入框里填流水线名。"; return; }

        try
        {
            var cfg = new FlowConfig { Name = name, UrlTemplate = StartUrl };
            foreach (var slot in Slots)
                if (!slot.BuiltIn) cfg.SetSlot(slot.Key, slot.ScriptName);

            _flows.Save(cfg);
            RefreshFlows();
            SelectedFlowName = cfg.Name;
            StatusText = "已保存流水线「" + cfg.Name + "」· " + cfg.Summary;
        }
        catch (Exception ex) { StatusText = "⚠ 保存流水线失败：" + FirstLine(ex.Message); }
    }

    [RelayCommand]
    private void DeleteFlow()
    {
        var name = SelectedFlowName;
        if (string.IsNullOrWhiteSpace(name)) { StatusText = "先在工具条下拉里选一条流水线。"; return; }

        if (_flows.Delete(name))
        {
            RefreshFlows();
            FlowName = "";
            StatusText = "已删除流水线「" + name + "」。" ;
        }
        else StatusText = "⚠ 删除失败：" + name;
    }

    [RelayCommand]
    private void BackToCatalog() => _nav?.NavigateTo("web-scrape");

    // ==================== 右栏：命令编辑（写回 RULES 块） ====================

    [RelayCommand]
    private void Save()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.ScriptName.Length == 0) { StatusText = "先给这个槽位选一个脚本。"; return; }

        try
        {
            var text = _files.Read(slot.ScriptName);
            _files.Write(slot.ScriptName, ReplaceRulesBlock(text, _allCommands));
            StatusText = "已保存到 " + slot.ScriptName + ".py";
        }
        catch (Exception ex) { StatusText = "⚠ 保存失败：" + FirstLine(ex.Message); }
    }

    [RelayCommand]
    private void AddCommand()
    {
        var n = 1;
        while (_allCommands.Any(c => c.Key == "field" + n)) n++;
        var cmd = new RuleCommand { Key = "field" + n, Expr = "css:" };
        _allCommands.Add(cmd);
        Keyword = "";
        ApplyFilter();
        SelectedCommand = cmd;
        NotifyEmpty();
        StatusText = "已加一条命令，改完记得「💾 保存」。";
    }

    [RelayCommand]
    private void RemoveCommand()
    {
        if (SelectedCommand is null) return;
        var idx = Commands.IndexOf(SelectedCommand);
        _allCommands.Remove(SelectedCommand);
        ApplyFilter();
        SelectedCommand = Commands.Count == 0 ? null : Commands[Math.Min(idx, Commands.Count - 1)];
        NotifyEmpty();
    }

    partial void OnKeywordChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var kw = (Keyword ?? "").Trim();
        Commands.Clear();
        foreach (var c in _allCommands)
        {
            if (kw.Length == 0
                || c.Key.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || c.Expr.Contains(kw, StringComparison.OrdinalIgnoreCase))
                Commands.Add(c);
        }

        if (SelectedCommand is not null && !Commands.Contains(SelectedCommand))
            SelectedCommand = Commands.FirstOrDefault();
        NotifyEmpty();
    }

    private void ReplaceCommands(IEnumerable<RuleCommand> list)
    {
        var keepKey = SelectedCommand?.Key;
        _allCommands.Clear();
        _allCommands.AddRange(list);
        ApplyFilter();
        SelectedCommand = keepKey is null
            ? Commands.FirstOrDefault()
            : Commands.FirstOrDefault(c => c.Key == keepKey) ?? Commands.FirstOrDefault();
    }

    // ==================== 诊断复制 ====================

    /// <summary>
    /// 📋 复制诊断：把「现在这台机器上到底发生了什么」打包成一段纯文本（给 AI / 给自己复盘）。
    /// 🔴 必须含：段位 / 脚本名 / 起始网址 / HTML 长度 / 每字段 count + error / traceback。
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagAsync()
    {
        var sb = new StringBuilder();
        var slot = SelectedSlot;

        sb.AppendLine("# NewToys 抓取诊断");
        sb.AppendLine("段位：" + (slot?.Key ?? "—") + "（" + (slot?.Label ?? "—") + "）");
        sb.AppendLine("脚本：" + (slot?.ScriptName ?? "—"));
        sb.AppendLine("起始网址：" + (StartUrl.Length == 0 ? "—" : StartUrl));
        sb.AppendLine("HTML：" + (HtmlText.Length == 0 ? "（没抓）" : HtmlText.Length + " 字符"));
        sb.AppendLine("流水线：" + (FlowName.Length == 0 ? "—" : FlowName));
        sb.AppendLine();

        if (Candidates.Count > 0)
        {
            sb.AppendLine("## 试跑推荐");
            foreach (var c in Candidates)
                sb.AppendLine($"  {c.Name} · {c.CountText} · 完整度 {c.FillText} · {c.FieldsText}"
                            + (c.HasError ? " · 错误：" + c.Error : ""));
            sb.AppendLine();
        }

        sb.AppendLine("## 字段");
        foreach (var c in _allCommands)
        {
            sb.AppendLine($"  {c.Key} · {c.Expr} · {c.HitText}");
            if (c.HasError) sb.AppendLine("    错误：" + c.Error);
        }

        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(sb.ToString().TrimEnd());
            StatusText = "已复制诊断信息（段位 / 脚本 / 网址 / 每字段命中与错误）。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 查找 / 定位（保留 收尾能力） ====================

    private void Find(int direction)
    {
        var needle = FindText ?? "";
        if (needle.Length == 0 || HtmlText.Length == 0) { FindHint = ""; return; }

        var idx = -1;
        if (direction >= 0)
        {
            var from = Math.Min(Math.Max(HtmlSelEnd, 0), HtmlText.Length);
            idx = HtmlText.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = HtmlText.IndexOf(needle, 0, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var from = Math.Clamp(HtmlSelStart - 1, 0, Math.Max(HtmlText.Length - 1, 0));
            idx = from < 0 ? -1 : HtmlText.LastIndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = HtmlText.LastIndexOf(needle, HtmlText.Length - 1, StringComparison.OrdinalIgnoreCase);
        }

        if (idx < 0) { FindHint = "没找到"; return; }

        HtmlSelEnd = idx;
        HtmlSelStart = idx;
        HtmlSelEnd = idx + needle.Length;
        FindHint = $"第 {CountBefore(idx) + 1}/{CountTotal()} 个";
        UpdateLineInfo();
    }

    [RelayCommand] private void FindNext() => Find(1);
    [RelayCommand] private void FindPrev() => Find(-1);

    partial void OnHtmlSelStartChanged(int value) => UpdateLineInfo();

    [RelayCommand]
    private void LocateHit()
    {
        var first = (SelectedCommand?.Preview ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.Trim() ?? "";
        if (first.Length == 0) { StatusText = "先跑一次「▶ 跑这段」，选中命令要有命中结果。"; return; }
        if (first.Length > 200) first = first[..200];

        FindText = first;
        HtmlSelStart = 0;
        HtmlSelEnd = 0;
        Find(1);
        StatusText = FindHint == "没找到"
            ? "命中的这段文本在源码里找不到（页面可能是压缩过的 / 结果经过净化改写）。"
            : "已定位到「" + SelectedCommand?.Key + "」的首条命中。";
    }

    private int CountTotal()
    {
        var needle = FindText ?? "";
        if (needle.Length == 0 || HtmlText.Length == 0) return 0;
        var n = 0;
        var i = HtmlText.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (i >= 0) { n++; i = HtmlText.IndexOf(needle, i + needle.Length, StringComparison.OrdinalIgnoreCase); }
        return n;
    }

    private int CountBefore(int index)
    {
        var needle = FindText ?? "";
        if (needle.Length == 0) return 0;
        var n = 0;
        var i = HtmlText.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (i >= 0 && i < index)
        {
            n++;
            i = HtmlText.IndexOf(needle, i + needle.Length, StringComparison.OrdinalIgnoreCase);
        }
        return n;
    }

    private void UpdateLineInfo()
    {
        if (HtmlText.Length == 0) { LineInfo = ""; return; }
        var pos = Math.Clamp(HtmlSelStart, 0, HtmlText.Length);
        var line = HtmlText.Count(ch => ch == '\n') + 1;
        var cur = 1;
        for (var i = 0; i < pos; i++) if (HtmlText[i] == '\n') cur++;
        LineInfo = $"行 {cur} / {line}";
    }

    private void UpdateHtmlInfo()
    {
        if (HtmlText.Length == 0)
        {
            HtmlInfo = "还没抓 HTML。填起始网址后点「🌐 抓页」。";
            return;
        }

        var truncated = false;
        var text = HtmlText;

        if (text.Length > MaxHtmlChars) { text = text[..MaxHtmlChars]; truncated = true; }

        var lines = text.Split('\n');
        if (lines.Length > MaxHtmlLines)
        {
            text = string.Join("\n", lines.Take(MaxHtmlLines));
            truncated = true;
        }

        HtmlText = text;
        HtmlInfo = $"共 {HtmlText.Length:N0} 字符 · {HtmlText.Count(ch => ch == '\n') + 1:N0} 行"
                 + (truncated ? "（已截断显示，完整内容在缓存文件里）" : "");
        HtmlSelStart = 0;
        HtmlSelEnd = 0;
        FindHint = "";
        UpdateLineInfo();
    }

    partial void OnHtmlTextChanged(string value) => OnPropertyChanged(nameof(HasHtml));

    private void NotifyEmpty()
    {
        OnPropertyChanged(nameof(HasHtml));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(HasCandidates));
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx > 0 ? text[..idx] : text;
    }

    /// <summary>把命令列表写成 <c>RULES = [...]</c>，替换文件里原有的块（找不到就追加到末尾）。</summary>
    internal static string ReplaceRulesBlock(string text, IEnumerable<RuleCommand> commands)
    {
        var block = new StringBuilder("RULES = [\n");
        foreach (var c in commands)
            block.Append("    (\"").Append(Esc(c.Key)).Append("\", \"").Append(Esc(c.Expr)).Append("\"),\n");
        block.Append("]\n");

        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(l => l.TrimStart().StartsWith("RULES = [", StringComparison.Ordinal));
        if (start < 0) return (text ?? "").TrimEnd() + "\n\n" + block;

        var end = lines.FindIndex(start, l => l.Trim() == "]");
        if (end < 0) return (text ?? "").TrimEnd() + "\n\n" + block;

        var head = string.Join("\n", lines.Take(start));
        var tail = string.Join("\n", lines.Skip(end + 1));
        return head + "\n" + block + tail;
    }

    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
