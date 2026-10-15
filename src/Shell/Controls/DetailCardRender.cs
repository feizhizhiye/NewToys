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
using NewToys.Core.Services;
using NewToys.Shared.Details;

namespace NewToys.Shell.Controls;

// ============================================================================
// NT-054 · L1 渲染层：把「描述符」翻译成「可绑定行」
// 需求 §4.5.1 / 规范 §3.6「详情卡声明式渲染」。
//
// 🔴 为什么不让 XAML 直接绑描述符里的字符串路径？
//    因为 `{Binding <字符串>}` 在 XAML 里**写不出来**（路径必须是字面量）。
//    所以这里在 C# 侧把 `ValuePath` / `VisibleWhen` / `OptionsPath` … 解析成真实值，
//    行对象只暴露**强类型属性**给模板 → 模板里零字符串路径，改属性名时编译器至少管住行这一侧，
//    描述符那一侧由 `skill/scripts/check-detail-paths.ps1` 兜底。
//
// 🔴 刷新契约（需求 §4.5.1）：
//    属性级改动 → 靠页面 VM 的 INotifyPropertyChanged；本层**订阅源 VM 一次**，
//    任何属性变化就整体重解析（卡片规模极小，够用且不会漏）。
//    动作级改动 → 页面 VM 自己回刷；描述符不参与。
// ============================================================================

/// <summary>绑定路径解析（<c>"A.B"</c> 多级 + <c>"!"</c> 取反）。⛔ 一切失败都当「取不到」处理，不抛。</summary>
internal static class DetailPath
{
    /// <summary>路径是否带取反前缀 <c>!</c>。</summary>
    public static bool IsNegated(string? path) => path is { Length: > 0 } && path[0] == '!';

    /// <summary>剥掉取反前缀。</summary>
    public static string Strip(string? path)
        => path is null ? "" : IsNegated(path) ? path[1..] : path;

    /// <summary>按路径取值；任一段缺失 → null。</summary>
    public static object? Resolve(object? root, string? path)
    {
        var p = Strip(path);
        if (root is null || p.Length == 0) return null;

        var cur = root;
        foreach (var seg in p.Split('.'))
        {
            if (cur is null) return null;
            var pi = cur.GetType().GetProperty(seg, BindingFlags.Public | BindingFlags.Instance);
            if (pi is null) return null;
            cur = pi.GetValue(cur);
        }
        return cur;
    }

    /// <summary>按路径取布尔值（带取反前缀）。</summary>
    public static bool ResolveBool(object? root, string? path)
    {
        var b = Resolve(root, path) is true;
        return IsNegated(path) ? !b : b;
    }

    /// <summary>按路径写值（末段必须有 setter）；类型不符时做一次转换。</summary>
    public static void Write(object? root, string? path, object? value)
    {
        var p = Strip(path);
        if (root is null || p.Length == 0) return;

        var segs = p.Split('.');
        var cur = root;
        for (var i = 0; i < segs.Length - 1; i++)
        {
            if (cur is null) return;
            var mid = cur.GetType().GetProperty(segs[i], BindingFlags.Public | BindingFlags.Instance);
            if (mid is null) return;
            cur = mid.GetValue(cur);
        }
        if (cur is null) return;

        var pi = cur.GetType().GetProperty(segs[^1], BindingFlags.Public | BindingFlags.Instance);
        if (pi is null || !pi.CanWrite) return;

        try { pi.SetValue(cur, Coerce(value, pi.PropertyType)); }
        catch (Exception) { /* ⛔ 路径 / 类型不对就静默跳过：详情卡不该因为一处声明写错就崩 UI */ }
    }

    /// <summary>把值掰成目标类型（Nullable 拆包 / 枚举 / 字符串化）。</summary>
    private static object? Coerce(object? value, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null) return null;
        if (t.IsInstanceOfType(value)) return value;
        if (t == typeof(string)) return value.ToString();
        if (t.IsEnum) return value is string s ? Enum.Parse(t, s, true) : Enum.ToObject(t, value);
        return System.Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
    }

    /// <summary>宽松比较（跨 long/int/decimal 的 Id 也能配上）。</summary>
    public static bool LooseEquals(object? a, object? b) => string.Equals(a?.ToString(), b?.ToString(), StringComparison.Ordinal);
}

