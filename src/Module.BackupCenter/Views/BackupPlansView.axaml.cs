using System;
using Avalonia.Controls;

namespace NewToys.BackupCenter.Views;

/// <summary>「备份中心 → 备份任务」页（`backup-plans`）。</summary>
public partial class BackupPlansView : UserControl
{
    // 🔴 Avalonia 的 UserControl **不会自动生成** InitializeComponent() —— 漏了整块 XAML 静默不加载。
    public BackupPlansView() => InitializeComponent();

    /// <summary>右键菜单挂在独立弹出树上，DataContext 不随宿主继承 —— 必须手工同步，否则点了没反应。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (PlanList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } header) header.DataContext = DataContext;
    }
}
