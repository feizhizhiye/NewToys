using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NewToys.Shell.Diagnostics;

/// <summary>
/// 🩺 一次性 UI 样式自检（临时诊断，定位「页面样式没生效」这类问题用）。
/// <para>
/// 背景：屏幕上出现过「内容区文字纯黑、按钮没有按钮样子、面板/分隔线全丢，而侧栏样式正常」的现象。
/// 这类问题只看截图无法区分三种成因：
/// ① <c>StyleInclude</c> 没加载；② 应用级样式根本没作用到页面控件；
/// ③ 模块程序集与 Shell 不是同一份 Avalonia（历史上的「两份副本」事故）。
/// </para>
/// <para>
/// 本类把这三条都写成一行行事实，落到 <c>%LocalAppData%\NewToys\logs\ui-diag.log</c>：
/// 启动时记一次全局状态，之后**每次切换内容区**记一次「这一页到底吃到了什么样式」。
/// 查询本身全部包在 try/catch 里 —— 诊断代码绝不能把主流程弄崩。
/// </para>
/// <para>
/// ⚠️ 定位完就可以整文件删除（调用点只有 <c>ShellView.axaml.cs</c> 的两处）。
/// </para>
/// </summary>
public static class UiStyleDiag
{
    private static string? _logPath;
    private static bool _startupDone;

    private static string LogPath
    {
        get
        {
            if (_logPath != null) return _logPath;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NewToys", "logs");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "ui-diag.log");
            return _logPath;
        }
    }

    private static void W(string line)
    {
        try
        {
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff  ") + line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* 诊断日志写不进去就算了，绝不干扰主流程 */ }
    }

    /// <summary>启动自检：应用级样式集合、资源键、同名程序集（两份副本）。只记一次。</summary>
    public static void Startup(Application? app)
    {
        if (_startupDone) return;
        _startupDone = true;
        if (app == null) { W("⚠ Application.Current 为 null —— 样式根本无从谈起"); return; }

        try
        {
            W("");
            W("================= 启动自检 =================");
            W($"Application        = {app.GetType().FullName}");
            W($"Styles 顶层条目数   = {app.Styles.Count()}");
            W($"  其中 Style        = {app.Styles.OfType<Style>().Count()}");
            var includes = app.Styles.OfType<StyleInclude>().ToList();
            W($"  其中 StyleInclude = {includes.Count}");
            foreach (var inc in includes)
                W($"    Source = {inc.Source}　Loaded = {(inc.Loaded != null ? "已加载" : "!! 未加载 !!")}");

            W($"Resources 合并字典  = {app.Resources.MergedDictionaries.Count}");
            W($"PrimaryTextBrush   = {app.FindResource("PrimaryTextBrush") ?? "!! 取不到 !!"}");
            W($"SecondaryTextBrush = {app.FindResource("SecondaryTextBrush") ?? "!! 取不到 !!"}");
            W($"PanelBrush         = {app.FindResource("PanelBrush") ?? "!! 取不到 !!"}");
            W($"Button 程序集       = {typeof(Button).Assembly.Location}");

            // 独立验证样式表本身能不能被加载出来（与 StyleInclude 是否生效相互印证）
            // ⚠️ AvaloniaXamlLoader.Load 没有单 Uri 重载，参数顺序是 (uri, baseUri)
            try
            {
                var controlsUri = new Uri("avares://NewToys/Styles/Controls.axaml");
                var sheet = AvaloniaXamlLoader.Load(controlsUri, new Uri("avares://NewToys/"));
                var n = sheet is Styles s ? s.OfType<Style>().Count() : -1;
                W($"直接加载 Controls.axaml = {sheet?.GetType().FullName ?? "null"}，含 Style {n} 条");
            }
            catch (Exception ex) { W($"直接加载 Controls.axaml 失败：{ex.GetType().Name} {ex.Message}"); }

            // 「两份副本」探测：同名程序集出现两次，控件就可能跟 Shell 不在同一个 Avalonia 里
            var dups = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .GroupBy(a => a.GetName().Name)
                .Where(g => g.Count() > 1)
                .ToList();
            if (dups.Count == 0) W("同名程序集重复     = 无");
            else foreach (var g in dups)
                    W($"⚠ 同名程序集 {g.Key} ×{g.Count()}：{string.Join("  |  ", g.Select(x => x.Location))}");
        }
        catch (Exception ex) { W("启动自检失败：" + ex); }
    }

    /// <summary>页面自检：延后一个后台优先级再采样（等这一页布局完成）。</summary>
    public static void ProbeContent(ContentControl? area, object? content)
        => Dispatcher.UIThread.Post(() => Probe(area, content), DispatcherPriority.Background);

    private static void Probe(ContentControl? area, object? content)
    {
        try
        {
            W("");
            W("-------------- 切换内容区自检 --------------");
            if (area == null) { W("ContentArea = null"); return; }
            if (content is not Control c)
            {
                W($"内容 = {content?.GetType().FullName ?? "null"}（不是 Control）");
                return;
            }

            var baseAsm = c.GetType().BaseType?.Assembly;
            W($"页面类型            = {c.GetType().FullName}");
            W($"页面程序集          = {c.GetType().Assembly.Location}");
            W($"基类程序集          = {baseAsm?.Location}");
            W($"基类 == Shell 的 Avalonia = {ReferenceEquals(baseAsm, typeof(Button).Assembly)}");
            W($"IsEnabled           = 内容区={area.IsEnabled}  页面={c.IsEnabled}");
            W($"页面能取到 PrimaryTextBrush = {c.FindResource("PrimaryTextBrush") ?? "!! 取不到 !!（资源链也断了）"}");

            // 最能说明问题的一条：直接读被样式控制的属性，跟期望值对比
            var tb = ((Visual)c).GetVisualDescendants()
                                .OfType<TextBlock>()
                                .FirstOrDefault(x => x.Classes.Contains("hint-text"));
            if (tb == null)
                W("页面上暂时没有 Classes=hint-text 的 TextBlock（可能还没布局完 / 这一页没有提示文字）");
            else
                W($"hint-text 的 Foreground 实际 = {tb.Foreground}　期望 = {area.FindResource("SecondaryTextBrush")}"
                  + (Equals(tb.Foreground, area.FindResource("SecondaryTextBrush")) ? "　✅ 样式生效" : "　❌ 样式没生效"));
        }
        catch (Exception ex) { W("页面自检失败：" + ex); }
    }
}
