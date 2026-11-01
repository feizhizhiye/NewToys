using Avalonia.Controls;

namespace NewToys.WebTools.Views;

/// <summary>
/// 「Ping 目标」页 code-behind：页内工具条由 Shell 承载，「📥 导入文件」的文件选择器走
/// <c>PingViewModel.ImportTargetsCommand</c> + <c>DialogHooks</c>（VM ⛔ 不引 Avalonia UI）；
/// 表格交互（排序 / 列宽 / 右键菜单 DataContext）由 <c>TableViewHost</c> 自理。
/// </summary>
public partial class PingView : UserControl
{
    public PingView() => InitializeComponent();
}
