using System.Collections.ObjectModel;

namespace NewToys.Shared.Models;

public class MenuGroup
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "📁";
    public int Order { get; set; } = 100;
    public bool IsBottom { get; set; } = false;
    public bool IsHome { get; set; } = false;
    public ObservableCollection<MenuItem> Items { get; set; } = new();
}

public class MenuItem
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