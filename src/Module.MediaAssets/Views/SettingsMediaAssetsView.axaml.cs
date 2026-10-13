using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「媒体资产」模块设置页 code-behind（NT-021）：只做 **View 才做得了的事**（选目录），业务逻辑全在 VM。
/// </summary>
public partial class SettingsMediaAssetsView : UserControl
{
    public SettingsMediaAssetsView() => InitializeComponent();

    /// <summary>「📂 选目录…」：选缩略图目录（⛔ 不建目录——生成缩略图时才建）。</summary>
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
}
