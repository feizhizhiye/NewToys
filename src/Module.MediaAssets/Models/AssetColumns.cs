using System.Collections.Generic;
using NewToys.Shared.Models;

namespace NewToys.MediaAssets.Models;

/// <summary>
/// 资产行的**列定义**（键 / 表头 / 宽度 / 示例值），「全部资产」与「资产浏览」两页共用一份。
/// <para>⛔ 两页各写一套会漂移（列键对不上 <see cref="AssetRow"/> 的展示属性 ⇒ 筛选 / 排序静默失效）；
/// 显隐与列宽偏好仍按**各自的页键**落 <c>config\columnlayout.json</c>（本类只管元数据）。</para>
/// </summary>
public static class AssetColumns
{
    /// <summary>全部列（顺序即表头与行的列顺序）。<c>Sample</c> 给的是「典型内容宽度」，用于列宽锁定判定。</summary>
    public static IReadOnlyList<ListColumn> All { get; } = new ListColumn[]
    {
        new("Name", "名称", "2.6*", sample: "The.Matrix.1999.1080p.mkv"),
        new("Type", "类型", "0.8*", sample: "视频"),
        new("Tags", "标签", "1.4*", sample: "科幻, 收藏"),
        new("Categories", "分类", "1.2*", sample: "电影 / 动作"),
        new("Collections", "集合", "1.2*", sample: "周末片单"),
        new("Size", "大小", "0.9*", sample: "4.2 GB"),
        new("Modified", "修改时间", "1.3*", sample: "2026-10-13 21:04"),
        new("Storage", "存储源", "1.2*", sample: "移动硬盘 D"),
        new("Duration", "时长", "0.9*", sample: "02:14:33"),
        new("Resolution", "分辨率", "0.9*", sample: "1920×1080"),
        new("Codec", "编码", "0.9*", sample: "h264"),
        new("Bitrate", "码率", "0.9*", sample: "8.5 Mbps")
    };
}