/// <summary>字段行基类：承载「可见 / 可用」这类通用联动，子类只管自己的值。</summary>
public abstract class DetailFieldRow : ObservableObject
{
    protected DetailFieldRow(DetailField field, object? source)
    {
        Field = field;
        Source = source;
    }

    /// <summary>原始声明（文案 / 修饰位这类静态信息直接透给模板）。</summary>
    public DetailField Field { get; }

    /// <summary>卡片 DataContext（= 页面 VM），所有路径都相对它解析。</summary>
    protected object? Source { get; }

    /// <summary>提交语义（子类据此决定「什么时候把值写回 VM」）。</summary>
    protected EditCommitMode CommitMode => Field.Commit;

    public string? Label => Field.Label;
    public bool HasLabel => !string.IsNullOrWhiteSpace(Field.Label);
    public string? Hint => Field.Hint;
    public bool HasHint => !string.IsNullOrWhiteSpace(Field.Hint);

    private bool _isVisible = true;
    private bool _isEnabled = true;

    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

    /// <summary>重解析「可见 / 可用」+ 子类的值。源 VM 任一属性变化都会调到。</summary>
    public void Refresh()
    {
        IsVisible = Field.VisibleWhen is null || DetailPath.ResolveBool(Source, Field.VisibleWhen);
        IsEnabled = Field.EnabledWhen is null || DetailPath.ResolveBool(Source, Field.EnabledWhen);
        RefreshCore();
    }

    /// <summary>子类重写：回读自己的值。</summary>
    protected virtual void RefreshCore() { }
}

/// <summary><see cref="FieldKind.Text"/>：单行 / 多行 / 只读可复制 / 密码。</summary>
public sealed class TextFieldRow : DetailFieldRow
{
    public TextFieldRow(DetailField field, object? source) : base(field, source) { }

    public bool IsSelectable => Field.Selectable;
    public bool IsEditable => !Field.Selectable;
    public bool MultiLine => Field.MultiLine;
    public bool Mono => Field.Mono;
    public char PasswordChar => Field.Password ? '●' : '\0';
    public double BoxMinHeight => Field.MultiLine ? (Field.Rows > 0 ? Field.Rows : 52) : 0;
    public string? Placeholder => Field.Placeholder;

    /// <summary>OnBlur 语义：值的写回交给 <c>LostFocus</c>（模板里挂 <c>OnTextLostFocus</c>）。</summary>
    public bool CommitOnBlur => CommitMode == EditCommitMode.OnBlur;

    private string? _text;
    public string? Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value)) return;
            if (!CommitOnBlur) DetailPath.Write(Source, Field.ValuePath, value);
        }
    }

    /// <summary>失焦提交（模板的 <c>LostFocus</c> 事件调它）。</summary>
    public void Commit()
    {
        if (CommitOnBlur) DetailPath.Write(Source, Field.ValuePath, _text);
    }

    protected override void RefreshCore()
        => SetProperty(ref _text, DetailPath.Resolve(Source, Field.ValuePath)?.ToString(), nameof(Text));
}

/// <summary><see cref="FieldKind.Check"/>：勾选框（Instant）。</summary>
public sealed class CheckFieldRow : DetailFieldRow
{
    public CheckFieldRow(DetailField field, object? source) : base(field, source) { }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            DetailPath.Write(Source, Field.ValuePath, value);
        }
    }

    protected override void RefreshCore()
        => SetProperty(ref _isChecked, DetailPath.ResolveBool(Source, Field.ValuePath), nameof(IsChecked));
}

