using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IFavoritesService"/> 实现：落到 <c>&lt;应用根&gt;\config\favorites.json</c>。
/// 首次运行（尚无该文件）时用一组默认常用功能做种子，保持与旧版首页硬编码一致 —— 升级不清空。
/// </summary>
public class FavoritesService : IFavoritesService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>首次运行（尚无 favorites.json）的默认常用功能。</summary>
    private static readonly string[] DefaultKeys =
    {
        "script-info", "script-library", "web-scrape", "video-list", "settings-dns", "settings-general"
    };

    private readonly ConfigStoreService _cfg;
    private readonly List<string> _keys;

    public FavoritesService(ConfigStoreService cfg)
    {
        _cfg = cfg;
        _keys = Load();
    }

    public IReadOnlyList<string> Keys => _keys;

    public string FilePath => Path.Combine(_cfg.ConfigDir, "favorites.json");

    public event Action? Changed;

    public bool Contains(string key) => !string.IsNullOrEmpty(key) && _keys.Contains(key);

    public void Pin(string key)
    {
        if (string.IsNullOrEmpty(key) || _keys.Contains(key)) return;
        _keys.Add(key);
        Persist();
    }

    public void Unpin(string key)
    {
        if (_keys.Remove(key)) Persist();
    }

    public void Toggle(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_keys.Remove(key)) _keys.Add(key);
        Persist();
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(_cfg.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new FavoritesFile { Keys = _keys.ToList() }, JsonOpts));
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    private List<string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var f = JsonSerializer.Deserialize<FavoritesFile>(File.ReadAllText(FilePath));
                if (f?.Keys != null)
                    return f.Keys.Where(k => !string.IsNullOrEmpty(k)).ToList();
            }
        }
        catch { /* 配置损坏时回落到默认，不阻断启动 */ }
        return DefaultKeys.ToList();
    }
}

/// <summary>favorites.json 的磁盘结构：<c>{ "Keys": ["script-info", ...] }</c>。</summary>
internal class FavoritesFile
{
    public List<string> Keys { get; set; } = new();
}
