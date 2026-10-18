using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shell.ViewModels;

/// <summary>
/// 一个打开的标签页（v3.1 仿浏览器导航模型）= 一个缓存的 View 实例 + 一份 VM + 一个历史栈。
/// <para>
/// 单例语义：同一 <see cref="Key"/> 只允许一个标签页存在（工具类应用，非文档编辑器）。
/// 首页标签 <see cref="CanClose"/>=false 且 <see cref="ShowInTabStrip"/>=false（v3.66 第十八轮：
/// 它不常驻标签栏，入口 = 标签栏 `＋`；但**激活时会在标签栏露出**，见 <see cref="IsVisibleInStrip"/>）。
/// </para>
/// </summary>
public partial class TabItemViewModel : ObservableObject
{
    /// <summary>菜单 key（如 <c>script-packages</c>）；首页为 <c>home</c>。</summary>
    public string Key { get; }

    /// <summary>标签标题（= 菜单 title；首页为「首页」）。</summary>
    public string Title { get; }

    /// <summary>标签图标（本轮为 emoji，与菜单 icon 一致）。</summary>
    public string Icon { get; }

    /// <summary>缓存的 View 实例（切标签不重建，关闭时才销毁）。</summary>
    public object Content { get; private set; }

    /// <summary>对应 VM（用于探测 UI 契约 <c>IDetailPanelProvider</c> / <c>IHistoryProvider</c>）。</summary>
    public object? ViewModel { get; private set; }

    /// <summary>是否可关闭（首页 = false）。</summary>
    public bool CanClose { get; }

    /// <summary>
    /// 「是否**常驻**标签栏」：普通标签 <c>true</c>；首页 <c>false</c>（v3.66 第十八轮起首页不做固定按钮，
    /// 入口 = 标签栏 `＋`）。⚠️ 首页激活时**仍要在标签栏露出**（否则看不出当前在哪一页）⇒ 见 <see cref="IsVisibleInStrip"/>。
    /// </summary>
    public bool ShowInTabStrip { get; }

    /// <summary>
    /// 标签栏里**实际是否可见**（v3.68）：常驻标签恒可见；首页只在**激活时**出现，切走即隐去。
    /// </summary>
    public bool IsVisibleInStrip => ShowInTabStrip || IsActive;

    [ObservableProperty] private bool _isActive;

    internal Stack<object?> BackStack { get; } = new();
    internal Stack<object?> ForwardStack { get; } = new();

    public bool CanGoBack => BackStack.Count > 0;
    public bool CanGoForward => ForwardStack.Count > 0;

    public TabItemViewModel(string key, string title, string icon, object content, object? viewModel,
                            bool canClose, bool showInTabStrip = true)
    {
        Key = key;
        Title = title;
        Icon = icon;
        Content = content;
        ViewModel = viewModel;
        CanClose = canClose;
        ShowInTabStrip = showInTabStrip;
    }

    /// <summary>「⟳ 刷新」用：原地换掉缓存的 View + VM（标签身份/位置不变）。</summary>
    internal void Replace(object content, object? viewModel)
    {
        Content = content;
        ViewModel = viewModel;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(ViewModel));
    }

    /// <summary>激活态一变，标签栏可见性（首页联动）也要重算。</summary>
    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(IsVisibleInStrip));
}
