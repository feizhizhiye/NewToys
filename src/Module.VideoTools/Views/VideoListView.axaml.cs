using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.VideoTools.ViewModels;

namespace NewToys.VideoTools.Views;

public partial class VideoListView : UserControl
{
    public VideoListView()
    {
        InitializeComponent();

        // 目录框回车 = 扫描（Avalonia 的 TextBox 没有 AcceptButton 之类的属性）
        FolderBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (DataContext is VideoListViewModel vm) vm.ScanCommand.Execute(null);
            e.Handled = true;
        };

        BrowseBtn.Click += OnBrowse;
        // 双击列表项 = 播放（和资源管理器习惯一致）
        VideoList.DoubleTapped += (_, _) =>
        {
            if (DataContext is VideoListViewModel vm) vm.PlayCommand.Execute(null);
        };
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VideoList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) return;

            var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择视频目录",
                AllowMultiple = false
            });

            var dir = picked?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (DataContext is not VideoListViewModel vm) return;

            vm.Folder = dir;
            vm.ScanCommand.Execute(null);
        }
        catch (Exception ex)
        {
            if (DataContext is VideoListViewModel vm) vm.StatusText = "选择目录失败：" + ex.Message;
        }
    }
}
