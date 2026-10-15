using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// hosts 管理页：多套方案（列表）+ 右侧详情（编辑条目 / 应用到系统 hosts / 网络导入）。
/// 布局遵循规范 3.8 列表页规范；列显隐 / 详情开合偏好落在 <c>config\columnlayout.json</c>。
/// </summary>
public partial class HostsViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    // ==================== 声明式工具条（NT-055 / 需求 §4.8.5） ====================
    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "➕ 新建方案", Classes = "primary", CommandPath = nameof(NewProfileCommand) },
        new() { Text = "🔄 重读方案", CommandPath = nameof(RefreshCommand), ToolTip = "重新读一遍本地的 hosts 方案清单（⛔ 不动系统 hosts、不导入）" },
        new() { Kind = ToolbarItemKind.TextField, Width = 300, Placeholder = "网络 hosts 文件 URL…", ValuePath = nameof(ImportUrl), ToolTip = "粘贴一份 hosts 文本文件的网址，点「导入」下载并解析" },
        new() { Text = "🌐 导入", CommandPath = nameof(ImportCommand), IsEnabledPath = "!" + nameof(IsBusy) },
        new() { Kind = ToolbarItemKind.Label, Text = "双击应用 = 写入系统 hosts（需管理员）" },
    };

    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly HostsService _svc = null!;

    public ObservableCollection<HostsProfile> Profiles { get; } = new();

    [ObservableProperty] private bool _isBusy;
    // v3.9：StatusText 已提到 ListPageViewModelBase（由 Shell 底部状态栏统一显示），这里不再自己声明。
    [ObservableProperty] private HostsProfile? _selected;
    [ObservableProperty] private string _entriesText = "";
    [ObservableProperty] private string _importUrl = "";
    [ObservableProperty] private string _blockPreview = "";

    // ============ 表头 ============
    [ObservableProperty] private string _nameHeader = "名称 ▲";
    [ObservableProperty] private string _entriesHeader = "条目数";
    [ObservableProperty] private string _sourceHeader = "来源";

    // ============ 列显隐 / 详情开合 / 排序 / 多项筛选：继承自 ListPageViewModelBase（v1.35 收口） ============
    //
    // XAML 只能绑具名属性 → 每列给一个直通属性（真正的状态在基类里，落 config\columnlayout.json）。

    public bool IsNameVisible => Vis("Name");
    public bool IsEntriesVisible => Vis("Entries");
    public bool IsSourceVisible => Vis("Source");

    protected override string PageKey => "web-hosts";

    /// <summary>列的元数据（顺序即表头与行的列顺序）；最后一列 Detail 是「▤」按钮的固定窄列。</summary>
    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        // v3.32 §4.8.3：每列给一条示例值（空态水印行 + 列宽判定）
        new("Name", "名称", "3*", sample: "开发环境 hosts"),
        new("Entries", "条目数", "1.2*", sample: "12"),
        new("Source", "来源", "2*", sample: @"D:\hosts\dev.hosts"),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Profiles.Count > 0) Selected = Profiles[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Profiles.Count > 0;

    // ==================== v3.32 §4.8.3：列表示例行 + 详情卡示例态 ====================

    /// <summary>详情卡 / 列表示例行用的**示例方案**。</summary>
    private static readonly HostsProfile SampleItem = new()
    {
        Name = "开发环境 hosts",
        Source = @"D:\hosts\dev.hosts",
        Entries = new List<HostsEntry>
        {
            new() { Ip = "127.0.0.1", Host = "dev.local", Comment = "示例数据" },
            new() { Ip = "10.0.0.8", Host = "api.dev.local", Comment = "" }
        }
    };

    /// <summary>列表为空时显示**示例行**（⛔ 不进 <c>Profiles</c>、不参与统计 / 筛选 / 排序）。</summary>
    public bool ShowSampleRow => HasSampleRow && Profiles.Count == 0;

    /// <summary>详情卡的数据源：有选中项用真值，没选中用**示例方案**。</summary>
    public HostsProfile DetailItem => Selected ?? SampleItem;

    /// <summary>详情卡正文是否显示（有选中项，或处于示例态）。</summary>
    public bool ShowDetailBody => HasSelection || IsDetailSample;

    protected override void OnDetailSampleChanged() => OnPropertyChanged(nameof(ShowDetailBody));

    // ==================== 详情面板 + 声明式详情卡（NT-054 的 L2 迁移页，需求 §4.5.1） ====================

    /// <summary>无权限写系统 hosts 时给出的可复制受管块是否非空（原 XAML 用 <c>ObjectConverters.IsNotNull</c>，描述符只认布尔路径）。</summary>
    public bool HasBlockPreview => !string.IsNullOrWhiteSpace(BlockPreview);

    partial void OnBlockPreviewChanged(string value) => OnPropertyChanged(nameof(HasBlockPreview));

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;

    /// <summary>声明式卡（<see cref="Card"/> 非 null）⇒ ⛔ 不再返回自绘视图；老路只留给尚未迁移的页。</summary>
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    /// <summary>工具条末位的详情开关 → 走本命令（⛔ 基类 <c>ToggleDetailCommand</c> 不通知 Shell，右列不会跟着开合）。</summary>
    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shell 按 config 回放「展开」时调（同样要通知 Shell）。</summary>
    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— 显式实现接口（⛔ 不写成 public 方法：基类已有同名的 private [RelayCommand] ToggleDetail / ShowDetail，隐式实现会撞名） ——
    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor? _card;

    /// <summary>
    /// 本页详情卡描述符（**C# 对象字面量**，⛔ 不做运行时 JSON / XAML 加载）。
    /// <para>结构与迁移前的页内自绘卡片对应：方案名 + 条目编辑区 + 受管块预览 + 四个卡内动作（保存 / 应用 / 导出 / 删除）。</para>
    /// </summary>
    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        SamplePath = nameof(IsDetailSample),
        EmptyText = "在左侧列表里选中一套方案",
        SampleText = "（示例数据）在左侧选中一套方案后，这里显示它的真实内容；示例态下不可编辑。",
        Sections =
        {
            new DetailSection
            {
                Fields =
                {
                    new DetailField
                    {
                        Label = "方案名称",
                        ValuePath = "DetailItem.Name",
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "条目（每行一条：IP 主机名 # 注释；行首 # 表示禁用）",
                        ValuePath = nameof(EntriesText),
                        Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur,
                        Mono = true,
                        MultiLine = true,
                        Rows = 160
                    },
                    new DetailField
                    {
                        Kind = FieldKind.Hint,
                        Hint = "无权限写入系统 hosts 时，下方会显示可复制的受管块：",
                        VisibleWhen = nameof(HasBlockPreview)
                    },
                    new DetailField
                    {
                        ValuePath = nameof(BlockPreview),
                        Kind = FieldKind.Text,
                        Selectable = true,
                        Mono = true,
                        VisibleWhen = nameof(HasBlockPreview)
                    }
                }
            }
        },
        Actions =
        {
            // 原来这四个按钮是页内卡里的 WrapPanel（只在有选中 / 示例态之外显示）；声明式卡里进 Actions 区。
            new DetailAction
            {
                Text = "💾 保存",
                Classes = "primary",
                ToolTip = "把当前内容写回方案（落 config 下的 hosts 方案文件）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = SaveCommand
            },
            new DetailAction
            {
                Text = "✅ 应用",
                Classes = "ghost",
                ToolTip = "把这套方案应用到系统 hosts（无权限时给出可复制的受管块）",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ApplyCommand
            },
            new DetailAction
            {
                Text = "📤 导出",
                Classes = "ghost",
                ToolTip = "把当前方案导出成文件",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = ExportCommand
            },
            new DetailAction
            {
                Text = "🗑 删除",
                Classes = "ghost",
                ToolTip = "删除这套方案",
                IsVisiblePath = nameof(ShowDetailBody),
                Command = DeleteCommand
            }
        }
    };

    public HostsViewModel() { }

    public HostsViewModel(HostsService svc, IColumnLayoutService cols)
        : base(cols)
    {
        _svc = svc;
        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
        UpdateDetailSample(false);        // v3.32 §4.8.3：初始没有选中项 → 详情卡进"示例态"
        Refresh();
    }

    // ============ 加载 ============
    [RelayCommand]
    private void Refresh()
    {
        Profiles.Clear();
        foreach (var p in _svc.Load().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Profiles.Add(p);
        if (Selected != null && !Profiles.Contains(Selected)) Selected = null;
        ApplyView();
        StatusText = $"共 {Profiles.Count} 套方案";
    }

    // ============ 排序 ============
    private void ApplyView()
    {
        // 多项筛选（基类）：多条条件之间取「且」
        IEnumerable<HostsProfile> q = ApplyFilters(Profiles);

        q = SortDescending
            ? q.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
        var ordered = q.ToList();
        Profiles.Clear();
        foreach (var p in ordered) Profiles.Add(p);
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowSampleRow));   // v3.32：有真实数据后示例行消失
        UpdateDetailSample(Selected != null);
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
    }

    // ============ 选中 → 同步详情编辑区 ============
    partial void OnSelectedChanged(HostsProfile? value)
    {
        UpdateDetailSample(value != null);   // v3.32 §4.8.3：没选中项 → 详情卡改用示例方案
        OnPropertyChanged(nameof(DetailItem));
        var v = value ?? (HasSampleRow ? SampleItem : null);
        EntriesText = v == null ? "" : string.Join("\n", v.Entries.Select(e => e.ToLine()));
        BlockPreview = "";
        OnPropertyChanged(nameof(HasSelection));
    }

    // ============ 增删改 ============
    [RelayCommand]
    private void NewProfile()
    {
        var p = new HostsProfile { Name = "新方案 " + (Profiles.Count + 1) };
        Profiles.Add(p);
        Selected = p;
        IsDetailVisible = true;
        StatusText = "已新建方案，编辑条目后点「保存」。";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected == null) return;
        Profiles.Remove(Selected);
        await PersistAsync();
        Selected = null;
        StatusText = "已删除方案。";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected == null) return;
        Selected.Entries = HostsService.ParseText(EntriesText);
        await PersistAsync();
        StatusText = $"已保存「{Selected.Name}」（{Selected.Entries.Count} 条）。";
    }

    private async Task PersistAsync()
    {
        await Task.Run(() => _svc.Save(Profiles.ToList()));
    }

    // ============ 应用 / 导出 / 导入 ============
    [RelayCommand]
    private void Apply()
    {
        if (Selected == null) return;
        var (ok, msg) = _svc.Apply(Selected);
        StatusText = msg;
        if (!ok) BlockPreview = _svc.ExportBlock(Selected);   // 无权限时给出可复制的块
    }

    [RelayCommand]
    private void Export()
    {
        if (Selected == null) return;
        BlockPreview = _svc.ExportBlock(Selected);
        StatusText = "已生成受管块，可复制后手动粘贴到系统 hosts。";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportUrl)) { StatusText = "请先填写网络 hosts 文件 URL。"; return; }
        try
        {
            IsBusy = true;
            var (ok, msg, entries) = await _svc.ImportFromUrlAsync(ImportUrl.Trim());
            if (ok && Selected != null)
            {
                Selected.Entries = entries;
                Selected.Source = "network:" + ImportUrl.Trim();
                EntriesText = string.Join("\n", Selected.Entries.Select(e => e.ToLine()));
                await PersistAsync();
            }
            StatusText = msg;
        }
        finally { IsBusy = false; }
    }
}
