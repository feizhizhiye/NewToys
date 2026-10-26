using NewToys.Core.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产自有库（<c>store\assets\assets.db</c>）。
/// <para>
/// 🔴 一个模块一个库：13 张资产表只在资产库里，⛔ 与视频 / 网络 / 脚本模块共库。
/// 派生类型是为了让 DI 能把「本模块的库」与别的模块的库区分开（同一个容器里 4 个库各是不同类型）——
/// <c>internal</c> 不必要：模块之间本就禁互引，别的模块拿不到这个类型。
/// </para>
/// </summary>
public sealed class AssetsDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "assets";

    /// <summary>库文件名。</summary>
    public const string FileName = "assets.db";

    public AssetsDb(ConfigStoreService store) : base(store, Key, FileName) { }
}
