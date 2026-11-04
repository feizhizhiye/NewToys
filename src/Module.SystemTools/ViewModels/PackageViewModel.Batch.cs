using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.SystemTools.Models;
using NewToys.SystemTools.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.SystemTools.ViewModels;

public partial class PackageViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>
    /// 「安装选中」：把勾选的包**逐条串行**安装（winget 自身会争用源，⛔ 不并行）。
    /// <para>单条失败不影响其余（失败隔离）；结束汇总成功 / 失败明细。已安装的包 winget 会自行跳过。</para>
    /// </summary>
    [RelayCommand]
    private async Task InstallCheckedAsync()
    {
        if (_pkg == null || IsBusy) return;

        var targets = BatchSelection.ToList();
        if (targets.Count == 0) { StatusText = "先在列表里选中要安装的包（Ctrl / Shift 可多选）。"; return; }

        try
        {
            IsBusy = true;
            var ok = 0;
            var fails = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                StatusText = $"正在安装 {i + 1}/{targets.Count}：{p.Id} …（较大的包会久一点，请勿关闭窗口）";
                var (success, msg) = await _pkg.InstallAsync(p.Id,
                    p.Source, SilentInstall);
                if (success) ok++;
                else fails.Add($"{p.Id}（{msg}）");
            }

            _installedIds = null;
            _installedList = null;
            await MarkInstalledAsync();
            ApplyView();
            StatusText = fails.Count == 0
                ? $"批量安装完成：{ok} 个全部成功。"
                : $"批量安装完成：成功 {ok} / 失败 {fails.Count}；失败明细 —— {string.Join("；", fails)}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 「升级选中」：勾选的包里**有可升级版本**的逐条升级；没有的跳过并计数（⛔ 不拿没有新版本的包去试）。
    /// </summary>
    [RelayCommand]
    private async Task UpgradeCheckedAsync()
    {
        if (_pkg == null || IsBusy) return;

        var checkedAll = BatchSelection.ToList();
        if (checkedAll.Count == 0) { StatusText = "先在列表里选中要升级的包（Ctrl / Shift 可多选）。"; return; }

        var targets = checkedAll.Where(p => p.HasUpgrade).ToList();
        var skipped = checkedAll.Count - targets.Count;
        if (targets.Count == 0)
        {
            StatusText = $"选中的 {checkedAll.Count} 个都没有可升级版本（已是最新）。";
            return;
        }

        try
        {
            IsBusy = true;
            var ok = 0;
            var fails = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var p = targets[i];
                StatusText = $"正在升级 {i + 1}/{targets.Count}：{p.Id} {p.Version} → {p.Available} …";
                var (success, msg) = await _pkg.UpgradeAsync(p.Id, SilentInstall);
                if (success) ok++;
                else fails.Add($"{p.Id}（{msg}）");
            }

            if (_mode == ListMode.Upgrades) await LoadUpgradesAsync();
            else { await ApplyPinnedAsync(); ApplyView(); }
            StatusText = (fails.Count == 0
                    ? $"批量升级完成：{ok} 个全部成功"
                    : $"批量升级完成：成功 {ok} / 失败 {fails.Count}；失败明细 —— {string.Join("；", fails)}")
                + (skipped > 0 ? $"；跳过 {skipped} 个无可升级版本" : "");
        }
        finally { IsBusy = false; }
    }

    // ==================== 导入 / 导出（v3.19：换机恢复） ====================

    /// <summary>
    /// 📤 导出当前列表为 json（建议先点「已安装」再导）。
    /// <para>选择器走 <see cref="DialogHooks"/>（VM ⛔ 不引 Avalonia UI 类型）；
    /// ⛔ 别回到 <c>PackageView.axaml.cs</c> 挂 TopLevel / StorageProvider。</para>
    /// </summary>
    [RelayCommand]
    private async Task ExportPackagesAsync()
    {
        var path = await DialogHooks.SaveFileAsync("导出已安装包清单", ImportExportDefaultDir, "packages.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, ExportPackagesJson());
            StatusText = $"已导出当前列表到：{path}（新系统上用它生成安装脚本）";
        }
        catch (Exception ex) { StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>📥 导入导出的 json → 生成 winget 批量安装脚本(.bat)（同「导出」搬进 VM）。</summary>
    [RelayCommand]
    private async Task ImportPackagesAsync()
    {
        var files = await DialogHooks.PickFilesAsync("选择要恢复的包清单 json", ImportExportDefaultDir, false, "*.json");
        var src = files.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(src)) return;

        try
        {
            var script = BuildInstallScript(File.ReadAllText(src));
            if (string.IsNullOrEmpty(script))
            {
                StatusText = "⚠ 这个 json 里没有可安装的包（或不是本程序导出的清单）。";
                return;
            }

            var dst = await DialogHooks.SaveFileAsync("保存批量安装脚本", ImportExportDefaultDir, "install-packages.bat", "*.bat");
            if (string.IsNullOrWhiteSpace(dst)) return;

            File.WriteAllText(dst, script);
            StatusText = $"已生成批量安装脚本：{dst}（在新系统上运行它即可恢复）";
        }
        catch (Exception ex) { StatusText = "导入失败：" + ex.Message; }
    }

    /// <summary>📤 导出：把当前列表导成 json（换机恢复用；只留 Id / Name / Version）。建议先在「已安装」后再导。</summary>
    public string ExportPackagesJson()
    {
        var list = _all.Where(p => !string.IsNullOrWhiteSpace(p.Id))
                       .Select(p => new PackageExportItem { Id = p.Id, Name = p.Name, Version = p.Version })
                       .ToList();
        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>📥 导入：把导出的 json 变成 winget 批量安装脚本（.bat）文本；解析失败 / 无有效条目返回空串。</summary>
    public static string BuildInstallScript(string json)
    {
        List<PackageExportItem>? items = null;
        try { items = JsonSerializer.Deserialize<List<PackageExportItem>>(json); }
        catch { return ""; }
        if (items == null || items.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("rem NewToys 生成的 winget 批量安装脚本（换机恢复；建议以管理员运行）");
        var n = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Id)) continue;
            sb.AppendLine($"winget install --id {it.Id} --exact --accept-package-agreements --accept-source-agreements --disable-interactivity");
            n++;
        }
        if (n == 0) return "";
        sb.AppendLine("echo Done.");
        return sb.ToString();
    }

    [RelayCommand]
    private async Task CopyDetailAsync()
    {
        var p = Selected;
        if (p == null) return;

        // 与详情卡字段**一一对应**（加了字段记得同步这里，⛔ 不要漏成"复制少了"）。
        var sb = new StringBuilder();
        Append("名称", p.Name);
        Append("Id", p.Id);
        Append("版本", p.Version);
        Append("可升级到", p.Available);
        Append("状态", p.StatusText);
        Append("源", p.Source);
        Append("发布者", p.Publisher);
        Append("协议 / 许可证", p.License);
        Append("安装位置", p.InstallLocation);
        Append("主页", p.Homepage);
        Append("描述", p.Description);

        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(sb.ToString().TrimEnd());
            StatusText = $"已复制「{p.Name}」的详情信息。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }

        void Append(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.Append(label).Append(" = ").AppendLine(value.Trim());
        }
    }

    [RelayCommand]
    private async Task CopyIdAsync()
    {
        var p = Selected;
        if (p == null) return;
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(p.Id);
            StatusText = $"已复制 Id：{p.Id}";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        var url = Selected?.Homepage;
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText = "这个包没有主页信息；可先点「🔎 读取详情」。";
            return;
        }
        LaunchRunner.OpenUrlDefault(url);
        StatusText = "已用默认浏览器打开：" + url;
    }

    /// <summary>「更多功能」里的一项：在 winget.run 上查这个包（网页版 winget 索引）。</summary>
    [RelayCommand]
    private void OpenInWingetRun()
    {
        var id = Selected?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        LaunchRunner.OpenUrlDefault("https://winget.run/pkg/" + id.Replace('.', '/'));
        StatusText = "已在浏览器打开 winget.run 的包页面。";
    }

    // ==================== 即时写盘（无保存按钮，规范 3.7） ====================

    /// <summary>v3.19：「仅看已安装」从工具条勾选并入表头右键菜单（切换过滤）。</summary>
    [RelayCommand]
    private void ToggleInstalledOnly()
    {
        InstalledOnly = !InstalledOnly;
        StatusText = InstalledOnly ? "仅看已安装（搜索结果里只留本机已装的）。" : "已取消「仅看已安装」。";
    }

    partial void OnSilentInstallChanged(bool value)
    {
        if (_syncing || _settings == null) return;
        _settings.Settings.SilentInstall = value;
        _settings.Save();
        StatusText = value ? "安装 / 升级将使用 --silent（静默）。" : "安装 / 升级会保留安装器界面。";
    }

    // 注：源的下拉只是**只读快照**（进入本页读一次系统 winget 的源）。
    // 要增删 / 切换源请去「命令库 → winget 源」集合（那里的命令带 UAC 提权；改的也是系统 winget）。

}
