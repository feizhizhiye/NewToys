using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using NewToys.Shared.Services;

namespace NewToys.UI.Settings;

/// <summary>设置页里的一个节（从 Shell.Models 上移进 UI 库，应用级设置页与各模块设置页共用）。（WHY: UI-024）</summary>
public partial class SettingsSection : ObservableObject
{
    /// <summary>节锚点 Key（`settings-general` … ；模块页可留空 = 不参与外部寻址）。</summary>
    public string Key { get; set; } = "";

    /// <summary>节标题（折叠块的抬头）。</summary>
    public string Title { get; set; } = "";

    /// <summary>节图标（emoji，仅抬头用）。</summary>
    public string Icon { get; set; } = "";

    /// <summary>一句话说明（抬头副标题，也是搜索索引）。留空 ⇒ 抬头右侧不占位。</summary>
    public string Desc { get; set; } = "";

    /// <summary>
    /// 节的视图（折叠 / 展开只切可见性，⛔ 不重建）。
    /// <para>🔴 XAML 内容属性：模块设置页把节内容直接写成子元素即可（DataContext 继承页面 VM）。</para>
    /// </summary>
    [Content]
    public Control? Content { get; set; }

    /// <summary>本节承载的 VM 列表（合节时可多个；首个 = 主 VM）。</summary>
    public IReadOnlyList<object> ViewModels { get; set; } = Array.Empty<object>();

    /// <summary>节的**主** VM（首个）。</summary>
    public object? ViewModel => ViewModels.Count > 0 ? ViewModels[0] : null;

    /// <summary>
    /// 本节是否有**自己的工具条**（抬头下方、正文之上渲染一条 <c>ToolbarHost</c>）。
    /// 判定 = 节 VM 实现 <see cref="IToolbarSource"/> 且工具条非空（惰性取，绑定求值时算一次）。
    /// </summary>
    public bool HasToolbar => ViewModels.Any(v => v is IToolbarSource { Toolbar.Count: > 0 });

    /// <summary>本节**第一次展开**时回调（典型 = 「运行环境」节做**惰性探测**外部程序）。</summary>
    public Action? OnActivated { get; set; }

    /// <summary>折叠块当前是否展开。**只由点击决定**（抬头 /「全部收起」），滚动 ⛔ 不改它。</summary>
    [ObservableProperty] private bool _isExpanded;
}
