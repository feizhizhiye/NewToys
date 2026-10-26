using NewToys.Core.Services;

namespace NewToys.WebTools.Services;

/// <summary>
/// 网络工具自有库（<c>store\web\web.db</c>）：<c>web_docs</c>（网址收藏 / Ping 目标 / Hosts 方案 / 源清单）。
/// <para>派生类型让 DI 能把本模块的库与别的模块的库区分开（同一容器里 4 个库各是不同类型）。</para>
/// </summary>
public sealed class WebDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "web";

    /// <summary>库文件名。</summary>
    public const string FileName = "web.db";

    public WebDb(ConfigStoreService store) : base(store, Key, FileName) { }
}
