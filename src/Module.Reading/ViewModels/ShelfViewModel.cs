using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Core.Services;
using NewToys.Reading.Models;
using NewToys.Reading.Services;
using NewToys.Shared.Details;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Reading.ViewModels;

/// <summary>书架列表的一行。</summary>
public sealed class ShelfRow
{
    public ShelfRow(ShelfBook book, string sourceName)
    {
        Book = book;
        SourceName = sourceName;
    }

    public ShelfBook Book { get; }

    public string Name => Book.Name;

    public string Author => Book.AuthorText;

    public string SourceName { get; }

    public string Added => Book.AddedText;

    public string Tip => Book.BookUrl;
}

/// <summary>
/// 「阅读 → 书架」页（<c>reading-shelf</c>）：搜索结果里点「加入书架」存下来的书。
/// <para>双击 / 右键「继续阅读」把书交给搜索页（那里有完整的目录 + 正文），⛔ 本页不重复实现阅读器。</para>
/// </summary>
public partial class ShelfViewModel : ListPageViewModelBase, IDetailPanelProvider, IDetailCardSource
{
    private readonly ShelfStore _store;
    private readonly SourceStore _sources;
    private readonly ReadingSession _session;
    private readonly INavigationService? _nav;

    private readonly List<ShelfRow> _all = new();
    private IReadOnlyList<ToolbarItemDescriptor>? _toolbar;

    [ObservableProperty] private ShelfRow? _selected;

    /// <summary>设计期 / 无 DI 兜底构造。</summary>
    public ShelfViewModel() : base(null, null)
    {
        _store = null!;
        _sources = null!;
        _session = new ReadingSession();
        _nav = null;
    }

    public ShelfViewModel(
        ShelfStore store,
        SourceStore sources,
        ReadingSession session,
        INavigationService? nav = null,
        IColumnLayoutService? cols = null,
        IOptionsStore? opts = null)
        : base(cols, opts)
    {
        _store = store;
        _sources = sources;
        _session = session;
        _nav = nav;

        LoadLayout();
        Reload();
    }

    public ObservableCollection<ShelfRow> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    public bool HasSelection => Selected is not null;

    public bool ShowDetailBody => Selected is not null;

    protected override string PageKey => "reading-shelf";

    protected override IReadOnlyList<ListColumn> Columns { get; } = new List<ListColumn>
    {
        new("Name", "书名", "240", sample: "剑来"),
        new("Author", "作者", "160", sample: "烽火戏诸侯"),
        new("SourceName", "来源", "160", sample: "某某小说网（优）"),
        new("Added", "加入时间", "110", sample: "2026-11-01"),
    };

    public bool IsNameVisible => Vis("Name");
    public bool IsAuthorVisible => Vis("Author");
    public bool IsSourceNameVisible => Vis("SourceName");
    public bool IsAddedVisible => Vis("Added");

    protected override void RefreshView() => ApplyFilter();

    protected override void EnsureDetailSelection()
    {
        if (Selected is null && Items.Count > 0) Selected = Items[0];
    }

