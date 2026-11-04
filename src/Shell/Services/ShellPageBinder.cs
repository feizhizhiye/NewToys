using System;
using System.ComponentModel;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Services;

/// <summary>
/// 当前标签页的 **UI 契约绑定**：详情面板 / 历史栈 / 状态栏 / 声明式工具条。
/// <para>
/// 独立成类的理由：契约探测与订阅生命周期（切标签、设置页切节、页面自报状态变化）
/// 和 Shell 的标签管理是两件事，混在 <see cref="ShellViewModel"/> 里会让那个类继续膨胀。
/// </para>
/// <para>
/// 每次 <see cref="Bind"/> 都**先解绑再重探**（旧订阅不摘会泄漏）；探测结果写回宿主 VM 的
/// 展示属性（IsDetailPanelVisible / ToolbarSource / StatusBarText …），宿主的 XAML 只认那些属性。
/// </para>
/// </summary>
internal sealed class ShellPageBinder
{
    private readonly ShellViewModel _owner;
    private readonly IColumnLayoutService _cols;

    private IDetailPanelProvider? _detail;
    private IHistoryProvider? _history;
    private IStatusProvider? _status;
    private IStatusActionProvider? _action;
    private ISelectionStatusProvider? _selection;

    /// <summary>当前标签就是应用级设置页（复合 VM：切节要重绑契约）。</summary>
    private SettingsViewModel? _settings;

    public ShellPageBinder(ShellViewModel owner, IColumnLayoutService cols)
    {
        _owner = owner;
        _cols = cols;
    }

    /// <summary>状态栏 `⚠` 行的跳转目标 Key（无目标 = null ⇒ 不可点）。</summary>
    public string? StatusActionKey => _action?.StatusActionKey;

    /// <summary>状态栏点击提示（tooltip）。</summary>
    public string StatusActionTip => _action?.StatusActionTip ?? "";

    /// <summary>状态栏那一行当前是否可点击跳转。</summary>
    public bool HasStatusAction => !string.IsNullOrWhiteSpace(_action?.StatusActionKey);

    /// <summary>标题栏「详情」开关：转给当前页的 <see cref="IDetailPanelProvider"/>。</summary>
    public void ToggleDetail() => _detail?.ToggleDetail();

    /// <summary>为某个标签页重绑全部契约；<paramref name="tab"/> 为 null = 无页（全部清空）。</summary>
    public void Bind(TabItemViewModel? tab)
    {
        Unbind();

        _owner.IsDetailPanelVisible = false;
        _owner.DetailPanelContent = null;
        _owner.DetailCard = null;
        _owner.DetailCardSource = null;
        _owner.IsDetailCardActive = false;
        _owner.StatusBarText = "";
        _owner.SelectionText = "";
        _owner.HasSelection = false;
        _owner.RefreshStatusAction();

        // 应用级设置页是**复合 VM**：它把契约转发给当前节，切节时发 SectionChanged ⇒ 这里重探一遍
        //（工具条 / 详情卡 / 状态栏随节变）。
        if (tab?.ViewModel is SettingsViewModel settings)
        {
            _settings = settings;
            settings.SectionChanged += OnSettingsSectionChanged;
        }

        // 声明式工具条：页面没实现契约（或工具条为空）= 本页没有工具条（⛔ 不留空行）。
        // 复合页可经 IToolbarOwner 把承载者换成**子 VM**（描述符里的 CommandPath 相对它解析）。
        var toolbarOwner = tab?.ViewModel is IToolbarOwner owner ? owner.ToolbarOwner : tab?.ViewModel;
        var toolbar = (toolbarOwner as IToolbarSource)?.Toolbar;
        _owner.IsToolbarActive = toolbar is { Count: > 0 };
        _owner.ToolbarSource = _owner.IsToolbarActive ? toolbarOwner : null;

        if (tab?.ViewModel is IDetailPanelProvider p)
        {
            _detail = p;
            // 初值从 config 恢复（Shell 统一持久化）
            var open = _cols.LoadFlag(tab.Key, IColumnLayoutService.FlagDetailPanel);
            if (open && !p.IsDetailOpen) p.OpenDetail();
            p.DetailChanged += OnDetailChanged;
            RefreshDetail(tab.Key, p);
        }

        if (tab?.ViewModel is IHistoryProvider h)
        {
            _history = h;
            h.StatePushed += OnStatePushed;
        }

        if (tab?.ViewModel is IStatusProvider sp)
        {
            _status = sp;
            sp.StatusChanged += OnStatusChanged;
            _owner.StatusBarText = sp.StatusText;
        }

        _action = tab?.ViewModel as IStatusActionProvider;
        _owner.RefreshStatusAction();

        // 选中数走**属性变更**而不是专用事件：页面 VM 都派生自 ObservableObject，
        // 选中变化时本就会 OnPropertyChanged(nameof(SelectedCount))（多选列表页的既有写法）。
        if (tab?.ViewModel is ISelectionStatusProvider sel)
        {
            _selection = sel;
            sel.PropertyChanged += OnSelectionPropertyChanged;
            RefreshSelection();
        }
    }