/// <summary>下拉候选项：<c>Display</c> = 显示文案，<c>Source</c> = 原对象（写回时按 <c>OptionValuePath</c> 取值）。</summary>
public sealed class DetailOption
{
    public object? Source { get; init; }
    public string Display { get; init; } = "";
}

/// <summary>
/// <see cref="FieldKind.Select"/>：下拉。
/// <para>🔴 复杂性全部收在这里：<c>ItemsSource</c> + <c>SelectedItem</c> + 显示文案 + 值回写，
/// ⛔ 不再让每页各写一遍 <c>SelectedValue</c>/<c>SelectedValueBinding</c>/<c>ItemTemplate</c> 四件套。</para>
/// </summary>
public sealed class SelectFieldRow : DetailFieldRow
{
    public SelectFieldRow(DetailField field, object? source) : base(field, source) { }

    private List<DetailOption> _options = new();
    public List<DetailOption> Options { get => _options; private set => SetProperty(ref _options, value); }

    private object? _boundOptions;

    private DetailOption? _selected;
    public DetailOption? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            // ⛔ 候选还没到位时（ComboBox 初始 null / 空列表）不回写：否则会把 VM 上的值抹成 null
            if (_options.Count == 0) return;
            DetailPath.Write(Source, Field.ValuePath,
                Field.OptionValuePath is null ? value?.Source : DetailPath.Resolve(value?.Source, Field.OptionValuePath));
        }
    }

    public string? Placeholder => Field.Placeholder;

    protected override void RefreshCore()
    {
        var raw = DetailPath.Resolve(Source, Field.OptionsPath) as IEnumerable;

        // ⚠️ 只在**候选集合实例变了**时才换 Options：换新 List 会让 ComboBox 把 SelectedItem 清成 null，
        //    经 TwoWay 回写就把页面 VM 的值抹掉了（改了参数的卡尤其要命）。
        if (!ReferenceEquals(raw, _boundOptions))
        {
            _boundOptions = raw;
            var list = new List<DetailOption>();
            if (raw is not null)
            {
                foreach (var item in raw)
                {
                    var display = Field.OptionDisplayPath is null
                        ? item?.ToString()
                        : DetailPath.Resolve(item, Field.OptionDisplayPath)?.ToString();
                    list.Add(new DetailOption { Source = item, Display = display ?? "" });
                }
            }
            Options = list;
        }

        var cur = DetailPath.Resolve(Source, Field.ValuePath);
        DetailOption? hit = null;
        foreach (var o in Options)
        {
            var ov = Field.OptionValuePath is null ? o.Source : DetailPath.Resolve(o.Source, Field.OptionValuePath);
            if (DetailPath.LooseEquals(ov, cur)) { hit = o; break; }
        }
        SetProperty(ref _selected, hit, nameof(Selected));
    }
}

/// <summary>
/// <see cref="FieldKind.Select"/> 的**索引通道**：页面 VM 上本来就是「第几个选项」（<c>EncoderIndex</c> / <c>BitrateModeIndex</c> 这类），
/// 候选项是 XAML 里写死的那几条 ⇒ 走 <c>ComboBox.SelectedIndex</c>（配 <c>LiteralOptions</c>）。
/// <para>⛔ 与值通道（<see cref="SelectFieldRow"/>）互斥：<c>IndexPath</c> 非空即走本行对象。</para>
/// </summary>
public sealed class IndexSelectFieldRow : DetailFieldRow
{
    public IndexSelectFieldRow(DetailField field, object? source) : base(field, source) { }

