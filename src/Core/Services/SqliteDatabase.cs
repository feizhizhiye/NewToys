using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NewToys.Core.Services;

/// <summary>
/// 工程内**唯一的 SQLite 入口**：所有结构化本地数据（当前是视频库的媒体信息索引，
/// 后续更多配置项也会陆续搬进来）都落同一个库文件 <c>&lt;应用根&gt;\data\newtoys.db</c>。
/// <para>
/// 为什么放 <c>data\</c> 而不是 <c>config\</c>：<c>config\</c> 下的 <c>*.json</c> 会被
/// 「设置 → 数据管理」整份备份 / 导入，而这里的索引是**可重建的缓存**（文件没了重新探测即可），
/// 不该跟着备份来回搬（见规范 §9 对"设置数据 / 功能数据"的划分）。
/// </para>
/// <para>
/// 本类只做三件事：定位库文件、建库（schema 由各功能自己建，见 <see cref="EnsureTable"/>）、
/// 发连接。它**不认识任何业务表**，因此新增数据表不需要动它。
/// </para>
/// <para>
/// ⚠️ 取连接用 <see cref="Open"/>，用完即 <c>Dispose</c>（连接池由 Microsoft.Data.Sqlite 自己管）。
/// 并发写入很轻（只在 UI 线程上按批落库），所以不做跨线程互斥；<c>busy_timeout</c> 兜住偶发争用。
/// </para>
/// </summary>
public class SqliteDatabase
{
    private readonly ConfigStoreService _store;

    /// <summary>数据库文件全路径（界面提示用）。</summary>
    public string DatabasePath => Path.Combine(_store.DataDir, "newtoys.db");

    /// <summary>数据库是否可用（库建不出来时为 false，依赖它的页面应给出自己的提示而不是崩）。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>建库失败的原因（<see cref="IsAvailable"/> 为 false 时有效）。</summary>
    public string LastError { get; private set; } = "";

    public SqliteDatabase(ConfigStoreService store)
    {
        _store = store;
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Directory.CreateDirectory(_store.DataDir);
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS meta (" +
                "  key TEXT PRIMARY KEY," +
                "  value TEXT NOT NULL" +
                ");";
            cmd.ExecuteNonQuery();
            IsAvailable = true;
            LastError = "";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LastError = ex.Message;
        }
    }

    /// <summary>打开一个连接（调用方负责 Dispose）。</summary>
    public SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=3000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>建表 / 建索引等 DDL（幂等语句，功能服务在构造时调一次即可）。</summary>
    public bool EnsureTable(string ddl)
    {
        if (!IsAvailable) return false;
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}
