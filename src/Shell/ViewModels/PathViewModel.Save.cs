using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shell.Models;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

public partial class PathViewModel : ListPageViewModelBase, IStatusActionProvider
{

    /// <summary>💾 保存：把当前选中项的值写回 <c>app.json</c>。</summary>
    [RelayCommand]
    private void Save()
    {
        var item = Selected;
        if (item == null || _cfg == null) { StatusText = "先在左侧选中一项。"; return; }

        if (item.IsReadOnly) { StatusText = $"「{item.Name}」是只读项，不能改。"; return; }

        item.Value = (EditValue ?? "").Trim();
        WriteBack();
        StatusText = $"已保存「{item.Name}」→ {item.ValueDisplay}（下一轮启动 / 调用即生效）";
    }

    /// <summary>↩ 还原默认：只**预填**默认值，还要点「💾 保存」才生效（规范 3.5）。</summary>
    [RelayCommand]
    private void RestoreDefault()
    {
        var item = Selected;
        if (item == null) { StatusText = "先在左侧选中一项。"; return; }
        if (item.IsReadOnly) { StatusText = "只读项没有默认值可还原。"; return; }

        EditValue = item.DefaultValue;
        StatusText = item.DefaultValue.Length > 0
            ? $"已预填默认值「{item.DefaultValue}」，点「💾 保存」生效。"
            : "默认值就是留空（用系统默认），点「💾 保存」生效。";
    }

    /// <summary>工具条 / 右键菜单的「📂 浏览…」：作用于当前选中项。</summary>
    [RelayCommand]
    private async Task PickPathAsync() => await BrowseAsync(Selected);

    /// <summary>行内「📂 浏览」：作用于这一行（⛔ 不必先选中）。</summary>
    [RelayCommand]
    private async Task PickRowPathAsync(PathSettingItem? row) => await BrowseAsync(row ?? Selected);

    /// <summary>
    /// 选文件 / 选目录（按行的 <see cref="PathItemKind"/> 决定用哪个选择器），选完**立即生效**并落盘。
    /// <para>v3.53：改走 <c>DialogHooks</c>（宿主注入）—— VM ⛔ 不引 Avalonia，页内 code-behind 不再需要接线。</para>
    /// </summary>
    private async Task BrowseAsync(PathSettingItem? item)
    {
        if (item == null) { StatusText = "先在左侧选中一项。"; return; }
        if (item.IsReadOnly) { StatusText = $"「{item.Name}」是只读展示项，不能改。"; return; }

        try
        {
            if (item.Kind == PathItemKind.Folder)
            {
                var dir = await DialogHooks.PickFolderAsync($"选择{item.Name}", ImportExportDefaultDir);
                if (!string.IsNullOrWhiteSpace(dir)) ApplyPickedValue(item, dir!);
                return;
            }

            var files = await DialogHooks.PickFilesAsync($"选择{item.Name}", ImportExportDefaultDir, false, "*");
            var path = files.Count > 0 ? files[0] : null;
            if (!string.IsNullOrWhiteSpace(path)) ApplyPickedValue(item, path!);
        }
        catch (Exception ex)
        {
            StatusText = "选择路径失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 「📂 浏览…」选完之后调用：选路径是**明确的动作** → 立即生效并落盘
    /// （不用再点保存；这也避免了"选了却没生效"的困惑）。
    /// </summary>
    public void ApplyPickedValue(PathSettingItem item, string path)
    {
        if (item == null || item.IsReadOnly || string.IsNullOrWhiteSpace(path)) return;

        item.Value = path.Trim();
        if (ReferenceEquals(item, Selected)) EditValue = item.Value;
        WriteBack();
        StatusText = $"已设置「{item.Name}」→ {item.Value}";
    }

    /// <summary>↗ 打开：目录项打开目录；文件项在资源管理器里定位（文件不存在就退到它的上级目录）。
    /// 不传参就用当前选中项（行内按钮 / 详情卡按钮都可用）。</summary>
    [RelayCommand]
    private void Open(PathSettingItem? item)
    {
        var target = item ?? Selected;
        if (target == null) { StatusText = "先在左侧选中一项。"; return; }

        var value = (target.Value.Length > 0 ? target.Value : target.DefaultValue).Trim();
        if (value.Length == 0) { StatusText = "这一项还是空的，没有可打开的位置。"; return; }

        try
        {
            if (target.Kind == PathItemKind.Folder || Directory.Exists(value))
            {
                Directory.CreateDirectory(value);
                Process.Start(new ProcessStartInfo("explorer.exe", value) { UseShellExecute = true });
            }
            else if (File.Exists(value))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{value}\"") { UseShellExecute = true });
            }
            else
            {
                var dir = Path.GetDirectoryName(value) ?? "";
                if (dir.Length == 0 || !Directory.Exists(dir)) { StatusText = $"找不到这个位置：{value}"; return; }
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            StatusText = "已打开：" + value;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenConfigDir()
    {
        try
        {
            var dir = _store?.ConfigDir ?? Path.Combine(AppContext.BaseDirectory, "config");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch { StatusText = "无法打开配置目录。"; }
    }

    /// <summary>把列表里每个可编辑项的值写回 <see cref="IAppConfigService"/>（每个 setter 各自落盘，这里收口存储根）。</summary>
    private void WriteBack()
    {
        if (_cfg == null) return;
        foreach (var it in Items)
        {
            var v = it.Value.Trim();
            switch (it.Key)
            {
                case "FfmpegPath": _cfg.FfmpegPath = v; break;
                case "PlayerPath": _cfg.PlayerPath = v; break;
                // 三个解释器路径回迁应用级（app.json）—— 本页是它们的唯一写入点。
                case "PythonPath": _cfg.PythonPath = v; break;
                case "PwshPath": _cfg.PwshPath = v; break;
                case "PowerShell51Path": _cfg.PowerShell51Path = v; break;
                case "SevenZipPath": _cfg.SevenZipPath = v; break;
                // 以上五项已下沉 env.json（机器级，⛔ app.json 里不再有这些键）。
                // ⚠️ 脚本目录（ScriptFolder）仍归「设置 → 脚本管理器设置」（config\modules\script-manager.json），⛔ 本页不写它。
                // StoreDir / CacheDir / ConfigDir / AppConfigFile 是只读展示项，不写；配置行（Key = "profile:<名>"）不属于 app.json
            }
        }
        _cfg.Save();
    }

    // ==================== 配置侧动作（v3.53：由「设置 → 配置」页原样并入） ====================
    // 🔴 语义**零改动**：⇄ 切换仍二次确认 + 本窗重启；🗑 删除仍禁删当前配置并二次确认。
    // ♻ 重置（内置档语义）已撤销 —— 配置只是根目录，没有"初始态"这个概念。
    // 每个命令都收一个行参数（行内按钮传行、工具条与详情卡不传 ⇒ 用当前选中项）。

    /// <summary>把行参数归一成"当前要操作的配置行"（不传参就用选中项）。</summary>
    private PathSettingItem? Resolve(PathSettingItem? row) => row ?? Selected;

}
