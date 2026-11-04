using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;      // ← TryFindResource 是这里的扩展方法（挂在 IResourceHost 上）
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Details;

namespace NewToys.UI.Controls;


/// <summary><see cref="FieldKind.Number"/>：数值 / 评分（可带取值区间）。</summary>
public sealed class NumberFieldRow : DetailFieldRow
{
    public NumberFieldRow(DetailField field, object? source) : base(field, source) { }

    public decimal? Minimum => Field.Min is null ? null : (decimal)Field.Min.Value;
    public decimal? Maximum => Field.Max is null ? null : (decimal)Field.Max.Value;
    public decimal Increment => Field.Step <= 0 ? 1m : (decimal)Field.Step;
    public string? Placeholder => Field.Placeholder;

    private decimal? _number;
    public decimal? Number
    {
        get => _number;
        set
        {
            if (!SetProperty(ref _number, value)) return;
            DetailPath.Write(Source, Field.ValuePath, value);
        }
    }

    protected override void RefreshCore()
    {
        var v = DetailPath.Resolve(Source, Field.ValuePath);
        decimal? d = null;
        if (v is not null)
        {
            try { d = System.Convert.ToDecimal(v, CultureInfo.InvariantCulture); }
            catch (Exception) { d = null; }
        }
        SetProperty(ref _number, d, nameof(Number));
    }
}

/// <summary>集合编辑器的一枚 chip。</summary>
public sealed class DetailChip : ObservableObject
{
    public object? Source { get; init; }
    public string Text { get; init; } = "";
    public ICommand? Command { get; init; }
    public object? Argument { get; init; }

    /// <summary>chip 左侧的色块（<c>ChipBrushPath</c> 非空时给）—— 标签 / 分类 / 集合 / 色板都用它带颜色。</summary>
    public Avalonia.Media.IBrush? Brush { get; init; }

    /// <summary>是否有色块（模板据此显隐那个 8×8 圆角方块）。</summary>
    public bool HasBrush => Brush is not null;

    /// <summary>是否有文本（色板模式只给色块、文本为空 ⇒ 隐藏那个 TextBlock）。</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>鼠标悬停提示（<c>ChipToolTipPath</c>，可空；色板用它显示 Hex）。</summary>
    public string? ToolTipText { get; init; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>chips 的一个**分组**（标签按命名空间分组显示时用）。</summary>
public sealed class DetailChipGroup
{
    /// <summary>组标题（<c>ChipGroupPath</c> 取到的键；空键归到「其它」）。</summary>
    public string? Header { get; set; }

    public bool HasHeader => !string.IsNullOrWhiteSpace(Header);

    /// <summary>是否色块模式（透传 <see cref="ChipsFieldRow.IsSwatch"/>；模板只在**上一层**取，⛔ 不用链式 $parent）。</summary>
    public bool IsSwatch { get; set; }

    public ObservableCollection<DetailChip> Chips { get; } = new();
}

/// <summary>
/// <see cref="FieldKind.Chips"/>：集合编辑器（标签：点一下打上 / 取消）。
/// <para><see cref="DetailField.ChipGroupPath"/> 非空 ⇒ 按这个键**分组显示**（每组一个小标题 + 一组 chips）。</para>
/// </summary>
public sealed class ChipsFieldRow : DetailFieldRow
{
    private INotifyCollectionChanged? _watched;

    /// <summary>上次构建 chip 用的源集合实例：同一个实例 ⇒ 结构只能经 <c>CollectionChanged</c> 变。</summary>
    private IEnumerable? _boundRaw;

    /// <summary>源集合结构变了（Clear / Add / Remove）⇒ 下次 Refresh 强制重建。</summary>
    private bool _structureDirty = true;

    /// <summary>源项 → chip（选中态变化时原地同步，⛔ 不重建集合）。</summary>
    private readonly Dictionary<object, DetailChip> _chipByItem = new();
    private readonly List<INotifyPropertyChanged> _subscribedItems = new();

    public ChipsFieldRow(DetailField field, object? source) : base(field, source) { }

    /// <summary>全部 chip（扁平；分组时 = 各组之和）。</summary>
    public ObservableCollection<DetailChip> Chips { get; } = new();
    public bool HasChips => Chips.Count > 0;

    /// <summary>**分组视图**（模板走这个；不分组时只有一组、且没有标题）。</summary>
    public ObservableCollection<DetailChipGroup> Groups { get; } = new();
    public bool HasGroups => Groups.Count > 0;

    /// <summary>
    /// 色块模式（<c>ChipBrushPath</c> 非空）：模板给按钮换成 <c>chip-swatch</c> 样式（紧凑色块按钮），
    /// 色板 / 带颜色的标签 chips 都走这条路。
    /// </summary>
    public bool IsSwatch => !string.IsNullOrEmpty(Field.ChipBrushPath);

    protected override void RefreshCore()
    {
        var raw = DetailPath.Resolve(Source, Field.OptionsPath) as IEnumerable;
        var cmd = DetailPath.Resolve(Source, Field.ChipCommandPath) as ICommand;

        // 集合对象换了 → 换订阅（增删 chip 要能刷新）
        if (raw is INotifyCollectionChanged ncc && !ReferenceEquals(ncc, _watched))
        {
            if (_watched is not null) _watched.CollectionChanged -= OnSourceCollectionChanged;
            _watched = ncc;
            _watched.CollectionChanged += OnSourceCollectionChanged;
            _structureDirty = true;
        }

        // 同一集合实例且结构没变 ⇒ 只同步选中态（🔴 Clear + 整排重建 = 每次刷新闪一下，失焦提交尤其明显）。
        // ⚠️ 前提：源是 ObservableCollection（结构变化必发 CollectionChanged → _structureDirty）；
        //    纯 List 原地改内容检测不到，换集合实例（发 VM 属性通知）才会重建。
        if (!_structureDirty && ReferenceEquals(raw, _boundRaw))
        {
            SyncSelections();
            return;
        }

        _boundRaw = raw;
        _structureDirty = false;
        UnsubscribeItems();

        Chips.Clear();
        Groups.Clear();
        _chipByItem.Clear();

        var groupPath = Field.ChipGroupPath;
        var grouping = !string.IsNullOrWhiteSpace(groupPath);
        DetailChipGroup? cur = null;
        string? lastKey = null;

        if (raw is not null)
        {
            foreach (var item in raw)
            {
                var key = grouping ? DetailPath.Resolve(item, groupPath)?.ToString() : null;
                // 组键变了（或第一项）⇒ 开一个新组；不分组 ⇒ 全程只有一组、无标题
                if (cur is null || (grouping && !string.Equals(key, lastKey, StringComparison.Ordinal)))
                {
                    cur = new DetailChipGroup
                    {
                        Header = grouping ? (string.IsNullOrWhiteSpace(key) ? "其它" : key) : null,
                        IsSwatch = IsSwatch
                    };
                    Groups.Add(cur);
                    lastKey = key;
                }

                var text = Field.ChipTextPath is null
                    ? item?.ToString()
                    : DetailPath.Resolve(item, Field.ChipTextPath)?.ToString();
                var chip = new DetailChip
                {
                    Source = item,
                    Text = text ?? "",
                    Command = cmd,
                    // 命令参数：默认把**候选项本身**传出去（标签 / 分类 / 集合的 Toggle 命令收整项）；
                    // 给了 ChipCommandArgPath 就取候选项上的那个属性（色板的 SetColorCommand 收 Hex 字符串）。
                    Argument = Field.ChipCommandArgPath is null ? item : DetailPath.Resolve(item, Field.ChipCommandArgPath),
                    Brush = DetailPath.Resolve(item, Field.ChipBrushPath) as Avalonia.Media.IBrush,
                    ToolTipText = DetailPath.Resolve(item, Field.ChipToolTipPath)?.ToString(),
                    IsSelected = DetailPath.ResolveBool(item, Field.ChipSelectedPath)
                };
                Chips.Add(chip);
                cur.Chips.Add(chip);
                if (item is not null)
                {
                    _chipByItem[item] = chip;
                    SubscribeItem(item);
                }
            }
        }
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsSwatch));
    }

