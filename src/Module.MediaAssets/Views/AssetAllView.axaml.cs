using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「全部资产」页的 code-behind：只做**三件 View 才做得了的事**
/// （弹选择器 / 转发表头拖拽事件 / 给 ContextMenu 补 DataContext），业务逻辑一律在 VM。
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
        ScanSplit.Click += (_, _) => ScanFolderAsync();
        ScanFolderItem.Click += (_, _) => ScanFolderAsync();
        ScanFilesItem.Click += (_, _) => ScanFilesAsync();

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
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (AssetList?.ContextMenu is { } amenu) amenu.DataContext = DataContext;
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
}
