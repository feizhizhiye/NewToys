using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace NewToys.UI.Behaviors;

/// <summary>
/// 让筛选栏下拉（样式类 <c>filter-combo</c> 的 <see cref="ComboBox"/>）的弹出列表**贴住框左边缘**向下展开。
/// <para>🔴 主题模板给的是 <c>Placement=Bottom</c>（水平居中）⇒ 候选文字比筛选框宽时向两侧同时伸出，
/// 换一次条件（选项长短变化）列表就左右乱跳。这里写的是 <see cref="Popup"/> **实例**的本地值 ——
/// 优先级高于模板里的字面值，⛔ 靠 XAML 样式改不动模板目标。</para>
/// <para>用法：在页面根节点设 <c>ui:FilterDropDownAlign.Attach="True"</c>，页内所有 <c>filter-combo</c> 一并生效。</para>
/// </summary>
public class FilterDropDownAlign
{
    /// <summary>挂载开关：在页面根节点设 True 即校正该页所有筛选下拉的展开位置。</summary>
    public static readonly AttachedProperty<bool> AttachProperty =
        AvaloniaProperty.RegisterAttached<FilterDropDownAlign, Control, bool>("Attach");

    public static void SetAttach(Control obj, bool value) => obj.SetValue(AttachProperty, value);
    public static bool GetAttach(Control obj) => obj.GetValue(AttachProperty);

    static FilterDropDownAlign()
    {
        AttachProperty.Changed.AddClassHandler<Control>((host, e) =>
        {
            if (e.GetNewValue<bool>()) Watch(host);
        });
    }

    /// <summary>模板还没套上时拿不到 <see cref="Popup"/>，最多重试这么多帧就放弃（⛔ 别每帧遍历整页可视树）。</summary>
    private const int MaxAttempts = 5;

    private static void Watch(Control host)
    {
        var done = false;
        var attempts = 0;

        host.LayoutUpdated += (_, _) =>
        {
            if (done || attempts >= MaxAttempts) return;
            attempts++;

            var combos = host.GetVisualDescendants().OfType<ComboBox>()
                             .Where(c => c.Classes.Contains("filter-combo"))
                             .ToList();
            if (combos.Count == 0) return;

            var aligned = 0;
            foreach (var combo in combos)
            {
                if (combo.GetVisualDescendants().OfType<Popup>().FirstOrDefault() is not { } popup) continue;
                popup.Placement = PlacementMode.BottomEdgeAlignedLeft;
                aligned++;
            }

            // 全部到位才收工；有的还没套上模板（拿不到 Popup）就等下一帧再试
            done = aligned == combos.Count;
        };
    }
}