    private void Unbind()
    {
        if (_detail != null) { _detail.DetailChanged -= OnDetailChanged; _detail = null; }
        if (_history != null) { _history.StatePushed -= OnStatePushed; _history = null; }
        if (_status != null) { _status.StatusChanged -= OnStatusChanged; _status = null; }
        if (_selection != null) { _selection.PropertyChanged -= OnSelectionPropertyChanged; _selection = null; }
        if (_settings != null) { _settings.SectionChanged -= OnSettingsSectionChanged; _settings = null; }
        _action = null;
    }

    /// <summary>设置页**切节**：重绑契约 + 刷左树高亮（左树子项 = 节锚点）。</summary>
    private void OnSettingsSectionChanged(object? sender, EventArgs e)
    {
        var tab = _owner.ActiveTab;
        if (tab == null || !ReferenceEquals(tab.ViewModel, _settings)) return;
        Bind(tab);
        _owner.AfterSettingsSectionRebound(tab);
    }

    /// <summary>页面状态变了 → 刷新底部状态栏。</summary>
    private void OnStatusChanged(object? sender, EventArgs e)
    {
        _owner.StatusBarText = _status?.StatusText ?? "";
        _owner.RefreshStatusAction();   // 状态文本与「可点击目标」同生同灭
    }

    /// <summary>页面选中数变了 → 刷新状态栏右端的「已选 N」。</summary>
    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(ISelectionStatusProvider.SelectedCount))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        var count = _selection?.SelectedCount ?? 0;
        _owner.HasSelection = count > 0;
        _owner.SelectionText = count > 0 ? $"已选 {count}" : "";
    }

    private void OnDetailChanged(object? sender, EventArgs e)
    {
        if (_detail != null && _owner.ActiveTab != null)
            RefreshDetail(_owner.ActiveTab.Key, _detail);
    }

    /// <summary>历史栈：页面压入一个状态点 → 记到当前标签自己的栈里。</summary>
    private void OnStatePushed(object? sender, EventArgs e)
    {
        var tab = _owner.ActiveTab;
        if (tab == null || _history == null) return;
        tab.BackStack.Push(_history.CaptureState());
        tab.ForwardStack.Clear();
    }

    private void RefreshDetail(string pageKey, IDetailPanelProvider p)
    {
        _owner.IsDetailPanelVisible = p.HasDetailPanel && p.IsDetailOpen;

        // 两轨择一：页面实现 IDetailCardSource 且 Card 非空 → 走声明式（DetailCardHost 统一渲染），
        // ⛔ 此时不碰 p.DetailContent（否则白建一个自绘视图）；否则退回页面自绘 DetailContent。
        var card = (p as IDetailCardSource)?.Card;
        _owner.IsDetailCardActive = card is not null;
        _owner.DetailCard = card;
        _owner.DetailCardSource = card is null ? null : p;
        _owner.DetailPanelContent = card is null ? p.DetailContent : null;

        _cols.SaveFlag(pageKey, IColumnLayoutService.FlagDetailPanel, p.IsDetailOpen);
    }
}
