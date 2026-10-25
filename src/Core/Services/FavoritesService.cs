using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

/// <summary>
/// <see cref="IFavoritesService"/> 实现：落到 <c>config\shell.json</c> 的 <c>favorites</c> 节（前是独立的 favorites.json）。
/// 首次运行（尚无该文件）时用一组默认常用功能做种子，保持与旧版首页硬编码一致 —— 升级不清空。
/// </summary>
public class FavoritesService : IFavoritesService
{
    /// <summary>首次运行（尚无收藏）的默认常用功能。</summary>
    private static readonly string[] DefaultKeys =
    {
        "script-info", "script-library", "web-scrape", "video-list", "settings-dns", "settings-general"
    };

    private readonly ShellConfigStore _shell;
    private readonly List<string> _keys;

    public FavoritesService(ShellConfigStore shell)
    {
        _shell = shell;
        _keys = Load();
    }

    public IReadOnlyList<string> Keys => _keys;

    /// <summary>收藏所在的配置文件（<c>shell.json</c>，前是独立的 <c>favorites.json</c>）。</summary>
    public string FilePath => _shell.FilePath;

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
            var arr = new JsonArray();
            foreach (var k in _keys) arr.Add(k);
            _shell.Set("favorites", new JsonObject { ["Keys"] = arr });
        }
        catch { /* 保存失败不打断 UI；下一次改动会再试 */ }
        Changed?.Invoke();
    }

    private List<string> Load()
    {
        try
        {
            if (_shell.Get("favorites") is JsonObject node
                && node.TryGetPropertyValue("Keys", out var keysNode)
                && keysNode is JsonArray arr)
            {
                var list = new List<string>();
                foreach (var item in arr)
                {
                    if (item is not JsonValue v) continue;
                    if (v.GetValueKind() != JsonValueKind.String) continue;
                    var s = v.GetValue<string>();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
                return list;
            }
        }
        catch { /* 配置损坏时回落到默认，不阻断启动 */ }
        return DefaultKeys.ToList();
    }
}
