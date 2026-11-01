using Avalonia.Controls;

namespace NewToys.Reading.Views;

/// <summary>书源页（纯列表页，交互全部走 VM 命令，无控件级逻辑）。</summary>
public partial class SourceListView : UserControl
{
    public SourceListView()
    {
        InitializeComponent();
    }
}
