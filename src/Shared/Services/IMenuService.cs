using System.Collections.Generic;
using NewToys.Shared.Models;

namespace NewToys.Shared.Services;

public interface IMenuService
{
    IReadOnlyList<AppMenuGroup> Groups { get; }
    AppMenuGroup RegisterModule(string key, string title, string icon, int order, bool isBottom = false, bool isHome = false);
    /// <summary>注册一个二级功能。<paramref name="desc"/> 是一句话功能简介（功能清单页展示，首页收藏卡也复用它）。（WHY: SH-042）</summary>
    void RegisterFunction<TView>(string moduleKey, string key, string title, string icon,
                                 int order = 0, string desc = "", SettingsCategory category = SettingsCategory.None)
        where TView : class;
    AppMenuItem? FindItem(string key);
    AppMenuGroup? FindGroup(string key);
}