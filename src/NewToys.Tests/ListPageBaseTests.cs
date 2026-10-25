using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using Xunit;

namespace NewToys.Tests;

/// <summary>
/// <see cref="ListPageViewModelBase"/> 的**纯逻辑**回归测试。
/// <para>
/// 这个基类被 14 个列表页共用，历史上出过两次线上崩溃 / 一次数据错位：
/// <list type="bullet">
///   <item>「全部」页大小（<c>int.MaxValue</c>）算总页数整数溢出成负数 ⇒ 页码链全乱；</item>
///   <item>软件包页（预设 20/50/100）读到通用页（预设 25/50/100/500）落盘的 25，下拉候选被换 ⇒ 进程崩溃；</item>
///   <item>隐藏列从规格串里删掉而不是写 0 ⇒ 表头与行列索引错位。</item>
/// </list>
/// 三条都写在注释里（现已搬进本文件断言），但注释不会在改代码时报警 —— 这里会。
/// </para>
/// <para>
/// ⛔ 只测纯逻辑：不碰 Avalonia、不碰文件系统、不 new 任何 View。
/// </para>
/// </summary>
public class ListPageBaseTests
{
    // ==================== 测试替身 ====================

    /// <summary>内存版布局存储（列显隐 + 页面级开关）。</summary>
    private sealed class FakeLayout : IColumnLayoutService
    {
        public Dictionary<string, Dictionary<string, bool>> Pages { get; } = new();
        public Dictionary<string, bool> Flags { get; } = new();
        public int SavePageCount { get; private set; }

        public Dictionary<string, bool> LoadPage(string pageKey)
            => Pages.TryGetValue(pageKey, out var v) ? new Dictionary<string, bool>(v) : new();

        public void SavePage(string pageKey, Dictionary<string, bool> visibility)
        {
            SavePageCount++;
            Pages[pageKey] = new Dictionary<string, bool>(visibility);
        }

        public bool LoadFlag(string pageKey, string name, bool defaultValue = false)
            => Flags.TryGetValue(pageKey + "|" + name, out var v) ? v : defaultValue;

        public void SaveFlag(string pageKey, string name, bool value)
            => Flags[pageKey + "|" + name] = value;
    }

    /// <summary>内存版选项存储（页大小记忆走这里）。</summary>
    private sealed class FakeOptions : IOptionsStore
    {
        public Dictionary<string, string?> Data { get; } = new();

        public string? Get(string module, string key)
            => Data.TryGetValue(module + "|" + key, out var v) ? v : null;

        public void Set(string module, string key, string? value) => Data[module + "|" + key] = value;

        public bool Remove(string module, string key) => Data.Remove(module + "|" + key);
    }

    private sealed class Row
    {
        public string Name { get; set; } = "";
        public string Size { get; set; } = "";
    }

    /// <summary>最小派生页：三列，其中 Name 是标识列（不可隐藏）。</summary>
    private sealed class TestPage : ListPageViewModelBase
    {
        private readonly IReadOnlyList<ListColumn> _cols;

        public TestPage(FakeLayout? layout = null, FakeOptions? opts = null)
            : base(layout, opts)
        {
            _cols = new List<ListColumn>
            {
                new("Name", "名称", "2*", sample: "名称"),
                new("Size", "大小", "*",  sample: "1024"),
                new("Note", "备注", "*"),   // 无 sample ⇒ 永不参与锁定
            };
            LoadLayout();          // ⚠️ 必须在 _cols 就绪后
        }

        protected override string PageKey => "test-page";
        protected override IReadOnlyList<ListColumn> Columns => _cols;
        protected override void RefreshView() => RefreshCount++;

        public int RefreshCount { get; private set; }

        // 本页预设**故意**比全局少一档（模拟软件包页），用于验证页大小收敛
        public override IReadOnlyList<int> PageSizeChoices => new[] { 25, 50, 100 };

