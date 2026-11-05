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


/// <summary><see cref="FieldKind.Text"/>：单行 / 多行 / 只读可复制 / 密码。</summary>
public sealed class TextFieldRow : DetailFieldRow
{
    public TextFieldRow(DetailField field, object? source) : base(field, source) { }

    public bool IsSelectable => Field.Selectable;
    public bool IsEditable => !Field.Selectable;
    public bool MultiLine => Field.MultiLine;
    public bool Mono => Field.Mono;
    /// <summary>行内排版（<see cref="DetailField.Inline"/>）：模板据此切换「上下两行 / 一行标签：值」。</summary>
    public bool IsInline => Field.Inline;
    public char PasswordChar => Field.Password ? '●' : '\0';
    public double BoxMinHeight => Field.MultiLine ? (Field.Rows > 0 ? Field.Rows : 52) : 0;
    public string? Placeholder => Field.Placeholder;

    /// <summary>输入框右侧只读后缀（取 <see cref="DetailField.SuffixPath"/>）。</summary>
    public string Suffix => _suffix ?? "";
    public bool HasSuffix => Suffix.Length > 0;

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

    private string? _suffix = "";

    // ⛔ null 归一成空串：VM 值为 null 时反复在 null ⇄ "" 之间推送会让空输入框闪（Avalonia 重新排版文本）。
    protected override void RefreshCore()
    {
        SetProperty(ref _text, DetailPath.Resolve(Source, Field.ValuePath)?.ToString() ?? "", nameof(Text));
        var suffix = string.IsNullOrEmpty(Field.SuffixPath)
            ? ""
            : DetailPath.Resolve(Source, Field.SuffixPath!)?.ToString() ?? "";
        if (SetProperty(ref _suffix, suffix, nameof(Suffix))) OnPropertyChanged(nameof(HasSuffix));
    }
}

/// <summary>FieldKind.Pair：成对输入—— 一行两个输入框 + 中间固定符号。</summary>
public sealed class PairFieldRow : DetailFieldRow
{
    public PairFieldRow(DetailField field, object? source) : base(field, source) { }

    public bool Mono => Field.Mono;
    public string? FirstPlaceholder => Field.Placeholder;
    public string? SecondPlaceholder => Field.SecondPlaceholder;
    public string Separator => string.IsNullOrEmpty(Field.PairSeparator) ? "×" : Field.PairSeparator;

    /// <summary>OnBlur 语义：写回交给模板的 <c>LostFocus</c>（<c>OnPairLostFocus</c>）。</summary>
    public bool CommitOnBlur => CommitMode == EditCommitMode.OnBlur;

    private string? _first;
    public string? First
    {
        get => _first;
        set
        {
            if (!SetProperty(ref _first, value)) return;
            if (!CommitOnBlur) DetailPath.Write(Source, Field.ValuePath, value);
        }
    }

    private string? _second;
    public string? Second
    {
        get => _second;
        set
        {
            if (!SetProperty(ref _second, value)) return;
            if (!CommitOnBlur) DetailPath.Write(Source, Field.SecondValuePath, value);
        }
    }

    /// <summary>失焦提交（两框一起写）。</summary>
    public void Commit()
    {
        if (!CommitOnBlur) return;
        DetailPath.Write(Source, Field.ValuePath, _first);
        DetailPath.Write(Source, Field.SecondValuePath, _second);
    }

    protected override void RefreshCore()
    {
        // null 归一成空串（理由同 TextFieldRow.RefreshCore）
        SetProperty(ref _first, DetailPath.Resolve(Source, Field.ValuePath)?.ToString() ?? "", nameof(First));
        SetProperty(ref _second, DetailPath.Resolve(Source, Field.SecondValuePath)?.ToString() ?? "", nameof(Second));
    }
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

    /// <summary>行内排版（Inline=true）：一行「标签：下拉」，短选项省竖向空间。</summary>
    public bool IsInline => Field.Inline;


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

    /// <summary>行内排版（Inline=true）：一行「标签：下拉」，短选项省竖向空间。</summary>
    public bool IsInline => Field.Inline;

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
                // 索引通道也认 `OptionsPath`；⛔ 只处理 `LiteralOptions` 会让给了 OptionsPath 的下拉**空白**
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

