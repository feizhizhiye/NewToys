namespace NewToys.Shared.Services;

/// <summary>
/// 总库（<c>data\newtoys.db</c>）的**建表贡献者**（NT-076）：每个用到结构化数据的模块实现一个，
/// 由 <c>LibrarySchemaService</c> 在库打开后统一调用。
/// <para>
/// 🔴 为什么需要它：总库由多个模块共建表，而建表原先埋在各自 DB 服务的构造函数里 ——
/// 用户只打开「资产」页时脚本模块的表压根不存在。统一入口后，「库一打开 ⇒ 所有模块的表都在」。
/// </para>
/// <para>
/// 🔴 契约：<see cref="EnsureSchema"/> 必须**幂等**（<c>CREATE IF NOT EXISTS</c> + 只加可空列的
/// <c>ALTER TABLE ADD COLUMN</c>），且**可在任意启动次数下重复执行**；⛔ 不许在这里做破坏性 DDL
/// （`DROP` 只允许针对本模块的**可重建缓存表**，并且必须带版本护栏）。
/// </para>
/// <para>
/// ⚠️ 本接口**不带 SqliteConnection 参数**：`Shared` 不引 `Microsoft.Data.Sqlite`。
/// 实现方自己注入模块 / Core 的 DB 服务即可。
/// </para>
/// </summary>
public interface ILibrarySchemaContributor
{
    /// <summary>模块键（唯一；建议与菜单分组键一致，如 <c>script-manager</c> / <c>media-assets</c>）。用作版本记录键 <c>schema.&lt;键&gt;</c>。</summary>
    string ModuleKey { get; }

    /// <summary>本模块表的 schema 版本（只增不减）。库里的版本更高 ⇒ 说明库来自更新版软件，只告警、不降级。</summary>
    int SchemaVersion { get; }

    /// <summary>幂等建表 / 补列（实现在内部必须自己 try/catch 兜住，⛔ 不许抛出去拖垮其它模块）。</summary>
    void EnsureSchema();
}
