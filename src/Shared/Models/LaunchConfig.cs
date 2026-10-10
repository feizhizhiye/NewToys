using System.Collections.Generic;

namespace NewToys.Shared.Models;

public class LaunchButtonConfig
{
    public string Name { get; set; } = "";
    public string ProgramPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Urls { get; set; } = "";
    public int IconColor { get; set; } = 0; // 0=蓝绿 1=青绿 2=橙
}

public class LaunchConfig
{
    public bool StartWithSystem { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool CheckUpdateOnStartup { get; set; } = true;

    /// <summary>点窗口关闭按钮时最小化到托盘（而不是退出）。</summary>
    public bool CloseToTray { get; set; } = false;
    public LaunchButtonConfig Browser1 { get; set; } = new()
    {
        Name = "浏览器1 批量开网址",
        Urls = "",
        ProgramPath = "",
        Arguments = "",
        IconColor = 0
    };
    public LaunchButtonConfig Browser2 { get; set; } = new()
    {
        Name = "浏览器2 批量开网址",
        Urls = "",
        ProgramPath = "",
        Arguments = "",
        IconColor = 1
    };
    public List<LaunchButtonConfig> Programs { get; set; } = new()
    {
        new LaunchButtonConfig { Name = "批量打开软件", ProgramPath = "", Arguments = "", IconColor = 2 }
    };
}