    private List<string> _options = new();
    public List<string> Options { get => _options; private set => SetProperty(ref _options, value); }

    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value)) return;
            // ⛔ 候选还没到位时（ComboBox 初始 -1 / 空列表）不回写：否则会把 VM 上的值抹成 -1
            if (_options.Count == 0) return;
            DetailPath.Write(Source, Field.IndexPath, value);
        }
    }

    protected override void RefreshCore()
    {
        // ⚠️ 候选列表**只建一次**：换新的 ItemsSource 会让 ComboBox 把 SelectedIndex 清成 -1 并 TwoWay 回写，
        //    等于每刷新一次就把页面 VM 上的「第几个编码器」抹掉。
        if (_options.Count == 0)
        {
            if (Field.LiteralOptions is not null)
                Options = new List<string>(Field.LiteralOptions);
            else if (DetailPath.Resolve(Source, Field.OptionsPath) is IEnumerable opts)
                // NT-058：索引通道也支持 `OptionsPath`（原来只认 `LiteralOptions` ⇒ 给了 OptionsPath 就**下拉空白**）
                Options = opts.Cast<object?>().Select(x => x?.ToString() ?? "").ToList();
        }

        var raw = DetailPath.Resolve(Source, Field.IndexPath);
        var index = -1;
        if (raw is not null)
        {
            try { index = System.Convert.ToInt32(raw, CultureInfo.InvariantCulture); }
            catch (Exception) { index = -1; }
        }
        SetProperty(ref _selectedIndex, index, nameof(SelectedIndex));
    }
}

/// <summary>只读列表（<see cref="FieldKind.List"/>）的一行。</summary>
public sealed class DetailListRow
{
    public string? Label { get; init; }
    public string? Text { get; init; }
    public string? Sub { get; init; }
    public string? Detail { get; init; }

    /// <summary>主文本与右侧强调值之间的固定符号（如 <c>"→"</c>），由所属字段统一下发（⛔ 模板里不写死）。</summary>
    public string? Separator { get; init; }

    /// <summary>项级命令（<c>ItemCommandPath</c> 非空时给，如「点一下打开这个资产」）；null = 这一行只读。</summary>
    public ICommand? Command { get; init; }

    /// <summary>项级命令的参数（<c>ItemCommandArgPath</c> 的解析结果；未给该路径时 = **候选项本身**）。</summary>
    public object? Argument { get; init; }

    /// <summary>是否有项级命令（模板据此加 <c>link</c> 样式类 = 悬停高亮 + 手型；⛔ 不加类就长得像普通只读行）。</summary>
    public bool HasCommand => Command is not null;

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasSub => !string.IsNullOrWhiteSpace(Sub);
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasSeparator => !string.IsNullOrWhiteSpace(Separator);
}

/// <summary>
/// <see cref="FieldKind.List"/>：只读列表（键值行 / 对比行）；配 <see cref="DetailField.ItemCommandPath"/> 时**每行可点击**。
/// <para>数据源复用 <see cref="DetailField.OptionsPath"/>，单项取数见 <c>Item*Path</c>（左标签 / 主文本 / 右侧强调值 / 第二行说明 / 项级命令）。</para>
/// </summary>
public sealed class ListFieldRow : DetailFieldRow
{
    public ListFieldRow(DetailField field, object? source) : base(field, source) { }

    public ObservableCollection<DetailListRow> Items { get; } = new();
    public bool HasItems => Items.Count > 0;

    protected override void RefreshCore()
    {
        var raw = DetailPath.Resolve(Source, Field.OptionsPath) as IEnumerable;
        var cmd = DetailPath.Resolve(Source, Field.ItemCommandPath) as ICommand;
        Items.Clear();
        if (raw is not null)
        {
            foreach (var item in raw)
            {
                Items.Add(new DetailListRow
                {
                    Label = DetailPath.Resolve(item, Field.ItemLabelPath)?.ToString(),
                    Text = DetailPath.Resolve(item, Field.ItemTextPath)?.ToString(),
                    Sub = DetailPath.Resolve(item, Field.ItemSubPath)?.ToString(),
                    Detail = DetailPath.Resolve(item, Field.ItemDetailPath)?.ToString(),
                    Separator = Field.ItemSeparator,
                    Command = cmd,
                    // 默认把候选项本身当参数（先例：OpenAssetCommand 收整项）
                    Argument = Field.ItemCommandArgPath is null ? item : DetailPath.Resolve(item, Field.ItemCommandArgPath)
                });
            }
        }
        OnPropertyChanged(nameof(HasItems));
    }
}

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

