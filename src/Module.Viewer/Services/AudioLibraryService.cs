using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NewToys.Viewer.Controls;

namespace NewToys.Viewer.Services;

/// <summary>
/// 音频标签读取（标题 / 艺术家 / 专辑 / 时长 / 封面）。
/// <para>
/// 🔴 只读标签，⛔ 不回写文件 —— 标签编辑属于「媒体资产」的活，查看器只看不改。
/// </para>
/// <para>🔴 读不到标签不算失败：退回文件名 + 时长 0，播放照常。</para>
/// </summary>
public sealed class AudioLibraryService
{
    /// <summary>把一个音频文件读成播放列表条目。</summary>
    public AudioTrackItem Read(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        try
        {
            using var file = TagLib.File.Create(path);

            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? name : RepairMojibake(file.Tag.Title);
            var artist = RepairMojibake(file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist ?? "");
            var album = RepairMojibake(file.Tag.Album);

            byte[]? cover = null;
            var pictures = file.Tag.Pictures;
            if (pictures is { Length: > 0 })
            {
                var front = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover);
                var data = (front ?? pictures[0]).Data?.Data;
                if (data is { Length: > 0 }) cover = data;
            }

            // 内嵌没有 ⇒ 落到同目录 / 上一级目录的外挂图（多数 mp3 根本没内嵌封面）
            if (cover is null) cover = CoverFromDisk(path);

            return new AudioTrackItem
            {
                Path = path,
                Title = title,
                Artist = artist,
                Album = album,
                Duration = file.Properties?.Duration ?? TimeSpan.Zero,
                Cover = cover,
                TechText = TechTextOf(path, file.Properties),
            };
        }
        catch
        {
            return new AudioTrackItem
            {
                Path = path,
                Title = name,
                Artist = "",
                Album = "",
                Duration = TimeSpan.Zero,
                Cover = null,
                TechText = TechTextOf(path, null),
            };
        }
    }

    /// <summary>
    /// 一行技术信息：格式 / 采样率 / 码率 / 声道。
    /// <para>🔴 全部读不出来也不算错 —— 至少给出扩展名，界面不留空行。</para>
    /// </summary>
    private static string TechTextOf(string path, TagLib.Properties? properties)
    {
        var parts = new List<string>(4)
        {
            Path.GetExtension(path).TrimStart('.').ToUpperInvariant()
        };

        if (properties is not null)
        {
            if (properties.AudioSampleRate > 0)
                parts.Add((properties.AudioSampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz");
            if (properties.AudioBitrate > 0)
                parts.Add(properties.AudioBitrate.ToString(CultureInfo.InvariantCulture) + " kbps");
            if (properties.AudioChannels == 1) parts.Add("单声道");
            else if (properties.AudioChannels == 2) parts.Add("立体声");
            else if (properties.AudioChannels > 2)
                parts.Add(properties.AudioChannels.ToString(CultureInfo.InvariantCulture) + " 声道");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>批量读（拖入整个文件夹 / 多选文件）。</summary>
    public IReadOnlyList<AudioTrackItem> ReadMany(IEnumerable<string> paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Read).ToList();

    /// <summary>
    /// 找封面：① 内嵌标签（已在 <see cref="Read"/> 里取过，这里只管磁盘上的）→
    /// ② 同目录「同名图」→ ②b 宽松同名（歌曲名多带「歌手 - 」前缀 / 轨道号，图名只有歌名）→
    /// ③ 同目录固定名（cover / folder / front / album…）→ ④ 上一级目录的固定名。
    /// <para>🔴 只读、⛔ 联网、⛔ 写回文件 —— 在线抓封面归媒体资产的标签服务。</para>
    /// </summary>
    private byte[]? CoverFromDisk(string audioPath)
    {
        var folder = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrEmpty(folder)) return null;

        // ② 同名图
        var baseName = Path.GetFileNameWithoutExtension(audioPath);
        foreach (var ext in CoverExtensions)
        {
            var same = Path.Combine(folder, baseName + ext);
            if (File.Exists(same)) return TryReadBytes(same);
        }

        // ②b 宽松同名：图名与歌名互相包含即算（取字符覆盖最多的那张）
        var loose = FindLooseCover(folder, baseName);
        if (loose is not null) return TryReadBytes(loose);

        // ③ 同目录固定名 → ④ 上一级目录（专辑分碟 CD1/ 场景）
        var image = FindCoverImage(folder) ?? FindCoverImage(Path.GetDirectoryName(folder));
        return image is null ? null : TryReadBytes(image);
    }

    /// <summary>
    /// 宽松同名匹配：把文件名切成词（去括号内容 / 轨道号 / 分隔符 → 小写），
    /// 图名词集合被歌名词集合包含（或反之）即命中，覆盖率 = 较短一侧的字符数。
    /// <para>例：<c>周杰伦 - 稻香.mp3</c> ⇄ <c>稻香.jpg</c>、<c>01 稻香.flac</c> ⇄ <c>稻香 (Live).jpg</c>。</para>
    /// <para>🔴 至少 2 个字符才认，否则 <c>a.jpg</c> 这种会乱配。</para>
    /// </summary>
    private string? FindLooseCover(string folder, string baseName)
    {
        var key = folder + "|" + baseName;
        lock (_coverGate)
        {
            if (_looseCoverCache.TryGetValue(key, out var cached)) return cached;
        }

        string? best = null;
        var bestScore = 0;
        var song = NameTokens(baseName);

        if (song.Length > 0)
        {
            foreach (var image in EnumerateImages(folder))
            {
                var score = LooseScore(song, NameTokens(Path.GetFileNameWithoutExtension(image)));
                if (score <= bestScore) continue;
                bestScore = score;
                best = image;
            }
        }

        lock (_coverGate)
        {
            if (_looseCoverCache.Count > 512) _looseCoverCache.Clear();
            _looseCoverCache[key] = best;
        }
        return best;
    }

    private List<string> EnumerateImages(string folder)
    {
        lock (_coverGate)
        {
            if (_imageCache.TryGetValue(folder, out var cached)) return cached;
        }

        var list = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (CoverExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    list.Add(file);
            }
        }
        catch { /* 目录不可读当作没有图 */ }

        lock (_coverGate)
        {
            if (_imageCache.Count > 64) _imageCache.Clear();
            _imageCache[folder] = list;
        }
        return list;
    }

    /// <summary>把文件名切成可比较的词：去括号内容、去轨道号、分隔符统一成空格、小写。</summary>
    private static string[] NameTokens(string name)
    {
        var text = BracketContent.Replace(name.ToLowerInvariant(), " ");
        text = TrackNumberPrefix.Replace(text, " ");

        foreach (var sep in NameSeparators)
            text = text.Replace(sep, " ");

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>互相包含则返回覆盖率（较短一侧字符数），否则 0。</summary>
    private static int LooseScore(string[] song, string[] cover)
    {
        if (song.Length == 0 || cover.Length == 0) return 0;
        if (!ContainsAll(song, cover) && !ContainsAll(cover, song)) return 0;

        var score = Math.Min(song.Sum(t => t.Length), cover.Sum(t => t.Length));
        if (score < 2) return 0;

        // 歌名最后一个词通常就是标题：命中它压过只命中歌手的图（⛔ 把「歌手.jpg」当封面）
        if (cover.Any(t => string.Equals(t, song[^1], StringComparison.Ordinal))) score += 100;
        return score;
    }

    private static bool ContainsAll(string[] outer, string[] inner)
        => inner.All(t => outer.Any(o => o.Contains(t, StringComparison.Ordinal)));

    private string? FindCoverImage(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        lock (_coverGate)
        {
            if (_coverCache.TryGetValue(folder, out var cached)) return cached;
        }

        string? found = null;
        foreach (var name in CoverNames)
        {
            foreach (var ext in CoverExtensions)
            {
                var path = Path.Combine(folder, name + ext);
                if (!File.Exists(path)) continue;
                found = path;
                break;
            }
            if (found is not null) break;
        }

        lock (_coverGate)
        {
            if (_coverCache.Count > 256) _coverCache.Clear();
            _coverCache[folder] = found;
        }
        return found;
    }

    private static byte[]? TryReadBytes(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    private static readonly string[] CoverNames = { "cover", "folder", "front", "album", "albumart", "albumartsmall" };

    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    /// <summary>括号及其内容：<c>稻香 (Live)</c> ⇒ <c>稻香</c>。</summary>
    private static readonly Regex BracketContent = new(@"[\(（\[【][^\)）\]】]*[\)）\]】]", RegexOptions.Compiled);

    /// <summary>轨道号前缀：<c>01 - </c> / <c>01.</c> / <c>01_</c>。</summary>
    private static readonly Regex TrackNumberPrefix = new(@"^\s*\d{1,3}\s*[-._]+\s*", RegexOptions.Compiled);

    private static readonly string[] NameSeparators = { " - ", "-", "—", "_", "&", "、", ",", ".", "　" };

    private readonly Dictionary<string, string?> _coverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _looseCoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _coverGate = new();

    /// <summary>
    /// 读歌词，返回「文本 + 是否带时间轴」：① 同名 <c>.lrc</c> → ② 内嵌标签（无时间轴）→
    /// ③「标题 - 艺术家.lrc」/「艺术家 - 标题.lrc」→ 都没有返回 <c>(null, true)</c>。
    /// </summary>
    public (string? Text, bool Timed) ReadLyrics(string audioPath)
    {
        var lrc = ReadTextSmart(Path.ChangeExtension(audioPath, ".lrc"));
        if (lrc is not null) return (lrc, true);

        TagLib.File? file = null;
        try
        {
            file = TagLib.File.Create(audioPath);

            if (!string.IsNullOrWhiteSpace(file.Tag.Lyrics)) return (RepairMojibake(file.Tag.Lyrics), false);

            var title = RepairMojibake(file.Tag.Title);
            if (string.IsNullOrWhiteSpace(title)) return (null, true);

            var folder = Path.GetDirectoryName(audioPath) ?? "";
            var artist = RepairMojibake(file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist);

            var candidates = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(artist))
            {
                candidates.Add(Path.Combine(folder, title + " - " + artist + ".lrc"));
                candidates.Add(Path.Combine(folder, artist + " - " + title + ".lrc"));
            }
            candidates.Add(Path.Combine(folder, title + ".lrc"));

            foreach (var candidate in candidates)
            {
                var found = ReadTextSmart(candidate);
                if (found is not null) return (found, true);
            }
        }
        catch { /* 读不到标签不算失败，当没有歌词 */ }
        finally { try { file?.Dispose(); } catch { } }

        return (null, true);
    }

    /// <summary>
    /// 读文本文件并做编码回退：BOM → UTF-8（严格）→ GB18030 → Latin1。
    /// <para>🔴 中文老歌的 <c>.lrc</c> 绝大多数是 GBK：按 UTF-8 直读会整片乱码，这步不能省。</para>
    /// </summary>
    public static string? ReadTextSmart(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return "";

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { /* 不是合法 UTF-8 ⇒ 当中文 GBK 再试 */ }

            try { return Gb18030.GetString(bytes); }
            catch { return Encoding.Latin1.GetString(bytes); }
        }
        catch { return null; }
    }

    private static readonly Encoding Gb18030 = CreateGb18030();

    private static Encoding CreateGb18030()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030");
        }
        catch { return Encoding.UTF8; }
    }

    /// <summary>还原「帧头标 Latin1、实际是 GBK 字节」的中文标签文本；其它文本原样返回。</summary>
    public static string RepairMojibake(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var bytes = new byte[text.Length];
        var hasHighByte = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c > 0xFF) return text!;
            bytes[i] = (byte)c;
            if (c >= 0x80) hasHighByte = true;
        }
        if (!hasHighByte) return text!;

        try
        {
            var repaired = Gb18030.GetString(bytes);
            var hasEastAsian = false;
            foreach (var c in repaired)
            {
                if (c == '\uFFFD'
                    || (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
                    || (c >= 0x7F && c < 0xA0)) return text!;
                if (c >= '\u2E80' && c <= '\u9FFF') hasEastAsian = true;
            }
            return hasEastAsian ? repaired : text!;
        }
        catch { return text!; }
    }

    /// <summary>
    /// 读同名**译文**歌词：优先 <c>歌曲.zh.lrc</c>，其次 <c>歌曲.trans.lrc</c>、<c>歌曲.translation.lrc</c>。
    /// <para>译文按时间戳与原文对齐（由播放器合并），没有就返回 <c>null</c> —— 单语歌词照常显示。</para>
    /// </summary>
    public string? ReadTranslation(string audioPath)
    {
        var baseName = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? "",
            Path.GetFileNameWithoutExtension(audioPath));

        foreach (var suffix in TranslationSuffixes)
        {
            var path = baseName + suffix;
            if (!File.Exists(path)) continue;

            var text = ReadTextSmart(path);
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return null;
    }

    private static readonly string[] TranslationSuffixes = { ".zh.lrc", ".trans.lrc", ".translation.lrc", ".cn.lrc" };

    /// <summary>播放列表扩展名（m3u / m3u8）。</summary>
    public static bool IsPlaylist(string path)
    {
        var ext = Path.GetExtension(path ?? "");
        return ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 m3u / m3u8：返回其中**磁盘上确实存在**的音频路径（顺序 = 文件里的顺序）。
    /// <para>相对路径按播放列表所在目录解析；<c>#EXTINF</c> / 注释行跳过；去重。</para>
    /// </summary>
    public IReadOnlyList<string> ReadPlaylist(string playlistPath)
    {
        var result = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(playlistPath)) ?? "";
            foreach (var raw in File.ReadAllLines(playlistPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                var path = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(dir, line));
                if (!File.Exists(path)) continue;
                if (!ViewerHost.IsAudio(path)) continue;
                if (result.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(path);
            }
        }
        catch { /* 坏列表当空处理，界面提示「列表里没有可播的音频」 */ }
        return result;
    }
}
