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

    /// <summary>
    /// 状态栏 `⚠` 那一行**可点击跳转**（需求 §4.9.2）：目标 Key 由当前页的
    /// <see cref="IStatusActionProvider"/> 给（典型 = 外部程序缺失 → 跳到「运行环境」节）。
    /// </summary>
    [RelayCommand]
    private void InvokeStatusAction()
    {
        var key = _binder.StatusActionKey;
        if (!string.IsNullOrWhiteSpace(key)) OpenTab(key);
    }

    /// <summary>
    /// 折叠 / 展开左树：
    /// **折叠时顺手把全部一级分组也收起**（用户要求"折叠菜单栏时自动折叠全部二级菜单"），并落盘。
    /// </summary>
    [RelayCommand]
    private void ToggleTree()
    {
        IsTreeCollapsed = !IsTreeCollapsed;

        if (IsTreeCollapsed)
        {
            // 折叠时把全部一级分组也收起（"折叠菜单栏时自动折叠全部二级菜单"），并落盘
            foreach (var node in TreeNodes)
            {
                if (!node.IsGroup || !node.IsExpanded) continue;
                node.IsExpanded = false;
                _cols.SaveFlag(ShellPageKey, "treeGroup:" + node.Key, false);
            }
            return;
        }

        // 展开时确保当前页所属分组是展开的（不收起其它）
        EnsureActiveGroupExpanded(ActiveTab);
    }

    /// <summary>标签页滚轮切换：切到下一个 / 上一个（**到头即止，不循环**）。</summary>
    [RelayCommand] private void NextTab() => StepTab(1);
    [RelayCommand] private void PrevTab() => StepTab(-1);

    private void StepTab(int delta)
    {
        if (Tabs.Count < 2) return;
        var i = ActiveTab == null ? 0 : Tabs.IndexOf(ActiveTab);
        if (i < 0) i = 0;
        var n = Math.Clamp(i + delta, 0, Tabs.Count - 1);
        if (n == i) return;
        ActiveTab = Tabs[n];
    }

    /// <summary>右详情开合（供标题栏/快捷键调用；列表页表头 ▤ 直接调 VM 自己的契约方法）。</summary>
    [RelayCommand] private void ToggleDetail() => _binder.ToggleDetail();

    /// <summary>左树节点点击：一级分组折叠/展开（不打开页）；叶子打开/激活标签页。</summary>
    [RelayCommand]
    private void TreeClick(TreeMenuItem? node)
    {
        if (node == null) return;

        if (node.IsGroup)
        {
            node.IsExpanded = !node.IsExpanded;
            _cols.SaveFlag(ShellPageKey, "treeGroup:" + node.Key, node.IsExpanded);   // v3.6：手动展开态落盘
            // 折叠态下点分组 → 先展开整棵树（简化实现，不做浮出面板）
            if (IsTreeCollapsed) IsTreeCollapsed = false;
            // 展开后通知 ShellView 把整组滚入视野（靠底部的分组展开后子项会被 ScrollViewer 裁掉）
            if (node.IsExpanded) TreeNodeExpanded?.Invoke(this, node);
            return;
        }

        OpenTab(node.Key);
    }

    /// <summary>模块热加载完成（入口已由标题栏移入功能清单页「增加功能」操作区）： 重建左树 + 底栏消息（文案与旧标题栏按钮逐字一致）。（WHY: SHL-046）</summary>
    private void OnModulesReloaded((int Added, int Skipped, int Failed) result)
    {
        BuildTree();   // 左树重建：新增 / 移除的模块菜单反映出来

        var (added, skipped, failed) = result;
        var msg = failed > 0
            ? $"模块重载：新增 {added} / 跳过 {skipped} / 失败 {failed}（失败不影响使用，详情见 Debug 输出）"
            : $"模块重载：新增 {added} / 跳过 {skipped} / 失败 0。";
        StatusText = msg;
        // 壳级 StatusText 没有 UI 消费 → 直接写底栏（瞬态；切标签页会被页面状态覆盖，符合预期）
        StatusBarText = msg;
    }

    /// <summary>设置页切节后：重绑由绑定器完成，这里只补左树高亮与分组展开。</summary>
    internal void AfterSettingsSectionRebound(TabItemViewModel tab)
    {
        UpdateTreeActive();
        EnsureActiveGroupExpanded(tab);
    }

    // ==================== 左树构建 / 高亮 ====================

    /// <summary>左树构建 / 高亮（public：热加载重扫后要能重建）。</summary>
    public void BuildTree()
    {
        TreeNodes.Clear();

        // 首页：与一级分组平级的叶子节点
        TreeNodes.Add(new TreeMenuItem
        {
            Key = HomeKey, Title = "首页", Icon = "🏠", VectorIcon = ResolveIcon("IconHome"), IsGroup = false
        });

        // 一级分组（含设置组，排在末尾 —— MenuService.Groups 已按 IsBottom/Order 排好）
        foreach (var g in _menu.Groups.Where(x => !x.IsHome))
        {
            var node = new TreeMenuItem
            {
                Key = g.Key,
                Title = g.Title,
                Icon = g.Icon,
                VectorIcon = ResolveIcon(g.Icon),   // v3.2：一级分组 icon = Geometry 资源键
                IsGroup = true,
                // 展开态恢复落盘（它是**用户状态**，不是派生状态）——首次运行默认不展开
                IsExpanded = _cols.LoadFlag(ShellPageKey, "treeGroup:" + g.Key, false)
            };
            foreach (var it in g.Items)
                node.Children.Add(new TreeMenuItem { Key = it.Key, Title = it.Title, Icon = it.Icon, IsGroup = false });
            TreeNodes.Add(node);
        }
    }

    private void UpdateTreeActive()
    {
        var key = ActiveTreeKey;   // v3.65：设置页 → 当前节锚点（左树子项是节，不是页 Key）
        foreach (var node in TreeNodes)
        {
            // 二级：命中当前标签页
            foreach (var child in node.Children)
                child.IsActive = string.Equals(child.Key, key, StringComparison.Ordinal);

            // 一级：首页等叶子看自身；分组则**任一子项命中时也高亮**
            node.IsActive = node.IsGroup
                ? node.Children.Any(c => c.IsActive)
                : string.Equals(node.Key, key, StringComparison.Ordinal);
        }
    }

    /// <summary>自动导航：切标签时确保当前页所属的一级分组是展开的，。（WHY: SHL-047）</summary>
    private void EnsureActiveGroupExpanded(TabItemViewModel? tab)
    {
        if (IsTreeCollapsed) return;

        var key = ActiveTreeKey;   // v3.65：设置页按**当前节锚点**找所属分组
        if (string.IsNullOrEmpty(key)) return;

        foreach (var node in TreeNodes)
        {
            if (!node.IsGroup || node.IsExpanded) continue;   // 已展开的不动
            if (!node.Children.Any(c => string.Equals(c.Key, key, StringComparison.Ordinal))) continue;

            node.IsExpanded = true;
            _cols.SaveFlag(ShellPageKey, "treeGroup:" + node.Key, true);
        }
    }

    /// <summary>把菜单里的图标键解析成 Geometry。（WHY: SHL-048）</summary>
    private static Geometry? ResolveIcon(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        try
        {
            var app = Avalonia.Application.Current;
            if (app != null && app.TryFindResource(key, out var value) && value is Geometry g)
                return g;
        }
        catch { /* 资源链异常不该影响界面 */ }
        return null;
    }

    // ==================== 视图解析（沿用 v1.35，改「为新标签页解析一次并缓存」） ====================

}
