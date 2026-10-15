using System;
using NewToys.Core.Services;

namespace NewToys.MediaAssets.Services;

/// <summary>
/// 媒体资产库的表结构（**13 张表 + 10 条索引**，需求 §6.6 数据模型）。
/// <para>
/// 落**同一个** <c>data\newtoys.db</c> —— 与视频工具**共库但表级隔离、互不引用**
/// （那边是 ffmpeg 命令 + 批量执行，表名 <c>video_*</c>）。
/// </para>
/// <para>
/// 构造时幂等建表（<c>CREATE TABLE IF NOT EXISTS</c>）+ 老库补列（<c>ALTER TABLE ADD COLUMN</c>，⛔ 只加可空列）；
/// 库不可用（<see cref="SqliteDatabase.IsAvailable"/> = false）时**不抛**，各页面自行降级提示。
/// </para>
/// </summary>
public class AssetDatabase
{
    private readonly SqliteDatabase _db;

    public AssetDatabase(SqliteDatabase db)
    {
        _db = db;
        EnsureSchema();
    }

    /// <summary>库是否可用（false = 资产库降级：页面照常显示 + 状态栏提示，⛔ 不崩）。</summary>
    public bool IsAvailable => _db.IsAvailable;

    /// <summary>数据库文件全路径（界面提示用）。</summary>
    public string DatabasePath => _db.DatabasePath;

    /// <summary>连接入口（各服务用完即 Dispose；连接池由 Microsoft.Data.Sqlite 管）。</summary>
    public SqliteDatabase Db => _db;

    // ==================== 建表（幂等） ====================

    private void EnsureSchema()
    {
        if (!_db.IsAvailable) return;

        _db.EnsureTable(Tables);
        _db.EnsureTable(Indexes);

        // 前向兼容：老库（先建过表、后又加了字段）缺列时补上。
        // 与视频工具同一套做法（v3.16）：⛔ 只加可空列，⛔ 不回填数据。
        EnsureColumn("assets", "metadata_json", "TEXT");
        EnsureColumn("assets", "rating", "INTEGER");
        EnsureColumn("assets", "description", "TEXT");
        EnsureColumn("file_locations", "quick_hash", "TEXT");
        EnsureColumn("file_locations", "inode", "TEXT");
        EnsureColumn("file_locations", "device", "TEXT");
        EnsureColumn("file_locations", "last_seen_at", "TEXT");
        EnsureColumn("storages", "online", "INTEGER");
        EnsureColumn("assets", "is_favorite", "INTEGER");   // NT-058：详情卡「★ 收藏」
        EnsureColumn("operation_logs", "finished_at", "TEXT");
        EnsureColumn("scan_jobs", "error_count", "INTEGER");
    }

    /// <summary>给已有表补一列（幂等）：列已存在时 SQLite 报 duplicate column name，直接忽略。</summary>
    private void EnsureColumn(string table, string column, string type)
    {
        if (!_db.IsAvailable) return;
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            cmd.ExecuteNonQuery();
        }
        catch { /* 已存在 / 库只读：都不影响使用 */ }
    }

    private const string Tables = @"
