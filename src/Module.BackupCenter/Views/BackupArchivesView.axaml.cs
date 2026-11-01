using Avalonia.Controls;

namespace NewToys.BackupCenter.Views;

/// <summary>
/// 「备份中心 → 备份包」页（`backup-archives`）：表格交互（排序 / 列宽 / 右键菜单 DataContext）
/// 由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class BackupArchivesView : UserControl
{
    public BackupArchivesView() => InitializeComponent();
}
