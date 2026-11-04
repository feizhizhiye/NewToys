using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using NewToys.UI.Settings;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「媒体资产」模块设置页 code-behind：只做 **View 才做得了的事**（选目录），业务逻辑全在 VM。
/// </summary>
public partial class SettingsMediaAssetsView : UserControl
{
    public SettingsMediaAssetsView()
    {
        InitializeComponent();
        Add("扫描", "🔍", "忽略规则 / 快速哈希采样", "Sec0");
        Add("元数据与缩略图", "🖼", "自动提取 / 抽帧位置 / 缩略图目录", "Sec1");
        Add("命名模板与冲突策略", "✏", "重命名模板与重名处理", "Sec2");
        Add("回收站", "🗑", "保留天数与自动清理", "Sec3");
        Add("物理操作", "🛠", "删除是否进系统回收站 / 二次确认", "Sec4");
        Add("导入导出", "📤", "默认目录 / 用户手动数据", "Sec5");
        Add("设置文件", "📄", "配置落在哪 / 本轮未做的部分", "Sec6");
    }

    /// <summary>把一个"XAML 资源里的节内容"挂成设置节（节头样式/折叠/密度全由 UI 库骨架给）。</summary>
    private void Add(string title, string icon, string desc, string key)
        => Host.Sections.Add(new SettingsSection
        {
            Title = title, Icon = icon, Desc = desc, IsExpanded = true,
            Content = (Control)Resources[key]!,
        });

    /// <summary>「选目录…」：选缩略图目录（⛔ 不建目录——生成缩略图时才建）。</summary>
    private async void OnPickThumbsDirClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsMediaAssetsViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持目录选择器。"; return; }

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择缩略图目录",
            AllowMultiple = false
        }).ConfigureAwait(false);

        var path = folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path)) return;
        vm.SetThumbsDirectory(path);
    }

    /// <summary>「恢复用户数据…」：选一个导出的 json → 合并恢复（VM 做合并，这里只选文件）。</summary>
    private async void OnRestoreUserDataClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsMediaAssetsViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要恢复的用户数据 json",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("用户数据 JSON") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
            }
        }).ConfigureAwait(false);

        var src = files?.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(src)) return;

        try
        {
            var (ok, msg) = vm.RestoreUserData(File.ReadAllText(src));
            vm.StatusText = msg;
        }
        catch (Exception ex) { vm.StatusText = "恢复失败：" + ex.Message; }
    }
}
