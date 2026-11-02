using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using NewToys.Shared.Services;
using NewToys.UI.Readers;

namespace NewToys.UI.Viewers;

/// <summary>渲染器类型。</summary>
public enum ViewerKind
{
    /// <summary>还没打开 / 不支持的类型。</summary>
    None = 0,
    /// <summary>短文本 / 源码 / HTML（HTML 只取纯文本）。</summary>
    Text = 1,
    /// <summary>位图。</summary>
    Image = 2,
    /// <summary>音频（本地音乐播放器）。</summary>
    Audio = 3,
    /// <summary>长文本阅读器（章节 / 目录 / 书签）。</summary>
    Reader = 4,
}

/// <summary>阅读位置（章 + 章内比例），宿主用来存进度。</summary>
public readonly record struct ViewerReaderState(int ChapterIndex, double Ratio);

/// <summary>
/// 查看器外壳：按扩展名选渲染器并装载，记录阅读进度。
/// <para>
/// 🔴 消费方只需 <see cref="Open"/> / <see cref="OpenReader"/> / <see cref="ReadProgress"/>；媒体资产 / 视频工具将来要
/// 「用查看器打开」，直接 new 本控件放进自己的面板即可，⛔ 不必依赖 <c>Module.Viewer</c>。
/// </para>
/// <para>
/// ⛔ 本类不写文件、不碰配置 —— 最近打开 / 位置记忆 / 压缩包遍历由宿主（<c>Module.Viewer</c>）负责。
/// </para>
/// </summary>
public partial class ViewerHost : UserControl
{
    private static readonly string[] TextExtensions =
    {
        ".json", ".xml", ".csv", ".ini", ".cfg", ".conf",
        ".cs", ".py", ".ps1", ".js", ".ts", ".sql", ".bat", ".cmd", ".yaml", ".yml", ".toml",
        ".html", ".htm",
    };

