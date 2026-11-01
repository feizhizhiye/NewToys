using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;   // 🔴 SetTextAsync 是 IClipboard 的扩展方法，漏了这行会报 CS1061
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Controls;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class CrawlPanelViewModel : ObservableObject, IStatusProvider, IToolbarSource
{

    /// <summary>
    /// 下载勾选的行；一行都不勾 = 链式模式（起始网址起，content + next 边跑边累积）。
    /// 产物落 <c>downloads\&lt;脚本&gt;\&lt;条目&gt;\</c>（⛔ 不进备份）。
    /// </summary>
    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (IsBusy) return;

        var script = SelectedContentScript?.Value ?? "";
        if (script.Length == 0)
        {
            StatusText = "先在「内容脚本 ▾」里选 content 段脚本。";
            return;
        }

        var req = new DownloadRequest { Script = script };
        var checkedRows = _links.Entries.Where(e => e.IsSelected && e.Url.Length > 0).ToList();
        if (checkedRows.Count > 0)
        {
            foreach (var e in checkedRows) req.Entries.Add((e.Url, e.Title.Length > 0 ? e.Title : e.Url));
        }
        else
        {
            req.StartUrl = (StartUrl ?? "").Trim();
            if (req.StartUrl.Length == 0)
            {
                StatusText = "先点几行（行底色变深 = 已勾上）再下载；或一行不勾 + 填起始网址 = 链式模式。";
                return;
            }
        }

        _dlCts = new CancellationTokenSource();
        IsBusy = true;
        _progress.IsBusy = true;
        _progress.Status = CrawlSection.Ok;
        DownloadRows.Clear();
        DlTotal = req.IsChain ? -1 : req.Entries.Count;
        DlDone = 0;
        DlCurrent = "";
        var sw = Stopwatch.StartNew();
        try
        {
            StatusText = req.IsChain
                ? "链式下载：从起始网址 content + next 逐页跟进……"
                : "开始下载 " + req.Entries.Count + " 条（并发 " + Math.Clamp(_files.Scan().FirstOrDefault(m => m.Name == script)?.Concurrency ?? 1, 1, 8) + "）……";

            var outcome = await _dl.DownloadAsync(req, new Progress<DownloadEvent>(OnDlEvent), _dlCts.Token)
                .ConfigureAwait(true);
            sw.Stop();

            _progress.IsBusy = false;
            _progress.Status = outcome.Failed > 0 ? CrawlSection.Warn : CrawlSection.Ok;
            _progress.CountText = $"✓ {outcome.Ok} · ↷ {outcome.Skipped} · ✗ {outcome.Failed}";
            FooterSummary = $"下载 {outcome.Ok + outcome.Skipped + outcome.Failed} 条"
                          + $"（✓{outcome.Ok} ↷{outcome.Skipped} ✗{outcome.Failed}）· 耗时 {sw.Elapsed.TotalSeconds:F1}s"
                          + (outcome.StopReason.Length > 0 ? " · " + outcome.StopReason : "");
            StatusText = outcome.Cancelled
                ? "已取消下载（已下完的文件保留）。"
                : outcome.StopReason.Length > 0
                    ? "⚠ " + outcome.StopReason
                    : "下载完成 → " + _dl.DownloadsDir;
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 下载失败：" + FirstLine(ex.Message);
            _progress.Status = CrawlSection.Error;
            _progress.IsBusy = false;
        }
        finally
        {
            IsBusy = false;
            _dlCts?.Dispose();
            _dlCts = null;
        }
    }

    /// <summary>⏹ 取消下载。</summary>
    [RelayCommand]
    private void CancelDownload()
    {
        if (_dlCts is null || IsBusy == false) return;
        _dlCts.Cancel();
        StatusText = "正在取消……（已下完的文件保留）";
    }

    /// <summary>下载事件 → ③ 节逐行 + 进度；链式模式顺带把 ① 节边跑边累积。</summary>
    private void OnDlEvent(DownloadEvent ev)
    {
        if (ev.Phase == "start")
        {
            DlTotal = ev.Total;
            return;
        }

        var row = DownloadRows.FirstOrDefault(r => string.Equals(r.Name, ev.Name, StringComparison.Ordinal));
        if (row is null)
        {
            row = new DownloadRow(ev.Name);
            DownloadRows.Add(row);
        }
        row.Status = ev.Status;
        row.Detail = ev.Detail;
        DlDone = ev.Done;
        DlCurrent = ev.Name + " · " + ev.Detail;
        _progress.CountText = ev.Total > 0 ? ev.Done + "/" + ev.Total : ev.Done + " 条";

        // 链式模式：目录边跑边累积（🔴 ① 节空时才灌，⛔ 不冲掉抓链接的结果）
        if (DlTotal < 0 && _links.Entries.Count == 0)
        {
            EnsureChainHeader();
            _links.Entries.Add(HookEntry(new CrawlEntry(new List<SysInfoCell>
            {
                new("title", "标题", ev.Name, true),
                new("url", "网址", ev.Url, true),
                new("status", "状态", row.StatusText + " " + ev.Detail, true)
            }, ev.Url, ev.Name)));
            _links.NotifyEntriesChanged();
        }
    }

    /// <summary>链式累积的默认三列（标题 / 网址 / 状态）。</summary>
    private void EnsureChainHeader()
    {
        if (_chainHeaderBuilt) return;
        _links.HeaderCells.Clear();
        foreach (var (key, title, width) in new[]
                 {
                     ("title", "标题", "200"),
                     ("url", "网址", "300"),
                     ("status", "状态", "160")
                 })
        {
            _links.HeaderCells.Add(new SysInfoHeaderCell(key, title, "", true) { SortCommand = SortByCommand });
            _links.ColumnSpecs += (_links.ColumnSpecs.Length > 0 ? "," : "") + width;
        }
        _chainHeaderBuilt = true;
        _links.EmptyText = "链式下载进行中：每完成一条就往这里累积一行。";
    }

    // ==================== ④ 运行记录 ====================

    /// <summary>当前「脚本 ▾」选中脚本的最近运行记录（新 → 旧）。</summary>
    public ObservableCollection<RunHistoryRow> HistoryRows { get; } = new();

    /// <summary>🔴 XAML 的 IsVisible 只认 bool（⛔ 绑 .Count）。</summary>
    public bool HasHistoryRows => HistoryRows.Count > 0;

    /// <summary>④ 节空态文案（节体 DataContext 是页面 VM ⇒ 从节对象转一道）。</summary>
    public string HistoryEmptyText => _history?.Hint ?? "";

}
