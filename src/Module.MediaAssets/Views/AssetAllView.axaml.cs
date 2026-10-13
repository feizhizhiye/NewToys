using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「全部资产」页的 code-behind：只做**四件 View 才做得了的事**
/// （弹选择器 / 转发表头拖拽事件 / 给 ContextMenu 补 DataContext / 取跨页投递的件），业务逻辑一律在 VM。
/// </summary>
public partial class AssetAllView : UserControl
{
    public AssetAllView()
    {
        InitializeComponent();

        // ① 「📁 扫描」SplitButton：主按钮直接点 = 选文件夹；下拉 = 选文件夹 / 选文件。
        //    v3.30（用户定调）：原先「📁 扫描文件夹」+「📄 扫描文件」两个独立按钮**合并**成这一条，
        //    样式与接线都对齐 VideoTools 的「📂 导入」。
        //    两条路都是**预览**（只进内存、⛔ 不入库）：看完决定「💾 加入库」还是「🧹 清除列表」换目录重来。
        //    用 `_ =` 显式丢弃返回的 Task：事件处理器是 void 语义，这里就是"点火即忘"（CS4014 的规范写法）。
        ScanSplit.Click += (_, _) => _ = ScanFolderAsync();
        ScanFolderItem.Click += (_, _) => _ = ScanFolderAsync();
        ScanFilesItem.Click += (_, _) => _ = ScanFilesAsync();

        // ② NT-026：「🗄 并入库文件…」主按钮 = 选一个外部 .db 并入当前库（预览 + 确认在 VM 里做）。
        MergeSplit.Click += (_, _) => _ = PickMergeDbAsync();

        // ③ 「➕ 登记文件夹」：加入待扫列表（⛔ 不扫描，等「🔄 扫描全部」）
        BtnRegisterFolder.Click += async (_, _) =>
        {
            if (DataContext is not AssetAllViewModel vm) return;
            var dir = await PickFolderAsync(vm).ConfigureAwait(false);
            if (dir == null) return;
            vm.RegisterFolder(dir);
        };

        // ④ 路径输入框回车 = 预览扫描（省得再去点按钮）
        ImportPathBox.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            if (DataContext is AssetAllViewModel vm) vm.PreviewPathCommand.Execute(null);
            e.Handled = true;
        };

        // ⑤ 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // ⑥ 表头首次测量完 → 让 VM 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };

        // ⑦ v3.34 / NT-018：表头 ☑ 上的右键菜单（全选 / 取消选择 / 反选）
        //    ⚠️ 必须是**原生三态 CheckBox**（⛔ 别用字符"☑"画 —— 字体会渲染成深色块，v3.16 连踩两轮）；
        //    ⚠️ ContextMenu 挂在 **CheckBox** 上（不是外层 Border）→ 这里要读 HeaderCheckBox，
        //       整格热区由外层透明 Border 的右键手动开菜单。
        HeaderCheckHost.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(HeaderCheckHost).Properties.IsRightButtonPressed != true) return;
            if (HeaderCheckBox?.ContextMenu is { } menu) menu.Open(HeaderCheckHost);
            e.Handled = true;
        };
    }

    /// <summary>
    /// 🔴 跨页取件点（NT-018，规范 §4.8 硬规则 8）：别的页面（标签管理 / 分类管理）请求"打开某个资产"时，
    /// 在这里取件并选中。⛔ 不能放 `OnDataContextChanged` —— 标签已存在、切回来的那次只有可视树重挂，
    /// DataContext 不变、那个回调**不会触发**。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AssetAllViewModel vm) vm.ConsumePendingAsset();
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (AssetList?.ContextMenu is { } amenu) amenu.DataContext = DataContext;
        if (HeaderCheckBox?.ContextMenu is { } cmenu) cmenu.DataContext = DataContext;
    }

    /// <summary>「📁 扫描 → 文件夹…」：选一个目录 → **预览**（只进内存，⛔ 不入库）。</summary>
    private async Task ScanFolderAsync()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var dir = await PickFolderAsync(vm).ConfigureAwait(false);
        if (dir == null) return;
        await vm.PreviewFolderAsync(dir).ConfigureAwait(false);
    }

    /// <summary>「📁 扫描 → 文件…」：只预览选中的几个文件（⛔ 同样不入库）。</summary>
    private async Task ScanFilesAsync()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
            AllowMultiple = true
        }).ConfigureAwait(false);

        if (files == null || files.Count == 0) return;
        var paths = new List<string>();
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        if (paths.Count == 0) return;
        await vm.PreviewFilesAsync(paths).ConfigureAwait(false);
    }

    /// <summary>NT-026：「🗄 并入库文件…」：选一个外部 .db → VM 预览 + 确认 + 入队合并。</summary>
    private async Task PickMergeDbAsync()
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要并入当前库的 newtoys.db",
            SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite 数据库") { Patterns = new[] { "*.db", "*.db3", "*.sqlite" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
            }
        }).ConfigureAwait(false);

        var path = files?.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path)) return;
        vm.MergeLibraryCommand.Execute(path);
    }

    /// <summary>NT-026：「💾 保存」：选一个目标 .db → VM 另存（已存在会问覆盖）。</summary>
    private async void OnSaveLibraryAsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetAllViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存资产库为",
            SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
            SuggestedFileName = "newtoys.db",
            FileTypeChoices = new[] { new FilePickerFileType("SQLite 数据库") { Patterns = new[] { "*.db" } } }
        }).ConfigureAwait(false);

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        vm.SaveLibraryAsCommand.Execute(path);
    }

    /// <summary>弹目录选择框，返回选中的本地路径（取消 / 拿不到 → null）。</summary>
    private async Task<string?> PickFolderAsync(AssetAllViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
            AllowMultiple = false
        }).ConfigureAwait(false);

        var path = folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>把文件 / 目录选择器定位到「导入导出默认目录」（v3.19 / NT-006 全页统一）。</summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }

    // ==================== NT-022：资产列表 JSON 导入 / 导出 ====================

    private async void OnExportAssetsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetAllViewModel vm) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出资产列表",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                SuggestedFileName = "media-assets.json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } } }
            }).ConfigureAwait(false);

            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            File.WriteAllText(path, vm.ExportAssetsJson());
            vm.StatusText = $"已导出当前列表到：{path}";
        }
        catch (Exception ex) { vm.StatusText = "导出失败：" + ex.Message; }
    }

    private async void OnImportAssetsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetAllViewModel vm) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要导入的资产 json",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            }).ConfigureAwait(false);

            var src = files?.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (string.IsNullOrWhiteSpace(src)) return;

            var (ok, msg) = vm.ImportAssetsJson(File.ReadAllText(src));
            vm.StatusText = msg;
        }
        catch (Exception ex) { vm.StatusText = "导入失败：" + ex.Message; }
    }
}
