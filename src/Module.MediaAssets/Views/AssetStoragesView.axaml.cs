using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views;

/// <summary>
/// 「存储源」页 code-behind（NT-020）：只做 **View 才做得了的事**
/// （弹目录选择器 / 转发表头拖拽与测宽事件 / 给弹出树补 DataContext），业务逻辑全在 VM。
/// </summary>
public partial class AssetStoragesView : UserControl
{
    public AssetStoragesView()
    {
        InitializeComponent();

        // 拖表头列边界 → 转给 VM（Grid.ColumnDefinitions 不能绑，只能这样转）
        HeaderGrid.ColumnResize += (_, e) =>
        {
            if (DataContext is AssetStoragesViewModel vm)
                vm.ResizeColumns(e.LeftIndex, e.LeftWidth, e.RightIndex, e.RightWidth);
        };

        // 表头首次测量完 → 按「示例宽 ≤ 表头宽」锁列宽（v3.22 §4.8.1）
        HeaderGrid.HeaderMeasured += (_, e) =>
        {
            if (DataContext is AssetStoragesViewModel vm) vm.LockColumnsBySample(e.Widths, e.SampleWidths);
        };
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 §3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
        if (StorageList?.ContextMenu is { } lmenu) lmenu.DataContext = DataContext;
    }

    /// <summary>「➕ 添加存储源」：选一个目录 → 登记（⛔ 不扫描）。</summary>
    private async void OnAddStorageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AssetStoragesViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持目录选择器。"; return; }

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择要登记为存储源的目录",
            AllowMultiple = false
        }).ConfigureAwait(false);

        var path = folders?.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path)) return;
        vm.AddStorage(path);
    }
}
