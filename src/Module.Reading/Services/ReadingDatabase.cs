using System;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;

namespace NewToys.Reading.Services;

/// <summary>网络阅读模块自有库（<c>store\reading\reading.db</c>）。一个模块一个库，⛔ 与别模块共库。</summary>
public sealed class ReadingDb : SqliteDatabase
{
    /// <summary>库键 = <c>store\</c> 下的子目录名。</summary>
    public const string Key = "reading";

    /// <summary>库文件名。</summary>
    public const string FileName = "reading.db";

    public ReadingDb(ConfigStoreService store) : base(store, Key, FileName) { }
}

/// <summary>
/// 阅读库表结构：<c>book_sources</c>（书源）+ <c>shelf</c>（书架）+ <c>reading_progress</c>（阅读进度）。
/// <para>后两张表本批次只建表、不接界面（书架 / 进度属后续批次）。</para>
/// </summary>
public sealed class ReadingDatabase
{
    private readonly SqliteDatabase _db;

    public ReadingDatabase(ReadingDb db)
    {
        _db = db;
        SchemaOutcome = db.ApplySchema(ReadingSchemaContributor.Key, ReadingSchemaContributor.Version, EnsureSchema);
    }

    /// <summary>本次 schema 结果（供 <see cref="ReadingSchemaContributor"/> 汇总，⛔ 不重复建表）。</summary>
    public SchemaOutcome SchemaOutcome { get; }

    /// <summary>库是否可用（false = 降级：页面照常显示 + 状态栏提示，⛔ 不崩）。</summary>
    public bool IsAvailable => _db.IsAvailable;

    /// <summary>库文件全路径（界面提示用）。</summary>
    public string DatabasePath => _db.DatabasePath;

    /// <summary>连接入口（用完即 Dispose）。</summary>
    public SqliteDatabase Db => _db;

    private void EnsureSchema()
    {
        if (!_db.IsAvailable) return;
        _db.EnsureTable(Tables);
        _db.EnsureTable(Indexes);

        // 书架分组是本批新增列：老库只有建表 DDL 不够（表已存在），必须补列
        EnsureColumn("shelf", "group_name", "TEXT NOT NULL DEFAULT ''");

        // 书源校验（批8）：老库补三列，未校验 = 0
        EnsureColumn("book_sources", "check_state", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("book_sources", "check_note", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("book_sources", "checked_at", "TEXT");

        // 书架批量更新（批9）：最新章节 / 章数 / 新增章数 / 检查时间 / 失败原因
        EnsureColumn("shelf", "last_chapter", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("shelf", "chapter_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("shelf", "new_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("shelf", "checked_at", "TEXT");
        EnsureColumn("shelf", "update_note", "TEXT NOT NULL DEFAULT ''");
    }

    /// <summary>给已存在的表补列（列已在 ⇒ 什么都不做）。</summary>
    private void EnsureColumn(string table, string column, string definition)
    {
        try
        {
            using var conn = _db.Open();
            using var check = conn.CreateCommand();
            check.CommandText = $"PRAGMA table_info({table});";
            using (var reader = check.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
                }
            }

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            alter.ExecuteNonQuery();
        }
        catch { /* 补列失败不阻断：读写层按「列可能不存在」降级 */ }
    }

    private const string Tables = @"
CREATE TABLE IF NOT EXISTS book_sources (
  url TEXT PRIMARY KEY,
  name TEXT NOT NULL DEFAULT '',
  group_name TEXT NOT NULL DEFAULT '',
  source_type INTEGER NOT NULL DEFAULT 0,
  enabled INTEGER NOT NULL DEFAULT 1,
  rules_json TEXT NOT NULL DEFAULT '',
  last_used_at TEXT,
  check_state INTEGER NOT NULL DEFAULT 0,
  check_note TEXT NOT NULL DEFAULT '',
  checked_at TEXT
);

CREATE TABLE IF NOT EXISTS shelf (
  book_url TEXT PRIMARY KEY,
  source_url TEXT NOT NULL DEFAULT '',
  name TEXT NOT NULL DEFAULT '',
  author TEXT NOT NULL DEFAULT '',
  cover_url TEXT NOT NULL DEFAULT '',
  group_name TEXT NOT NULL DEFAULT '',
  added_at TEXT NOT NULL DEFAULT '',
  last_chapter TEXT NOT NULL DEFAULT '',
  chapter_count INTEGER NOT NULL DEFAULT 0,
  new_count INTEGER NOT NULL DEFAULT 0,
  checked_at TEXT,
  update_note TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS reading_progress (
  book_url TEXT NOT NULL,
  chapter_url TEXT NOT NULL,
  chapter_name TEXT NOT NULL DEFAULT '',
  ""offset"" REAL NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL DEFAULT '',
  PRIMARY KEY (book_url, chapter_url)
);

CREATE TABLE IF NOT EXISTS content_cache (
  chapter_url TEXT PRIMARY KEY,
  content TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL DEFAULT ''
);
";

    private const string Indexes = @"
CREATE INDEX IF NOT EXISTS idx_reading_sources_name ON book_sources(name);
CREATE INDEX IF NOT EXISTS idx_reading_sources_group ON book_sources(group_name);
CREATE INDEX IF NOT EXISTS idx_reading_shelf_added ON shelf(added_at);
CREATE INDEX IF NOT EXISTS idx_reading_cache_updated ON content_cache(updated_at);
";
}

/// <summary>阅读库的 schema 贡献者（启动期让三张表就位，不依赖用户先点开阅读页）。</summary>
public sealed class ReadingSchemaContributor : ILibrarySchemaContributor
{
    /// <summary>模块键（版本记录键 <c>schema.reading</c>）。</summary>
    public const string Key = "reading";

    /// <summary>本模块 schema 版本。</summary>
    public const int Version = 5;

    private readonly IServiceProvider _sp;

    public ReadingSchemaContributor(IServiceProvider sp) => _sp = sp;

    public string ModuleKey => Key;

    public string DatabaseKey => ReadingDb.Key;

    public int SchemaVersion => Version;

    public SchemaOutcome EnsureSchema() => _sp.GetRequiredService<ReadingDatabase>().SchemaOutcome;
}
