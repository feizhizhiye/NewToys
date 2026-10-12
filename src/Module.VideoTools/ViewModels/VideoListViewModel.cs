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
/// 视频列表页 —— 一个「比资源管理器显示更多信息的视频查看器」，用来快速判断哪些片子该剪辑 / 该转码。
/// <para>
/// 布局遵循规范 <b>3.6 列表页</b>（与「PC 工具箱 → 软件包管理」同款）：左侧可排序表头 + 列表、
/// 右侧选中项详情、列表挂右键菜单；表头最右侧还有一个无文字小按钮，点一下展开详情（并自动选中第一项）。
/// 列显隐 / 详情开合 / 排序 / 多项筛选全部继承自 <see cref="ListPageViewModelBase"/>（v1.35 收口）。
/// </para>
/// <para>
/// <b>目录每次手动选</b>（v1.34）：本页与设置彻底解绑 —— 不再有"视频库默认目录"，也不回写 <c>app.json</c>
/// （视频 / 音乐这类库常常分散在好几个文件夹，写死一个默认目录只会误导）。只在**本次程序运行内**记住上次选的目录。
/// </para>
/// <para>
/// <b>多选</b>（v1.35）：行首的勾选框用来挑一批文件，然后「➡ 发送到转码」/「➡ 发送到剪切」
/// —— 两页的任务清单由单例 <see cref="VideoTaskStore"/> 持有，切页不丢。
/// </para>
/// <para>
/// <b>媒体信息</b>（分辨率 / 编码 / 码率 / 时长）走 ffprobe：
/// ① 扫描目录时先用 SQLite 缓存（<c>data\newtoys.db</c>）把已知信息填上，零成本；
/// ② 没缓存的点「🔎 读取全部」并发批量探测（并发度取「设置 → 通用」里的最大并发数），读完写回缓存。
/// </para>
/// <para><b>没有「保存」按钮</b>：这里没有任何"设置项"，全是动作按钮（规范 3.5）。</para>
/// </summary>
public partial class VideoListViewModel : ListPageViewModelBase
{
    /// <summary>上次选的目录（**只在本次运行内**记住；不写配置，见类注释）。</summary>
    private static string _lastFolder = "";

    private readonly IAppConfigService? _cfg;
    private readonly VideoMediaIndex? _index;
    private readonly VideoTaskStore? _store;
    private readonly VideoCatalogService? _catalog;
    private readonly VideoFavoritesService? _fav;
    private readonly INavigationService? _nav;

    private readonly List<VideoItem> _all = new();

    private bool _syncing;

    /// <summary>扫描 / 探测取消用（重复点先取消上一次）。</summary>
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _probeCts;

    public ObservableCollection<VideoItem> Videos { get; } = new();

    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isBusy;
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
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
    // v3.15 扩充列（§6.4.8）
    [ObservableProperty] private string _tagsHeader = "标签";
    [ObservableProperty] private string _fpsHeader = "帧率";
    [ObservableProperty] private string _containerHeader = "容器格式";
    [ObservableProperty] private string _audioHeader = "音频编码";
    [ObservableProperty] private string _pathHeader = "路径";
    [ObservableProperty] private string _createdHeader = "创建时间";
    [ObservableProperty] private string _favoriteHeader = "已收藏";

    // 详情卡里两个**可编辑**字段（§6.4.6 标签 / §6.4.7 文件名）：提交时才动磁盘 / 数据库。
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editTags = "";

    // ==================== 列显隐（基类管状态；这里只给 XAML 提供具名属性） ====================

    public bool IsNameVisible => Vis("Name");
    public bool IsTypeVisible => Vis("Type");
    public bool IsSizeVisible => Vis("Size");
    public bool IsDurationVisible => Vis("Duration");
    public bool IsResolutionVisible => Vis("Resolution");
    public bool IsCodecVisible => Vis("Codec");
    public bool IsBitrateVisible => Vis("Bitrate");
    public bool IsModifiedVisible => Vis("Modified");
    public bool IsPickVisible => Vis("Pick");
    // v3.15 扩充列
    public bool IsTagsVisible => Vis("Tags");
    public bool IsFpsVisible => Vis("Fps");
    public bool IsContainerVisible => Vis("Container");
    public bool IsAudioVisible => Vis("Audio");
    public bool IsPathVisible => Vis("Path");
    public bool IsCreatedVisible => Vis("Created");
    public bool IsFavoriteVisible => Vis("Favorite");

