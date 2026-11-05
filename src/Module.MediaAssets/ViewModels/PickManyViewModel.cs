using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>多选弹层的一行（一个标签 / 一个集合）。</summary>
public sealed partial class PickManyItem : ObservableObject
{
    public PickManyItem(long id, string name, IBrush brush, string? group, bool already, bool blocked)
    {
        Id = id;
        Name = name;
        Brush = brush;
        Group = group;
        _already = already;
        _blocked = blocked;
    }

    public long Id { get; }
    public string Name { get; }
    public IBrush Brush { get; }

    /// <summary>分组（标签 = 类型；集合 = 空）。</summary>
    public string? Group { get; }

    /// <summary>分组文案（空 = 不显示）。</summary>
    public string GroupText => string.IsNullOrWhiteSpace(Group) ? "" : Group!;

    /// <summary>勾上 = 这次要加。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>所有目标都已有（显示「已有」，勾了也不会重复加）。</summary>
    [ObservableProperty]
    private bool _already;

    /// <summary>不能手工加（智能集合：成员由规则决定）。</summary>
    [ObservableProperty]
    private bool _blocked;

    public string Note => Blocked ? "智能集合，不能手工加" : Already ? "已有" : "";
}

/// <summary>
/// 「批量加标签 / 加集合」的多选弹层 VM。
/// <para>
/// 🔴 语义是**只加不减**：勾上 = 给所有目标加这个标签 / 集合；不勾 = 不动（⛔ 不会顺手把别人已有的删掉）。
/// 要取消某个标签，去详情卡点色片（那才是「设置成什么」的语义）。
/// </para>
/// <para>
/// ⛔ 不弹输入框让用户手打标签名：手打必然拼错、看不到已有标签、也没法一次加多个。
/// 新建仍然保留输入框（这是唯一必须手打的地方，且只影响一个名字）。
/// </para>
/// </summary>
public sealed partial class PickManyViewModel : ObservableObject
{
    /// <summary>这次是加标签还是加集合。</summary>
    public enum PickKind
    {
        Tag,
        Collection
    }

    private readonly List<PickManyItem> _all = new();
    private List<long> _assetIds = new();
    private PickKind _kind = PickKind.Tag;
    private TagService? _tagSvc;
    private CollectionService? _colSvc;
    private OperationLogService? _opLog;
    private Action<string>? _done;

    public ObservableCollection<PickManyItem> Items { get; } = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "批量加标签";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>新建名字（留空 = 不新建）；新建的标签按「通用」类型。</summary>
    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private bool _canConfirm;

    /// <summary>有没有勾中任何一项（「确认」的启用条件之一）。</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>新建或勾选用的按钮文案（标签 / 集合）。</summary>
    public string Noun => _kind == PickKind.Tag ? "标签" : "集合";

    public void Attach(TagService tagSvc, CollectionService colSvc, OperationLogService opLog)
    {
        _tagSvc = tagSvc;
        _colSvc = colSvc;
        _opLog = opLog;
    }

