using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.ScriptManager.Models;
using NewToys.ScriptManager.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.ScriptManager.ViewModels;

/// <summary>
/// 「脚本管理器 → 集合」页（NT-068 §三.7）：<b>主从两栏</b> —— 左 280px 集合列表 + 右成员表；
/// 详情卡走标准右侧声明式卡（<see cref="IDetailCardSource"/>，与全应用一致）。
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>成员关系存在条目侧</b>（<see cref="ScriptItem.Collections"/>）：右栏是「全库清单 + 勾选」——
/// 勾 = 在集合内、取消勾 = 移出，<b>即时生效、无保存按钮</b>。⛔ 集合侧不存成员 id 列表。
/// </para>
/// <para>
/// ⛔ 本期不做层级分组（几十个集合靠「搜索 + 分组下拉 + 类型 / 标签筛选」扛，卡 §三.7）。
/// </para>
/// </remarks>
public partial class CollectionViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly IScriptStoreService? _store;
    private readonly IQuickActionService? _svc;
    private readonly INavigationService? _nav;
    private readonly IScriptManagerSettingsService? _settings;

    /// <summary>批量改属性时压住「回写 + 落盘」（初始化 / 切换集合时）。</summary>
    private bool _loading;

    /// <summary>压住成员勾选的联动（重建成员表时）。</summary>
    private bool _suppressMember;

    public CollectionViewModel() { }

    public CollectionViewModel(IScriptStoreService store, IQuickActionService svc, INavigationService nav,
        IScriptManagerSettingsService settings)
    {
        _store = store;
        _svc = svc;
        _nav = nav;
        _settings = settings;

        Members.CollectionChanged += OnMembersChanged;
        RefreshCollections();
    }

    // ==================== 左栏：集合列表 ====================

    public ObservableCollection<CollectionRow> Collections { get; } = new();

    [ObservableProperty] private CollectionRow? _selectedCollection;

    /// <summary>左栏搜索（名称 / 描述 / 标签）。</summary>
    [ObservableProperty] private string _collectionSearch = "";

    /// <summary>左栏分组：全部 / 置顶 / 最近使用。</summary>
    public ObservableCollection<CollectionFilterOption> GroupOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "全部", Value = "" },
        new CollectionFilterOption { Label = "置顶", Value = "pinned" },
        new CollectionFilterOption { Label = "最近使用", Value = "recent" },
    });

    [ObservableProperty] private CollectionFilterOption? _selectedGroup;

    /// <summary>左栏类型筛选（集合类别）。</summary>
    public ObservableCollection<CollectionFilterOption> KindFilterOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "全部类型", Value = "" },
        new CollectionFilterOption { Label = "信息面板", Value = ScriptCollectionKind.Info },
        new CollectionFilterOption { Label = "脚本库", Value = ScriptCollectionKind.Script },
        new CollectionFilterOption { Label = "命令库", Value = ScriptCollectionKind.Command },
        new CollectionFilterOption { Label = "软件包", Value = ScriptCollectionKind.Package },
    });

    [ObservableProperty] private CollectionFilterOption? _selectedKindFilter;

    public bool HasCollections => Collections.Count > 0;

    partial void OnCollectionSearchChanged(string value) => RefreshCollections();
    partial void OnSelectedGroupChanged(CollectionFilterOption? value) => RefreshCollections();
    partial void OnSelectedKindFilterChanged(CollectionFilterOption? value) => RefreshCollections();

    partial void OnSelectedCollectionChanged(CollectionRow? value)
    {
        LoadMemberTable();
        LoadDetailEdits();
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==================== 右栏：成员表 ====================

    public ObservableCollection<MemberRow> Members { get; } = new();

    /// <summary>成员搜索。</summary>
    [ObservableProperty] private string _memberSearch = "";

    /// <summary>
    /// 「显示不可采集」（卡 §三.5）：默认只列可采集项；打开后不可采集项灰显、⛔ 勾不上。
    /// </summary>
    [ObservableProperty] private bool _showNonCollectable;

    partial void OnMemberSearchChanged(string value) => LoadMemberTable();
    partial void OnShowNonCollectableChanged(bool value) => LoadMemberTable();

    public bool HasMembers => Members.Count > 0;

    // ==================== 详情卡编辑缓冲 ====================

    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editIcon = "";
    [ObservableProperty] private string _editKind = ScriptCollectionKind.Info;
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private string _editRefreshMode = ScriptRefreshMode.OnEnter;

    public ObservableCollection<CollectionFilterOption> KindOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "信息面板", Value = ScriptCollectionKind.Info },
        new CollectionFilterOption { Label = "脚本库", Value = ScriptCollectionKind.Script },
        new CollectionFilterOption { Label = "命令库", Value = ScriptCollectionKind.Command },
        new CollectionFilterOption { Label = "软件包", Value = ScriptCollectionKind.Package },
    });

    public ObservableCollection<CollectionFilterOption> ViewOptions { get; } = new()
    {
        new CollectionFilterOption { Label = "默认视图", Value = "" },
    };

    [ObservableProperty] private CollectionFilterOption? _selectedKindOption;
    [ObservableProperty] private CollectionFilterOption? _selectedViewOption;

    public ObservableCollection<CollectionFilterOption> RefreshOptions { get; } = new(new[]
    {
        new CollectionFilterOption { Label = "进页面刷新", Value = ScriptRefreshMode.OnEnter },
        new CollectionFilterOption { Label = "只手动刷新", Value = ScriptRefreshMode.Manual },
        new CollectionFilterOption { Label = "定时刷新", Value = ScriptRefreshMode.Timer },
    });

    [ObservableProperty] private CollectionFilterOption? _selectedRefreshOption;

    partial void OnSelectedKindOptionChanged(CollectionFilterOption? value)
    {
        if (value == null) return;
        EditKind = value.Value;
    }

    partial void OnSelectedRefreshOptionChanged(CollectionFilterOption? value)
    {
        if (value == null) return;
        EditRefreshMode = value.Value;
    }

    partial void OnSelectedViewOptionChanged(CollectionFilterOption? value) => ApplyCollectionEdits();

    partial void OnEditKindChanged(string value) => ApplyCollectionEdits();
    partial void OnEditRefreshModeChanged(string value) => ApplyCollectionEdits();

    // ==================== 列表基础设施（ListPageViewModelBase 抽象成员） ====================

    protected override string PageKey => "script-collections";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Member", "加入", "0.7*", sortable: false),
        new("Title", "名称", "3*", sample: "磁盘使用"),
        new("Role", "类型", "1*", sample: "脚本"),
        new("Target", "目标", "4*", sortable: false, sample: "sysinfo/disk.ps1"),
    };

    public bool IsMemberVisible => true;
    public bool IsTitleVisible => true;
    public bool IsRoleVisible => true;
    public bool IsTargetVisible => true;

    protected override void RefreshView() => RefreshCollections();

    protected override string? FieldText(object row, string columnKey)
    {
        if (row is not MemberRow m) return base.FieldText(row, columnKey);
        return columnKey switch
        {
            "Title" => m.Title,
            "Role" => m.RoleText,
            "Target" => m.Target,
            _ => base.FieldText(row, columnKey),
        };
    }

    // ==================== 详情卡（标准声明式） ====================

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    public bool HasDetailPanel => true;
    public bool IsDetailOpen => IsDetailVisible;
    public object? DetailContent => null;

    public event EventHandler? DetailChanged;

    [RelayCommand]
    private void ToggleDetailPanel()
    {
        ToggleDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenDetailPanel()
    {
        ShowDetailCommand.Execute(null);
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    void IDetailPanelProvider.ToggleDetail() => ToggleDetailPanelCommand.Execute(null);
    void IDetailPanelProvider.OpenDetail() => OpenDetailPanelCommand.Execute(null);

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(HasSelectedCollection),
        EmptyText = "在左侧选中一个集合，可在这里改名称 / 图标 / 类别 / 描述 / 刷新策略 / 标签。集合只是「一组采集器的容器」，本身不跑任何东西。",
        Sections =
        {
            new DetailSection
            {
                Header = "集合",
                Fields =
                {
                    new DetailField
                    {
                        Label = "名称", ValuePath = nameof(EditName), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField
                    {
                        Label = "图标", ValuePath = nameof(EditIcon), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur, Placeholder = "一个 emoji 或字符"
                    },
                    new DetailField
                    {
                        Label = "类别", Kind = FieldKind.Select,
                        OptionsPath = nameof(KindOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        ValuePath = nameof(SelectedKindOption)
                    },
                    new DetailField
                    {
                        Label = "描述", ValuePath = nameof(EditDescription), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur, MultiLine = true, Rows = 52
                    },
                    new DetailField
                    {
                        Label = "默认视图", Kind = FieldKind.Select,
                        OptionsPath = nameof(ViewOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        ValuePath = nameof(SelectedViewOption)
                    },
                    new DetailField
                    {
                        Label = "刷新策略", Kind = FieldKind.Select,
                        OptionsPath = nameof(RefreshOptions), OptionDisplayPath = nameof(CollectionFilterOption.Label),
                        ValuePath = nameof(SelectedRefreshOption)
                    },
                    new DetailField
                    {
                        Label = "标签（逗号分隔）", ValuePath = nameof(EditTags), Kind = FieldKind.Text,
                        Commit = EditCommitMode.OnBlur
                    },
                    new DetailField { Kind = FieldKind.Divider },
                    new DetailField { Kind = FieldKind.Hint, ValuePath = nameof(CollectionHint) },
                }
            }
        }
    };

    public bool HasSelectedCollection => SelectedCollection != null;

    public string CollectionHint => SelectedCollection == null
        ? ""
        : $"成员 {SelectedCollection.MemberCount} 个 · 内置集合"
          + (SelectedCollection.Builtin ? "（可改名，⛔ 不可删）" : "")
          + $" · 落盘 {_store?.FilePath ?? ""}";

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => new ToolbarItemDescriptor[]
    {
        new() { Text = "➕ 新建集合", Classes = "ghost", CommandPath = nameof(NewCollectionCommand), ToolTip = "新建一个空集合，然后在右栏勾选成员" },
        new() { Text = "⧉ 复制", Classes = "ghost", CommandPath = nameof(DuplicateCollectionCommand) },
        new() { Text = "🗑 删除", Classes = "danger-btn", CommandPath = nameof(DeleteCollectionCommand), ToolTip = "内置集合不可删除" },
        new() { Text = "📦 归档", Classes = "ghost", CommandPath = nameof(ArchiveCollectionCommand), ToolTip = "归档后默认不显示，可从「全部」里找回" },
        new() { Text = "🔍 扫描目录", Classes = "ghost", CommandPath = nameof(ScanIntoCollectionCommand), ToolTip = "扫描脚本目录，把新脚本入库并加入当前集合" },
        new() { Text = "📤 发送到信息面板", Classes = "ghost", CommandPath = nameof(SendToInfoPanelCommand), ToolTip = "选中这个集合并跳到信息面板" },
        new() { Text = "📤 导出", Classes = "ghost", CommandPath = nameof(ExportCommand), ToolTip = "把集合定义导出为 json" },
        new()
        {
            Kind = ToolbarItemKind.TextField, Width = 96, Placeholder = "搜索成员…",
            ValuePath = nameof(MemberSearch)
        },
        new()
        {
            Kind = ToolbarItemKind.Toggle, Text = "显示不可采集",
            ValuePath = nameof(ShowNonCollectable),
            ToolTip = "默认只列可采集条目；打开后不可采集项灰显（⛔ 勾不上）"
        },
    };

    // ==================== 数据加载 ====================

    private void RefreshCollections()
    {
        if (_store == null) return;
        _loading = true;

        var keepId = SelectedCollection?.Id;
        var keyword = (CollectionSearch ?? "").Trim();
        var kindFilter = SelectedKindFilter?.Value ?? "";
        var group = SelectedGroup?.Value ?? "";

        var rows = _store.Data.Collections
            .Where(c => kindFilter.Length == 0 || ScriptCollectionKind.Normalize(c.Kind) == kindFilter)
            .Where(c => keyword.Length == 0 || Matches(c, keyword))
            .Where(c => group != "pinned" || c.Pinned)
            .OrderBy(c => c.Archived)                          // 归档的排最后（仍可见，方便取消归档）
            .ThenByDescending(c => c.Pinned)
            .ThenByDescending(c => SafeDate(c.LastOpenedAt))
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (group == "recent")
            rows = rows.OrderByDescending(c => SafeDate(c.LastOpenedAt)).ToList();

        Collections.Clear();
        foreach (var c in rows)
        {
            var row = new CollectionRow(c) { MemberCount = _store.ItemsOf(c.Id).Count };
            Collections.Add(row);
        }

        SelectedCollection = Collections.FirstOrDefault(r => r.Id == keepId) ?? Collections.FirstOrDefault();
        _loading = false;

        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(CollectionHint));
    }

    private static bool Matches(ScriptCollection c, string keyword)
        => (c.Name ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
           || (c.Description ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
           || (c.Tags ?? new List<string>()).Any(t => t.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

    private static DateTime SafeDate(string? iso)
        => DateTime.TryParse(iso, out var dt) ? dt : DateTime.MinValue;

    /// <summary>重建右栏成员表：全库条目 + 是否属于当前集合。</summary>
    private void LoadMemberTable()
    {
        if (_store == null) return;

        _suppressMember = true;
        Members.Clear();

        var colId = SelectedCollection?.Id ?? "";
        var keyword = (MemberSearch ?? "").Trim();

        foreach (var item in _store.Data.Items)
        {
            if (!ShowNonCollectable && !item.Collectable) continue;
            if (keyword.Length > 0
                && !(item.Title ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
                && !(item.Target ?? "").Contains(keyword, StringComparison.CurrentCultureIgnoreCase)) continue;

            Members.Add(new MemberRow(item, item.Collections.Contains(colId)));
        }

        _suppressMember = false;
        OnPropertyChanged(nameof(HasMembers));
    }

    /// <summary>给每一行挂上勾选联动（重建时统一挂，避免漏挂）。</summary>
    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (MemberRow r in e.OldItems) r.PropertyChanged -= OnMemberRowPropertyChanged;
        if (e.NewItems != null)
            foreach (MemberRow r in e.NewItems) r.PropertyChanged += OnMemberRowPropertyChanged;
    }

    private void OnMemberRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MemberRow.IsMember)) return;
        if (_suppressMember || _loading || sender is not MemberRow row) return;
        if (_store == null || SelectedCollection == null) return;

        if (!row.CanToggle)
        {
            // 不可采集的行 ⛔ 勾不上：把勾选状态翻回去。
            _suppressMember = true;
            row.IsMember = !row.IsMember;
            _suppressMember = false;
            StatusText = string.IsNullOrEmpty(row.Reason) ? "这条不可采集，不能加入集合。" : $"这条不可采集：{row.Reason}";
            return;
        }

        if (_store.SetMembership(row.ItemId, SelectedCollection.Id, row.IsMember))
        {
            _store.Save();
            var col = SelectedCollection;
            col.MemberCount = _store.ItemsOf(col.Id).Count;
            StatusText = row.IsMember
                ? $"已把「{row.Title}」加入「{col.Name}」"
                : $"已把「{row.Title}」移出「{col.Name}」";
        }
    }

    // ==================== 详情卡回写 ====================

    private void LoadDetailEdits()
    {
        _loading = true;
        var col = SelectedCollection == null || _store == null ? null : _store.FindCollection(SelectedCollection.Id);

        EditName = col?.Name ?? "";
        EditIcon = col?.Icon ?? "";
        EditKind = ScriptCollectionKind.Normalize(col?.Kind);
        EditDescription = col?.Description ?? "";
        EditTags = col == null ? "" : string.Join(", ", col.Tags);
        EditRefreshMode = ScriptRefreshMode.Normalize(col?.RefreshMode);

        SelectedKindOption = KindOptions.FirstOrDefault(o => o.Value == EditKind);
        SelectedRefreshOption = RefreshOptions.FirstOrDefault(o => o.Value == EditRefreshMode);

        ViewOptions.Clear();
        ViewOptions.Add(new CollectionFilterOption { Label = "默认视图", Value = "" });
        if (col != null)
            foreach (var v in _store?.Data.Views.Where(v => v.CollectionId == col.Id) ?? Enumerable.Empty<ScriptView>())
                ViewOptions.Add(new CollectionFilterOption { Label = v.Name, Value = v.Id });
        SelectedViewOption = ViewOptions.FirstOrDefault(o => o.Value == (col?.DefaultViewId ?? ""));

        _loading = false;
        OnPropertyChanged(nameof(HasSelectedCollection));
        OnPropertyChanged(nameof(CollectionHint));
    }

    private void ApplyCollectionEdits()
    {
        if (_loading || _store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        col.Name = string.IsNullOrWhiteSpace(EditName) ? col.Name : EditName.Trim();
        col.Icon = EditIcon ?? "";
        col.Kind = ScriptCollectionKind.Normalize(EditKind);
        col.Description = EditDescription ?? "";
        col.Tags = (EditTags ?? "").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        col.RefreshMode = ScriptRefreshMode.Normalize(EditRefreshMode);
        col.DefaultViewId = SelectedViewOption?.Value ?? "";

        _store.Save();

        // 左栏行同步（⛔ 不整表重建，免得丢选中）
        SelectedCollection.Name = col.Name;
        SelectedCollection.Kind = col.Kind;
        SelectedCollection.Icon = col.Icon;
        SelectedCollection.Description = col.Description;
        OnPropertyChanged(nameof(CollectionHint));
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void NewCollection()
    {
        if (_store == null) return;
        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = "新集合",
            Kind = ScriptCollectionKind.Info,
            Icon = "🗂",
            Order = _store.Data.Collections.Count,
            RefreshMode = ScriptRefreshMode.OnEnter,
            LastOpenedAt = Now(),
        };
        _store.Data.Collections.Add(col);
        _store.Save();
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault(r => r.Id == col.Id);
        StatusText = "已新建集合，在右栏勾选成员（即时生效）。";
    }

    [RelayCommand]
    private void DuplicateCollection()
    {
        if (_store == null || SelectedCollection == null) return;
        var src = _store.FindCollection(SelectedCollection.Id);
        if (src == null) return;

        var col = new ScriptCollection
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = src.Name + " 副本",
            Kind = src.Kind,
            Icon = src.Icon,
            Description = src.Description,
            Order = _store.Data.Collections.Count,
            Pinned = false,
            Builtin = false,
            Tags = new List<string>(src.Tags),
            RefreshMode = src.RefreshMode,
            RefreshSeconds = src.RefreshSeconds,
            LastOpenedAt = Now(),
        };
        _store.Data.Collections.Add(col);

        // 成员一并复制（条目侧加一条集合 id）
        foreach (var item in _store.Data.Items.Where(i => i.Collections.Contains(src.Id)))
            item.Collections.Add(col.Id);

        _store.Save();
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault(r => r.Id == col.Id);
        StatusText = $"已复制为「{col.Name}」。";
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync()
    {
        if (_store == null || SelectedCollection == null) return;
        var row = SelectedCollection;
        var col = _store.FindCollection(row.Id);
        if (col == null) return;

        if (col.Builtin) { StatusText = "内置集合不可删除（可以改名或归档）。"; return; }

        var ok = await DialogHooks.ConfirmAsync($"确定删除集合「{col.Name}」吗？\n只删集合定义，⛔ 不删里面的条目。");
        if (!ok) return;

        // 先清成员关系（条目侧），再删集合。
        foreach (var item in _store.Data.Items) item.Collections.RemoveAll(c => c == col.Id);
        _store.Data.Collections.RemoveAll(c => c.Id == col.Id);
        if (_store.Data.LastCollectionId == col.Id) _store.Data.LastCollectionId = "";
        _store.Save();

        RefreshCollections();
        StatusText = $"已删除集合「{col.Name}」。";
    }

    [RelayCommand]
    private void ArchiveCollection()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        col.Archived = !col.Archived;
        _store.Save();
        RefreshCollections();
        StatusText = col.Archived ? $"已归档「{col.Name}」。" : $"已取消归档「{col.Name}」。";
    }

    [RelayCommand]
    private void SendToInfoPanel()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        _store.Data.LastCollectionId = col.Id;
        col.LastOpenedAt = Now();
        _store.Save();

        if (_nav == null) { StatusText = "无法跳转（导航服务不可用）。"; return; }
        if (_nav.CurrentPageKey == "script-info") { StatusText = $"「{col.Name}」已在信息面板中。"; return; }

        _nav.NavigateTo("script-info");
        StatusText = $"已发送「{col.Name}」到信息面板。";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_store == null || SelectedCollection == null) return;
        var col = _store.FindCollection(SelectedCollection.Id);
        if (col == null) return;

        var path = await DialogHooks.SaveFileAsync("导出集合", _store.ConfigDir, $"collection-{col.Name}.json", "*.json");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(col,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
            File.WriteAllText(path, json);
            StatusText = $"已导出到 {path}";
        }
        catch (Exception ex)
        {
            StatusText = "导出失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 🔍 扫描目录：把脚本目录里的新脚本入库，并**加入当前集合**。
    /// <para>本页的扫描是"补进这个集合"，所以不再走库页那套候选勾选。</para>
    /// </summary>
    [RelayCommand]
    private async Task ScanIntoCollectionAsync()
    {
        if (_store == null || SelectedCollection == null) { StatusText = "先选中一个集合。"; return; }

        var dir = await DialogHooks.PickFolderAsync("扫描脚本目录（新脚本会入库并加入当前集合）",
            _settings?.EffectiveScriptsFolder ?? _svc?.ScriptsDirectory ?? "");
        if (string.IsNullOrWhiteSpace(dir)) return;

        var candidates = ScriptScanner.Scan(dir, true, _store.Data.Items)
                                     .Where(c => c.State == ScriptCandidate.StateNew)
                                     .ToList();
        if (candidates.Count == 0) { StatusText = "没有新脚本可加入。"; return; }

        var col = _store.FindCollection(SelectedCollection.Id);
        foreach (var c in candidates)
        {
            var item = new ScriptItem
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Role = ScriptItemRole.Script,
                Title = Path.GetFileNameWithoutExtension(c.FullPath),
                Target = c.RelativePath,
                Capture = true,
                Readonly = true,
                RetentionMode = ScriptRetentionMode.Latest,
                Collectable = true,
                CollectableSource = ScriptCollectableSource.Auto,
            };
            item.Collections.Add(SelectedCollection.Id);
            _store.Data.Items.Add(item);
            CollectableEvaluator.Apply(item, dir);
        }

        _store.SyncBuiltinCollections();
        _store.Save();
        _svc?.Reload();

        RefreshCollections();
        LoadMemberTable();
        StatusText = $"已加入 {candidates.Count} 个脚本到「{col?.Name}」（同时入库）。";
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
}