    private void ApplyFilter(string? keyword = null)
    {
        var key = (keyword ?? "").Trim();
        var rows = key.Length == 0
            ? _all
            : _all.Where(r => r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                              || r.Author.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();

        var keep = Selected?.Book.BookUrl;

        Items.Clear();
        foreach (var row in rows) Items.Add(row);

        OnPropertyChanged(nameof(HasItems));
        if (keep is not null) Selected = Items.FirstOrDefault(r => r.Book.BookUrl == keep);
        if (Selected is null && Items.Count > 0 && IsDetailVisible) Selected = Items[0];
    }

    private string _searchQuery = "";

    /// <summary>工具条搜索框：按书名 / 作者过滤。</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            var text = value ?? "";
            if (_searchQuery == text) return;
            _searchQuery = text;
            OnPropertyChanged();
            ApplyFilter(text);
        }
    }

    partial void OnSelectedChanged(ShelfRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowDetailBody));
        OnPropertyChanged(nameof(DetailName));
        OnPropertyChanged(nameof(DetailAuthor));
        OnPropertyChanged(nameof(DetailSource));
        OnPropertyChanged(nameof(DetailAdded));
        OnPropertyChanged(nameof(DetailUrl));
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private void Reload()
    {
        _all.Clear();
        if (_store is not null)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_sources is not null)
                foreach (var source in _sources.LoadAll()) names[source.Url] = source.Name;

            foreach (var book in _store.LoadAll())
            {
                var name = names.TryGetValue(book.SourceUrl, out var found) ? found : "（源已删除）";
                _all.Add(new ShelfRow(book, name));
            }
        }

        ApplyFilter(_searchQuery);
        StatusText = _all.Count == 0
            ? "书架是空的 —— 到「网上搜索」搜到书后，右键「＋ 加入书架」。"
            : $"书架里 {_all.Count} 本";
    }

    [RelayCommand]
    private void Read()
    {
        if (Selected is null) { StatusText = "先选中一本。"; return; }

        _session.PendingBook = new PendingBook(Selected.Book.SourceUrl, Selected.Book.BookUrl, Selected.Name);
        _nav?.NavigateTo("reading-search");
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (Selected is null) { StatusText = "先选中一本。"; return; }

        var ok = await DialogHooks.ConfirmAsync($"把《{Selected.Name}》移出书架？（只从列表移除，不影响源站）").ConfigureAwait(true);
        if (!ok) return;

        _store.Remove(new[] { Selected.Book.BookUrl });
        Selected = null;
        Reload();
        StatusText = "已移出书架";
    }

    // ==================== 工具条 ====================

    public override IReadOnlyList<ToolbarItemDescriptor>? Toolbar => _toolbar ??= new List<ToolbarItemDescriptor>
    {
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🔄 刷新书架",
            CommandPath = "ReloadCommand",
        },
        new()
        {
            Kind = ToolbarItemKind.TextField,
            Placeholder = "搜索书名 / 作者",
            ValuePath = "SearchQuery",
            Width = 220,
        },
        new() { Kind = ToolbarItemKind.Separator },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "📖 继续阅读",
            Classes = "primary",
            CommandPath = "ReadCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "打开这本书的目录与正文",
        },
        new()
        {
            Kind = ToolbarItemKind.Button,
            Text = "🗑 移出书架",
            Classes = "danger-btn",
            CommandPath = "RemoveCommand",
            IsEnabledPath = "HasSelection",
            ShowToolTipOnDisabled = true,
            ToolTip = "只从书架移除，不影响源站",
        },
    };

    // ==================== 详情面板 + 详情卡 ====================

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

    public string DetailName => Selected?.Name ?? "";

    public string DetailAuthor => Selected?.Author ?? "";

    public string DetailSource => Selected?.SourceName ?? "";

    public string DetailAdded => Selected?.Added ?? "";

    public string DetailUrl => Selected?.Book.BookUrl ?? "";

    private DetailCardDescriptor? _card;

    public DetailCardDescriptor? Card => _card ??= BuildCard();

    private DetailCardDescriptor BuildCard() => new()
    {
        BodyVisiblePath = nameof(ShowDetailBody),
        EmptyText = "书架是空的。在「网上搜索」里搜到书后，右键「＋ 加入书架」。",
        Sections =
        {
            new DetailSection
            {
                Header = "这本书",
                Fields =
                {
                    new DetailField { Label = "书名", ValuePath = nameof(DetailName), Kind = FieldKind.Text, Selectable = true },
                    new DetailField { Label = "作者", ValuePath = nameof(DetailAuthor), Kind = FieldKind.Text },
                    new DetailField { Label = "来源", ValuePath = nameof(DetailSource), Kind = FieldKind.Text },
                    new DetailField { Label = "加入时间", ValuePath = nameof(DetailAdded), Kind = FieldKind.Text },
                    new DetailField { Label = "详情地址", ValuePath = nameof(DetailUrl), Kind = FieldKind.Text, Selectable = true, Mono = true, MultiLine = true, Rows = 44 },
                }
            },
        }
    };
}
