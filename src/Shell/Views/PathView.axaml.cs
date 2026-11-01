using Avalonia.Controls;

namespace NewToys.Shell.Views;

/// <summary>
/// 设置页「运行环境」节的正文（配置档 / 路径清单）。
/// <para>文件 / 目录选择器走 <c>DialogHooks</c>（由 Shell 注入）；表格交互（排序 / 列宽 /
/// 右键菜单 DataContext）由 <c>TableViewHost</c> 自理。</para>
/// </summary>
public partial class PathView : UserControl
{
    public PathView()
    {
        InitializeComponent();
    }
}
