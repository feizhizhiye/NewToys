using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewToys.Core.Services;
using NewToys.SystemTools.Models;

namespace NewToys.SystemTools.Services;

/// <summary>
/// <see cref="ISystemCommandStore"/> 实现（NT-091）：一份 JSON 清单，落
/// <c>&lt;ConfigDir&gt;\system\commands.json</c>（<c>config\</c> 下的 json 才进备份）。
/// <para>🔴 写盘走 Core 的 <c>AtomicJsonFile</c>（temp + Move）；读失败 / 坏文件 ⇒ 按空清单处理（⛔ 不拦启动）。</para>
/// </summary>
public sealed class SystemCommandStore : ISystemCommandStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // 中文不转 \uXXXX
    };

    private readonly ConfigStoreService _cfg;
    private readonly List<SystemCommand> _items = new();

    public SystemCommandStore(ConfigStoreService cfg)
    {
        _cfg = cfg;
        Load();
    }

    /// <inheritdoc />
    public string FilePath => Path.Combine(_cfg.ConfigDir, "system", "commands.json");

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> Items => _items;

    /// <inheritdoc />
    public SystemCommand Add(string title)
    {
        var item = new SystemCommand { Title = title };
        _items.Add(item);
        Save();
        return item;
    }

    /// <inheritdoc />
    public SystemCommand Insert(SystemCommand item)
    {
        _items.Add(item);
        Save();
        return item;
    }

    /// <inheritdoc />
    public bool Remove(SystemCommand item)
    {
        if (!_items.Remove(item)) return false;
        Save();
        return true;
    }

    /// <inheritdoc />
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(_items, Opts));
        }
        catch
        {
            // 写不动 ⇒ 内存里保留，页面照常可用（⛔ 不让整页挂掉）。
        }
    }

    /// <inheritdoc />
    public void Reload() => Load();

    /// <inheritdoc />
    public (int Added, int Updated) Merge(IEnumerable<SystemCommand> incoming)
    {
        var added = 0;
        var updated = 0;
        foreach (var it in incoming)
        {
            if (it is null || string.IsNullOrWhiteSpace(it.Id)) continue;

            var exist = _items.FirstOrDefault(x => string.Equals(x.Id, it.Id, StringComparison.Ordinal));
            if (exist is null) { _items.Add(it); added++; continue; }

            exist.Title = it.Title;
            exist.Category = it.Category;
            exist.Description = it.Description;
            exist.Command = it.Command;
            exist.Shell = it.Shell;
            exist.NeedAdmin = it.NeedAdmin;
            exist.RunHidden = it.RunHidden;
            exist.IsFavorite = it.IsFavorite;
            updated++;
        }

        if (added + updated > 0) Save();
        return (added, updated);
    }

    private void Load()
    {
        _items.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<SystemCommand>>(File.ReadAllText(FilePath), Opts);
            if (list is null) return;
            foreach (var it in list) _items.Add(it);
        }
        catch
        {
            // 坏文件 ⇒ 空清单。
        }
    }
}
