using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using MenuItem = NewToys.Shared.Models.MenuItem;
using NewToys.Shared.Services;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly IThemeService _theme;
    private readonly INavigationService _nav;

    [ObservableProperty] private bool _isHomePage = true;
    [ObservableProperty] private MenuGroup? _currentGroup;
    [ObservableProperty] private MenuItem? _currentItem;
    [ObservableProperty] private object? _currentContent;
    [ObservableProperty] private string _statusText = "NewToys · 已就绪";

    public IReadOnlyList<MenuGroup> Groups => _menu.Groups;

    public ShellViewModel(IServiceProvider sp, IMenuService menu, IThemeService theme, INavigationService nav)
    {
        _sp = sp; _menu = menu; _theme = theme; _nav = nav;
        SelectGroupCommand = new RelayCommand<string?>(k => { if (!string.IsNullOrEmpty(k)) SelectGroup(k); });
        SelectItemCommand = new RelayCommand<string?>(k => { if (!string.IsNullOrEmpty(k)) SelectItem(k); });
        // 标题栏的主题开关：改的是 IThemeService（立即落盘 + 广播），App 收到后重建资源字典，
        // 不需要任何"应用"按钮，也不需要重启。
        ToggleThemeCommand = new RelayCommand(() => _theme.Toggle());
        GoHomeCommand = new RelayCommand(() =>
        {
            // 首页不属于任何分组：清掉分组/子项，第一列的模块高亮随之清空。
            // 顺序同 SelectGroup —— 先落状态，最后翻 IsHomePage（视图按这两个属性算高亮与第二列可见性）。
            CurrentItem = null;
            CurrentGroup = null;
            IsHomePage = true;
            // ⚠️ 顺序不可颠倒：**先给 View 设好 DataContext，再挂到内容区**。
            // DataContext 是继承属性 —— 一旦把 HomeView 赋给绑定的 ContentControl，它会立刻进入
            // 可视树并从父级继承 ShellViewModel；此时再判 `DataContext == null` 恒为 false，
            // HomeViewModel 永远设不上 → 页面只剩静态 XAML，收藏卡 / 一键按钮全空。
            // （启动时之所以正常：本方法在 ShellViewModel 构造里执行，那一刻 ShellView 还没建、
            //  ContentControl 未绑定，HomeView 尚未进树，DataContext 仍为 null，所以能设上 VM。）
            var home = _sp.GetRequiredService<HomeView>();
            if (home.DataContext == null)
                home.DataContext = _sp.GetRequiredService<HomeViewModel>();
            CurrentContent = home;
        });
        GoHomeCommand.Execute(null);
        // 模块（如「功能清单」页、模块首页）通过 INavigationService.NavigateTo(key) 发起跳转，
        // 这里统一接住 Navigated 事件来切换内容区 —— 这样模块无需引用 Shell 程序集即可导航。
        _nav.Navigated += (s, e) => OnNavigated(e.key, e.view);
    }

    /// <summary>
    /// 接住 <see cref="INavigationService.Navigated"/>：把导航目标（已实例化的 View）设好 VM 后切到内容区，
    /// 并同步分组 / 选中态（让侧栏高亮与二级菜单跟着更新）。
    /// </summary>
    private void OnNavigated(string key, object? view)
    {
        if (view is not Control ctrl) return;
        var it = _menu.FindItem(key);
        if (it == null) return;
        CurrentItem = it;
        CurrentGroup = _menu.FindGroup(it.GroupKey) ?? CurrentGroup;
        IsHomePage = false;
        if (ctrl.DataContext == null)
            ctrl.DataContext = ResolveViewModel(ctrl.GetType(), it.ViewTypeName);
        CurrentContent = ctrl;
    }

    public ICommand SelectGroupCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand GoHomeCommand { get; }
    public ICommand ToggleThemeCommand { get; }

    private void SelectGroup(string key)
    {
        var g = _menu.FindGroup(key);
        if (g == null) return;

        // ⚠️ 赋值顺序不能颠倒：**先落 CurrentGroup，再翻 IsHomePage**。
        // 视图层在 CurrentGroup / IsHomePage 任一变化时都会重算「二级菜单列」的可见性，
        // 而可见性判定同时依赖这两个属性。若先翻 IsHomePage，那一刻 CurrentGroup 还是旧值
        // （首次点击一级菜单时是 null）→ 被判成「本组没有二级项」→ 第二列保持 IsVisible=false，
        // 后续 CurrentGroup 变化只重建列表、不会再把列显示出来 ——
        // 表现就是「点一级菜单不出现二级菜单，但内容区正常切换」。
        CurrentGroup = g;
        IsHomePage = false;

        // 默认选中第一项；分组下没有二级项时，该一级菜单本身就是一个整页。
        var first = g.Items.FirstOrDefault();
        if (first != null) SelectItem(first.Key);
        else { CurrentItem = null; CurrentContent = null; }
    }

    private void SelectItem(string key)
    {
        var it = _menu.FindItem(key);
        if (it == null) return;
        CurrentItem = it;
        CurrentGroup = _menu.FindGroup(it.GroupKey) ?? CurrentGroup;
        IsHomePage = false;
        // DI 解析 View
        CurrentContent = ResolveView(it);
    }

    /// <summary>
    /// 按约定定位 View：XxxView → XxxViewModel。
    /// 约定：<c>NewToys.&lt;模块&gt;.Views.XxxView</c> ↔ <c>NewToys.&lt;模块&gt;.ViewModels.XxxViewModel</c>。
    /// ⚠️ 必须在 **View 自己所在的程序集** 里先找 —— 不同模块允许出现同名 View
    /// （例如 Module.Settings 与 Module.WebTools 都有 DnsView），全 AppDomain 按类名扫会撞车。
    /// </summary>
    private Control? ResolveView(MenuItem it)
    {
        Type? viewType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var t = asm.GetTypes().FirstOrDefault(x =>
                x.Name == it.ViewTypeName
                && !x.IsAbstract && x.IsClass
                && typeof(Control).IsAssignableFrom(x));
            if (t != null) { viewType = t; break; }
        }
        if (viewType == null) return BuildErrorView($"找不到视图类型：{it.ViewTypeName}");

        Control? view;
        try { view = ActivatorUtilities.CreateInstance(_sp, viewType) as Control; }
        catch (Exception ex) { return BuildErrorView($"视图 {it.ViewTypeName} 创建失败：{ex.Message}"); }
        if (view == null) return BuildErrorView($"视图 {it.ViewTypeName} 实例化返回 null");

        view.DataContext = ResolveViewModel(viewType, it.ViewTypeName);
        return view;
    }

    /// <summary>按「命名空间 .Views → .ViewModels + 类名 View→ViewModel」的约定解析 VM。</summary>
    private object? ResolveViewModel(Type viewType, string viewName)
    {
        var vmName = viewName.EndsWith("View", StringComparison.Ordinal)
            ? viewName[..^4] + "ViewModel"
            : viewName + "ViewModel";

        var candidates = new List<Type?>();
        if (viewType.Namespace?.EndsWith(".Views", StringComparison.Ordinal) == true)
        {
            var ns = viewType.Namespace[..^".Views".Length] + ".ViewModels";
            candidates.Add(viewType.Assembly.GetType($"{ns}.{vmName}", throwOnError: false));
        }
        // 兜底：全程序集按类名找（用于 View 命名空间不合规的历史页面）
        candidates.Add(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => a.GetTypes().FirstOrDefault(t => t.Name == vmName && !t.IsAbstract && t.IsClass))
            .FirstOrDefault(t => t != null));

        foreach (var vmType in candidates)
        {
            if (vmType == null) continue;
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
            Margin = new Thickness(24),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = "Consolas"
        };
        return new ScrollViewer { Content = tb };
    }
}