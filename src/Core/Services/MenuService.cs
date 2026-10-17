using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class MenuService : IMenuService
{
    private readonly List<AppMenuGroup> _groups = new();

    /// <summary>跨组注册的暂存区（v3.20，需求 §3.4）：RegisterFunction 时目标分组还没建立 → 先存这，
    /// 等 RegisterModule 建立该分组时补登。⛔ 不抛异常——InitializeAll 的逐模块 try-catch 会吞掉异常，
    /// Release 目录扫描顺序不保证 → 菜单项静默丢失。</summary>
    private readonly List<AppMenuItem> _pending = new();

    public IReadOnlyList<AppMenuGroup> Groups
    {
        get
        {
            lock (_groups)
            {
                return _groups
                    .OrderBy(g => g.IsBottom ? 1 : 0)
                    .ThenBy(g => g.Order)
                    .ToList()
                    .AsReadOnly();
            }
        }
    }

    public AppMenuGroup RegisterModule(string key, string title, string icon, int order,
                                       bool isBottom = false, bool isHome = false)
    {
        lock (_groups)
        {
            var existing = _groups.FirstOrDefault(g => g.Key == key);
            if (existing != null)
            {
                existing.Title = title;
                existing.Icon = icon;
                existing.Order = order;
                existing.IsBottom = isBottom;
                existing.IsHome = isHome;
            }
            else
            {
                existing = new AppMenuGroup
                {
                    Key = key,
                    Title = title,
                    Icon = icon,
                    Order = order,
                    IsBottom = isBottom,
                    IsHome = isHome
                };
                _groups.Add(existing);
            }

            // 补登此前暂存的跨组菜单项（RegisterFunction 时本分组还不存在）
            FlushPending(existing);
            return existing;
        }
    }

    public void RegisterFunction(string moduleKey, string key, string title, string icon,
                                  string viewName, int order = 0, string desc = "",
                                  SettingsCategory category = SettingsCategory.None)
    {
        lock (_groups)
        {
            var g = _groups.FirstOrDefault(x => x.Key == moduleKey);
            if (g == null)
            {
                // 跨组注册：分组尚未注册（典型 = 功能模块的设置页进 settings 组，但 Shell 的
                // AppSettingsRegistration 尚未执行）→ 暂存，待 RegisterModule 建立该分组时补登。
                _pending.Add(new AppMenuItem
                {
                    Key = key,
                    GroupKey = moduleKey,
                    Title = title,
                    Desc = desc,
                    Icon = icon,
                    ViewTypeName = viewName,
                    Category = category,
                    Order = order
                });
                return;
            }

            AddOrUpdateItem(g, new AppMenuItem
            {
                Key = key,
                GroupKey = moduleKey,
                Title = title,
                Desc = desc,
                Icon = icon,
                ViewTypeName = viewName,
                Category = category,
                Order = order
            });

            var sorted = g.Items.OrderBy(i => i.Order).ToList();
            g.Items.Clear();
            foreach (var it in sorted) g.Items.Add(it);
        }
    }

    /// <summary>往分组里加 / 更新一个菜单项（同 Key = 更新；简介为空不覆盖已有值）。锁内调用。</summary>
    private static void AddOrUpdateItem(AppMenuGroup g, AppMenuItem item)
    {
        var existing = g.Items.FirstOrDefault(i => i.Key == item.Key);
        if (existing != null)
        {
            existing.Title = item.Title;
            existing.Icon = item.Icon;
            existing.ViewTypeName = item.ViewTypeName;
            existing.Order = item.Order;
            existing.GroupKey = item.GroupKey;
            // 分类：显式给了就覆盖（None 视为"没给"，允许"只改标题不重写分类"的局部更新）
            if (item.Category != SettingsCategory.None) existing.Category = item.Category;
            // 简介为空时不覆盖已有值（允许"只改标题不重写简介"的局部更新）
            if (!string.IsNullOrEmpty(item.Desc)) existing.Desc = item.Desc;
            return;
        }
        g.Items.Add(item);
    }

    /// <summary>把暂存区里属于 <paramref name="g"/> 的菜单项补登进去，并按 Order 重排。锁内调用。</summary>
    private void FlushPending(AppMenuGroup g)
    {
        if (_pending.Count == 0) return;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_pending[i].GroupKey, g.Key, StringComparison.Ordinal)) continue;
            AddOrUpdateItem(g, _pending[i]);
            _pending.RemoveAt(i);
        }
        if (g.Items.Count == 0) return;
        var sorted = g.Items.OrderBy(i => i.Order).ToList();
        g.Items.Clear();
        foreach (var it in sorted) g.Items.Add(it);
    }

    public AppMenuItem? FindItem(string key)
    {
        foreach (var g in Groups)
        {
            var it = g.Items.FirstOrDefault(i => i.Key == key);
            if (it != null) return it;
        }
        return null;
    }

    /// <summary>暂存区里还没补登的跨组菜单项数量（v3.26 启动健全性检查用：&gt;0 = 有分组始终没被建立）。</summary>
    public int PendingCount
    {
        get { lock (_groups) return _pending.Count; }
    }

    public AppMenuGroup? FindGroup(string key) => Groups.FirstOrDefault(g => g.Key == key);
}