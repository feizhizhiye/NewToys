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
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

public partial class ScriptInfoViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    private void LoadSectionSettings()
    {
        var item = SettingsSection?.Item;
        if (item == null) return;

        SettingsParser = ParserOptions.FirstOrDefault(o => o.Value == ScriptParseMode.Normalize(item.ParseMode));
        SettingsTimeout = (item.Timeout <= 0 ? 30 : item.Timeout).ToString();
        SettingsRetention = RetentionOptions.FirstOrDefault(o => o.Value == ScriptRetentionMode.Normalize(item.RetentionMode));
        SettingsRetentionN = Math.Max(0, item.RetentionN).ToString();
        SettingsAuto = item.Auto;
        SettingsInterval = Math.Max(0, item.IntervalSeconds).ToString();

        var col = CurrentCollection();
        CollectionRefresh = CollectionRefreshOptions.FirstOrDefault(o => o.Value == ScriptRefreshMode.Normalize(col?.RefreshMode));
        CollectionRefreshSeconds = Math.Max(0, col?.RefreshSeconds ?? 0).ToString();

        OnPropertyChanged(nameof(CanAuto));
        OnPropertyChanged(nameof(AutoHint));
        OnPropertyChanged(nameof(RetentionHint));
    }

    private void OpenSettingsFor(InfoSectionViewModel section)
    {
        SettingsSection = section;
        OpenDetailPanelCommand.Execute(null);
    }

    /// <summary>保存节设置 + 集合刷新策略。</summary>
    [RelayCommand]
    private void SaveSectionSettings()
    {
        var item = SettingsSection?.Item;
        if (item == null || _store == null) return;

        item.ParseMode = ScriptParseMode.Normalize(SettingsParser?.Value);
        item.Timeout = int.TryParse((SettingsTimeout ?? "").Trim(), out var t) && t > 0 ? Math.Min(t, 300) : 30;

        // 只读条目强制 latest（硬约束）
        item.RetentionMode = item.Readonly
            ? ScriptRetentionMode.Latest
            : ScriptRetentionMode.Normalize(SettingsRetention?.Value);
        item.RetentionN = item.RetentionMode == ScriptRetentionMode.Latest
            ? 0
            : Math.Max(0, int.TryParse((SettingsRetentionN ?? "").Trim(), out var n) ? n : 0);

        item.Auto = CollectorService.CanAutoRefresh(item) && SettingsAuto;
        item.IntervalSeconds = CollectorService.ClampInterval(
            int.TryParse((SettingsInterval ?? "").Trim(), out var i) ? i : 0);

        var col = CurrentCollection();
        if (col != null)
        {
            col.RefreshMode = ScriptRefreshMode.Normalize(CollectionRefresh?.Value);
            col.RefreshSeconds = Math.Max(0, int.TryParse((CollectionRefreshSeconds ?? "").Trim(), out var s) ? s : 0);
        }

        // 列宽属于「显示方式」⇒ 记在当前视图（脚本 schema 只留默认值）。
        if (_view != null && SettingsSection != null)
        {
            ViewColumnProjection.SetWidths(_view, SettingsSection.Item,
                SettingsSection.HeaderCells.Select(h => new KeyValuePair<string, string>(h.Attr, h.Width)));
        }

        _store.Save();

        // 宽度改变要重建列定义才生效（ApplyColumns 保留 RowKey / 选中）。
        if (SettingsSection != null)
            SettingsSection.ApplyColumns(ViewColumnProjection.Resolve(SettingsSection.Item, _view));

        LoadSectionSettings();
        StatusText = $"已保存「{SettingsSection?.Title}」的采集设置。";
    }

    // ==================== 操作 ====================

    /// <summary>📋 复制选中行的「属性 = 值」。</summary>
    [RelayCommand]
    private async Task CopySelectedRowAsync()
    {
        if (SelectedRow is not { } row) { StatusText = "先在节里选中一行。"; return; }
        await CopyTextAsync(RowText(row));
    }

    /// <summary>📋 复制全部（全部节，制表符分隔）。</summary>
    [RelayCommand]
    private async Task CopyAllAsync()
    {
        var sb = new StringBuilder();
        foreach (var s in Sections)
        {
            if (s.AllRows.Count == 0) continue;

            sb.AppendLine("## " + s.Title);
            sb.AppendLine(string.Join('\t', s.HeaderCells.Select(h => h.Title)));
            foreach (var r in s.AllRows)
                sb.AppendLine(string.Join('\t', r.Cells.Select(c => c.Text)));
            sb.AppendLine();
        }

        if (sb.Length == 0) { StatusText = "还没有数据。"; return; }
        await CopyTextAsync(sb.ToString());
    }

    private static string RowText(SysInfoRow row)
        => string.Join(Environment.NewLine, row.Cells.Where(c => c.IsVisible).Select(c => $"{c.Title} = {c.Text}"));

    private async Task CopyTextAsync(string text)
    {
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(text);
            StatusText = "已复制到剪贴板。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>✏ 打开采集器脚本（命令型条目没有文件）。</summary>
    [RelayCommand]
    private void EditSectionScript(InfoSectionViewModel? section)
    {
        if (section == null) return;
        if (CollectorService.IsCommandItem(section.Item)) { StatusText = "这是命令行采集器，没有脚本文件。"; return; }

        var path = CollectableEvaluator.ResolveScriptPath(section.Item.Target, _settings?.EffectiveScriptsFolder ?? "");
        if (path.Length == 0 || !File.Exists(path)) { StatusText = "⚠ 找不到脚本文件：" + section.Item.Target; return; }
        OpenWithShell(path, "脚本");
    }

    /// <summary>📂 打开脚本目录。</summary>
    [RelayCommand]
    private void OpenScriptDir()
    {
        try
        {
            var dir = _settings?.EffectiveScriptsFolder ?? "";
            if (dir.Length == 0) { StatusText = "⚠ 脚本目录不可用"; return; }
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            StatusText = "已打开脚本目录：" + dir;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    private void OpenWithShell(string path, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusText = $"已打开{what}：{path}";
        }
        catch (Exception ex) { StatusText = $"打开{what}失败：" + ex.Message; }
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

}
