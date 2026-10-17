using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace NewToys.UI.Controls;

/// <summary>
/// 列表页骨架（NT-063 P1）：四段容器 —— 顶工具条区 / 列表区 / 空态区 / 底栏区。
/// <para>
/// 页面把自己的内容填进三个内容槽：
/// <list type="bullet">
///   <item><see cref="TopArea"/>：工具条 / 筛选条（可空）；</item>
///   <item><see cref="HeaderArea"/>：表头（<c>Border.list-header</c> + <c>ColumnGrid</c>，含列显隐右键菜单）；</item>
///   <item><see cref="ListArea"/>：列表本体（<c>ListBox</c>，行模板各页自写）。</item>
/// </list>
/// 空态区与 <see cref="ListArea"/> 同格：<see cref="IsEmpty"/> 绑宿主 VM 的 <c>!HasItems</c>，
/// 文案 / 入口命令经 <see cref="EmptyMessage"/> / <see cref="EmptyActionText"/> / <see cref="EmptyActionCommand"/> 下发。
/// </para>
/// <para>
/// 底栏 = <see cref="ListFooterBar"/>（UI 壳）：<see cref="ShowFooter"/> 由宿主绑 VM 的
/// <c>HasFooterContent</c>（列表超出页面底部才 true，NT-064 接入前无该属性 ⇒ 不显示）。
/// </para>
/// </summary>
public partial class ListPageSkeleton : UserControl
{
    /// <summary>
    /// 首帧兜底：code-behind 赋值只在**属性变化**时触发 ⇒ 构造期的默认显隐要在这里落一次
    /// （页面不设 <see cref="ShowFooter"/> 时底栏必须不占位；空态文案为空时空态块必须不占位）。
    /// </summary>
    public ListPageSkeleton()
    {
        InitializeComponent();
        Footer.IsVisible = ShowFooter;
        ApplyEmptyState();

        // NT-064：用户在底栏改了页大小 ⇒ 回填骨架属性（⇒ 经宿主页的双向绑写回 VM）。
        // 🔴 没有这一跳，ComboBox 只改到 Footer.PageSize 就断了 —— VM 的 PageSize 永远不变。
        Footer.PropertyChanged += (_, e) =>
        {
            if (e.Property == ListFooterBar.PageSizeProperty) PageSize = Footer.PageSize;
        };
    }

    /// <summary>顶区内容：工具条 / 筛选条（null ⇒ 该行高度 0）。</summary>
    public static readonly StyledProperty<object?> TopAreaProperty =
        AvaloniaProperty.Register<ListPageSkeleton, object?>(nameof(TopArea));

    /// <summary>表头内容（card 内第一行；<c>Border.list-header</c> + <c>ColumnGrid</c>）。</summary>
    public static readonly StyledProperty<object?> HeaderAreaProperty =
        AvaloniaProperty.Register<ListPageSkeleton, object?>(nameof(HeaderArea));

    /// <summary>列表内容（card 内 <c>*</c> 行；<c>ListBox</c> 连行模板）。</summary>
    public static readonly StyledProperty<object?> ListAreaProperty =
        AvaloniaProperty.Register<ListPageSkeleton, object?>(nameof(ListArea));

    /// <summary>空态说明文案。</summary>
    public static readonly StyledProperty<string?> EmptyMessageProperty =
        AvaloniaProperty.Register<ListPageSkeleton, string?>(nameof(EmptyMessage));

    /// <summary>空态入口按钮文案（为空 ⇒ 按钮不显示）。</summary>
    public static readonly StyledProperty<string?> EmptyActionTextProperty =
        AvaloniaProperty.Register<ListPageSkeleton, string?>(nameof(EmptyActionText));

    /// <summary>空态入口按钮命令（复用本页 VM 的现有命令）。</summary>
    public static readonly StyledProperty<ICommand?> EmptyActionCommandProperty =
        AvaloniaProperty.Register<ListPageSkeleton, ICommand?>(nameof(EmptyActionCommand));

    /// <summary>是否显示空态（宿主绑 VM 的 <c>!HasItems</c>；默认 false）。</summary>
    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<ListPageSkeleton, bool>(nameof(IsEmpty));

    /// <summary>是否显示底栏（宿主绑 VM 的 <c>HasFooterContent</c>；默认 false）。</summary>
    public static readonly StyledProperty<bool> ShowFooterProperty =
        AvaloniaProperty.Register<ListPageSkeleton, bool>(nameof(ShowFooter));

    /// <summary>底栏「共 N 条」（NT-064 接入前宿主可不绑）。</summary>
    public static readonly StyledProperty<int> TotalCountProperty =
        AvaloniaProperty.Register<ListPageSkeleton, int>(nameof(TotalCount));

    /// <summary>底栏「已选 M」（NT-064 接入前宿主可不绑）。</summary>
    public static readonly StyledProperty<int> SelectedCountProperty =
        AvaloniaProperty.Register<ListPageSkeleton, int>(nameof(SelectedCount));

    /// <summary>底栏页码文本（NT-064：「第 x / y 页」）。</summary>
    public static readonly StyledProperty<string?> PageTextProperty =
        AvaloniaProperty.Register<ListPageSkeleton, string?>(nameof(PageText));

    /// <summary>底栏跳第一页（NT-064）。</summary>
    public static readonly StyledProperty<ICommand?> FirstCommandProperty =
        AvaloniaProperty.Register<ListPageSkeleton, ICommand?>(nameof(FirstCommand));

    /// <summary>底栏跳上一页（NT-064）。</summary>
    public static readonly StyledProperty<ICommand?> PrevCommandProperty =
        AvaloniaProperty.Register<ListPageSkeleton, ICommand?>(nameof(PrevCommand));

