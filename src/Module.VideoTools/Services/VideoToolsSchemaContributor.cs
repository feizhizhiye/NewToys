using System;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.Shared.Services;

namespace NewToys.VideoTools.Services;

/// <summary>
/// 视频工具在自有库（<c>store\video\video.db</c>）里的 schema 贡献者。
/// <list type="bullet">
///   <item><b>一次性清旧表</b>：v3.32 起本模块不再用 <c>video_catalog / video_tags / video_favorites</c>。</item>
///   <item><b>建媒体索引表</b>：由 <see cref="VideoMediaIndex"/> 构造完成（本类只负责「确保它被构造」）。</item>
///   <item><b>建任务缓存文档表</b>（第二批）：<c>video_docs</c> + 旧 <c>video-tasks.json</c> 一次性入表。</item>
/// </list>
/// <para>
/// 🔴 这段 DROP 必须带护栏：无护栏的破坏性 DDL 迟早会误伤（表名写错就是清空数据）⇒
/// 改成「缓存版本号变化才清」，与模块自身的 <c>SchemaVersion</c> 解耦（清缓存不等于 schema 升级）。
/// </para>
/// </summary>
public sealed class VideoToolsSchemaContributor : ILibrarySchemaContributor
{
    /// <summary>模块键（版本记录键 <c>schema.video-tools</c>）。</summary>
    public const string Key = "video-tools";

    /// <summary>本模块 schema 版本（v2 = <c>video_docs</c> + video-tasks.json 入表）。</summary>
    public const int Version = 2;

    /// <summary>旧表清理的"执行过"标记：只在值不等于当前常量时清一次（改这个值 = 允许再清一次）。</summary>
    private const string CacheKey = "cache.video.legacy-tables";

    /// <summary>清表标记的版本（⛔ 不是 schema 版本；schema 版本见 <see cref="SchemaVersion"/>）。</summary>
    private const int CacheVersion = 2;

    private readonly SqliteDatabase _db;
    private readonly IServiceProvider _sp;
    private readonly ConfigStoreService _cfg;
    private readonly JsonDocStore _docs;

    public VideoToolsSchemaContributor(VideoDb db, IServiceProvider sp, ConfigStoreService cfg)
    {
        _db = db;
        _sp = sp;
        _cfg = cfg;
        _docs = new JsonDocStore(db, "video");
    }

    public string ModuleKey => Key;

    public string DatabaseKey => VideoDb.Key;

    public int SchemaVersion => Version;

    public SchemaOutcome EnsureSchema()
    {
        if (!string.Equals(_db.GetMeta(CacheKey), CacheVersion.ToString(), StringComparison.Ordinal))
        {
            _db.EnsureTable(
                "DROP TABLE IF EXISTS video_catalog; DROP TABLE IF EXISTS video_tags; DROP TABLE IF EXISTS video_favorites;");
            _db.SetMeta(CacheKey, CacheVersion.ToString());
        }

        // 触碰即建表（VideoMediaIndex 构造里跑 ApplySchema：快照 / 建 video_media / 版本记账）。
        // ⚠️ 顺序不能反：DROP 必须在它之前，否则会把刚建好的表删掉。
        var outcome = _sp.GetRequiredService<VideoMediaIndex>().SchemaOutcome;

        // 任务缓存文档表 + 旧 video-tasks.json 入表（幂等）
        if (!_docs.EnsureSchema()) return SchemaOutcome.Failed;
        JsonDocMigration.ImportOnce(_docs, VideoTaskStore.DocName,
            System.IO.Path.Combine(_cfg.ConfigDir, "video-tasks.json"));

        return outcome;
    }
}
