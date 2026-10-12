using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class PingView : UserControl
{
    public PingView()
    {
        InitializeComponent();

        // 「导入文件」：弹文件选择框，把选中的文本（一行一个主机）交给 VM。
        // ⚠️ Avalonia 12 起统一走 TopLevel.StorageProvider.OpenFilePickerAsync（OpenFileDialog 已移除）。
        BtnImport.Click += async (_, _) =>
        {
            if (DataContext is not PingViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    // ⚠️ FilePickerFileType 的 Name 是**只读**的，只能通过构造函数传（FilePickerFileType(string? name)）
                    new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("全部文件") { Patterns = new[] { "*" } },
                }
            }).ConfigureAwait(false);

            if (files == null || files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            await vm.ImportFromFileAsync(path).ConfigureAwait(false);
        };
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (TargetList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

    /// <summary>把文件选择器定位到「导入导出默认目录」（v3.19 / NT-006 全页统一）。</summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }
}
