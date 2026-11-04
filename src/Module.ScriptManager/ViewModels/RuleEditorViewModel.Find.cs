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

    private void Find(int direction)
    {
        var needle = FindText ?? "";
        if (needle.Length == 0 || HtmlText.Length == 0) { FindHint = ""; return; }

        var idx = -1;
        if (direction >= 0)
        {
            var from = Math.Min(Math.Max(HtmlSelEnd, 0), HtmlText.Length);
            idx = HtmlText.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = HtmlText.IndexOf(needle, 0, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var from = Math.Clamp(HtmlSelStart - 1, 0, Math.Max(HtmlText.Length - 1, 0));
            idx = from < 0 ? -1 : HtmlText.LastIndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = HtmlText.LastIndexOf(needle, HtmlText.Length - 1, StringComparison.OrdinalIgnoreCase);
        }

        if (idx < 0) { FindHint = "没找到"; return; }

        HtmlSelEnd = idx;
        HtmlSelStart = idx;
        HtmlSelEnd = idx + needle.Length;
        FindHint = $"第 {CountBefore(idx) + 1}/{CountTotal()} 个";
        UpdateLineInfo();
    }

    [RelayCommand] private void FindNext() => Find(1);
    [RelayCommand] private void FindPrev() => Find(-1);

    partial void OnHtmlSelStartChanged(int value) => UpdateLineInfo();

    [RelayCommand]
    private void LocateHit()
    {
        var first = (SelectedCommand?.Preview ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()?.Trim() ?? "";
        if (first.Length == 0) { StatusText = "先跑一次「跑这段」，选中命令要有命中结果。"; return; }
        if (first.Length > 200) first = first[..200];

        FindText = first;
        HtmlSelStart = 0;
        HtmlSelEnd = 0;
        Find(1);
        StatusText = FindHint == "没找到"
            ? "命中的这段文本在源码里找不到（页面可能是压缩过的 / 结果经过净化改写）。"
            : "已定位到「" + SelectedCommand?.Key + "」的首条命中。";
    }

    private int CountTotal()
    {
        var needle = FindText ?? "";
        if (needle.Length == 0 || HtmlText.Length == 0) return 0;
        var n = 0;
        var i = HtmlText.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (i >= 0) { n++; i = HtmlText.IndexOf(needle, i + needle.Length, StringComparison.OrdinalIgnoreCase); }
        return n;
    }

    private int CountBefore(int index)
    {
        var needle = FindText ?? "";
        if (needle.Length == 0) return 0;
        var n = 0;
        var i = HtmlText.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (i >= 0 && i < index)
        {
            n++;
            i = HtmlText.IndexOf(needle, i + needle.Length, StringComparison.OrdinalIgnoreCase);
        }
        return n;
    }

    private void UpdateLineInfo()
    {
        if (HtmlText.Length == 0) { LineInfo = ""; return; }
        var pos = Math.Clamp(HtmlSelStart, 0, HtmlText.Length);
        var line = HtmlText.Count(ch => ch == '\n') + 1;
        var cur = 1;
        for (var i = 0; i < pos; i++) if (HtmlText[i] == '\n') cur++;
        LineInfo = $"行 {cur} / {line}";
    }

    private void UpdateHtmlInfo()
    {
        if (HtmlText.Length == 0)
        {
            HtmlInfo = "还没抓 HTML。填起始网址后点「🌐 抓页」。";
            return;
        }

        var truncated = false;
        var text = HtmlText;

        if (text.Length > MaxHtmlChars) { text = text[..MaxHtmlChars]; truncated = true; }

        var lines = text.Split('\n');
        if (lines.Length > MaxHtmlLines)
        {
            text = string.Join("\n", lines.Take(MaxHtmlLines));
            truncated = true;
        }

        HtmlText = text;
        HtmlInfo = $"共 {HtmlText.Length:N0} 字符 · {HtmlText.Count(ch => ch == '\n') + 1:N0} 行"
                 + (truncated ? "（已截断显示，完整内容在缓存文件里）" : "");
        HtmlSelStart = 0;
        HtmlSelEnd = 0;
        FindHint = "";
        UpdateLineInfo();
    }

    partial void OnHtmlTextChanged(string value) => OnPropertyChanged(nameof(HasHtml));

    private void NotifyEmpty()
    {
        OnPropertyChanged(nameof(HasHtml));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(HasCandidates));
    }

    private static string FirstLine(string text)
    {
        var idx = text.IndexOf('\n');
        return idx > 0 ? text[..idx] : text;
    }

    /// <summary>把命令列表写成 <c>RULES = [...]</c>，替换文件里原有的块（找不到就追加到末尾）。</summary>
    internal static string ReplaceRulesBlock(string text, IEnumerable<RuleCommand> commands)
    {
        var block = new StringBuilder("RULES = [\n");
        foreach (var c in commands)
            block.Append("    (\"").Append(Esc(c.Key)).Append("\", \"").Append(Esc(c.Expr)).Append("\"),\n");
        block.Append("]\n");

        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(l => l.TrimStart().StartsWith("RULES = [", StringComparison.Ordinal));
        if (start < 0) return (text ?? "").TrimEnd() + "\n\n" + block;

        var end = lines.FindIndex(start, l => l.Trim() == "]");
        if (end < 0) return (text ?? "").TrimEnd() + "\n\n" + block;

        var head = string.Join("\n", lines.Take(start));
        var tail = string.Join("\n", lines.Skip(end + 1));
        return head + "\n" + block + tail;
    }

    private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

}
