using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>
/// 按**简单类名**检索已加载程序集里的类型（页面 View / ViewModel 解析用）。
/// <para>
/// 🔴 存在的理由：页面由菜单项上的字符串 <c>ViewTypeName</c> 驱动，解析只能靠反射。
/// 反射散落多处会让「找不到类型」变成静默空页，且每次导航都全量扫程序集。
/// 本契约把扫描收成一份**带缓存的索引**，模块热加载后由调用方 <see cref="Invalidate"/>。
/// </para>
/// <para>⛔ 实现不得依赖 Avalonia：Core 层据此服务，判断「是不是控件」由调用方自行过滤。</para>
/// </summary>
public interface IPageCatalog
{
    /// <summary>取该简单类名的全部类型（保持程序集与类型声明顺序）；无匹配返回空集合。</summary>
    IReadOnlyList<Type> FindByName(string simpleName);

    /// <summary>
    /// 按命名约定给出候选 ViewModel 类型（同命名空间优先，其后是全程程序集同名兜底）。
    /// 只返回类型，不做实例化 —— 实例化带 DI 与异常兜底，属于调用方职责。
    /// </summary>
    IReadOnlyList<Type> ViewModelCandidates(Type viewType);

    /// <summary>丢弃索引，下次访问重建（模块热加载后必须调用，否则新程序集的类型永远查不到）。</summary>
    void Invalidate();
}
