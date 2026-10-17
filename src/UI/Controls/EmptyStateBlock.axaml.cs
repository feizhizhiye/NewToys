using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace NewToys.UI.Controls;

/// <summary>
/// 空态块（NT-063 P1，NT-060 口径）：说明文案 + 复用本页现有命令的入口。
/// <para>
/// 由宿主页经 <see cref="Message"/> / <see cref="ActionText"/> / <see cref="ActionCommand"/> 下发；
/// <see cref="ActionText"/> 为空时按钮不占位（纯文案空态）。
/// </para>
/// </summary>
public partial class EmptyStateBlock : UserControl
{
    /// <summary>
    /// 🔴 必须显式调 <c>InitializeComponent()</c>：Avalonia **不会自动生成**调用它的构造函数 ——
    /// 漏了它整个 XAML 都不加载（具名字段全 null、样式类不生效），且**无任何报错**。
    /// 2026-10-16 实测：本控件漏构造 ⇒ 骨架一下发属性就 NRE（「视图创建失败」）。
    /// </summary>
    public EmptyStateBlock()
    {
        InitializeComponent();
    }

    /// <summary>空态说明文案（一行，居中自动换行）。</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyStateBlock, string?>(nameof(Message));

    /// <summary>入口按钮文案（为空 ⇒ 按钮不显示）。</summary>
    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<EmptyStateBlock, string?>(nameof(ActionText));

    /// <summary>入口按钮命令（复用本页 VM 的现有命令，⛔ 不为空态新增能力）。</summary>
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyStateBlock, ICommand?>(nameof(ActionCommand));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>
    /// 🔴 v3.71 / NT-063 P2：属性变化 ⇒ 直接写进具名元素（⛔ 不用祖先绑定，见 axaml 顶部注释）。
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MessageProperty) MessageText.Text = Message ?? "";
        else if (change.Property == ActionTextProperty)
        {
            ActionButton.Content = ActionText;
            ActionButton.IsVisible = !string.IsNullOrEmpty(ActionText);
        }
        else if (change.Property == ActionCommandProperty) ActionButton.Command = ActionCommand;
    }
}
