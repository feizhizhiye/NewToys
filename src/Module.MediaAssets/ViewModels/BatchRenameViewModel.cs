using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 弹层用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>批量重命名的一个目标：一个磁盘文件 + 它对应的库记录。</summary>
public sealed class BatchRenameTarget
{
    public BatchRenameTarget(AssetRow row, FileLocation loc)
    {
        Row = row;
        Loc = loc;
        OldName = Path.GetFileName(loc.Path);
        Dir = Path.GetDirectoryName(loc.Path) ?? "";
    }

    /// <summary>库里的资产行（只读：改名只动物理文件与 location 路径）。</summary>
    public AssetRow Row { get; }

    /// <summary>物理位置行（<c>Rename</c> 成功后其 <c>Path</c> 会被就地更新）。</summary>
    public FileLocation Loc { get; }

    /// <summary>改之前的文件名。</summary>
    public string OldName { get; }

    /// <summary>所在目录（查重名用）。</summary>
    public string Dir { get; }
}

/// <summary>预览表的一行（原名 → 新名 + 处理说明）。</summary>
public sealed partial class BatchRenameRow : ObservableObject
{
    public BatchRenameRow(BatchRenameTarget target) => Target = target;

    public BatchRenameTarget Target { get; }

    public string OldName => Target.OldName;

    /// <summary>规则算出来的新名（冲突且能避让时 = 避让后的名字）。</summary>
    [ObservableProperty]
    private string _newName = "";

    /// <summary>处理说明（没变 / 已加序号 / 重名原因 / 非法字符）。</summary>
    [ObservableProperty]
    private string _note = "";

    /// <summary>红色行：这个名字不能要，确认按钮会被挡住。</summary>
    [ObservableProperty]
    private bool _isProblem;

    /// <summary>不动它（名字没变，或按「跳过」策略跳过）。</summary>
    [ObservableProperty]
    private bool _skip;
}

/// <summary>
/// 批量重命名弹层的 VM：**规则 → 预览 → 逐个落盘**。
/// <para>
/// 两个页面（全部资产 / 资产浏览）各持有一个实例，弹层控件 <c>BatchRenameOverlay</c> 的 DataContext 就是它。
/// 预览在这一层算完（含重名避让），落盘仍走 <see cref="FileOperationService.Rename"/> —— 日志先行、
/// 库里的 <c>file_locations.path</c> 同步、同名冲突策略全部复用同一条通路，⛔ 不另开写盘口子。
/// </para>
/// <para>
/// 🔴 预览是**唯一**的判断依据：确认按钮只在「没有红行」时可点；红行 = 空名 / 非法字符 / 重名且策略是跳过。
/// </para>
/// </summary>
public sealed partial class BatchRenameViewModel : ObservableObject
{
    private readonly RenameRules _rules = new();
    private readonly List<BatchRenameTarget> _targets = new();
    private FileOperationService? _fileOps;
    private OperationLogService? _opLog;
    private Action<string>? _done;
    private int _skipped;
    private bool _rebuilding;

    /// <summary>接上落盘通路（不接 = 只预览不落盘，按钮会提示不可用）。</summary>
    public void Attach(FileOperationService fileOps, OperationLogService opLog)
    {
        _fileOps = fileOps;
        _opLog = opLog;
    }

    // ==================== 弹层状态 ====================

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "批量重命名";

    /// <summary>「共 N 个文件（已跳过 M 个未入库项）」。</summary>
    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>没有红行且有可改的行 ⇒ 才能确认。</summary>
    [ObservableProperty]
    private bool _canConfirm;

    /// <summary>预览行（原名 / 新名 / 说明）。</summary>
    public ObservableCollection<BatchRenameRow> Rows { get; } = new();

    // ==================== 规则（绑定字段 → 每次变更重算预览） ====================

    [ObservableProperty]
    private string _findText = "";

    [ObservableProperty]
    private string _replaceText = "";

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _prefix = "";

    [ObservableProperty]
    private string _suffix = "";

    [ObservableProperty]
    private bool _numbering;

    [ObservableProperty]
    private int _numberStart = 1;

    [ObservableProperty]
    private int _numberStep = 1;

    [ObservableProperty]
    private int _numberDigits = 2;

    [ObservableProperty]
    private string _numberSeparator = "_";

    /// <summary>序号落点（下标 ↔ <see cref="RenameNumberPlacement"/>）。</summary>
    [ObservableProperty]
    private int _placementIndex = 1;

    /// <summary>大小写（下标 ↔ <see cref="RenameCaseMode"/>）。</summary>
    [ObservableProperty]
    private int _caseIndex;

    /// <summary>0 = 自动加序号，1 = 跳过冲突项。</summary>
    [ObservableProperty]
    private int _conflictIndex;

