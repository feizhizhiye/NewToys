using System.Collections.ObjectModel;

namespace NewToys.Shared.Models;

// ⚠️ 命名约定：这里的类型一律带 App 前缀（AppMenuGroup / AppMenuItem）。
//    原因：这是**本应用注册的菜单模型**（模块 ↔ 二级功能），而 Avalonia.Controls 里已有
//    同名的 MenuItem（以及 Menu / NativeMenu 等控件类型）。凡同时 using 两者（Avalonia.Controls
//    + NewToys.Shared.Models）的 code-behind，裸写 MenuItem 会 CS0104 不明确引用（v1.32 踩过一次）。
//    加前缀后：① 编译期不再撞名，也不必再写 using 别名；② 读代码时一眼分清「框架控件」与「应用模型」。
//    新增模型时同样避免使用框架已有的类型名（如 Grid/Border/Panel/Separator/ContextMenu…）。

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
    public int Order { get; set; }
}
