using NewToys.Core.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>媒体资产自有库（store\assets\assets.db）。（WHY: MA-019）</summary>
public sealed class AssetsDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "assets";

    /// <summary>库文件名。</summary>
    public const string FileName = "assets.db";

    public AssetsDb(ConfigStoreService store) : base(store, Key, FileName) { }
}
