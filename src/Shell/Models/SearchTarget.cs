using System;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Models;

/// <summary>搜索命中的目标类型（决定选中后走哪条导航路径 —— 但两条最终都归一成 <c>OpenTab(key)</c>）。</summary>
public enum SearchTargetKind
{
    /// <summary>二级菜单项 / 独立页（含模块设置页本身）：Key = 菜单项 Key。</summary>
    MenuItem,

    /// <summary>应用级设置页的**节**：Key = 节锚点，<c>OpenTab</c> 会归一成设置页并展开该节。</summary>
    SettingsSection,
}

/// <summary>
/// 标题栏搜索的一条结果（NT-066）：统一「二级菜单项」与「设置页节点」两种目标，
/// ⛔ 不搜模块私有内容（资产名 / 脚本名）。
/// <para>
/// 索引由 <see cref="ShellViewModel"/> 现场构建（菜单项来自 <c>IMenuService.Groups</c>，
/// 设置节来自 <see cref="SettingsViewModel.Catalog"/>）⇒ 热加载模块后自动生效，⛔ 无静态缓存。
/// </para>
/// </summary>
public sealed class SearchTarget
{
    /// <summary>导航 Key（菜单项 Key / 节锚点）。</summary>
    public string Key { get; init; } = "";

    /// <summary>主标题（页名 / 节名）—— 结果列表第一行。</summary>
    public string Title { get; init; } = "";

    /// <summary>副标题（所属分组 + 一句话简介 / 「设置 · 简介」）—— 结果列表第二行。</summary>
    public string Detail { get; init; } = "";

    /// <summary>结果类型（`页面` / `设置项`），列表右侧灰字。</summary>
    public string KindLabel { get; init; } = "";

    public SearchTargetKind Kind { get; init; }

    /// <summary>
    /// 匹配得分（0 = 不命中）：标题相等 &gt; 标题前缀 &gt; 标题子串 &gt; 副标题子串。
    /// 🔴 只做**子串匹配**（大小写不敏感）—— 拼音 / 首字母匹配成本明显更高，本轮不做（卡内待拍板 3）。
    /// </summary>
    public int MatchScore(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return 0;

        if (string.Equals(Title, keyword, StringComparison.CurrentCultureIgnoreCase)) return 100;
        if (Title.StartsWith(keyword, StringComparison.CurrentCultureIgnoreCase)) return 80;
        if (Title.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)) return 60;
        if (Detail.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)) return 30;
        return 0;
    }
}
