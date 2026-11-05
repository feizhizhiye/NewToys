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


/// <summary>FieldKind.IconBar 里的一个图标按钮。</summary>
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
                if (await DetailCardHooks.ConfirmAsync(message).ConfigureAwait(true)) inner.Execute(null);
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

    private bool _isVisible = true;

    /// <summary>整节是否可见（<see cref="DetailSection.VisiblePath"/>；null = 恒可见）。</summary>
    public bool IsVisible { get => _isVisible; private set => SetProperty(ref _isVisible, value); }

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
        IsVisible = Section.VisiblePath is null || DetailPath.ResolveBool(Source, Section.VisiblePath);
        if (IsCollapsible) SetProperty(ref _isExpanded, DetailPath.ResolveBool(Source, Section.IsExpandedPath), nameof(IsExpanded));
        if (HasToggle) SetProperty(ref _toggleChecked, DetailPath.ResolveBool(Source, Section.TogglePath), nameof(ToggleChecked));
        ToggleEnabled = Section.ToggleEnabledPath is null || DetailPath.ResolveBool(Source, Section.ToggleEnabledPath);
        foreach (var f in Fields) f.Refresh();
    }

    /// <summary>Kind → 行对象（唯一分派点；加新 Kind 只在这里加一个 case + 一个模板）。</summary>
    private static DetailFieldRow CreateRow(DetailField f, object? source) => f.Kind switch
    {
        FieldKind.Check => new CheckFieldRow(f, source),
        FieldKind.Pair => new PairFieldRow(f, source),
        // Select 有两个落值通道：值（默认）/ 索引（页面 VM 上本来就是"第几个选项"）→ 两个行对象、两个模板
        FieldKind.Select => f.IndexPath is null ? new SelectFieldRow(f, source) : new IndexSelectFieldRow(f, source),
        FieldKind.Number => new NumberFieldRow(f, source),
        FieldKind.Chips => new ChipsFieldRow(f, source),
        FieldKind.Hint => new HintFieldRow(f, source),
        FieldKind.Divider => new DividerFieldRow(f, source),
        FieldKind.List => new ListFieldRow(f, source),
        FieldKind.IconBar => new IconBarFieldRow(f, source),
        FieldKind.EditableList => new EditableListFieldRow(f, source),
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
                if (await DetailCardHooks.ConfirmAsync(message).ConfigureAwait(true)) inner.Execute(null);
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

    /// <summary>动作图标；无键或键不存在 ⇒ null（按钮只显示文字）。</summary>
    public Avalonia.Media.Geometry? Icon => Resolve(Descriptor.IconKey);

    public bool HasIcon => Icon is not null;

    private static Avalonia.Media.Geometry? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var app = Avalonia.Application.Current;
        if (app is null) return null;
        return app.TryFindResource(key!, out var value) && value is Avalonia.Media.Geometry g ? g : null;
    }
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

