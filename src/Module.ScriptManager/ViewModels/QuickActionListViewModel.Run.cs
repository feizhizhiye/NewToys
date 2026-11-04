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

public abstract partial class QuickActionListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    // ==================== 运行 ====================

    /// <summary>
    /// 运行一条命令（行内「运行」传本行、详情卡与右键菜单传当前选中项）。
    /// <para>命令经可见终端窗口执行（优先 pwsh，需要管理员权限的走 UAC 提权，窗口同样可见）。
    /// 输出直接显示在弹出的窗口里，状态行只回报「已打开窗口运行」。</para>
    /// </summary>
    [RelayCommand]
    private async Task RunAsync(QuickAction? item)
    {
        var target = item ?? Selected;
        if (target == null || _svc == null) return;
        if (IsBusy) return;

        IsBusy = true;
        StatusText = $"正在运行：{target.Title} …";
        try
        {
            var (ok, message) = await _svc.RunAsync(target).ConfigureAwait(true);
            StatusText = ok ? message : "⚠ " + message;
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 运行失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ==================== 批量导入脚本目录（v1.35） ====================

    /// <summary>认得的脚本扩展名（小写，带点）。导入时只挑这些。</summary>
    public static readonly string[] ScriptExtensions = { ".py", ".ps1", ".bat", ".cmd" };

    /// <summary>文件选择器的类型过滤（<c>;</c> 分隔的通配模式，见 <c>App.BuildFileTypes</c>）。</summary>
    public static string ScriptFilePattern => string.Join(";", ScriptExtensions.Select(e => "*" + e));

    /// <summary>导入子目录里的脚本吗（工具条上的勾选框直接读它；只对「导入文件夹」生效）。</summary>
    [ObservableProperty] private bool _importSubFolders = true;

    /// <summary>上一次导入的结论（状态行就是它）。</summary>
    public string ImportResultText { get; private set; } = "";

    private static string ScriptExtensionsList => string.Join(" / ", ScriptExtensions);

    /// <summary>
    /// 📥 导入**一批脚本文件**（工具条「导入文件 / 导入文件夹」两路共用）。
    /// <para>
    /// 规则（都写进状态行，绝不"悄悄跳过"）：
    /// ① 只收 <see cref="ScriptExtensions"/> 里的扩展名；
    /// ② 清单里已有的（按脚本文件名字比对）**跳过**，不重复建；
    /// ③ 落在脚本目录内的存**相对文件名**、目录外的存绝对路径（路径一律以**配置的脚本目录**为准）；
    /// ④ 名称取文件名（去扩展名）、分类填「导入」，方便之后批量改。
    /// </para>
    /// <para>
    /// ⚠️ 导入后**清掉搜索 / 分类 / 表头筛选**并选中第一条新增 —— 否则新条目可能被当前筛选挡住，
    /// 看起来像"导入没生效"（与「新增」同一口径）。
    /// </para>
    /// </summary>
    /// <returns>(新增几条, 跳过几条)。</returns>
    public (int Added, int Skipped) ImportScriptFiles(IEnumerable<string> files)
    {
        if (_svc == null) return (0, 0);

        var scriptsDir = ScriptsDirectory;
        var existing = new HashSet<string>(
            _svc.Items.Select(i => (i.ScriptFile ?? "").Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var skipped = 0;
        QuickAction? first = null;

        foreach (var file in files ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file)) continue;
            if (!ScriptExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;

            var stored = ToStoredScriptPath(file, scriptsDir);
            if (existing.Contains(stored)) { skipped++; continue; }

            var item = _svc.Add(_pageKind);
            item.Kind = QuickActionKind.Script;
            item.Title = Path.GetFileNameWithoutExtension(file);
            // 分类**不自动填**（v3.62：分类 = 功能标签，由用户在详情卡的 chips 上点；⛔ 不再写"导入"）
            item.Category = "";
            item.Description = $"导入：{Path.GetFileName(file)}";
            item.ScriptFile = stored;
            item.Interpreter = ScriptInterpreter.Auto;
            AttachToCurrentLibrary(item);   // 收录进当前库（「全部库」= 默认库）
            existing.Add(stored);
            added++;
            first ??= item;
        }

        if (added > 0)
        {
            _svc.Save();
            RebuildCategories();
            _suppress = true;
            SearchText = "";
            if (CategoryOptions.Count > 0) SelectedCategory = CategoryOptions[0];
            _suppress = false;
            ResetFilters();
            ApplyView();
            if (first != null) Selected = Items.FirstOrDefault(i => ReferenceEquals(i, first)) ?? first;
        }

        ImportResultText = added == 0
            ? $"没有可导入的新脚本（只认 {ScriptExtensionsList}）"
              + (skipped > 0 ? $"；{skipped} 个已在清单里，已跳过" : "")
            : $"已导入 {added} 个脚本"
              + (skipped > 0 ? $"；{skipped} 个已存在，已跳过" : "");
        StatusText = ImportResultText;
        return (added, skipped);
    }

    /// <summary>📥 导入一个**目录**里的脚本（<paramref name="includeSubFolders"/> = 是否连子目录一起收）。</summary>
    public (int Added, int Skipped) ImportScriptFolder(string folder, bool includeSubFolders = true)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return (0, 0);

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*",
                        includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .ToList();   // 先固化，避免枚举中途目录变动
        }
        catch { return (0, 0); }

        return ImportScriptFiles(files);
    }

    /// <summary>
    /// 落盘的路径怎么存：脚本目录**内**的文件存**相对文件名**（换机器 / 换目录也能跑），目录外的存绝对路径。
    /// </summary>
    private static string ToStoredScriptPath(string file, string scriptsDir)
        => !string.IsNullOrWhiteSpace(scriptsDir)
           && file.StartsWith(scriptsDir, StringComparison.OrdinalIgnoreCase)
            ? file[scriptsDir.Length..].TrimStart('\\', '/')
            : file;

    // ==================== 工具条（/ 需求 §4.8.5：由 Shell 统一渲染） ====================

    /// <summary>导出时建议的文件名（两页不同：<c>script-library-export.json</c> / <c>script-commands-export.json</c>）。</summary>
    protected virtual string ExportSuggestedName => "script-export.json";

    /// <summary>搜索框的悬停提示（「命令库」页原本没有 → 派生类覆盖成 null）。</summary>
    protected virtual string? SearchToolTip => "即时过滤（本地）";

    /// <summary>
    /// 夹在「搜索框」与「导入 / 导出」之间的**页专属**项（默认空）：
    /// 「脚本库」页放「导入脚本」按钮 + 脚本目录只读框；「命令库」页没有。
    /// </summary>
    protected virtual IEnumerable<ToolbarItemDescriptor> ToolbarExtras => Array.Empty<ToolbarItemDescriptor>();

    /// <summary>
    /// 排在**共用搜索框之后**的页专属项（v3.61）：给「入口动作 / 输入框 / 搜索框」三件套后面还要挂按钮的页用
    /// （如 <c>script-library</c> 的「含子目录」；⛔ 别塞进 <see cref="ToolbarExtras"/>，否则会把搜索框挤到后面）。
    /// </summary>
    // ==================== 手动扫描（§三.6：用户自己扫，⛔ 不自动登记） ====================

    /// <summary>扫描候选（页内候选面板的数据源）。</summary>
    public ObservableCollection<ScanCandidateItem> ScanCandidates { get; } = new();

    /// <summary>候选面板是否展开（有扫描结果才展开）。</summary>
    [ObservableProperty] private bool _hasScanResult;

    /// <summary>「加入库」是否可点 = 至少勾了一条新文件。</summary>
    public bool CanAddScanCandidates => ScanCandidates.Any(c => c.Selectable && c.IsSelected);

    /// <summary>🔍 扫描目录：只列候选，⛔ 不自动入库（用户 2026-10-17 拍板「用户自己扫描」）。</summary>
    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        if (_svc == null) return;
        var dir = await DialogHooks.PickFolderAsync("扫描脚本目录（只列候选，不会自动入库）", ScriptsDirectory);
        if (string.IsNullOrWhiteSpace(dir)) return;

        var found = _svc.Scan(dir, true);
        ScanCandidates.Clear();
        foreach (var c in found) ScanCandidates.Add(new ScanCandidateItem(c));

        HasScanResult = ScanCandidates.Count > 0;
        OnPropertyChanged(nameof(CanAddScanCandidates));
        OnPropertyChanged(nameof(Toolbar));

        var newCount = ScanCandidates.Count(c => c.Selectable);
        var known = ScanCandidates.Count(c => c.State == ScriptCandidate.StateExisting);
        var bad = ScanCandidates.Count(c => c.State == ScriptCandidate.StateIneligible);
        StatusText = ScanCandidates.Count == 0
            ? $"「{dir}」里没有 {string.Join(" / ", ScriptExtensions)} 这类脚本文件。"
            : $"扫描完成：新文件 {newCount} · 已入库 {known} · 不可采集 {bad}（勾选后点「加入库」）";
    }

}
