using System;
using System.Collections.Generic;
using System.Linq;

namespace NewToys.ScriptManager.Models;

/// <summary>
/// 随包<b>预设规则集</b>的一条（NT-086 批 E）：一个 <c>.py</c> 文件 + 元数据 + 探测路径。
/// <para>
/// 预设由 <see cref="Services.PresetSeeder"/> 播种进 <c>&lt;DataDir&gt;\script\rules\</c>（只播种一次），
/// 「从预设创建」= 把预设文件复制成一条新规则集（⛔ 不覆盖同名）。
/// </para>
/// <para>
/// 🔴 <see cref="ProbePaths"/> 是**站点根**的相对路径（探测用）；空 = 兜底预设（任意页面都能用，⛔ 不探测）。
/// </para>
/// </summary>
public sealed class PresetInfo
{
    /// <summary>预设 id（= 规则文件名，如 <c>rss</c>）。</summary>
    public string Id { get; init; } = "";

    /// <summary>下拉 / 推荐里显示的标题。</summary>
    public string Title { get; init; } = "";

    /// <summary>一句话说明（工具条悬停 / 状态栏用）。</summary>
    public string Desc { get; init; } = "";

    /// <summary>「从预设创建」时的规则集名基座（重名自动加序号）。</summary>
    public string NewNameBase { get; init; } = "";

    /// <summary>探测路径（站点根相对；空 = 兜底预设不参与探测）。</summary>
    public IReadOnlyList<string> ProbePaths { get; init; } = Array.Empty<string>();

    /// <summary>P0 七个预设（顺序 = 推荐时的平手裁决顺序）。</summary>
    public static readonly IReadOnlyList<PresetInfo> All = new[]
    {
        new PresetInfo
        {
            Id = "rss", Title = "RSS / Atom 订阅", NewNameBase = "RSS 源",
            Desc = "抓 RSS 2.0 / Atom 订阅源条目（标题 + 链接 + 日期）",
            ProbePaths = new[] { "/feed", "/rss.xml", "/atom.xml" }
        },
        new PresetInfo
        {
            Id = "opds", Title = "OPDS 书目录", NewNameBase = "OPDS 目录",
            Desc = "抓 OPDS 电子书目录条目（书名 + 详情链接 + 作者）",
            ProbePaths = new[] { "/opds" }
        },
        new PresetInfo
        {
            Id = "sitemap", Title = "Sitemap 全站", NewNameBase = "Sitemap 站点",
            Desc = "抓 sitemap.xml 里的全部网址（拿全站链接清单）",
            ProbePaths = new[] { "/sitemap.xml" }
        },
        new PresetInfo
        {
            Id = "wp-json", Title = "WordPress API", NewNameBase = "WordPress 站点",
            Desc = "抓 WordPress /wp-json/wp/v2/posts 文章（标题 + 链接 + 发布时间）",
            ProbePaths = new[] { "/wp-json/wp/v2/posts?per_page=1" }
        },
        new PresetInfo
        {
            Id = "mediawiki", Title = "MediaWiki API", NewNameBase = "MediaWiki 站点",
            Desc = "抓 MediaWiki 全站页面清单（标题 + 页面完整网址）",
            ProbePaths = new[]
            {
                "/api.php?action=query&generator=allpages&gaprefix=A&gaplimit=10&prop=info&inprop=url&format=json"
            }
        },
        new PresetInfo
        {
            Id = "links", Title = "页面链接（兜底）", NewNameBase = "页面链接",
            Desc = "兜底：把任意网页的所有链接抓成条目"
        },
        new PresetInfo
        {
            Id = "images", Title = "页面图片（兜底）", NewNameBase = "页面图片",
            Desc = "兜底：把任意网页的所有图片抓成条目"
        },
    };

    /// <summary>按 id 找预设（找不到返回 null）。</summary>
    public static PresetInfo? Find(string? id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
