using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NewToys.Shared.Details;

namespace NewToys.UI.Controls;

/// <summary>
/// 详情卡声明式宿主（NT-054 L1）：输入 <see cref="Card"/>（描述符）+ <see cref="Source"/>（页面 VM），
/// 产出 <see cref="Renderer"/>（渲染对象），由本控件的 XAML 用 7 个字段 <c>DataTemplate</c> 渲染。
/// <para>
/// 🔴 <see cref="Source"/> 必须是**页面 VM 本身**（不是描述符、也不是选中项）—— 描述符里的所有路径都相对它解析。
/// </para>
/// </summary>
public partial class DetailCardHost : UserControl
{
    /// <summary>卡片描述符（null = 什么都不渲染）。</summary>
    public static readonly StyledProperty<DetailCardDescriptor?> CardProperty =
        AvaloniaProperty.Register<DetailCardHost, DetailCardDescriptor?>(nameof(Card));

    /// <summary>卡片 DataContext（= 页面 VM），描述符路径的解析根。</summary>
    public static readonly StyledProperty<object?> SourceProperty =
        AvaloniaProperty.Register<DetailCardHost, object?>(nameof(Source));

    /// <summary>渲染对象（XAML 的 DataContext；由本控件在 <see cref="Card"/>/<see cref="Source"/> 变化时重建）。</summary>
    public static readonly StyledProperty<DetailCardRenderer?> RendererProperty =
        AvaloniaProperty.Register<DetailCardHost, DetailCardRenderer?>(nameof(Renderer));

    private DetailCardRenderer? _owned;

    public DetailCardHost()
    {
        InitializeComponent();
    }

    public DetailCardDescriptor? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public DetailCardRenderer? Renderer
    {
        get => GetValue(RendererProperty);
        set => SetValue(RendererProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardProperty || change.Property == SourceProperty) Rebuild();
    }

    /// <summary>换卡片 / 换页面时重建渲染对象（⚙ 旧的一律 <c>Dispose</c>，退订源 VM 的 PropertyChanged）。</summary>
    private void Rebuild()
    {
        _owned?.Dispose();
        _owned = Card is null ? null : new DetailCardRenderer(Card, Source);
        Renderer = _owned;
    }

    /// <summary>
    /// <c>OnBlur</c> 提交语义（需求 §4.5.1「3 种提交语义」）：TextBox 失焦 → 把值写回页面 VM。
    /// <para>模板里所有 Kind=Text 的输入框都挂这个处理器，处理器内按行的 <c>CommitOnBlur</c> 自行判断，
    /// ⛔ 不要在这里写具体字段名。</para>
    /// </summary>
    private void OnTextLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TextFieldRow row }) row.Commit();
    }
}
