using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NewToys.Core.Services;

/// <summary>框架级偏好（profiles\&lt;配置&gt;\config\shell.json）的唯一读写入口。（WHY: CR-046）</summary>
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
