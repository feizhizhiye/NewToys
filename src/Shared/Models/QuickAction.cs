using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shared.Models;

/// <summary>
/// 「设置与脚本」里的一条项目：一条<b>系统设置项</b>（注册表开关 / 电源计划 / 控制面板入口 …）
/// 或一段常用<b>脚本</b>命令。
/// <para>
/// ⚠️ 这里的「设置」指 <b>操作系统设置</b>，<b>不是本软件自己的设置项</b>（那种在「设置」模块里）。
/// 两者在本模型里统一成「一条命令 + 元信息」，贴合本工程「个人工具 ≈ 带 GUI 的脚本管理器」的定位。
/// </para>
/// <para>
/// 本轮（v1.24）只落地 <b>列表 / 增删改查 / 收藏</b>；<see cref="Command"/> 只用于展示与编辑，
/// <b>尚未接「运行」</b>（后续版本再补），所以界面上刻意不出现执行按钮 —— 不虚报能力。
/// </para>
/// </summary>
public partial class QuickAction : ObservableObject
{
    /// <summary>稳定标识（新建时生成；收藏夹按它引用，改名不受影响）。</summary>
    public string Id { get; set; } = "";

    /// <summary>名称（列表第一列）。</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>分类（如「系统设置」「网络」「维护」「打开」）；列表可按它排序 / 过滤。</summary>
    [ObservableProperty] private string _category = "";

    /// <summary>简介（一句话说明这条做什么）。</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>命令内容（cmd / PowerShell / 程序 / 网址）。本轮只展示、可编辑，不执行。</summary>
    [ObservableProperty] private string _command = "";

    /// <summary>执行时是否需要管理员权限（仅作元信息展示）。</summary>
    [ObservableProperty] private bool _needAdmin;

    /// <summary>是否已收藏到「本模块 → 收藏夹」。</summary>
    [ObservableProperty] private bool _isFavorite;

    /// <summary>列表「★」列的显示文本（收藏即 ★）。派生自 <see cref="IsFavorite"/>，不写盘。</summary>
    [JsonIgnore]
    public string FavoriteMark => IsFavorite ? "★" : "";

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteMark));
}