    /// <summary>同一集合实例的轻量刷新：只把 chip 选中态对齐源项（值没变不触发，无视觉扰动）。</summary>
    private void SyncSelections()
    {
        if (Field.ChipSelectedPath is null) return;
        foreach (var kv in _chipByItem)
        {
            var v = DetailPath.ResolveBool(kv.Key, Field.ChipSelectedPath);
            if (kv.Value.IsSelected != v) kv.Value.IsSelected = v;
        }
    }

    /// <summary>订阅源项的选中态变化（PickItem 等 ObservableObject）：点一下 chip 立即变色，不用等整卡重解析。</summary>
    private void SubscribeItem(object? item)
    {
        if (item is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += OnItemPropertyChanged;
            _subscribedItems.Add(inpc);
        }
    }

    private void UnsubscribeItems()
    {
        foreach (var i in _subscribedItems) i.PropertyChanged -= OnItemPropertyChanged;
        _subscribedItems.Clear();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Field.ChipSelectedPath is null
            || sender is null
            || !_chipByItem.TryGetValue(sender, out var chip)) return;
        var seg = DetailPath.Strip(Field.ChipSelectedPath).Split('.')[^1];
        if (e.PropertyName.Length > 0 && e.PropertyName != seg) return;
        chip.IsSelected = DetailPath.ResolveBool(sender, Field.ChipSelectedPath);
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _structureDirty = true;
        Refresh();
    }
}

/// <summary>
/// <see cref="FieldKind.Hint"/>：纯提示文字（含空态）。
/// <para>两种写法：① 静态文案（<c>Label</c> / <c>Hint</c> 直接写死）；② 动态行（给 <c>ValuePath</c> 取运行值，
/// 可配 <c>Prefix</c> 当前缀）。</para>
/// </summary>
public sealed class HintFieldRow : DetailFieldRow
{
    public HintFieldRow(DetailField field, object? source) : base(field, source) { }

    private string? _text;
    public string? Text { get => _text; set => SetProperty(ref _text, value); }

    protected override void RefreshCore()
    {
        var body = Field.ValuePath is null
            ? Field.Hint ?? Field.Label
            : DetailPath.Resolve(Source, Field.ValuePath)?.ToString();
        Text = string.IsNullOrEmpty(Field.Prefix) ? body : Field.Prefix + body;
    }
}

/// <summary><see cref="FieldKind.Divider"/>：分隔线。</summary>
public sealed class DividerFieldRow : DetailFieldRow
{
    public DividerFieldRow(DetailField field, object? source) : base(field, source) { }
}

