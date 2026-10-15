using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NewToys.PcTools.ViewModels;

namespace NewToys.PcTools.Views;

public partial class MyShortcutsView : UserControl
{
    public MyShortcutsView()
    {
        InitializeComponent();
        // NT-055（需求 §4.8.5）：原来「📂 选择」脚本目录 / 「📥 批量导入脚本」两个 Click 接线随页内工具条
        // 一起删了 —— 现在它们由描述符的命令承担（命令在 VM 里，选择器走 DialogHooks）。
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

        // NT-054：把「📂 选脚本文件…」的真实实现（要 TopLevel / StorageProvider）注入给声明式卡的动作。
        // 每次 DataContext 变化重挂一次，幂等 —— 命令内部只认最新 VM。
        if (DataContext is QuickActionListViewModel vm)
            vm.PickScriptFileCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => _ = PickScriptFileAsync());
    }

    /// <summary>详情卡「📂」：为当前选中项挑一个脚本文件（.py / .ps1 / .bat）。</summary>
    private async Task PickScriptFileAsync()
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
}
