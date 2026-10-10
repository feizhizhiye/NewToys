using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;

namespace NewToys.Core.Services;

/// <summary>
/// 模块加载器。两种模式：
/// <list type="bullet">
///   <item><b>Debug</b>：由调用方传入<b>显式类型清单</b>（Shell.csproj 用 Debug 条件 ProjectReference）。
///         程序集是<b>延迟加载</b>的，纯扫描一个都扫不到，表现为「左侧菜单全空」。</item>
///   <item><b>Release</b>：不传清单，改为扫描 <c>&lt;程序目录&gt;\Modules\</c> 下的 DLL。</item>
/// </list>
/// </summary>
public static class ModuleLoader
{
    private static readonly List<IModule> _modules = new();

    /// <summary>Release 模式下扫描的模块目录名（相对程序目录）。</summary>
    public const string ModulesSubDirectory = "Modules";

    /// <param name="services">DI 容器</param>
    /// <param name="explicitModuleTypes">Debug 的显式类型清单；传 null 表示 Release 走目录扫描。</param>
    public static IReadOnlyList<IModule> LoadAll(IServiceCollection services, IEnumerable<Type>? explicitModuleTypes)
    {
        _modules.Clear();

        var types = explicitModuleTypes != null
            ? explicitModuleTypes.ToList()
            : ScanModulesDirectory();

        foreach (var t in types)
        {
            if (t.IsAbstract || t.IsInterface) continue;
            if (!typeof(IModule).IsAssignableFrom(t)) continue;
            try
            {
                if (Activator.CreateInstance(t) is IModule m) _modules.Add(m);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"模块 {t.FullName} 创建失败: {ex.Message}");
            }
        }

        foreach (var m in _modules) m.RegisterServices(services);
        return _modules;
    }

    /// <summary>在 ServiceProvider 构建完成后调用；单个模块初始化失败不影响其它模块。</summary>
    public static void InitializeAll(IServiceProvider sp)
    {
        foreach (var m in _modules)
        {
            try { m.Initialize(sp); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"模块 {m.GetType().Name} 初始化失败: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Release 目录扫描：只扫 <c>&lt;程序目录&gt;\Modules\</c>（比扫程序目录全部 DLL 省一大截），
    /// 没有 Modules 目录时兜底程序目录本身。
    /// ⚠️ 路径必须用 <see cref="AppContext.BaseDirectory"/> 拼<b>绝对路径</b>——
    ///    写相对 ".\Modules" 会在快捷方式启动时崩 "directory was not found"。
    /// </summary>
    private static List<Type> ScanModulesDirectory()
    {
        var result = new List<Type>();
        var baseDir = AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, ModulesSubDirectory);
        if (!Directory.Exists(dir)) dir = baseDir;

        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
        {
        // 已加载的程序集一律跳过：Release 的 Modules\ 里可能混着 NewToys.Shared / NewToys.Core 等
        // 框架 DLL，如果把它们再 LoadFrom 成第二份副本，就会出现「同一程序集两个-instance」，
        // 导致模块类里的 IModule 与这里看到的 IModule 类型标识不同 → IsAssignableFrom 为假 → 菜单全空。
        if (IsAlreadyLoaded(Path.GetFileNameWithoutExtension(dll))) continue;

        Assembly asm;
        try { asm = Assembly.LoadFrom(dll); }
        catch { continue; } // 非托管 / 非 .NET DLL，跳过

            try
            {
                result.AddRange(asm.GetTypes().Where(t =>
                    !t.IsAbstract && !t.IsInterface && typeof(IModule).IsAssignableFrom(t)));
            }
            catch (ReflectionTypeLoadException ex)
            {
                result.AddRange(ex.Types.Where(t => t != null && typeof(IModule).IsAssignableFrom(t))!);
            }
            catch { /* 依赖缺失的程序集，跳过 */ }
        }
        return result;
    }

    private static bool IsAlreadyLoaded(string simpleName)
        => AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && a.GetName().Name == simpleName);
}
