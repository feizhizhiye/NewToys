using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Shared.Services;

namespace NewToys.Viewer.Services;

/// <summary>查看器设置与记忆：落 config\viewer.json（经 Core 的 IOptionsStore）。（WHY: VW-017）</summary>
public sealed class ViewerSettingsService
{
    private const string Module = "viewer";
    private const string KeyFontSize = "fontSize";
    private const string KeyLineSpacing = "lineSpacing";
    private const string KeyPagePadding = "pagePadding";
    private const string KeyRecent = "recent";
    private const string KeyPositions = "positions";

    /// <summary>最近打开上限。</summary>
    private const int RecentMax = 20;

    private readonly IOptionsStore _opts;

    public ViewerSettingsService(IOptionsStore opts) => _opts = opts;

    // ---------- 排版 ----------

    /// <summary>正文字号（px），默认 15。</summary>
    public double FontSize
    {
        get => ReadDouble(KeyFontSize, 15, 9, 40);
        set => WriteDouble(KeyFontSize, Math.Clamp(value, 9, 40));
    }

    /// <summary>行距倍数（相对字号），默认 1.7。</summary>
    public double LineSpacing
    {
        get => ReadDouble(KeyLineSpacing, 1.7, 1.0, 3.0);
        set => WriteDouble(KeyLineSpacing, Math.Clamp(value, 1.0, 3.0));
    }

    /// <summary>页边距（px），默认 32。</summary>
    public double PagePadding
    {
        get => ReadDouble(KeyPagePadding, 32, 0, 200);
        set => WriteDouble(KeyPagePadding, Math.Clamp(value, 0, 200));
    }

    // ---------- 长文本阅读器（txt / md / log） ----------
    // 🔴 长文本阅读已并入「网络阅读」模块；本节排版 / 位置 / 书签 / 章节规则的键随批 3 下线，
    //    config\viewer.json 里的旧键成死数据（无害，不做迁移）。

    private const string KeyAudioVolume = "audioVolume";

    /// <summary>播放器音量 0~100。</summary>
    public int AudioVolume
    {
        get => (int)ReadDouble(KeyAudioVolume, 80, 0, 100);
        set => WriteDouble(KeyAudioVolume, Math.Clamp(value, 0, 100));
    }

    // ---------- 音乐播放列表（关掉再开能续上） ----------

    private const string KeyAudioPlaylist = "audioPlaylist";

    /// <summary>上次的播放列表（只留磁盘上还在的文件；没有返回空）+ 续播信息（位置秒 / 模式）。</summary>
    public (IReadOnlyList<string> Paths, int Index, double PositionSeconds, int Mode) AudioPlaylist
    {
        get
        {
            var dto = LoadJson<AudioPlaylistDto>(KeyAudioPlaylist);
            if (dto?.Paths is not { Count: > 0 }) return (Array.Empty<string>(), 0, 0, 0);

            var paths = dto.Paths
                .Where(static p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return (paths, Math.Clamp(dto.Index, 0, Math.Max(paths.Count - 1, 0)), Math.Max(0, dto.PositionSeconds), Math.Clamp(dto.Mode, 0, 2));
        }
        set
        {
            var paths = value.Paths
                .Where(static p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();
            if (paths.Count == 0) { _opts.Remove(Module, KeyAudioPlaylist); return; }
            SaveJson(KeyAudioPlaylist, new AudioPlaylistDto
            {
                Paths = paths,
                Index = Math.Clamp(value.Index, 0, paths.Count - 1),
                PositionSeconds = Math.Max(0, value.PositionSeconds),
                Mode = Math.Clamp(value.Mode, 0, 2),
            });
        }
    }

    private sealed class AudioPlaylistDto
    {
        public List<string> Paths { get; set; } = new();
        public int Index { get; set; }
        public double PositionSeconds { get; set; }
        public int Mode { get; set; }
    }

    // ---------- 最近打开 ----------

    /// <summary>最近打开（新的在前，已剔除磁盘上不存在的路径）。</summary>
    public IReadOnlyList<string> Recent
    {
        get
        {
            var raw = _opts.Get(Module, KeyRecent);
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

            List<string>? list;
            try { list = JsonSerializer.Deserialize<List<string>>(raw); }
            catch { return Array.Empty<string>(); }
            if (list is null) return Array.Empty<string>();

            return list.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Take(RecentMax)
                       .ToList();
        }
    }

    /// <summary>记一条最近打开（去重后置顶，超限截断）。</summary>
    public void PushRecent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var list = new List<string> { path };
        list.AddRange(Recent.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)));
        _opts.Set(Module, KeyRecent, JsonSerializer.Serialize(list.Take(RecentMax).ToList()));
    }

    /// <summary>清空最近打开。</summary>
    public void ClearRecent() => _opts.Remove(Module, KeyRecent);

    // ---------- 阅读位置 ----------

    /// <summary>取某文件的阅读进度（0~1）；没有记录返回 0。</summary>
    public double GetProgress(string path)
    {
        var key = ProgressKey(path);
        if (key.Length == 0) return 0;
        var dict = LoadPositions();
        return dict.TryGetValue(key, out var v) ? Math.Clamp(v, 0, 1) : 0;
    }

    /// <summary>存某文件的阅读进度（&lt;0.01 视为从头读，直接删记录不占空间）。</summary>
    public void SetProgress(string path, double progress)
    {
        var key = ProgressKey(path);
        if (key.Length == 0) return;

        var dict = LoadPositions();
        if (progress < 0.01) dict.Remove(key);
        else dict[key] = Math.Clamp(progress, 0, 1);

        // 只留最近 500 条，避免 json 无限膨胀
        if (dict.Count > 500)
        {
            foreach (var k in dict.Keys.Take(dict.Count - 500).ToList()) dict.Remove(k);
        }

        _opts.Set(Module, KeyPositions, JsonSerializer.Serialize(dict));
    }

    private Dictionary<string, double> LoadPositions()
    {
        var raw = _opts.Get(Module, KeyPositions);
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(raw);
            return dict is null
                ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, double>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>进度键：路径 + 大小 + 修改时间；文件不存在返回空串。</summary>
    private static string ProgressKey(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var fi = new FileInfo(path);
            if (!fi.Exists) return "";
            return fi.FullName + "|" + fi.Length.ToString(CultureInfo.InvariantCulture)
                   + "|" + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch { return ""; }
    }

    // ---------- 底层 ----------

    private T? LoadJson<T>(string key) where T : class
    {
        var raw = _opts.Get(Module, key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<T>(raw); }
        catch { return null; }
    }

    private void SaveJson<T>(string key, T value) => _opts.Set(Module, key, JsonSerializer.Serialize(value));

    private double ReadDouble(string key, double fallback, double min, double max)
    {
        var raw = _opts.Get(Module, key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, min, max)
            : fallback;
    }

    private void WriteDouble(string key, double value)
        => _opts.Set(Module, key, value.ToString("0.##", CultureInfo.InvariantCulture));
}
