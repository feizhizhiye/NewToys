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
    /// <summary>
    /// 把「跟随系统」所需的系统明暗状态喂给 <see cref="IThemeService"/>。
    /// <para>
    /// 为什么放在 Shell：<c>IPlatformSettings</c> 是 Avalonia 类型，而 <c>Core</c> 不引用 Avalonia，
    /// 所以由这一层读、由 <see cref="IThemeService.SystemDark"/> 收。
    /// 事件可能从**非 UI 线程**触发，改状态要 <c>Post</c> 回 UI 线程（随后 <c>ApplyTheme</c> 会动资源字典）。
    /// </para>
    /// 平台不支持时静默放弃：System 模式退化为「系统状态固定为默认深色」，不影响其余功能。
    /// </summary>
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

    /// <summary>
    /// 按当前主题状态重建资源字典。**幂等**：随时可重复调用。
    /// 深色 / 浅色两套基础色 + **强调色派生色**（强调色与「选中的卡片」底/字色联动，见 <see cref="AccentPalette"/>）。
    /// </summary>
    private void ApplyTheme(IThemeService theme)
    {
        var dark = theme.IsDark;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = AccentPalette.Find(theme.AccentId);
        var accentColor = Color.Parse(accent.Color);

        var d = new ResourceDictionary();
        void S(string k, string c) => d[k] = new Avalonia.Media.SolidColorBrush(Color.Parse(c));
        if (dark)
        {
            S("ContentBrush","#191919");
            // 窗口外框（自绘 1px）。激活态要能在同色窗口上分辨出来 → 比 DividerBrush(#333) 更亮；
            // 失焦态压暗到接近底色，形成「哪个窗口在前台」的直接视觉信号。
            S("WindowBorderBrush","#4A4A4A"); S("WindowBorderInactiveBrush","#2A2A2A");
            S("TitleBarCloseHoverBrush","#E81123"); S("TitleBarCloseHoverTextBrush","#FFFFFF");
            S("SiderBrush","#202020"); S("SiderTextBrush","#FFFFFF"); S("SiderIconBrush","#9A9A9A"); S("SiderIconActiveBrush","#FFFFFF"); S("SiderSelectedBrush","#2E2E2E"); S("SiderDividerBrush","#2E2E2E");
            S("SubSiderBrush","#262626"); S("SubSiderHeaderBrush","#2B2B2B");
            // 「选中的卡片」用强调色派生：底 ≈ 强调色压暗到深色面板上，字 ≈ 强调色提亮（深色底上才看得清）
            // （/ 需求 §4.10.4「激活态加深」）：原强调色占比 22% 太淡，菜单/标签/列表选中不凸显；
            //   加深到 34%（选中 vs 悬停/常规对比更明显），字色同步提亮到 34% 保证深色底上可读。
            S("CardSelectedBrush", Hex(Blend(accentColor, Color.Parse("#262626"), 0.34)));
            S("CardSelectedTextBrush", Hex(Blend(accentColor, Colors.White, 0.34)));
            S("ContentBrush","#191919"); S("ContentHeaderBrush","#1F1F1F"); S("StatusBarBrush","#1F1F1F");
            S("PanelBrush","#262626"); S("CardHoverBrush","#333333"); S("DividerBrush","#333333"); S("DangerBrush","#F85149"); S("LoadingOverlayBrush","#80000000");
            S("PrimaryTextBrush","#E5E5E5"); S("SecondaryTextBrush","#9E9E9E"); S("SiderIconHoverBrush","#3A3A3A");
            // 输入框水印（v3.68：用户定调「太重」）：比次要文字再淡一档，只给 PlaceholderText 用
            S("PlaceholderTextBrush","#6B6B6B");
            S("AccentBrush", accent.Color); S("AccentHoverBrush", accent.HoverColor); S("AccentTextBrush","#FFFFFF");
            S("CodeBlockBrush","#1F1F1F"); S("InputBorderBrush","#3D3D3D"); S("InputHoverBorderBrush", accent.Color);
            // 滚动条滑块：深色底上要能看出「有一根条」，但不能抢眼；悬停切强调色
            S("ScrollThumbBrush","#4A4A4A"); S("ScrollThumbHoverBrush", accent.Color);
        }
        else
        {
            S("ContentBrush","#FFFFFF");
            // 浅色下窗口底色就是白的 → 边框必须够深才看得见（比 DividerBrush(#E6E8EC) 深一档）；
            // 失焦态退回 DividerBrush 同档，几乎融进白底。
            S("WindowBorderBrush","#C8CCD2"); S("WindowBorderInactiveBrush","#E6E8EC");
            S("TitleBarCloseHoverBrush","#E81123"); S("TitleBarCloseHoverTextBrush","#FFFFFF");
            S("SiderBrush","#2E2E2E"); S("SiderTextBrush","#FFFFFF"); S("SiderIconBrush","#B7B7B7"); S("SiderIconActiveBrush","#FFFFFF"); S("SiderSelectedBrush","#444444"); S("SiderDividerBrush","#3F3F3F");
            S("SubSiderBrush","#F3F3F3"); S("SubSiderHeaderBrush","#F7F7F7");
            // 浅色底上的选中卡片：底 ≈ 强调色冲淡到接近白，字 = 强调色本身
            // 浅色选中底同样加深（16%→26%），选中态更清晰、不易被忽略。
            S("CardSelectedBrush", Hex(Blend(accentColor, Colors.White, 0.26)));
            S("CardSelectedTextBrush", accent.Color);
            S("ContentBrush","#FFFFFF"); S("ContentHeaderBrush","#FFFFFF"); S("StatusBarBrush","#FAFBFC");
            S("PanelBrush","#F7F8FA"); S("CardHoverBrush","#EFEFEF"); S("DividerBrush","#E6E8EC"); S("DangerBrush","#D93026"); S("LoadingOverlayBrush","#80FFFFFF");
            S("PrimaryTextBrush","#1F1F1F"); S("SecondaryTextBrush","#8A8A8F"); S("SiderIconHoverBrush","#E9E9E9");
            // 输入框水印：浅色下同样比次要文字淡一档
            S("PlaceholderTextBrush","#B4B4BA");
            S("AccentBrush", accent.Color); S("AccentHoverBrush", accent.HoverColor); S("AccentTextBrush","#FFFFFF");
            S("CodeBlockBrush","#FFFFFF"); S("InputBorderBrush","#D1D5DB"); S("InputHoverBorderBrush", accent.Color);
            // 滚动条滑块：白底上要比 DividerBrush(#E6E8EC) 深一档才看得见；悬停切强调色
            S("ScrollThumbBrush","#C6CAD0"); S("ScrollThumbHoverBrush", accent.Color);
        }
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

    /// <summary>按 <paramref name="fgWeight"/>（0~1，前景占比）做线性混色 —— 用来从强调色派生浅/深变体。</summary>
    private static Color Blend(Color fg, Color bg, double fgWeight)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(Math.Round(a * fgWeight + b * (1 - fgWeight)), 0, 255);
        return Color.FromArgb(255, Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }

    /// <summary><c>#RRGGBB</c> 文本（<c>Color.Parse</c> 能原样读回）。</summary>
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

}