/// <summary>chips 的一个**分组**（NT-058：标签按命名空间分组显示时用）。</summary>
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
/// <para>NT-058：<see cref="DetailField.ChipGroupPath"/> 非空 ⇒ 按这个键**分组显示**（每组一个小标题 + 一组 chips）。</para>
/// </summary>
public sealed class ChipsFieldRow : DetailFieldRow
{
    private INotifyCollectionChanged? _watched;

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
        }

        Chips.Clear();
        Groups.Clear();

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
            }
        }
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsSwatch));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
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

/// <summary>
/// <see cref="FieldKind.IconBar"/> 里的一个图标按钮（NT-058）。
/// <para>
/// 图标由 <c>App.axaml</c> 的 <c>Icon*</c> <c>StreamGeometry</c> **按资源键**解析成 <see cref="Avalonia.Media.Geometry"/>
/// （模板里 <c>Path.Data</c> 直接绑它 —— XAML 写不出动态资源键，所以这一步只能在 C# 侧做）。
/// </para>
/// </summary>
public sealed class DetailIconActionRender : ObservableObject
{
    private readonly DetailIconAction _d;
    private readonly object? _source;
    private readonly ICommand? _command;

    public DetailIconActionRender(DetailIconAction action, object? source)
    {
        _d = action;
        _source = source;
        Icon = ResolveIcon(action.IconKey);

        var inner = DetailPath.Resolve(source, action.CommandPath) as ICommand;
        if (inner is not null && !string.IsNullOrWhiteSpace(action.ConfirmText))
        {
            var message = action.ConfirmText!;
            _command = new AsyncRelayCommand(async () =>
            {
                if (await DialogHooks.ConfirmAsync(message).ConfigureAwait(true)) inner.Execute(null);
            });
        }
        else
        {
            _command = inner;
        }
    }

    public Avalonia.Media.Geometry? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string? Text => _d.Text;
    public bool HasText => !string.IsNullOrWhiteSpace(_d.Text);
    public string? ToolTip => _d.ToolTip;
    public bool IsDanger => _d.IsDanger;
    public bool IsSeparator => _d.IsSeparator;
    public ICommand? Command => _command;

    private bool _isVisible = true;
    private bool _isEnabled = true;
    private bool _isActive;

    public bool IsVisible { get => _isVisible; private set => SetProperty(ref _isVisible, value); }
    public bool IsEnabled { get => _isEnabled; private set => SetProperty(ref _isEnabled, value); }
    public bool IsActive { get => _isActive; private set => SetProperty(ref _isActive, value); }

    public void Refresh()
    {
        IsVisible = _d.IsVisiblePath is null || DetailPath.ResolveBool(_source, _d.IsVisiblePath);
        IsActive = _d.IsActivePath is not null && DetailPath.ResolveBool(_source, _d.IsActivePath);
        IsEnabled = (_d.IsEnabledPath is null || DetailPath.ResolveBool(_source, _d.IsEnabledPath))
                    && (_command is null || _command.CanExecute(null));
    }

    /// <summary>
    /// 按资源键取图标（<c>App.axaml</c> 的 <c>Icon*</c>）。
    /// ⚠️ Avalonia 12 的 <c>TryFindResource</c> 是 **Try 模式**（<c>out</c> 参数），且是 <c>IResourceHost</c> 的扩展方法。
    /// </summary>
    private static Avalonia.Media.Geometry? ResolveIcon(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var app = Avalonia.Application.Current;
        if (app is null) return null;
        return app.TryFindResource(key!, out var value) && value is Avalonia.Media.Geometry g ? g : null;
    }
}

/// <summary>
/// <see cref="FieldKind.IconBar"/>：一行图标动作（32×32 方形小按钮，溢出换行）。
/// <para>成员来自 <see cref="DetailField.IconActions"/>（顺序即渲染顺序、数量不限）；同一张卡可以出现多组。</para>
/// </summary>
public sealed class IconBarFieldRow : DetailFieldRow
{
    public IconBarFieldRow(DetailField field, object? source) : base(field, source)
    {
        foreach (var a in field.IconActions) Items.Add(new DetailIconActionRender(a, source));
    }

