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
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.VideoTools.Models;
using NewToys.VideoTools.Services;

namespace NewToys.VideoTools.ViewModels;

/// <summary>
/// 视频库页。
/// <para>
/// 布局与「PC 工具箱 → 软件包管理」<b>完全同款</b>（规范 <b>3.8 列表页规范</b>）：
/// 左侧可排序表头 + 列表、右侧选中项详情、列表挂右键菜单。
/// 这两页是全工程的"列表页样板"——以后再加同类页（书签、Ping 结果、抓取结果…）照抄即可。
/// </para>
/// <para>
/// <b>无「保存」按钮</b>：目录这一项由「🔍 扫描」这个动作顺带落盘（写 <c>app.json</c> 的
/// <c>DefaultVideoFolder</c>），符合规范 3.7 的"动作按钮"分界。
/// </para>
/// </summary>
public partial class VideoListViewModel : ObservableObject
{
    private readonly IAppConfigService? _cfg;

    private readonly List<VideoItem> _all = new();

    private string _sortColumn = "Name";
    private bool _sortDescending;
    private bool _syncing;

    /// <summary>扫描取消用（重复点扫描时先取消上一次）。</summary>
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<VideoItem> Videos { get; } = new();

    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _summaryText = "尚未扫描";
    [ObservableProperty] private VideoItem? _selected;

    // 表头（点一下切换排序）
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _typeHeader = "类型";
    [ObservableProperty] private string _sizeHeader = "大小";
    [ObservableProperty] private string _modifiedHeader = "修改时间";
    [ObservableProperty] private string _durationHeader = "时长";

    // ==================== 列显隐（右键表头增删列，规范 3.8） ====================
    private const string PageKey = "video-list";
    private readonly IColumnLayoutService? _cols;

    [ObservableProperty] private bool _isNameVisible = true;
    [ObservableProperty] private bool _isTypeVisible = true;
    [ObservableProperty] private bool _isSizeVisible = true;
    [ObservableProperty] private bool _isModifiedVisible = true;
    [ObservableProperty] private bool _isDurationVisible = true;

    /// <summary>列的元数据（键 / 标题 / 宽度 / 是否可排序）；顺序即表头与行的列顺序。</summary>
    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "3*"),
        new("Type", "类型", "1*"),
        new("Size", "大小", "1.2*"),
        new("Modified", "修改时间", "1.8*"),
        new("Duration", "时长", "1.2*"),
    };

    /// <summary>列宽规格串（可绑定）：可见列写星号宽度，隐藏列写 "0"（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串而不是 <c>ColumnDefinitions</c>：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，
    /// Avalonia XAML 不允许在它上面写 {Binding}（编译期 AVLN3000），故由 <c>ColumnGrid</c> 控件解析（规范 3.8.5）。</summary>
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
        "Modified" => IsModifiedVisible,
        "Duration" => IsDurationVisible,
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
            case "Modified": IsModifiedVisible = !IsModifiedVisible; break;
            case "Duration": IsDurationVisible = !IsDurationVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>把当前列显隐偏好落盘（config\columnlayout.json，会被数据管理一起备份）。</summary>
    private void SaveColumnVisibility()
    {
        if (_cols == null) return;
        _cols.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Name"] = IsNameVisible, ["Type"] = IsTypeVisible, ["Size"] = IsSizeVisible,
            ["Modified"] = IsModifiedVisible, ["Duration"] = IsDurationVisible,
        });
    }

    /// <summary>进入页面时读回上次保存的列显隐偏好（未保存过的列保持默认「可见」）。</summary>
    private void LoadColumnVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(PageKey);
        if (saved.TryGetValue("Name", out var n)) IsNameVisible = n;
        if (saved.TryGetValue("Type", out var t)) IsTypeVisible = t;
        if (saved.TryGetValue("Size", out var s)) IsSizeVisible = s;
        if (saved.TryGetValue("Modified", out var m)) IsModifiedVisible = m;
        if (saved.TryGetValue("Duration", out var d)) IsDurationVisible = d;
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Videos.Count > 0;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public VideoListViewModel() { }

    public VideoListViewModel(IAppConfigService cfg, IColumnLayoutService cols)
    {
        _cfg = cfg;
        _cols = cols;
        LoadColumnVisibility();
        Folder = cfg.DefaultVideoFolder;
        if (string.IsNullOrWhiteSpace(Folder))
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NewToys");
        UpdateHeaders();

        // 目录不存在时不扫描：直接如实提示（默认目录在全新机器上本来就不存在，报错更有用）
        if (!Directory.Exists(Folder))
        {
            StatusText = $"目录还不存在：{Folder} —— 点「📂 浏览…」选一个视频目录，或先在「设置 → 路径配置」里填。";
            SummaryText = "尚未扫描";
        }
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

        // 目录属于"下次进来还要用"的设置 → 扫描时顺手落盘（app.json）
        if (_cfg != null) _cfg.DefaultVideoFolder = dir;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            IsBusy = true;
            StatusText = $"正在扫描 {dir} ……";

            var items = await Task.Run(() => VideoScanner.Scan(dir, Recursive, ct), ct);
            ct.ThrowIfCancellationRequested();

            _all.Clear();
            _all.AddRange(items);
            ApplyView();
            StatusText = $"扫描完成：{items.Count} 个视频（{DateTime.Now:HH:mm:ss}）"
                         + (Recursive ? " · 含子目录" : " · 仅当前目录");
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
        if (_syncing) return;
        _ = ScanAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    partial void OnSelectedChanged(VideoItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
    }

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
        ModifiedHeader = "修改时间" + (_sortColumn == "Modified" ? arrow : "");
        DurationHeader = "时长" + (_sortColumn == "Duration" ? arrow : "");
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

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

    /// <summary>🔎 用 ffprobe 读一次时长 / 分辨率 / 编码（扫目录时**不**逐个跑，太慢）。</summary>
    [RelayCommand]
    private async Task LoadMediaInfoAsync()
    {
        var v = Selected;
        if (v == null) return;

        var probe = VideoScanner.DeriveFfprobe(_cfg?.FfmpegPath);
        StatusText = $"正在用 ffprobe 读取 {v.Name} ……";
        var (ok, msg) = await VideoScanner.ProbeAsync(v, probe);
        StatusText = msg;

        if (ok)
        {
            // 行文本（时长/状态）不是 ObservableObject 的属性 → 重建列表让它刷新
            ApplyView();
        }
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
}
