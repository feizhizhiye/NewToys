using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// 规则编辑器（<c>web-rule-editor</c>，NT-084 批 C）—— 三段式：
/// <b>命令列表</b>（上）／ <b>HTML 源码面板</b>（下）／ <b>规则编辑</b>（右）。
/// <para>
/// 🔴 本页是**新开的非列表页**（自绘布局），⛔ 不用 `ListPageSkeleton`、⛔ 不动 340px 详情卡。
/// <br/>🔴 本页不解析规则：命令列表与命中数都由 Python 侧算（`RuleRunService`），C# 只搬运。
/// </para>
/// <para>
/// 🔴 **样式统一（NT-084 收尾）**：工具条走 <see cref="IToolbarSource"/> 描述符（Shell 的
/// <c>ToolbarHost</c> 统一渲染，⛔ 页内不写 <c>Border.toolbar</c>）；状态文本走
/// <see cref="IStatusProvider"/>（Shell 底部状态栏）—— 与目录页 / 各列表页同一套路。
/// </para>
/// </summary>
public partial class RuleEditorViewModel : ObservableObject, IStatusProvider, IToolbarSource
{
    private readonly RuleFileService _files = null!;
    private readonly RuleRunService _run = null!;
    private readonly WebTransferService _transfer = null!;
    private readonly INavigationService? _nav;

    /// <summary>当前规则集名（空 = 还没选）。</summary>
    [ObservableProperty] private string _ruleName = "";
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _encoding = "utf8";
    [ObservableProperty] private string _keyword = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _htmlText = "";
    [ObservableProperty] private string _htmlInfo = "还没抓取 HTML。点「🌐 抓取 HTML」。";
    [ObservableProperty] private RuleCommand? _selectedCommand;

    // ==================== 声明式工具条（NT-055：Shell 统一承载） ====================

