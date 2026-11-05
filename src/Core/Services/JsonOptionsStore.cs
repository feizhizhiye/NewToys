using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>IOptionsStore 的 json 实现（ §3.5）：每个 module 一个 &lt;配置根&gt;\config\&lt;module&gt;.json，内容是扁平的 { "key": "value" }。（WHY: CR-031）</summary>
public class JsonOptionsStore : IOptionsStore
{
    /// <summary>读盘策略：容忍尾逗号 + <c>//</c> 注释（手改留下的）。</summary>
    private static readonly JsonDocumentOptions DocOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>落盘策略：缩进 + 中文不转义（配置要人可读、可手改 —— 需求 §一 用户画像）。</summary>
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConfigStoreService _cfg;
    private readonly ShellConfigStore _shell;
    private readonly object _gate = new();

    /// <summary>module → 该模块的选项表（懒加载一次，之后以内存为准）。</summary>
    private readonly Dictionary<string, SortedDictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public JsonOptionsStore(ConfigStoreService cfg, ShellConfigStore shell)
    {
        _cfg = cfg;
        _shell = shell;
    }

    /// <summary>
    /// 分区 <c>ui</c>（列表页大小等框架级 KV）**不再单独占一个 <c>ui.json</c>**，
    /// 而是读写 <c>shell.json</c> 的 <c>ui</c> 节。其余分区仍是 <c>config\&lt;分区&gt;.json</c>。
    /// </summary>
    private const string ShellPartition = "ui";

    private static bool IsShellPartition(string module) => module.Equals(ShellPartition, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 某个 module 的配置文件全路径（设置页展示 / 排障用；模块自身⛔不该知道这个）。
    /// <para>分区文件统一落 <c>config\modules\&lt;分区&gt;.json</c>；<c>ui</c> 分区仍走 <c>shell.json</c>。</para>
    /// </summary>
    public string FilePath(string module)
        => IsShellPartition(module) ? _shell.FilePath : Path.Combine(_cfg.ModulesConfigDir, Validate(module) + ".json");

    public string? Get(string module, string key)
    {
        Validate(module);
        ValidateKey(key);
        lock (_gate)
        {
            var map = Load(module);
            return map.TryGetValue(key, out var v) ? v : null;
        }
    }

    public void Set(string module, string key, string? value)
    {
        Validate(module);
        ValidateKey(key);
        lock (_gate)
        {
            var map = Load(module);
            var existed = map.TryGetValue(key, out var old);

            if (value is null)
            {
                if (!existed) return;                       // 本来就没有 → 不写盘
                map.Remove(key);
            }
            else
            {
                if (existed && string.Equals(old, value, StringComparison.Ordinal)) return;   // 无变化 → 不写盘
                map[key] = value;
            }

            try { Save(module, map); }
            catch
            {
                // 保「上一个有效值」：内存也回滚，磁盘本来就是旧内容（需求 §3.5 不写盘）
                if (existed) map[key] = old!; else map.Remove(key);
                throw;
            }
        }
    }

    public bool Remove(string module, string key)
    {
        Validate(module);
        ValidateKey(key);
        lock (_gate)
        {
            var map = Load(module);
            if (!map.TryGetValue(key, out var old)) return false;

            map.Remove(key);
            try { Save(module, map); }
            catch
            {
                map[key] = old!;                            // 回滚后再抛，磁盘保持旧内容
                throw;
            }
            return true;
        }
    }

    // ==================== 内部：读 / 写 / 校验 ====================

    private SortedDictionary<string, string> Load(string module)
    {
        if (_cache.TryGetValue(module, out var cached)) return cached;

        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);

        if (IsShellPartition(module))
        {
            // 值从 shell.json 的 ui 节取；读失败按"没有"处理
            if (_shell.Get(ShellPartition) is JsonObject obj)
                foreach (var p in obj)
                    map[p.Key] = AsString(p.Value);
            _cache[module] = map;
            return map;
        }

        var path = FilePath(module);
        // 迁移兜底：config\modules\ 里没有、但旧的 config\<分区>.json 还在 ⇒ 读旧的
        // （搬文件被占用 / 只读时不让用户"设置全丢"）
        var legacy = Path.Combine(_cfg.ConfigDir, Validate(module) + ".json");
        if (!File.Exists(path) && File.Exists(legacy)) path = legacy;
        try
        {
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), DocOpts);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject())
                        map[p.Name] = AsString(p.Value);
            }
        }
        catch (JsonException)
        {
            // 手改坏了的文件：先改名留档，再用空表接手（下一次 Set 才会写出新文件）
            TryBackup(path);
        }
        catch
        {
            // 文件被占用 / 权限不足等：本次当作"还没有这个文件"，⛔ 不动磁盘上的东西
        }

        _cache[module] = map;
        return map;
    }

    private void Save(string module, SortedDictionary<string, string> map)
    {
        if (IsShellPartition(module))
        {
            if (map.Count == 0) { _shell.Set(ShellPartition, null); return; }
            var obj = new JsonObject();
            foreach (var kv in map) obj[kv.Key] = kv.Value;
            _shell.Set(ShellPartition, obj);
            return;
        }

        // 原子写 + 空表即删文件 —— 模块设置全是默认值时不再留一个 2 字节的 `{}`。
        AtomicJsonFile.Write(FilePath(module), JsonSerializer.Serialize(map, WriteOpts));
    }

    /// <summary>JsonNode → 存进 KV 表的字符串（字符串取原值，其余取 JSON 原文）。</summary>
    private static string AsString(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>() ?? "",
        _ => node.ToJsonString()
    };

    /// <summary>把任意 JSON 值读成 string：字符串取原值，其余取原始文本（数字 / 布尔 / 对象原样保留）。</summary>
    private static string AsString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => e.GetRawText()
    };

    private static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch { /* 留档失败也不能阻断：宁可覆盖一个本就读不动的文件 */ }
    }

    /// <summary>module 必须是**安全文件名基名**（防路径穿越：<c>..\..\x</c> 这种必须被挡住）。</summary>
    private static string Validate(string module)
    {
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("module 不能为空。", nameof(module));

        var m = module.Trim();
        if (!string.Equals(m, module, StringComparison.Ordinal))
            throw new ArgumentException($"module 首尾不能有空白：\"{module}\"", nameof(module));
        if (m is "." or "..")
            throw new ArgumentException($"非法的 module：\"{module}\"", nameof(module));
        if (m.Contains('\\') || m.Contains('/'))
            throw new ArgumentException($"module 不能含路径分隔符：\"{module}\"", nameof(module));
        if (m.Contains('.'))
            throw new ArgumentException($"module 是文件名基名（不带扩展名）：\"{module}\"", nameof(module));
        foreach (var c in Path.GetInvalidFileNameChars())
            if (m.IndexOf(c) >= 0)
                throw new ArgumentException($"module 含非法文件名字符：\"{module}\"", nameof(module));

        return m;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("key 不能为空。", nameof(key));
    }
}
