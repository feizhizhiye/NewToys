using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Controls;

namespace NewToys.UI.Viewers;

/// <summary>渲染器类型。</summary>
public enum ViewerKind
{
    /// <summary>还没打开 / 不支持的类型。</summary>
    None = 0,
    /// <summary>文本 / 源码 / HTML（HTML 只取纯文本）。</summary>
    Text = 1,
    /// <summary>位图。</summary>
    Image = 2,
}

/// <summary>
/// 查看器外壳：按扩展名选渲染器并装载，记录阅读进度。
/// <para>
/// 🔴 消费方只需 <see cref="Open"/> + <see cref="ReadProgress"/>；媒体资产 / 视频工具将来要「用查看器打开」，
/// 直接 new 本控件放进自己的面板即可，⛔ 不必依赖 <c>Module.Viewer</c>。
/// </para>
/// <para>
/// ⛔ 本类不写文件、不碰配置 —— 最近打开 / 位置记忆由宿主（<c>Module.Viewer</c>）负责。
/// </para>
/// </summary>
public partial class ViewerHost : UserControl
{
    private static readonly string[] TextExtensions =
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf",
        ".cs", ".py", ".ps1", ".js", ".ts", ".sql", ".bat", ".cmd", ".yaml", ".yml", ".toml",
        ".html", ".htm",
    };

    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico",
    };

    private Control? _current;

    public ViewerHost()
    {
        InitializeComponent();
    }

    /// <summary>当前渲染器类型。</summary>
    public ViewerKind Kind { get; private set; }

    /// <summary>当前打开的文件（空 = 空态）。</summary>
    public string CurrentPath { get; private set; } = "";

    /// <summary>文本阅读进度（0~1）；图片恒 0。</summary>
    public double ReadProgress => _current is TextViewer t ? t.ReadProgress : 0;

    /// <summary>图片缩放变化（宿主刷新状态栏用）。</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>打开成功（宿主据此记「最近打开」）。</summary>
    public event EventHandler<string>? Opened;

    /// <summary>按扩展名判定类型（不做 IO）。</summary>
    public static ViewerKind KindOf(string path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        if (Array.IndexOf(ImageExtensions, ext) >= 0) return ViewerKind.Image;
        if (Array.IndexOf(TextExtensions, ext) >= 0) return ViewerKind.Text;
        return ViewerKind.None;
    }

    /// <summary>
    /// 打开文件。<paramref name="progress"/> = 上次读到哪（0~1）。
    /// 返回 false 时 <paramref name="error"/> 给出原因，界面保持空态（⛔ 不静默失败）。
    /// </summary>
    public bool Open(string path, double progress, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path)) { error = "路径为空。"; return false; }
        if (!File.Exists(path)) { error = "文件不存在：" + path; return false; }

        var kind = KindOf(path);
        if (kind == ViewerKind.None)
        {
            error = "暂不支持的类型（V1 只认文本与图片）：" + Path.GetExtension(path);
            return false;
        }

        _current = null;
        Kind = ViewerKind.None;

        if (kind == ViewerKind.Text)
        {
            var tv = new TextViewer();
            tv.Load(path, progress);

            var ext = Path.GetExtension(path);
            var isHtml = ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                      || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
            if (isHtml) tv.SetText(StripHtml(tv.Text), progress);

            _current = tv;
        }
        else
        {
            var iv = new ImageViewer();
            if (!iv.Load(path, out error)) return false;
            iv.ZoomChanged += (_, _) => ZoomChanged?.Invoke(this, EventArgs.Empty);
            _current = iv;
        }

        Kind = kind;
        CurrentPath = path;
        ContentHost.Content = _current;
        EmptyPanel.IsVisible = false;
        Opened?.Invoke(this, path);
        return true;
    }

    /// <summary>回到空态。</summary>
    public void Close()
    {
        _current = null;
        Kind = ViewerKind.None;
        CurrentPath = "";
        ContentHost.Content = null;
        EmptyPanel.IsVisible = true;
        EmptyDetail.Text = "";
    }

    /// <summary>空态下的补充说明（不支持的类型提示等）。</summary>
    public void SetHint(string detail) => EmptyDetail.Text = detail ?? "";

    /// <summary>文本排版参数下发（字号 / 行距倍数 / 页边距）。</summary>
    public void ApplyTextOptions(double fontSize, double lineSpacing, double padding)
    {
        if (_current is TextViewer t) t.ApplyTypography(fontSize, lineSpacing, padding);
    }

    // ---------- 图片操作（图片类型下有效，其余忽略） ----------

    public void FitToWindow() => (_current as ImageViewer)?.FitToWindow();
    public void ResetZoom() => (_current as ImageViewer)?.ResetZoom();
    public void ZoomBy(double factor) => (_current as ImageViewer)?.ZoomBy(factor);
    public double Zoom => (_current as ImageViewer)?.Zoom ?? 0;

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
