using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Services;
using NewToys.Shell.ViewModels;
using NewToys.Shell.Views;
using NewToys.UI.Controls;

namespace NewToys.Shell;

public partial class App : Application
{
    // 🔴 色值唯一来源：颜色只在这里出现一次，其余 XAML / C# 一律走 *Brush 语义键。
    // 区域档位只看 `RegionLayers`；Bg2 起是控件层（输入框 / 按钮面 / 浮层），不参与区域分档。
    // ⚠️ 区域不靠多档灰划分（色差小就糊、大就像贴纸）；档位口径见 ui.md §3。
    // Ink0 主文字 → Ink1 次文字 → Ink2 三级（水印）。
    private const string DarkBg0 = "#161616";
    private const string DarkBg1 = "#1E1E1E";
    private const string DarkBg2 = "#2A2A2A";
    private const string DarkBg3 = "#313131";
    private const string DarkBg4 = "#3C3C3C";
    private const string DarkBorderStrong = "#4A4A4A";
    private const string DarkInput = "#2A2A2A";
    private const string DarkInk0 = "#E5E5E5";
    private const string DarkInk1 = "#9E9E9E";
    private const string DarkInk2 = "#6B6B6B";
    private const string DarkDanger = "#F85149";

    // 亮色 = 「白 chrome + 浅灰卡」：卡外（标题栏 / 左菜单 / 版本行 / 状态栏）纯白，
    // 卡内（主显示区 / 详情列及其工具条·表头·底栏）一档浅灰 ⇒ 卡靠底色浮在白底上。
    // ⚠️ 档位是 `layers = { bg0, bg1, bg2, bg3, bg4 }`，**1 档 = bg0、2 档 = bg1**，别按序号错位。
    private const string LightBg0 = "#FFFFFF";
    private const string LightBg1 = "#E9EBEF";
    // 🔴 亮色档差要比暗色**大得多**才「看起来一样」：人眼在亮区对微小亮度差不敏感（暗色 8~12 灰阶
    // 就是 36~40% 相对差，亮色同样 8~12 只有 4~5%）。故卡内外 22、行区与卡面 18 —— 约等于暗色
    // 「22 ⇄ 30 ⇄ 42」在感知上的对应值。⛔ 别按暗色的绝对灰阶照抄。
    private const string LightBg2 = "#D7DAE0";
    private const string LightBg3 = "#C9CDD4";
    private const string LightBg4 = "#BBC0C8";
    private const string LightBorderStrong = "#C8CCD2";
    // 控件描边（按钮 / 输入框共用）：浅色下 ⛔ 不用纯白 —— 白色描边压在 #E2E4E7 面板上是「高光」方向，
    // 比底色更抢眼（看着就是一圈白框）；取比面板深一档的淡灰才是「淡而可见」。
    private const string LightInput = "#D5D8DD";
    private const string LightInk0 = "#1F1F1F";
    // 浅色下二级 / 三级文字压深一档：#8A8A8F 在白底上只有 3.5:1，12~13px 中文整片发虚
    private const string LightInk1 = "#5F5F66";
    private const string LightInk2 = "#8E8E96";
    private const string LightDanger = "#D93026";

    /// <summary>标题栏关闭按钮悬停红（Windows 通行的窗口语义色，不随主题变）。</summary>
    private const string BrandCloseHover = "#E81123";

    /// <summary>区域 → 色档（1 = 卡外平铺底，2 = 卡内卡片面）。颜色分配的单一事实源。（WHY: SHL-002）</summary>
    internal static readonly (string Key, int Layer)[] RegionLayers =
    {
        ("WindowBackdropBrush", 1),
        ("TitleBarBrush", 1),
        ("SiderBrush", 1),
        ("SiderFootBrush", 1),
        ("ToolbarBrush", 2),
        ("ListHeaderBrush", 2),
        // 行区独立一档（2026-11-04 实机）：行区与表头 / 工具条同为 2 档时，亮色下整片浅灰糊在一起
        // （暗色靠大面积深灰尚能分辨，亮色直接看不出表格边界）。⛔ 再并回 2 档。
        ("ListBodyBrush", 3),
        ("ContentBrush", 2),
        ("ContentBareBrush", 2),
        ("FooterBarBrush", 2),
        ("StatusBarBrush", 1),
        ("DetailCardBrush", 2),
    };

