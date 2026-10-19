using Avalonia.Controls;
using NewToys.UI.Settings;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 「脚本管理器」模块设置页 code-behind（v3.64 / NT-061）：⛔ 无逻辑 —— 选目录 / 选文件全走 VM 的
/// <c>DialogHooks</c> 命令（VM 不引 Avalonia，对话框由 Shell 注入）。
/// </summary>
public partial class SettingsScriptView : UserControl
{
    public SettingsScriptView()
    {
        InitializeComponent();
        Add("脚本目录", "📂", "脚本库扫描的根目录（脚本库页同一份值）", "Sec0");
        Add("软件源（winget）", "📦", "源归系统 winget + 命令库管：这里只列命令，一键写进命令库执行", "Sec1");
        Add("设置文件", "📄", "配置落在哪 / 解释器路径在哪配", "Sec2");
    }

    /// <summary>把一个"XAML 资源里的节内容"挂成设置节（节头样式/折叠/密度全由 UI 库骨架给）。</summary>
    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });
}
