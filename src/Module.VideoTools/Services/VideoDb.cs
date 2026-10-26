using NewToys.Core.Services;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频工具自有库（<c>store\video\video.db</c>）：<c>video_media</c>（ffprobe 缓存）+ <c>video_docs</c>（任务清单）。
/// <para>派生类型让 DI 能把本模块的库与别的模块的库区分开（同一容器里 4 个库各是不同类型）。</para>
/// </summary>
public sealed class VideoDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "video";

    /// <summary>库文件名。</summary>
    public const string FileName = "video.db";

    public VideoDb(ConfigStoreService store) : base(store, Key, FileName) { }
}
