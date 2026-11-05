using System;
using NewToys.Core.Services;
using NewToys.Core.Services.DataMigrations;
using NewToys.Shared.Services;

namespace NewToys.WebTools.Services;

/// <summary>网络工具在自有库（store\web\web.db）里的 schema 贡献者。（WHY: WT-012）</summary>
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

    /// <summary>模块键（版本记录键 <c>schema.web-tools</c>）。</summary>
    public const string Key = "web-tools";

    /// <summary>本模块 schema 版本。</summary>
    public const int Version = 1;

    private readonly JsonDocStore _docs;
    private readonly ConfigStoreService _cfg;

    public WebToolsSchemaContributor(WebDb db, ConfigStoreService cfg)
    {
        _docs = new JsonDocStore(db, "web");
        _cfg = cfg;
    }

    public string ModuleKey => Key;

    public string DatabaseKey => WebDb.Key;

    public int SchemaVersion => Version;

    public SchemaOutcome EnsureSchema()
    {
        if (!_docs.EnsureSchema()) return SchemaOutcome.Failed;

        foreach (var (doc, file) in Pairs())
            JsonDocMigration.ImportOnce(_docs, doc, System.IO.Path.Combine(_cfg.ConfigDir, file));

        return SchemaOutcome.UpToDate;
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
