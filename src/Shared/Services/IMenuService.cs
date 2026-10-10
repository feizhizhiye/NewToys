using System.Collections.Generic;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

public interface IMenuService
{
    IReadOnlyList<MenuGroup> Groups { get; }
    MenuGroup RegisterModule(string key, string title, string icon, int order, bool isBottom = false, bool isHome = false);
    /// <summary>
    /// 注册一个二级功能。<paramref name="desc"/> 是一句话功能简介（功能清单页展示，首页收藏卡也复用它）。
    /// 追加在参数表末尾并带默认值，避免破坏既有调用。
    /// </summary>
    void RegisterFunction(string moduleKey, string key, string title, string icon, string viewName, int order = 0, string desc = "");
    MenuItem? FindItem(string key);
    MenuGroup? FindGroup(string key);
}