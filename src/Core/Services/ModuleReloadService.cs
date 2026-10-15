using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// 模块热加载服务（NT-052）：包装现有 <see cref="ModuleLoader.HotReload(IServiceProvider)"/>（静态类⛔不动），
/// 完成后触发 <see cref="IModuleReloadService.Reloaded"/> 供 Shell 收尾（重建左树 + 底栏消息）。
/// </summary>
public sealed class ModuleReloadService : IModuleReloadService
{
    private readonly IServiceProvider _sp;

    public ModuleReloadService(IServiceProvider sp) => _sp = sp;

    public (int Added, int Skipped, int Failed) HotReload()
    {
        var result = ModuleLoader.HotReload(_sp);
        Reloaded?.Invoke(result);
        return result;
    }

    public event Action<(int Added, int Skipped, int Failed)>? Reloaded;
}
