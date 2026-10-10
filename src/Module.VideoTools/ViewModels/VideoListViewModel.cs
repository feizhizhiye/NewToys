using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 视频库页 —— 一个「比资源管理器显示更多信息的视频查看器」，用来快速判断哪些片子该剪辑 / 该转码。
/// <para>
/// 布局遵循规范 <b>3.6 列表页</b>（与「PC 工具箱 → 软件包管理」同款）：左侧可排序表头 + 列表、
/// 右侧选中项详情、列表挂右键菜单；表头最右侧还有一个无文字小按钮，点一下展开详情（并自动选中第一项）。
/// </para>
/// <para>
/// <b>目录每次手动选</b>（v1.34）：本页与设置彻底解绑 —— 不再有"视频库默认目录"，也不回写 <c>app.json</c>
/// （视频 / 音乐这类库常常分散在好几个文件夹，写死一个默认目录只会误导）。只在**本次程序运行内**记住上次选的目录。
/// </para>
/// <para>
/// <b>媒体信息</b>（分辨率 / 编码 / 码率 / 时长）走 ffprobe：
/// ① 扫描目录时先用 SQLite 缓存（<c>data\newtoys.db</c>）把已知信息填上，零成本；
/// ② 没缓存的点「🔎 读取全部」并发批量探测（并发度取「设置 → 通用」里的最大并发数），读完写回缓存。
/// </para>
/// <para><b>没有「保存」按钮</b>：这里没有任何"设置项"，全是动作按钮（规范 3.5）。</para>
/// </summary>
public partial class VideoListViewModel : ObservableObject
{
    /// <summary>上次选的目录（**只在本次运行内**记住；不写配置，见类注释）。</summary>
    private static string _lastFolder = "";

    private readonly IAppConfigService? _cfg;
    private readonly IColumnLayoutService? _cols;
    private readonly VideoMediaIndex? _index;

    private readonly List<VideoItem> _all = new();

    private string _sortColumn = "Name";
    private bool _sortDescending;
    private bool _syncing;

    /// <summary>扫描 / 探测取消用（重复点先取消上一次）。</summary>
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _probeCts;

    public ObservableCollection<VideoItem> Videos { get; } = new();

    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _summaryText = "尚未扫描";
    [ObservableProperty] private VideoItem? _selected;

    /// <summary>供行内按钮绑定（<c>{Binding !IsBusy}</c> 只对同层路径可靠，跨 ListBox 取父级 VM 得用这个）。</summary>
    public bool IsNotBusy => !IsBusy;

    // 表头（点一下切换排序）
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _durationHeader = "时长";
    [ObservableProperty] private string _resolutionHeader = "分辨率";
    [ObservableProperty] private string _codecHeader = "编码";
    [ObservableProperty] private string _bitrateHeader = "码率";
    [ObservableProperty] private string _modifiedHeader = "修改时间";

    // ==================== 列显隐（右键表头增删列，规范 3.6） ====================
    private const string PageKey = "video-list";

