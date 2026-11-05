namespace NewToys.Viewer.Services;

/// <summary>查看器模块内部的主题（本模块三个页面之间传参）。（WHY: VW-018）</summary>
public static class ViewerTopics
{
    /// <summary>请目标页打开一批文件；载荷 = <c>ViewerOpenRequest</c>。</summary>
    public const string OpenFiles = "viewer.openfiles";
}
