using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
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
    public double BoxMinHeight => Field.MultiLine ? (Field.Rows > 0 ? Field.Rows * 18 : 52) : 0;
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

    private DetailOption? _selected;
    public DetailOption? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            DetailPath.Write(Source, Field.ValuePath,
                Field.OptionValuePath is null ? value?.Source : DetailPath.Resolve(value?.Source, Field.OptionValuePath));
        }
    }

    public string? Placeholder => Field.Placeholder;

    protected override void RefreshCore()
    {
        var raw = DetailPath.Resolve(Source, Field.OptionsPath) as IEnumerable;
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

        var cur = DetailPath.Resolve(Source, Field.ValuePath);
        DetailOption? hit = null;
        foreach (var o in list)
        {
            var ov = Field.OptionValuePath is null ? o.Source : DetailPath.Resolve(o.Source, Field.OptionValuePath);
            if (DetailPath.LooseEquals(ov, cur)) { hit = o; break; }
        }
        SetProperty(ref _selected, hit, nameof(Selected));
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

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary><see cref="FieldKind.Chips"/>：集合编辑器（标签：点一下打上 / 取消）。</summary>
public sealed class ChipsFieldRow : DetailFieldRow
{
    private INotifyCollectionChanged? _watched;

    public ChipsFieldRow(DetailField field, object? source) : base(field, source) { }

    public ObservableCollection<DetailChip> Chips { get; } = new();
    public bool HasChips => Chips.Count > 0;

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
        if (raw is not null)
        {
            foreach (var item in raw)
            {
                var text = Field.ChipTextPath is null
                    ? item?.ToString()
                    : DetailPath.Resolve(item, Field.ChipTextPath)?.ToString();
                Chips.Add(new DetailChip
                {
                    Source = item,
                    Text = text ?? "",
                    Command = cmd,
                    Argument = item,
                    IsSelected = DetailPath.ResolveBool(item, Field.ChipSelectedPath)
                });
            }
        }
        OnPropertyChanged(nameof(HasChips));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
}

/// <summary><see cref="FieldKind.Hint"/>：纯提示文字（含空态）。</summary>
public sealed class HintFieldRow : DetailFieldRow
{
    public HintFieldRow(DetailField field, object? source) : base(field, source) { }

    /// <summary>提示正文：Kind=Hint 时 <c>Label</c> 也当正文用（少写一个属性）。</summary>
    public string? Text => Field.Hint ?? Field.Label;
}

/// <summary><see cref="FieldKind.Divider"/>：分隔线。</summary>
public sealed class DividerFieldRow : DetailFieldRow
{
    public DividerFieldRow(DetailField field, object? source) : base(field, source) { }
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

    public ICommand ToggleExpandCommand { get; }

    private void ToggleExpand() => DetailPath.Write(Source, Section.IsExpandedPath, !IsExpanded);

    public void Refresh()
    {
        if (IsCollapsible) SetProperty(ref _isExpanded, DetailPath.ResolveBool(Source, Section.IsExpandedPath), nameof(IsExpanded));
        if (HasToggle) SetProperty(ref _toggleChecked, DetailPath.ResolveBool(Source, Section.TogglePath), nameof(ToggleChecked));
        foreach (var f in Fields) f.Refresh();
    }

    /// <summary>Kind → 行对象（唯一分派点；加新 Kind 只在这里加一个 case + 一个模板）。</summary>
    private static DetailFieldRow CreateRow(DetailField f, object? source) => f.Kind switch
    {
        FieldKind.Check => new CheckFieldRow(f, source),
        FieldKind.Select => new SelectFieldRow(f, source),
        FieldKind.Number => new NumberFieldRow(f, source),
        FieldKind.Chips => new ChipsFieldRow(f, source),
        FieldKind.Hint => new HintFieldRow(f, source),
        FieldKind.Divider => new DividerFieldRow(f, source),
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
    private readonly string? _samplePath;

    public DetailCardRenderer(DetailCardDescriptor card, object? source)
    {
        _source = source;
        _bodyPath = card.BodyVisiblePath;
        _samplePath = card.SamplePath;
        EmptyText = card.EmptyText;
        SampleText = card.SampleText;

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
    public string? SampleText { get; }
    public bool HasSampleText => !string.IsNullOrWhiteSpace(SampleText);

    private bool _showBody = true;
    private bool _isSample;

    /// <summary>详情正文是否可见（空态 / 示例态之外的正常态）。</summary>
    public bool ShowBody { get => _showBody; private set => SetProperty(ref _showBody, value); }

    /// <summary>示例态（v3.32 §4.8.3）：整卡 <c>IsEnabled=false</c>，⛔ 不让用户改 / 保存一份假数据。</summary>
    public bool IsSample { get => _isSample; private set => SetProperty(ref _isSample, value); }

    /// <summary>源 VM 任一属性变化 → 整体重解析。</summary>
    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>重解析全部路径（可见性 / 示例态 / 各字段值 / 动作可用性）。</summary>
    public void Refresh()
    {
        ShowBody = _bodyPath is null || DetailPath.ResolveBool(_source, _bodyPath);
        IsSample = _samplePath is not null && DetailPath.ResolveBool(_source, _samplePath);
        foreach (var s in Sections) s.Refresh();
        foreach (var a in Actions) a.Refresh();
    }

    /// <summary>宿主换卡片时调：退订源 VM（防内存泄漏）。</summary>
    public void Dispose()
    {
        if (_sourceNotifier is not null) _sourceNotifier.PropertyChanged -= OnSourceChanged;
    }
}
