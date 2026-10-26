using NewToys.Core.Services;

namespace NewToys.ScriptManager.Services;

/// <summary>
/// 脚本管理器自有库（<c>store\script\script.db</c>）：
/// <c>script_docs</c>（集合 / 条目 / 视图 / 抓取目录元数据）+ <c>pc_observations</c> / <c>pc_runs</c>（采集观测与运行记录）。
/// <para>派生类型让 DI 能把本模块的库与别的模块的库区分开（同一容器里 4 个库各是不同类型）。</para>
/// </summary>
public sealed class ScriptDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "script";

    /// <summary>库文件名。</summary>
    public const string FileName = "script.db";

    public ScriptDb(ConfigStoreService store) : base(store, Key, FileName) { }
}
