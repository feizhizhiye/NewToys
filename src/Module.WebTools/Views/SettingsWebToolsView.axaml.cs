using Avalonia.Controls;
using NewToys.UI.Settings;

namespace NewToys.WebTools.Views;

/// <summary>
/// 「网页工具」模块设置页 code-behind（v3.64）：⛔ 无逻辑 —— 全部状态与落盘在 VM 里。
/// </summary>
public partial class SettingsWebToolsView : UserControl
{
    public SettingsWebToolsView()
    {
        InitializeComponent();
        Add("网页抓取", "🌐", "默认 UA 与单次抓取超时", "Sec0");
        Add("Hosts 检测与备份", "🗂", "检测超时 / 并发 / 备份保留份数 / 应用后是否刷新 DNS", "Sec2");
        Add("设置文件与其它功能", "📄", "配置落在哪 / 哪些数据不在本文件里", "Sec1");
    }

    /// <summary>把一个"XAML 资源里的节内容"挂成设置节（节头样式/折叠/密度全由 UI 库骨架给）。</summary>
    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });
}
