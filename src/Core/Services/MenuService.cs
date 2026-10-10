using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NewToys.Shared.Models;
using NewToys.Shared.Services;

namespace NewToys.Core.Services;

public class MenuService : IMenuService
{
    private readonly List<MenuGroup> _groups = new();

    public IReadOnlyList<MenuGroup> Groups
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

    public MenuGroup RegisterModule(string key, string title, string icon, int order,
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
                return existing;
            }
            var g = new MenuGroup
            {
                Key = key,
                Title = title,
                Icon = icon,
                Order = order,
                IsBottom = isBottom,
                IsHome = isHome
            };
            _groups.Add(g);
            return g;
        }
    }

    public void RegisterFunction(string moduleKey, string key, string title, string icon,
                                  string viewName, int order = 0, string desc = "")
    {
        MenuGroup? g;
        lock (_groups)
        {
            g = _groups.FirstOrDefault(x => x.Key == moduleKey);
        }
        if (g == null) throw new InvalidOperationException($"模块 {moduleKey} 未注册");
        var existing = g.Items.FirstOrDefault(i => i.Key == key);
        if (existing != null)
        {
            existing.Title = title;
            existing.Icon = icon;
            existing.ViewTypeName = viewName;
            existing.Order = order;
            existing.GroupKey = moduleKey;
            // 简介为空时不覆盖已有值（允许"只改标题不重写简介"的局部更新）
            if (!string.IsNullOrEmpty(desc)) existing.Desc = desc;
            return;
        }
        g.Items.Add(new MenuItem
        {
            Key = key,
            GroupKey = moduleKey,
            Title = title,
            Desc = desc,
            Icon = icon,
            ViewTypeName = viewName,
            Order = order
        });
        var sorted = g.Items.OrderBy(i => i.Order).ToList();
        g.Items.Clear();
        foreach (var it in sorted) g.Items.Add(it);
    }

    public MenuItem? FindItem(string key)
    {
        foreach (var g in Groups)
        {
            var it = g.Items.FirstOrDefault(i => i.Key == key);
            if (it != null) return it;
        }
        return null;
    }

    public MenuGroup? FindGroup(string key) => Groups.FirstOrDefault(g => g.Key == key);
}