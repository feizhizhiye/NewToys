using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;
namespace NewToys.Core.Services;
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly IPageCatalog _pages;
    public event EventHandler<(string key, object? view, object? parameter)>? Navigated;
    public string CurrentPageKey { get; private set; } = "";
    public object? CurrentView { get; private set; }
    public NavigationService(IServiceProvider sp, IMenuService menu, IPageCatalog pages)
    { _sp = sp; _menu = menu; _pages = pages; }
    public void NavigateTo(string pageKey, object? parameter = null)
    {
        CurrentPageKey = pageKey;
        var item = _menu.FindItem(pageKey);
        object? view = null;
        if (item != null)
        {
            var viewType = _pages.FindByName(item.ViewTypeName)
                .FirstOrDefault(t => t.IsClass && !t.IsAbstract);
            if (viewType != null)
            {
                // ⚠️ 实例化失败绝不能把异常抛回调用方（模块页里点一下按钮就整个程序退出）。
                // 两层兜底：先走 DI（能注入 VM / 服务），再退到无参构造；都不行就交一枚 null 出去，
                // 由 ShellViewModel.OnNavigated 忽略（它会判 `view is not Control`）。
                try { view = ActivatorUtilities.CreateInstance(_sp, viewType); }
                catch
                {
                    try { view = Activator.CreateInstance(viewType); }
                    catch { view = null; }
                }
            }
        }
        CurrentView = view;
        Navigated?.Invoke(this, (pageKey, view, parameter));
    }
}