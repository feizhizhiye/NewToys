using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IPageCatalog"/> 默认实现：首次访问时把已加载程序集的类型按简单类名建索引，之后命中字典。
/// <para>
/// 索引顺序 = <see cref="AppDomain.GetAssemblies"/> 顺序 + 各程序集 <c>GetTypes()</c> 顺序，
/// 因此「同名类型取第一个」的结果与逐次扫描完全一致，⛔ 不改变既有页面解析结果。
/// </para>
/// </summary>
public sealed class PageCatalog : IPageCatalog
{
    private readonly object _gate = new();
    private Dictionary<string, Type[]>? _index;

    public IReadOnlyList<Type> FindByName(string simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return Array.Empty<Type>();
        return EnsureIndex().TryGetValue(simpleName, out var types) ? types : Array.Empty<Type>();
    }

    public IReadOnlyList<Type> ViewModelCandidates(Type viewType)
    {
        if (viewType == null) return Array.Empty<Type>();

        var vmName = ViewNaming.ViewModelName(viewType.Name);
        var result = new List<Type>();

        // 1) 同程序集 + 约定命名空间（命中率最高，且能定位到模块自己的 VM）
        var ns = ViewNaming.ViewModelNamespace(viewType.Namespace);
        if (ns != null)
        {
            var exact = viewType.Assembly.GetType(ns + "." + vmName, throwOnError: false);
            if (exact != null && exact.IsClass && !exact.IsAbstract) result.Add(exact);
        }

        // 2) 兜底：全程序集按类名找（覆盖 Views 命名空间不合规的历史页面）
        foreach (var t in FindByName(vmName))
        {
            if (t.IsClass && !t.IsAbstract && !result.Contains(t)) result.Add(t);
        }

        return result;
    }

    public void Invalidate()
    {
        lock (_gate) _index = null;
    }

    private Dictionary<string, Type[]> EnsureIndex()
    {
        var index = _index;
        if (index != null) return index;

        lock (_gate)
        {
            if (_index != null) return _index;

            var built = new Dictionary<string, Type[]>(StringComparer.Ordinal);
            var buckets = new Dictionary<string, List<Type>>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                foreach (var type in SafeTypes(asm))
                {
                    if (!buckets.TryGetValue(type.Name, out var list))
                    {
                        list = new List<Type>(1);
                        buckets[type.Name] = list;
                    }
                    list.Add(type);
                }
            }

            foreach (var kv in buckets) built[kv.Key] = kv.Value.ToArray();
            _index = built;
            return built;
        }
    }

    /// <summary>
    /// 安全枚举程序集类型：<see cref="Assembly.GetTypes"/> 只要有任意一个类型依赖加载不了就整体抛
    /// <see cref="ReflectionTypeLoadException"/>；这里退化成用异常的 <c>Types</c>（含 null 洞）继续找。
    /// </summary>
    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Select(t => t!); }
        catch { return Array.Empty<Type>(); }
    }
}
