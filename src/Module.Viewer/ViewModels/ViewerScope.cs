namespace NewToys.Viewer.ViewModels;

/// <summary>查看器页的范围：一页只处理一类内容（菜单分「文档查看 / 图片查看 / 音乐播放」三页）。（WHY: VW-020）</summary>
public enum ViewerScope
{
    /// <summary>不限（设计期预览用；运行时不出这种页）。</summary>
    All,

    /// <summary>源码 / 短文本 / HTML（长文本阅读在「网络阅读」模块）。</summary>
    Text,

    /// <summary>图片，以及压缩包内的图片。</summary>
    Image,

    /// <summary>本地音频。</summary>
    Audio,
}

/// <summary>
/// 跨页打开请求：文件不属于当前页时，投递给目标页。
/// </summary>
/// <param name="Scope">目标页范围 —— 收到请求的页先比对自己的范围，不匹配直接忽略。</param>
/// <param name="Paths">要打开的文件（音频可以是多首）。</param>
public sealed record ViewerOpenRequest(ViewerScope Scope, IReadOnlyList<string> Paths);
