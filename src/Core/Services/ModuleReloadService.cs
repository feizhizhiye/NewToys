using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// 模块热加载服务：包装现有 <see cref="ModuleLoader.HotReload(IServiceProvider)"/>（静态类⛔不动），
/// 完成后触发 <see cref="IModuleReloadService.Reloaded"/> 供 Shell 收尾（重建左树 + 底栏消息）。
/// </summary>
public sealed class ModuleReloadService : IModuleReloadService
{
    private readonly IServiceProvider _sp;

    public ModuleReloadService(IServiceProvider sp) => _sp = sp;

    public (int Added, int Skipped, int Failed) HotReload()
    {
        var result = ModuleLoader.HotReload(_sp);
        // 🔴 新程序集带来新页面类型 ⇒ 类型索引必须重建，否则热加载后点新页面一律「找不到视图」。
        // 放在 Reloaded 之前：Shell 收到通知就会重建左树并允许点击。
        _sp.GetService<IPageCatalog>()?.Invalidate();
        Reloaded?.Invoke(result);
        return result;
    }

    public event Action<(int Added, int Skipped, int Failed)>? Reloaded;
}
