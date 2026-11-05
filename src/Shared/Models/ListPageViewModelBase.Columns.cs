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

public abstract partial class ListPageViewModelBase : ObservableObject, IStatusProvider, IToolbarSource
{

    /// <summary>
    /// 列元数据的只读视图。
    /// <para>给视图层用（<c>NewToys.UI.Controls.TableViewHost</c> 按下标对齐表头文案 / 列显隐 / 排序键）；
    /// <see cref="Columns"/> 本身保持 protected，派生类不必为视图放开可见性。</para>
    /// </summary>
    public IReadOnlyList<ListColumn> ColumnLayout => Columns;

    /// <summary>
    /// 读回上次保存的列显隐与详情栏开合状态。
    /// ⚠️ 必须等派生类的 <see cref="Columns"/> 就绪后再调（所以放在派生构造函数里，而不是基类构造里）。
    /// </summary>
    protected void LoadLayout()
    {
        LoadVisibility();
        IsDetailVisible = _cols?.LoadFlag(PageKey, IColumnLayoutService.FlagDetailPanel) ?? false;
        SeedStandardQuickFilters();   // v3.19：表头右键「筛选」子菜单的标准项（页有分类再 SetQuickFilters 覆盖）
    }

    private void LoadVisibility()
    {
        if (_cols == null) return;
        var saved = _cols.LoadPage(PageKey);
        if (saved.Count == 0) return;
        foreach (var c in Columns)
            if (saved.TryGetValue(c.Key, out var v)) _visibility[c.Key] = v;
        NotifyColumnsChanged();
    }

    /// <summary>落盘本页列显隐。（WHY: SH-014）</summary>
    private void SaveVisibility()
    {
        if (_cols == null) return;
        var map = _cols.LoadPage(PageKey);
        foreach (var c in Columns) map[c.Key] = Vis(c.Key);
        _cols.SavePage(PageKey, map);
    }

