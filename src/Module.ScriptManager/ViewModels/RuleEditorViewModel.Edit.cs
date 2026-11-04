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

    [RelayCommand]
    private void Save()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.ScriptName.Length == 0) { StatusText = "先给这个槽位选一个脚本。"; return; }

        try
        {
            var text = _files.Read(slot.ScriptName);
            _files.Write(slot.ScriptName, ReplaceRulesBlock(text, _allCommands));
            StatusText = "已保存到" + slot.ScriptName + ".py";
        }
        catch (Exception ex) { StatusText = "⚠ 保存失败：" + FirstLine(ex.Message); }
    }

    [RelayCommand]
    private void AddCommand()
    {
        var n = 1;
        while (_allCommands.Any(c => c.Key == "field" + n)) n++;
        var cmd = new RuleCommand { Key = "field" + n, Expr = "css:" };
        _allCommands.Add(cmd);
        Keyword = "";
        ApplyFilter();
        SelectedCommand = cmd;
        NotifyEmpty();
        StatusText = "已加一条命令，改完记得「保存」。";
    }

    [RelayCommand]
    private void RemoveCommand()
    {
        if (SelectedCommand is null) return;
        var idx = Commands.IndexOf(SelectedCommand);
        _allCommands.Remove(SelectedCommand);
        ApplyFilter();
        SelectedCommand = Commands.Count == 0 ? null : Commands[Math.Min(idx, Commands.Count - 1)];
        NotifyEmpty();
    }

    partial void OnKeywordChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var kw = (Keyword ?? "").Trim();
        Commands.Clear();
        foreach (var c in _allCommands)
        {
            if (kw.Length == 0
                || c.Key.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || c.Expr.Contains(kw, StringComparison.OrdinalIgnoreCase))
                Commands.Add(c);
        }

        if (SelectedCommand is not null && !Commands.Contains(SelectedCommand))
            SelectedCommand = Commands.FirstOrDefault();
        NotifyEmpty();
    }

    private void ReplaceCommands(IEnumerable<RuleCommand> list)
    {
        var keepKey = SelectedCommand?.Key;
        _allCommands.Clear();
        _allCommands.AddRange(list);
        ApplyFilter();
        SelectedCommand = keepKey is null
            ? Commands.FirstOrDefault()
            : Commands.FirstOrDefault(c => c.Key == keepKey) ?? Commands.FirstOrDefault();
    }

    // ==================== 诊断复制 ====================

    /// <summary>
    /// 📋 复制诊断：把「现在这台机器上到底发生了什么」打包成一段纯文本（给 AI / 给自己复盘）。
    /// 🔴 必须含：段位 / 脚本名 / 起始网址 / HTML 长度 / 每字段 count + error / traceback。
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagAsync()
    {
        var sb = new StringBuilder();
        var slot = SelectedSlot;

        sb.AppendLine("# NewToys 抓取诊断");
        sb.AppendLine("段位：" + (slot?.Key ?? "—") + "（" + (slot?.Label ?? "—") + "）");
        sb.AppendLine("脚本：" + (slot?.ScriptName ?? "—"));
        sb.AppendLine("起始网址：" + (StartUrl.Length == 0 ? "—" : StartUrl));
        sb.AppendLine("HTML：" + (HtmlText.Length == 0 ? "（没抓）" : HtmlText.Length + " 字符"));
        sb.AppendLine("流水线：" + (FlowName.Length == 0 ? "—" : FlowName));
        sb.AppendLine();

        if (Candidates.Count > 0)
        {
            sb.AppendLine("## 试跑推荐");
            foreach (var c in Candidates)
                sb.AppendLine($"  {c.Name} · {c.CountText} · 完整度 {c.FillText} · {c.FieldsText}"
                            + (c.HasError ? " · 错误：" + c.Error : ""));
            sb.AppendLine();
        }

        sb.AppendLine("## 字段");
        foreach (var c in _allCommands)
        {
            sb.AppendLine($"  {c.Key} · {c.Expr} · {c.HitText}");
            if (c.HasError) sb.AppendLine("    错误：" + c.Error);
        }

        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (win?.Clipboard is null) { StatusText = "剪贴板不可用。"; return; }

        try
        {
            await win.Clipboard.SetTextAsync(sb.ToString().TrimEnd());
            StatusText = "已复制诊断信息（段位 / 脚本 / 网址 / 每字段命中与错误）。";
        }
        catch (Exception ex) { StatusText = "⚠ 复制失败：" + ex.Message; }
    }

    // ==================== 查找 / 定位（保留 收尾能力） ====================

}