    /// <summary>底栏跳下一页（NT-064）。</summary>
    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<ListPageSkeleton, ICommand?>(nameof(NextCommand));

    /// <summary>底栏跳最后一页（NT-064）。</summary>
    public static readonly StyledProperty<ICommand?> LastCommandProperty =
        AvaloniaProperty.Register<ListPageSkeleton, ICommand?>(nameof(LastCommand));

    /// <summary>底栏页大小候选项（NT-064）。</summary>
    public static readonly StyledProperty<IEnumerable?> PageSizesProperty =
        AvaloniaProperty.Register<ListPageSkeleton, IEnumerable?>(nameof(PageSizes));

    /// <summary>底栏当前页大小（NT-064，双向）。</summary>
    public static readonly StyledProperty<int> PageSizeProperty =
        AvaloniaProperty.Register<ListPageSkeleton, int>(nameof(PageSize));

    /// <summary>底栏是否显示页码跳组（NT-064：宿主从 VM 的 HasPaging 下发）。</summary>
    public static readonly StyledProperty<bool> ShowPagingProperty =
        AvaloniaProperty.Register<ListPageSkeleton, bool>(nameof(ShowPaging));

    /// <summary>底栏总页数（「共 N 页」+ 页码按钮组渲染用；NT-064）。</summary>
    public static readonly StyledProperty<int> PageCountProperty =
        AvaloniaProperty.Register<ListPageSkeleton, int>(nameof(PageCount));

    /// <summary>底栏当前页码（页码按钮强调色；NT-064）。</summary>
    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<ListPageSkeleton, int>(nameof(CurrentPage));

    /// <summary>底栏「跳转到」/ 页码按钮的直达命令（参数 = 页码；NT-064）。</summary>
    public static readonly StyledProperty<ICommand?> JumpToPageCommandProperty =
        AvaloniaProperty.Register<ListPageSkeleton, ICommand?>(nameof(JumpToPageCommand));

    public object? TopArea
    {
        get => GetValue(TopAreaProperty);
        set => SetValue(TopAreaProperty, value);
    }

    public object? HeaderArea
    {
        get => GetValue(HeaderAreaProperty);
        set => SetValue(HeaderAreaProperty, value);
    }

    public object? ListArea
    {
        get => GetValue(ListAreaProperty);
        set => SetValue(ListAreaProperty, value);
    }

    public string? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public string? EmptyActionText
    {
        get => GetValue(EmptyActionTextProperty);
        set => SetValue(EmptyActionTextProperty, value);
    }

    public ICommand? EmptyActionCommand
    {
        get => GetValue(EmptyActionCommandProperty);
        set => SetValue(EmptyActionCommandProperty, value);
    }

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public bool ShowFooter
    {
        get => GetValue(ShowFooterProperty);
        set => SetValue(ShowFooterProperty, value);
    }

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
    /// 🔴 v3.71 / NT-063 P2：属性变化 ⇒ **直接给宿主控件赋值**（⛔ 不用祖先绑定，见 axaml 顶部注释）。
    /// 页面在 XAML 里设属性一定发生在本构造（InitializeComponent）之后 ⇒ 具名宿主都已就绪。
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TopAreaProperty) TopAreaHost.Content = TopArea;
        else if (change.Property == HeaderAreaProperty) HeaderAreaHost.Content = HeaderArea;
        else if (change.Property == ListAreaProperty) ListAreaHost.Content = ListArea;
        else if (change.Property == EmptyMessageProperty
              || change.Property == EmptyActionTextProperty
              || change.Property == EmptyActionCommandProperty
              || change.Property == IsEmptyProperty) ApplyEmptyState();
        else if (change.Property == ShowFooterProperty) Footer.IsVisible = ShowFooter;
        else if (change.Property == TotalCountProperty) Footer.TotalCount = TotalCount;
        else if (change.Property == SelectedCountProperty) Footer.SelectedCount = SelectedCount;
        // NT-064：分页插槽整组转发给底栏（缺一个 ⇒ 该功能在底栏上不出现）
        else if (change.Property == PageTextProperty) Footer.PageText = PageText;
        else if (change.Property == FirstCommandProperty) Footer.FirstCommand = FirstCommand;
        else if (change.Property == PrevCommandProperty) Footer.PrevCommand = PrevCommand;
        else if (change.Property == NextCommandProperty) Footer.NextCommand = NextCommand;
        else if (change.Property == LastCommandProperty) Footer.LastCommand = LastCommand;
        else if (change.Property == PageSizesProperty) Footer.PageSizes = PageSizes;
        else if (change.Property == PageSizeProperty) Footer.PageSize = PageSize;
        else if (change.Property == ShowPagingProperty) Footer.ShowPaging = ShowPaging;
        // NT-064：页码直达组（共 N 页 / 当前页 / 跳转命令）
        else if (change.Property == PageCountProperty) Footer.PageCount = PageCount;
        else if (change.Property == CurrentPageProperty) Footer.CurrentPage = CurrentPage;
        else if (change.Property == JumpToPageCommandProperty) Footer.JumpToPageCommand = JumpToPageCommand;
    }

    /// <summary>
    /// 空态块三件套下发 + 显隐。🔴 **没文案就不显示**：页面绑定断了（IsEmpty 误 true）时
    /// 也不能拿一个空 StackPanel 盖住列表区（那是「列表没了」的另一种形态）。
    /// </summary>
    private void ApplyEmptyState()
    {
        EmptyState.Message = EmptyMessage;
        EmptyState.ActionText = EmptyActionText;
        EmptyState.ActionCommand = EmptyActionCommand;
        EmptyState.IsVisible = IsEmpty && !string.IsNullOrEmpty(EmptyMessage);
    }
}
