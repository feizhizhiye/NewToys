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

    /// <summary>把已登记的存储源**全部**扫一遍并直接入库（`ScanAsync(null)` 扫所有在线存储源）。</summary>
    [RelayCommand]
    private async Task ScanAllAsync()
    {
        PendingFolders.Clear();
        OnPropertyChanged(nameof(HasPendingFolders));
        await RunJobAsync("正在扫描全部存储源……", (ct, progress) => _scan.ScanAsync(null, progress, ct)).ConfigureAwait(false);
    }

    // ==================== ④ 通用：进度上报 / 任务外壳 ====================

    /// <summary>
    /// 造一个进度报告器：把后台线程的进度文本 **Post 回 UI 线程**再写 <c>StatusText</c>
    /// （⛔ 直接在后台线程写界面状态会出线程问题；本项目之前没有从后台推进度的先例）。
    /// </summary>
    private IProgress<string> PostProgress()
        => new Progress<string>(text =>
        {
            try { Dispatcher.UIThread.Post(() => StatusText = text); }
            catch { /* 还没有 UI 线程（设计期 / 已关闭）→ 忽略 */ }
        });

    private async Task RunJobAsync(string startText,
        Func<CancellationToken, IProgress<string>, Task<ScanJob>> work)
    {
        if (IsBusy) { StatusText = "还有任务在跑，等它结束再试。"; return; }

        try
        {
            IsBusy = true;
            StatusText = startText;

            // ⚠️ 进度回调来自后台线程（ScanService 里的 Task.Run）→ 必须 Post 回 UI 线程再碰界面状态
            var progress = new Progress<string>(text =>
            {
                try { Dispatcher.UIThread.Post(() => StatusText = text); }
                catch { /* 设计期 / 还没有 UI 线程：忽略，不影响任务本身 */ }
            });

            var job = await work(CancellationToken.None, progress).ConfigureAwait(false);

            Reload();
            StatusText = job.Status == ScanStatuses.Cancelled
                ? "已取消。"
                : $"完成：新增 {job.AddedFiles} / 更新 {job.UpdatedFiles} / 丢失 {job.MissingFiles} / 失败 {job.ErrorCount}";
        }
        catch (Exception ex)
        {
            StatusText = "失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    // ==================== v3.34：勾选列 + 批量逻辑操作 ====================

    /// <summary>
    /// 行被勾了几条（工具条「🏷 加标签」「📚 加集合」按它操作）。
    /// <para>口径 = <c>_all</c> 里被勾的行数，而 <c>_all</c> 的语义分两态：
    /// <b>库态</b> = 当前页（翻页会重建 ⇒ 勾选**不跨页保留**）；
    /// <b>草稿态</b> = 全部预览行（对象一直在内存 ⇒ 勾选**天然跨页保留**）。
    /// 这是分页列表的常规行为，⛔ 别理解成 bug —— 想一次勾全部就把底栏「每页」切到「全部」。</para>
    /// </summary>
    public int CheckedCount => _all.Count(r => r.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    // v3.41（用户定调）：⛔ 工具条不再放「未勾选 / 已勾选 N 个」指示文字 ——
    // 「需先勾选」这件事改用按钮自己的 ToolTip（+ ToolTip.ShowOnDisabled）表达，勾选数由列表左列勾选框直接可见。

    /// <summary>
    /// 表头 ☑ 的三态（🔴 用**原生三态 CheckBox**，⛔ 别用字符"☑"画 —— 字体会渲染成深色块，v3.16 连踩两轮）：
    /// <c>true</c> = 全选、<c>false</c> = 全不选、<c>null</c> = 部分选中。
    /// </summary>
    [ObservableProperty] private bool? _allCheckedState = false;

}
