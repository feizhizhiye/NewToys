using System;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产在**总库**里的 schema 贡献者。
/// <para>
/// 本模块的建表逻辑（幂等 <c>CREATE IF NOT EXISTS</c> + 只加可空列的 <c>ALTER TABLE ADD COLUMN</c>）
/// 一直写在 <see cref="AssetDatabase"/> 的构造里（`AssetDatabase.cs:42-43`）。本类不改写 DDL，
/// 只负责**让它在总库 schema 阶段被构造**——这样「库一打开 ⇒ 资产的 12 张表都在」，不依赖用户先点开资产页。
/// </para>
/// </summary>
public sealed class MediaAssetsSchemaContributor : ILibrarySchemaContributor
{
    private readonly IServiceProvider _sp;

    public MediaAssetsSchemaContributor(IServiceProvider sp) => _sp = sp;

    public string ModuleKey => "media-assets";

    public int SchemaVersion => 1;

    /// <summary>解析 <see cref="AssetDatabase"/> ⇒ 构造函数完成建表 / 补列（幂等）。</summary>
    public void EnsureSchema() => _ = _sp.GetRequiredService<AssetDatabase>();
}
