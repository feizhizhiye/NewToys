namespace NewToys.Shared.Services;

/// <summary>模块自有库的建表贡献者：每个用到结构化数据的模块实现一个， 由 LibrarySchemaService 在首帧之后统一调用。（WHY: SH-041）</summary>
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
