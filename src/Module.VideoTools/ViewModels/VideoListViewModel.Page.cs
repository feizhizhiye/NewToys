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

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public VideoListViewModel() { }

    public VideoListViewModel(IAppConfigService cfg, IColumnLayoutService cols, VideoMediaIndex index,
                              VideoTaskStore store, INavigationService nav,
                              IVideoToolsSettingsService? vt = null)
        : base(cols)
    {
        _cfg = cfg;
        _index = index;
        _store = store;
        _nav = nav;
        _vt = vt;
        LoadLayout();
        Folder = _lastFolder;                                                            // 本次运行内记住上次选的目录
        UpdateHeaders();

        // 目录**不来自设置**（v1.34 解绑）：没选过就如实提示，由用户点「📂 导入」选
        if (string.IsNullOrWhiteSpace(Folder))
            StatusText = "点「📂 导入」选一个视频目录（每次都手动选，不与设置绑定）；扫描后可从表头看到分辨率 / 编码 / 码率 / 时长。";
        else if (!Directory.Exists(Folder))
            StatusText = $"上次的目录已经不在了：{Folder} —— 重新选一个。";
        else _ = ScanAsync();
    }

    // ==================== 扫描 ====================

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;

        var dir = (Folder ?? "").Trim();
        if (dir.Length == 0) { StatusText = "请先填写或选择一个视频目录。"; return; }
        if (!Directory.Exists(dir)) { StatusText = $"目录不存在：{dir}"; return; }

        _lastFolder = dir;   // 只在本次运行内记住；不写进任何配置文件（v1.34 与设置解绑）

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            IsBusy = true;
            StatusText = $"正在扫描 {dir} ……";

            var items = await Task.Run(() => VideoScanner.Scan(dir, Recursive, ct), ct);
            ct.ThrowIfCancellationRequested();

            // SQLite 缓存命中：大小 / 修改时间没变的，直接把媒体信息填上（零成本，不跑 ffprobe）
            var cached = 0;
            if (_index != null)
            {
                foreach (var v in items)
                    if (_index.TryApplyCached(v)) cached++;
            }

            // 保留上次的勾选状态（重扫不该把用户挑好的一批弄丢）
            var checkedPaths = new HashSet<string>(_all.Where(v => v.IsChecked).Select(v => v.FullPath),
                                                   StringComparer.OrdinalIgnoreCase);
            foreach (var v in items) v.IsChecked = checkedPaths.Contains(v.FullPath);

            _all.Clear();
            _all.AddRange(items);
            RebuildQuickFilters();      // v3.21：类型值进表头右键「筛选」
            ApplyView();

            var missing = items.Count - items.Count(v => v.HasMedia);
            StatusText = $"扫描完成：{items.Count} 个视频（{DateTime.Now:HH:mm:ss}）"
                         + (Recursive ? " · 含子目录" : " · 仅当前目录")
                         + (cached > 0 ? $" · 缓存命中 {cached}" : "")
                         + (missing > 0 ? $" · {missing} 个还没有媒体信息，点「🔎 读取全部」" : "");
        }
        catch (OperationCanceledException) { StatusText = "扫描已取消。"; }
        catch (Exception ex) { StatusText = "扫描失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ScanAsync();

    // ==================== 过滤 / 排序 ====================

    partial void OnRecursiveChanged(bool value)
    {
        if (_syncing || string.IsNullOrWhiteSpace(Folder)) return;
        _ = ScanAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    /// <summary>
    /// 「失焦即提交」的文件名（需求 §6.4.7）：声明式卡的输入框失焦时把文本写回 <see cref="EditName"/>，
    /// 这里再落盘 —— 等价于迁移前 code-behind 的 <c>NameBox.LostFocus → SaveNameCommand</c>。
    /// <para>⛔ 填充编辑缓冲（切选中项）时也会走到这里，所以先跟当前选中项比一次名字：
    /// 没变就直接返回，⛔ 不去碰磁盘。</para>
    /// </summary>
    partial void OnEditNameChanged(string value)
    {
        if (Selected == null) return;
        var name = (value ?? "").Trim();
        if (name.Length == 0) return;
        if (string.Equals(name, Selected.Name, StringComparison.Ordinal)) return;
        SaveNameCommand.Execute(null);
    }

    partial void OnSelectedChanged(VideoItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DetailItem));
        // 详情卡可编辑字段跟着选中项走：切换选中即丢弃**未提交**的改动（提交要显式回车 / 失焦）；
        // 没有选中项 → 编辑缓冲留空
        EditName = value?.Name ?? "";
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Videos.Count > 0) Selected = Videos[0];
    }

    /// <summary>筛选取值（多项筛选用）：按列键拿这一行的文本。</summary>
    protected override string? FieldText(object row, string columnKey) => columnKey switch
    {
        "Name" => ((VideoItem)row).Name,
        "Type" => ((VideoItem)row).Extension,
        "Size" => ((VideoItem)row).SizeDisplay,
        "Duration" => ((VideoItem)row).DurationDisplay,
        "Resolution" => ((VideoItem)row).ResolutionDisplay,
        "Codec" => ((VideoItem)row).CodecDisplay,
        "Bitrate" => ((VideoItem)row).BitRateDisplay,
        "Modified" => ((VideoItem)row).ModifiedDisplay,
        // v3.15 扩充列：列键与模型属性名不一致的（Container/Audio/Path）在这里显式映射，
        // 不能靠基类的反射兜底（反射按列键找属性，找不着会整条筛选条件失效）。
        "Fps" => ((VideoItem)row).FpsDisplay,
        "Container" => ((VideoItem)row).ContainerDisplay,
        "Audio" => ((VideoItem)row).AudioCodecDisplay,
        "Path" => ((VideoItem)row).FullPath,
        "Created" => ((VideoItem)row).CreatedDisplay,
        _ => null
    };

    /// <summary>筛选取值（数值比较用）：大小按字节、时长按秒、分辨率按像素数。</summary>
    protected override double? FieldNumber(object row, string columnKey)
    {
        var v = (VideoItem)row;
        return columnKey switch
        {
            "Size" => v.SizeBytes,
            "Duration" => v.Duration,
            "Resolution" => (v.Width ?? 0) * (double)(v.Height ?? 0),
            "Modified" => v.Modified.ToOADate(),
            "Fps" => v.Fps,
            "Created" => v.Created == default ? null : v.Created.ToOADate(),
            _ => null
        };
    }

    private void ApplyView()
    {
        IEnumerable<VideoItem> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(v => v.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || v.Extension.Contains(text, StringComparison.OrdinalIgnoreCase));

        // 多项筛选（基类）：多条条件之间取「且」
        q = ApplyFilters(q);

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = SortColumn switch
        {
            "Type" => SortDescending ? q.OrderByDescending(v => v.Extension, byText) : q.OrderBy(v => v.Extension, byText),
            "Size" => SortDescending ? q.OrderByDescending(v => v.SizeBytes) : q.OrderBy(v => v.SizeBytes),
            "Modified" => SortDescending ? q.OrderByDescending(v => v.Modified) : q.OrderBy(v => v.Modified),
            // 未读取媒体信息的排最后（-1），而不是当成 0 秒排最前
            "Duration" => SortDescending ? q.OrderByDescending(v => v.Duration ?? -1) : q.OrderBy(v => v.Duration ?? -1),
            "Resolution" => SortDescending ? q.OrderByDescending(v => (v.Width ?? 0) * (long)(v.Height ?? 0))
                                            : q.OrderBy(v => (v.Width ?? 0) * (long)(v.Height ?? 0)),
            "Codec" => SortDescending ? q.OrderByDescending(v => v.Codec, byText) : q.OrderBy(v => v.Codec, byText),
            "Bitrate" => SortDescending ? q.OrderByDescending(v => v.BitRate ?? -1) : q.OrderBy(v => v.BitRate ?? -1),
            // v3.15 扩充列
            "Fps" => SortDescending ? q.OrderByDescending(v => v.Fps ?? -1) : q.OrderBy(v => v.Fps ?? -1),
            "Container" => SortDescending ? q.OrderByDescending(v => v.ContainerFormat, byText) : q.OrderBy(v => v.ContainerFormat, byText),
            "Audio" => SortDescending ? q.OrderByDescending(v => v.AudioCodec, byText) : q.OrderBy(v => v.AudioCodec, byText),
            "Path" => SortDescending ? q.OrderByDescending(v => v.FullPath, byText) : q.OrderBy(v => v.FullPath, byText),
            "Created" => SortDescending ? q.OrderByDescending(v => v.Created) : q.OrderBy(v => v.Created),
            _ => SortDescending ? q.OrderByDescending(v => v.Name, byText) : q.OrderBy(v => v.Name, byText)
        };

        _syncing = true;
        try
        {
            Videos.Clear();
            foreach (var v in q) Videos.Add(v);
        }
        finally { _syncing = false; }

        if (Selected != null && !Videos.Contains(Selected)) Selected = null;

        UpdateHeaders();
        var total = _all.Sum(v => v.SizeBytes);
        SummaryText = _all.Count == 0
            ? "尚未扫描"
            : $"共 {Videos.Count} / {_all.Count} 个视频 · 合计 {FormatSize(total)}"
              + (CheckedCount > 0 ? $" · 已勾选 {CheckedCount}" : "")
              + (Selected != null ? $" · 已选：{Selected.Name}" : "");
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AllCheckedState));   // v3.16.1：导入 / 清除 / 重扫后表头三态刷新
    }

    /// <summary>v3.21 §4.8.2：表头右键「🔎 筛选」的分类项 = **类型（扩展名）**。</summary>
    private void RebuildQuickFilters()
    {
        var items = new List<(string Label, string ColumnKey, string Value, string? Op)>();

        foreach (var ext in _all.Select(v => v.Extension)
                                .Where(e => e.Length > 0)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            items.Add(($"类型：{ext}", "Type", ext, null));

        SetQuickFilters(items);
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
        TypeHeader = "类型" + SortArrow("Type");
        SizeHeader = "大小" + SortArrow("Size");
        DurationHeader = "时长" + SortArrow("Duration");
        ResolutionHeader = "分辨率" + SortArrow("Resolution");
        CodecHeader = "编码" + SortArrow("Codec");
        BitrateHeader = "码率" + SortArrow("Bitrate");
        ModifiedHeader = "修改时间" + SortArrow("Modified");
        FpsHeader = "帧率" + SortArrow("Fps");
        ContainerHeader = "容器格式" + SortArrow("Container");
        AudioHeader = "音频编码" + SortArrow("Audio");
        PathHeader = "路径" + SortArrow("Path");
        CreatedHeader = "创建时间" + SortArrow("Created");
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

    // ==================== 多选 / 发送到任务页 ====================

}
