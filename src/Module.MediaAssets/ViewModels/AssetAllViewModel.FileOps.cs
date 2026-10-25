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

    /// <summary>
    /// 用**系统默认程序**打开选中的文件（视频 = 播放、书 = 阅读、图 = 看图）。
    /// 🔴 这类"针对某一条"的动作为什么不在工具条：工具条只放**页面级**功能（扫描 / 筛选 / 入库…），
    /// 单条操作归右键菜单（用户 v3.29 定调，需求 §4.8 硬规则 9）。
    /// </summary>
    [RelayCommand]
    private void OpenFile()
    {
        if (Selected == null) return;
        Launch(Selected.FilePath, false, "文件");
    }

    /// <summary>在资源管理器里**选中**该文件（右键「📁 打开所在文件夹」）。</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = $"/select,\"{Selected.FilePath}\""
            });
            StatusText = "已在资源管理器中定位该文件。";
        }
        catch (Exception ex) { StatusText = "打开文件夹失败：" + ex.Message; }
    }

    /// <summary>用系统默认程序打开一个路径（<paramref name="isDb"/> 只影响提示文案）。</summary>
    private void Launch(string path, bool isDb, string what)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { StatusText = $"{what}不存在：{path}"; return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = isDb
                ? "已用系统默认程序打开库文件（没反应多半是没装能开 .db 的工具）。"
                : $"已用系统默认程序打开{what}。";
        }
        catch (Exception ex)
        {
            StatusText = $"打开{what}失败：" + ex.Message + (isDb ? " —— 需要先装一个能打开 .db 的工具（如 DB Browser for SQLite）。" : "");
        }
    }

    /// <summary>
    /// 「📂 打开库所在文件夹」：在资源管理器打开 db 所在目录（**路径形式**，⛔ 不指向具体文件）。
    /// 🔴 2026-10-16 用户定稿：原「🔎 打开当前库文件」用系统默认程序直接开 .db ——
    /// 没装 DB Browser 之类的工具必然报「打不开」；改成开所在目录，用户在里面手动选。
    /// </summary>
    [RelayCommand]
    private void OpenDbFile()
    {
        var db = _assets.DatabasePath;
        var dir = string.IsNullOrEmpty(db) ? "" : Path.GetDirectoryName(Path.GetFullPath(db)) ?? "";
        if (dir.Length == 0 || !Directory.Exists(dir))
        {
            StatusText = $"库所在目录不存在：{dir}";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = $"\"{dir}\""
            });
            StatusText = $"已在资源管理器打开库所在目录：{dir}";
        }
        catch (Exception ex) { StatusText = "打开库所在目录失败：" + ex.Message; }
    }

    // ==================== ③ 登记文件夹 + 扫描全部（直接入库） ====================

    /// <summary>登记一个文件夹到「待扫列表」（⛔ 不扫描、⛔ 不入库）。</summary>
    public void RegisterFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        if (!Directory.Exists(dir)) { StatusText = $"目录不存在：{dir}"; return; }

        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = dir;

        var storage = new StorageSource { Name = name, RootPath = dir, Type = StorageTypes.Local };
        _assets.UpsertStorage(storage);
        if (!PendingFolders.Contains(dir)) PendingFolders.Add(dir);

        OnPropertyChanged(nameof(HasPendingFolders));
        StatusText = $"已登记「{name}」（共 {PendingFolders.Count} 个待扫）——继续添加，然后点「🔄 扫描全部」。";
    }

}
