using System.ComponentModel;

namespace NewToys.Shared.Services;

/// <summary>
/// 「选中数」契约：页面 VM 实现它，Shell 的底部状态栏在右端显示「已选 N」。
/// <para>
/// 与 <see cref="IStatusProvider"/> 分开的原因：状态文本会被业务消息（「⚠ 查询失败…」/「已复制 3 项」）
/// 反复覆盖，选中数是**常驻状态**，塞进同一条串会时有时无。
/// </para>
/// <para>
/// 🔴 只有**多选列表页**需要实现（当前 = 全部资产 / 资产浏览 / 视频列表 / 批量处理）；
/// 没有多选的页面不实现 ⇒ 状态栏不出现这一段。
/// </para>
/// <para>
/// ⚠️ 继承 <see cref="INotifyPropertyChanged"/> 是**为了少写代码**：页面 VM 都派生自
/// <c>ObservableObject</c>，本就实现了它，只需在类声明里加一个接口名、无需额外事件；
/// Shell 监听 <see cref="SelectedCount"/> 的属性变更即可。
/// </para>
/// </summary>
public interface ISelectionStatusProvider : INotifyPropertyChanged
{
    /// <summary>当前选中的条数（0 = 状态栏不显示这一段）。</summary>
    int SelectedCount { get; }
}
