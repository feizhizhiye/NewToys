using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewToys.MediaAssets.Models;
using NewToys.MediaAssets.Services;

namespace NewToys.MediaAssets.ViewModels;

/// <summary>
/// 「资产浏览」页的**索引结果集模式**：不枚举目录，改用索引里符合条件的一批资产铺成缩略图卡。
/// <para>
/// 入口 = 标签详情卡的「🎬 视频 → 资产浏览」：投递 <see cref="ResultSetRequest"/>，本页挂上树时取件后进入本模式。
/// </para>
/// <para>
/// 🔴 只换**列表来源**与左树显隐，翻页 / 缩略图 / 类型筛选 / 搜索 / 排序 / 详情卡全部复用目录模式的实现
/// —— 否则「按标签浏览」会变成第二套浏览页，两套交互迟早漂移。
/// </para>
/// </summary>
public partial class AssetBrowseViewModel
{
    /// <summary>本页在菜单里的键（结果集请求按它配对）。</summary>
    private const string ResultSetMenuKey = "assets-browse";

    /// <summary>结果集一次最多取多少条（缩略图解码是有成本的，几千条会拖住列表）。</summary>
    private const int ResultSetLimit = 2000;

    private ResultSetRequest? _resultSet;

    /// <summary>是否处于结果集模式（左树隐藏、工具条出现「退出结果集」）。</summary>
    public bool IsResultSetMode => _resultSet != null;

    /// <summary>结果集标题（如 <c>标签「科幻」· 视频</c>）；目录模式下为空串。</summary>
    public string ResultSetTitle => _resultSet?.Title ?? "";

    /// <summary>
    /// 取件点（由 View 在 <c>OnAttachedToVisualTree</c> 调 —— 🔴 不是 <c>OnDataContextChanged</c>：
    /// 切回来的那次只有可视树重挂）。没有待处理请求时什么都不做，页面保持原样。
    /// </summary>
    public void ConsumePendingResultSet()
    {
        var req = _navStore?.TakeResultSet(ResultSetMenuKey);
        if (req == null) return;

        _resultSet = req;
        IsTreeVisible = false;      // 结果集跨目录 ⇒ 左树没有对应的位置可高亮
        SelectedNode = null;
        Selected = null;
        SearchText = "";

        OnPropertyChanged(nameof(IsResultSetMode));
        OnPropertyChanged(nameof(ResultSetTitle));

        ResetToFirstPage();         // 第一页时它自己会调 RefreshView → ApplyView
        StatusText = $"{req.Title}：结果集模式（左树已收起，点「退出结果集」回到目录浏览）。";
    }

    /// <summary>清掉结果集状态（⛔ 不刷列表；调用方负责后续取数）。</summary>
    private void ClearResultSetState()
    {
        if (_resultSet == null) return;

        _resultSet = null;
        OnPropertyChanged(nameof(IsResultSetMode));
        OnPropertyChanged(nameof(ResultSetTitle));
    }

    /// <summary>
    /// 退出结果集模式，回到目录浏览（恢复上次的目录）。
    /// <para>用户直接点左树节点时也会走这里 —— 否则结果集模式会一直压着目录，树点了没反应。</para>
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ExitResultSet()
    {
        if (_resultSet == null) return;

        ClearResultSetState();
        IsTreeVisible = true;

        Reload(restoreLastFolder: true);   // 重建左树 + 恢复上次目录（会顺带刷列表）
        StatusText = "已退出结果集，回到目录浏览。";
    }

    /// <summary>结果集模式下的取数：走与「全部资产」同一套 <see cref="AssetService.AssetQuery"/>（WHERE 只写一份）。</summary>
    private async Task LoadResultSetAsync(int version, CancellationToken thumbCt)
    {
        var req = _resultSet!;
        var query = new AssetService.AssetQuery(Type: req.Type, TagName: req.TagName, Limit: ResultSetLimit);

        List<AssetRow> rows;
        try { rows = await Task.Run(() => _assets.QueryPage(query)).ConfigureAwait(true); }
        catch { rows = new List<AssetRow>(); }

        if (version != _viewVersion) return;   // 期间又刷了一次 ⇒ 本次作废

        var width = IconSize;
        var all = new List<AssetTile>(rows.Count);
        foreach (var row in rows)
        {
            // locationId 传 0：结果集只用于浏览 / 打开（按绝对路径），批量库操作请回「全部资产」页
            all.Add(new AssetTile(row, 0, "", row.FilePath, true, false, width));
        }

        _dirCount = all.Count;

        var list = FilterAndSort(all, FilterType?.Value ?? "", SearchText, SortColumn, SortDescending);

        IsBusy = false;
        TotalCount = list.Count;
        var before = PageIndex;
        ClampPageIndex();
        if (PageIndex != before) return;       // 页码收敛后已按新页重取

        var offset = (long)(PageIndex - 1) * PageSize;
        var page = list.Skip((int)System.Math.Min(offset, int.MaxValue)).Take(PageSize).ToList();

        FillChunk(page, 0, version, thumbCt);

        SyncChoiceSelection();
        UpdateHeaders();
        NotifyItemsChanged();

        if (_assets.LastQueryError.Length > 0) StatusText = "⚠ 查询失败：" + _assets.LastQueryError;
        else StatusText = $"{(req.Title)}：共 {TotalCount} 条（结果集模式）。";
    }
}
