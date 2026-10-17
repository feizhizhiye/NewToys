using Avalonia.Controls;

namespace NewToys.ScriptManager.Views;

/// <summary>
/// 「脚本管理器」模块设置页 code-behind（v3.64 / NT-061）：⛔ 无逻辑 —— 选目录 / 选文件全走 VM 的
/// <c>DialogHooks</c> 命令（VM 不引 Avalonia，对话框由 Shell 注入）。
/// </summary>
public partial class SettingsScriptView : UserControl
{
    public SettingsScriptView() => InitializeComponent();
}
