using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.WebTools.Models;
using NewToys.WebTools.Services;

namespace NewToys.WebTools.ViewModels;

/// <summary>
/// hosts 管理页：多套方案（列表）+ 右侧详情（编辑条目 / 应用到系统 hosts / 网络导入）。
/// 布局遵循规范 3.8 列表页规范；列显隐 / 详情开合偏好落在 <c>config\columnlayout.json</c>。
/// </summary>
public partial class HostsViewModel : ListPageViewModelBase
{
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly HostsService _svc = null!;

    public ObservableCollection<HostsProfile> Profiles { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "就绪";
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
        new("Name", "名称", "3*"),
        new("Entries", "条目数", "1.2*"),
        new("Source", "来源", "2*"),
        new("Detail", "", "36", sortable: false),
    };

    /// <summary>排序列 / 筛选条件变化后重算列表（基类的 SortCommand 与筛选都走这里）。</summary>
    protected override void RefreshView() => ApplyView();

    protected override void EnsureDetailSelection()
    {
        if (Selected == null && Profiles.Count > 0) Selected = Profiles[0];
    }

    public bool HasSelection => Selected != null;
    public bool HasItems => Profiles.Count > 0;

    public HostsViewModel() { }

    public HostsViewModel(HostsService svc, IColumnLayoutService cols)
        : base(cols)
    {
        _svc = svc;
        LoadLayout();                     // 列显隐 + 详情栏开合（基类）
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
    }

    private void UpdateHeaders()
    {
        NameHeader = "名称" + SortArrow("Name");
    }

    // ============ 选中 → 同步详情编辑区 ============
    partial void OnSelectedChanged(HostsProfile? value)
    {
        EntriesText = value == null ? "" : string.Join("\n", value.Entries.Select(e => e.ToLine()));
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
