using Avalonia.Controls;
using NewToys.UI.Settings;

namespace NewToys.SystemTools.Views;

/// <summary>「系统工具设置」页（`settings-system`，）。</summary>
public partial class SettingsSystemView : UserControl
{
    public SettingsSystemView()
    {
        InitializeComponent();
        Add("执行", "⚙", "命令怎么跑（超时）", "Sec0");
        Add("数据文件", "📄", "本模块的配置与清单落在哪", "Sec1");
    }

    /// <summary>把「XAML 资源里的节内容」挂成设置节（骨架给样式 / 折叠 / 密度）。</summary>
    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });
}
