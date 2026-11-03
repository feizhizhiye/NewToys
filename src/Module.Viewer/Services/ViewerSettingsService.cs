using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewToys.Shared.Services;
using NewToys.Viewer.Controls;

namespace NewToys.Viewer.Services;

/// <summary>
/// 查看器设置与记忆：落 <c>config\viewer.json</c>（经 Core 的 <see cref="IOptionsStore"/>）。
/// <para>
/// 三件事：排版参数（字号 / 行距 / 页边距）、最近打开（≤20）、按文件记忆阅读位置。
/// </para>
/// <para>
/// 🔴 位置键 = <c>路径|文件大小|最后修改时间</c>，⛔ 不用纯路径 —— 同名文件互相覆盖、
/// 文件被替换后还会把旧进度套上去。
/// </para>
/// </summary>
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

    private const string KeyReaderFontSize = "readerFontSize";
    private const string KeyReaderLineSpacing = "readerLineSpacing";
    private const string KeyReaderPadding = "readerPadding";
    private const string KeyReaderIndent = "readerIndent";
    private const string KeyReaderSpacing = "readerSpacing";
    private const string KeyReaderTheme = "readerTheme";
    private const string KeyReaderPositions = "readerPositions";
    private const string KeyBookmarks = "bookmarks";
    private const string KeyTxtRules = "txtRules";
    private const string KeyAudioVolume = "audioVolume";

    /// <summary>长文本排版参数（阅读器壳的持久化来源）。</summary>
    public ReaderTypography ReaderOptions => new()
    {
        FontSize = ReadDouble(KeyReaderFontSize, 17, 12, 34),
        LineSpacing = ReadDouble(KeyReaderLineSpacing, 1.8, 1.2, 2.6),
        Padding = ReadDouble(KeyReaderPadding, 48, 0, 160),
        Indent = ReadBool(KeyReaderIndent, true),
        ParagraphSpacing = ReadBool(KeyReaderSpacing, true) ? 1 : 0,
        Theme = (int)ReadDouble(KeyReaderTheme, 0, 0, 3),
    };

    /// <summary>保存长文本排版参数。</summary>
    public void SetReaderOptions(ReaderTypography options)
    {
        if (options is null) return;
        WriteDouble(KeyReaderFontSize, Math.Clamp(options.FontSize, 12, 34));
        WriteDouble(KeyReaderLineSpacing, Math.Clamp(options.LineSpacing, 1.2, 2.6));
        WriteDouble(KeyReaderPadding, Math.Clamp(options.Padding, 0, 160));
        WriteBool(KeyReaderIndent, options.Indent);
        WriteBool(KeyReaderSpacing, options.ParagraphSpacing > 0);
        WriteDouble(KeyReaderTheme, Math.Clamp(options.Theme, 0, 3));
    }

    /// <summary>取长文本阅读位置（章 + 章内比例）；没有记录返回 <c>null</c>。</summary>
    public ViewerReaderState? GetReaderPosition(string path)
    {
        var key = ProgressKey(path);
        if (key.Length == 0) return null;

        var map = LoadJson<Dictionary<string, ReaderPositionDto>>(KeyReaderPositions)
                  ?? new Dictionary<string, ReaderPositionDto>(StringComparer.OrdinalIgnoreCase);

        return map.TryGetValue(key, out var dto)
            ? new ViewerReaderState(dto.ChapterIndex, Math.Clamp(dto.Ratio, 0, 1))
            : null;
    }

    /// <summary>存长文本阅读位置。</summary>
    public void SetReaderPosition(string path, ViewerReaderState state)
    {
        var key = ProgressKey(path);
        if (key.Length == 0) return;

        var map = LoadJson<Dictionary<string, ReaderPositionDto>>(KeyReaderPositions)
                  ?? new Dictionary<string, ReaderPositionDto>(StringComparer.OrdinalIgnoreCase);

        map[key] = new ReaderPositionDto
        {
            ChapterIndex = Math.Max(0, state.ChapterIndex),
            Ratio = Math.Clamp(state.Ratio, 0, 1),
        };

        if (map.Count > 500)
        {
            foreach (var k in map.Keys.Take(map.Count - 500).ToList()) map.Remove(k);
        }

        SaveJson(KeyReaderPositions, map);
    }

    /// <summary>取某本书的书签（按文件全路径存，不随文件修改时间失效）。</summary>
    public IReadOnlyList<ReaderBookmark> GetBookmarks(string path)
    {
        var key = BookmarkKey(path);
        if (key.Length == 0) return Array.Empty<ReaderBookmark>();

        var map = LoadJson<Dictionary<string, List<BookmarkDto>>>(KeyBookmarks)
                  ?? new Dictionary<string, List<BookmarkDto>>(StringComparer.OrdinalIgnoreCase);

        if (!map.TryGetValue(key, out var list) || list is null) return Array.Empty<ReaderBookmark>();

        return list.Select(dto => new ReaderBookmark
        {
            Id = dto.Id,
            ChapterTitle = dto.Title,
            ChapterIndex = dto.ChapterIndex,
            Ratio = Math.Clamp(dto.Ratio, 0, 1),
            Preview = dto.Preview,
            CreatedAt = dto.CreatedAt,
        }).ToList();
    }

    /// <summary>整本替换某本书的书签列表。</summary>
    public void SetBookmarks(string path, IEnumerable<ReaderBookmark> bookmarks)
    {
        var key = BookmarkKey(path);
        if (key.Length == 0) return;

        var map = LoadJson<Dictionary<string, List<BookmarkDto>>>(KeyBookmarks)
                  ?? new Dictionary<string, List<BookmarkDto>>(StringComparer.OrdinalIgnoreCase);

        var list = bookmarks.Select(b => new BookmarkDto
        {
            Id = b.Id,
            ChapterIndex = b.ChapterIndex,
            Ratio = b.Ratio,
            Title = b.ChapterTitle,
            Preview = b.Preview,
            CreatedAt = b.CreatedAt,
        }).ToList();

        if (list.Count == 0) map.Remove(key);
        else map[key] = list;

        SaveJson(KeyBookmarks, map);
    }

    /// <summary>用户自定义的 txt 章节规则（排在内置规则之前）。</summary>
    public IReadOnlyList<TxtChapterRule> CustomTxtRules
        => LoadJson<List<TxtChapterRule>>(KeyTxtRules) ?? new List<TxtChapterRule>();

    /// <summary>保存自定义章节规则。</summary>
    public void SetCustomTxtRules(IEnumerable<TxtChapterRule> rules)
    {
        var list = rules.Where(r => !string.IsNullOrWhiteSpace(r.Pattern)).ToList();
        if (list.Count == 0) _opts.Remove(Module, KeyTxtRules);
        else SaveJson(KeyTxtRules, list);
    }

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

    private bool ReadBool(string key, bool fallback)
    {
        var raw = _opts.Get(Module, key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
    }

    private void WriteBool(string key, bool value) => _opts.Set(Module, key, value ? "true" : "false");

    private T? LoadJson<T>(string key) where T : class
    {
        var raw = _opts.Get(Module, key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<T>(raw); }
        catch { return null; }
    }

    private void SaveJson<T>(string key, T value) => _opts.Set(Module, key, JsonSerializer.Serialize(value));

    /// <summary>书签键 = 文件全路径（文件被修改 / 替换后书签仍指向同一本书）。</summary>
    private static string BookmarkKey(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var fi = new FileInfo(path);
            return fi.Exists ? fi.FullName : "";
        }
        catch { return ""; }
    }

    private sealed class ReaderPositionDto
    {
        public int ChapterIndex { get; set; }
        public double Ratio { get; set; }
    }

    private sealed class BookmarkDto
    {
        public string Id { get; set; } = "";
        public int ChapterIndex { get; set; }
        public double Ratio { get; set; }
        public string Title { get; set; } = "";
        public string Preview { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }

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
