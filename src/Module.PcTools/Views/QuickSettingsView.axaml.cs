using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.PcTools.ViewModels;

namespace NewToys.PcTools.Views;

public partial class QuickSettingsView : UserControl
{
    public QuickSettingsView() { InitializeComponent(); }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应（见规范 3.6）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ActionList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.6）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

    // ==================== 导入 / 导出（v3.19 / NT-006；VM 不碰 Avalonia，文件选择在这一层） ====================

    private void OnExportClick(object sender, RoutedEventArgs e) => _ = ExportAsync();

    private void OnImportMergeClick(object sender, RoutedEventArgs e) => _ = ImportAsync(replace: false);

    private void OnImportReplaceClick(object sender, RoutedEventArgs e) => _ = ImportAsync(replace: true);

    /// <summary>📤 导出：把本页全条目存成 json 文件。</summary>
    private async Task ExportAsync()
    {
        if (DataContext is not QuickActionListViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出清单",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                SuggestedFileName = "pc-quick-export.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } }
                }
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            File.WriteAllText(path, vm.ExportPageJson());
            vm.StatusText = $"已导出本页全条目到：{path}";
        }
        catch (Exception ex) { vm.StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>📥 导入：选 json 文件 → 合并 / 整页替换导入本页。</summary>
    private async Task ImportAsync(bool replace)
    {
        if (DataContext is not QuickActionListViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = replace ? "选择要整页替换导入的清单" : "选择要合并导入的清单",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });
            var path = files?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            var json = File.ReadAllText(path);
            vm.StatusText = vm.ImportPageJson(json, replace);
        }
        catch (Exception ex) { vm.StatusText = "导入失败：" + ex.Message; }
    }

    /// <summary>把文件选择器定位到「导入导出默认目录」（v3.19 / NT-006 全页统一）。</summary>
    private static async Task<IStorageFolder?> SuggestedStartAsync(IStorageProvider sp, string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return await sp.TryGetFolderFromPathAsync(dir); }
        catch { return null; }
    }
}
