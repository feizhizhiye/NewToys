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
public partial class VideoListViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    /// <summary>上次选的目录（**只在本次运行内**记住；不写配置，见类注释）。</summary>
    private static string _lastFolder = "";

    private readonly IAppConfigService? _cfg;
    private readonly VideoMediaIndex? _index;
    private readonly VideoTaskStore? _store;
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
    [ObservableProperty] private string _fpsHeader = "帧率";
    [ObservableProperty] private string _containerHeader = "容器格式";
    [ObservableProperty] private string _audioHeader = "音频编码";
    [ObservableProperty] private string _pathHeader = "路径";
    [ObservableProperty] private string _createdHeader = "创建时间";

    // 详情卡里的**可编辑**字段（§6.4.7 文件名）：提交时才动磁盘。
    [ObservableProperty] private string _editName = "";

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
    public bool IsFpsVisible => Vis("Fps");
    public bool IsContainerVisible => Vis("Container");
    public bool IsAudioVisible => Vis("Audio");
    public bool IsPathVisible => Vis("Path");
    public bool IsCreatedVisible => Vis("Created");

    protected override string PageKey => "video-list";

    /// <summary>
    /// 列的元数据（顺序即表头与行的列顺序）。
    /// <para>第一列 <c>Pick</c> = 行首勾选框（多选，用来「发送到转码 / 剪切」）；
    /// 最后一列 <c>Detail</c> 是表头右侧那个无文字小按钮的固定窄列。</para>
    /// </summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.22 §4.8.1：每列给一条**示例值** —— ① 列表为空时拼成一条水印示例行；
        // ② 用「示例宽 ≤ 表头宽」判定该列是否锁成表头宽（锁了就不可拖，且不低于 MinLockedWidth=34 保证把手可拖）。
        new("Pick", "", "34", sortable: false),
        new("Name", "名称", "3.2*", sample: "示例视频 2026.mp4"),
        new("Type", "类型", "0.9*", sample: ".mp4"),
        new("Size", "大小", "1*", sample: "1.5 GB"),
        new("Duration", "时长", "0.9*", sample: "1:02:03"),
        new("Resolution", "分辨率", "1.1*", sample: "1920×1080"),
        new("Codec", "编码", "1.1*", sample: "H.265/HEVC"),
        new("Bitrate", "码率", "1.1*", sample: "8.5 Mbps"),
        new("Modified", "修改时间", "1.5*", sample: "2026-10-13 15:00"),
        // v3.15 扩充（表头右键均可隐藏）：帧率 / 容器格式 / 音频编码 / 路径 / 创建时间
        new("Fps", "帧率", "0.8*", sample: "29.97"),
        new("Container", "容器格式", "1*", sample: "MP4"),
        new("Audio", "音频编码", "1*", sample: "AAC"),
        new("Path", "路径", "2.5*", sample: @"D:\Videos\示例视频 2026.mp4"),
        new("Created", "创建时间", "1.5*", sample: "2026-10-13 15:00"),
    };

    /// <summary>v3.22：列表为空时显示那条**示例行**（⛔ 不进 <c>Videos</c>、不参与统计 / 导入导出）。</summary>
    public bool ShowSampleRow => HasSampleRow && Videos.Count == 0;

    public bool HasSelection => Selected != null;
    public bool HasItems => Videos.Count > 0;

    // ==================== v3.32 §4.8.3：详情卡示例态 ====================

    /// <summary>详情卡的**示例项**（与列表示例行同一批数据）。</summary>
    private static readonly VideoItem SampleItem = CreateSampleItem();

    private static VideoItem CreateSampleItem()
    {
        var v = new VideoItem
        {
            Name = "示例视频 2026.mp4",
            FullPath = @"D:\Videos\示例视频 2026.mp4",
            Extension = ".mp4",
            SizeBytes = 1_610_612_736,                        // ≈ 1.5 GB
            Modified = new DateTime(2026, 10, 13, 15, 0, 0),
            Created = new DateTime(2026, 10, 13, 15, 0, 0)
        };
        v.ApplyMedia(new VideoMediaInfo(
            Duration: 3723, Width: 1920, Height: 1080, Codec: "hevc", BitRate: 8_500_000,
            Fps: 29.97, ContainerFormat: "mp4", AudioCodec: "aac"));
        return v;
    }

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例项**（示例态下卡片整体禁用）。</summary>
    public VideoItem DetailItem => Selected ?? SampleItem;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>
    /// 工具条末位的详情开关 → 走本命令。
    /// <para>⛔ 基类 <c>ToggleDetailCommand</c> 只翻 <c>IsDetailVisible</c>、**不通知 Shell**，
    /// Shell 的右列（由 <c>DetailCardHost</c> 承载）就不会跟着出现 / 消失。</para>
    /// </summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);   // 基类逻辑：翻转 + 落盘 + 展开时没选中就选第一项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);     // 基类逻辑：确保展开并定位到当前选中项
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片对应：单分区（只读信息 + 可改文件名），无卡内动作区
    /// （v3.19 起详情卡不放操作按钮，动作全在工具条 / 右键菜单）。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        SamplePath = nameof(IsDetailSample),
        EmptyText = "扫描一个目录后，在左侧列表里选中视频：这里会显示完整路径、大小、修改时间，以及分辨率 / 编码 / 码率 / 时长 —— 足够判断要不要剪辑或转码。勾上几行还能批量「发送到转码 / 剪切」。",
        SampleText = "（示例数据）在左侧选中一个视频后，这里显示它的真实信息；示例态下卡片不可编辑。",
        Sections =
        {
            new DetailSection
            {
                // 原页内卡的「选中视频」小标题挪成分区标题（Shell 渲染器统一给标题行样式）
                Header = "选中视频",
                Fields =
                {
                    new DetailField
                    {
                        Label = "文件名（可改 · 失焦即提交；也可用下方「✏ 应用新文件名」）",
                        ValuePath = nameof(EditName),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "完整路径",
                        ValuePath = "DetailItem.FullPath",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    // 原来「类型 / 大小」是**同行并排**两个值 → 按 NT-054 已拍板的降级口径拆成两行
                    new DetailField
                    {
                        Label = "类型",
                        ValuePath = "DetailItem.Extension",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "大小",
                        ValuePath = "DetailItem.SizeDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "时长",
                        ValuePath = "DetailItem.DurationDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "分辨率",
                        ValuePath = "DetailItem.ResolutionDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "编码格式",
                        ValuePath = "DetailItem.CodecDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "码率",
                        ValuePath = "DetailItem.BitRateDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "修改时间",
                        ValuePath = "DetailItem.ModifiedDisplay",
                        Kind = FieldKind.Text,
                        Selectable = true
                    },
                    new DetailField
                    {
                        Label = "媒体信息",
                        ValuePath = "DetailItem.MediaStatus",
                        Kind = FieldKind.Text,
                        Selectable = true
                    }
                }
            }
        },
        Actions =
        {
            // 原来改名是「详情卡的 TextBox 回车 / 失焦」→ code-behind 挂 KeyDown / LostFocus。
            // 声明式卡里输入框在 Shell 侧，页面 code-behind 够不着 ⇒ 收进 Actions 区补一个显式按钮
            //（失焦自动提交仍保留，见 OnEditNameChanged）。
            new DetailAction
            {
                Text = "✏ 应用新文件名",
                Classes = "ghost",
                ToolTip = "把「文件名」框里的内容提交到磁盘（回车场景：失焦已自动提交，这里手动补一次）",
                Command = SaveNameCommand
            }
        }
    };

    /// <summary>全局导入/导出默认目录（<c>app.json</c> 的 <c>VideoDbFolder</c>；v3.15 全局，覆盖 .db 与 .json 选择器）。</summary>
    public string ImportExportDefaultDir => _cfg?.VideoDbFolder ?? "";

    /// <summary>已勾选的文件数（工具条上的「发送到…」按钮据此启用 / 显示数量）。</summary>
    public int CheckedCount => _all.Count(v => v.IsChecked);

    public bool HasChecked => CheckedCount > 0;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public VideoListViewModel() { }

    public VideoListViewModel(IAppConfigService cfg, IColumnLayoutService cols, VideoMediaIndex index,
                              VideoTaskStore store, INavigationService nav)
        : base(cols)
    {
        _cfg = cfg;
        _index = index;
        _store = store;
        _nav = nav;
        LoadLayout();
        Folder = _lastFolder;                                                            // 本次运行内记住上次选的目录
        UpdateHeaders();
        UpdateDetailSample(false);      // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
        EditName = SampleItem.Name;

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
    /// <para>⛔ 填充编辑缓冲（切选中项 / 示例态）时也会走到这里，所以先跟当前选中项比一次名字：
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
        OnPropertyChanged(nameof(DetailItem));      // v3.32：详情卡示例态 → 真值 / 示例值 切换
        UpdateDetailSample(value != null);
        // 详情卡可编辑字段跟着选中项走：切换选中即丢弃**未提交**的改动（提交要显式回车 / 失焦）；
        // 没有选中项 → 用示例项的名字（示例态下卡片禁用，改不动）
        EditName = (value ?? SampleItem).Name;
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
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.22：有真实数据后示例行消失
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

    /// <summary>🧹 清除列表（≈ 浏览器无缓存刷新）：只清界面上的列表，⛔ 不动磁盘文件、不重扫目录。</summary>
    [RelayCommand]
    private async Task ClearListAsync()
    {
        var n = _all.Count;
        if (n == 0 && Videos.Count == 0) { StatusText = "列表已经是空的。"; return; }

        var msg = $"清除当前列表里的 {n} 个视频条目？" + Environment.NewLine
                + "只清界面上的列表 —— 不会删除任何视频文件；清完可以重新导入 / 扫描。";
        if (!await NewToys.Core.Services.DialogHooks.ConfirmAsync(msg)) { StatusText = "已取消清除。"; return; }

        _scanCts?.Cancel();
        ClearFiltersCommand.Execute(null);   // 筛选条件一并归零（否则清完看着像"还有条件挡着"）
        SearchText = "";
        _all.Clear();
        Videos.Clear();
        Selected = null;
        RebuildQuickFilters();
        ApplyView();
        StatusText = $"已清除列表（{n} 个条目）；视频文件没动 —— 点「📂 导入」重新载入。";
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

        RebuildQuickFilters();
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