    /// <summary>
    /// 工具条描述符（顺序 = 优先级，越前越不被折进「⋯」）。
    /// 🔴 页内 ⛔ 不再自绘 <c>Border.toolbar</c>（会出现两条工具条）。
    /// </summary>
    public IReadOnlyList<ToolbarItemDescriptor> Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "🌐 抓取 HTML", Classes = "ghost", CommandPath = nameof(FetchHtmlCommand),
                IsEnabledPath = "!" + nameof(IsBusy), ShowToolTipOnDisabled = true,
                ToolTip = "抓取当前规则集的页面并落缓存（重复调试不再发请求）" },
        new() { Text = "▶ 运行", Classes = "ghost", CommandPath = nameof(RunRulesCommand),
                IsEnabledPath = "!" + nameof(IsBusy), ShowToolTipOnDisabled = true,
                ToolTip = "对缓存 HTML 跑全部命令，回填命中数与预览" },
        new() { Kind = ToolbarItemKind.TextField, Width = 88, Placeholder = "搜索命令…", ValuePath = nameof(Keyword) },
        new() { Text = "💾 保存", Classes = "ghost", CommandPath = nameof(SaveCommand),
                ToolTip = "把命令列表写回规则文件的 RULES 块（⛔ 无自动保存）" },
        new() { Text = "🔄 重读", Classes = "ghost", CommandPath = nameof(ReloadCommand),
                IsEnabledPath = "!" + nameof(IsBusy), ShowToolTipOnDisabled = true,
                ToolTip = "重新从规则文件载入命令（外部改完用）" },
        new() { Text = "◀ 返回目录", Classes = "ghost", CommandPath = nameof(BackToCatalogCommand),
                ToolTip = "回「抓取规则」目录页" }
    };

    /// <summary>工具条构成变化通知（本页工具条静态，⛔ 不触发；帮助方法的存在即仓库同款写法，消 CS0067）。</summary>
    public event EventHandler? ToolbarChanged;

    /// <summary>工具条构成变了 → 通知 Shell（⛔ 只有动态构造 <see cref="Toolbar"/> 时才需要调）。</summary>
    private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>底部状态栏（v3.9 / <see cref="IStatusProvider"/>）：Shell 统一显示。</summary>
    public event EventHandler? StatusChanged;

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>右栏抬头：当前规则集名（没选时给占位，⛔ 不留空行）。</summary>
    public string HeaderTitle => string.IsNullOrWhiteSpace(RuleName) ? "（未选择规则集）" : RuleName;

    partial void OnRuleNameChanged(string value) => OnPropertyChanged(nameof(HeaderTitle));

    // ==================== HTML 面板：查找 / 定位（NT-084 收尾） ====================

    /// <summary>查找词（Ctrl+F）。</summary>
    [ObservableProperty] private string _findText = "";

    /// <summary>查找结果提示（「第 2/17 个」/「没找到」）。</summary>
    [ObservableProperty] private string _findHint = "";

    /// <summary>光标所在行信息（「行 12 / 3456」）—— 行号栏的降级形态，见 <see cref="UpdateLineInfo"/>。</summary>
    [ObservableProperty] private string _lineInfo = "";

    /// <summary>HTML 文本框选区起点（与控件**双向绑定**：VM 设置它即可选中并滚过去）。</summary>
    [ObservableProperty] private int _htmlSelStart;

    /// <summary>HTML 文本框选区终点。</summary>
    [ObservableProperty] private int _htmlSelEnd;

    public ObservableCollection<RuleCommand> Commands { get; } = new();

    /// <summary>命令全量主表（保存 / 增删都动它）；<see cref="Commands"/> = 经 <see cref="Keyword"/> 过滤后的视图。</summary>
    private readonly List<RuleCommand> _allCommands = new();

    public bool HasRule => !string.IsNullOrWhiteSpace(RuleName);
    public bool HasCommands => Commands.Count > 0;
    public bool HasHtml => HtmlText.Length > 0;

    /// <summary>一次显示的 HTML 上限（超过就截断，⛔ 别把 2MB 塞进 TextBox）。</summary>
    private const int MaxHtmlChars = 400_000;

    /// <summary>一次显示的 HTML 行数上限。</summary>
    private const int MaxHtmlLines = 8000;

    // ==================== 构造 ====================

    public RuleEditorViewModel() { }

    public RuleEditorViewModel(RuleFileService files, RuleRunService run, WebTransferService transfer, INavigationService? nav = null)
    {
        _files = files;
        _run = run;
        _transfer = transfer;
        _nav = nav;

        StatusText = "从「抓取规则」页点「✏ 编辑规则」进来，或在本页选择规则集。";

        // 目录页点「✏ 编辑规则」→ 这里消费一次性参数
        var pending = _transfer?.TakeRuleSet();
        if (!string.IsNullOrWhiteSpace(pending)) LoadRule(pending!);
    }

    // ==================== 载入 / 保存 ====================

    /// <summary>载入一条规则集（读文件头 + 用 Python 读出 RULES）。</summary>
    public void LoadRule(string name) => _ = LoadRuleAsync(name);

    private async Task LoadRuleAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_files.Exists(name)) { StatusText = "找不到规则集：" + name; return; }

        RuleName = name;
        var (b, e) = _files.ReadMeta(name);
        BaseUrl = b;
        Encoding = e;

        HtmlText = _run.LoadCachedHtml(name);
        UpdateHtmlInfo();
        await ReloadCommandsAsync().ConfigureAwait(true);
    }

    private async Task ReloadCommandsAsync()
    {
        try
        {
            IsBusy = true;
            var list = await _run.ListCommandsAsync(RuleName).ConfigureAwait(true);
            ReplaceCommands(list);
            StatusText = $"已载入 {list.Count} 条命令。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 读取命令失败：" + FirstLine(ex.Message);
        }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!HasRule) { StatusText = "先选一条规则集。"; return; }
        try
        {
            var text = _files.Read(RuleName);
            // 🔴 写主表（全量）：⛔ 不写 Commands（那只是过滤视图，会把被关键词藏起来的命令存丢）
            _files.Write(RuleName, ReplaceRulesBlock(text, _allCommands));
            StatusText = "已保存到 " + RuleName + ".py";
            await Task.CompletedTask;
        }
        catch (Exception ex) { StatusText = "⚠ 保存失败：" + FirstLine(ex.Message); }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (!HasRule) return;
        var (b, e) = _files.ReadMeta(RuleName);
        BaseUrl = b; Encoding = e;
        await ReloadCommandsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void BackToCatalog() => _nav?.NavigateTo("web-scrape");

    // ==================== 抓取 / 运行 ====================

    [RelayCommand]
    private async Task FetchHtmlAsync()
    {
        if (!HasRule) { StatusText = "先选一条规则集。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在抓取 " + BaseUrl + " ……";
            var (ok, html, error) = await _run.FetchAsync(RuleName, BaseUrl, Encoding).ConfigureAwait(true);
            if (!ok) { StatusText = "⚠ " + error; return; }

            HtmlText = html;
            UpdateHtmlInfo();
            StatusText = $"已抓取 {html.Length} 字符，已落缓存（重复调试不再发请求）。";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RunRulesAsync()
    {
        if (!HasRule) { StatusText = "先选一条规则集。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在跑命令……";
            var rows = await _run.RunRulesAsync(RuleName, BaseUrl).ConfigureAwait(true);

            // 就地合并（⛔ 不重建集合，否则选中项丢失）—— 合并进**主表**，再刷过滤视图
            foreach (var row in rows)
            {
                var exist = _allCommands.FirstOrDefault(c => c.Key == row.Key);
                if (exist is null) { _allCommands.Add(row); continue; }
                exist.HitText = row.HitText;
                exist.Preview = row.Preview;
            }

            ApplyFilter();
            StatusText = $"跑完 {rows.Count} 条命令。";
        }
        catch (Exception ex) { StatusText = "⚠ 运行失败：" + FirstLine(ex.Message); }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>只测当前选中的那条命令（结果合并回该行）。</summary>
    [RelayCommand]
    private async Task TestSelectedAsync()
    {
        if (SelectedCommand is null) { StatusText = "先选中一条命令。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在测试「" + SelectedCommand.Key + "」……";
            var rows = await _run.RunRulesAsync(RuleName, BaseUrl).ConfigureAwait(true);
            var hit = rows.FirstOrDefault(r => r.Key == SelectedCommand.Key);
            if (hit is not null)
            {
                SelectedCommand.HitText = hit.HitText;
                SelectedCommand.Preview = hit.Preview;
                StatusText = "「" + SelectedCommand.Key + "」命中 " + hit.HitText + "。";
            }
            else StatusText = "没找到这条命令的结果。";
        }
        catch (Exception ex) { StatusText = "⚠ 测试失败：" + FirstLine(ex.Message); }
        finally { IsBusy = false; }
    }

    // ==================== 命令增删 ====================

    [RelayCommand]
    private void AddCommand()
    {
        var n = 1;
        while (_allCommands.Any(c => c.Key == "field" + n)) n++;
        var cmd = new RuleCommand { Key = "field" + n, Expr = "css:" };
        _allCommands.Add(cmd);
        Keyword = "";   // 清过滤：保证新命令一定可见（否则藏在下拉筛选外，用户找不到）
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

    /// <summary>按 <see cref="Keyword"/> 过滤命令列表（字段名 / 表达式内含即命中；大小写不敏感）。</summary>
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

    // ==================== 查找 / 定位 ====================

    /// <summary>
    /// 在 HTML 里找下一个 / 上一个（<paramref name="direction"/> = 1 向后、-1 向前）。
    /// <para>🔴 用**选区**表达命中（= 系统高亮色），⛔ 不自绘高亮层：Avalonia 只读 <c>TextBox</c>
    /// 没有富文本着色 API，自绘行号列 + 自绘高亮要与滚动同步，成本高风险大。</para>
    /// <para>到头回绕（找完一圈回到第一个 / 最后一个），和常见编辑器的手感一致。</para>
    /// </summary>
    private void Find(int direction)
    {
        var needle = FindText ?? "";
        if (needle.Length == 0 || HtmlText.Length == 0) { FindHint = ""; return; }

        var idx = -1;
        if (direction >= 0)
        {
            var from = Math.Min(Math.Max(HtmlSelEnd, 0), HtmlText.Length);
            idx = HtmlText.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = HtmlText.IndexOf(needle, 0, StringComparison.OrdinalIgnoreCase);   // 回绕
        }
        else
        {
            var from = Math.Clamp(HtmlSelStart - 1, 0, Math.Max(HtmlText.Length - 1, 0));
            idx = from < 0 ? -1 : HtmlText.LastIndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = HtmlText.LastIndexOf(needle, HtmlText.Length - 1, StringComparison.OrdinalIgnoreCase);
        }

        if (idx < 0) { FindHint = "没找到"; return; }

        // ⚠️ 先缩回 End 再设 Start：否则「Start > 旧 End」这一瞬是非法选区（Avalonia 会 clamp，跳位就错）
        HtmlSelEnd = idx;
        HtmlSelStart = idx;
        HtmlSelEnd = idx + needle.Length;
        FindHint = $"第 {CountBefore(idx) + 1}/{CountTotal()} 个";
        UpdateLineInfo();
    }

    [RelayCommand] private void FindNext() => Find(1);
    [RelayCommand] private void FindPrev() => Find(-1);

    partial void OnHtmlSelStartChanged(int value) => UpdateLineInfo();

    /// <summary>把选中命令的首条命中填进查找框并**定位过去**（= 命中高亮的降级形态）。</summary>
    [RelayCommand]
    private void LocateHit()
    {
        var first = (SelectedCommand?.Preview ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.Trim() ?? "";
        if (first.Length == 0) { StatusText = "先跑一次「▶ 运行」，选中命令要有命中结果。"; return; }
        if (first.Length > 200) first = first[..200];   // 太长就没法在源码里定位了

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

    /// <summary>由选区起点推算「行 N / 共 M 行」（行号栏的降级：⛔ 不整列渲染 8000 行）。</summary>
    private void UpdateLineInfo()
    {
        if (HtmlText.Length == 0) { LineInfo = ""; return; }
        var pos = Math.Clamp(HtmlSelStart, 0, HtmlText.Length);
        var line = HtmlText.Count(ch => ch == '\n') + 1;   // 总数
        var cur = 1;
        for (var i = 0; i < pos; i++) if (HtmlText[i] == '\n') cur++;
        LineInfo = $"行 {cur} / {line}";
    }

    // ==================== 内部 ====================

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

    private void UpdateHtmlInfo()
    {
        if (HtmlText.Length == 0)
        {
            HtmlInfo = "还没抓取 HTML。点「🌐 抓取 HTML」。";
            return;
        }

        var truncated = false;
        var text = HtmlText;

        if (text.Length > MaxHtmlChars)
        {
            text = text[..MaxHtmlChars];
            truncated = true;
        }

        var lines = text.Split('\n');
        if (lines.Length > MaxHtmlLines)
        {
            text = string.Join("\n", lines.Take(MaxHtmlLines));
            truncated = true;
        }

        if (!ReferenceEquals(text, HtmlText)) HtmlText = text;
        HtmlText = text;
        HtmlInfo = $"共 {HtmlText.Length:N0} 字符 · {HtmlText.Count(ch => ch == '\n') + 1:N0} 行"
                 + (truncated ? "（已截断显示，完整内容在缓存文件里）" : "");
        HtmlSelStart = 0;
        HtmlSelEnd = 0;
        FindHint = "";
        UpdateLineInfo();
    }

    private void NotifyEmpty()
    {
        OnPropertyChanged(nameof(HasRule));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(HasHtml));
    }

    partial void OnHtmlTextChanged(string value) => OnPropertyChanged(nameof(HasHtml));

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

    /// <summary>Python 字符串字面量转义（只处理 \ 和 "）。</summary>
    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
