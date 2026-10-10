using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shared.Models;

/// <summary>
/// 「设置与脚本 / 快捷设置」两页的<b>归属标识</b>（<see cref="QuickAction.Page"/> 的合法取值）。
/// </summary>
public static class QuickActionPage
{
    /// <summary>「设置与脚本」页：较复杂的设置与脚本，靠命令 / 脚本执行。</summary>
    public const string Shortcuts = "shortcuts";

    /// <summary>「快捷设置」页：系统自带设置入口（Windows 设置面板 / 管理工具），点一下即打开。</summary>
    public const string Quick = "quick";
}

/// <summary>
/// 「设置与脚本」/「快捷设置」里的一条项目：一条<b>系统设置项</b>（注册表开关 / 电源计划 / 控制面板入口 …）
/// 或一段常用<b>脚本</b>命令。
/// <para>
/// ⚠️ 这里的「设置」指 <b>操作系统设置</b>，<b>不是本软件自己的设置项</b>（那种在「设置」模块里）。
/// 两者在本模型里统一成「一条命令 + 元信息」，贴合本工程「个人工具 ≈ 带 GUI 的脚本管理器」的定位。
/// </para>
/// <para>
/// <b>归属</b>由 <see cref="Page"/> 决定：<see cref="QuickActionPage.Shortcuts"/> 出现在「设置与脚本」页、
/// <see cref="QuickActionPage.Quick"/> 出现在「快捷设置」页。两页共用同一份清单文件（<c>config\quickactions.json</c>），
/// 收藏（<see cref="IsFavorite"/>）也是同一条记录上的标记 —— 单一真相源，收藏夹页据此汇总两页的收藏项。
/// </para>
/// <para>
/// <see cref="Command"/> 可由「▶ 运行」直接执行（<c>cmd /c</c>；<see cref="NeedAdmin"/> 为真时走 UAC 提权）。
/// </para>
/// </summary>
public partial class QuickAction : ObservableObject
{
    /// <summary>稳定标识（新建时生成；收藏夹按它引用，改名不受影响）。</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 归属页面（取值见 <see cref="QuickActionPage"/>）。
    /// <para>空串 = 旧版本文件没有这一列，加载时按分类回填（见 <c>QuickActionService.Load</c>）。</para>
    /// </summary>
    public string Page { get; set; } = "";

    /// <summary>名称（列表第一列）。</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>分类（如「系统设置」「网络」「维护」「打开」）；列表可按它排序 / 过滤。</summary>
    [ObservableProperty] private string _category = "";

    /// <summary>简介（一句话说明这条做什么）。</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>命令内容（cmd / PowerShell / 程序 / 网址），「▶ 运行」执行的就是它。</summary>
    [ObservableProperty] private string _command = "";

    /// <summary>执行时是否需要管理员权限（为真则用 UAC 提权运行）。</summary>
    [ObservableProperty] private bool _needAdmin;

    /// <summary>是否已收藏到「本模块 → 收藏夹」。</summary>
    [ObservableProperty] private bool _isFavorite;

    /// <summary>列表「★」列的显示文本（收藏即 ★）。派生自 <see cref="IsFavorite"/>，不写盘。</summary>
    [JsonIgnore]
    public string FavoriteMark => IsFavorite ? "★" : "";

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMark));
}
