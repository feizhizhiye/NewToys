using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 抓取规则 · 规则目录页（NT-084 批 A/B；页 Key 仍为 <c>web-scrape</c>）。
/// </summary>
public partial class RuleCatalogView : UserControl
{
    public RuleCatalogView() => InitializeComponent();

    /// <summary>⚠️ ContextMenu 挂在独立弹出树上，DataContext 不随宿主继承，必须手工同步（规范 3.8）。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (RuleSetList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } hmenu) hmenu.DataContext = DataContext;

        // NT-086 批 G：把「另存为」选择器注入 VM（VM ⛔ 不直接碰 StorageProvider）
        if (DataContext is ViewModels.RuleCatalogViewModel vm)
            vm.PickSaveFile = PickSaveFileAsync;
    }

    /// <summary>另存为（导出运行记录 CSV / JSON）；取消 / 失败返回 null。</summary>
    private async Task<string?> PickSaveFileAsync(string title, string suggested, string extension)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } sp) return null;

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggested,
                DefaultExtension = extension,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(extension.ToUpperInvariant() + " 文件") { Patterns = new[] { "*" + extension } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });

            return file?.TryGetLocalPath();
        }
        catch
        {
            return null;   // 选择器本身失败 ⇒ 当取消处理，状态栏由 VM 兜
        }
    }
}