    /// <summary>打开弹层：装候选 / 标出「已有」/ 清空勾选。</summary>
    public void Open(PickKind kind, IReadOnlyCollection<long> assetIds, string title, Action<string>? done)
    {
        _kind = kind;
        _assetIds = assetIds.ToList();
        _done = done;
        Title = title;
        SearchText = "";
        NewName = "";
        Summary = $"目标 {_assetIds.Count} 个资产";
        OnPropertyChanged(nameof(Noun));

        _all.Clear();
        if (kind == PickKind.Tag && _tagSvc != null)
        {
            var owned = _assetIds.Select(id => _tagSvc.ListTagIdsOfAsset(id).ToHashSet()).ToList();
            foreach (var t in _tagSvc.List().OrderBy(t => t.Namespace ?? "").ThenBy(t => t.Name, StringComparer.CurrentCulture))
                _all.Add(new PickManyItem(t.Id, t.Name, t.ChipBrush, t.Namespace,
                    already: owned.Count > 0 && owned.All(s => s.Contains(t.Id)), blocked: false));
        }
        else if (kind == PickKind.Collection && _colSvc != null)
        {
            var owned = _assetIds.Select(id => _colSvc.ListCollectionIdsOfAsset(id).ToHashSet()).ToList();
            foreach (var c in _colSvc.List().OrderBy(c => c.Name, StringComparer.CurrentCulture))
                _all.Add(new PickManyItem(c.Id, c.Name,
                    new SolidColorBrush(Avalonia.Media.Color.Parse(AssetPalette.For(c.Name))),
                    group: null,
                    already: owned.Count > 0 && owned.All(s => s.Contains(c.Id)),
                    blocked: c.IsSmart));
        }

        RefillList();
        // 勾选是 CheckBox 直接双向绑到 Item.IsSelected 的 ⇒ 这里订阅变化，好让「确认」的启用态实时跟上
        foreach (var item in _all)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PickManyItem.IsSelected)) RecomputeGate();
            };
        IsOpen = true;
        StatusText = _all.Count == 0
            ? $"库里还没有{Noun}：在下面「新建」里输入一个名字即可。"
            : "勾上要加的项，再点确认（只加不减）。";
    }

    partial void OnSearchTextChanged(string value) => RefillList();

    private void RefillList()
    {
        var text = (SearchText ?? "").Trim();
        Items.Clear();
        foreach (var item in _all)
        {
            if (text.Length > 0
                && item.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0
                && item.GroupText.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            Items.Add(item);
        }
        RecomputeGate();
    }

    /// <summary>勾选 / 新建名字变化 ⇒ 重算「确认」能不能点。</summary>
    private void RecomputeGate()
    {
        HasSelection = _all.Any(i => i.IsSelected && !i.Blocked);
        CanConfirm = HasSelection || (NewName ?? "").Trim().Length > 0;
    }

    /// <summary>勾上 / 取消（CheckBox 双向绑同一个属性，点行也能切）。</summary>
    [RelayCommand]
    private void Toggle(PickManyItem? item)
    {
        if (item == null || item.Blocked) return;
        item.IsSelected = !item.IsSelected;
        RecomputeGate();
    }

    partial void OnNewNameChanged(string value) => RecomputeGate();

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        _done = null;
    }

    /// <summary>确认：把勾选的（+ 新建的）加到所有目标上。</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (_assetIds.Count == 0) { StatusText = "没有目标（先选中资产）。"; return; }

        var picked = _all.Where(i => i.IsSelected && !i.Blocked).ToList();
        var newName = (NewName ?? "").Trim();
        if (picked.Count == 0 && newName.Length == 0) { StatusText = $"先勾一个{Noun}，或在「新建」里输个名字。"; return; }

        var added = 0;
        var created = 0;

        if (_kind == PickKind.Tag && _tagSvc != null)
        {
            foreach (var item in picked) added += _tagSvc.AddTagToAssets(_assetIds, item.Id);
            if (newName.Length > 0)
            {
                var id = _tagSvc.Ensure(newName, TagTypes.General);
                if (id > 0) { created = 1; added += _tagSvc.AddTagToAssets(_assetIds, id); }
            }
            _opLog?.Log("tag", "tag", null,
                afterJson: $"{{\"assets\":{_assetIds.Count},\"added\":{added},\"picked\":{picked.Count},\"new\":\"{newName}\"}}");
        }
        else if (_kind == PickKind.Collection && _colSvc != null)
        {
            foreach (var item in picked) added += _colSvc.AddAssets(item.Id, _assetIds);
            if (newName.Length > 0)
            {
                var id = _colSvc.Create(newName, CollectionTypes.Manual);
                if (id > 0) { created = 1; added += _colSvc.AddAssets(id, _assetIds); }
            }
            _opLog?.Log("collection", "collection", null,
                afterJson: $"{{\"assets\":{_assetIds.Count},\"added\":{added},\"picked\":{picked.Count},\"new\":\"{newName}\"}}");
        }

        IsOpen = false;
        var message = $"已给 {_assetIds.Count} 个资产加{Noun}（新增挂接 {added} 处"
                      + (created > 0 ? $"，其中新建「{newName}」" : "")
                      + "；本来已有的不重复加）。";
        _done?.Invoke(message);
        _done = null;
    }
}