    public ObservableCollection<DetailIconActionRender> Items { get; } = new();
    public bool HasItems => Items.Count > 0;

    protected override void RefreshCore()
    {
        foreach (var i in Items) i.Refresh();
        OnPropertyChanged(nameof(HasItems));
    }
}

/// <summary>分区渲染对象：标题（可点折叠）+ 可选标题勾选框 + 字段列表。</summary>
public sealed class DetailSectionRender : ObservableObject
{
    public DetailSectionRender(DetailSection section, object? source)
    {
        Section = section;
        Source = source;
        ToggleExpandCommand = new RelayCommand(ToggleExpand);
        foreach (var f in section.Fields) Fields.Add(CreateRow(f, source));
    }

    private DetailSection Section { get; }
    private object? Source { get; }

    public List<DetailFieldRow> Fields { get; } = new();

    public string? Header => Section.Header;
    public bool HasHeader => !string.IsNullOrWhiteSpace(Section.Header);

    public bool IsCollapsible => !string.IsNullOrEmpty(Section.IsExpandedPath);

    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    public bool HasToggle => !string.IsNullOrEmpty(Section.TogglePath);

    private bool _toggleChecked;
    public bool ToggleChecked
    {
        get => _toggleChecked;
        set
        {
            if (!SetProperty(ref _toggleChecked, value)) return;
            DetailPath.Write(Source, Section.TogglePath, value);
        }
    }

    private bool _toggleEnabled = true;

    /// <summary>标题勾选框是否可用（<c>ToggleEnabledPath</c>，如「有选中项才能改类型」）。</summary>
    public bool ToggleEnabled { get => _toggleEnabled; private set => SetProperty(ref _toggleEnabled, value); }

    public ICommand ToggleExpandCommand { get; }

    private void ToggleExpand() => DetailPath.Write(Source, Section.IsExpandedPath, !IsExpanded);

    public void Refresh()
    {
        if (IsCollapsible) SetProperty(ref _isExpanded, DetailPath.ResolveBool(Source, Section.IsExpandedPath), nameof(IsExpanded));
        if (HasToggle) SetProperty(ref _toggleChecked, DetailPath.ResolveBool(Source, Section.TogglePath), nameof(ToggleChecked));
        ToggleEnabled = Section.ToggleEnabledPath is null || DetailPath.ResolveBool(Source, Section.ToggleEnabledPath);
        foreach (var f in Fields) f.Refresh();
    }

    /// <summary>Kind → 行对象（唯一分派点；加新 Kind 只在这里加一个 case + 一个模板）。</summary>
    private static DetailFieldRow CreateRow(DetailField f, object? source) => f.Kind switch
    {
        FieldKind.Check => new CheckFieldRow(f, source),
        // Select 有两个落值通道：值（默认）/ 索引（页面 VM 上本来就是"第几个选项"）→ 两个行对象、两个模板
        FieldKind.Select => f.IndexPath is null ? new SelectFieldRow(f, source) : new IndexSelectFieldRow(f, source),
        FieldKind.Number => new NumberFieldRow(f, source),
        FieldKind.Chips => new ChipsFieldRow(f, source),
        FieldKind.Hint => new HintFieldRow(f, source),
        FieldKind.Divider => new DividerFieldRow(f, source),
        FieldKind.List => new ListFieldRow(f, source),
        FieldKind.IconBar => new IconBarFieldRow(f, source),
        _ => new TextFieldRow(f, source)
    };
}

/// <summary>动作区的一个按钮（<c>ConfirmText</c> 非空时包一层二次确认）。</summary>
public sealed class DetailActionRender : ObservableObject
{
    private readonly ICommand? _command;
    private readonly object? _source;

