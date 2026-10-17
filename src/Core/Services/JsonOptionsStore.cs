using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IOptionsStore"/> 的 json 实现（v3.60 §3.5 / NT-061）：每个 module 一个
/// <c>&lt;配置根&gt;\config\&lt;module&gt;.json</c>，内容是**扁平的** <c>{ "key": "value" }</c>。
/// <list type="bullet">
///   <item><b>profile 隔离</b>：路径取自 <see cref="ConfigStoreService.ConfigDir"/>（= <c>profiles\&lt;当前配置&gt;\config</c>），
///   换配置天然读到另一份，⛔ 不加 profile 列 / 不加 profile 字段。</item>
///   <item><b>单例 + 内存缓存 + 写穿</b>：首次访问才读盘；<see cref="Set"/> / <see cref="Remove"/> 立即落盘。</item>
///   <item><b>失败不吞异常</b>：写盘失败 → 内存回滚到上一个有效值后抛出，由模块设置服务转成状态栏红字（需求 §3.5）。</item>
///   <item><b>手改友好</b>：文件用缩进 + 不转义中文（路径里的中文不会变成 <c>\uXXXX</c>）；
///   读的时候数字 / 布尔 / 对象按原文本收下（手改时 <c>"pageSize": 30</c> 不用加引号）；
///   文件解析失败先另存 <c>&lt;module&gt;.json.bak-&lt;时间戳&gt;</c> 再用空表接手（⛔ 不静默丢内容）。</item>
/// </list>
/// </summary>
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
    private readonly object _gate = new();

    /// <summary>module → 该模块的选项表（懒加载一次，之后以内存为准）。</summary>
    private readonly Dictionary<string, SortedDictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public JsonOptionsStore(ConfigStoreService cfg) => _cfg = cfg;

    /// <summary>某个 module 的配置文件全路径（设置页展示 / 排障用；模块自身⛔不该知道这个）。</summary>
    public string FilePath(string module) => Path.Combine(_cfg.ConfigDir, Validate(module) + ".json");

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
        var path = FilePath(module);
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
        Directory.CreateDirectory(_cfg.ConfigDir);
        File.WriteAllText(FilePath(module), JsonSerializer.Serialize(map, WriteOpts));
    }

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
