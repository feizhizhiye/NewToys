using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NewToys.Shell.ViewModels;

/// <summary>一个打开的标签页（ 仿浏览器导航模型）= 一个缓存的 View 实例 + 一份 VM + 一个历史栈。</summary>
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

    [ObservableProperty] private bool _isActive;

    internal Stack<object?> BackStack { get; } = new();
    internal Stack<object?> ForwardStack { get; } = new();

    public bool CanGoBack => BackStack.Count > 0;
    public bool CanGoForward => ForwardStack.Count > 0;

    public TabItemViewModel(string key, string title, string icon, object content, object? viewModel,
                            bool canClose)
    {
        Key = key;
        Title = title;
        Icon = icon;
        Content = content;
        ViewModel = viewModel;
        CanClose = canClose;
    }

    /// <summary>「⟳ 刷新」用：原地换掉缓存的 View + VM（标签身份/位置不变）。</summary>
    internal void Replace(object content, object? viewModel)
    {
        // ⚠️ 旧 VM 必须在这里释放：它可能订阅了单例服务的事件（TaskQueue.Changed 这类），
        //    不释放就会在每次「⟳ 刷新」时留下一个不会再显示、却仍在被回调的 VM。
        if (!ReferenceEquals(ViewModel, viewModel))
            (ViewModel as IDisposable)?.Dispose();

        Content = content;
        ViewModel = viewModel;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(ViewModel));
    }
}
