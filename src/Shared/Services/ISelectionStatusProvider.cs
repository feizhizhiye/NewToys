using System.ComponentModel;

namespace NewToys.Shared.Services;

/// <summary>「选中数」契约：页面 VM 实现它，Shell 的底部状态栏在右端显示「已选 N」。（WHY: SH-053）</summary>
public interface ISelectionStatusProvider : INotifyPropertyChanged
{
    /// <summary>当前选中的条数（0 = 状态栏不显示这一段）。</summary>
    int SelectedCount { get; }
}
