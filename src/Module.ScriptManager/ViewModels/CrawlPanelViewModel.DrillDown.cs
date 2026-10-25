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

    /// <summary>双击 url 行 = 下钻：用这一行的地址再抓一次（链式，用户驱动）。</summary>
    [RelayCommand]
    private void DrillDown(CrawlEntry? entry)
    {
        if (entry is null) return;
        if (entry.Url.Length == 0) { StatusText = "这一行没有 url 列，没法下钻。"; return; }

        StartUrl = entry.Url;
        _ = FetchLinksAsync();
    }

    /// <summary>选流水线 = 按它的槽位填 入口 / content 脚本（ Toc 空 ⇒ 提示链式模式）。</summary>
    partial void OnSelectedFlowChanged(CategoryOption? value)
    {
        if (value is null || value.Value.Length == 0) return;
        try
        {
            var flow = _flows.Load(value.Value);
            if (flow.Entry.Length > 0)
                SelectedScript = ScriptOptions.FirstOrDefault(o => o.Value == flow.Entry) ?? SelectedScript;
            if (flow.Content.Length > 0)
                SelectedContentScript = ContentOptions.FirstOrDefault(o => o.Value == flow.Content) ?? SelectedContentScript;

            StatusText = flow.Toc.Length == 0
                ? $"流水线「{flow.Name}」没配目录段 ⇒ 链式模式：清空勾选，直接「⬇ 下载」从起始网址边跑边累积。"
                : $"已按流水线「{flow.Name}」填好入口 / content 脚本。勾行 →「⬇ 下载」。";
        }
        catch (Exception ex) { StatusText = "⚠ 读流水线失败：" + FirstLine(ex.Message); }
    }

    // ==================== 🧪 试下载 ====================

    /// <summary>
    /// 对一行跑 content 段（含 next 跟进）→ ② 节出预览。⛔ 不落最终产物（临时副本在 trials\）。
    /// </summary>
    [RelayCommand]
    private async Task TryDownload(CrawlEntry? entry)
    {
        if (IsBusy) return;

        var script = SelectedContentScript?.Value ?? "";
        if (script.Length == 0)
        {
            StatusText = "先在「内容脚本 ▾」里选 content 段脚本（产出正文 / 文件的那个）。";
            return;
        }

        var row = entry ?? SelectedEntry
                       ?? _links.Entries.FirstOrDefault(e => e.IsSelected)
                       ?? _links.Entries.FirstOrDefault();
        if (row is null) { StatusText = "没有可试下载的行 —— 先「🔗 抓链接」或在起始网址框填个地址。"; return; }
        if (row.Url.Length == 0) { StatusText = "这一行没有 url 列，没法试下载。"; return; }

        await RunTrialAsync(script, row.Url, row.Title).ConfigureAwait(true);
    }

    /// <summary>
    /// 试下载的执行体（🔴 与批量下载共用 <see cref="DownloadService"/> 的 content 管线）。
    /// ⛔ 不落最终产物（临时副本在 <c>trials\</c>）。
    /// </summary>
    private async Task RunTrialAsync(string script, string url, string title)
    {
        // 记住这一条：② 节头 ⟳ 重跑要用（行可能在重排 / 重新抓取后换对象）
        _trialUrl = url;
        _trialTitle = title;

        IsBusy = true;
        _preview.IsBusy = true;
        try
        {
            var name = title.Length > 0 ? title : url;
            StatusText = "🧪 试下载：「" + name + "」……";
            var t = await _dl.TrialAsync(script, url, title, CancellationToken.None).ConfigureAwait(true);

            Trial = t;
            _preview.IsBusy = false;
            _preview.CountText = t.Error.Length > 0 ? "出错" : t.Pages + " 页 · " + t.Seconds.ToString("F1") + "s";
            _preview.Status = t.Error.Length > 0 ? CrawlSection.Error : CrawlSection.Ok;
            StatusText = t.Error.Length > 0
                ? "⚠ 试下载失败：" + t.Error
                : "🧪 试下载完成（" + t.KindText + "，⛔ 未落最终产物）。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 试下载失败：" + FirstLine(ex.Message);
            _preview.Status = CrawlSection.Error;
            _preview.IsBusy = false;
        }
        finally
        {
            IsBusy = false;
            _preview.IsBusy = false;
        }
    }

    // ==================== ⬇ 批量下载 ====================

}
