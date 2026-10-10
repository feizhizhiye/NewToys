using System;

namespace NewToys.Core.Services;

/// <summary>
/// 全局服务访问点。
/// 由 Shell 在构建 ServiceProvider 之后调用 Initialize 注入；
/// 供各模块（不能反向引用 Shell）在 code-behind 中按需获取服务实例。
/// </summary>
public static class AppServices
{
    private static IServiceProvider? _provider;

    public static void Initialize(IServiceProvider provider) => _provider = provider;

    public static T? Get<T>() where T : class
        => _provider?.GetService(typeof(T)) as T;
}