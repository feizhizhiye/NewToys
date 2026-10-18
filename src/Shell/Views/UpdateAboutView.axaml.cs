using Avalonia.Controls;
using NewToys.Shell.ViewModels;

namespace NewToys.Shell.Views;

/// <summary>
/// 应用级设置页 · 「版本与更新」节（v3.67 / NT-062 收尾）：见 <c>UpdateAboutView.axaml</c> 顶部注释。
/// <para>
/// 🔴 两个子 View 的 DataContext 必须**分别**赋（<see cref="Init"/>）—— 外层 DataContext 只有一个，
/// 靠继承只能给到其中一个，另一个会拿到错误的 VM（症状：更新节的按钮点了没反应 / 关于节全是空值）。
/// </para>
/// </summary>
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
