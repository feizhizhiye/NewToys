using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

public partial class VideoListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{

    /// <summary>只有路径的导入（📂 导入 → 视频文件…、资产库投递的兼容分支）：等价于「路径 + 空元数据」。</summary>
    public int ImportVideoFiles(IEnumerable<string> paths)
        => ImportVideoFiles(paths.Select(p => (Path: p, MetaJson: "")).ToList());

    /// <summary>
    /// 📂 导入 / 资产库投递：把一批「路径 + 资产库元数据」**追加**进列表（不扫描整目录；同路径去重），
    /// 返回**带上了媒体信息**的条数。
    /// <para>元数据顺序：资产库带来的 <c>metadata_json</c>（资产页已经 ffprobe 过）→ 本地 video.db 缓存 → 都没有就留空
    /// （用户再点「🔎 读取信息」）。⛔ 不在导入时同步跑 ffprobe：几十个文件会把界面卡住。</para>
    /// </summary>
    public int ImportVideoFiles(IReadOnlyList<(string Path, string MetaJson)> entries)
    {
        var existing = new HashSet<string>(_all.Select(v => v.FullPath), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var carried = 0;

        foreach (var (path, metaJson) in entries)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!VideoScanner.Extensions.Contains(ext)) continue;   // 只收认得的视频，挡掉误选
            if (existing.Contains(path)) continue;
            existing.Add(path);

            var info = File.Exists(path) ? new FileInfo(path) : null;
            var item = new VideoItem
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                Extension = ext,
                SizeBytes = info?.Length ?? 0,
                Modified = info?.LastWriteTime ?? default
            };

            var media = VideoScanner.TryParseAssetMeta(metaJson);
            if (media != null)
            {
                item.ApplyMedia(media);
                _index?.Upsert(item);      // 顺手写进本地缓存，下次扫描这个目录直接命中
                carried++;
            }
            else _index?.TryApplyCached(item);

            _all.Add(item);
            added++;
        }

        if (added == 0)
        {
            StatusText = "没有可导入的视频文件（仅支持" + string.Join(" / ", VideoScanner.Extensions) + "）。";
            return 0;
        }

        RebuildQuickFilters();
        ApplyView();
        StatusText = carried > 0
            ? $"已导入 {added} 个视频文件（其中 {carried} 个带上了资产库读到的媒体信息）。"
            : $"已导入 {added} 个视频文件（追加到当前列表；媒体信息留空，选中后可点「🔎 读取信息」）。";
        return carried;
    }

    // ==================== 右键菜单 / 详情列的动作 ====================

    /// <summary>▶ 播放：配了外部播放器就用它，否则走系统默认关联程序。</summary>
    [RelayCommand]
    private void Play()
    {
        var v = Selected;
        if (v == null) return;
        if (!File.Exists(v.FullPath)) { StatusText = "文件已不存在：" + v.FullPath; return; }

        var player = (_cfg?.PlayerPath ?? "").Trim();
        try
        {
            if (player.Length > 0 && File.Exists(player))
                Process.Start(new ProcessStartInfo(player, $"\"{v.FullPath}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(v.FullPath) { UseShellExecute = true });
            StatusText = "已播放：" + v.Name;
        }
        catch (Exception ex) { StatusText = "播放失败：" + ex.Message; }
    }

    /// <summary>📂 在资源管理器中定位该文件。</summary>
    [RelayCommand]
    private void RevealInExplorer()
    {
        var v = Selected;
        if (v == null) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{v.FullPath}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(v.Folder) { UseShellExecute = true });
            StatusText = "已在文件管理器中定位。";
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task CopyPathAsync()
    {
        var v = Selected;
        if (v == null) return;
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }
            await win.Clipboard.SetTextAsync(v.FullPath);
            StatusText = "已复制路径：" + v.FullPath;
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    /// <summary>📋 复制 ffmpeg 命令模板（把这条视频转成 H.264 的起手式，直接粘到命令行里改）。</summary>
    [RelayCommand]
    private async Task CopyFfmpegAsync()
    {
        var v = Selected;
        if (v == null) return;
        try
        {
            var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (win?.Clipboard == null) { StatusText = "剪贴板不可用。"; return; }

            var outFile = Path.Combine(v.Folder, Path.GetFileNameWithoutExtension(v.Name) + "_h264.mp4");
            await win.Clipboard.SetTextAsync($"ffmpeg -i \"{v.FullPath}\" -c:v libx264 -crf 20 -c:a aac -b:a 192k \"{outFile}\"");
            StatusText = "已复制 ffmpeg 转码命令模板（可改 crf / 编码后粘到命令行执行）。";
        }
        catch (Exception ex) { StatusText = "复制失败：" + ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var v = Selected;
        if (v == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(v.Folder) { UseShellExecute = true });
            StatusText = "已打开目录：" + v.Folder;
        }
        catch (Exception ex) { StatusText = "打开失败：" + ex.Message; }
    }

    /// <summary>✏ 重命名（命令 / 确认框由宿主注入，见 <see cref="VideoFileService"/>）。</summary>
    [RelayCommand]
    private async Task RenameAsync()
    {
        var v = Selected;
        if (v == null) return;

        var (ok, msg) = await VideoFileService.RenameAsync(v.FullPath, v.Name);
        StatusText = msg;
        if (ok) await ScanAsync();
    }

    /// <summary>🗑 删除（移入回收站，二次确认由宿主注入）。</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var v = Selected;
        if (v == null) { StatusText = "先在列表里选中一个视频（右键会自动选中该行）。"; return; }

        var (ok, msg) = await VideoFileService.DeleteAsync(v.FullPath);
        StatusText = msg;
        if (ok)
        {
            _all.Remove(v);
            Selected = null;
            ApplyView();
        }
    }

    // ==================== v3.15：详情卡内改名 ====================

    /// <summary>
    /// ✏ 详情卡内直接改名（需求 §6.4.7）：提交 → <see cref="VideoFileService.RenameToAsync"/>
    /// （复用现有**防穿越 / 同名**检查）→ 更新内存里的 <see cref="VideoItem"/>。
    /// <para>⛔ **不触发整目录重扫**：重扫会把勾选状态和已读的媒体信息一起冲掉，这里只重渲染列表。</para>
    /// </summary>
    [RelayCommand]
    private async Task SaveNameAsync()
    {
        var v = Selected;
        if (v == null) return;

        var newName = (EditName ?? "").Trim();
        if (newName.Length == 0) { StatusText = "文件名不能为空。"; return; }
        if (string.Equals(newName, v.Name, StringComparison.Ordinal)) { StatusText = "名字没变。"; return; }

        var (ok, msg, newPath) = await VideoFileService.RenameToAsync(v.FullPath, newName);
        StatusText = msg;
        if (!ok || newPath == null) return;

        v.Name = Path.GetFileName(newPath);
        v.FullPath = newPath;

        // Name / FullPath 都是普通属性（不是 [ObservableProperty]），改了不会自动通知：
        // 主动发一次 Selected 变更，让详情卡的绑定重拉一遍。
        OnPropertyChanged(nameof(Selected));
        ApplyView();                                // 列表重渲染（同一批对象引用 → 选中、勾选都不丢）
    }

}
