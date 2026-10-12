using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.WebTools.ViewModels;

namespace NewToys.WebTools.Views;

public partial class BookmarkView : UserControl
{
    public BookmarkView()
    {
        InitializeComponent();

        // 「导入书签」：弹文件选择框，把选中的 HTML 交给 VM 解析。
        // ⚠️ Avalonia 12 **移除了** OpenFileDialog / FileDialogFilter（那是 Avalonia 11 的 API），
        // 取而代之的是 TopLevel.StorageProvider 的 OpenFilePickerAsync + FilePickerOpenOptions。
        BtnImport.Click += async (_, _) =>
        {
            if (DataContext is not BookmarkViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                SuggestedStartLocation = await SuggestedStartAsync(topLevel.StorageProvider, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    // ⚠️ FilePickerFileType 的 Name 是**只读**的，只能通过构造函数传（FilePickerFileType(string? name)）
                    new FilePickerFileType("书签 HTML") { Patterns = new[] { "*.html", "*.htm" } },
                    new FilePickerFileType("全部文件") { Patterns = new[] { "*" } },
                }
            }).ConfigureAwait(false);

            if (files == null || files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            await vm.ImportBookmarksFromFileAsync(path).ConfigureAwait(false);
        };
    }

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (BookmarkList?.ContextMenu is { } menu) menu.DataContext = DataContext;
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
