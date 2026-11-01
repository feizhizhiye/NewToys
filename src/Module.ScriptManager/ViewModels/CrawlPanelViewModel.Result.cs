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
    /// 🔗 抓链接：抓起始网址 → 跑入口脚本 → 目录节出行表。
    /// 🔴 落一条运行记录（⛔ 失败不打断）；结果同时回写底栏摘要。
    /// </summary>
    [RelayCommand]
    private async Task FetchLinksAsync()
    {
        if (IsBusy) return;

        var script = SelectedScript?.Value ?? "";
        if (script.Length == 0) { StatusText = "先在「脚本 ▾」里选一个入口脚本。"; return; }

        var url = (StartUrl ?? "").Trim();
        if (url.Length == 0) { StatusText = "先填起始网址。"; return; }

        IsBusy = true;
        _links.IsBusy = true;
        var sw = Stopwatch.StartNew();
        try
        {
            StatusText = "正在抓取 " + url + " ……";
            var (_, enc) = _files.ReadMeta(script);
            var (ok, _, error) = await _run.FetchIntoAsync(url, enc, _run.CrawlHtmlPath).ConfigureAwait(true);
            if (!ok)
            {
                StatusText = "⚠ " + error;
                _links.Status = CrawlSection.Error;
                return;
            }

            StatusText = "正在跑「" + script + "」……";
            var result = await _run.RunRowsAsync(script, _run.CrawlHtmlPath, url).ConfigureAwait(true);
            sw.Stop();
            _last = result;

            ApplyResult(result);

            try
            {
                _records.SaveFields(script, url, result.Columns.Select(c => new RunField
                {
                    Key = c.Key,
                    Expr = c.Expr,
                    Count = c.Count,
                    Error = c.Error,
                    Preview = c.Preview
                }).ToList(), _run.CrawlHtmlPath);
            }
            catch { /* 运行记录是附属产物，失败只当没记 */ }

            var bad = result.Columns.Count(c => c.HasError);
            FooterSummary = $"条数 {result.Total} · 字段 {result.Columns.Count} · 错误 {bad} · 耗时 {sw.Elapsed.TotalSeconds:F1}s";
            _links.Status = bad > 0 ? CrawlSection.Warn : CrawlSection.Ok;
            _links.CountText = result.Total + " 条";

            StatusText = bad > 0
                ? $"「{script}」跑完：{result.Total} 条（{bad} 列出错 —— 看节体或「📋 复制诊断」）。"
                : $"「{script}」跑完：{result.Total} 条。勾选行 →「⬇ 下载」，或双击 url 行下钻。";
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 运行失败：" + FirstLine(ex.Message);
            _links.Status = CrawlSection.Error;
        }
        finally
        {
            IsBusy = false;
            _links.IsBusy = false;
            RefreshHistory();
        }
    }

    /// <summary>把结果灌进目录节（列 / 表头 / 规格串 / 行；空态文案按情况换）。</summary>
    private void ApplyResult(CrawlResult result)
    {
        _links.Entries.Clear();
        _links.HeaderCells.Clear();
        _links.ColumnSpecs = "";
        _chainHeaderBuilt = false;

        foreach (var col in result.Columns)
        {
            var header = new SysInfoHeaderCell(col.Key, col.Key, "", true) { SortCommand = SortByCommand };
            _links.HeaderCells.Add(header);
        }

        _links.ColumnSpecs = CrawlSpecs.Build(result.Columns, result.Rows);

        foreach (var row in result.Rows)
        {
            var cells = result.Columns
                .Select(c => new SysInfoCell(c.Key, c.Key, row.TryGetValue(c.Key, out var v) ? v : "", true))
                .ToList();
            var url = row.TryGetValue("url", out var u) ? u : "";
            var title = row.TryGetValue("title", out var t) ? t : "";
            _links.Entries.Add(HookEntry(new CrawlEntry(cells, url, title)
            {
                // 🔴 行右键命令挂在行对象上（ContextMenu 弹出树取不到 VM）
                TryDownloadCommand = TryDownloadCommand,
                CopyDiagCommand = CopyDiagCommand
            }));
        }
        _links.NotifyEntriesChanged();

        _links.EmptyText = result.Columns.Count == 0
            ? "这个脚本没有 RULES（或没解析出列）—— 表达式写在 RULES 列表里。"
            : "跑完了，但一条都没解析出来（可能要换起始网址，或表达式该调）。";

        UpdateSortArrows();
    }

    /// <summary>目录行「全选 / 取消勾选」（行右键；行首勾选框与表头全选框已撤，勾选状态看行底色）。</summary>
    [RelayCommand]
    private void ToggleAllLinks()
    {
        if (_links.Entries.Count == 0) return;
        var target = !_links.Entries.All(e => e.IsSelected);
        foreach (var e in _links.Entries) e.IsSelected = target;
        StatusText = target ? $"已勾选 {_links.Entries.Count} 行。" : "已取消勾选。";
    }

    // ==================== 排序（客户端字符串 / 数字排序） ====================

    /// <summary>点表头 = 按这一列排序（再点 = 反向）。</summary>
    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (SortKey == key) SortDescending = !SortDescending;
        else { SortKey = key; SortDescending = false; }

        ReorderEntries();
        UpdateSortArrows();
    }

    private void ReorderEntries()
    {
        if (SortKey.Length == 0 || _links.Entries.Count == 0) return;

        var key = SortKey;
        var desc = SortDescending;
        var items = _links.Entries.ToList();
        items.Sort((a, b) =>
        {
            var va = TextOf(a, key);
            var vb = TextOf(b, key);
            var cmp = BothNumeric(va, vb, out var na, out var nb)
                ? na.CompareTo(nb)
                : string.Compare(va, vb, StringComparison.CurrentCultureIgnoreCase);
            return desc ? -cmp : cmp;
        });

        _links.Entries.Clear();
        foreach (var it in items) _links.Entries.Add(it);
    }

    /// <summary>给行注入右键命令（🔴 菜单在弹出树里，取不到页面 VM 的命令）。</summary>
    private CrawlEntry HookEntry(CrawlEntry entry)
    {
        entry.ToggleAllLinksCommand = ToggleAllLinksCommand;
        return entry;
    }

    private static string TextOf(CrawlEntry entry, string key)
        => entry.Cells.FirstOrDefault(c => c.Attr == key)?.Text ?? "";

    /// <summary>
    /// 两列都能按数值解析时才按数值比（否则按字符串）。
    /// 🔴 先给 out 赋初值：<c>&amp;&amp;</c> 短路会让第二个 <c>TryParse</c> 不执行 ⇒ CS0177。
    /// </summary>
    private static bool BothNumeric(string a, string b, out double na, out double nb)
    {
        na = 0;
        nb = 0;
        return double.TryParse(a, out na) && double.TryParse(b, out nb);
    }

    private void UpdateSortArrows()
    {
        foreach (var h in _links.HeaderCells)
            h.Text = h.Title + (h.Attr == SortKey ? (SortDescending ? " ▼" : " ▲") : "");
    }

    // ==================== 下钻 / 流水线 ====================

}
