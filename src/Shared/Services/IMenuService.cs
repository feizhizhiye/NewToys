using System.Collections.Generic;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

public interface IMenuService
{
    IReadOnlyList<AppMenuGroup> Groups { get; }
    AppMenuGroup RegisterModule(string key, string title, string icon, int order, bool isBottom = false, bool isHome = false);
    /// <summary>
    /// 注册一个二级功能。<paramref name="desc"/> 是一句话功能简介（功能清单页展示，首页收藏卡也复用它）。
    /// 追加在参数表末尾并带默认值，避免破坏既有调用。
    /// <para>
    /// <paramref name="category"/> = **显式分类**（v3.65 / NT-062，需求 §4.9.2）：设置项必须标；
    /// 普通功能项留默认 <c>None</c>。此后 <paramref name="order"/> **只作组内排序**。
    /// </para>
    /// </summary>
    void RegisterFunction(string moduleKey, string key, string title, string icon, string viewName,
                          int order = 0, string desc = "", SettingsCategory category = SettingsCategory.None);
    AppMenuItem? FindItem(string key);
    AppMenuGroup? FindGroup(string key);
}