    public DetailActionRender(DetailAction action, object? source)
    {
        Descriptor = action;
        _source = source;

        var inner = action.Command;
        if (inner is not null && !string.IsNullOrWhiteSpace(action.ConfirmText))
        {
            var message = action.ConfirmText!;
            _command = new AsyncRelayCommand(async () =>
            {
                if (await DialogHooks.ConfirmAsync(message).ConfigureAwait(true)) inner.Execute(null);
            });
        }
        else
        {
            _command = inner;
        }
    }

    private DetailAction Descriptor { get; }

    public ICommand? Command => _command;
    public string? Text => Descriptor.Text;
    public string? ToolTip => Descriptor.ToolTip;
    public bool IsPrimary => string.Equals(Descriptor.Classes, "primary", StringComparison.OrdinalIgnoreCase);
    public bool IsGhost => string.Equals(Descriptor.Classes, "ghost", StringComparison.OrdinalIgnoreCase);
    public bool IsDanger => string.Equals(Descriptor.Classes, "danger-btn", StringComparison.OrdinalIgnoreCase);
    public HorizontalAlignment Alignment => Descriptor.Stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

    private bool _isVisible = true;
    private bool _isEnabled = true;

    public bool IsVisible { get => _isVisible; private set => SetProperty(ref _isVisible, value); }
    public bool IsEnabled { get => _isEnabled; private set => SetProperty(ref _isEnabled, value); }

    public void Refresh()
    {
        IsVisible = Descriptor.IsVisiblePath is null || DetailPath.ResolveBool(_source, Descriptor.IsVisiblePath);
        IsEnabled = (Descriptor.IsEnabledPath is null || DetailPath.ResolveBool(_source, Descriptor.IsEnabledPath))
                    && (_command is null || _command.CanExecute(null));
    }
}

/// <summary>
/// 整卡渲染对象：<see cref="DetailCardHost"/> 的 DataContext。
/// 订阅源 VM 的 <see cref="INotifyPropertyChanged"/>，任何变化整体重解析（属性级刷新契约）。
/// </summary>
public sealed class DetailCardRenderer : ObservableObject, IDisposable
{
    private readonly object? _source;
    private readonly INotifyPropertyChanged? _sourceNotifier;
    private readonly string? _bodyPath;

    public DetailCardRenderer(DetailCardDescriptor card, object? source)
    {
        _source = source;
        _bodyPath = card.BodyVisiblePath;
        EmptyText = card.EmptyText;

        foreach (var s in card.Sections) Sections.Add(new DetailSectionRender(s, source));
        foreach (var a in card.Actions) Actions.Add(new DetailActionRender(a, source));

        _sourceNotifier = source as INotifyPropertyChanged;
        if (_sourceNotifier is not null) _sourceNotifier.PropertyChanged += OnSourceChanged;

        Refresh();
    }

    public ObservableCollection<DetailSectionRender> Sections { get; } = new();
    public ObservableCollection<DetailActionRender> Actions { get; } = new();

    public bool HasSections => Sections.Count > 0;
    public bool HasActions => Actions.Count > 0;
    public string? EmptyText { get; }

    private bool _showBody = true;

    /// <summary>详情正文是否可见（空态之外的正常态）。</summary>
    public bool ShowBody { get => _showBody; private set => SetProperty(ref _showBody, value); }

    /// <summary>源 VM 任一属性变化 → 整体重解析。</summary>
    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>重解析全部路径（可见性 / 各字段值 / 动作可用性）。</summary>
    public void Refresh()
    {
        ShowBody = _bodyPath is null || DetailPath.ResolveBool(_source, _bodyPath);
        foreach (var s in Sections) s.Refresh();
        foreach (var a in Actions) a.Refresh();
    }

    /// <summary>宿主换卡片时调：退订源 VM（防内存泄漏）。</summary>
    public void Dispose()
    {
        if (_sourceNotifier is not null) _sourceNotifier.PropertyChanged -= OnSourceChanged;
    }
}
