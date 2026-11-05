using System;
using System.Collections.Generic;

namespace NewToys.Shared.Services;

/// <summary>按简单类名检索已加载程序集里的类型（页面 View / ViewModel 解析用）。（WHY: SH-049）</summary>
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
