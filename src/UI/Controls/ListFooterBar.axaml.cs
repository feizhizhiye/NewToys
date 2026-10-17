using System;
using System.Collections;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Models;

namespace NewToys.UI.Controls;

/// <summary>
/// 列表页底栏（NT-063，UI 壳）：计数 + 分页跳转链。
/// <para>
/// 🔴 显隐由**宿主页**决定：把本控件的 <see cref="Visual.IsVisible"/> 绑到 VM 的
/// <c>HasFooterContent</c>（列表超出页面底部 或 有多页才 true）—— 控件自己不判断高度。
/// </para>
/// <para>
/// 🔴 NT-064：内部一律 **code-behind 按属性直配**（⛔ 不用祖先绑定，见 axaml 顶部注释）；
/// 分页命令 / 页大小 / 页码直达经宿主（<c>ListPageSkeleton</c>）转发进来。
/// 布局（2026-10-16 用户定稿）：左计数，中「共 N 页 « ‹ 1 2 3 4 5 › » 跳转到[ ]」，
/// 右「每页 [25/50/100/500]」；当前页码按钮用 <c>primary</c> 强调色。
/// </para>
/// </summary>
public partial class ListFooterBar : UserControl
{
    /// <summary>
    /// 🔴 必须显式调 <c>InitializeComponent()</c>：Avalonia **不会自动生成**调用它的构造函数 ——
    /// 漏了它整个 XAML 都不加载（具名控件事后全 null），且**无任何报错**。
    /// </summary>
    public ListFooterBar()
    {
        InitializeComponent();
        Apply();   // 首帧兜底：属性默认值也要落到具名控件上

        // 用户在底栏改了页大小 ⇒ 写回本控件的 PageSize（宿主骨架监听该属性 ⇒ 经双向绑写回 VM）
        PageSizeCombo.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.SelectedItemProperty
                && PageSizeCombo.SelectedItem is PageSizeChoice c && c.Value > 0 && c.Value != PageSize)
                PageSize = c.Value;
        };

        // 「跳转到」输入框：回车直达（数字不合法就当没输入）
        JumpBox.AddHandler(KeyDownEvent, OnJumpBoxKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    private void OnJumpBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = (JumpBox.Text ?? "").Trim();
        if (text.Length == 0) return;
        if (int.TryParse(text, out var page) && page > 0 && JumpToPageCommand is { } cmd && cmd.CanExecute(page))
            cmd.Execute(page);
        JumpBox.Text = "";
    }

    /// <summary>列表总条数（「共 N 条」）。</summary>
    public static readonly StyledProperty<int> TotalCountProperty =
        AvaloniaProperty.Register<ListFooterBar, int>(nameof(TotalCount));

    /// <summary>当前选中条数（「已选 M」；0 时不显示该行）。</summary>
    public static readonly StyledProperty<int> SelectedCountProperty =
        AvaloniaProperty.Register<ListFooterBar, int>(nameof(SelectedCount));

    /// <summary>页码文本（「第 x / y 页」；保留给宿主做状态栏等用途，底栏本体用页码按钮组呈现）。</summary>
    public static readonly StyledProperty<string?> PageTextProperty =
        AvaloniaProperty.Register<ListFooterBar, string?>(nameof(PageText));

    /// <summary>跳第一页。</summary>
    public static readonly StyledProperty<ICommand?> FirstCommandProperty =
        AvaloniaProperty.Register<ListFooterBar, ICommand?>(nameof(FirstCommand));

    /// <summary>跳上一页。</summary>
    public static readonly StyledProperty<ICommand?> PrevCommandProperty =
        AvaloniaProperty.Register<ListFooterBar, ICommand?>(nameof(PrevCommand));

    /// <summary>跳下一页。</summary>
    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<ListFooterBar, ICommand?>(nameof(NextCommand));

    /// <summary>跳最后一页。</summary>
    public static readonly StyledProperty<ICommand?> LastCommandProperty =
        AvaloniaProperty.Register<ListFooterBar, ICommand?>(nameof(LastCommand));

    /// <summary>页大小候选项（底栏下拉切换页大小）。</summary>
    public static readonly StyledProperty<IEnumerable?> PageSizesProperty =
        AvaloniaProperty.Register<ListFooterBar, IEnumerable?>(nameof(PageSizes));

    /// <summary>当前页大小（宿主经骨架双向绑 VM 的 PageSize，切换即落盘 + 重取）。</summary>
    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<ListFooterBar, int>(nameof(PageSize));

    /// <summary>是否显示页码跳组（宿主从 VM 的 HasPaging 下发；命令非空只作兜底）。</summary>
    public static readonly StyledProperty<bool> ShowPagingProperty =
        AvaloniaProperty.Register<ListFooterBar, bool>(nameof(ShowPaging));

    /// <summary>总页数（「共 N 页」+ 页码按钮组渲染用）。</summary>
    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<ListFooterBar, int>(nameof(PageCount));

    /// <summary>当前页码（页码按钮里用它判强调色）。</summary>
    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<ListFooterBar, int>(nameof(CurrentPage));

    /// <summary>页码直达命令（页码按钮 / 「跳转到」输入框；参数 = 页码）。</summary>
    public static readonly StyledProperty<ICommand?> JumpToPageCommandProperty =
        AvaloniaProperty.Register<ListFooterBar, ICommand?>(nameof(JumpToPageCommand));

    public int TotalCount
    {
        get => GetValue(TotalCountProperty);
        set => SetValue(TotalCountProperty, value);
    }

    public int SelectedCount
    {
        get => GetValue(SelectedCountProperty);
        set => SetValue(SelectedCountProperty, value);
    }

    public string? PageText
    {
        get => GetValue(PageTextProperty);
        set => SetValue(PageTextProperty, value);
    }

    public ICommand? FirstCommand
    {
        get => GetValue(FirstCommandProperty);
        set => SetValue(FirstCommandProperty, value);
    }

    public ICommand? PrevCommand
    {
        get => GetValue(PrevCommandProperty);
        set => SetValue(PrevCommandProperty, value);
    }

    public ICommand? NextCommand
    {
        get => GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    public ICommand? LastCommand
    {
        get => GetValue(LastCommandProperty);
        set => SetValue(LastCommandProperty, value);
    }

    public IEnumerable? PageSizes
    {
        get => GetValue(PageSizesProperty);
        set => SetValue(PageSizesProperty, value);
    }

    public int PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public bool ShowPaging
    {
        get => GetValue(ShowPagingProperty);
        set => SetValue(ShowPagingProperty, value);
    }

    public int PageCount
    {
        get => GetValue(PageCountProperty);
        set => SetValue(PageCountProperty, value);
    }

    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public ICommand? JumpToPageCommand
    {
        get => GetValue(JumpToPageCommandProperty);
        set => SetValue(JumpToPageCommandProperty, value);
    }

    /// <summary>
    /// 是否显示页码跳组：**只认宿主下发的 <see cref="ShowPaging"/>**（VM 的 HasPaging）。
    /// 🔴 2026-10-16 修：⛔ 去掉「四个命令非空即显示」的兜底 —— 骨架无条件转发跳页命令，
    /// 兜底会让**草稿态（分页挂起）也显示跳转链**，点了不刷新（VM 侧挂起不重取）=「按钮点了不生效」。
    /// </summary>
    public bool HasPaging => ShowPaging;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        Apply();
    }

    /// <summary>
    /// 把当前属性值直配到具名控件（NT-064：⛔ 不用绑定 —— 祖先绑定解析失败是静默空白）。
    /// XAML 未加载（具名控件 null）时整体跳过 —— 与 ListPageSkeleton 同口径的护栏。
    /// </summary>
    private void Apply()
    {
        if (TotalTextBlock is null || PagingGroup is null) return;   // XAML 未加载 ⇒ 跳过（⛔ 不 NRE）

        TotalTextBlock.Text = string.Format(CultureInfo.CurrentCulture, "共 {0} 条", TotalCount);

        var hasSel = SelectedCount > 0;
        SelectedTextBlock.IsVisible = hasSel;
        // 分页中注明「当前页」口径（验收标准⑤：批量操作只作用于当前页选中项）
        SelectedTextBlock.Text = HasPaging
            ? string.Format(CultureInfo.CurrentCulture, "已选 {0}（当前页）", SelectedCount)
            : string.Format(CultureInfo.CurrentCulture, "已选 {0}", SelectedCount);

        PagingGroup.IsVisible = HasPaging;
        PageCountText.Text = PageCount > 0 ? string.Format(CultureInfo.CurrentCulture, "共 {0} 页", PageCount) : "";
        FirstButton.Command = FirstCommand;
        PrevButton.Command = PrevCommand;
        NextButton.Command = NextCommand;
        LastButton.Command = LastCommand;

        SyncPageSizeCombo();

        RenderPageButtons();
    }

    /// <summary>
    /// 页大小下拉的一个选项。用<b>带 Label 的包装类型</b>而不是裸 int：
    /// 下拉要显示「全部」（= <see cref="ListPageViewModelBase.AllPageSize"/> 哨兵值），
    /// 而 Avalonia 的 ComboBox 默认用 <c>ToString()</c> 渲染 ⇒ 覆盖它即可，
    /// ⛔ 不必引 ItemTemplate / Converter（跨程序集 StaticResource 解析失败是**静默空白**，更难查）。
    /// </summary>
    private sealed record PageSizeChoice(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private string _pageSizeComboKey = "";

    private void SyncPageSizeCombo()
    {
        if (PageSizeCombo is null) return;

        var sizes = PageSizes?.Cast<object?>().OfType<int>().ToList() ?? new List<int>();
        if (PageSize > 0 && !sizes.Contains(PageSize)) sizes.Add(PageSize);   // 当前值不在候选项 ⇒ 补进下拉

        var items = sizes.Select(v => new PageSizeChoice(
                v,
                v >= ListPageViewModelBase.AllPageSize
                    ? "全部"
                    : v.ToString(CultureInfo.CurrentCulture)))
            .ToList();

        var key = string.Join(",", sizes);
        if (key != _pageSizeComboKey)
        {
            _pageSizeComboKey = key;
            PageSizeCombo.ItemsSource = items;
        }

        var sel = PageSize > 0 ? items.FirstOrDefault(i => i.Value == PageSize) : null;
        if (!ReferenceEquals(PageSizeCombo.SelectedItem, sel)) PageSizeCombo.SelectedItem = sel;
    }

    /// <summary>
    /// 页码按钮组（1 2 3 4 5 …）：当前页附近最多 5 个；当前页用 <c>primary</c> 强调色。
    /// key 相同（页范围 / 当前页 / 命令都没变）⇒ 跳过重建（Apply 每次属性变化都跑，别抖动）。
    /// </summary>
    private string _pageButtonsKey = "";

    private void RenderPageButtons()
    {
        if (PageButtonsHost is null) return;

        var count = PageCount;
        var cur = CurrentPage;
        var cmd = JumpToPageCommand;
        var start = 1;
        var end = count;
        if (count > 5)
        {
            start = Math.Max(1, Math.Min(cur - 2, count - 4));
            end = Math.Min(count, start + 4);
        }

        var key = $"{count}|{cur}|{start}|{end}|{(cmd is null ? "-" : "c")}";
        if (key == _pageButtonsKey) return;   // 没变 ⇒ 不重建
        _pageButtonsKey = key;

        PageButtonsHost.Children.Clear();
        if (count <= 0) return;

        for (var p = start; p <= end; p++)
        {
            var page = p;
            var b = new Button
            {
                Content = page.ToString(CultureInfo.CurrentCulture),
                Height = 22,
                MinWidth = 26,
                // 2026-10-17 版式返修：
                // · 左右内边距归零 ⇒ 各页码按钮盒宽一致（1 与 12 同宽），节距才均匀；
                // · **上内边距 3** = 光学居中：数字无降部 ⇒ 墨迹中心比按钮盒中心高 ~2px
                //   （实测 11px 字号：墨迹 y17→24 中心 20.5 vs 盒 y12→33 中心 22.5）。
                //   顶部垫 4 用户反馈"略偏下" ⇒ 收成 3（下移 1.5px，留 ~0.3px 偏高，肉眼不可见）；
                //   箭头 `« ‹ › »` 是反向偏差（画在基线附近、偏下 ~2px），在 axaml 里给下内边距 4。
                Padding = new Thickness(0, 3, 0, 0),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                // 显式给 Center：padding 的光学居中靠"内容在内容区居中"这条前提，⛔ 不赌默认值
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Classes = { page == cur ? "primary" : "ghost" },
                Command = cmd is null ? null : new RelayCommand<object?>(_ => cmd.Execute(page)),
            };
            PageButtonsHost.Children.Add(b);
        }
    }
}
