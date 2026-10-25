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

    /// <summary>➕ 加入库：把勾选的新文件建成条目（复用 <see cref="ImportScriptFiles"/> 的路径归一与去重）。</summary>
    [RelayCommand]
    private void AddScanCandidates()
    {
        var files = ScanCandidates.Where(c => c.Selectable && c.IsSelected).Select(c => c.FullPath).ToList();
        if (files.Count == 0) { StatusText = "没有勾选任何新文件。"; return; }

        CloseScan();
        ImportScriptFiles(files);
    }

    /// <summary>✕ 收起候选面板。</summary>
    [RelayCommand]
    private void CloseScan()
    {
        ScanCandidates.Clear();
        HasScanResult = false;
        OnPropertyChanged(nameof(CanAddScanCandidates));
        OnPropertyChanged(nameof(Toolbar));
    }

    protected virtual IEnumerable<ToolbarItemDescriptor> ToolbarTailExtras => Array.Empty<ToolbarItemDescriptor>();

    /// <summary>
    /// v3.63：共用的「📥 导入」（json 清单）放哪儿 —— **true = 排最前**（「导入 → 输入框 → 搜索」三件套，
    /// 「命令库」页走这条）；**false = 排在「📤 导出」前面**（「脚本库」页：最前已经有一个「📂 导入」脚本文件了，
    /// 两个导入挨在一起会分不清）。
    /// </summary>
    protected virtual bool JsonImportFirst => true;

    /// <summary>共用的「📥 导入」按钮（从 json 文件导入本页清单：合并 / 整页替换两路菜单）。</summary>
    private static ToolbarItemDescriptor JsonImportItem => new()
    {
        // v3.63：与「📤 导出」同一口径（emoji + 一个词），⛔ 不再写「导入清单」这类后缀
        Text = "📥 导入",
        Classes = "ghost",
        ToolTip = "从 json 文件导入本页清单（合并 / 整页替换）",
        Items = new ToolbarMenuItemDescriptor[]
        {
            new() { Text = "🔀 合并导入（按 Id 去重，同 Id 覆盖）", CommandPath = nameof(ImportPageMergeCommand) },
            new() { Text = "♻ 整页替换（先清空本页再导入）", CommandPath = nameof(ImportPageReplaceCommand) },
        }
    };

    /// <summary>
    /// 工具条（/ 需求 §4.8.5）：页面⛔ 不再自写 <c>Border.toolbar</c>，改由 Shell 渲染本描述符。
    /// <para>
    /// 顺序按「全部资产」同一口径：**入口动作（<see cref="ToolbarExtras"/>）→ 输入框 → 搜索框**，
    /// 页专属项在前，共用搜索框紧随其后，末尾是共用的「导入 / 导出 + 清空本页」。
    /// </para>
    /// </summary>
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar
    {
        get
        {
            var items = new List<ToolbarItemDescriptor>();

            // 库页最前 = 库选择 + 库级导入导出。
            if (LibraryKind.Length > 0)
            {
                items.Add(new()
                {
                    Kind = ToolbarItemKind.Combo, Width = 160,
                    ItemsPath = nameof(LibraryOptions),
                    DisplayPath = nameof(CollectionFilterOption.Label),
                    SelectedItemPath = nameof(SelectedLibraryOption),
                    ToolTip = "这个页面显示哪个库（「全部库」= 所有库的合集）",
                });
                items.Add(new()
                {
                    Text = "📥 导入库", Classes = "ghost",
                    CommandPath = nameof(ImportLibraryCommand),
                    ToolTip = "导入库包：json 索引包（只导索引）/ zip 全量包（连脚本文件）"
                });
                items.Add(new()
                {
                    Text = "📤 导出库", Classes = "ghost",
                    CommandPath = nameof(ExportLibraryCommand),
                    ToolTip = "导出当前库为索引包（json）"
                });
                items.Add(new()
                {
                    Text = "📦 全量导出", Classes = "ghost",
                    CommandPath = nameof(ExportLibraryBundleCommand),
                    ToolTip = "导出当前库为全量包（zip，含脚本文件）"
                });
            }

            if (JsonImportFirst) items.Add(JsonImportItem);

            // 页专属项（入口动作 / 输入框）在前，共用的搜索框紧随其后（与「全部资产」同一口径）。
            items.AddRange(ToolbarExtras);

            items.Add(new()
            {
                Kind = ToolbarItemKind.TextField,
                Width = 88,
                Placeholder = "搜索名称 / 分类 / 简介…",
                ValuePath = nameof(SearchText),
                ToolTip = SearchToolTip
            });

            // 搜索框之后的页专属项（⛔ 别塞进 ToolbarExtras —— 那会把搜索框从「入口动作 / 输入框」后面挤开）。
            items.AddRange(ToolbarTailExtras);

            // 扫描候选面板的两个动作（只在有候选时出现，⛔ 不占常态工具条）。
            if (HasScanResult)
            {
                items.Add(new()
                {
                    Text = "➕ 加入库",
                    Classes = "ghost",
                    CommandPath = nameof(AddScanCandidatesCommand),
                    ToolTip = "把勾选的新文件建成条目（已入库 / 不可采集的不会重复添加）"
                });
                items.Add(new()
                {
                    Text = "✕ 收起",
                    Classes = "ghost",
                    CommandPath = nameof(CloseScanCommand),
                    ToolTip = "收起扫描结果面板"
                });
            }

            // v3.63：不排在最前的页 ⇒ 「📥 导入」插到「📤 导出」前面
            if (!JsonImportFirst) items.Add(JsonImportItem);

            items.AddRange(new ToolbarItemDescriptor[]
            {
                new()
                {
                    Text = "📤 导出",
                    Classes = "ghost",
                    CommandPath = nameof(ExportPageCommand),
                    ToolTip = "把本页全条目导出为 json 文件"
                },
                new()
                {
                    Text = "🧹 清空本页",
                    Classes = "danger-btn",
                    CommandPath = nameof(ClearPageCommand),
                    IsEnabledPath = "!" + nameof(IsBusy),
                    ToolTip = "删掉本页全部条目并写盘（真删除，二次确认；建议先导出备份）"
                },
            });

            return items;
        }
    }

    /// <summary>📤 导出：把本页全条目存成 json 文件（原 code-behind 的 Click 搬进 VM，选择器走 DialogHooks）。</summary>
    [RelayCommand]
    private async Task ExportPageAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出清单", ImportExportDefaultDir, ExportSuggestedName, "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, ExportPageJson());
            StatusText = $"已导出本页全条目到：{path}";
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>📥 合并导入（按 Id 去重，同 Id 覆盖）。</summary>
    [RelayCommand]
    private Task ImportPageMergeAsync() => ImportPageFromFileAsync(replace: false);

    /// <summary>📥 整页替换导入（先清空本页再导入）。</summary>
    [RelayCommand]
    private Task ImportPageReplaceAsync() => ImportPageFromFileAsync(replace: true);

    /// <summary>📥 导入：选 json 文件 → 合并 / 整页替换导入本页（原 code-behind 的 Click 搬进 VM）。</summary>
    private async Task ImportPageFromFileAsync(bool replace)
    {
        var files = await DialogHooks.PickFilesAsync(
            replace ? "选择要整页替换导入的清单" : "选择要合并导入的清单",
            ImportExportDefaultDir, false, "*.json");
        var path = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;

        try { StatusText = ImportPageJson(File.ReadAllText(path), replace); }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

    // ==================== 整页导入 / 导出（v3.19；收尾后控件样式由 Shell 的 ToolbarHost 统一） ====================

    /// <summary>导出本页**全条目** json 文本（供 View「📤 导出」存盘）。</summary>
    public string ExportPageJson() => _svc?.ExportPage(_pageKind) ?? "[]";

    /// <summary>导入本页（View「📥 导入」读文件后调用）；<paramref name="replace"/>=true 整页替换、false 合并。</summary>
    public string ImportPageJson(string json, bool replace)
    {
        if (_svc == null) return "服务不可用。";
        var (added, updated, skipped) = _svc.ImportPage(_pageKind, json, replace);
        if (skipped < 0) return "⚠ 导入失败：选中的文件不是有效的清单 json。";

        RebuildCategories();
        ResetFilters();
        ApplyView();

        var parts = new List<string>();
        if (added > 0) parts.Add($"新增 {added} 条");
        if (updated > 0) parts.Add($"更新 {updated} 条");
        if (skipped > 0) parts.Add($"跳过 {skipped} 条无效");
        var summary = parts.Count > 0 ? string.Join("，", parts) : "没有可导入的条目";
        return (replace ? "已整页替换：" : "已合并导入：") + summary + $"（写入 {FilePath}）";
    }

    /// <summary>🧹 清空本页（v3.19，方案③：**真删除并落盘**）：删掉本页全部条目，二次确认。</summary>
    [RelayCommand]
    private async Task ClearPageAsync()
    {
        if (_svc == null) return;

        var count = _svc.Items.Count(BelongsToThisPage);
        if (count == 0) { StatusText = $"「{PageDisplayName}」已经是空的。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            $"确定清空「{PageDisplayName}」全部 {count} 条吗？\n会写入 {FilePath}（真删除，不可撤销；建议先「📤 导出」备份）。").ConfigureAwait(true);
        if (!ok) { StatusText = "已取消清空。"; return; }

        IsBusy = true;
        try
        {
            _svc.ClearPage(_pageKind);
            Selected = null;
            RebuildCategories();
            ApplyView();
            StatusText = $"已清空「{PageDisplayName}」{count} 条（写入 {FilePath}）。";
        }
        finally { IsBusy = false; }
    }

}
