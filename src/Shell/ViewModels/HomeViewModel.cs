using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;
using NewToys.Shell.Views;

namespace NewToys.Shell.ViewModels;

public class QuickBtn
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "🌐";
    public string Color { get; set; } = "#07C160";
    public ICommand RunCommand { get; set; } = null!;
}

/// <summary>
/// 首页「常用功能」墙里的一张卡（v3.19 / NT-009）。
/// <para>
/// v3.66 / NT-062 细节调整：尾部追加一张**「显示更多」卡**（<see cref="IsMore"/> = true）——
/// 它与收藏按钮**同容器同流转**（同一个 <c>WrapPanel</c>）、恒排最后，点开把**功能清单就地展开**在下方
/// （⛔ 不再跳独立页）。故命令与命令参数下沉到卡片自身（收藏卡 → 导航；更多卡 → 折叠切换）。
/// </para>
/// </summary>
public partial class FavCard : ObservableObject
{
    /// <summary>目标功能 Key（「显示更多」卡为空）。</summary>
    public string Key { get; set; } = "";

    /// <summary>悬停提示（收藏卡 = 菜单项简介）。</summary>
    public string Desc { get; set; } = "";

    /// <summary>图标（收藏卡 = 菜单项 emoji；「显示更多」卡 = ＋ / －）。</summary>
    public string Icon { get; set; } = "";

    /// <summary>所属模块分组的 key（<c>pc</c> / <c>web</c> / <c>video</c> / <c>assets</c>）。</summary>
    public string GroupKey { get; set; } = "";

    /// <summary>按钮底色（<c>#RRGGBB</c>）：同一模块 = 同一颜色，⛔ 不随"取消固定后其余卡片前移"而变。</summary>
    public string Color { get; set; } = "#07C160";

    /// <summary>是否尾部「显示更多 / 收起」卡（⛔ 不是收藏项：右键菜单与「取消固定」都不适用）。</summary>
    public bool IsMore { get; set; }

    /// <summary>点击执行（收藏卡 = 导航命令；「显示更多」卡 = 折叠切换命令）。</summary>
    public ICommand? Command { get; set; }

    public object? CommandParameter { get; set; }

    /// <summary>按钮文字（「显示更多」卡要随展开态变），故可观察。</summary>
    [ObservableProperty] private string _title = "";
}

public partial class HomeViewModel : ViewModelBase, IStatusProvider
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly LaunchRunner _runner;
    private readonly ILaunchConfigService _launch;
    private readonly IFavoritesService _fav;

    [ObservableProperty] private string _version = "v0.2.0";

    public ObservableCollection<FavCard> Favorites { get; } = new();
    public ObservableCollection<QuickBtn> QuickButtons { get; } = new();

    /// <summary>真实收藏数 &gt; 0（不含尾部「显示更多」卡）—— 空态提示用。</summary>
    private bool _hasFavorites;

    /// <summary>收藏卡尾部「显示更多 / 收起」卡：**恒在最后**（每次重建收藏都重新追加）。</summary>
    private readonly FavCard _moreCard = new() { IsMore = true, Icon = "＋" };

    private Control? _functionList;

    // ==================== 内嵌功能清单（v3.66 / NT-062 细节调整） ====================

    /// <summary>
    /// 功能清单是否已就地展开（收藏卡尾部「显示更多」切换）。
    /// ⚠️ 展开后一键操作区随页面滚动、折叠时钉在窗口底部 —— 见 HomeView 的 Grid 布局与 MinHeight。
    /// </summary>
    [ObservableProperty] private bool _isFunctionListOpen;

    /// <summary>
    /// 内嵌的**功能清单**视图（复用 <see cref="AllFunctionsView"/>：同一份"全集"卡片墙，
    /// 右键仍然可固定 / 取消固定到本页收藏卡）。⛔ 不复刻一份 XAML。
    /// </summary>
    public Control? FunctionListContent => _functionList ??= BuildFunctionList();

    private Control BuildFunctionList()
    {
        var view = _sp.GetRequiredService<AllFunctionsView>();
        if (view.DataContext == null)
            view.DataContext = _sp.GetRequiredService<AllFunctionsViewModel>();
        return view;
    }

    // ==================== 底部状态栏（IStatusProvider，v3.19 / NT-009）：仅首页显示模块/功能数 ====================

    /// <summary>
    /// 首页底部状态栏文本：已加载**模块数 / 功能数**（需求 §6.1）。
    /// 菜单（<see cref="IMenuService.Groups"/>）是单一真相源；「首页」与**不渲染宿主分组**都不算模块。
    /// ⚠️ 仅首页显示——其它页面各自实现 IStatusProvider 显示本页状态，Shell 状态栏随标签切换。
    /// </summary>
    public string StatusText
    {
        get
        {
            var groups = _menu?.Groups?.Where(g => !g.IsHome).ToList();
            var modules = groups?.Count ?? 0;
            var funcs = groups?.Sum(g => g.Items?.Count ?? 0) ?? 0;
            return $"已加载 {modules} 个模块 / {funcs} 个功能";
        }
    }

    /// <summary>模块/功能数为静态值（运行期不变），故不触发；保留事件以满足 <see cref="IStatusProvider"/>。</summary>
#pragma warning disable CS0067 // 事件是 IStatusProvider 契约要求，但本页状态静态、永不触发——不是遗漏
    public event EventHandler? StatusChanged;
