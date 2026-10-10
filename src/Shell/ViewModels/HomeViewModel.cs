using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Shell.ViewModels;

public class QuickBtn
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "🌐";
    public string Color { get; set; } = "#07C160";
    public ICommand RunCommand { get; set; } = null!;
}

public class FavCard
{
    public string Title { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Key { get; set; } = "";
    public string Icon { get; set; } = "";
}

public partial class HomeViewModel : ViewModelBase
{
    private readonly IServiceProvider _sp;
    private readonly IMenuService _menu;
    private readonly LaunchRunner _runner;
    private readonly ILaunchConfigService _launch;
    private readonly IFavoritesService _fav;

    [ObservableProperty] private string _version = "v0.2.0";

    public ObservableCollection<FavCard> Favorites { get; } = new();
    public ObservableCollection<QuickBtn> QuickButtons { get; } = new();

    public HomeViewModel(IServiceProvider sp, IMenuService menu, LaunchRunner runner,
                         ILaunchConfigService launch, IFavoritesService fav)
    {
        _sp = sp; _menu = menu; _runner = runner; _launch = launch; _fav = fav;
        NavigateCommand = new RelayCommand<string?>(k =>
        {
            if (string.IsNullOrEmpty(k)) return;
            var shell = sp.GetRequiredService<ShellViewModel>();
            var item = menu.FindItem(k);
            if (item == null) return;
            shell.SelectGroupCommand.Execute(item.GroupKey);
            shell.SelectItemCommand.Execute(k);
        });
        // 收藏夹变化（来自「设置 → 功能清单」右键固定/取消）→ 实时刷新首页卡片。
        // 订阅放在 HomeView 的 VisualTree 挂接/卸载时，避免瞬态 VM 被单例 FavoritesService 长期持有。
        BuildContent();
    }

    public ICommand NavigateCommand { get; }

    /// <summary>收藏夹卡片：key 来自 <see cref="IFavoritesService"/>，标题/图标/简介现从菜单取 —— 单一真相源。</summary>
    internal void BuildFavorites()
    {
        Favorites.Clear();
        foreach (var key in _fav.Keys)
        {
            var item = _menu.FindItem(key);
            if (item == null) continue;
            Favorites.Add(new FavCard
            {
                Title = item.Title,
                Desc = item.Desc,
                Key = item.Key,
                Icon = item.Icon
            });
        }
    }

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
            Color = "#2E9FFF",
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
            // 按钮名取自 launch.json 的第一个启动项 —— 「设置 → 一键按钮」页里改的就是它
            Title = cfg.Programs.FirstOrDefault()?.Name is { Length: > 0 } n ? n : "批量打开软件",
            Subtitle = "常用软件一键启动",
            Icon = "📦",
            Color = "#FF8C00",
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