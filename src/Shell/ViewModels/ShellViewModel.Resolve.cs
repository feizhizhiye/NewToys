using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Core.Services;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Shell.Models;
using NewToys.Shell.Services;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

public partial class ShellViewModel : ViewModelBase
{

    private TabItemViewModel? CreateTab(string key)
    {
        if (string.Equals(key, HomeKey, StringComparison.Ordinal))
        {
            var home = _sp.GetRequiredService<HomeView>();
            // ⚠️ 必须**先给 View 设好 DataContext，再挂到内容区**（DataContext 是继承属性）。
            if (home.DataContext == null)
                home.DataContext = _sp.GetRequiredService<HomeViewModel>();
            return new TabItemViewModel(HomeKey, "首页", "🏠", home, home.DataContext,
                                        canClose: false);
        }

        // 应用级设置页（**一页多节**）—— 与首页同构：从 DI 取 View + VM。
        // ⛔ 不走菜单项解析：`settings` 是**分组**键，压根没有对应的 AppMenuItem。
        if (string.Equals(key, SettingsKey, StringComparison.Ordinal))
        {
            var settings = _sp.GetRequiredService<SettingsView>();
            if (settings.DataContext == null)
                settings.DataContext = _sp.GetRequiredService<SettingsViewModel>();
            return new TabItemViewModel(SettingsKey, "设置", "⚙", settings, settings.DataContext, canClose: true);
        }

        var item = _menu.FindItem(key);
        if (item == null) return null;

        var view = ResolveView(item);
        return new TabItemViewModel(item.Key, item.Title, item.Icon, view, view.DataContext, canClose: true);
    }

    /// <summary>
    /// 按约定定位 View：XxxView → XxxViewModel。
    /// 约定：<c>NewToys.&lt;模块&gt;.Views.XxxView</c> ↔ <c>NewToys.&lt;模块&gt;.ViewModels.XxxViewModel</c>。
    /// ⚠️ 必须在 **View 自己所在的程序集** 里先找 —— 不同模块允许出现同名 View。
    /// </summary>
    private Control ResolveView(AppMenuItem it)
    {
        var viewType = _pages.FindByName(it.ViewTypeName)
            .FirstOrDefault(t => t.IsClass && !t.IsAbstract && typeof(Control).IsAssignableFrom(t));
        if (viewType == null) return BuildErrorView($"找不到视图类型：{it.ViewTypeName}");

        Control? view;
        try { view = ActivatorUtilities.CreateInstance(_sp, viewType) as Control; }
        catch (Exception ex) { return BuildErrorView($"视图 {it.ViewTypeName} 创建失败：{ex.Message}"); }
        if (view == null) return BuildErrorView($"视图 {it.ViewTypeName} 实例化返回 null");

        // DataContext 解析失败不该把整次导航也搭进去 —— 顶多这一页没数据，不能连页面都不给。
        try { view.DataContext = ResolveViewModel(viewType); }
        catch (Exception ex) { return BuildErrorView($"视图 {it.ViewTypeName} 的 ViewModel 解析失败：{ex.Message}"); }
        return view;
    }

    /// <summary>按命名约定解析并实例化 VM（规则见 <see cref="ViewNaming"/>）：先按候选类型逐个试，都不成返回 null。</summary>
    private object? ResolveViewModel(Type viewType)
    {
        foreach (var vmType in _pages.ViewModelCandidates(viewType))
        {
            try { return ActivatorUtilities.GetServiceOrCreateInstance(_sp, vmType); }
            catch { /* 换下一个候选 */ }
        }
        return null;
    }

    private Control BuildErrorView(string msg)
    {
        var tb = new TextBlock
        {
            Text = msg,
            Foreground = Avalonia.Media.Brushes.Red,
            Margin = new Avalonia.Thickness(24),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = "Consolas"
        };
        return new ScrollViewer { Content = tb };
    }

}
