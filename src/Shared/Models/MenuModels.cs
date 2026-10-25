using System.Collections.ObjectModel;

namespace NewToys.Shared.Models;

// ⚠️ 类型一律带 App 前缀（AppMenuGroup / AppMenuItem）：Avalonia.Controls 已有 MenuItem / Menu 等同名类型，
//    同时 using 两者时裸写会 CS0104 不明确引用。新增模型同样避开框架已有类型名（Grid / Border / Panel / ContextMenu…）。

/// <summary>一级菜单（模块分组）：一个模块注册一个，内含若干 <see cref="AppMenuItem"/>。</summary>
public class AppMenuGroup
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public int Order { get; set; } = 100;
    public bool IsBottom { get; set; } = false;
    public bool IsHome { get; set; } = false;
    public ObservableCollection<AppMenuItem> Items { get; set; } = new();
}

/// <summary>二级菜单项（模块注册的一个功能页）。</summary>
public class AppMenuItem
{
    public string Key { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>功能简介（一句话说明这个页面做什么）。功能清单页展示它；不要在这里写实现细节。</summary>
    public string Desc { get; set; } = "";
    public string Icon { get; set; } = "";
    public string ViewTypeName { get; set; } = "";
    public string ViewModelTypeName { get; set; } = "";
    /// <summary>本项所属的显式分类（v3.65，需求 §4.9.2）。默认 <see cref="SettingsCategory.None"/>。</summary>
    public SettingsCategory Category { get; set; } = SettingsCategory.None;
    /// <summary>组内排序（v3.65 起**只作排序**；"框架 / 模块"的判定改看 <see cref="Category"/>）。</summary>
    public int Order { get; set; }
}
