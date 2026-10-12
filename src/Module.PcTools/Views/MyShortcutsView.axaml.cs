using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NewToys.PcTools.ViewModels;

namespace NewToys.PcTools.Views;

public partial class MyShortcutsView : UserControl
{
    public MyShortcutsView()
    {
        InitializeComponent();
        // 「📂 选择」脚本目录 / 「📂」选脚本文件：都要用到平台文件选择器，
        // 而 VM 不能引用 Avalonia（规范：业务不碰 UI 类型），所以在这一层选完再把结果交给 VM。
        PickScriptFolderBtn.Click += (_, _) => _ = PickScriptFolderAsync();
        PickScriptFileBtn.Click += (_, _) => _ = PickScriptFileAsync();
        ImportFolderBtn.Click += (_, _) => _ = PickImportFolderAsync();
    }

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

    /// <summary>顶部「📂 选择」：换脚本目录（选完立即生效并写 app.json）。</summary>
    private async System.Threading.Tasks.Task PickScriptFolderAsync()
    {
        if (DataContext is not QuickActionListViewModel vm) return;

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择脚本目录",
                AllowMultiple = false
            });
            var dir = folders?.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(dir)) vm.ApplyPickedScriptFolder(dir);
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择脚本目录失败：" + ex.Message;
        }
    }

    /// <summary>📥 批量导入：选一个目录 → 把里面的脚本文件一次性全建成条目。</summary>
    private async System.Threading.Tasks.Task PickImportFolderAsync()
    {
        if (DataContext is not QuickActionListViewModel vm) return;

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择要导入的脚本目录",
                AllowMultiple = false
            });
            var dir = folders?.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(dir)) return;

            var (added, skipped) = vm.ImportScriptFolder(dir, vm.ImportSubFolders);
            if (added == 0 && skipped == 0)
                vm.StatusText = $"「{dir}」里没有 {string.Join(" / ", QuickActionListViewModel.ScriptExtensions)} 这类脚本文件。";
            // 有结果时结论已经写进 StatusText（ImportScriptFolder 内部负责）
        }
        catch (Exception ex)
        {
            vm.StatusText = "批量导入失败：" + ex.Message;
        }
    }

    /// <summary>详情卡「📂」：为当前选中项挑一个脚本文件（.py / .ps1 / .bat）。</summary>
    private async System.Threading.Tasks.Task PickScriptFileAsync()
    {
        if (DataContext is not QuickActionListViewModel vm) return;
        if (vm.Selected == null) { vm.StatusText = "先在左侧选中一条。"; return; }

        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var options = new FilePickerOpenOptions
            {
                Title = "选择脚本文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("脚本文件") { Patterns = new[] { "*.py", "*.ps1", "*.bat", "*.cmd" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            };

            var files = await sp.OpenFilePickerAsync(options);
            var picked = files?.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(picked)) vm.ApplyPickedScriptFile(picked);
        }
        catch (Exception ex)
        {
            vm.StatusText = "选择脚本文件失败：" + ex.Message;
        }
    }

    // ==================== 导入 / 导出（v3.19 / NT-007；与快捷设置页同一套 VM 方法） ====================

    private void OnExportClick(object sender, RoutedEventArgs e) => _ = ExportAsync();
    private void OnImportMergeClick(object sender, RoutedEventArgs e) => _ = ImportAsync(replace: false);
    private void OnImportReplaceClick(object sender, RoutedEventArgs e) => _ = ImportAsync(replace: true);

    /// <summary>📤 导出：把本页全条目存成 json 文件。</summary>
    private async System.Threading.Tasks.Task ExportAsync()
    {
        if (DataContext is not QuickActionListViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出清单",
                SuggestedFileName = "pc-scripts-export.json",
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
    private async System.Threading.Tasks.Task ImportAsync(bool replace)
    {
        if (DataContext is not QuickActionListViewModel vm) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) { vm.StatusText = "当前环境不支持文件选择器。"; return; }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = replace ? "选择要整页替换导入的清单" : "选择要合并导入的清单",
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
}