    /// <summary>直接相接的区域对 —— 两端必须不同档。lint.py --only regions 逐对核对。（WHY: SHL-003）</summary>
    internal static readonly (string A, string B)[] RegionAdjacency =
    {
        ("TitleBarBrush", "SiderBrush"),            // 都是 chrome 平铺面（允许同档）
        ("TitleBarBrush", "ToolbarBrush"),
        ("TitleBarBrush", "ContentBareBrush"),      // 无工具条的页面：标题栏直接压内容（允许同档）
        ("TitleBarBrush", "ListHeaderBrush"),       // 无工具条但有表头
        ("SiderBrush", "SiderFootBrush"),           // 版本行是侧栏的底边（允许同档）
        ("SiderBrush", "ContentBrush"),
        ("SiderBrush", "ListBodyBrush"),            // 列表铺满内容区：左树直接挨着行区
        ("SiderBrush", "ListHeaderBrush"),          // 表头右端接左树
        ("SiderFootBrush", "StatusBarBrush"),       // 版本行的右端接状态栏（同一条底边，允许同档）
        ("SiderFootBrush", "FooterBarBrush"),       // 版本行的右端接底栏
        ("ToolbarBrush", "ListHeaderBrush"),
        ("ToolbarBrush", "ListBodyBrush"),          // 无表头的列表页
        ("ToolbarBrush", "ContentBrush"),           // 无表头的页面
        ("ListHeaderBrush", "ListBodyBrush"),       // 表头压在行区上（最容易撞的一对）
        ("ListHeaderBrush", "ContentBrush"),
        ("ListHeaderBrush", "StatusBarBrush"),      // 自定义列表页：表头下方直接状态栏
        ("ListBodyBrush", "FooterBarBrush"),
        ("ListBodyBrush", "StatusBarBrush"),        // 无底栏的列表页
        ("ListBodyBrush", "DetailCardBrush"),       // 行区 ⇄ 右侧详情列（行区独立 3 档，故这条有真实色差）
        ("ContentBrush", "FooterBarBrush"),
        ("ContentBrush", "StatusBarBrush"),         // 无底栏的页面
        ("ContentBrush", "DetailCardBrush"),        // 右侧详情列 / 页内详情卡（卡内同面）
        ("ContentBareBrush", "StatusBarBrush"),     // 无工具条且无底栏的页面
        ("ContentBareBrush", "DetailCardBrush"),    // 无工具条页的右侧详情列（卡内同面）
        ("FooterBarBrush", "StatusBarBrush"),
        ("ToolbarBrush", "DetailCardBrush"),        // 工具条右端接详情列
        ("ListHeaderBrush", "DetailCardBrush"),     // 表头右端接详情列
        ("FooterBarBrush", "DetailCardBrush"),      // 底栏右端接详情列
        ("DetailCardBrush", "StatusBarBrush"),
        ("WindowBackdropBrush", "ContentBrush"),    // 窗口底 ⇄ 主卡：卡四周的留白露出窗口底
    };
    /// <summary>压在彩色块 / 图片上的次要文字（半透明白）。</summary>
    private const byte OnColorSecondaryAlpha = 0xB0;

    /// <summary>加载遮罩：内容底色 + 50% 透明度。</summary>
    private const byte OverlayAlpha = 0x80;

    /// <summary>把「跟随系统」所需的系统明暗状态喂给 IThemeService。（WHY: SHL-004）</summary>
    private void WatchSystemTheme(IThemeService theme)
    {
        try
        {
            var settings = PlatformSettings;
            if (settings == null) return;

            theme.SystemDark = settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
            settings.ColorValuesChanged += (_, e) =>
            {
                var dark = e.ThemeVariant == PlatformThemeVariant.Dark;
                if (Dispatcher.UIThread.CheckAccess()) theme.SystemDark = dark;
                else Dispatcher.UIThread.Post(() => theme.SystemDark = dark);
            };
        }
        catch { /* 平台无此能力（或早期版本 API 差异）时忽略 */ }
    }

