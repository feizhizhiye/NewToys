using System;
using System.Globalization;

namespace NewToys.UI.Viewers;

/// <summary>播放列表里的一首（标签由宿主读出来，控件只负责显示）。</summary>
public sealed class AudioTrackItem
{
    /// <summary>文件路径。</summary>
    public string Path { get; init; } = "";

    /// <summary>标题（读不到标签时用文件名）。</summary>
    public string Title { get; init; } = "";

    /// <summary>艺术家。</summary>
    public string Artist { get; init; } = "";

    /// <summary>专辑。</summary>
    public string Album { get; init; } = "";

    /// <summary>时长；未知为 0。</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>封面原始字节（可空）。</summary>
    public byte[]? Cover { get; init; }

    /// <summary>一行技术信息（例：<c>MP3 · 44.1 kHz · 320 kbps · 立体声</c>）；读不到时只有扩展名。</summary>
    public string TechText { get; init; } = "";

    /// <summary>列表里显示的一行摘要。</summary>
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : Title + " · " + Artist;

    /// <summary>时长文本（<c>mm:ss</c>，超 1 小时 <c>h:mm:ss</c>）；未知为空串（⛔ 显示 00:00）。</summary>
    public string DurationText => Duration <= TimeSpan.Zero
        ? ""
        : Duration.TotalHours >= 1
            ? Duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    public override string ToString() => Display;
}
