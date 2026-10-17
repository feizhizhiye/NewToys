using System;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shell.Models;

/// <summary>
/// 应用级设置页里的**一个节**（v3.65 / NT-062，需求 §4.9.2：一页多节）。
/// <para>
/// <see cref="Key"/> = **节锚点**（原菜单 Key，⛔ 一经发布不改）；<see cref="Content"/> = 节的 View，
/// <see cref="ViewModel"/> = 对应 VM（契约转发要拿它做 <c>is</c> 判定，故 View 之外单列一份）。
/// </para>
/// <para>
/// 🔴 v3.66 / NT-062 细节调整：整页是**滚动流式布局**（六节一起滚，顶部只有一条**磁吸标题**），
/// 每节是一个**可折叠块** —— 所以本模型承载 <see cref="IsExpanded"/>（展开态，用户点的或滚动自动展开的）。
/// 顶栏标题 / 自动展开都靠**视口**判定，故那些逻辑在 <c>SettingsView.axaml.cs</c>，⛔ 不在 VM 里。
/// </para>
/// </summary>
public partial class SettingsSection : ObservableObject
{
    /// <summary>节锚点 Key（`settings-general` … ；「关于」节用内部键 `settings-about`，⛔ 不进菜单）。</summary>
    public string Key { get; init; } = "";

    /// <summary>节标题（折叠块的抬头）。</summary>
    public string Title { get; init; } = "";

    /// <summary>节图标（emoji，仅折叠块抬头用）。</summary>
    public string Icon { get; init; } = "";

    /// <summary>一句话说明（折叠块抬头的副标题，也是搜索索引）。</summary>
    public string Desc { get; init; } = "";

    /// <summary>节的视图实例（折叠 / 展开只是切它的可见性，⛔ 不重建）。</summary>
    public Control Content { get; init; } = null!;

    /// <summary>节的 VM（契约转发 / 惰性探测用）。</summary>
    public object? ViewModel { get; init; }

    /// <summary>
    /// 本节是否有**自己的工具条**（v3.66 第五轮）：有 ⇒ 节点抬头下方、正文之上渲染一条
    /// <c>ToolbarHost</c>（= 原来顶在**页面顶部**的那条，用户要求"放回本节内容顶部"）。
    /// <para>判定 = 节 VM 实现 <see cref="NewToys.Shared.Services.IToolbarSource"/> 且工具条非空（构建时算一次）。</para>
    /// </summary>
    public bool HasToolbar { get; init; }

    /// <summary>本节**第一次展开**时回调（典型 = 「运行环境」节做**惰性探测**外部程序）。</summary>
    public Action? OnActivated { get; init; }

    /// <summary>折叠块当前是否展开（v3.66：由折叠按钮 / 滚动自动展开驱动）。</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>
    /// 用户是否**手动收起**过本节（v3.66 第七轮）：滚动的自动展开会跳过它，⛔ 不被下一帧弹开；
    /// 手动展开 / 外部寻址（搜索、首页跳转）会清掉。⛔ 不是 UI 绑定属性，故不加通知。
    /// </summary>
    public bool UserCollapsed { get; set; }
}
