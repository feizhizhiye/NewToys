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

/// <summary>FieldKind.EditableList：可编辑列表（每行 = 只读左标签 + 可编辑文本框，失焦提交）。</summary>
public sealed class EditableListFieldRow : DetailFieldRow
{
    private INotifyCollectionChanged? _watched;

    public EditableListFieldRow(DetailField field, object? source) : base(field, source) { }

    public ObservableCollection<EditableListItemRow> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    protected override void RefreshCore()
    {
        var raw = DetailPath.Resolve(Source, Field.OptionsPath) as IEnumerable;
        var cmd = DetailPath.Resolve(Source, Field.ItemCommandPath) as ICommand;

        // 候选集合换了 / 被重建（切节、列变化）⇒ 跟着刷新（否则卡里停留在上一节的列）
        if (raw is INotifyCollectionChanged ncc && !ReferenceEquals(ncc, _watched))
        {
            if (_watched is not null) _watched.CollectionChanged -= OnSourceCollectionChanged;
            _watched = ncc;
            _watched.CollectionChanged += OnSourceCollectionChanged;
        }

        Items.Clear();
        if (raw is not null)
        {
            foreach (var item in raw)
                Items.Add(new EditableListItemRow(
                    item,
                    DetailPath.Resolve(item, Field.ItemLabelPath)?.ToString(),
                    DetailPath.Resolve(item, Field.ItemTextPath)?.ToString(),
                    Field.ItemTextPath,
                    cmd));
        }
        OnPropertyChanged(nameof(HasItems));
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
}

/// <summary>可编辑列表的一行：左标签 + 文本框；<see cref="Commit"/> 由失焦触发。</summary>
public sealed class EditableListItemRow : ObservableObject
{
    private readonly object? _item;
    private readonly string? _textPath;
    private readonly ICommand? _command;
    private string _value = "";
    private bool _dirty;

    public EditableListItemRow(object? item, string? label, string? value, string? textPath, ICommand? command)
    {
        _item = item;
        _textPath = textPath;
        _command = command;
        _value = value ?? "";
        Label = label;
    }

    /// <summary>只读左标签（列 key / 属性名）。</summary>
    public string? Label { get; }

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>可编辑文本；改的时候只留在本地，失焦才写回。</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value ?? "")) return;
            _dirty = true;
        }
    }

    /// <summary>失焦提交：只写回真正改过的项（<see cref="_dirty"/> 为空时直接跳过，避免无谓重投影）。</summary>
    public void Commit()
    {
        if (!_dirty || _item is null) return;
        _dirty = false;

        DetailPath.Write(_item, _textPath, _value);
        if (_command is not null && _command.CanExecute(_item)) _command.Execute(_item);
    }
}