    /// <summary>按当前主题状态重建资源字典。幂等：随时可重复调用。</summary>
    private void ApplyTheme(IThemeService theme)
    {
        var dark = theme.IsDark;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = AccentPalette.Find(theme.AccentId);
        var d = new ResourceDictionary();

        // 🔴 先把强调色喂给 Fluent 主题本体：单选圆点 / 复选勾 / 开关滑块 / 列表选中条 / 滑块 / 进度条
        //    这些模板内部的强调色只认 ColorPaletteResources.Accent，不认我们自己的 AccentBrush。
        //    缺这一步 ⇒ 设置页里「自制键是微信绿、Fluent 控件是蓝」= 两套强调色并存。
        ApplyFluentAccent(accent);

        if (theme.Skin == ThemeSkin.Fluent) FillFluent(d, dark, accent);
        else FillNewToys(d, dark, accent);

        var (card, control, small) = Radii(theme.Skin);
        d["RadiusCard"] = card;
        d["RadiusControl"] = control;
        d["RadiusSmall"] = small;

        if (Resources != null)
        {
            // 只移除上一次注入的 NewToys 主题字典，避免 Clear() 误删 FluentTheme 自带的系统资源字典
            if (_themeDictionary != null && Resources.MergedDictionaries.Contains(_themeDictionary))
                Resources.MergedDictionaries.Remove(_themeDictionary);
            Resources.MergedDictionaries.Add(d);
            _themeDictionary = d;
        }

        // 强调色可能刚变过 ⇒ 窗口 / 托盘图标一起重画
        RefreshAppIcon();
    }

