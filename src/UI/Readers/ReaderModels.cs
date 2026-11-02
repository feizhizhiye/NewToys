using System;
using System.Collections.Generic;

namespace NewToys.UI.Readers;

/// <summary>文本阅读器的一章：整本书 <see cref="Text"/> 上的一段切片。</summary>
public sealed class ReaderChapter
{
    /// <summary>章节标题（目录里显示）。</summary>
    public string Title { get; init; } = "";

    /// <summary>在整本书字符串中的起始位置。</summary>
    public int Start { get; init; }

    /// <summary>章节长度（含标题行）。</summary>
    public int Length { get; init; }

    /// <summary>正文起始位置（跳过标题行之后）。</summary>
    public int BodyStart { get; init; }

    public override string ToString() => Title;
}

/// <summary>一条书签：指向「第几章 + 章内比例」，并带一段正文预览。</summary>
public sealed class ReaderBookmark
{
    /// <summary>书签标识（宿主生成，用于删除）。</summary>
    public string Id { get; init; } = "";

    /// <summary>章节标题（书签列表里显示）。</summary>
    public string ChapterTitle { get; init; } = "";

    /// <summary>章节序号（0 起）。</summary>
    public int ChapterIndex { get; init; }

    /// <summary>章内滚动比例 0~1。</summary>
    public double Ratio { get; init; }

    /// <summary>正文预览（列表第二行）。</summary>
    public string Preview { get; init; } = "";

    /// <summary>建立时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    public override string ToString() => ChapterTitle;
}

/// <summary>全书搜索命中：定位到章 + 章内字符偏移。</summary>
public sealed class ReaderSearchHit
{
    public int ChapterIndex { get; init; }
    public string ChapterTitle { get; init; } = "";

    /// <summary>在整本书字符串中的绝对位置。</summary>
    public int Position { get; init; }

    /// <summary>命中处的上下文片段（已截断）。</summary>
    public string Snippet { get; init; } = "";

    public override string ToString() => ChapterTitle + " · " + Snippet;
}

/// <summary>阅读位置（章节 + 章内比例）。</summary>
public readonly struct ReaderPosition
{
    public ReaderPosition(int chapterIndex, double ratio)
    {
        ChapterIndex = chapterIndex;
        Ratio = ratio;
    }

    public int ChapterIndex { get; }
    public double Ratio { get; }

    public static ReaderPosition Start => new(0, 0);
}

/// <summary>阅读排版参数（一次下发，避免控件里散着十几个 setter）。</summary>
public sealed class ReaderTypography
{
    public double FontSize { get; init; } = 16;
    public double LineSpacing { get; init; } = 1.7;
    public double Padding { get; init; } = 32;
    public double ParagraphSpacing { get; init; }
    public bool Indent { get; init; } = true;
    public string? FontFamily { get; init; }
    public string? Foreground { get; init; }
    public string? Background { get; init; }

    /// <summary>配色档序号（0 = 跟随主题，其余见控件内预设表）。</summary>
    public int Theme { get; init; }

    public static ReaderTypography Default => new();
}

/// <summary>阅读器对宿主公开的接口（宿主是 <c>Module.Viewer</c>，控件本身不碰文件与配置）。</summary>
public interface IReaderHost
{
    /// <summary>请求把当前阅读位置记下来。</summary>
    void SavePosition(ReaderPosition position);
}

/// <summary>章节列表（供目录列表绑定，避免每次重建包装对象）。</summary>
public sealed class ReaderChapterList : List<ReaderChapter>
{
    public ReaderChapterList() { }
    public ReaderChapterList(IEnumerable<ReaderChapter> items) : base(items) { }
}
