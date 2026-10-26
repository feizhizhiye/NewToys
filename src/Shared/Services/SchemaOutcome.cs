namespace NewToys.Shared.Services;

/// <summary>
/// 一次模块 schema 检查的结果，由 <c>LibrarySchemaService</c> 汇总成启动日志与提示。
/// </summary>
public enum SchemaOutcome
{
    /// <summary>库不可用 / DDL 报错（模块自己已 try 兜住，⛔ 不抛）。</summary>
    Failed = 0,

    /// <summary>本次真的改动了库（首次建库，或版本升级）。</summary>
    Applied = 1,

    /// <summary>版本已是当前值，只做了幂等补列。</summary>
    UpToDate = 2,

    /// <summary>库里的版本高于软件（旧版软件打开新库）⇒ ⛔ 未改动，只告警。</summary>
    Ahead = 3,
}
