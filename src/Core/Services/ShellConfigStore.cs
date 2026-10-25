using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NewToys.Core.Services;

/// <summary>
/// 框架级偏好（<c>profiles\&lt;配置&gt;\config\shell.json</c>）的**唯一读写入口**。
/// <para>
/// 5 份原本各占一个文件的框架偏好，合并成一个文件、按**节**分组：
/// <code>
/// {
///   "theme":        { "Mode": "Dark", "AccentId": "blue", "IsDark": true },
///   "window":       { "X": 275, "Y": 100, "Width": 1296, "Height": 808, "State": "Normal" },
///   "favorites":    { "Keys": [ "script-info", "web-scrape" ] },
///   "columnLayout": { "asset-all": { "Name": true } },
///   "ui":           { "list-page-size": "25" }
/// }
/// </code>
/// </para>
/// <para>
/// 设计取舍：
/// <list type="bullet">
///   <item><b>整节读写</b>（<see cref="Get"/> / <see cref="Set"/>）：调用方拿到的是自己的节，互不干扰；</item>
///   <item><b>单进程缓存 + 写穿</b>：首次访问读盘，之后以内存为准（和老的每文件实现一致）；</item>
///   <item><b>原子写 + 空即删</b>：委托 <see cref="AtomicJsonFile"/>；文档空了就删文件；</item>
///   <item><b>坏文件先归档</b>：解析失败改名 <c>shell.json.bak-&lt;时间戳&gt;</c> 再用空文档接手（⛔ 不静默丢）。</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ 只服务**框架层**（主题 / 窗口 / 收藏 / 列宽 / 页码大小）。模块设置仍走 <see cref="IOptionsStore"/>
/// 的独立文件（<c>config\&lt;模块&gt;.json</c>），⛔ 不要往这里塞模块的东西。
/// </para>
/// </summary>
public sealed class ShellConfigStore
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonDocumentOptions DocOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly ConfigStoreService _cfg;
    private readonly object _gate = new();
    private JsonObject? _doc;

    public ShellConfigStore(ConfigStoreService cfg) => _cfg = cfg;

    /// <summary>配置文件全路径（设置页「打开配置文件」/ 排障用）。</summary>
    public string FilePath => Path.Combine(_cfg.ConfigDir, "shell.json");

    /// <summary>取某一节的**副本**（调用方可随意改；null = 该节不存在）。</summary>
    public JsonNode? Get(string section)
    {
        lock (_gate)
        {
            var doc = Doc();
            return doc.TryGetPropertyValue(section, out var node) ? node?.DeepClone() : null;
        }
    }

    /// <summary>写某一节（<paramref name="node"/> 为 null ⇒ 删除该节）。每次调用立即落盘。</summary>
    public void Set(string section, JsonNode? node)
    {
        lock (_gate)
        {
            var doc = Doc();
            if (node is null)
            {
                if (!doc.Remove(section)) return;
            }
            else
            {
                doc[section] = node;
            }
            Save(doc);
        }
    }

    private JsonObject Doc()
    {
        if (_doc != null) return _doc;
        _doc = Load();
        return _doc;
    }

    private JsonObject Load()
    {
        // 旧版 5 份小文件必须先并进来再读：本类缓存整份文档，读完再迁 = 下次保存把搬来的节抹掉。
        _cfg.EnsureShellMigrated();

        var path = FilePath;
        try
        {
            if (!File.Exists(path)) return new JsonObject();
            if (JsonNode.Parse(File.ReadAllText(path), nodeOptions: null, documentOptions: DocOpts) is JsonObject obj)
                return obj;
        }
        catch (JsonException)
        {
            TryBackup(path);
        }
        catch
        {
            // 被占用 / 权限不足：本次按"还没有这个文件"处理，⛔ 不动磁盘
        }
        return new JsonObject();
    }

    private void Save(JsonObject doc)
    {
        try
        {
            AtomicJsonFile.Write(FilePath, doc.Count == 0 ? "{}" : doc.ToJsonString(WriteOpts));
        }
        catch
        {
            // 写盘失败（权限 / 磁盘）不致命：偏好丢了而已，界面照常
        }
    }

    private static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch { /* 留档失败也不能阻断：宁可覆盖一个本就读不动的文件 */ }
    }
}
