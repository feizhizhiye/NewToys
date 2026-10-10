using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;
namespace NewToys.Core.Services;
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    public event EventHandler<(string key, object? view, object? parameter)>? Navigated;
    public string CurrentPageKey { get; private set; } = "";
    public object? CurrentView { get; private set; }
    public NavigationService(IServiceProvider sp, IMenuService menu) { _sp = sp; _menu = menu; }
    public void NavigateTo(string pageKey, object? parameter = null)
    {
        CurrentPageKey = pageKey;
        var item = _menu.FindItem(pageKey);
        object? view = null;
        if (item != null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var viewType = asm.GetTypes().FirstOrDefault(t => t.Name == item.ViewTypeName && !t.IsAbstract && t.IsClass);
                if (viewType != null)
                {
                    try { view = ActivatorUtilities.CreateInstance(_sp, viewType); }
                    catch { view = Activator.CreateInstance(viewType); }
                    break;
                }
            }
        }
        CurrentView = view;
        Navigated?.Invoke(this, (pageKey, view, parameter));
    }
}