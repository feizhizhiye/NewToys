using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.PcTools.ViewModels;

namespace NewToys.PcTools.Views;

public partial class PackageManagerView : UserControl
{
    public PackageManagerView()
    {
        InitializeComponent();

        // 搜索框回车 = 点「🔍 联网搜索」（Avalonia 的 TextBox 没有 AcceptButton 之类的属性）
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (DataContext is PackageManagerViewModel vm) vm.SearchCommand.Execute(null);
            e.Handled = true;
        };
    }

    /// <summary>
    /// ⚠️ <c>ContextMenu</c> 挂在**独立的弹出树**上，DataContext 不保证随宿主继承 ——
    /// 不显式同步的话，菜单项里的 <c>{Binding XxxCommand}</c> 解析不到，右键点了没反应。
    /// 这里跟着页面的 DataContext 走（页面 DataContext 就是 <c>PackageManagerViewModel</c>）。
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PkgList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        // 表头右键「显示 / 隐藏列」菜单同样挂在独立弹出树，必须手工同步 DataContext（见规范 3.8）。
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;
    }

    // ==================== 已安装包清单 导入 / 导出（v3.19 / NT-008：换机恢复） ====================

    private void OnExportClick(object sender, RoutedEventArgs e) => _ = ExportAsync();
    private void OnImportClick(object sender, RoutedEventArgs e) => _ = ImportAsync();

    /// <summary>📤 导出当前列表为 json（建议先点「📥 已安装」再导）。</summary>
    private async Task ExportAsync()
    {
        if (DataContext is not PackageManagerViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出已安装包清单",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                SuggestedFileName = "packages.json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } } }
            });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            File.WriteAllText(path, vm.ExportPackagesJson());
            vm.StatusText = $"已导出当前列表到：{path}（新系统上用它生成安装脚本）";
        }
        catch (Exception ex) { vm.StatusText = "导出失败：" + ex.Message; }
    }

    /// <summary>📥 导入导出的 json → 生成 winget 批量安装脚本(.bat)。</summary>
    private async Task ImportAsync()
    {
        if (DataContext is not PackageManagerViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要恢复的包清单 json",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 清单") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });
            var src = files?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(src)) return;

            var script = PackageManagerViewModel.BuildInstallScript(File.ReadAllText(src));
            if (string.IsNullOrEmpty(script)) { vm.StatusText = "⚠ 这个 json 里没有可安装的包（或不是本程序导出的清单）。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存批量安装脚本",
                SuggestedStartLocation = await SuggestedStartAsync(sp, vm.ImportExportDefaultDir),
                SuggestedFileName = "install-packages.bat",
                FileTypeChoices = new[] { new FilePickerFileType("批处理脚本") { Patterns = new[] { "*.bat" } } }
            });
            var dst = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(dst)) return;

            File.WriteAllText(dst, script);
            vm.StatusText = $"已生成批量安装脚本：{dst}（在新系统上运行它即可恢复）";
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