    /// <summary>走阅读器壳的长文本（章节 / 目录 / 书签）。</summary>
    private static readonly string[] ReaderExtensions =
    {
        ".txt", ".md", ".markdown", ".log",
    };

    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico",
    };

    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".opus", ".aiff",
    };

    private Control? _current;

    public ViewerHost()
    {
        InitializeComponent();
    }

    /// <summary>当前渲染器类型。</summary>
    public ViewerKind Kind { get; private set; }

    /// <summary>当前打开的文件（空 = 空态）；压缩包内图片显示为 <c>包路径!条目名</c>。</summary>
    public string CurrentPath { get; private set; } = "";

    /// <summary>短文本阅读进度（0~1）；其它类型恒 0。</summary>
    public double ReadProgress => _current is TextViewer t ? t.ReadProgress : 0;

    /// <summary>长文本阅读位置（没有则 null）。</summary>
    public ViewerReaderState? ReaderState =>
        _current is ReaderShell r ? new ViewerReaderState(r.Position.ChapterIndex, r.Position.Ratio) : null;

    /// <summary>当前阅读器（宿主接事件用）。</summary>
    public ReaderShell? Reader => _current as ReaderShell;

    /// <summary>当前音频控件（宿主接事件用）。</summary>
    public AudioPlayer? Audio => _current as AudioPlayer;

    /// <summary>图片缩放变化（宿主刷新状态栏用）。</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>打开成功（宿主据此记「最近打开」）。</summary>
    public event EventHandler<string>? Opened;

    /// <summary>按扩展名判定类型（不做 IO）。</summary>
    public static ViewerKind KindOf(string path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        if (Array.IndexOf(ImageExtensions, ext) >= 0) return ViewerKind.Image;
        if (Array.IndexOf(AudioExtensions, ext) >= 0) return ViewerKind.Audio;
        if (Array.IndexOf(ReaderExtensions, ext) >= 0) return ViewerKind.Reader;
        if (Array.IndexOf(TextExtensions, ext) >= 0) return ViewerKind.Text;
        return ViewerKind.None;
    }

    /// <summary>是否音频扩展名（宿主拖入多文件时筛选用）。</summary>
    public static bool IsAudio(string path) => KindOf(path) == ViewerKind.Audio;

    /// <summary>是否图片扩展名。</summary>
    public static bool IsImage(string path) => KindOf(path) == ViewerKind.Image;

    /// <summary>压缩包扩展名（图片浏览入口）。</summary>
    public static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext is ".zip" or ".rar" or ".7z" or ".cbz" or ".cbr";
    }

    /// <summary>
    /// 打开文件（短文本 / 图片 / 音频）。长文本请走 <see cref="OpenReader"/>。
    /// <para><paramref name="progress"/> = 上次读到哪（0~1）；<paramref name="audioBackend"/> 为音频播放后端工厂。</para>
    /// </summary>
    public bool Open(string path, double progress, out string error, Func<IAudioPlayback?>? audioBackend = null)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path)) { error = "路径为空。"; return false; }
        if (!File.Exists(path)) { error = "文件不存在：" + path; return false; }

        var kind = KindOf(path);
        if (kind == ViewerKind.None)
        {
            error = "不支持的类型：" + Path.GetExtension(path);
            return false;
        }

        // 🔴 同类复用一个 ImageViewer：目录里翻图时不重建控件 ⇒ 缩放连续、键盘焦点不丢
        //    （重建会让焦点回到窗口，方向键失效，必须再点一下图片才恢复）。
        if (kind == ViewerKind.Image && Kind == ViewerKind.Image && _current is ImageViewer keep)
        {
            if (!keep.Load(path, out error)) return false;
            CurrentPath = path;
            Opened?.Invoke(this, path);
            return true;
        }

        Release();
        Kind = ViewerKind.None;

        switch (kind)
        {
            case ViewerKind.Text:
            {
                var tv = new TextViewer();
                tv.Load(path, progress);

                var ext = Path.GetExtension(path);
                var isHtml = ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                          || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
                if (isHtml) tv.SetText(StripHtml(tv.Text), progress);

                _current = tv;
                break;
            }

            case ViewerKind.Image:
            {
                var iv = new ImageViewer();
                if (!iv.Load(path, out error)) return false;
                iv.ZoomChanged += (_, _) => ZoomChanged?.Invoke(this, EventArgs.Empty);
                _current = iv;
                break;
            }

            case ViewerKind.Audio:
            {
                var player = new AudioPlayer { Playback = audioBackend?.Invoke() };
                _current = player;
                break;
            }

            case ViewerKind.Reader:
                error = "长文本请用阅读器打开（宿主判断扩展名后调用 OpenReader）。";
                return false;
        }

        Kind = kind;
        CurrentPath = path;
        ContentHost.Content = _current;
        EmptyPanel.IsVisible = false;
        Opened?.Invoke(this, path);
        return true;
    }

    /// <summary>打开长文本阅读器（文本 + 章节切片 + 续读位置）。</summary>
    public bool OpenReader(string path, string text, IReadOnlyList<ReaderChapter> chapters,
        ViewerReaderState state, out string error)
    {
        Release();

        var shell = new ReaderShell();
        if (!shell.Load(text, chapters, new ReaderPosition(state.ChapterIndex, state.Ratio), out error))
            return false;

        _current = shell;
        Kind = ViewerKind.Reader;
        CurrentPath = path;
        ContentHost.Content = shell;
        EmptyPanel.IsVisible = false;
        Opened?.Invoke(this, path);
        return true;
    }

    /// <summary>
    /// 显示一张「来自流」的图片（压缩包内图片）；<paramref name="display"/> 写进 <see cref="CurrentPath"/>。
    /// <para><paramref name="size"/> = 未压缩字节数，<paramref name="archivePath"/> = 所属压缩包（信息浮层用），都可省。</para>
    /// </summary>
    public bool OpenImageStream(Stream stream, string display, out string error, long size = -1, string archivePath = "")
    {
        if (_current is ImageViewer existing && Kind == ViewerKind.Image)
        {
            // 复用同一个 ImageViewer：压缩包里翻页时不必重建控件（缩放状态连续）
            if (!existing.Load(stream, out error)) return false;
            existing.SetArchiveSource(display, archivePath, size);
            CurrentPath = display;
            return true;
        }

        Release();
        var viewer = new ImageViewer();
        if (!viewer.Load(stream, out error)) return false;

        viewer.SetArchiveSource(display, archivePath, size);
        viewer.ZoomChanged += (_, _) => ZoomChanged?.Invoke(this, EventArgs.Empty);
        _current = viewer;
        Kind = ViewerKind.Image;
        CurrentPath = display;
        ContentHost.Content = viewer;
        EmptyPanel.IsVisible = false;
        return true;
    }

    /// <summary>回到空态。</summary>
    public void Close()
    {
        Release();
        CurrentPath = "";
        ContentHost.Content = null;
        EmptyPanel.IsVisible = true;
        EmptyDetail.Text = "";
    }

    /// <summary>空态下的补充说明（不支持的类型提示等）。</summary>
    public void SetHint(string detail) => EmptyDetail.Text = detail ?? "";

    /// <summary>短文本排版参数下发（字号 / 行距倍数 / 页边距）。</summary>
    public void ApplyTextOptions(double fontSize, double lineSpacing, double padding)
    {
        if (_current is TextViewer t) t.ApplyTypography(fontSize, lineSpacing, padding);
    }

    /// <summary>阅读器排版下发（长文本）。</summary>
    public void ApplyReaderTypography(ReaderTypography typography)
        => (_current as ReaderShell)?.ApplyTypography(typography);

    // ---------- 图片操作（图片类型下有效，其余忽略） ----------

    public void FitToWindow() => (_current as ImageViewer)?.FitToWindow();
    public void ResetZoom() => (_current as ImageViewer)?.ResetZoom();
    public void ZoomBy(double factor) => (_current as ImageViewer)?.ZoomBy(factor);
    public double Zoom => (_current as ImageViewer)?.Zoom ?? 0;

    /// <summary>顺时针旋转 90°（单图有效，分屏网格忽略）。</summary>
    public void Rotate() => (_current as ImageViewer)?.Rotate();

    /// <summary>图片信息浮层是否打开（宿主页据此屏蔽翻页 / 滚轮）。</summary>
    public bool IsInfoVisible => (_current as ImageViewer)?.IsInfoVisible == true;

    private void Release()
    {
        switch (_current)
        {
            case AudioPlayer audio:
                audio.Stop();
                break;
            case ImageViewer image:
                image.Close();
                break;
        }

        _current = null;
        Kind = ViewerKind.None;
    }

    /// <summary>HTML 去标签转纯文本：块级标签变换行、实体解码、脚本与样式整段丢弃。</summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = Regex.Replace(html, @"<(script|style|head)\b[^>]*>.*?</\1>",
            " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(br|/p|/div|/li|/tr|/h[1-6])\b[^>]*>", "\n",
            RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]{2,}", " ");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }
}
