using Avalonia.Controls;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>应用级设置页 ·「版本与更新」节：外壳视图，装配更新 / 关于两个子视图。（WHY: SHL-071）</summary>
// lint:view-no-vm —— 设置节外壳：两个子视图的 DataContext 靠 Init 分别给，自身不建 VM
public partial class UpdateAboutView : UserControl
{
    public UpdateAboutView() { InitializeComponent(); }

    /// <summary>装配两个子 View 的 VM（由 <c>SettingsViewModel.BuildSections</c> 调用，⛔ 不重复调用）。</summary>
    public void Init(GeneralViewModel updateVm, AboutSettingsViewModel aboutVm)
    {
        UpdatePart.DataContext = updateVm;
        AboutPart.DataContext = aboutVm;
    }
}
