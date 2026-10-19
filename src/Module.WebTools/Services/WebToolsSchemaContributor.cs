using System;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.Shared.Services;

namespace NewToys.WebTools.Services;

/// <summary>
/// 网络工具在**总库**里的 schema 贡献者（NT-076 第二批）。
/// <list type="bullet">
///   <item>建 <c>web_docs</c>（书签 / ping 目标 / 抓取任务 / hosts 方案四份文档，见 <see cref="JsonDocStore"/>）。</item>
///   <item>把 <c>config\</c> 下 4 个旧 JSON 一次性搬进表，旧文件改名 <c>.legacy</c>（⛔ 不删）。</item>
/// </list>
/// <para>
/// 文档名沿用旧文件名基名（<c>bookmarks</c> / <c>ping-targets</c> / <c>scrape-tasks</c> / <c>hosts-profiles</c>）——
/// 排障时「文件叫什么、表里就叫什么」，不用再查一张映射表。
/// </para>
/// </summary>
public sealed class WebToolsSchemaContributor : ILibrarySchemaContributor
{
    /// <summary>书签文档名。</summary>
    public const string DocBookmarks = "bookmarks";

    /// <summary>Ping 目标文档名。</summary>
    public const string DocPingTargets = "ping-targets";

    /// <summary>抓取任务文档名。</summary>
    public const string DocScrapeTasks = "scrape-tasks";

    /// <summary>hosts 方案文档名。</summary>
    public const string DocHostsProfiles = "hosts-profiles";

    private readonly JsonDocStore _docs;
    private readonly ConfigStoreService _cfg;

    public WebToolsSchemaContributor(SqliteDatabase db, ConfigStoreService cfg)
    {
        _docs = new JsonDocStore(db, "web");
        _cfg = cfg;
    }

    public string ModuleKey => "web-tools";

    public int SchemaVersion => 1;

    public void EnsureSchema()
    {
        if (!_docs.EnsureSchema()) return;

        foreach (var (doc, file) in Pairs())
            JsonDocMigration.ImportOnce(_docs, doc, System.IO.Path.Combine(_cfg.ConfigDir, file));
    }

    /// <summary>文档名 ↔ 旧文件名的对应（迁移与排障共用一份）。</summary>
    private static (string Doc, string File)[] Pairs() => new[]
    {
        (DocBookmarks,     "bookmarks.json"),
        (DocPingTargets,   "ping-targets.json"),
        (DocScrapeTasks,   "scrape-tasks.json"),
        (DocHostsProfiles, "hosts-profiles.json"),
    };
}
