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

    /// <summary>刷新当前标签页：销毁并重建 View + VM（标签身份/位置不变）。</summary>
    [RelayCommand]
    private void Refresh()
    {
        var tab = ActiveTab;
        if (tab == null) return;
        var rebuilt = CreateTab(tab.Key);
        if (rebuilt == null) return;

        // ⚠️ 这里曾试过"先把内容区置空再挂新 View"，实测会让页面**整片空白**
        // （内容区的 Content 换成新 View 时挂不上）⇒ 已回滚。现在就按"原地换 View"来。
        tab.Replace(rebuilt.Content, rebuilt.ViewModel);
        _binder.Bind(tab);
        // 内容区由 Shell 侧**显式挂载**（见 ShellView.SyncContentHost）——
        // 标签还是同一个、只是它缓存的 View 换了 ⇒ 这里补发一次"当前标签变了"，让内容区重挂。
        OnPropertyChanged(nameof(ActiveTab));
        StatusText = $"已重建：{tab.Title}";
    }

    // ==================== 标题栏 / 左树 ====================

    // 注：标题栏删掉了 🏠 主页 / ⚙ 设置 / 🌓 主题三个按钮 →
    // OpenSettings / ToggleTheme 两个命令随之移除（设置走左树 / Ctrl+,、主题走设置页）。
    // 🔴 标签栏 `＋` 从「打开功能清单页」改为「**导航到首页**」——
    // 首页收藏卡尾部自带功能清单入口（内嵌 `AllFunctionsView`），`＋` 不再直达 `settings-all`。
    [RelayCommand] private void OpenHomeTab() => OpenTab(HomeKey);

    /// <summary>
    /// 打开**应用级设置页**（需求 §4.3）：入口 = 左树底部版本行的**设置齿轮**
    /// 与 `Ctrl+,`。默认落在「通用」节（页内可再切到其它节）。
    /// </summary>
    [RelayCommand] private void OpenSettings() => OpenTab("settings-general");

    // ==================== 标题栏搜索 ====================

    /// <summary>结果条数上限（下拉列表不撑爆弹窗；再多请把关键词写细）。</summary>
    private const int MaxSearchResults = 12;

    /// <summary>搜索结果（标题栏搜索框的下拉列表；数据源见 <see cref="BuildSearchIndex"/>）。</summary>
    public ObservableCollection<SearchTarget> SearchResults { get; } = new();

    /// <summary>搜索关键词（文本框双向绑定 ⇒ 每次变化即时刷新结果）。</summary>
    [ObservableProperty] private string _searchText = "";

    /// <summary>当前高亮的结果（↑↓ 改它、回车打开它；⛔ 不靠 ListBox 的焦点）。</summary>
    [ObservableProperty] private SearchTarget? _selectedSearchResult;

    /// <summary>是否有结果（无结果时列表不占位，只留一行提示）。</summary>
    [ObservableProperty] private bool _hasSearchResults;

    /// <summary>列表下方的一行提示（项数 / 未命中 / 用法）。⛔ 不加 `⚠` 前缀 —— 未命中不是错误。</summary>
    [ObservableProperty] private string _searchHint = "输入关键词：页面名 / 功能名 / 设置项";

    partial void OnSearchTextChanged(string value) => RefreshSearch(value);

    /// <summary>清空搜索状态（每次弹出搜索框时调 —— 免得上一次的残留在下次弹开时闪一下）。</summary>
    public void ResetSearch()
    {
        SearchText = "";
        SearchResults.Clear();
        SelectedSearchResult = null;
        HasSearchResults = false;
        SearchHint = "输入关键词：页面名 / 功能名 / 设置项";
    }

    /// <summary>
    /// 关键词变了 ⇒ 重建结果列表（**子串匹配**，标题命中优先于说明命中）。
    /// <para>🔴 范围 = **全部已注册的二级菜单项**（含各模块设置页本身）+ **应用级设置页的节**；
    /// ⛔ 不搜模块私有内容（资产名 / 脚本名），⛔ 不给拼音 / 首字母匹配。</para>
    /// </summary>
    private void RefreshSearch(string? keyword)
    {
        var kw = keyword?.Trim() ?? "";

        SearchResults.Clear();
        SelectedSearchResult = null;
        HasSearchResults = false;

        if (kw.Length == 0)
        {
            SearchHint = "输入关键词：页面名 / 功能名 / 设置项";
            return;
        }

        var hits = BuildSearchIndex()
            .Select(t => (Target: t, Score: t.MatchScore(kw)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Target.Title, StringComparer.CurrentCulture)
            .Take(MaxSearchResults)
            .Select(x => x.Target)
            .ToList();

        foreach (var h in hits) SearchResults.Add(h);
        HasSearchResults = hits.Count > 0;
        SelectedSearchResult = hits.Count > 0 ? hits[0] : null;
        SearchHint = hits.Count == 0
            ? $"没找到与「{kw}」匹配的页面或设置项"
            : $"共 {hits.Count} 项 · ↑↓ 选择 · 回车打开";
    }

    /// <summary>
    /// 构建搜索索引：① 菜单项（<see cref="IMenuService.Groups"/> 里的**已注册**项 ——
    /// ⛔ 不取 <c>MenuService</c> 的 `_pending` 暂存区，那些还没落进任何分组、导航过去也解析不到）；
    /// ② 应用级设置页的节（<see cref="SettingsViewModel.Catalog"/>）。
    /// <para>两者 Key 有重叠（`settings-general` …）⇒ 后者**覆盖**前者：用户看到的是节标题，不是菜单注册标题。</para>
    /// </summary>
    private List<SearchTarget> BuildSearchIndex()
    {
        var map = new Dictionary<string, SearchTarget>(StringComparer.Ordinal);

        // 首页不是菜单项（`HomeKey` 是常量、左树叶子单独加）⇒ 手工补一条，否则搜「首页」无结果
        map[HomeKey] = new SearchTarget
        {
            Key = HomeKey, Title = "首页", Detail = "收藏 / 一键操作 / 功能清单入口",
            KindLabel = "页面", Kind = SearchTargetKind.MenuItem,
        };

        foreach (var g in _menu.Groups)
        {
            foreach (var it in g.Items)
            {
                if (string.IsNullOrWhiteSpace(it.Key)) continue;
                map[it.Key] = new SearchTarget
                {
                    Key = it.Key,
                    Title = it.Title,
                    Detail = string.IsNullOrWhiteSpace(it.Desc)
                        ? g.Title
                        : g.IsHome ? it.Desc : $"{g.Title} · {it.Desc}",
                    KindLabel = "页面",
                    Kind = SearchTargetKind.MenuItem,
                };
            }
        }

        foreach (var m in SettingsViewModel.Catalog)
        {
            map[m.Key] = new SearchTarget
            {
                Key = m.Key,
                Title = m.Title,
                Detail = $"设置 · {m.Desc}",
                KindLabel = "设置项",
                Kind = SearchTargetKind.SettingsSection,
            };
        }

        return map.Values.ToList();
    }

    /// <summary>
    /// 打开一条搜索结果（回车 / 双击）：菜单项直接开页；设置节锚点由 <see cref="OpenTab"/> 归一成设置页
    /// 并**展开 + 滚动到该节**（⛔ 不新开标签页）。⛔ 不另设状态栏文案 —— 页面切换本身就是反馈，且当前页
    /// 的 <see cref="IStatusProvider"/> 状态会被盖掉。
    /// </summary>
    [RelayCommand]
    private void OpenSearchResult(SearchTarget? target)
    {
        if (target == null) return;
        OpenTab(target.Key);
    }

    /// <summary>↑↓ 移动高亮（由 <c>ShellView</c> 的 KeyDown 调；VM ⛔ 不监听键盘）。</summary>
    public void MoveSearchSelection(int delta)
    {
        if (SearchResults.Count == 0) return;
        var i = SelectedSearchResult == null ? -1 : SearchResults.IndexOf(SelectedSearchResult);
        SelectedSearchResult = SearchResults[Math.Clamp(i + delta, 0, SearchResults.Count - 1)];
    }

}
