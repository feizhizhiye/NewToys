namespace NewToys.Viewer.Services;

/// <summary>
/// 查看器模块内部的主题（本模块三个页面之间传参）。
/// <para>
/// 🔴 ⛔ 不往 <c>ModuleTopics</c> 里加 —— 那层只放**跨模块**主题；同模块三页共用一个通道，
/// 靠载荷里的 <c>Scope</c> 区分归属。
/// </para>
/// </summary>
public static class ViewerTopics
{
    /// <summary>请目标页打开一批文件；载荷 = <c>ViewerOpenRequest</c>。</summary>
    public const string OpenFiles = "viewer.openfiles";
}
