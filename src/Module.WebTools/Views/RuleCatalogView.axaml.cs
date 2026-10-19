using System;
using Avalonia.Controls;

namespace NewToys.WebTools.Views;

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
    }
}
