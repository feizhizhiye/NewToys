using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;

namespace NewToys.Viewer.Controls;

/// <summary>文本渲染器：整篇文本 + 垂直滚动，记录阅读进度（0~1）。（WHY: VW-008）</summary>
public partial class TextViewer : UserControl
{
    private static bool _encodingReady;

    public TextViewer()
    {
        InitializeComponent();
        // 🔴 用属性变更而不是 ScrollChanged 事件（后者在项目里踩过不触发 / 时序不稳的坑）
        Scroller.PropertyChanged += OnScrollerPropertyChanged;
    }

    /// <summary>阅读进度（0~1），跨会话由宿主保存 / 恢复。</summary>
    public double ReadProgress { get; private set; }

    /// <summary>正文（供查找 / 复制用）。</summary>
    public string Text => Body.Text ?? "";

    /// <summary>载入文件并从 <paramref name="progress"/>（0~1）位置续读。</summary>
    public void Load(string path, double progress)
    {
        string text;
        try
        {
            text = ReadText(path);
        }
        catch (Exception ex)
        {
            text = "读取失败：" + ex.Message;
            progress = 0;
        }

        SetText(text, progress);
    }

    /// <summary>直接换正文（HTML 转纯文本后也走这里），并从 <paramref name="progress"/> 续读。</summary>
    public void SetText(string text, double progress)
    {
        Body.Text = text ?? "";
        ReadProgress = Math.Clamp(progress, 0, 1);
        // 布局完成后再滚：此刻 Extent 还是 0，直接设 Offset 会被吞掉
        Avalonia.Threading.Dispatcher.UIThread.Post(RestoreOffset, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>字号 / 行距倍数 / 页边距（由宿主的设置下发）。</summary>
    public void ApplyTypography(double fontSize, double lineSpacing, double padding)
    {
        Body.FontSize = fontSize <= 0 ? 16 : fontSize;
        Body.LineHeight = Math.Round(Body.FontSize * (lineSpacing <= 0 ? 1.7 : lineSpacing));
        var pad = padding < 0 ? 32 : padding;
        Pad.Padding = new Avalonia.Thickness(pad, 24);
    }

    /// <summary>
    /// 字体与前景色（阅读器用自己的配色档时下发；<c>null</c> / 空白 ⇒ 保持父级继承）。
    /// <para>背景色由宿主的容器给（本控件保持透明），⛔ 不在这里另存一份主题。</para>
    /// </summary>
    public void ApplyAppearance(string? fontFamily, string? foreground)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            try { Body.FontFamily = new FontFamily(fontFamily.Trim()); }
            catch { /* 字体名无效 ⇒ 用系统默认 */ }
        }

        if (string.IsNullOrWhiteSpace(foreground)) return;

        try { Body.Foreground = new SolidColorBrush(Color.Parse(foreground.Trim())); }
        catch { /* 色值写错 ⇒ 保持主题色 */ }
    }

    /// <summary>按比例滚到某处（进度条 / 记忆位置调用）。</summary>
    public void ScrollToProgress(double progress)
    {
        var max = Scroller.Extent.Height - Scroller.Viewport.Height;
        if (max <= 0) return;
        Scroller.Offset = new Avalonia.Vector(Scroller.Offset.X, Math.Clamp(progress, 0, 1) * max);
    }

    private void RestoreOffset()
    {
        if (ReadProgress > 0) ScrollToProgress(ReadProgress);
    }

    private void OnScrollerPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty) return;
        var max = Scroller.Extent.Height - Scroller.Viewport.Height;
        if (max <= 0) return;
        ReadProgress = Math.Clamp(Scroller.Offset.Y / max, 0, 1);
    }

    /// <summary>BOM → 严格 UTF-8 → GB18030。</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            if (!_encodingReady)
            {
                // GBK / GB18030 需注册代码页编码器（net10 共享框架自带，⛔ 不引第三方包）
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _encodingReady = true; } catch { }
            }
            try { return Encoding.GetEncoding("gb18030").GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }
}
