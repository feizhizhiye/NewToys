using System;
using System.Collections.Generic;
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
                if (asm.IsDynamic) continue;
                var viewType = SafeTypes(asm).FirstOrDefault(t => t.Name == item.ViewTypeName && !t.IsAbstract && t.IsClass);
                if (viewType == null) continue;

                // ⚠️ 实例化失败绝不能把异常抛回调用方（模块页里点一下按钮就整个程序退出）。
                // 两层兜底：先走 DI（能注入 VM / 服务），再退到无参构造；都不行就交一枚 null 出去，
                // 由 ShellViewModel.OnNavigated 忽略（它会判 `view is not Control`）。
                try { view = ActivatorUtilities.CreateInstance(_sp, viewType); }
                catch
                {
                    try { view = Activator.CreateInstance(viewType); }
                    catch { view = null; }
                }
                break;
            }
        }
        CurrentView = view;
        Navigated?.Invoke(this, (pageKey, view, parameter));
    }

    /// <summary>
    /// 安全枚举程序集类型：<see cref="System.Reflection.Assembly.GetTypes"/> 只要有任意一个类型
    /// 依赖加载不了就整体抛 <see cref="System.Reflection.ReflectionTypeLoadException"/>；
    /// 直接调用会让一次导航变成未处理异常。这里退化成用异常的 <c>Types</c>（含 null 洞）继续找。
    /// </summary>
    private static IEnumerable<Type> SafeTypes(System.Reflection.Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Select(t => t!);
        }
        catch { return Array.Empty<Type>(); }
    }
}