using System;

namespace NewToys.Shared.Services;

/// <summary>
/// 页面 View ↔ ViewModel 的命名约定（唯一实现处）。
/// <para>
/// 约定：<c>XxxView</c> ↔ <c>XxxViewModel</c>；命名空间 <c>...\Views</c> ↔ <c>...\ViewModels</c>。
/// 解析与静态检查必须共用本类，⛔ 不要各自写一份字符串处理 —— 两处规则一旦漂移，
/// 表现是「页面打得开但整页空白、无任何异常」，编译期与运行期都不报错。
/// </para>
/// </summary>
public static class ViewNaming
{
    /// <summary>由 View 类名推导 ViewModel 类名：带 <c>View</c> 后缀则替换为 <c>ViewModel</c>，否则直接追加。</summary>
    public static string ViewModelName(string viewName)
    {
        if (string.IsNullOrEmpty(viewName)) return "ViewModel";
        return viewName.EndsWith("View", StringComparison.Ordinal)
            ? viewName.Substring(0, viewName.Length - 4) + "ViewModel"
            : viewName + "ViewModel";
    }

    /// <summary>
    /// 由 View 命名空间推导 ViewModel 命名空间；不以 <c>.Views</c> 结尾时返回 <c>null</c>（调用方退到全程序集按类名兜底）。
    /// </summary>
    public static string? ViewModelNamespace(string? viewNamespace)
    {
        if (string.IsNullOrEmpty(viewNamespace)) return null;
        return viewNamespace.EndsWith(".Views", StringComparison.Ordinal)
            ? viewNamespace.Substring(0, viewNamespace.Length - ".Views".Length) + ".ViewModels"
            : null;
    }
}