    /// <summary>保留原扩展名（默认开：规则只作用在主名上）。</summary>
    [ObservableProperty]
    private bool _keepExtension = true;

    public string[] CaseOptions { get; } = { "不变", "全小写", "全大写", "首字母大写" };

    public string[] PlacementOptions { get; } = { "替换原名", "放在最前", "放在最后" };

    public string[] ConflictOptions { get; } = { "自动加序号", "跳过冲突项" };

    // ==================== 打开 / 关闭 ====================

    /// <summary>
    /// 打开弹层。<paramref name="skippedNotInLibrary"/> = 选中项里没能参与的数量（未入库 / 无文件位置），只用于提示。
    /// </summary>
    public void Open(IReadOnlyList<BatchRenameTarget> targets, int skippedNotInLibrary, string title, Action<string>? done)
    {
        _targets.Clear();
        _targets.AddRange(targets);
        _skipped = skippedNotInLibrary;
        _done = done;
        Title = title;

        Rebuild();
        StatusText = CanConfirm
            ? "看右侧预览确认无误后点「应用」。"
            : "先让红行消失（改规则 / 换「自动加序号」）。";
        IsOpen = true;
    }

    /// <summary>关掉弹层（⛔ 不动磁盘）。</summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        _done = null;
    }

    // ==================== 预览 ====================

    partial void OnFindTextChanged(string value) => Rebuild();
    partial void OnReplaceTextChanged(string value) => Rebuild();
    partial void OnUseRegexChanged(bool value) => Rebuild();
    partial void OnPrefixChanged(string value) => Rebuild();
    partial void OnSuffixChanged(string value) => Rebuild();
    partial void OnNumberingChanged(bool value) => Rebuild();
    partial void OnNumberStartChanged(int value) => Rebuild();
    partial void OnNumberStepChanged(int value) => Rebuild();
    partial void OnNumberDigitsChanged(int value) => Rebuild();
    partial void OnNumberSeparatorChanged(string value) => Rebuild();
    partial void OnPlacementIndexChanged(int value) => Rebuild();
    partial void OnCaseIndexChanged(int value) => Rebuild();
    partial void OnConflictIndexChanged(int value) => Rebuild();
    partial void OnKeepExtensionChanged(bool value) => Rebuild();

    /// <summary>
    /// 把绑定字段灌进规则、逐个算新名、查重名，重填 <see cref="Rows"/>。
    /// 重名判定按**目录**：每个目录枚举一次（本次预览内缓存），已排给别人的新名也算占用 ⇒ 同一批不会互撞。
    /// </summary>
    private void Rebuild()
    {
        if (_rebuilding) return;
        _rebuilding = true;

        try
        {
            SyncRules();

            var rows = new List<BatchRenameRow>(_targets.Count);
            var taken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var problems = 0;
            var unchanged = 0;

            for (var i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                var (name, error) = _rules.Apply(target.OldName, i);
                var row = new BatchRenameRow(target) { NewName = name };

                if (error != null)
                {
                    row.IsProblem = true;
                    row.Note = error;
                    problems++;
                    rows.Add(row);
                    continue;
                }

                if (string.Equals(name, target.OldName, StringComparison.OrdinalIgnoreCase))
                {
                    row.Skip = true;
                    row.Note = "名字没变，跳过";
                    unchanged++;
                    rows.Add(row);
                    continue;
                }

                var dirNames = NamesOf(target.Dir, taken);
                if (dirNames.Contains(name))
                {
                    if (ConflictIndex == 0)
                    {
                        name = AvoidClash(dirNames, name);
                        row.NewName = name;
                        row.Note = "重名，已自动加序号";
                    }
                    else
                    {
                        row.IsProblem = true;
                        row.Note = "重名：换「自动加序号」或改规则";
                        problems++;
                    }
                }

                dirNames.Add(name);
                rows.Add(row);
            }

            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);

            var renameable = Rows.Count(r => !r.Skip);
            CanConfirm = problems == 0 && renameable > 0 && _fileOps != null;

            Summary = _targets.Count == 0
                ? "没有可改名的文件。"
                : $"共 {_targets.Count} 个文件，本次要改 {renameable} 个"
                  + (unchanged > 0 ? $"（{unchanged} 个名字没变）" : "")
                  + (_skipped > 0 ? $"；已跳过 {_skipped} 个未入库 / 无文件位置的项" : "");

            if (problems > 0) StatusText = $"有 {problems} 行红字不能应用，先把它们消掉。";
            else if (_fileOps == null) StatusText = "批量重命名当前不可用。";
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>绑定字段 → 规则对象（⚠️ 下标越界按 0 兜底，⛔ 不让 UI 脏值把预览搞崩）。</summary>
    private void SyncRules()
    {
        _rules.FindText = FindText ?? "";
        _rules.ReplaceText = ReplaceText ?? "";
        _rules.UseRegex = UseRegex;
        _rules.Prefix = Prefix ?? "";
        _rules.Suffix = Suffix ?? "";
        _rules.Numbering = Numbering;
        _rules.NumberStart = NumberStart;
        _rules.NumberStep = NumberStep;
        _rules.NumberDigits = NumberDigits;
        _rules.NumberSeparator = NumberSeparator ?? "";
        _rules.NumberPlacement = (RenameNumberPlacement)Math.Clamp(
            PlacementIndex, 0, (int)RenameNumberPlacement.After);
        _rules.CaseMode = (RenameCaseMode)Math.Clamp(CaseIndex, 0, (int)RenameCaseMode.Title);
        _rules.KeepExtension = KeepExtension;
    }

    /// <summary>某目录里已被占用的名字（首次访问枚举磁盘，之后复用本次预览的缓存）。</summary>
    private static HashSet<string> NamesOf(string dir, Dictionary<string, HashSet<string>> cache)
    {
        if (cache.TryGetValue(dir, out var set)) return set;

        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
                set.Add(Path.GetFileName(path));
        }
        catch
        {
            // 目录读不到（离线 / 权限）⇒ 当作空目录：真改的时候 FileOperationService 还会再判一次
        }

        cache[dir] = set;
        return set;
    }

    /// <summary>「名字 (1).ext」逐个试，直到不撞。</summary>
    private static string AvoidClash(HashSet<string> taken, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var n = 1; n < 10000; n++)
        {
            var candidate = baseName + " (" + n + ")" + ext;
            if (!taken.Contains(candidate)) return candidate;
        }

        return name + " (" + Guid.NewGuid().ToString("N")[..4] + ")";
    }

    // ==================== 落盘 ====================

    /// <summary>
    /// 「应用」：逐个走 <see cref="FileOperationService.Rename"/>（日志先行 + 库里路径同步）。
    /// 一条失败不影响其余；末尾写一条批量摘要日志并汇总「成功 / 失败」。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!CanConfirm || _fileOps == null) return;

        var plan = Rows.Where(r => !r.Skip).Select(r => (Row: r, NewName: r.NewName)).ToList();
        var done = _done;
        var fileOps = _fileOps;
        var opLog = _opLog;
        var skipped = Rows.Count(r => r.Skip);

        var confirmed = await DialogHooks.ConfirmAsync(
            $"把 {plan.Count} 个文件按预览改名？\n" +
            $"· 原名 → 新名见刚才的预览表；\n" +
            $"· 每条都走「日志先行」，库里的文件路径同步更新；\n" +
            $"· 一条失败不影响其余，结果看「任务中心」。" +
            (skipped > 0 ? $"\n· 另有 {skipped} 个不动（名字没变）。" : "")).ConfigureAwait(true);
        if (!confirmed) { StatusText = "已取消（没改任何文件）。"; return; }

        IsOpen = false;

        fileOps.Queue.Enqueue($"批量重命名 {plan.Count} 个文件", async (ct, p) =>
        {
            var ok = 0;
            var failed = 0;

            await Task.Run(() =>
            {
                for (var i = 0; i < plan.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var (row, newName) = plan[i];
                    p.Report($"正在改名（{i + 1}/{plan.Count}）「{row.OldName}」→「{newName}」…");

                    var result = fileOps.Rename(row.Target.Loc, newName, ConflictPolicy.AppendNumber);
                    if (result.Ok) { ok++; continue; }

                    failed++;
                    p.Report($"「{row.OldName}」失败：{result.Message}");
                }
            }, ct).ConfigureAwait(false);

            opLog?.Log("rename_batch", "asset", null,
                afterJson: $"{{\"renamed\":{ok},\"failed\":{failed},\"unchanged\":{skipped}}}");

            p.Report(failed == 0
                ? $"批量重命名完成：改了 {ok} 个文件。"
                : $"批量重命名完成：改了 {ok} 个，失败 {failed} 个（看任务中心明细）。");

            var message = failed == 0
                ? $"批量重命名完成：改了 {ok} 个文件（失败 {failed} 个）。"
                : $"批量重命名完成：改了 {ok} 个，失败 {failed} 个（失败的保持原名，看任务中心）。";
            try { Dispatcher.UIThread.Post(() => done?.Invoke(message)); } catch { /* 页面已关闭 */ }
        });

        StatusText = $"已把 {plan.Count} 个文件的改名排进任务队列（进度看「任务中心」）。";
        _done = null;
    }
}
