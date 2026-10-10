using System;

namespace NewToys.Shared.Services;

public interface INavigationService
{
    void NavigateTo(string pageKey, object? parameter = null);
    string CurrentPageKey { get; }
    object? CurrentView { get; }
    /// <summary>
    /// 导航完成后触发，携带目标页 key 与已实例化的 View（未绑定 DataContext）。
    /// 宿主（Shell）订阅它来切换内容区；模块只调用 <see cref="NavigateTo"/>，无需引用 Shell 程序集。
    /// </summary>
    event EventHandler<(string key, object? view, object? parameter)> Navigated;
}