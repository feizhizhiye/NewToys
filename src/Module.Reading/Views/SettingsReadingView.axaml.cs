using Avalonia.Controls;
using NewToys.UI.Settings;

namespace NewToys.Reading.Views;

/// <summary>「网络阅读」模块设置页 code-behind：⛔ 无逻辑，状态与落盘全在 VM。</summary>
public partial class SettingsReadingView : UserControl
{
    public SettingsReadingView()
    {
        InitializeComponent();
        Add("抓取", "🌐", "并发 / 超时 / UA / 正文缓存 / 预下载", "Sec0");
        Add("阅读器", "📖", "字号 / 行距 / 字体 / 宽度 / 配色 / 段落 / 进度", "Sec1");
        Add("听书", "🎧", "朗读语速 / 音量 / 音色（用系统语音包，入口在阅读页顶栏）", "Sec5");
        Add("替换规则", "🧽", "全局正则净化：清广告 / 站点口号（对任何书源的正文都生效）", "Sec4");
        Add("样式方案", "🎨", "内置预设 / 导入导出一份样式文件（与阅读页同一套）", "Sec2");
        Add("设置文件", "📄", "配置落在哪 / 备份会覆盖它", "Sec3");
    }

    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });
}
