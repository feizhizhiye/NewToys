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
    /// 读磁盘刷新 ④ 节（⛔ 不跑脚本）：取当前脚本最近 <see cref="RunRecordService.KeepRuns"/> 条 run。
    /// </summary>
    private void RefreshHistory()
    {
        HistoryRows.Clear();
        var script = SelectedScript?.Value ?? "";
        if (script.Length == 0)
        {
            _history.CountText = "—";
            OnPropertyChanged(nameof(HasHistoryRows));
            return;
        }

        foreach (var rec in _records.History(script))
        {
            HistoryRows.Add(new RunHistoryRow(rec)
            {
                // 🔴 右键命令挂在行对象上（ContextMenu 在弹出树，取不到页面 VM）
                OpenDirCommand = OpenRunDirCommand,
                OpenLogCommand = OpenRunLogCommand,
                CopyDiagCommand = CopyRunDiagCommand
            });
        }

        _history.CountText = HistoryRows.Count > 0 ? HistoryRows.Count + " 条" : "—";
        OnPropertyChanged(nameof(HasHistoryRows));
    }

    /// <summary>换了入口脚本 ⇒ 换一批历史。</summary>
    partial void OnSelectedScriptChanged(CategoryOption? value) => RefreshHistory();

    /// <summary>📂 打开这次运行的目录（产物：result.json / page.html / diag.txt）。</summary>
    [RelayCommand]
    private void OpenRunDir(RunHistoryRow? row)
    {
        if (row is null || !Directory.Exists(row.Dir)) { StatusText = "这次运行的目录已经不在了。"; return; }
        try { Process.Start(new ProcessStartInfo(row.Dir) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "⚠ 打开目录失败：" + FirstLine(ex.Message); }
    }

    /// <summary>📄 打开这次运行的日志（diag.txt，没有就记事本打开不了 ⇒ 直接提示）。</summary>
    [RelayCommand]
    private void OpenRunLog(RunHistoryRow? row)
    {
        if (row is null) return;
        if (!File.Exists(row.LogPath)) { StatusText = "这次运行没有留下 diag.txt。"; return; }
        try { Process.Start(new ProcessStartInfo(row.LogPath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = "⚠ 打开日志失败：" + FirstLine(ex.Message); }
    }

    /// <summary>📋 复制这次运行的诊断（diag.txt 原文；没文件就拼一条摘要）。</summary>
    [RelayCommand]
    private async Task CopyRunDiagAsync(RunHistoryRow? row)
    {
        if (row is null) return;

        var text = File.Exists(row.LogPath)
            ? File.ReadAllText(row.LogPath)
            : $"脚本：{SelectedScript?.Label ?? "—"}\n网址：{row.Url}\n时间：{row.AtText}\n{row.Summary}\n目录：{row.Dir}";

        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制这次运行的诊断。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 节头 ====================

    /// <summary>节头 ⟳ = 重跑（目录节 = 重新抓取；试下载节 = 对当前行再试一次）。</summary>
    [RelayCommand]
    private void RefreshSection(CrawlSection? section)
    {
        if (section is { IsLinks: true }) { _ = FetchLinksAsync(); return; }

        // ② 节 ⟳ = 对最后试过的那条再跑一次（🔴 行对象可能已换，按 url + title 重来）
        if (section is { IsPreview: true } && _trialUrl.Length > 0)
        {
            var script = SelectedContentScript?.Value ?? "";
            if (script.Length == 0) { StatusText = "先在「内容脚本 ▾」里选 content 段脚本。"; return; }
            _ = RunTrialAsync(script, _trialUrl, _trialTitle);
        }

        // ④ 节 ⟳ = 重新读磁盘（历史是文件，⛔ 不重跑脚本）
        if (section is { IsHistory: true }) { RefreshHistory(); StatusText = "已刷新运行记录。"; }
    }

    /// <summary>节头折叠 / 展开。</summary>
    [RelayCommand]
    private void ToggleSection(CrawlSection? section)
    {
        if (section is not null) section.IsExpanded = !section.IsExpanded;
    }

    // ==================== 诊断复制 ====================

    /// <summary>
    /// 📋 复制诊断：脚本 / 网址 / HTML 大小 / 每列命中与错误 / 前 5 行（给 AI / 给自己复盘）。
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# NewToys 爬虫面板诊断");
        sb.AppendLine("入口脚本：" + (SelectedScript?.Label ?? "—"));
        sb.AppendLine("content 脚本：" + (SelectedContentScript?.Label ?? "—"));
        sb.AppendLine("流水线：" + (SelectedFlow?.Value.Length > 0 ? SelectedFlow.Value : "（手动）"));
        sb.AppendLine("网址：" + (StartUrl.Length == 0 ? "—" : StartUrl));
        sb.AppendLine("HTML：" + (File.Exists(_run.CrawlHtmlPath)
            ? new FileInfo(_run.CrawlHtmlPath).Length + " 字节"
            : "（没抓）"));
        sb.AppendLine();

        sb.AppendLine("## 列");
        foreach (var c in _last.Columns)
            sb.AppendLine($"  {c.Key} · {c.Expr} · {c.Count} 条" + (c.HasError ? " · 错误：" + c.Error : ""));

        sb.AppendLine();
        sb.AppendLine("## 前 5 行");
        foreach (var row in _last.Rows.Take(5))
            sb.AppendLine("  " + string.Join(" | ",
                _last.Columns.Select(c => c.Key + "=" + (row.TryGetValue(c.Key, out var v) ? v : ""))));

        if (Trial.Diag.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 试下载诊断");
            sb.AppendLine(Trial.Diag);
        }

        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(sb.ToString().TrimEnd());
            StatusText = "已复制诊断（脚本 / 网址 / 每列命中与错误 / 前 5 行）。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    private static string FirstLine(string? text)
    {
        var t = (text ?? "").Trim();
        var nl = t.IndexOf('\n');
        return nl > 0 ? t[..nl] : t;
    }

}
