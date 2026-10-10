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
public partial class HostsViewModel : ObservableObject
{
    private const string PageKey = "web-hosts";
    // 只由 DI 注入（保留无参构造供设计期 / DI 未注册时兜底）→ 用 null! 抑制 CS8618
    private readonly HostsService _svc = null!;
    private readonly IColumnLayoutService? _cols;
    private string _sortColumn = "Name";
    private bool _sortDescending;

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

    // ============ 列显隐 ============
    [ObservableProperty] private bool _isNameVisible = true;
    [ObservableProperty] private bool _isEntriesVisible = true;
    [ObservableProperty] private bool _isSourceVisible = true;

    public IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "名称", "3*"),
        new("Entries", "条目数", "1.2*"),
        new("Source", "来源", "2*"),
        new("Detail", "", "36", sortable: false),
    };

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
        "Name" => IsNameVisible, "Entries" => IsEntriesVisible, "Source" => IsSourceVisible, _ => true
    };

    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (key == null) return;
        switch (key)
        {
            case "Name": IsNameVisible = !IsNameVisible; break;
            case "Entries": IsEntriesVisible = !IsEntriesVisible; break;
            case "Source": IsSourceVisible = !IsSourceVisible; break;
            default: return;
        }
        SaveColumnVisibility();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    private void SaveColumnVisibility()
    {
        _cols?.SavePage(PageKey, new Dictionary<string, bool>
        {
            ["Name"] = IsNameVisible, ["Entries"] = IsEntriesVisible, ["Source"] = IsSourceVisible
        });
    }

    private void LoadColumnVisibility()
    {
        var saved = _cols?.LoadPage(PageKey);
        if (saved == null) return;
        if (saved.TryGetValue("Name", out var n)) IsNameVisible = n;
        if (saved.TryGetValue("Entries", out var e)) IsEntriesVisible = e;
        if (saved.TryGetValue("Source", out var s)) IsSourceVisible = s;
    }

    // ============ 详情开合 ============
    [ObservableProperty] private bool _isDetailVisible;

    [RelayCommand]
    private void ToggleDetail()
    {
        IsDetailVisible = !IsDetailVisible;
        if (IsDetailVisible && Selected == null && Profiles.Count > 0) Selected = Profiles[0];
        SaveDetailFlag();
    }

    [RelayCommand]
    private void ShowDetail()
    {
        if (Selected == null && Profiles.Count > 0) Selected = Profiles[0];
        IsDetailVisible = true;
        SaveDetailFlag();
    }

    private void SaveDetailFlag() => _cols?.SaveFlag(PageKey, IColumnLayoutService.FlagDetailPanel, IsDetailVisible);

    public bool HasSelection => Selected != null;
    public bool HasItems => Profiles.Count > 0;

    public HostsViewModel() { }

    public HostsViewModel(HostsService svc, IColumnLayoutService cols)
    {
        _svc = svc;
        _cols = cols;
        LoadColumnVisibility();
        IsDetailVisible = cols.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel);
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
    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase)) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }
        ApplyView();
    }

    private void ApplyView()
    {
        var q = _sortDescending
            ? Profiles.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
        var ordered = q.ToList();
        Profiles.Clear();
        foreach (var p in ordered) Profiles.Add(p);
        UpdateHeaders();
        OnPropertyChanged(nameof(HasItems));
    }

    private void UpdateHeaders()
    {
        var arrow = _sortDescending ? " ▼" : " ▲";
        NameHeader = "名称" + (_sortColumn == "Name" ? arrow : "");
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
