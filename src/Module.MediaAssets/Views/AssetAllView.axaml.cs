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

        // ① 导入文件夹：选一个目录 → 登记为存储源并扫描（**会**做丢失校准）
        BtnImportFolder.Click += async (_, _) =>
        {
            if (DataContext is not AssetAllViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
                AllowMultiple = false
            }).ConfigureAwait(false);

            var path = folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(path)) return;
            await vm.ImportFolderAsync(path!).ConfigureAwait(false);
        };

        // ② 导入文件：只导入选中的几个（**不做**丢失校准 —— 同目录其它文件没被选上，不能判丢）
        BtnImportFiles.Click += async (_, _) =>
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
            await vm.ImportFilesAsync(paths).ConfigureAwait(false);
        };

        // ③ 路径输入框回车 = 执行扫描（省得再去点按钮）
        ImportPathBox.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            if (DataContext is AssetAllViewModel vm) vm.ImportPathCommand.Execute(null);
            e.Handled = true;
        };

        // ④ 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetAllViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // ⑤ 表头首次测量完 → 让 VM 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
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
    }

    /// <summary>把文件 / 目录选择器定位到「导入导出默认目录」（v3.19 / NT-006 全页统一）。</summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }
}