    [ObservableProperty] private bool _isNameVisible = true;
    [ObservableProperty] private bool _isTypeVisible = true;
    [ObservableProperty] private bool _isSizeVisible = true;
    [ObservableProperty] private bool _isDurationVisible = true;
    [ObservableProperty] private bool _isResolutionVisible = true;
    [ObservableProperty] private bool _isCodecVisible = true;
    [ObservableProperty] private bool _isBitrateVisible = true;
    [ObservableProperty] private bool _isModifiedVisible = true;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。
    /// <para>最后一列 <c>Detail</c> 是**表头右侧那个无文字小按钮**用的固定窄列（规范 3.6），不进列显隐菜单。</para></summary>
    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "3.2*"),
        new("Type", "类型", "0.9*"),
        new("Size", "大小", "1*"),
        new("Duration", "时长", "0.9*"),
        new("Resolution", "分辨率", "1.1*"),
        new("Codec", "编码", "1.1*"),
        new("Bitrate", "码率", "1.1*"),
        new("Modified", "修改时间", "1.5*"),
        new("Detail", "", "36", sortable: false),
    };

    /// <summary>列宽规格串（可绑定）：可见列写星号宽度，隐藏列写 "0"（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串而不是 <c>ColumnDefinitions</c>：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，
    /// Avalonia XAML 不允许在它上面写 {Binding}（编译期 AVLN3000），故由 <c>ColumnGrid</c> 控件解析（规范 3.6）。</summary>
    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                parts[i] = IsVisibleFor(Columns[i].Key) ? Columns[i].Width : "0";
            return string.Join(",", parts);
        }
    }

    private bool IsVisibleFor(string key) => key switch
    {
        "Name" => IsNameVisible,
        "Type" => IsTypeVisible,
        "Size" => IsSizeVisible,
        "Duration" => IsDurationVisible,
        "Resolution" => IsResolutionVisible,
        "Codec" => IsCodecVisible,
        "Bitrate" => IsBitrateVisible,
        "Modified" => IsModifiedVisible,
        _ => true,
    };

    /// <summary>右键表头勾选 / 取消勾选某列（增删表头项）。</summary>
    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        switch (key)
        {
            case "Name": IsNameVisible = !IsNameVisible; break;
            case "Type": IsTypeVisible = !IsTypeVisible; break;
            case "Size": IsSizeVisible = !IsSizeVisible; break;
            case "Duration": IsDurationVisible = !IsDurationVisible; break;
            case "Resolution": IsResolutionVisible = !IsResolutionVisible; break;
            case "Codec": IsCodecVisible = !IsCodecVisible; break;
            case "Bitrate": IsBitrateVisible = !IsBitrateVisible; break;
            case "Modified": IsModifiedVisible = !IsModifiedVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    private void SaveColumnVisibility()
    {
        if (_cols == null) return;
        _cols.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Name"] = IsNameVisible, ["Type"] = IsTypeVisible, ["Size"] = IsSizeVisible,
            ["Duration"] = IsDurationVisible, ["Resolution"] = IsResolutionVisible,
            ["Codec"] = IsCodecVisible, ["Bitrate"] = IsBitrateVisible, ["Modified"] = IsModifiedVisible,
        });
    }

    private void LoadColumnVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(PageKey);
        if (saved.TryGetValue("Name", out var n)) IsNameVisible = n;
        if (saved.TryGetValue("Type", out var t)) IsTypeVisible = t;
        if (saved.TryGetValue("Size", out var s)) IsSizeVisible = s;
        if (saved.TryGetValue("Duration", out var d)) IsDurationVisible = d;
        if (saved.TryGetValue("Resolution", out var r)) IsResolutionVisible = r;
        if (saved.TryGetValue("Codec", out var c)) IsCodecVisible = c;
        if (saved.TryGetValue("Bitrate", out var b)) IsBitrateVisible = b;
        if (saved.TryGetValue("Modified", out var m)) IsModifiedVisible = m;
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Videos.Count > 0;

    // ==================== 右栏详情开合 ====================

    /// <summary>是否显示右侧详情栏。<b>默认收起</b>；点选结果落 <c>config\columnlayout.json</c>，下次进本页保持。</summary>
    [ObservableProperty] private bool _isDetailVisible;

    /// <summary>表头右侧小按钮：展开 / 收起详情；展开且没选中时自动选中第一项（规范 3.6）。</summary>
    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible && Selected == null && Videos.Count > 0) Selected = Videos[0];
        SaveDetailFlag();
    }

    /// <summary>右键「显示详情栏」：确保展开并定位到当前选中项（没选中就选第一项）。</summary>
    [RelayCommand]
    private void ShowDetail()
    {
        if (Selected == null && Videos.Count > 0) Selected = Videos[0];
        IsDetailVisible = true;
        SaveDetailFlag();
    }

    private void SaveDetailFlag()
    {
        _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);
    }

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public VideoListViewModel() { }

    public VideoListViewModel(IAppConfigService cfg, IColumnLayoutService cols, VideoMediaIndex index)
    {
        _cfg = cfg;
        _cols = cols;
        _index = index;
        LoadColumnVisibility();
        IsDetailVisible = cols.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel);   // 默认收起
        Folder = _lastFolder;                                                            // 本次运行内记住上次选的目录
        UpdateHeaders();

        // 目录**不来自设置**（v1.34 解绑）：没选过就如实提示，由用户点「📂 浏览…」选
        if (string.IsNullOrWhiteSpace(Folder))
            StatusText = "点「📂 浏览…」选一个视频目录（每次都手动选，不与设置绑定）；扫描后可从表头看到分辨率 / 编码 / 码率 / 时长。";
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

            _all.Clear();
            _all.AddRange(items);
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

    partial void OnSelectedChanged(VideoItem? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase))
            _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }
        ApplyView();
    }

    private void ApplyView()
    {
        IEnumerable<VideoItem> q = _all;

        var text = (SearchText ?? "").Trim();
        if (text.Length > 0)
            q = q.Where(v => v.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || v.Extension.Contains(text, StringComparison.OrdinalIgnoreCase));

        var byText = StringComparer.CurrentCultureIgnoreCase;
        q = _sortColumn switch
        {
            "Type" => _sortDescending ? q.OrderByDescending(v => v.Extension, byText) : q.OrderBy(v => v.Extension, byText),
            "Size" => _sortDescending ? q.OrderByDescending(v => v.SizeBytes) : q.OrderBy(v => v.SizeBytes),
            "Modified" => _sortDescending ? q.OrderByDescending(v => v.Modified) : q.OrderBy(v => v.Modified),
            // 未读取媒体信息的排最后（-1），而不是当成 0 秒排最前
            "Duration" => _sortDescending ? q.OrderByDescending(v => v.Duration ?? -1) : q.OrderBy(v => v.Duration ?? -1),
            "Resolution" => _sortDescending ? q.OrderByDescending(v => (v.Width ?? 0) * (long)(v.Height ?? 0))
                                            : q.OrderBy(v => (v.Width ?? 0) * (long)(v.Height ?? 0)),
            "Codec" => _sortDescending ? q.OrderByDescending(v => v.Codec, byText) : q.OrderBy(v => v.Codec, byText),
            "Bitrate" => _sortDescending ? q.OrderByDescending(v => v.BitRate ?? -1) : q.OrderBy(v => v.BitRate ?? -1),
            _ => _sortDescending ? q.OrderByDescending(v => v.Name, byText) : q.OrderBy(v => v.Name, byText)
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
              + (Selected != null ? $" · 已选：{Selected.Name}" : "");
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void UpdateHeaders()
    {
        var arrow = _sortDescending ? " ▼" : " ▲";
        NameHeader = "名称" + (_sortColumn == "Name" ? arrow : "");
        TypeHeader = "类型" + (_sortColumn == "Type" ? arrow : "");
        SizeHeader = "大小" + (_sortColumn == "Size" ? arrow : "");
        DurationHeader = "时长" + (_sortColumn == "Duration" ? arrow : "");
        ResolutionHeader = "分辨率" + (_sortColumn == "Resolution" ? arrow : "");
        CodecHeader = "编码" + (_sortColumn == "Codec" ? arrow : "");
        BitrateHeader = "码率" + (_sortColumn == "Bitrate" ? arrow : "");
        ModifiedHeader = "修改时间" + (_sortColumn == "Modified" ? arrow : "");
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

    // ==================== 媒体信息（ffprobe + 多线程） ====================

    /// <summary>读当前选中项的媒体信息（跑一次 ffprobe 并写回缓存）。</summary>
    [RelayCommand]
    private async Task LoadMediaInfoAsync()
    {
        var v = Selected;
        if (v == null) return;

        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        StatusText = $"正在用 ffprobe 读取 {v.Name} ……";
        var (ok, msg, info) = await VideoScanner.ProbeAsync(v.FullPath, probe);
        if (ok && info != null)
        {
            v.ApplyMedia(info);
            _index?.Upsert(v);
            StatusText = $"{v.Name}：{v.MediaStatus}";
        }
        else
        {
            v.MediaStatus = "读取失败：" + msg;
            StatusText = msg;
        }
    }

    /// <summary>
    /// 并发读取列表里**所有还没有媒体信息**的视频（已缓存的跳过，不重复跑）。
    /// 并发度取「设置 → 通用」里的最大并发数（<c>app.json</c> 的 <c>MaxConcurrency</c>）。
    /// </summary>
    [RelayCommand]
    private async Task LoadAllMediaInfoAsync()
    {
        if (IsBusy) return;

        var targets = _all.Where(v => !v.HasMedia).ToList();
        if (targets.Count == 0) { StatusText = "列表里的视频都已有媒体信息。"; return; }

        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        var concurrency = Math.Clamp(_cfg?.MaxConcurrency ?? 2, 1, 16);

        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var ct = _probeCts.Token;

        IsBusy = true;
        var done = 0;
        StatusText = $"正在读取媒体信息（并发 {concurrency}）… 0/{targets.Count}";
        try
        {
            using var gate = new SemaphoreSlim(concurrency);
            var tasks = targets.Select(async v =>
            {
                try
                {
                    await gate.WaitAsync(ct).ConfigureAwait(true);
                    try
                    {
                        var (ok, msg, info) = await VideoScanner.ProbeAsync(v.FullPath, probe, ct);
                        if (ok && info != null)
                        {
                            v.ApplyMedia(info);
                            _index?.Upsert(v);
                        }
                        else v.MediaStatus = "读取失败：" + msg;
                    }
                    finally { gate.Release(); }

                    done++;
                    StatusText = $"正在读取媒体信息（并发 {concurrency}）… {done}/{targets.Count}";
                }
                catch (OperationCanceledException) { /* 取消：静默收尾 */ }
            }).ToList();

            await Task.WhenAll(tasks);

            ApplyView();   // 时长 / 大小等排序可能受影响，排序一次到位
            var failed = targets.Count(t => !t.HasMedia);
            StatusText = ct.IsCancellationRequested
                ? $"已取消：已读取 {done - failed}/{targets.Count} 个。"
                : $"媒体信息读取完成：{targets.Count - failed}/{targets.Count} 个成功"
                  + (failed > 0 ? $"（{failed} 个失败，可单独选中后点「🔎 读取信息」看原因）" : "");
        }
        catch (Exception ex) { StatusText = "读取媒体信息失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>清空 SQLite 里的媒体信息缓存（下次扫描会重新探测）。</summary>
    [RelayCommand]
    private async Task ClearMediaCacheAsync()
    {
        if (_index == null) { StatusText = "缓存服务未就绪。"; return; }

        var ok = await DialogHooks.ConfirmAsync(
            "确定清空媒体信息缓存吗？\n下次扫描这些视频时要重新跑 ffprobe（会比较慢），不影响视频文件本身。");
        if (!ok) { StatusText = "已取消。"; return; }

        StatusText = _index.Clear()
            ? $"已清空媒体信息缓存（{_index.DatabasePath}）。"
            : "清空缓存失败：" + (_index.IsAvailable ? "写入被拒绝" : "数据库不可用");
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
        if (v == null) return;

        var (ok, msg) = await VideoFileService.DeleteAsync(v.FullPath);
        StatusText = msg;
        if (ok)
        {
            _all.Remove(v);
            Selected = null;
            ApplyView();
        }
    }
}