    /// <summary>把强调色写进 FluentTheme 的调色板（官方入口 = ColorPaletteResources.Accent）。（WHY: SHL-006）</summary>
    private void ApplyFluentAccent(AccentPreset accent)
    {
        try
        {
            if (Styles?.OfType<FluentTheme>().FirstOrDefault() is not { } fluent) return;
            var color = Color.Parse(accent.Color);

            // 只改已有调色板的 Accent —— 官方约束：除 Accent 外的属性「启动后只读一次」，
            // 整体替换调色板条目等于在运行期重建整套静态色，得不偿失。
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                if (!fluent.Palettes.TryGetValue(variant, out var palette) || palette is null)
                {
                    palette = new ColorPaletteResources();
                    fluent.Palettes[variant] = palette;
                }

                palette.Accent = color;
            }
        }
        catch { /* 调色板写入失败不阻断换肤 */ }
    }

    /// <summary>三档圆角（卡片 / 控件 / 小元素）。原生皮肤直接沿用 Fluent 的 ControlCornerRadius 派生。</summary>
    private (CornerRadius Card, CornerRadius Control, CornerRadius Small) Radii(ThemeSkin skin)
    {
        if (skin == ThemeSkin.Fluent
            && this.TryFindResource("ControlCornerRadius", out var o)
            && o is CornerRadius c && c.TopLeft > 0)
        {
            return (new CornerRadius(c.TopLeft * 2), c, new CornerRadius(Math.Max(c.TopLeft / 2, 1)));
        }

        return (new CornerRadius(8), new CornerRadius(6), new CornerRadius(4));
    }

    /// <summary>
    /// 自制皮肤：把中性色阶铺到全部语义键上。
    /// 强调色只影响 6 个键，其余全部来自色阶 —— 这样"加一个主题"= 改这里一张表。
    /// </summary>
    private static void FillNewToys(ResourceDictionary d, bool dark, AccentPreset accent)
    {
        var bg0 = Color.Parse(dark ? DarkBg0 : LightBg0);
        var bg1 = Color.Parse(dark ? DarkBg1 : LightBg1);
        var bg2 = Color.Parse(dark ? DarkBg2 : LightBg2);
        var bg3 = Color.Parse(dark ? DarkBg3 : LightBg3);
        var bg4 = Color.Parse(dark ? DarkBg4 : LightBg4);
        var strong = Color.Parse(dark ? DarkBorderStrong : LightBorderStrong);
        var input = Color.Parse(dark ? DarkInput : LightInput);
        var ink0 = Color.Parse(dark ? DarkInk0 : LightInk0);
        var ink1 = Color.Parse(dark ? DarkInk1 : LightInk1);
        var ink2 = Color.Parse(dark ? DarkInk2 : LightInk2);
        var danger = Color.Parse(dark ? DarkDanger : LightDanger);
        var accentColor = Color.Parse(accent.Color);

        void S(string k, Color c) => d[k] = new SolidColorBrush(c);

        // —— 底 ——
        // 每个区域由 RegionLayers 派到某一档（⛔ 别在这里逐个手写，会跟相邻检查脱节）。
        var layers = new[] { bg0, bg1, bg2, bg3, bg4 };
        foreach (var (key, layer) in RegionLayers) S(key, layers[layer - 1]);

        // 页内抬头条 / 设置节头（不是窗口区域，不参与相邻检查）：抬到控件层，压在卡面上高 12
        S("ContentHeaderBrush", bg2);
        // 面板 / 按钮常体 / 浮层底：控件层 —— 按钮才有实面、浮层才浮得起
        S("PanelBrush", bg2);
        // 侧栏悬停：按侧栏自己的底提亮（CardHoverBrush 按卡面算，压在侧栏上会亮一大块）
        S("SiderHoverBrush", dark ? Blend(Colors.White, bg0, 0.08) : Blend(Colors.Black, bg0, 0.05));
        // 卡内悬停 / 按下：比卡面再提一点，落在卡片面上的控件才看得出来
        S("CardHoverBrush", dark ? Blend(Colors.White, bg1, 0.08) : Blend(Colors.Black, bg1, 0.05));
        // 列表行悬停：行区自成一档（3 档），与卡面差 10 ⇒ 通用的 CardHoverBrush 落在行区上几乎看不出变化，
        // 行区单独一档「提到卡面色」——行浮起来，而不是像按钮那样按下去。
        S("ListRowHoverBrush", Blend(Colors.White, bg2, dark ? 0.10 : 0.55));

        // —— 字 ——
        S("PrimaryTextBrush", ink0);
        S("SecondaryTextBrush", ink1);
        S("PlaceholderTextBrush", ink2);
        // 侧栏文字：亮色下侧栏是**白底**，白字会直接消失 ⇒ 跟主文字色走
        S("SiderTextBrush", dark ? Colors.White : ink0);
        S("SiderIconActiveBrush", dark ? Colors.White : ink0);
        S("SiderIconBrush", ink1);
        S("OnColorSecondaryTextBrush", Color.FromArgb(OnColorSecondaryAlpha, 255, 255, 255));

        // —— 语义 ——
        S("DangerBrush", danger);
        S("AccentBrush", accentColor);
        // 强调色**文字/图标**专用：浅底上的强调色要压深，否则与淡强调底同色相、字糊在背景里
        S("AccentInkBrush", dark ? accentColor : Blend(accentColor, Colors.Black, 0.45));
        S("AccentHoverBrush", Color.Parse(accent.HoverColor));
        S("AccentTextBrush", Colors.White);

        // —— 线与滚动条 ——
        S("InputBorderBrush", input);
        // 🔴 控件描边「淡化」必须盖两类键：本项目显式引用 InputBorderBrush 的那些**加** Fluent 模板
        //    自带的资源键 —— 裸 TextBox / ComboBox / Button（没写 BorderBrush 的）走的是后者，
        //    只改前者会有「一部分控件淡了、一部分还是原色」的割裂感。
        S("TextControlBorderBrush", input);
        S("ComboBoxBorderBrush", input);
        S("ButtonBorderBrush", input);
        // 表头下沿的 1px：卡内表头与行区同色，只能靠这条极淡线把「列标题」和「数据」分开
        S("HairlineBrush", Color.Parse(dark ? "#2C2C2C" : "#C6CBD3"));
        S("ScrollThumbBrush", strong);
        S("ScrollThumbHoverBrush", accentColor);
        S("WindowBorderBrush", strong);
        // 失焦态要"几乎融进底色"：深色取标题条档（比底色亮一档），浅色取分隔线档
        S("WindowBorderInactiveBrush", dark ? bg1 : bg3);

        // —— 窗口按钮（系统语义，固定红）——
        S("TitleBarCloseHoverBrush", Color.Parse(BrandCloseHover));
        S("TitleBarCloseHoverTextBrush", Colors.White);

        // —— 选中态：由强调色派生（深色底压暗、浅色底冲淡，对比度才够）——
        S("CardSelectedBrush", dark ? Blend(accentColor, bg2, 0.34) : Blend(accentColor, bg0, 0.26));
        S("CardSelectedTextBrush", dark ? Blend(accentColor, Colors.White, 0.34) : accentColor);

        // 遮罩 = 内容底色 + 50% 透明度（两套皮肤共用同一条派生规则）
        S("LoadingOverlayBrush", Color.FromArgb(OverlayAlpha, bg0.R, bg0.G, bg0.B));
    }

    /// <summary>
    /// 原生皮肤：先用自制皮肤垫底，再把能查到的键逐个覆盖成 Fluent 自带资源。
    /// 垫底是**故意的**——Avalonia 换版本时 Fluent 资源键可能改名，改动后最差也只是这几个键退回自有色阶，
    /// 不会出现一片透明/黑块。
    /// </summary>
    private void FillFluent(ResourceDictionary d, bool dark, AccentPreset accent)
    {
        FillNewToys(d, dark, accent);

        // 🔴 必须**显式传变体**查：ApplyTheme 刚设完 RequestedThemeVariant，Application 的
        //    ActualThemeVariant 要到样式重估后才刷新，此时不带变体查会拿到上一个明暗的取值
        //    （表现：切到原生皮肤后深色也一直是浅色）。
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var accentColor = Color.Parse(accent.Color);

        Color? Pick(string fluentKey)
        {
            if (!this.TryFindResource(fluentKey, variant, out var o)) return null;
            return o switch
            {
                ISolidColorBrush sb => sb.Color,
                Color c => c,
                _ => null,
            };
        }

        void S(string k, Color c) => d[k] = new SolidColorBrush(c);

        void F(string key, string fluentKey)
        {
            if (Pick(fluentKey) is { } c) d[key] = new SolidColorBrush(c);
        }

        // 底（四档：内容底 → chrome → 面板 → 悬停；与自制皮肤同一套语义键）
        F("ContentHeaderBrush", "SystemControlBackgroundChromeMediumLowBrush");
        F("CardHoverBrush", "SystemControlHighlightListLowBrush");

        // 区域底：按 RegionLayers 的同一个序号取底色。
        // 🔴 Fluent 自带的 4 个 Chrome 刷子在暗色下实际只给出 2 档（工具条 / 表头 / 列表 / 状态栏挤在同一灰度，
        //    去掉外缘后接缝直接消失 —— 用户实测「相邻区域没色差」）⇒ 以内容底为基准做明度阶梯：
        //    相邻档按 5% / 10% / 15% 向白（暗色）或黑（亮色）混，档差 ≥10，再按 RegionLayers 分发。
        var baseCol = Pick("SystemControlPageBackgroundChromeLowBrush")
                      ?? Color.Parse(dark ? DarkBg0 : LightBg0);
        var toward = dark ? Colors.White : Colors.Black;
        var fluentLayers = new[]
        {
            baseCol,
            Blend(toward, baseCol, 0.050),
            Blend(toward, baseCol, 0.100),
            Blend(toward, baseCol, 0.150),
            Blend(toward, baseCol, 0.200),
        };
        foreach (var (key, layer) in RegionLayers)
        {
            S(key, fluentLayers[layer - 1]);
        }
        // 面板 / 按钮常体并入同一阶梯（否则与表头落在同一个系统灰上，行区与表头又糊成一片）
        S("PanelBrush", fluentLayers[2]);
        // 列表行悬停：与自制皮肤同一口径 —— 行区（3 档）提到卡面色，而不是用只对卡面有效的 CardHoverBrush
        S("ListRowHoverBrush", Blend(toward, fluentLayers[2], 0.12));

        // 字
        F("PrimaryTextBrush", "SystemControlForegroundBaseHighBrush");
        // 🔴 浅色下二级 / 三级文字**不用** Fluent 的 BaseMedium / BaseLow：
        //    它们是约 60% / 40% 黑，12~13px 中文压在 #F3F3F3 底上整片发虚（用户实测「明亮主题字体很淡」）。
        //    改为「主文字色按比例混底」的派生值 —— 与自制皮肤同一套对比度，色相也不跑偏。
        //    深色保持 Fluent 原值：暗底上 60% 白本来就合适，压低反而糊。
        if (dark)
        {
            F("SecondaryTextBrush", "SystemControlForegroundBaseMediumBrush");
            F("PlaceholderTextBrush", "SystemControlForegroundBaseLowBrush");
        }
        else
        {
            var inkForText = Pick("SystemControlForegroundBaseHighBrush") ?? Color.Parse(LightInk0);
            var bgForText = Pick("SystemControlPageBackgroundChromeLowBrush") ?? Color.Parse(LightBg0);
            S("SecondaryTextBrush", Blend(inkForText, bgForText, 0.70));
            S("PlaceholderTextBrush", Blend(inkForText, bgForText, 0.45));
        }
        // 🔴 侧栏文字必须按明暗分两套：Fluent **浅色**的 ChromeMedium 是浅灰底，
        //    再配 ChromeWhite 文字就整片糊掉（曾表现成「原生皮肤只有明色、切深色不生效」）。
        if (dark)
        {
            F("SiderTextBrush", "SystemControlForegroundChromeWhiteBrush");
            F("SiderIconActiveBrush", "SystemControlForegroundChromeWhiteBrush");
            F("SiderIconBrush", "SystemControlForegroundChromeGrayBrush");
        }
        else
        {
            F("SiderTextBrush", "SystemControlForegroundBaseHighBrush");
            F("SiderIconActiveBrush", "SystemControlForegroundBaseHighBrush");
            F("SiderIconBrush", "SystemControlForegroundBaseMediumBrush");
        }

        // 语义
        F("DangerBrush", "SystemControlErrorTextForegroundBrush");
        // 🔴 强调色这 4 个键**不取 Fluent 的强调色资源**，一律用用户选的颜色派生：
        //    Fluent 的 SystemControl*Accent* 在运行时不一定跟随调色板（有的模板把它硬编进笔刷），
        //    要求「自制键 ↔ Fluent 控件」永远同色，只有自己算这条路可靠。
        S("AccentBrush", accentColor);
        S("AccentInkBrush", dark ? accentColor : Blend(accentColor, Colors.Black, 0.45));
        S("AccentHoverBrush", Color.Parse(accent.HoverColor));
        F("AccentTextBrush", "SystemControlForegroundChromeWhiteBrush");

        // 线：🔴 原生皮肤下 ⛔ 不直接用 Fluent 的 `TextControlBorderBrush`（偏重，换皮肤时边框观感突变）；按内容底混 12% 白/黑。
        // ⚠️ 混合量放常量：`lint.py` 靠 `Blend(toward, baseCol, X)` 正则顺序抓档位，多一个字面量就整体错位。
        const double EdgeMix = 0.12;
        var edge = Blend(toward, baseCol, EdgeMix);
        S("InputBorderBrush", edge);
        S("TextControlBorderBrush", edge);
        S("ComboBoxBorderBrush", edge);
        S("ButtonBorderBrush", edge);
        // 表头下沿的 1px（与自制皮肤同一个用途：卡内表头与行区同色）
        S("HairlineBrush", Blend(toward, baseCol, 0.10));
        F("ScrollThumbBrush", "SystemControlForegroundBaseMediumLowBrush");
        S("ScrollThumbHoverBrush", accentColor);
        F("WindowBorderBrush", "SystemControlForegroundBaseLowBrush");
        F("WindowBorderInactiveBrush", "SystemControlBackgroundBaseLowBrush");

        // 选中态：与自制皮肤同一条派生规则 —— 底色千差万别，强调色的混合物必须自己算才跟用户走
        var pageBg = Pick("SystemControlPageBackgroundChromeLowBrush") ?? Color.Parse(dark ? DarkBg0 : LightBg0);
        var panelBg = Pick("SystemControlBackgroundChromeMediumBrush") ?? Color.Parse(dark ? DarkBg2 : LightBg2);
        S("CardSelectedBrush", dark ? Blend(accentColor, panelBg, 0.34) : Blend(accentColor, pageBg, 0.26));
        S("CardSelectedTextBrush", dark ? Blend(accentColor, Colors.White, 0.34) : accentColor);

        // 遮罩跟着实际内容底色走（原生皮肤的底和自制不同，不能沿用垫底值）
        if (Pick("SystemControlPageBackgroundChromeLowBrush") is { } overlayBg)
            S("LoadingOverlayBrush", Color.FromArgb(OverlayAlpha, overlayBg.R, overlayBg.G, overlayBg.B));

        // 侧栏悬停：按侧栏自己的底色提亮（CardHoverBrush 是按阅读底算的，压在侧栏上会亮一大块）
        S("SiderHoverBrush", Blend(toward, baseCol, 0.09));
    }

    /// <summary>按 <paramref name="fgWeight"/>（0~1，前景占比）做线性混色 —— 用来从强调色派生浅/深变体。</summary>
    private static Color Blend(Color fg, Color bg, double fgWeight)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(Math.Round(a * fgWeight + b * (1 - fgWeight)), 0, 255);
        return Color.FromArgb(255, Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }
}
