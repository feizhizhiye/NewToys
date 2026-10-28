using System;
using Avalonia.Controls;

namespace NewToys.BackupCenter.Views;

/// <summary>「备份中心 → 备份包」页（`backup-archives`）。</summary>
public partial class BackupArchivesView : UserControl
{
    public BackupArchivesView() => InitializeComponent();

    /// <summary>右键菜单挂在独立弹出树上，DataContext 需手工同步。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ArchiveList?.ContextMenu is { } menu) menu.DataContext = DataContext;
        if (HeaderGrid?.ContextMenu is { } header) header.DataContext = DataContext;
    }
}
