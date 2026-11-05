using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.MediaAssets.Models;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>「按条件铺一整套结果」的请求（标签详情卡 → 资产浏览页）：页面不再按目录枚举， 改用索引里符合 TagName + Type 的资产，页内提示用 Title。</summary>
/// <param name="Title">页内提示文案（例：<c>标签「科幻」· 视频</c>）。</param>
/// <param name="TagName">标签名（按名字过滤，与「全部资产」页标签下拉同一口径）。</param>
/// <param name="Type">资产类型原始值（<c>video</c> / <c>image</c> / <c>music</c>…）；<c>null</c> = 不限类型。</param>
public sealed record ResultSetRequest(string Title, string? TagName, string? Type);

/// <summary>跨页投递：模块内单例，仿视频工具 VideoTaskStore 的做法。（WHY: MA-018）</summary>
public class AssetNavigationStore
{
    private readonly INavigationService? _nav;

    public AssetNavigationStore() { }

    public AssetNavigationStore(INavigationService? nav) => _nav = nav;

    private readonly object _gate = new();
    private (string MenuKey, long AssetId)? _pending;
    private (string MenuKey, ResultSetRequest Request)? _pendingSet;

    /// <summary>登记一次「跳到某页并选中某资产」。后登记的覆盖先登记的（只有最后一次有意义）。</summary>
    public void Request(string menuKey, long assetId)
    {
        if (string.IsNullOrWhiteSpace(menuKey) || assetId <= 0) return;
        lock (_gate) _pending = (menuKey, assetId);
    }

    /// <summary>
    /// 登记一次「跳到某页并按这个条件铺一批结果」（标签 → 资产浏览：只显示这个标签下的视频）。
    /// <para>与 <see cref="Request"/> 并列、互不覆盖：一个是「定位到某一项」，一个是「换一整套列表」。</para>
    /// </summary>
    public void RequestResultSet(string menuKey, ResultSetRequest request)
    {
        if (string.IsNullOrWhiteSpace(menuKey) || request is null) return;
        lock (_gate) _pendingSet = (menuKey, request);
    }

    /// <summary>取走结果集请求（取完即清空）；指定的页对不上时也清空并返回 <c>null</c>。</summary>
    public ResultSetRequest? TakeResultSet(string menuKey)
    {
        lock (_gate)
        {
            if (!_pendingSet.HasValue) return null;
            var p = _pendingSet.Value;
            _pendingSet = null;
            return string.Equals(p.MenuKey, menuKey, StringComparison.Ordinal) ? p.Request : null;
        }
    }

    /// <summary>跳到某页（⛔ 不选资产；任务入队后看进度用）。</summary>
    public void NavigateTo(string menuKey)
    {
        if (!string.IsNullOrWhiteSpace(menuKey)) _nav?.NavigateTo(menuKey);
    }

    /// <summary>取件（取完即清空）；没有待处理项时返回 <c>null</c>。</summary>
    public (string MenuKey, long AssetId)? Take()
    {
        lock (_gate)
        {
            var p = _pending;
            _pending = null;
            return p;
        }
    }

    /// <summary>有没有待取件（页面可用它决定是否显示提示）。</summary>
    public bool HasPending
    {
        get { lock (_gate) return _pending != null; }
    }

    /// <summary>按 id 在行集合里定位下标（取件后选中用）；找不到返回 -1。</summary>
    public static int IndexOf(IEnumerable<AssetRow> rows, long assetId)
    {
        var i = 0;
        foreach (var r in rows)
        {
            if (r.Id == assetId) return i;
            i++;
        }
        return -1;
    }
}
