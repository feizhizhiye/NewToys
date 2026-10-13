using System;
using System.Collections.Generic;
using System.Linq;
using NewToys.MediaAssets.Models;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 跨页投递（NT-018）：模块内**单例**，仿视频工具 <c>VideoTaskStore</c> 的做法。
/// <para>
/// 场景：在「标签管理」详情卡里点某个资产 → 想跳到它所在的页并选中它。<see cref="Request"/> 登记，
/// 目标页在 <c>OnAttachedToVisualTree</c>（🔴 **不是** `OnDataContextChanged`，规范 §4.8 硬规则 8）
/// 调 <see cref="Take"/> 取件；取完即清空（⛔ 不重复消费）。
/// </para>
/// </summary>
public class AssetNavigationStore
{
    private readonly object _gate = new();
    private (string MenuKey, long AssetId)? _pending;

    /// <summary>登记一次「跳到某页并选中某资产」。后登记的覆盖先登记的（只有最后一次有意义）。</summary>
    public void Request(string menuKey, long assetId)
    {
        if (string.IsNullOrWhiteSpace(menuKey) || assetId <= 0) return;
        lock (_gate) _pending = (menuKey, assetId);
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
