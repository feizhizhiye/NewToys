using System;
using System.Text.Json;

namespace NewToys.Core.Services;

/// <summary>
/// <b>内容型 JSON 文档表</b>：把散在 <c>config\*.json</c> 的「功能数据」
/// （书签 / ping 目标 / 抓取任务 / hosts 方案 / 视频任务队列 / 脚本手工数据）收进模块库。
/// <para>
/// 表结构 = <c>&lt;前缀&gt;_docs(name TEXT PRIMARY KEY, payload TEXT NOT NULL, updated_at TEXT NOT NULL)</c>：
/// 一条文档一行，<c>payload</c> 存**整份 JSON 原文**。
/// </para>
/// <para>
/// 🔴 为什么不拆成关系表：这些结构都是「整份读、整份写」的文档（脚本库还带三层嵌套：集合 → 视图 → 逐节列覆盖），
/// 拆表要把读方的模型全改成 SQL 投影，收益只有「人能用 sqlite 查一查」，风险远大于收益。
/// 本类保证的是卡片要的两件事：**文件不再散在 config\ 里**、**同一份数据只有一个写入点**。
/// 真要按字段查询时，SQLite 的 <c>json_extract(payload,'$.x')</c> 直接可用（JSON1 默认编译进来）。
/// </para>
/// <para>
/// 表名前缀纪律（framework §7）：模块只能用自己的前缀（<c>script_ / web_ / video_ / asset_</c>）。
/// DDL **不在这里执行** —— 由各模块的 <c>ILibrarySchemaContributor</c> 统一建（建表只有一个入口）。
/// </para>
/// </summary>
public sealed class JsonDocStore
{
    private readonly SqliteDatabase _db;
    private readonly string _table;
    private bool _ensured;

    /// <param name="db">本模块的库。</param>
    /// <param name="modulePrefix">模块表前缀（<c>script</c> / <c>web</c> / <c>video</c>），最终表名 = <c>&lt;前缀&gt;_docs</c>。</param>
    public JsonDocStore(SqliteDatabase db, string modulePrefix)
    {
        if (string.IsNullOrWhiteSpace(modulePrefix))
            throw new ArgumentException("modulePrefix 不能为空。", nameof(modulePrefix));

        _db = db;
        // 表名只可能来自代码里的常量 ⇒ 直接内联进 SQL，⛔ 不接受外部拼串。
        _table = modulePrefix.TrimEnd('_') + "_docs";
    }

    /// <summary>表名（诊断 / 日志用）。</summary>
    public string TableName => _table;

    /// <summary>库是否可用（false 时读一律返回 null、写静默跳过）。</summary>
    public bool IsAvailable => _db.IsAvailable;

    /// <summary>本表 DDL（幂等）。给模块的 schema 贡献者拼接用。</summary>
    public string Ddl => $"""
        CREATE TABLE IF NOT EXISTS {_table} (
            name       TEXT PRIMARY KEY,
            payload    TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        """;

    /// <summary>
    /// 建表（幂等）。**正常路径由模块的 schema 贡献者在启动时调**，保证「不打开这页表也在」。
    /// <para>
    /// 读取方在第一次访问时也会懒补一次（<see cref="LoadRaw"/> 等内部调）——
    /// 防的是「服务在 schema 阶段之前就被解析」的时序问题（只会多跑一次幂等 DDL，不会盖数据）。
    /// </para>
    /// </summary>
    public bool EnsureSchema()
    {
        if (_ensured) return true;
        _ensured = _db.EnsureTable(Ddl);
        return _ensured;
    }

    /// <summary>读整份文档原文（不存在 / 库不可用 / 读失败 ⇒ null，⛔ 不抛）。</summary>
    public string? LoadRaw(string name)
    {
        if (!IsAvailable || string.IsNullOrEmpty(name)) return null;
        EnsureSchema();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT payload FROM {_table} WHERE name = $n;";
            cmd.Parameters.AddWithValue("$n", name);
            return cmd.ExecuteScalar() as string;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewToys][docs] 读 {_table}/{name} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>反序列化读（不存在 / 坏 JSON ⇒ null）。</summary>
    public T? Load<T>(string name, JsonSerializerOptions? options = null) where T : class
    {
        var raw = LoadRaw(name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<T>(raw, options); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewToys][docs] {_table}/{name} 反序列化失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>写整份文档原文（upsert）。库不可用 ⇒ 静默跳过；其余异常向上抛，由调用方决定提示方式。</summary>
    public void SaveRaw(string name, string json)
    {
        if (!IsAvailable || string.IsNullOrEmpty(name)) return;
        EnsureSchema();
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO {_table} (name, payload, updated_at) VALUES ($n, $p, $t) " +
            "ON CONFLICT(name) DO UPDATE SET payload = excluded.payload, updated_at = excluded.updated_at;";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", json);
        cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>序列化写。</summary>
    public void Save<T>(string name, T value, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(value, options);
        SaveRaw(name, json);
    }

    /// <summary>文档是否存在（含空 payload 的行）。</summary>
    public bool Has(string name)
    {
        if (!IsAvailable || string.IsNullOrEmpty(name)) return false;
        EnsureSchema();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {_table} WHERE name = $n LIMIT 1;";
            cmd.Parameters.AddWithValue("$n", name);
            return cmd.ExecuteScalar() != null;
        }
        catch { return false; }
    }

    /// <summary>删一条文档（空数据收尾用，等价于旧「空列表即删文件」）。</summary>
    public bool Delete(string name)
    {
        if (!IsAvailable || string.IsNullOrEmpty(name)) return false;
        EnsureSchema();
        try
        {
            using var conn = _db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {_table} WHERE name = $n;";
            cmd.Parameters.AddWithValue("$n", name);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }
}
