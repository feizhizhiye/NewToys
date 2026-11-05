using System.Text.RegularExpressions;

namespace NewToys.MediaAssets.Services;

/// <summary>音乐外挂封面 / 歌词文件探测（只读，⛔ 不写文件）。</summary>
internal static class MusicFileProbe
{
    public static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    private static readonly string[] CoverNames = { "cover", "folder", "front", "album", "albumart", "albumartsmall" };
    private static readonly string[] TranslationSuffixes = { ".zh.lrc", ".trans.lrc", ".translation.lrc", ".cn.lrc" };

    /// <summary>找外挂封面文件，返回完整路径；没有返回 null。</summary>
    public static string? FindCover(string audioPath)
    {
        var folder = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        // ① 同名图
        var baseName = Path.GetFileNameWithoutExtension(audioPath);
        foreach (var ext in CoverExtensions)
        {
            var same = Path.Combine(folder, baseName + ext);
            if (File.Exists(same)) return same;
        }

        // ② 宽松同名 → ③ 固定名 → ④ 上一级目录固定名（专辑分碟 CD1/ 场景）
        return FindLooseCover(folder, baseName) ?? FindFixedName(folder) ?? FindFixedName(Path.GetDirectoryName(folder));
    }

    /// <summary>
    /// 找歌词文件，返回（主歌词完整路径，译文完整路径）。主歌词按同名 → 标题组合的顺序；
    /// 译文没有就返回 null，单语歌词照常显示。
    /// </summary>
    public static (string? Main, string? Translation) FindLyrics(string audioPath, string? title, string? artist)
    {
        var folder = Path.GetDirectoryName(audioPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(audioPath);

        var candidates = new List<string>(4) { Path.Combine(folder, baseName + ".lrc") };
        var t = title?.Trim();
        var a = artist?.Trim();
        if (!string.IsNullOrEmpty(t))
        {
            if (!string.IsNullOrEmpty(a))
            {
                candidates.Add(Path.Combine(folder, t + " - " + a + ".lrc"));
                candidates.Add(Path.Combine(folder, a + " - " + t + ".lrc"));
            }
            candidates.Add(Path.Combine(folder, t + ".lrc"));
        }

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var translation = FindTranslation(folder, baseName, t);
            return (candidate, translation);
        }
        return (null, null);
    }

    private static string? FindTranslation(string folder, string baseName, string? title)
    {
        foreach (var suffix in TranslationSuffixes)
        {
            var p = Path.Combine(folder, baseName + suffix);
            if (File.Exists(p)) return p;
            if (!string.IsNullOrEmpty(title))
            {
                p = Path.Combine(folder, title + suffix);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static string? FindFixedName(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        foreach (var name in CoverNames)
            foreach (var ext in CoverExtensions)
            {
                var path = Path.Combine(folder, name + ext);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    /// <summary>
    /// 宽松同名匹配：把文件名切成词（去括号内容 / 轨道号 / 分隔符 → 小写），
    /// 词集互相包含即命中，覆盖率 = 较短一侧的字符数。
    /// <para>🔴 至少 2 个字符才认，否则 <c>a.jpg</c> 这种会乱配。</para>
    /// </summary>
    private static string? FindLooseCover(string folder, string baseName)
    {
        var song = NameTokens(baseName);
        if (song.Length == 0) return null;

        string? best = null;
        var bestScore = 0;
        try
        {
            foreach (var image in Directory.EnumerateFiles(folder))
            {
                if (!CoverExtensions.Contains(Path.GetExtension(image), StringComparer.OrdinalIgnoreCase)) continue;
                var score = LooseScore(song, NameTokens(Path.GetFileNameWithoutExtension(image)));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = image;
                }
            }
        }
        catch { /* 目录不可读当作没有图 */ }
        return best;
    }

    private static string[] NameTokens(string name)
    {
        var text = TrackNumberPrefix.Replace(BracketContent.Replace(name, " "), "").ToLowerInvariant();
        foreach (var sep in NameSeparators) text = text.Replace(sep, " ");
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

    /// <summary>括号及其内容：<c>稻香 (Live)</c> ⇒ <c>稻香</c>。</summary>
    private static readonly Regex BracketContent = new(@"[\(（\[【][^\)）\]】]*[\)）\]】]", RegexOptions.Compiled);

    /// <summary>轨道号前缀：<c>01 - </c> / <c>01.</c> / <c>01_</c>。</summary>
    private static readonly Regex TrackNumberPrefix = new(@"^\s*\d{1,3}\s*[-._]+\s*", RegexOptions.Compiled);

    private static readonly string[] NameSeparators = { " - ", "-", "—", "_", "&", "、", ",", ".", "　" };
}