    protected override string PageKey => "video-list";

    /// <summary>
    /// 列的元数据（顺序即表头与行的列顺序）。
    /// <para>第一列 <c>Pick</c> = 行首勾选框（多选，用来「发送到转码 / 剪切」）；
    /// 最后一列 <c>Detail</c> 是表头右侧那个无文字小按钮的固定窄列。</para>
    /// </summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Pick", "", "34", sortable: false),
        new("Name", "名称", "3.2*"),
        new("Type", "类型", "0.9*"),
        new("Size", "大小", "1*"),
        new("Duration", "时长", "0.9*"),
        new("Resolution", "分辨率", "1.1*"),
        new("Codec", "编码", "1.1*"),
        new("Bitrate", "码率", "1.1*"),
        new("Modified", "修改时间", "1.5*"),
        // v3.15 扩充（表头右键均可隐藏）：标签 / 帧率 / 容器格式 / 音频编码 / 路径 / 创建时间
        new("Tags", "标签", "1.2*"),
        new("Fps", "帧率", "0.8*"),
        new("Container", "容器格式", "1*"),
        new("Audio", "音频编码", "1*"),
        new("Path", "路径", "2.5*"),
        new("Created", "创建时间", "1.5*"),
        // 「已收藏」列：真值在 video_favorites 表（v3.15 §6.4.9）；基类 Sort 对它首点即降序 → ★ 排前面
        new("Favorite", "已收藏", "0.7*"),
    };

    public bool HasSelection => Selected != null;
    public bool HasItems => Videos.Count > 0;

    /// <summary>全局导入/导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.15 全局，覆盖 .db 与 .json 选择器）。</summary>
    public string ImportExportDefaultDir => _cfg?.VideoDbFolder ?? "";

    /// <summary>已勾选的文件数（工具条上的「发送到…」按钮据此启用 / 显示数量）。</summary>
    public int CheckedCount => _all.Count(v => v.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public VideoListViewModel() { }

    public VideoListViewModel(IAppConfigService cfg, IColumnLayoutService cols, VideoMediaIndex index,
                              VideoTaskStore store, VideoCatalogService catalog, INavigationService nav,
                              VideoFavoritesService? fav = null)
        : base(cols)
    {
        _cfg = cfg;
        _index = index;
        _store = store;
        _catalog = catalog;
        _nav = nav;
        _fav = fav;
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
            RefreshFavoriteFlags();     // v3.15：★ 标记（一次查库，不逐行查）
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

    partial void OnSelectedChanged(VideoItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        // 详情卡两个可编辑字段跟着选中项走：切换选中即丢弃**未提交**的改动（提交要显式点保存 / 回车 / 失焦）
        EditName = value?.Name ?? "";
        EditTags = value?.Tags ?? "";
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
        "Tags" => ((VideoItem)row).Tags,
        "Fps" => ((VideoItem)row).FpsDisplay,
        "Container" => ((VideoItem)row).ContainerDisplay,
        "Audio" => ((VideoItem)row).AudioCodecDisplay,
        "Path" => ((VideoItem)row).FullPath,
        "Created" => ((VideoItem)row).CreatedDisplay,
        "Favorite" => ((VideoItem)row).FavoriteDisplay,
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
            "Favorite" => v.IsFavorite ? 1 : 0,
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
            "Tags" => SortDescending ? q.OrderByDescending(v => v.Tags, byText) : q.OrderBy(v => v.Tags, byText),
            "Fps" => SortDescending ? q.OrderByDescending(v => v.Fps ?? -1) : q.OrderBy(v => v.Fps ?? -1),
            "Container" => SortDescending ? q.OrderByDescending(v => v.ContainerFormat, byText) : q.OrderBy(v => v.ContainerFormat, byText),
            "Audio" => SortDescending ? q.OrderByDescending(v => v.AudioCodec, byText) : q.OrderBy(v => v.AudioCodec, byText),
            "Path" => SortDescending ? q.OrderByDescending(v => v.FullPath, byText) : q.OrderBy(v => v.FullPath, byText),
            "Created" => SortDescending ? q.OrderByDescending(v => v.Created) : q.OrderBy(v => v.Created),
            // ★ 排前面（基类 Sort 对 "Favorite" 首点即降序）
            "Favorite" => SortDescending ? q.OrderByDescending(v => v.IsFavorite) : q.OrderBy(v => v.IsFavorite),
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
        TagsHeader = "标签" + SortArrow("Tags");
        FpsHeader = "帧率" + SortArrow("Fps");
        ContainerHeader = "容器格式" + SortArrow("Container");
        AudioHeader = "音频编码" + SortArrow("Audio");
        PathHeader = "路径" + SortArrow("Path");
        CreatedHeader = "创建时间" + SortArrow("Created");
        FavoriteHeader = "已收藏" + SortArrow("Favorite");
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB"
    };

    // ==================== 多选 / 发送到任务页 ====================

    /// <summary>勾选状态变了 → 刷新「已勾选几个」与统计行。</summary>
    public void OnItemCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AllCheckedState));   // v3.16.1：表头三态勾选框跟着走
        SummaryText = _all.Count == 0
            ? "尚未扫描"
            : $"共 {Videos.Count} / {_all.Count} 个视频 · 合计 {FormatSize(_all.Sum(v => v.SizeBytes))}"
              + (CheckedCount > 0 ? $" · 已勾选 {CheckedCount}" : "");
    }

    /// <summary>行首勾选框被点了一下（只刷新"已勾选几个"，不重排列表 —— 重排会把正在勾的那行挪走）。</summary>
    [RelayCommand]
    private void ItemChecked() => OnItemCheckedChanged();

    /// <summary>
    /// 表头勾选框的三态（v3.16.1）：null = 部分勾选（显示为实心方块）。
    /// ⚠️ 行勾选变化 / 列表刷新后要 `OnPropertyChanged(AllCheckedState)`，否则表头不跟着走。
    /// </summary>
    public bool? AllCheckedState
    {
        get
        {
            if (_all.Count == 0) return false;
            var c = CheckedCount;
            return c == 0 ? false : (c >= _all.Count ? true : null);
        }
    }

    /// <summary>点表头勾选框：已全勾 → 取消；否则 → 全选。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        if (CheckedCount >= _all.Count && _all.Count > 0) UncheckAll();
        else CheckAll();
    }

    /// <summary>☑ 全选（当前列表里显示的全部勾上）。</summary>
    [RelayCommand]
    private void CheckAll()
    {
        foreach (var v in Videos) v.IsChecked = true;
        OnItemCheckedChanged();
        StatusText = $"已勾选 {CheckedCount} 个文件。";
    }

    /// <summary>☑ 反选。</summary>
    [RelayCommand]
    private void InvertChecked()
    {
        foreach (var v in Videos) v.IsChecked = !v.IsChecked;
        OnItemCheckedChanged();
        StatusText = $"反选后已勾选 {CheckedCount} 个文件。";
    }

    /// <summary>☐ 取消全部勾选。</summary>
    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var v in _all) v.IsChecked = false;
        OnItemCheckedChanged();
        StatusText = "已取消全部勾选。";
    }

    /// <summary>➡ 批量处理·转码：把勾选的文件交给「批量处理」页批量生成转码任务（v3.8：原"发送到转码页"）。</summary>
    [RelayCommand]
    private void SendToTranscode()
    {
        var paths = _all.Where(v => v.IsChecked).Select(v => v.FullPath).ToList();
        if (paths.Count == 0) { StatusText = "先勾几个文件（行首的勾选框）。"; return; }

        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.EnqueueForTranscode(paths);
        StatusText = $"已把 {paths.Count} 个文件送去「批量处理」建转码任务……";
        _nav?.NavigateTo("video-batch");
    }

    /// <summary>✂ 视频预览·剪辑：把勾选的文件送进「视频预览」页的剪辑任务编辑器。</summary>
    [RelayCommand]
    private void SendToCut()
    {
        var paths = _all.Where(v => v.IsChecked).Select(v => v.FullPath).ToList();
        if (paths.Count == 0) { StatusText = "先勾几个文件（行首的勾选框）。"; return; }

        if (_store == null) { StatusText = "任务服务未就绪。"; return; }
        _store.EnqueueForCut(paths);
        StatusText = $"已把 {paths.Count} 个文件送去「视频预览」页……";
        _nav?.NavigateTo("video-cut");
    }

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

    // ==================== 视频数据库（catalog，需求 §6.4.3） ====================

    /// <summary>💾 加入视频库：把**当前列表（整个扫描结果 _all）**写进 SQLite 视频库（video_catalog 表）。
    /// ⛔ 只写列表已有信息 —— 没读取媒体信息的行对应字段留空（NULL），不强行跑 ffprobe。</summary>
    [RelayCommand]
    private void AddToCatalog()
    {
        if (_catalog == null) { StatusText = "视频库服务未就绪。"; return; }
        if (!_catalog.IsAvailable) { StatusText = $"视频库不可用（数据库未就绪）：{_catalog.DatabasePath}"; return; }
        if (_all.Count == 0) { StatusText = "先扫描一个目录，列表里有东西才能入库。"; return; }

        var n = 0;
        var withMedia = 0;
        foreach (var v in _all)
        {
            _catalog.Upsert(v);
            n++;
            if (v.HasMedia) withMedia++;
        }
        StatusText = $"已写入视频库：{n} 个文件（其中 {withMedia} 个带媒体信息，未读的字段留空待补）；库文件 = {_catalog.DatabasePath}";
    }

    /// <summary>📑 从外部 .db 读回视频库（需求 §6.4.3「读取文件夹功能扩展支持读取数据库文件」）。
    /// 由 VideoListView 的文件选择器挑完 .db 后调用，直接把记录灌进当前列表。</summary>
    public void LoadCatalog(string dbPath)
    {
        if (_catalog == null) { StatusText = "视频库服务未就绪。"; return; }

        // v3.16：读不出来必须把**原因**说出来（以前是静默返回空列表，表现为"点了没反应"）。
        var r = _catalog.ReadFile(dbPath);
        if (r.Error != null) { StatusText = "导入视频库失败 —— " + r.Error; return; }
        if (r.IsEmptyLibrary)
        {
            StatusText = $"「{Path.GetFileName(dbPath)}」里的 video_catalog 表是空的（0 条记录）—— 先在列表里点「💾 加入视频库」再导入。";
            return;
        }

        // ⛔ 不覆盖 Folder：读库只是临时替换列表，Folder 仍是真实目录，便于回头点「🔍 扫描」重扫
        var items = r.Items;
        _all.Clear();
        _all.AddRange(items);
        foreach (var v in _all) v.IsChecked = false;   // 新来源，勾选状态重置
        Selected = null;                               // 旧的选中项已不在新列表里，别让详情卡留在消失的行上
        RefreshFavoriteFlags();
        ApplyView();

        StatusText = $"已从视频库读入 {items.Count} 个记录（来自 {dbPath}）：文件可能已移动，选中后可点「🔎 读取媒体信息」刷新。";
    }

    /// <summary>🧹 清除列表（≈ 浏览器无缓存刷新）：只清界面上的列表，⛔ 不动磁盘文件、不动视频库、不重扫目录。</summary>
    [RelayCommand]
    private async Task ClearListAsync()
    {
        var n = _all.Count;
        if (n == 0 && Videos.Count == 0) { StatusText = "列表已经是空的。"; return; }

        var msg = $"清除当前列表里的 {n} 个视频条目？" + Environment.NewLine
                + "只清界面上的列表 —— 不会删除任何视频文件，也不会动视频库；清完可以重新导入 / 扫描。";
        if (!await NewToys.Core.Services.DialogHooks.ConfirmAsync(msg)) { StatusText = "已取消清除。"; return; }

        _scanCts?.Cancel();
        ClearFiltersCommand.Execute(null);   // 筛选条件一并归零（否则清完看着像"还有条件挡着"）
        SearchText = "";
        _all.Clear();
        Videos.Clear();
        Selected = null;
        ApplyView();
        StatusText = $"已清除列表（{n} 个条目）；视频文件与视频库都没动 —— 点「📂 导入」或「🔍 扫描」重新载入。";
    }

    /// <summary>📂 导入 → 视频文件…：把所选的一个 / 多个视频文件**追加**进当前列表（不扫描整目录、媒体信息留空待按需读）。
    /// 与「文件夹…」（替换整列）不同，这里是累加；同路径去重，避免重复条目。</summary>
    public void ImportVideoFiles(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(_all.Select(v => v.FullPath), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (!VideoScanner.Extensions.Contains(ext)) continue;   // 只收认得的视频，挡掉误选
            if (existing.Contains(p)) continue;
            existing.Add(p);
            _all.Add(new VideoItem
            {
                Name = Path.GetFileName(p),
                FullPath = p,
                Extension = ext,
                SizeBytes = File.Exists(p) ? new FileInfo(p).Length : 0,
                Modified = File.Exists(p) ? File.GetLastWriteTime(p) : default
            });
            added++;
        }

        if (added == 0)
        {
            StatusText = "没有可导入的视频文件（仅支持 " + string.Join(" / ", VideoScanner.Extensions) + "）。";
            return;
        }

        RefreshFavoriteFlags();
        ApplyView();
        StatusText = $"已导入 {added} 个视频文件（追加到当前列表；媒体信息留空，选中后可点「🔎 读取信息」）。";
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

    // ==================== v3.15：详情卡内改名 / 标签 / 收藏 ====================

    /// <summary>把 <c>video_favorites</c> 里的路径一次性映射回列表行的 <c>IsFavorite</c>（⛔ 别每行查一次库）。</summary>
    private void RefreshFavoriteFlags()
    {
        if (_fav == null || !_fav.IsAvailable) return;
        var set = new HashSet<string>(_fav.All().Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var v in _all) v.IsFavorite = set.Contains(v.FullPath);
    }

    /// <summary>
    /// ✏ 详情卡内直接改名（需求 §6.4.7）：提交 → <see cref="VideoFileService.RenameToAsync"/>
    /// （复用现有**防穿越 / 同名**检查）→ 更新内存里的 <see cref="VideoItem"/>、同步视频库与收藏的 path。
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

        var oldPath = v.FullPath;
        var (ok, msg, newPath) = await VideoFileService.RenameToAsync(oldPath, newName);
        StatusText = msg;
        if (!ok || newPath == null) return;

        v.Name = Path.GetFileName(newPath);
        v.FullPath = newPath;
        if (_catalog != null && _catalog.HasPath(oldPath)) _catalog.MovePath(oldPath, newPath, v.Name);
        _fav?.MovePath(oldPath, newPath);          // 收藏按路径记，改名后旧路径就失效了

        // Name / FullPath / Tags 都是普通属性（不是 [ObservableProperty]），改了不会自动通知：
        // 主动发一次 Selected 变更，让详情卡的绑定重拉一遍。
        OnPropertyChanged(nameof(Selected));
        ApplyView();                                // 列表重渲染（同一批对象引用 → 选中、勾选都不丢）
    }

    /// <summary>
    /// 🏷 详情卡内改标签（需求 §6.4.6）：提交 → <see cref="VideoCatalogService.UpdateTags"/>（落 <c>video_catalog.tags</c>）。
    /// 标签随视频库走 —— **没入视频库的项**提示先点「💾 加入视频库」，不静默丢掉。
    /// </summary>
    [RelayCommand]
    private void SaveTags()
    {
        var v = Selected;
        if (v == null) return;

        var tags = (EditTags ?? "").Trim();
        if (string.Equals(tags, v.Tags ?? "", StringComparison.Ordinal)) { StatusText = "标签没变。"; return; }

        if (_catalog == null || !_catalog.HasPath(v.FullPath))
        {
            StatusText = $"「{v.Name}」还没进视频库 —— 先点工具条的「💾 加入视频库」，标签才能存下来。";
            return;
        }

        v.Tags = tags;
        _catalog.UpdateTags(v.FullPath, tags);
        OnPropertyChanged(nameof(Selected));
        ApplyView();
        StatusText = "已更新标签：" + (tags.Length > 0 ? tags : "（已清空）");
    }

    /// <summary>
    /// ★ 收藏（需求 §6.4.9）：把**勾选的**加入 <c>video_favorites</c>（一个都没勾就用当前选中的那个）。
    /// 收藏必须来自视频库 —— 不在 <c>video_catalog</c> 里的拒绝并提示先入库。
    /// </summary>
    [RelayCommand]
    private void AddToFavorites()
    {
        var targets = _all.Where(v => v.IsChecked).ToList();
        if (targets.Count == 0 && Selected != null) targets.Add(Selected);
        if (targets.Count == 0) { StatusText = "先勾几个文件（行首的勾选框），或选中一个。"; return; }
        if (_fav == null) { StatusText = "收藏服务未就绪。"; return; }
        if (!_fav.IsAvailable) { StatusText = "收藏不可用（数据库未就绪）。"; return; }

        var added = 0;
        var skipped = 0;
        foreach (var v in targets)
        {
            if (_catalog != null && !_catalog.HasPath(v.FullPath)) { skipped++; continue; }
            if (_fav.Add(v.FullPath)) { v.IsFavorite = true; added++; }
        }

        ApplyView();
        StatusText = (added > 0 ? $"已收藏 {added} 个视频（缩略图在「视频预览」页点「📸 取当前帧」生成）。" : "")
                     + (skipped > 0 ? $" {skipped} 个还没进视频库 —— 先点「💾 加入视频库」。" : "");
    }

    // ==================== v3.19 / NT-011：标签管理（弹层卡） ====================

    /// <summary>标签管理弹层是否打开。</summary>
    [ObservableProperty] private bool _isTagManagerOpen;

    /// <summary>「新建标签」输入框。</summary>
    [ObservableProperty] private string _newTagInput = "";

    /// <summary>标签管理的行（名称 + 使用数）。</summary>
    public ObservableCollection<TagManagerItem> TagManagerItems { get; } = new();

    [RelayCommand]
    private void OpenTagManager()
    {
        RefreshTagManager();
        IsTagManagerOpen = true;
    }

    [RelayCommand]
    private void CloseTagManager() => IsTagManagerOpen = false;

    [RelayCommand]
    private void RefreshTagManager()
    {
        TagManagerItems.Clear();
        if (_catalog == null) return;
        foreach (var (tag, count) in _catalog.GetAllTags())
            TagManagerItems.Add(new TagManagerItem { Name = tag, Count = count });
    }

    [RelayCommand]
    private async Task RenameTagAsync(TagManagerItem? item)
    {
        if (item == null || _catalog == null) return;
        var nn = await DialogHooks.InputAsync("重命名标签", item.Name);
        if (string.IsNullOrWhiteSpace(nn)) return;
        var n = _catalog.RenameTag(item.Name, nn.Trim());
        StatusText = n > 0 ? $"已把标签「{item.Name}」改为「{nn.Trim()}」（{n} 个视频）" : $"没有改动（没找到「{item.Name}」）。";
        RefreshTagManager();
    }

    [RelayCommand]
    private async Task DeleteTagAsync(TagManagerItem? item)
    {
        if (item == null || _catalog == null) return;
        var ok = await DialogHooks.ConfirmAsync($"删除标签「{item.Name}」？\n会从 {item.Count} 个视频上移除该标签（不影响视频文件）。");
        if (!ok) return;
        var n = _catalog.DeleteTag(item.Name);
        StatusText = $"已删除标签「{item.Name}」（{n} 个视频）。";
        RefreshTagManager();
    }

    [RelayCommand]
    private void CreateTag()
    {
        var name = (NewTagInput ?? "").Trim();
        if (name.Length == 0) { StatusText = "先输入标签名。"; return; }
        if (TagManagerItems.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        { StatusText = $"标签「{name}」已存在。"; return; }
        TagManagerItems.Insert(0, new TagManagerItem { Name = name, Count = 0 });
        NewTagInput = "";
        StatusText = $"已新建标签「{name}」（还没用到任何视频；在视频列表里给它打上后才会存进库）。";
    }

    /// <summary>标签管理弹层的一行。</summary>
    public sealed class TagManagerItem
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public string CountDisplay => Count > 0 ? $"{Count} 个视频" : "尚未使用";
    }
}