CREATE TABLE IF NOT EXISTS storages (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  root_path TEXT NOT NULL,
  type TEXT NOT NULL DEFAULT 'local',      -- local/removable/nas
  online INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS assets (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  type TEXT NOT NULL,                      -- video/music/book/image/document/other
  title TEXT,
  display_name TEXT,
  description TEXT,
  status TEXT NOT NULL DEFAULT 'active',   -- active/trashed/missing/archived
  rating INTEGER DEFAULT 0,
  metadata_json TEXT DEFAULT '{}',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS file_locations (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  asset_id INTEGER NOT NULL,
  storage_id INTEGER NOT NULL,
  path TEXT NOT NULL,
  relative_path TEXT,
  file_name TEXT NOT NULL,
  ext TEXT,
  mime_type TEXT,
  size INTEGER,
  mtime TEXT,
  hash TEXT,
  quick_hash TEXT,
  inode TEXT,
  device TEXT,
  is_primary INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'active',   -- active/missing/deleted/moved/error
  last_seen_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (asset_id) REFERENCES assets(id),
  FOREIGN KEY (storage_id) REFERENCES storages(id)
);
CREATE TABLE IF NOT EXISTS tags (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  namespace TEXT,
  color TEXT,
  parent_id INTEGER,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(namespace, name)
);
CREATE TABLE IF NOT EXISTS asset_tags (
  asset_id INTEGER NOT NULL,
  tag_id INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY (asset_id, tag_id)
);
CREATE TABLE IF NOT EXISTS categories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  parent_id INTEGER,
  sort_order INTEGER DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS asset_categories (
  asset_id INTEGER NOT NULL,
  category_id INTEGER NOT NULL,
  is_primary INTEGER DEFAULT 0,
  PRIMARY KEY (asset_id, category_id)
);
CREATE TABLE IF NOT EXISTS collections (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  description TEXT,
  type TEXT NOT NULL DEFAULT 'manual',     -- manual/smart
  rule_json TEXT,
  parent_id INTEGER,
  sort_mode TEXT DEFAULT 'manual',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS collection_assets (
  collection_id INTEGER NOT NULL,
  asset_id INTEGER NOT NULL,
  sort_order INTEGER DEFAULT 0,
  added_at TEXT NOT NULL,
  PRIMARY KEY (collection_id, asset_id)
);
CREATE TABLE IF NOT EXISTS relations (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  source_asset_id INTEGER NOT NULL,
  target_asset_id INTEGER NOT NULL,
  relation_type TEXT NOT NULL,             -- subtitle_of/cover_of/same_work/version_of/format_of/reference/related_to
  metadata_json TEXT DEFAULT '{}',
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS operation_logs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  op_type TEXT NOT NULL,
  entity_type TEXT NOT NULL,
  entity_id INTEGER,
  before_json TEXT,
  after_json TEXT,
  status TEXT NOT NULL,                    -- pending/running/success/failed/rolled_back/cancelled
  error TEXT,
  reversible INTEGER DEFAULT 0,
  created_at TEXT NOT NULL,
  finished_at TEXT
);
CREATE TABLE IF NOT EXISTS scan_jobs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  storage_id INTEGER,
  mode TEXT NOT NULL,                      -- full/incremental
  status TEXT NOT NULL DEFAULT 'pending',
  total_files INTEGER DEFAULT 0,
  scanned_files INTEGER DEFAULT 0,
  added_files INTEGER DEFAULT 0,
  updated_files INTEGER DEFAULT 0,
  missing_files INTEGER DEFAULT 0,
  error_count INTEGER DEFAULT 0,
  started_at TEXT,
  finished_at TEXT,
  created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS scan_errors (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  scan_job_id INTEGER NOT NULL,
  path TEXT,
  error_type TEXT,
  message TEXT,
  created_at TEXT NOT NULL
);
";

    private const string Indexes = @"
CREATE INDEX IF NOT EXISTS idx_assets_type_status ON assets(type, status);
CREATE INDEX IF NOT EXISTS idx_assets_updated_at ON assets(updated_at);
CREATE INDEX IF NOT EXISTS idx_locations_asset ON file_locations(asset_id);
CREATE INDEX IF NOT EXISTS idx_locations_hash ON file_locations(hash);
CREATE INDEX IF NOT EXISTS idx_locations_quick_hash ON file_locations(quick_hash);
CREATE INDEX IF NOT EXISTS idx_locations_storage_relpath ON file_locations(storage_id, relative_path);
CREATE INDEX IF NOT EXISTS idx_asset_tags_tag ON asset_tags(tag_id);
CREATE INDEX IF NOT EXISTS idx_collection_assets_asset ON collection_assets(asset_id);
CREATE INDEX IF NOT EXISTS idx_relations_source ON relations(source_asset_id);
CREATE INDEX IF NOT EXISTS idx_relations_target ON relations(target_asset_id);
CREATE INDEX IF NOT EXISTS idx_oplogs_entity ON operation_logs(entity_type, entity_id);
";
}