    /// <summary>把「IsXxxVisible」这批具名属性整体发一次通知（加载 / 复位后让 XAML 跟上）。</summary>
    protected void NotifyColumnsChanged()
    {
        foreach (var c in Columns) OnPropertyChanged("Is" + c.Key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>
    /// **必须始终可见的列**（右键菜单里不可隐藏、<see cref="VisibleColumnSpecs"/> 恒给它宽度）。
    /// 默认自动识别「名称 / 标题 / 主机」这类标识列；派生类可 override 明确指定。
    /// </summary>
    protected virtual string? MandatoryColumnKey
    {
        get
        {
            foreach (var c in Columns)
                if (c.Key is "Name" or "Title" or "Host"
                    || c.Header.Contains("名称") || c.Header.Contains("标题"))
                    return c.Key;
            return null;
        }
    }

    /// <summary>读某列当前是否可见（未在字典里 = 默认可见；标识列恒可见）。供派生类的具名属性直通。</summary>
    protected bool Vis(string key)
        => key == MandatoryColumnKey || !_visibility.TryGetValue(key, out var v) || v;

    // ==================== 列宽规格（表头与行共用同一份） ====================

    /// <summary>
    /// 「手动列宽」（v3.20，需求 §6.4.15）：列键 → 像素宽。拖表头边界产生，只存会话（⛔ 不落盘）。
    /// 不在这里的列 = 用 <see cref="Columns"/> 元数据里的星号宽度。
    /// </summary>
    private readonly Dictionary<string, double> _manualWidths = new();

    /// <summary>
    /// 「锁定列宽」（v3.22，需求 §4.8.1）：`ListColumn.Sample` 的示例文本宽 ≤ 表头宽 → 该列锁成表头宽（且不窄于 34px）。
    /// 锁定的列**不可拖拽**、不参与任何自动调整。
    /// </summary>
    private readonly Dictionary<string, double> _lockedWidths = new();

    /// <summary>按「示例值宽 ≤ 表头宽」把列锁成表头宽（不可拖、不自动调）。</summary>
    public void LockColumnsBySample(IReadOnlyList<double>? headerWidths, IReadOnlyList<double>? sampleWidths)
    {
        if (headerWidths == null) return;

        var changed = false;
        for (var i = 0; i < Columns.Count && i < headerWidths.Count; i++)
        {
            var col = Columns[i];
            if (string.IsNullOrEmpty(col.Sample)) continue;          // 没给示例值 → 不参与判定
            if (_manualWidths.ContainsKey(col.Key)) continue;        // 用户手调过（拖 / 双击自适应）→ 不再锁
            var hw = headerWidths[i];
            if (hw <= 0) continue;                                   // 隐藏列 / 还没测到
            var sw = sampleWidths != null && i < sampleWidths.Count ? sampleWidths[i] : double.MaxValue;
            if (sw > hw) continue;                                   // 示例比表头还宽 → 不锁

            var w = Math.Max(hw, MinLockedWidth);
            if (_lockedWidths.TryGetValue(col.Key, out var old) && Math.Abs(old - w) <= 0.5) continue;
            _lockedWidths[col.Key] = w;
            changed = true;
        }
        if (!changed) return;

        OnPropertyChanged(nameof(VisibleColumnSpecs));
        OnPropertyChanged(nameof(LockedColumnMask));
    }

    /// <summary>锁定的最小列宽（px）—— 保证「已收藏」这类表头很短的列也留得下拖拽把手。</summary>
    public const double MinLockedWidth = 34;

    /// <summary>
    /// 示例值文本串（';' 分隔，给表头 <c>ColumnGrid.SampleSpecs</c> 测量用；⛔ 别用逗号 —— 示例值里可能有逗号）。
    /// </summary>
    public string SampleSpecs => string.Join(';', Columns.Select(c => (c.Sample ?? "").Replace(';', ' ')));

    /// <summary>
    /// 锁定列的掩码串（给表头 <c>ColumnGrid.LockedColumns</c> 用）：每位 '1' = 该列已锁定（边界不放拖拽把手）。
    /// </summary>
    public string LockedColumnMask
    {
        get
        {
            var chars = new char[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
                chars[i] = _lockedWidths.ContainsKey(Columns[i].Key) ? '1' : '0';
            return new string(chars);
        }
    }

    private bool IsLocked(int index)
        => index >= 0 && index < Columns.Count && _lockedWidths.ContainsKey(Columns[index].Key);

    /// <summary>拖动表头列边界 / 双击自适应 → 改这一列的像素宽（由 ColumnGrid 直接调 DataContext）。</summary>
    public void ResizeColumns(int leftIndex, double leftWidthPx, int rightIndex, double rightWidthPx)
    {
        if (leftIndex < 0 || rightIndex != leftIndex + 1) return;
        if (leftIndex >= Columns.Count || rightIndex >= Columns.Count) return;
        if (leftWidthPx < 0 || rightWidthPx < 0) return;

        var unlocked = _lockedWidths.Remove(Columns[leftIndex].Key);
        unlocked |= _lockedWidths.Remove(Columns[rightIndex].Key);

        _manualWidths[Columns[leftIndex].Key] = leftWidthPx;
        _manualWidths[Columns[rightIndex].Key] = rightWidthPx;
        OnPropertyChanged(nameof(VisibleColumnSpecs));
        if (unlocked) OnPropertyChanged(nameof(LockedColumnMask));
    }

    /// <summary>
    /// 列宽规格串：可见列写它的宽度（手动像素优先，否则星号比例），隐藏列写 <c>"0"</c>（列索引保持不动 → 表头与行永不错位）。
    /// ⚠️ 必须做成字符串：<c>Grid.ColumnDefinitions</c> 是 CLR 属性，XAML 里不能写 {Binding}（AVLN3000），
    /// 故由各模块的 <c>ColumnGrid</c> 控件解析。
    /// </summary>
    public string VisibleColumnSpecs
    {
        get
        {
            var parts = new string[Columns.Count];
            for (var i = 0; i < Columns.Count; i++)
            {
                var key = Columns[i].Key;
                // 优先级：隐藏 0 → 锁定（表头宽）→ 手动（拖出来的）→ 星号元数据
                parts[i] = !Vis(key) ? "0"
                    : _lockedWidths.TryGetValue(key, out var lw)
                        ? ((int)Math.Round(lw)).ToString(CultureInfo.InvariantCulture)
                    : _manualWidths.TryGetValue(key, out var w)
                        ? ((int)Math.Round(w)).ToString(CultureInfo.InvariantCulture)
                        : Columns[i].Width;
            }
            return string.Join(",", parts);
        }
    }

    /// <summary>清掉手动 / 锁定列宽，回到列元数据里的星号宽度（表头右键「恢复默认列宽」）。</summary>
    [RelayCommand]
    private void ResetColumnWidths()
    {
        _manualWidths.Clear();
        _lockedWidths.Clear();
        OnPropertyChanged(nameof(VisibleColumnSpecs));
        OnPropertyChanged(nameof(LockedColumnMask));
    }

    /// <summary>右键表头勾选 / 取消勾选某列（增删表头项）；结果落 <c>config\columnlayout.json</c>。标识列（名称）不可隐藏。</summary>
    [RelayCommand]
    private void ToggleColumn(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (key == MandatoryColumnKey) return;   // v3.19：标识列（名称）必须显示，忽略隐藏请求
        _visibility[key] = !Vis(key);
        SaveVisibility();
        OnPropertyChanged("Is" + key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    /// <summary>把某一列强制设为可见 / 不可见（派生类初始化时用）。</summary>
    protected void SetColumnVisible(string key, bool visible)
    {
        _visibility[key] = visible;
        OnPropertyChanged("Is" + key + "Visible");
        OnPropertyChanged(nameof(VisibleColumnSpecs));
    }

    // ==================== 右栏详情开合 ====================

    /// <summary>是否显示右侧详情栏。<b>默认收起</b>；用户的点选落盘，下次进本页保持。</summary>
    [ObservableProperty] private bool _isDetailVisible;

}
