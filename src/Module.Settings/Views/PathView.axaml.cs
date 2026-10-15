using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.Settings.Models;
using NewToys.Settings.ViewModels;

namespace NewToys.Settings.Views;

public partial class PathView : UserControl
{
    public PathView()
    {
        InitializeComponent();
        // ⚠️ NT-054：原来那个页内卡里的「📂 浏览…」按钮（BrowseDetailBtn）随卡片一起删了 ——
        //    现在它是声明式卡的 Actions 项，命令由本页在 OnDataContextChanged 里注入（见下）。
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PathList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;

        // NT-054：把「📂 浏览…」的真实实现（要 TopLevel / StorageProvider）注入给声明式卡的动作。
        // 每次 DataContext 变化重挂一次，幂等 —— 命令内部只认最新 VM。
        if (DataContext is PathViewModel vm)
            vm.PickPathCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => _ = BrowseForSelectedAsync());
    }

    /// <summary>行内「📂 浏览」：拿这一行的项去选路径。</summary>
    private void OnBrowseRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not PathSettingItem item) return;
        _ = BrowseAsync(item);
    }

    /// <summary>右键「📂 浏览…」：作用于当前选中项。</summary>
    private void OnBrowseMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PathViewModel vm) _ = BrowseAsync(vm.Selected);
    }

    private async System.Threading.Tasks.Task BrowseForSelectedAsync()
    {
        if (DataContext is PathViewModel vm) await BrowseAsync(vm.Selected);
    }

    /// <summary>
    /// 选文件 / 选目录（按项的 <see cref="PathItemKind"/> 决定用哪个选择器），选完**立即生效**并落盘。
    /// </summary>
    private async System.Threading.Tasks.Task BrowseAsync(PathSettingItem? item)
    {
        if (item == null || item.IsReadOnly) return;
        if (DataContext is not PathViewModel vm) return;

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            if (item.Kind == PathItemKind.Folder)
            {
                var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = $"选择{item.Name}",
                    SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                    AllowMultiple = false
                });
                var dir = folders?.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(dir)) vm.ApplyPickedValue(item, dir);
                return;
            }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"选择{item.Name}",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("程序 / 可执行文件") { Patterns = new[] { "*.exe" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });
            var picked = files?.FirstOrDefault();
            if (picked != null)
            {
                var path = picked.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path)) vm.ApplyPickedValue(item, path);
            }
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择路径失败：" + ex.Message;
        }
    }

    /// <summary>把文件选择器定位到「导入导出默认目录」（v3.19 / NT-006 全页统一）。</summary>
    private static async System.Threading.Tasks.Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }
}
