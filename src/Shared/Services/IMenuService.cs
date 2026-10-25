using System.Collections.Generic;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

public interface IMenuService
{
    IReadOnlyList<AppMenuGroup> Groups { get; }
    AppMenuGroup RegisterModule(string key, string title, string icon, int order, bool isBottom = false, bool isHome = false);
    /// <summary>
    /// 注册一个二级功能。<paramref name="desc"/> 是一句话功能简介（功能清单页展示，首页收藏卡也复用它）。
    /// <para>
    /// <paramref name="category"/> = **显式分类**（需求 §4.9.2）：设置项必须标；
    /// 普通功能项留默认 <c>None</c>。此后 <paramref name="order"/> **只作组内排序**。
    /// </para>
    /// <para>
    /// 视图类型由 <typeparamref name="TView"/> 决定（取 <c>typeof(TView).Name</c>）—— ⛔ 不接受字符串：
    /// View 类改名时编译器强制本处跟随，杜绝「注册名与 View 类名不一致 ⇒ 菜单项永远打不开」。
    /// </para>
    /// </summary>
    void RegisterFunction<TView>(string moduleKey, string key, string title, string icon,
                                 int order = 0, string desc = "", SettingsCategory category = SettingsCategory.None)
        where TView : class;
    AppMenuItem? FindItem(string key);
    AppMenuGroup? FindGroup(string key);
}