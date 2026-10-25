using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;   // 🔴 SetTextAsync / GetTextAsync 是 IClipboard 的**扩展方法**，漏了这行会报 CS1061
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.UI.Services;   // 跨页传物（书签 / Ping → 抓取）已上提 NewToys.UI
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class RuleEditorViewModel : ObservableObject, IStatusProvider, IToolbarSource
{

    /// <summary>目录页带过来的规则集：填起始网址 + 按 role 落到对应槽位。</summary>
    private void ApplyPendingRule(string name)
    {
        var meta = _scripts.FirstOrDefault(m => m.Name == name)
                ?? _files.Scan().FirstOrDefault(m => m.Name == name);
        if (meta is null) { StatusText = "找不到规则集：" + name; return; }

        if (meta.BaseUrl.Length > 0) StartUrl = meta.BaseUrl;
        Encoding = meta.Encoding;

        var stage = FlowConfig.StageKeys.FirstOrDefault(k => k == meta.Role.ToLowerInvariant()) ?? "entry";
        var slot = Slots.FirstOrDefault(s => s.Key == stage);
        if (slot is not null)
        {
            var opt = slot.Options.FirstOrDefault(o => o.Value == name);
            if (opt is null)
            {
                opt = new SlotOption { Value = name, Label = name };
                slot.Options.Add(opt);
            }
            slot.Selected = opt;
            SelectedSlot = slot;
        }

        StatusText = "已带入「" + name + "」→ " + stage + " 段。点「🌐 抓页」后再「🔍 试跑推荐」。";
    }

    // ==================== 抓取 / 跑 / 推荐 ====================

    [RelayCommand]
    private async Task FetchHtmlAsync()
    {
        if (StartUrl.Length == 0) { StatusText = "先填起始网址（或从目录页带一个规则集进来）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在抓取 " + StartUrl + " ……";
            var (ok, html, error) = await _run.FetchIntoAsync(StartUrl, Encoding, _run.DebugHtmlPath).ConfigureAwait(true);
            if (!ok) { StatusText = "⚠ " + error; return; }

            HtmlText = html;
            UpdateHtmlInfo();
            StatusText = $"已抓取 {html.Length} 字符（共享 HTML，试跑推荐用它）。";
        }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>跑当前槽位选中的脚本（用共享 HTML），回填命中 / 预览 / 错误。</summary>
    [RelayCommand]
    private async Task RunSegmentAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.ScriptName.Length == 0) { StatusText = "先给这个槽位选一个脚本。"; return; }
        if (HtmlText.Length == 0) { StatusText = "先点「🌐 抓页」（没有 HTML 无从求值）。"; return; }

        try
        {
            IsBusy = true;
            StatusText = "正在跑「" + slot.ScriptName + "」……";
            var rows = await _run.RunScriptAsync(slot.ScriptName, _run.DebugHtmlPath, StartUrl).ConfigureAwait(true);

            MergeCommands(rows);
            slot.HitText = Summarize(rows, out var missUrl);

            // 落一条运行记录（⛔ 记录失败不打断本次运行）
            try { _records.Save(slot.ScriptName, StartUrl, rows, _run.DebugHtmlPath); }
            catch { /* 运行记录是附属产物，失败只当没记 */ }

            // 🔴 段间契约：入口 / 中间段输出必须含 url，缺了显式报出来（⛔ 不静默）
            if (missUrl && slot.Key != "content")
                StatusText = $"⚠ 「{slot.ScriptName}」的输出里没有 url 字段 —— 下一段拿不到要抓的地址。";
            else
                StatusText = $"「{slot.ScriptName}」跑完：{slot.HitText}。";
        }
        catch (Exception ex) { StatusText = "⚠ 运行失败：" + FirstLine(ex.Message); }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>
    /// 试跑推荐：当前槽位的**全部候选**对同一份 HTML 求值 → 排序展示。
    /// ⛔ 只推荐不代选（用户点「✅ 用它」才写进槽位）。
    /// </summary>
    [RelayCommand]
    private async Task RecommendAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.BuiltIn) { StatusText = "先选一个可选脚本的段位。"; return; }
        if (HtmlText.Length == 0) { StatusText = "先点「🌐 抓页」——推荐要对同一份 HTML 求值。"; return; }

        var names = slot.Options.Where(o => o.Value.Length > 0).Select(o => o.Value).ToList();
        if (names.Count == 0)
        {
            StatusText = "这个段位没有候选脚本（脚本头要写 # role: " + slot.Key + "）。";
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "正在对 " + names.Count + " 个候选求值……";
            var list = await _run.RunProbeAsync(
                _run.DebugHtmlPath,
                StartUrl,
                names.Select(n => (n, _files.PathOf(n)))).ConfigureAwait(true);

            Candidates.Clear();
            foreach (var c in list) Candidates.Add(c);
            SelectedCandidate = Candidates.FirstOrDefault();

            var bad = list.Count(c => c.HasError);
            var top = list.FirstOrDefault(c => c.Count > 0);
            StatusText = top is null
                ? $"没有一个候选在这份 HTML 上出条目（{names.Count} 个候选，{bad} 个报错）—— 页面可能要换起始网址。"
                : $"推荐「{top.Name}」：{top.Count} 条 · 完整度 {top.FillText}" + (bad > 0 ? $"（{bad} 个候选报错）" : "");
        }
        catch (Exception ex) { StatusText = "⚠ 试跑失败：" + FirstLine(ex.Message); }
        finally { IsBusy = false; NotifyEmpty(); }
    }

    /// <summary>把推荐结果里选中的那条写进当前槽位（⛔ 不自动选，必须用户点）。</summary>
    [RelayCommand]
    private void ApplyCandidate()
    {
        var slot = SelectedSlot;
        var cand = SelectedCandidate;
        if (slot is null || cand is null) { StatusText = "先在推荐列表里选一条。"; return; }

        var opt = slot.Options.FirstOrDefault(o => o.Value == cand.Name)
               ?? new SlotOption { Value = cand.Name, Label = cand.Name };
        if (!slot.Options.Contains(opt)) slot.Options.Add(opt);
        slot.Selected = opt;
        StatusText = "已把「" + cand.Name + "」填进 " + slot.Label + " 段。";
    }

    /// <summary>右栏：载入当前槽位脚本的命令列表（<c>RULES</c>）。</summary>
    private async Task LoadCommandsAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.ScriptName.Length == 0)
        {
            _allCommands.Clear();
            ApplyFilter();
            return;
        }

        try
        {
            var list = await _run.ListCommandsAsync(slot.ScriptName).ConfigureAwait(true);
            ReplaceCommands(list);
        }
        catch (Exception ex)
        {
            StatusText = "⚠ 读取命令失败：" + FirstLine(ex.Message);
        }
        finally { NotifyEmpty(); }
    }

    /// <summary>就地合并运行结果（⛔ 不重建集合，否则选中项丢失）。</summary>
    private void MergeCommands(IEnumerable<RuleCommand> rows)
    {
        foreach (var row in rows)
        {
            var exist = _allCommands.FirstOrDefault(c => c.Key == row.Key);
            if (exist is null) { _allCommands.Add(row); continue; }
            exist.Expr = row.Expr;
            exist.HitText = row.HitText;
            exist.Preview = row.Preview;
            exist.Error = row.Error;
        }

        ApplyFilter();
    }

    /// <summary>命中摘要「12 条 · 3/3 字段」，并回报是否缺 <c>url</c>。</summary>
    private static string Summarize(IReadOnlyList<RuleCommand> rows, out bool missUrl)
    {
        missUrl = false;
        if (rows.Count == 0) return "没有命令";

        var url = rows.FirstOrDefault(r => r.Key.Equals("url", StringComparison.OrdinalIgnoreCase));
        var count = url is not null ? ParseCount(url.HitText) : rows.Max(r => ParseCount(r.HitText));
        var filled = rows.Count(r => ParseCount(r.HitText) > 0);
        missUrl = url is null || ParseCount(url.HitText) == 0;
        return $"{count} 条 · {filled}/{rows.Count} 字段";
    }

    private static int ParseCount(string hitText)
    {
        var t = (hitText ?? "").Replace(" 条", "");
        return int.TryParse(t, out var n) ? n : 0;
    }

    // ==================== 流水线（flow） ====================

}
