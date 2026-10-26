namespace NewToys.Shared.Services;

/// <summary>
/// 模块自有库的**建表贡献者**：每个用到结构化数据的模块实现一个，
/// 由 <c>LibrarySchemaService</c> 在首帧之后统一调用。
/// <para>
/// 🔴 一个模块 = 一个库文件（<c>store\&lt;库键&gt;\&lt;库键&gt;.db</c>），库之间不共享连接、不跨库查询。
/// 建表统一在这个入口跑，是为了「库一打开 ⇒ 该模块的表都在」，不依赖用户先点开某个页面。
/// </para>
/// <para>
/// 🔴 契约：<see cref="EnsureSchema"/> 必须**幂等**（<c>CREATE IF NOT EXISTS</c> + 只加可空列的
/// <c>ALTER TABLE ADD COLUMN</c>），且**可在任意启动次数下重复执行**；⛔ 不许在这里做破坏性 DDL
/// （`DROP` 只允许针对本模块的**可重建缓存表**，并且必须带版本护栏）。
/// </para>
/// <para>
/// ⚠️ 本接口**不带 SqliteConnection 参数**：`Shared` 不引 `Microsoft.Data.Sqlite`。
/// 实现方注入本模块的库服务（<c>AssetsDb</c> / <c>VideoDb</c> …）并调它的 <c>ApplySchema</c>。
/// </para>
/// </summary>
public interface ILibrarySchemaContributor
{
    /// <summary>模块键（唯一；与菜单分组键一致，如 <c>script-manager</c>）。用作版本记录键 <c>schema.&lt;键&gt;</c>。</summary>
    string ModuleKey { get; }

    /// <summary>模块自有库的键 = <c>store\</c> 下的子目录名（<c>assets</c> / <c>video</c> / <c>web</c> / <c>script</c>）。</summary>
    string DatabaseKey { get; }

    /// <summary>本模块表的 schema 版本（只增不减）。库里的版本更高 ⇒ 说明库来自更新版软件，只告警、不降级。</summary>
    int SchemaVersion { get; }

    /// <summary>幂等建表 / 补列 / 版本记账（实现在内部必须自己兜住异常，⛔ 不许抛出去拖垮其它模块）。</summary>
    SchemaOutcome EnsureSchema();
}
