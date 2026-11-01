using Avalonia.Controls;

namespace NewToys.BackupCenter.Views;

/// <summary>
/// 「备份中心 → 备份任务」页（`backup-plans`）：表格交互（排序 / 列宽 / 右键菜单 DataContext）
/// 由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class BackupPlansView : UserControl
{
    // 🔴 Avalonia 的 UserControl **不会自动生成** InitializeComponent() —— 漏了整块 XAML 静默不加载。
    public BackupPlansView() => InitializeComponent();
}
