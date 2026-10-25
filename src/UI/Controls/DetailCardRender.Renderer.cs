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