#pragma warning restore CS0067

    public HomeViewModel(IServiceProvider sp, IMenuService menu, LaunchRunner runner,
                         ILaunchConfigService launch, IFavoritesService fav)
    {
        _sp = sp; _menu = menu; _runner = runner; _launch = launch; _fav = fav;
        NavigateCommand = new RelayCommand<string?>(k =>
        {
            if (string.IsNullOrEmpty(k)) return;
            // v3.1：导航统一走标签页（打开或激活对应 key 的标签）。
            var shell = sp.GetRequiredService<ShellViewModel>();
            shell.OpenTabCommand.Execute(k);
        });
        // 右键「✕ 取消固定」：与「脚本管理器 → 收藏夹」按钮墙同一个操作逻辑
        // （收藏 = IFavoritesService 单一真相源，取消后首页与功能清单页同时更新）。
        UnpinCommand = new RelayCommand<string?>(k =>
        {
            if (string.IsNullOrEmpty(k)) return;
            _fav.Unpin(k);
            BuildFavorites();
        });
        // 尾部「显示更多」：**就地展开 / 收起**功能清单（v3.66：⛔ 不再跳独立页）。
        _moreCard.Command = ToggleFunctionListCommand;

        // 收藏夹变化（来自内嵌功能清单右键固定/取消）→ 实时刷新首页卡片。
        // 订阅放在 HomeView 的 VisualTree 挂接/卸载时，避免瞬态 VM 被单例 FavoritesService 长期持有。
        BuildContent();
    }

    public ICommand NavigateCommand { get; }

    /// <summary>右键菜单里的「✕ 取消固定」（参数 = 功能 key）。</summary>
    public ICommand UnpinCommand { get; }

    /// <summary>展开 / 收起内嵌的功能清单（按钮文字与图标跟着切）；由 <c>[RelayCommand]</c> 生成命令属性。</summary>
    [RelayCommand]
    private void ToggleFunctionList()
    {
        IsFunctionListOpen = !IsFunctionListOpen;
        SyncMoreCard();
    }

    /// <summary>把「显示更多」卡的文案与图标同步成当前展开态。</summary>
    private void SyncMoreCard()
    {
        _moreCard.Title = IsFunctionListOpen ? "收起" : "显示更多";
        _moreCard.Icon = IsFunctionListOpen ? "－" : "＋";
        _moreCard.Desc = IsFunctionListOpen
            ? "收起功能清单"
            : "打开功能清单：全部模块与功能（右键可固定到本页）";
    }

    /// <summary>收藏夹卡片：key 来自 <see cref="IFavoritesService"/>，标题/图标/简介现从菜单取 —— 单一真相源。</summary>
    internal void BuildFavorites()
    {
        Favorites.Clear();
        var count = 0;
        foreach (var key in _fav.Keys)
        {
            var item = _menu.FindItem(key);
            if (item == null) continue;
            Favorites.Add(new FavCard
            {
                Title = item.Title,
                Desc = item.Desc,
                Key = item.Key,
                Icon = item.Icon,
                GroupKey = item.GroupKey,
                Color = ColorOfGroup(item.GroupKey),
                Command = NavigateCommand,
                CommandParameter = item.Key
            });
            count++;
        }
        _hasFavorites = count > 0;

        // v3.66：尾部「显示更多」卡 —— 与收藏按钮**同一个 WrapPanel**（同流转、自动换行）、**恒在最后**。
        SyncMoreCard();
        Favorites.Add(_moreCard);

        OnPropertyChanged(nameof(HasFavorites));
    }

    /// <summary>常用功能是否为空（空态提示用）；⛔ 不含尾部「显示更多」卡。</summary>
    public bool HasFavorites => _hasFavorites;

    /// <summary>
    /// 按所属模块给按钮一个底色（同一模块 = 同一颜色，稳定不跳变）。
    /// 颜色取自 <see cref="AccentPalette"/> 的同族色号，保证与主题色卡同源。
    /// </summary>
    private static string ColorOfGroup(string groupKey) => (groupKey ?? "").Trim().ToLowerInvariant() switch
    {
        "script" => "#2E9FFF",
        "web" => "#00ACC1",
        "video" => "#7C4DFF",
        "settings" => "#FF8F00",
        _ => AccentPalette.Default.Color
    };

    private void BuildContent()
    {
        BuildFavorites();

        QuickButtons.Clear();
        var cfg = _launch.Config;
        QuickButtons.Add(new QuickBtn
        {
            Title = cfg.Browser1.Name,
            Subtitle = "自定义网址列表",
            Icon = "🌐",
            Color = "#07C160",
            RunCommand = new RelayCommand(() => RunBrowser(cfg.Browser1))
        });
        QuickButtons.Add(new QuickBtn
        {
            Title = cfg.Browser2.Name,
            Subtitle = "工作/学习独立配置",
            Icon = "🌐",
            Color = "#07C160",
            RunCommand = new RelayCommand(() => RunBrowser(cfg.Browser2))
        });
        QuickButtons.Add(new QuickBtn
        {
            // 按钮名取自 launch.json 的第一个启动项 —— 应用级设置页「一键按钮」节里改的就是它
            Title = cfg.Programs.FirstOrDefault()?.Name is { Length: > 0 } n ? n : "批量打开软件",
            Subtitle = "常用软件一键启动",
            Icon = "📦",
            Color = "#07C160",
            RunCommand = new RelayCommand(RunPrograms)
        });
    }

    private void RunBrowser(LaunchButtonConfig b)
    {
        var urls = (b.Urls ?? "").Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
        _runner.OpenBrowser(b.ProgramPath, b.Arguments, urls);
    }

    private void RunPrograms()
    {
        var list = new List<(string,string)>();
        foreach (var p in _launch.Config.Programs)
        {
            if (!string.IsNullOrWhiteSpace(p.ProgramPath) && File.Exists(p.ProgramPath))
                list.Add((p.ProgramPath, p.Arguments ?? ""));
        }
        _runner.LaunchPrograms(list.ToArray());
    }
}
