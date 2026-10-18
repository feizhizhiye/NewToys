using Avalonia.Controls;
using NewToys.UI.Settings;

namespace NewToys.VideoTools.Views;

/// <summary>
/// 「视频工具」模块设置页 code-behind（v3.64 / NT-061）：⛔ 无逻辑 —— 全部状态与落盘在 VM 里。
/// </summary>
public partial class SettingsVideoToolsView : UserControl
{
    public SettingsVideoToolsView()
    {
        InitializeComponent();
        Add("性能", "⚡", "批量处理并发数", "Sec0");
        Add("全局项（本模块复用）", "🔗", "ffmpeg / 播放器 / 导入导出目录", "Sec1");
        Add("设置文件", "📄", "配置落在哪 / 备份会覆盖它", "Sec2");
    }

    /// <summary>把一个"XAML 资源里的节内容"挂成设置节（节头样式/折叠/密度全由 UI 库骨架给）。</summary>
    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });
}
