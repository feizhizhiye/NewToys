using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// 模块加载器（v3.26 起，需求 §3.4）：**必载清单 + 目录扫描**两步——
/// <list type="bullet">
///   <item><b>必载清单</b>：框架组件（<c>Module.Settings</c>，物理必备）由调用方传入，先加载；
///         Debug 同时把功能模块也放进清单（程序集延迟加载，纯扫描一个都扫不到 →「左侧菜单全空」）。</item>
///   <item><b>目录扫描</b>：随后扫 <c>&lt;程序目录&gt;\Modules\</c> 下的 DLL 补功能模块，
///         跳过已加载同名（Shared/Core、以及清单里已加载的模块由此跳过）。</item>
/// </list>
/// </summary>
public static class ModuleLoader
{
    private static readonly List<IModule> _modules = new();

    /// <summary>Release 模式下扫描的模块目录名（相对程序目录）。</summary>
    public const string ModulesSubDirectory = "Modules";

    /// <param name="services">DI 容器</param>
    /// <param name="mandatoryModuleTypes">必载清单（框架组件置顶 + Debug 的功能模块）；⛔ 非 null。</param>
    public static IReadOnlyList<IModule> LoadAll(IServiceCollection services, IEnumerable<Type> mandatoryModuleTypes)
    {
        _modules.Clear();

        // 必载清单先加载；再扫 Modules\ 补功能模块（跳过已加载同名）。
        var types = mandatoryModuleTypes.ToList();
        types.AddRange(ScanModulesDirectory());

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

        // 健全性检查（v3.26，需求 §3.4）：必备的 settings 分组没注册上（Module.Settings 缺失 /
        // 它的 Initialize 抛异常被上面的逐模块 try-catch 吞了）→ 报一行，⛔ 不静默。
        // 启动不中断——框架自给（首页 / 一键启动不依赖任何功能模块），仅设置不可用。
        var menu = sp.GetService<IMenuService>();
        if (menu?.FindGroup("settings") == null)
            System.Diagnostics.Debug.WriteLine(
                "[NewToys] 健全性检查：settings 分组未注册——Module.Settings 缺失或初始化失败，设置不可用（框架其余功能不受影响）。");
        else if (menu is MenuService ms && ms.PendingCount > 0)
            // 暂存区还有孤儿项 = 有模块往一个始终没建立的分组注册过（典型 = 分组 key 打错）
            System.Diagnostics.Debug.WriteLine(
                $"[NewToys] 健全性检查：{ms.PendingCount} 个跨组菜单项的目标分组始终未注册（分组 key 打错？），这些项不显示。");
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
