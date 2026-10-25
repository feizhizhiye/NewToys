using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

// v3.66：Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>「📁 扫描」主按钮 / 「📁 文件夹…」：选目录 → **预览**（⛔ 只进内存、不入库）。</summary>
    /// <para>🔴 主按钮（SplitButton 主体）：输入框里已有**有效目录** ⇒ 直接扫它（⛔ 不弹选择框）——
    /// 2026-10-16 用户反馈「输入框有路径，点扫描不读取」；空 / 不存在时才弹选择框。</para>
    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        var typed = (ImportPath ?? "").Trim().Trim('"');
        if (typed.Length > 0)
        {
            if (!Directory.Exists(typed)) { StatusText = $"目录不存在：{typed}"; return; }
            await PreviewFolderAsync(typed).ConfigureAwait(false);
            return;
        }
        var dir = await DialogHooks.PickFolderAsync("选择要扫描的目录", ImportExportDefaultDir);
        if (string.IsNullOrWhiteSpace(dir)) return;
        await PreviewFolderAsync(dir).ConfigureAwait(false);
    }

    /// <summary>「📄 文件…」：只预览选中的几个文件（⛔ 同样不入库）。</summary>
    [RelayCommand]
    private async Task ScanFilesAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要扫描的文件", ImportExportDefaultDir, true, "*");
        if (files.Count == 0) return;
        await PreviewFilesAsync(files).ConfigureAwait(false);
    }

    /// <summary>「➕ 登记」：选一个目录加进待扫列表（⛔ 不扫描，等「🔄 扫描全部」）。</summary>
    [RelayCommand]
    private async Task RegisterFolderAsync()
    {
        var dir = await DialogHooks.PickFolderAsync("选择要登记的目录", ImportExportDefaultDir);
        if (string.IsNullOrWhiteSpace(dir)) return;
        RegisterFolder(dir);
    }

    /// <summary>「🗄 并入…」主按钮：选一个外部 .db → VM 预览 + 确认 + 入队合并。</summary>
    [RelayCommand]
    private async Task MergeDbAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要并入当前库的 newtoys.db", ImportExportDefaultDir, false, "*.db;*.db3;*.sqlite");
        var path = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return;
        MergeLibraryCommand.Execute(path);
    }

    /// <summary>
    /// 「💿 另存为…」：选一个目标 .db → 调既有 <see cref="SaveLibraryAsCommand"/> 另存（已存在会问覆盖）。
    /// <para>⚠️ 方法名带 <c>Pick</c> 前缀是**必须的**：既有的 <c>SaveLibraryAsAsync(string?)</c> 已占用
    /// <c>SaveLibraryAsCommand</c> 这个名字，同名再加一个 [RelayCommand] 方法会报 **MVVMTK0023**（同类型内命令名必须唯一）。</para>
    /// </summary>
    [RelayCommand]
    private async Task PickSaveLibraryAsAsync()
    {
        var path = await DialogHooks.SaveFileAsync("保存资产库为", ImportExportDefaultDir, "newtoys.db", "*.db");
        if (string.IsNullOrWhiteSpace(path)) return;
        SaveLibraryAsCommand.Execute(path);
    }

    /// <summary>
    /// 「📤 导出」：按所选格式导出 —— .json = 当前列表（含标签 / 分类 / 集合的名字）；
    /// .db = **整个资产库**另存（备份 / 交付用，复用「💿 另存为…」同一命令）。
    /// </summary>
    [RelayCommand]
    private async Task ExportAssetsAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出资产列表", ImportExportDefaultDir, "media-assets.json",
            "JSON 列表（*.json）|*.json|SQLite 库（*.db）|*.db");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                SaveLibraryAsCommand.Execute(path);      // 整库另存（已有命令：已存在会问覆盖）
                return;                                  // 结果文案由 SaveLibraryAsAsync 自己发
            }
            File.WriteAllText(path, ExportAssetsJson());
            StatusText = $"已导出当前列表到：{path}";
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>
    /// 「📥 导入」：把 db / json 的内容**加载到列表显示（草稿态预览）**——⛔ 不写库、⛔ 不弹确认、⛔ 不入队。
    /// 2026-10-16 用户定稿：导入只作为草稿上列表，要不要进库由「💾 加入库」决定（与扫描预览同语义）。
    /// </summary>
    [RelayCommand]
    private async Task ImportAssetsAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要导入预览的资产（json 列表 / newtoys.db）",
            ImportExportDefaultDir, false, "*.json;*.db");
        var src = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(src)) return;
        try
        {
            List<AssetRow> rows;
            string what;
            if (string.Equals(Path.GetExtension(src), ".db", StringComparison.OrdinalIgnoreCase))
            {
                var (ok, msg, dbRows) = _merge.ReadAssets(src);
                if (!ok) { StatusText = msg; return; }
                rows = dbRows;
                what = $"从「{Path.GetFileName(src)}」";
            }
            else
            {
                var (ok, msg, jsonRows) = _impExp.Parse(File.ReadAllText(src));
                if (!ok) { StatusText = msg; return; }
                rows = jsonRows;
                what = $"从「{Path.GetFileName(src)}」";
            }
            if (rows.Count == 0) { StatusText = "没有可显示的条目。"; return; }

            LoadDraft(rows);
            StatusText = $"已导入预览 {rows.Count} 条{what}（未入库存）：点「💾 加入库」写入资产库，或「🧹 清除列表」放弃。";
        }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

}
