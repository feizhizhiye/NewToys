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

// Core 里也有一个 FileOperationService（纯磁盘动作，跨模块共用）。
// 本页用的是**带库表日志**的模块版，显式取别名，免得两个 using 撞名（CS0104）。
using FileOperationService = NewToys.MediaAssets.Services.FileOperationService;

namespace NewToys.MediaAssets.ViewModels;

public partial class AssetAllViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>按工具条输入框里的路径预览扫描（手输入口；回车 / 点「⤓」都走这里）。</summary>
    [RelayCommand]
    private async Task PreviewPathAsync()
    {
        var dir = (ImportPath ?? "").Trim().Trim('"');
        if (dir.Length == 0) { StatusText = "先填一个文件夹路径。"; return; }
        if (!Directory.Exists(dir)) { StatusText = $"目录不存在：{dir}"; return; }
        await PreviewFolderAsync(dir).ConfigureAwait(false);
    }

    /// <summary>预览扫一个文件夹 → 进草稿态。</summary>
    public async Task PreviewFolderAsync(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || IsBusy) return;
        ImportPath = dir;
        try
        {
            IsBusy = true;
            StatusText = $"正在预览扫描「{dir}」（只读文件信息，不写库）…";
            var progress = PostProgress();
            // ⛔ 不带 ConfigureAwait(false)：LoadDraft 要改列表集合，必须在 UI 线程上做
            var rows = await _scan.PreviewAsync(dir, Recursive, progress, CancellationToken.None);
            LoadDraft(rows);
            StatusText = $"预览到 {TotalCount} 个：点「加入库」保存，或「清除列表」换文件夹重扫。";
        }
        catch (Exception ex) { StatusText = "预览失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>预览选中的若干文件（不遍历目录）→ 进草稿态。</summary>
    public Task PreviewFilesAsync(IReadOnlyList<string> files)
    {
        if (files == null || files.Count == 0) return Task.CompletedTask;

        var rows = new List<AssetRow>();
        foreach (var f in files)
        {
            try
            {
                if (!File.Exists(f)) continue;
                var info = new FileInfo(f);
                var name = Path.GetFileNameWithoutExtension(info.Name);
                var dir = Path.GetDirectoryName(f) ?? "";
                rows.Add(new AssetRow
                {
                    Name = name,
                    Title = name,
                    FileName = info.Name,
                    FilePath = f,
                    Type = ScanService.GuessAssetType(info.Extension),
                    Size = info.Length,
                    Mtime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    StorageName = Path.GetFileName(dir.TrimEnd('\\', '/'))
                });
            }
            catch { /* 单个文件读不了 → 跳过 */ }
        }
        LoadDraft(rows);
        StatusText = $"预览到 {TotalCount} 个文件：点「加入库」保存，或「清除列表」放弃。";
        return Task.CompletedTask;
    }

    /// <summary>把一批预览行装进列表并进入草稿态。</summary>
    private void LoadDraft(List<AssetRow> rows)
    {
        _all.Clear();
        AddRows(rows);
        Selected = null;
        IsDraft = true;
        TotalCount = rows.Count;   // 草稿态总数 = 预览行数（分页挂起，⛔ 不走 SQL）
        ApplyView();
    }

    // ==================== ② 加入库 / 清除列表（草稿态专用） ====================

    [RelayCommand]
    private void CommitDraft()
    {
        if (!IsDraft || _all.Count == 0) { StatusText = "没有待入库的预览结果。"; return; }

        var total = _all.Count;
        var n = _assets.CommitRows(_all);
        IsDraft = false;
        Reload();
        StatusText = n > 0
            ? $"已把 {n} 个加入资产库" + (total > n ? $"（{total - n} 个已在库里，按路径跳过）。" : "。")
            : IsDbAvailable
                ? $"这 {total} 条都已在资产库里（按路径去重），没有新增。"
                : "入库失败（数据库不可用？）。";
    }

    [RelayCommand]
    private void ClearList()
    {
        // 🔴 任何状态都可用（v3.29 用户反馈：库视图下置灰看着像坏了）。两种语义：
        if (IsDraft)
        {
            // 草稿态 = 丢弃预览结果，回库视图
            _all.Clear();
            Selected = null;
            IsDraft = false;
            Reload();
            StatusText = "已清除预览结果，可以换个文件夹重新扫描。";
            return;
        }

        // 库视图 = 只清**当前显示**（数据仍在库里，「重读资产库」即找回）—— 对齐视频列表的同名按钮（§6.4.11）
        Items.Clear();
        Selected = null;
        OnPropertyChanged(nameof(HasItems));
        StatusText = "已清空列表显示（数据仍在库里，点「重读」找回）。";
    }

    /// <summary>库文件全路径（右键「🗄 打开库文件」的 ToolTip 用）。</summary>
    public string DatabasePath => _assets.DatabasePath;

}
