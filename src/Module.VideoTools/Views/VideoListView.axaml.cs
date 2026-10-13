using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.Shared.Models;
using NewToys.VideoTools.Services;
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

        // §6.4.7：详情卡「文件名」**回车 / 失焦即提交**（详情卡内不放按钮）。
        // ⚠️ 命令里自带"名字没变"的短路，所以"切换选中项时顺带触发一次失焦"只会空转，不会误改磁盘文件。
        // v3.21：标签**不再手打**（改成点色片选择）→ 这里的 TagsBox 提交逻辑整体删除。
        NameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitName(); e.Handled = true; } };
        NameBox.LostFocus += (_, _) => CommitName();

        ImportSplit.Click += OnBrowseFolder;
        ImportFolderItem.Click += OnBrowseFolder;
        ImportFilesItem.Click += OnPickVideoFiles;
        // v3.16 §6.4.3：视频库导入从「📂 导入」的下拉里独立出来，改成自己的按钮（点即选，默认定位到默认目录）
        ImportCatalogBtn.Click += OnPickCatalogDb;
        // 双击列表项 = 播放（和资源管理器习惯一致）
        VideoList.DoubleTapped += (_, _) =>
        {
            if (DataContext is VideoListViewModel vm) vm.PlayCommand.Execute(null);
        };

        // v3.20 §6.4.15：拖动表头列边界 → VM 改「手动列宽」→ 表头与所有行同规格重建
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is ListPageViewModelBase vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };
    }

    /// <summary>提交详情卡里改过的**文件名**（改名走 <c>VideoFileService.RenameToAsync</c>）。</summary>
    private void CommitName()
    {
        if (DataContext is VideoListViewModel vm) vm.SaveNameCommand.Execute(null);
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
        // v3.16 §6.4.10：表头勾选框的「全选 / 取消选择 / 反选」菜单同理（不同步的话右键点了没反应）。
        // v3.16.1：菜单挪到 HeaderCheckHost（透明 Border = 整格热区）上。
        if (HeaderCheckHost?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;
    }

    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
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

    /// <summary>📑 读取视频库：挑一个 <c>.db</c> 文件，把里面的 video_catalog 记录读回当前列表（需求 §6.4.3）。</summary>
    private async void OnPickCatalogDb(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) return;
            if (DataContext is not VideoListViewModel vm) return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择视频库文件（.db）",
                AllowMultiple = false,
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("视频库（SQLite）") { Patterns = new[] { "*.db" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            if (files == null || files.Count == 0) { vm.StatusText = "没选文件，导入视频库已取消。"; return; }

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                // 挑到了但拿不到本地路径（常见于"库"之类虚拟位置）→ ⛔ 不许静默返回，否则表现为"点了没反应"
                vm.StatusText = "读不出这个文件的本地路径（它不在本地磁盘上？）—— 换一个 .db 文件试试。";
                return;
            }
            vm.LoadCatalog(path);
        }
        catch (Exception ex)
        {
            if (DataContext is VideoListViewModel vm) vm.StatusText = "读取视频库失败：" + ex.Message;
        }
    }

    /// <summary>📂 导入 → 视频文件…：多选单个视频文件，直接建列表条目追加进当前列表（不扫描整目录）。</summary>
    private async void OnPickVideoFiles(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) return;
            if (DataContext is not VideoListViewModel vm) return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择视频文件（可多选）",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("视频文件") { Patterns = VideoScanner.Extensions },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            if (files == null || files.Count == 0) return;
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();
            if (paths.Count == 0) return;
            vm.ImportVideoFiles(paths);
        }
        catch (Exception ex)
        {
            if (DataContext is VideoListViewModel vm) vm.StatusText = "选择视频文件失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 把"导入/导出默认目录"（v3.15 全局）解析成 <see cref="IStorageFolder"/>，作为文件选择器的 <see cref="FilePickerOpenOptions.SuggestedStartLocation"/>。
    /// 目录为空 / 解析失败 → 返回 <c>null</c>（选择器回退到系统默认）。
    /// </summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }
}
