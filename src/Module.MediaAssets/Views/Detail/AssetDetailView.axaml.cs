using Avalonia.Controls;
using NewToys.MediaAssets.ViewModels;

namespace NewToys.MediaAssets.Views.Detail;

/// <summary>
/// 资产详情卡（由 <see cref="AssetAllViewModel.DetailContent"/> 交给 Shell 的右详情列承载）。
/// <para>
/// 这里唯一做的事：**显示名输入框失焦即保存** —— 卡内 ⛔ 不放任何按钮（NT-016 验收项），
/// 所以不能用「💾 保存」按钮触发，只能挂在失焦上。
/// </para>
/// </summary>
public partial class AssetDetailView : UserControl
{
    public AssetDetailView()
    {
        InitializeComponent();

        DisplayNameBox.LostFocus += (_, _) =>
        {
            if (DataContext is AssetAllViewModel vm) vm.SaveDisplayNameCommand.Execute(null);
        };
    }
}
