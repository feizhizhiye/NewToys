using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewToys.Shared.Services;

namespace NewToys.Shared.Models;

/// <summary>「列表页」ViewModel 的公共基类：把五个列表页重复过五遍的那套逻辑收在一处 （列显隐 + 详情栏开合 + 排序状态 + 多项筛选），派生类只留自己独有的部分。（WHY: SH-017）</summary>
public abstract partial class ListPageViewModelBase : ObservableObject, IStatusProvider, IToolbarSource, IDisposable
{
    private readonly IColumnLayoutService? _cols;

    /// <summary>页大小等全局选项的落盘处（无注入 = 不记忆，⛔ 不碰文件）。</summary>
    private readonly IOptionsStore? _opts;

    /// <summary>列可见性（只有被显式保存过的列才在字典里；不在字典里的列 = 默认可见）。</summary>
    private readonly Dictionary<string, bool> _visibility = new();

    private string _sortColumn = "";
    private bool _sortDescending;

    protected ListPageViewModelBase() { }

    /// <summary>
    /// 页面标签关闭时由 Shell 调用。
    /// <para>🔴 派生类凡订阅了**单例服务**的事件（<c>TaskQueue.Changed</c> 这类），必须在这里 <c>-=</c> 退订：
    /// 单例活到进程结束，不退订会把整个页面 VM（连同 Items）钉在内存里，并在页面关掉后继续被回调。</para>
    /// </summary>
    public virtual void Dispose() { }

    protected ListPageViewModelBase(IColumnLayoutService? cols, IOptionsStore? opts = null)
    {
        _cols = cols;
        _opts = opts;

        // 🔴 构造期只走属性赋值（⛔ 不直连 _pageSize，MVVMTK0034）；临时挂起 setter 副作用
        //    （写盘 + RefreshView）—— 派生类还没构造完，RefreshView 会碰到未初始化成员。
        _initializing = true;
        try
        {
            if (opts != null
                && int.TryParse(opts.Get(OptionsModule, PageSizeKey), out var saved) && saved > 0)
                PageSize = saved;   // 记住上次选择的页大小（shell 分区，全局）
            SnapPageSizeToChoices();
        }
        finally { _initializing = false; }
    }

    /// <summary>构造期标志：抑制 <see cref="OnPageSizeChanged"/> 的写盘 / 重取副作用。</summary>
    private bool _initializing;

    /// <summary>把落盘的页大小收成本页预设里的值。（WHY: SH-018）</summary>
    private void SnapPageSizeToChoices()
    {
        var choices = PageSizeChoices;
        if (choices is { Count: > 0 } && !choices.Contains(PageSize))
            PageSize = choices[0];
    }

    // ==================== 底部状态栏（v3.9：提到基类，Shell 统一承载） ====================

    /// <summary>⚠️ 起提到基类：各列表页不要再自己声明 [ObservableProperty] private string _statusText （同名会隐藏基类属性、且事件不会触发）—— 页面里继续正常写 StatusText = "…" 即可。</summary>
    [ObservableProperty] private string _statusText = "就绪";

    /// <summary>状态文本变化 → 通知 Shell 刷新底栏。</summary>
    public event EventHandler? StatusChanged;

    // ==================== 声明式工具条（v3.53：提到基类，Shell 统一承载） ====================

    /// <summary>工具条内容（需求 §4.8.5）。默认 null = 本页没有声明式工具条（Shell ⛔ 不留空行， 页面继续用页内自绘工具条）；迁到 Shell 承载的页只需 override 本属性给出描述符列表。（WHY: SH-020）</summary>
    public virtual IReadOnlyList<ToolbarItemDescriptor>? Toolbar => null;

    /// <summary>工具条构成变化时通知 Shell 重建（工具条静态的页用不着；给"项随状态增删"的页备用）。</summary>
    public event EventHandler? ToolbarChanged;

