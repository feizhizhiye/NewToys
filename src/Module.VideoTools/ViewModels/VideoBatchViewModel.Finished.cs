using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.VideoTools.Models;

namespace NewToys.VideoTools.ViewModels;

/// <summary>批量处理页的「已完成」分区：跑成功的任务沉到列表下方独立一块， 上面那块只剩待处理 / 运行中 / 失败 —— 一眼能看出「还有多少没跑完」。（WHY: VT-030）</summary>
public partial class VideoBatchViewModel
{
    /// <summary>已完成（<see cref="VideoTaskStatus.Done"/>）的任务，按当前排序排在下面那块。</summary>
    public ObservableCollection<VideoTaskBase> FinishedItems { get; } = new();

    /// <summary>下区的显隐（没有已完成项时整块收起）。</summary>
    public bool HasFinished => FinishedItems.Count > 0;

    /// <summary>分区标题：数量 + 能干什么。</summary>
    public string FinishedHeaderText => $"✔ 已完成（{FinishedItems.Count}）· 右键可播放输出 / 定位文件";

    /// <summary>「已完成」区里当前选中的一条（下区右键菜单的对象）。</summary>
    [ObservableProperty] private VideoTaskBase? _selectedFinished;

    /// <summary>▶ 播放输出文件：跑完后直接看结果（没输出就退回源文件）。</summary>
    [RelayCommand]
    private void PlayOutput()
    {
        var path = OutputPathOf(SelectedFinished);
        if (path.Length == 0) { StatusText = "这条任务还没有输出文件。"; return; }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = "正在打开：" + Path.GetFileName(path);
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>📂 在资源管理器里定位输出文件。</summary>
    [RelayCommand]
    private void RevealOutput()
    {
        var path = OutputPathOf(SelectedFinished);
        if (path.Length == 0) { StatusText = "这条任务还没有输出文件。"; return; }

        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                if (dir.Length == 0) return;
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { StatusText = "定位失败：" + ex.Message; }
    }

    /// <summary>🗑 从队列里删掉这条已完成任务（⛔ 不动磁盘上的输出文件）。</summary>
    [RelayCommand]
    private void DeleteFinished()
    {
        var t = SelectedFinished;
        if (t == null || _store == null) return;

        if (t is CutTask c) _store.RemoveCut(c);
        else if (t is TranscodeTask x) _store.RemoveTranscode(x);

        SelectedFinished = null;
        RefreshView();
        StatusText = "已从队列删除：" + t.Name + "（输出文件还在磁盘上）";
    }

    /// <summary>取任务的实际输出路径（没设过输出名就按类型自动命名算一遍）。</summary>
    private static string OutputPathOf(VideoTaskBase? t)
        => t == null ? "" : ResolveOutputOf(t);
}
