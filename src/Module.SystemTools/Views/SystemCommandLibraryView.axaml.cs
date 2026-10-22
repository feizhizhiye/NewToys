using Avalonia.Controls;

namespace NewToys.SystemTools.Views;

/// <summary>「系统工具 → 命令库」页（`script-commands`，NT-091）。</summary>
public partial class SystemCommandLibraryView : UserControl
{
    // 🔴 Avalonia 的 UserControl **不会自动生成**构造里的 InitializeComponent() —— 漏了整块 XAML 静默不加载（无报错）。
    public SystemCommandLibraryView() => InitializeComponent();
}
