using Avalonia.Controls;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 调试集合的右侧详情列（字段检验 + 编辑）—— Shell 右详情列承载，内容来自 <c>RuleEditorViewModel.DetailContent</c>。
/// </summary>
// lint:view-no-vm —— 这是详情列的内容视图，不是页面；DataContext 由 RuleEditorViewModel 装配时显式给
public partial class RuleInspectorView : UserControl
{
    public RuleInspectorView() => InitializeComponent();
}
