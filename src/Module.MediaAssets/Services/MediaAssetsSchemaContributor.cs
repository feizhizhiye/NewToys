using System;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产在自有库（<c>store\assets\assets.db</c>）里的 schema 贡献者。
/// <para>
/// 本模块的建表逻辑（幂等 <c>CREATE IF NOT EXISTS</c> + 只加可空列的 <c>ALTER TABLE ADD COLUMN</c>）
/// 一直写在 <see cref="AssetDatabase"/> 的构造里。本类不改写 DDL，只负责**让它在 schema 阶段被构造**——
/// 这样「库一打开 ⇒ 资产的 13 张表都在」，不依赖用户先点开资产页。
/// </para>
/// </summary>
public sealed class MediaAssetsSchemaContributor : ILibrarySchemaContributor
{
    /// <summary>模块键（版本记录键 <c>schema.media-assets</c>）。</summary>
    public const string Key = "media-assets";

    /// <summary>本模块 schema 版本。</summary>
    public const int Version = 1;

    private readonly IServiceProvider _sp;

    public MediaAssetsSchemaContributor(IServiceProvider sp) => _sp = sp;

    public string ModuleKey => Key;

    public string DatabaseKey => AssetsDb.Key;

    public int SchemaVersion => Version;

    /// <summary>解析 <see cref="AssetDatabase"/> ⇒ 构造函数跑 <c>ApplySchema</c>（快照 / 建表 / 补列 / 版本记账）。</summary>
    public SchemaOutcome EnsureSchema() => _sp.GetRequiredService<AssetDatabase>().SchemaOutcome;
}