    /// <summary>工具条构成变了 → 通知 Shell（<see cref="Toolbar"/> 是动态构造的页才需要调）。</summary>
    protected void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);

    partial void OnStatusTextChanged(string value) => StatusChanged?.Invoke(this, EventArgs.Empty);

    // ==================== 分页（数据层 LIMIT/OFFSET + 排序下推 + COUNT(*)） ====================

    /// <summary>页大小预设（底栏下拉用；<see cref="PageSize"/> 记忆上次选择）。</summary>
    public static readonly int[] PageSizeOptions = { 25, 50, 100, 500, AllPageSize };

    /// <summary>页大小预设的实例视图（XAML 绑 ItemsSource 用；⛔ 不直接绑静态字段）。
    /// 改 **virtual** —— 个别页（如软件包）要自己的一档预设，override 即可，⛔ 别改全局数组。</summary>
    public virtual IReadOnlyList<int> PageSizeChoices => PageSizeOptions;

    /// <summary>默认页大小（2026-10-16 用户定：25）。</summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// 「不分页」的哨兵值（底栏下拉里显示成「全部」）：一次把符合条件的行全列出来
    /// —— 2026-10-16 用户要的「间接全量选择」通道（选它 ⇒ 全选覆盖整个库 / 整个预览结果）。
    /// 🔴 别拿 0 当哨兵：<see cref="OnPageSizeChanged"/> 里 0 会被判非法并重置回 <see cref="DefaultPageSize"/>。
    /// </summary>
    public const int AllPageSize = int.MaxValue;

    /// <summary>页大小落盘位置（<see cref="IOptionsStore"/> 的 ui 分区 = 全局记忆，⛔ 不按页各存一份）。</summary>
    private const string OptionsModule = "ui";
    private const string PageSizeKey = "list-page-size";

    /// <summary>
    /// 本页是否启用分页：<b>默认 false = 旧行为</b>（全量进内存，内存里筛/排）。
    /// 派生页 override 成 true ⇒ <see cref="RefreshView"/> 自己按 <see cref="PageIndex"/> /
    /// <see cref="PageSize"/> 从数据源按需取页（SQL 侧筛/排，⛔ 不再整表入内存）。
    /// </summary>
    protected virtual bool PagingEnabled => false;

    /// <summary>临时挂起分页（派生类用：如「草稿态预览列表」数据不在库里，⛔ 不能 SQL 分页）。</summary>
    protected virtual bool IsPagingSuspended => false;

    /// <summary>当前页码（<b>1 起</b>）。</summary>
    [ObservableProperty] private int _pageIndex = 1;

    /// <summary>每页条数（底栏下拉切换；变化即落盘 + 收敛页码 + 重取）。</summary>
    [ObservableProperty] private int _pageSize = DefaultPageSize;

    /// <summary>符合条件的总条数（🔴 独立 COUNT(*) 查询，⛔ 不用 Items.Count = 当前页条数）。</summary>
    [ObservableProperty] private int _totalCount;

    /// <summary>
    /// 总页数（0 = 无数据）。
    /// <para>🔴 用 <c>(TotalCount - 1) / PageSize + 1</c> 而不是 <c>(TotalCount + PageSize - 1) / PageSize</c>：
    /// 选了「全部」（<see cref="AllPageSize"/> = <see cref="int.MaxValue"/>）时后者会**整数溢出**成负数。</para>
    /// </summary>
    public int PageCount =>
        TotalCount <= 0 || PageSize <= 0 ? 0 : (TotalCount - 1) / PageSize + 1;

    /// <summary>底栏页码文本（「第 x / y 页」）。</summary>
    public string PageText => PageCount <= 0 ? "无数据" : $"第 {PageIndex} / {PageCount} 页";

    /// <summary>是否显示底栏页码跳组（分页启用 + 未挂起 + 真的有多页；单页 ⛔ 不显示按钮）。</summary>
    public bool HasPaging => PagingEnabled && !IsPagingSuspended && PageCount > 1;

    /// <summary>底栏显隐（宿主绑 ListPageSkeleton.ShowFooter）：列表未超出页面底部不显示。（WHY: SH-021）</summary>
    public bool HasFooterContent => PagingEnabled && (PageCount > 1 || _contentOverflow);

    private bool _contentOverflow;

    /// <summary>宿主页量完布局后回填：列表内容是否超出视口（出现滚动）。</summary>
    public void SetContentOverflow(bool overflow)
    {
        if (_contentOverflow == overflow) return;
        _contentOverflow = overflow;
        OnPropertyChanged(nameof(HasFooterContent));
    }

    partial void OnPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PageText));
        if (PagingEnabled && !IsPagingSuspended) RefreshView();   // 翻页 ⇒ 派生类按新页码重取
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0) { PageSize = DefaultPageSize; return; }
        if (_initializing) return;                  // 构造期赋值（读盘 / 收成本页预设）不写盘、不重取
        _opts?.Set(OptionsModule, PageSizeKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        OnPropertyChanged(nameof(PageCount));       // 页大小变了 ⇒ 总页数重算（底栏「共 N 页」/ 页码按钮）
        OnPropertyChanged(nameof(HasPaging));       // 单页⇄多页切换（如「全部」→100）⇒ 分页链显隐必须重算
        OnPropertyChanged(nameof(HasFooterContent));
        // 页大小变了 ⇒ 当前页码可能越界：先收敛（越界时 setter 自带 RefreshView），没越界才手动重取
        var target = System.Math.Min(System.Math.Max(1, PageIndex), System.Math.Max(1, PageCount));
        if (target != PageIndex) PageIndex = target;
        else if (PagingEnabled && !IsPagingSuspended) RefreshView();
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(HasPaging));
        OnPropertyChanged(nameof(HasFooterContent));
    }

    /// <summary>派生类读完一页数据后调：把 <see cref="PageIndex"/> 收敛回合法范围（末页不足 / 删除后总数变少都不越界）。</summary>
    protected void ClampPageIndex()
    {
        var max = System.Math.Max(1, PageCount);
        if (PageIndex > max) PageIndex = max;   // setter → OnPageIndexChanged → RefreshView 重取收敛后的页
    }

    /// <summary>
    /// 排序 / 筛选条件变了 ⇒ 回到第一页（🔴 ⛔ 不停在旧结果集的第 N 页 = 空页或错页）。
    /// 已在第一页时直接重算。未启用分页的页 = 与 RefreshView() 完全等价。
    /// </summary>
    protected void ResetToFirstPage()
    {
        if (PageIndex != 1) { PageIndex = 1; return; }
        RefreshView();
    }

    [RelayCommand]
    private void FirstPage()
    {
        if (PageIndex > 1) PageIndex = 1;   // setter → RefreshView
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (PageIndex > 1) PageIndex = PageIndex - 1;
    }

    [RelayCommand]
    private void NextPage()
    {
        if (PageIndex < PageCount) PageIndex = PageIndex + 1;
    }

    [RelayCommand]
    private void LastPage()
    {
        if (PageCount > 0 && PageIndex != PageCount) PageIndex = PageCount;
    }

    /// <summary>底栏「跳转到」输入框 / 页码按钮：直达指定页（越界自动收敛）。</summary>
    [RelayCommand]
    private void GoToPage(int page)
    {
        var target = Math.Min(Math.Max(1, page), Math.Max(1, PageCount));
        if (target != PageIndex) PageIndex = target;   // setter → RefreshView
    }

    // ==================== 派生类必须给出的东西 ====================

    /// <summary>本页在 <c>config\columnlayout.json</c> 里的页键（如 <c>"video-list"</c>）。</summary>
    protected abstract string PageKey { get; }

    /// <summary>列元数据（顺序即表头与行的列顺序）。</summary>
    /// <para>⛔ **不要追加 <c>Detail</c> 占位列**（v3.12）：详情开关已从表头挪到工具条末位
    /// （<c>Button.detail-toggle</c>，见规范 §3.6），表头因此不再有那一格。</para>
    protected abstract IReadOnlyList<ListColumn> Columns { get; }

    /// <summary>排序列 / 筛选条件变化后重算列表（派生类的 ApplyView / ApplyFilters）。</summary>
    protected abstract void RefreshView();

    /// <summary>取某行某列的文本（给筛选用）。（WHY: SH-022）</summary>
    protected virtual string? FieldText(object row, string columnKey)
    {
        if (row == null || string.IsNullOrEmpty(columnKey)) return null;
        try
        {
            var p = row.GetType().GetProperty(columnKey);
            return p?.GetValue(row)?.ToString();
        }
        catch { return null; }
    }

    /// <summary>取某行某列的数值（给「大于 / 小于」筛选用）。默认不支持数值比较。</summary>
    protected virtual double? FieldNumber(object row, string columnKey) => null;

    // ==================== 初始化 ====================

}
