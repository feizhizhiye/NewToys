using Avalonia.Controls;
using NewToys.UI.Settings;

namespace NewToys.BackupCenter.Views;

/// <summary>「备份中心设置」页（`settings-backup`）。</summary>
public partial class SettingsBackupView : UserControl
{
    public SettingsBackupView()
    {
        InitializeComponent();
        Add("主密码", "🔐", "密码库的设置 / 解锁 / 更换", "Sec1");
        Add("保留与目录", "📦", "默认保留份数、默认备份目录", "Sec2");
        Add("邮件投递", "📧", "邮箱账号 / 分卷大小 / 待发目录", "SecMail");
        Add("文件位置", "📄", "设置文件 / 数据库 / 暂存目录", "Sec3");
    }

    /// <summary>把 XAML 资源里的节内容挂成设置节（折叠 / 磁吸 / 密度由骨架提供）。</summary>
    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });
}