        // 暴露 protected / 命令给断言
        public bool IsVisible(string key) => Vis(key);
        public string Specs => VisibleColumnSpecs;
        public string LockMask => LockedColumnMask;
        public void Toggle(string key) => ToggleColumnCommand.Execute(key);
        public void GoPage(int page) => GoToPageCommand.Execute(page);
        public string? TextOf(object row, string key) => FieldText(row, key);
        public IEnumerable<T> Filter<T>(IEnumerable<T> rows) => ApplyFilters(rows);
        public void PageSizeRaw(int value) => PageSize = value;
    }

    // ==================== 分页：总页数（溢出是历史崩溃点） ====================

    [Fact]
    public void PageCount_NoData_IsZero_AndTextIsEmpty()
    {
        var p = new TestPage();
        p.TotalCount = 0;

        Assert.Equal(0, p.PageCount);
        Assert.Equal("无数据", p.PageText);
        Assert.False(p.HasPaging);
    }

    [Theory]
    [InlineData(100, 25, 4)]     // 整除
    [InlineData(101, 25, 5)]     // 余 1 条也要多一页
    [InlineData(25, 25, 1)]
    [InlineData(1, 25, 1)]
    public void PageCount_CeilsRemainder(int total, int pageSize, int expected)
    {
        var p = new TestPage();
        p.PageSize = pageSize;
        p.TotalCount = total;

        Assert.Equal(expected, p.PageCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void PageCount_AllPageSizeSentinel_NeverOverflows(int total)
    {
        // 🔴 历史 bug：「全部」= int.MaxValue，(Total + Size - 1) 会溢出成负数。
        //    这里断言的是**性质**：任何 TotalCount 下总页数都不为负，且选了「全部」就只有 1 页。
        var p = new TestPage();
        p.PageSize = ListPageViewModelBase.AllPageSize;
        p.TotalCount = total;

        Assert.True(p.PageCount >= 0, $"总页数为负：{p.PageCount}");
        Assert.Equal(total == 0 ? 0 : 1, p.PageCount);
    }

    [Fact]
    public void PageSize_NonPositive_FallsBackToDefault()
    {
        var p = new TestPage();
        p.PageSizeRaw(0);

        Assert.Equal(ListPageViewModelBase.DefaultPageSize, p.PageSize);
    }

    [Fact]
    public void GoToPage_ClampsOutOfRange()
    {
        var p = new TestPage();
        p.PageSize = 25;
        p.TotalCount = 100;      // 4 页

        p.GoPage(999);
        Assert.Equal(4, p.PageIndex);

        p.GoPage(-5);
        Assert.Equal(1, p.PageIndex);
    }

    // ==================== 页大小记忆：跨页预设不同时的收敛 ====================

    [Fact]
    public void PageSize_SavedValueInsidePresets_IsRestored()
    {
        var opts = new FakeOptions();
        opts.Set("ui", "list-page-size", "50");

        var p = new TestPage(opts: opts);

        Assert.Equal(50, p.PageSize);
    }

    [Fact]
    public void PageSize_SavedValueOutsidePresets_SnapsToFirstChoice()
    {
        // 🔴 历史崩溃点：全局落盘 500（通用页有这档），本页预设 25/50/100 没有 ⇒
        //    底栏把 500 补进下拉当默认项，用户再点任一预设时 ItemsSource 被换 ⇒ 弹层选中索引越界。
        var opts = new FakeOptions();
        opts.Set("ui", "list-page-size", "500");

        var p = new TestPage(opts: opts);

        Assert.Equal(25, p.PageSize);
        Assert.Contains(p.PageSize, p.PageSizeChoices);
    }

    [Fact]
    public void PageSize_ChangingWritesThrough_ButConstructorDoesNot()
    {
        var opts = new FakeOptions();
        opts.Set("ui", "list-page-size", "50");

        var p = new TestPage(opts: opts);
        // 构造期读盘不该反向覆盖全局值
        Assert.Equal("50", opts.Get("ui", "list-page-size"));

        p.PageSize = 100;
        Assert.Equal("100", opts.Get("ui", "list-page-size"));
    }

    // ==================== 列规格串：隐藏列必须写 0 占位 ====================

    [Fact]
    public void ColumnSpecs_Default_FollowsColumnMetadata()
    {
        var p = new TestPage();

        Assert.Equal("2*,*,*", p.Specs);
    }

    [Fact]
    public void ColumnSpecs_HiddenColumn_WritesZero_AndKeepsSlot()
    {
        // 🔴 关键不变量：隐藏 = 宽度 0，**不是**从串里删掉。
        //    删掉会让这一列之后的所有列索引左移 ⇒ 表头与行错位（历史 bug）。
        var p = new TestPage();

        p.Toggle("Size");

        Assert.Equal("2*,0,*", p.Specs);
        Assert.Equal(3, p.Specs.Split(',').Length);   // 列数不变
        Assert.False(p.IsVisible("Size"));
    }

    [Fact]
    public void ToggleColumn_IdentityColumn_CannotBeHidden()
    {
        var p = new TestPage();

        p.Toggle("Name");     // Name = 标识列（MandatoryColumnKey 自动识别）

        Assert.True(p.IsVisible("Name"));
        Assert.Equal("2*,*,*", p.Specs);
    }

    [Fact]
    public void ToggleColumn_PersistsVisibility()
    {
        var layout = new FakeLayout();
        var p = new TestPage(layout);

        p.Toggle("Note");

        Assert.True(layout.SavePageCount > 0);
        Assert.False(layout.Pages["test-page"]["Note"]);
    }

    [Fact]
    public void ToggleColumn_Twice_RestoresVisibility()
    {
        var p = new TestPage();

        p.Toggle("Size");
        p.Toggle("Size");

        Assert.True(p.IsVisible("Size"));
        Assert.Equal("2*,*,*", p.Specs);
    }

    // ==================== 列宽：手动拖 / 锁定 ====================

    [Fact]
    public void ResizeColumns_AdjacentColumns_AppliesBothWidths()
    {
        var p = new TestPage();

        p.ResizeColumns(0, 180, 1, 120);

        Assert.Equal("180,120,*", p.Specs);
    }

    [Fact]
    public void ResizeColumns_NonAdjacent_IsIgnored()
    {
        // 只允许拖「第 i 列与第 i+1 列」之间的边界；跳列 = 非法输入
        var p = new TestPage();

        p.ResizeColumns(0, 180, 2, 120);

        Assert.Equal("2*,*,*", p.Specs);
    }

    [Fact]
    public void LockColumnsBySample_LocksWhenSampleNotWiderThanHeader()
    {
        // 示例 "名称" 宽 ≤ 表头 "名称" 宽 ⇒ 锁成表头宽；
        // 示例 "1024" 窄于表头 "大小" ⇒ 也锁，但不低于 MinLockedWidth；
        // 第三列没有 sample ⇒ 不参与。
        var p = new TestPage();

        p.LockColumnsBySample(
            headerWidths: new[] { 60d, 30d, 40d },
            sampleWidths: new[] { 40d, 20d, 999d });

        Assert.Equal("60,34,*", p.Specs);        // 30 < MinLockedWidth(34) ⇒ 取 34
        Assert.Equal("110", p.LockMask);         // 前两列锁定
    }

    [Fact]
    public void LockColumnsBySample_SampleWiderThanHeader_DoesNotLock()
    {
        var p = new TestPage();

        p.LockColumnsBySample(
            headerWidths: new[] { 60d, 30d, 40d },
            sampleWidths: new[] { 200d, 300d, 400d });

        Assert.Equal("2*,*,*", p.Specs);
        Assert.Equal("000", p.LockMask);
    }

    [Fact]
    public void ResizeColumns_LockedColumn_IsRejected()
    {
        // 锁定列 = 内容没超过表头宽 ⇒ 不可拖（用户要求：锁定的列不参与任何自动调整）
        var p = new TestPage();
        p.LockColumnsBySample(
            headerWidths: new[] { 60d, 30d, 40d },
            sampleWidths: new[] { 40d, 20d, 999d });

        p.ResizeColumns(0, 300, 1, 300);

        Assert.Equal("60,34,*", p.Specs);        // 没被改动
    }

    // ==================== 筛选：默认反射取值 + 「且」语义 ====================

    [Fact]
    public void FieldText_DefaultImplementation_ReadsPropertyByName()
    {
        var p = new TestPage();
        var row = new Row { Name = "abc", Size = "1.2 MB" };

        Assert.Equal("abc", p.TextOf(row, "Name"));
        Assert.Equal("1.2 MB", p.TextOf(row, "Size"));
    }

    [Fact]
    public void FieldText_UnknownColumn_ReturnsNull()
    {
        var p = new TestPage();

        Assert.Null(p.TextOf(new Row(), "NoSuchProperty"));
    }

    [Fact]
    public void ApplyFilters_UnknownColumnKey_DoesNotDropRows()
    {
        // 🔴 设计选择：列键与模型属性对不上时，这条条件「不生效」，而不是把所有行筛掉。
        //    否则改个列键就会让整页变空白，且没有任何报错。
        var p = new TestPage();
        p.Filters.Add(new FilterClause
        {
            Column = new ListColumn("Ghost", "幽灵", "*"),
            Op = FilterOperators.Contains,
            Value = "zzz",
        });

        var rows = new[] { new Row { Name = "a" }, new Row { Name = "b" } };

        Assert.Equal(2, p.Filter(rows).Count());
    }

    [Fact]
    public void ApplyFilters_MultipleClauses_AreAnded()
    {
        var p = new TestPage();
        p.Filters.Add(new FilterClause
        {
            Column = new ListColumn("Name", "名称", "*"),
            Op = FilterOperators.Contains,
            Value = "git",
        });
        p.Filters.Add(new FilterClause
        {
            Column = new ListColumn("Size", "大小", "*"),
            Op = FilterOperators.Contains,
            Value = "MB",
        });

        var rows = new[]
        {
            new Row { Name = "git",   Size = "2 MB" },   // ✔ 两条都中
            new Row { Name = "git",   Size = "2 KB" },   // ✘ 大小不中
            new Row { Name = "maven", Size = "2 MB" },   // ✘ 名称不中
        };

        var hit = p.Filter(rows).ToList();

        Assert.Single(hit);
        Assert.Equal("2 MB", hit[0].Size);
    }

    [Fact]
    public void ApplyFilters_InactiveClause_IsSkipped()
    {
        // 空值的条件 = 不生效（"先加条件后填值"不能瞬间把列表筛空）
        var p = new TestPage();
        p.Filters.Add(new FilterClause
        {
            Column = new ListColumn("Name", "名称", "*"),
            Op = FilterOperators.Contains,
            Value = "   ",
        });

        var rows = new[] { new Row { Name = "a" }, new Row { Name = "b" } };

        Assert.Equal(2, p.Filter(rows).Count());
    }

    [Fact]
    public void ActiveFilterCount_IgnoresInactiveClauses()
    {
        var p = new TestPage();
        p.Filters.Add(new FilterClause
        {
            Column = new ListColumn("Name", "名称", "*"),
            Op = FilterOperators.Contains,
            Value = "",
        });
        p.Filters.Add(new FilterClause
        {
            Column = new ListColumn("Size", "大小", "*"),
            Op = FilterOperators.Empty,      // 不需要值 ⇒ 立即生效
        });

        Assert.Equal(1, p.ActiveFilterCount);
        Assert.Equal(2, p.Filters.Count);
    }

    // ==================== 详情栏开合：落盘 ====================

    [Fact]
    public void ToggleDetail_PersistsFlag()
    {
        var layout = new FakeLayout();
        var p = new TestPage(layout);

        p.ToggleDetailCommand.Execute(null);

        Assert.True(p.IsDetailVisible);
        Assert.True(layout.Flags["test-page|" + IColumnLayoutService.FlagDetailPanel]);
    }

    [Fact]
    public void DetailFlag_RestoredOnLoadLayout()
    {
        var layout = new FakeLayout();
        layout.SaveFlag("test-page", IColumnLayoutService.FlagDetailPanel, true);

        var p = new TestPage(layout);

        Assert.True(p.IsDetailVisible);
    }
}
