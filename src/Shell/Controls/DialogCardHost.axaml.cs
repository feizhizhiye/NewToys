using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace NewToys.Shell.Controls;

/// <summary>
/// 全局弹出卡片层：确认框 / 提示框 / 输入框的唯一呈现器（替代原独立系统 Window）。
/// <para>
/// 调用方（⛔ 页面不得自造弹窗）：① <c>App.axaml.cs</c> 给 <c>Core.DialogHooks</c> 的
/// <c>ConfirmHandler / InputHandler</c> 做宿主实现 ⇒ 所有模块的 <c>DialogHooks.ConfirmAsync</c>
/// 自动落到这里；② <c>DataView</c> 的导入/备份确认；③ 启动检查更新的提示。
/// </para>
/// <para>
/// 🔴 未展示时整层 <c>IsVisible=False</c> ⇒ 不吃命中测试，缩放热区 / 标题栏按钮照常。
/// 🔴 钩子没接上时调用方一律按「用户取消」处理（与旧 Window 实现同口径）。
/// </para>
/// </summary>
public partial class DialogCardHost : UserControl
{
    /// <summary>卡片类型：图标 / 按钮组合 / 返回值语义都不同。</summary>
    private enum Kind
    {
        /// <summary>二次确认（取消 / 继续）→ bool。</summary>
        Confirm,
        /// <summary>单按钮提示（知道了）→ 无返回值。</summary>
        Notify,
        /// <summary>单行输入（取消 / 确定）→ string?。</summary>
        Input,
        /// <summary>通用消息（可配确定按钮文字 / 是否带取消）→ bool；DataView 导入确认用。</summary>
        Message
    }

    // 图标几何（24 网格，stroke 风格；颜色由 XAML 的 CardIcon 给 = 强调底上的白）
    private const string IconWarn = "M12,4 L21,19 L3,19 Z M12,10 L12,14 M12,16.5 L12,16.51";
    private const string IconInfo = "M12,3 A9,9 0 1,1 12,21 A9,9 0 1,1 12,3 Z M12,11 L12,16 M12,8 L12,8.01";
    private const string IconEdit = "M4,20 L4,15 L15,4 L20,9 L9,20 Z M4,20 L9,15 M15,4 L17.5,1.5 L21.5,5.5 L19,8";

    /// <summary>当前卡片的完成源；null = 没有卡片在展示（遮罩隐藏，可安全切页面）。</summary>
    private TaskCompletionSource<object?>? _tcs;

    /// <summary>当前卡片类型（关闭时判返回值语义用：输入 = null，其余 = false）。</summary>
    private Kind _kind;

    public DialogCardHost()
    {
        InitializeComponent();
    }

    // ==================== 公开 API ====================

    /// <summary>二次确认（取消 / 继续）。</summary>
    public async Task<bool> ConfirmAsync(string message)
        => await ShowCore(Kind.Confirm, "请确认", message, "继续", true, "") is true;

    /// <summary>单按钮提示（知道了）。</summary>
    public Task NotifyAsync(string title, string message)
        => ShowCore(Kind.Notify, title, message, "知道了", false, "");

    /// <summary>单行输入（取消 / 确定）；回车 = 确定。取消返回 null。</summary>
    public async Task<string?> InputAsync(string title, string initialValue)
        => (string?)await ShowCore(Kind.Input, title, "", "确定", true, initialValue);

    /// <summary>通用消息卡片：可配确定按钮文字、可隐藏取消；DataView 的导入/备份确认用。</summary>
    public async Task<bool> MessageAsync(string title, string message, string okText, bool showCancel)
        => await ShowCore(Kind.Message, title, message, okText, showCancel, "") is true;

    // ==================== 展示 / 关闭 ====================

    /// <summary>
    /// 构造并展示一张卡片，返回用户的选择（框起来 await）。
    /// 已有卡片在展示时先把旧的按「取消」收掉（防御：调用方 await 链断了不等结果）。
    /// </summary>
    private Task<object?> ShowCore(
        Kind kind, string title, string message, string okText, bool showCancel, string initialValue)
    {
        // 防御：上一张没被 await 到结果就被下一张顶掉 ⇒ 先给它一个取消，免得调用方永远挂着
        _tcs?.TrySetResult(_kind == Kind.Input ? null : (object?)false);

        _kind = kind;

        CardTitle.Text = title;
        CardMessage.Text = message ?? "";
        CardIcon.Data = StreamGeometry.Parse(kind switch
        {
            Kind.Confirm => IconWarn,
            Kind.Notify => IconInfo,
            Kind.Input => IconEdit,
            _ => IconInfo
        });
        BtnOk.Content = okText;
        BtnCancel.IsVisible = showCancel;
        CardInput.IsVisible = kind == Kind.Input;
        CardInput.Text = initialValue ?? "";
        CardInput.PlaceholderText = "请输入";

        Root.IsVisible = true;

        // 焦点：输入卡片给输入框（回车即确定、打开即可输）；其余给卡片本体（收 ESC / 回车）
        if (kind == Kind.Input) CardInput.Focus();
        else Card.Focus();

        _tcs = new TaskCompletionSource<object?>();
        return _tcs.Task;
    }

    /// <summary>收掉当前卡片并回传结果。幂等：重复调用只认第一次。</summary>
    private void Close(object? result)
    {
        if (_tcs == null) return;
        Root.IsVisible = false;
        CardInput.IsVisible = false;
        var tcs = _tcs;
        _tcs = null;
        tcs.TrySetResult(result);
    }

    /// <summary>取消的返回值：输入框 = null；其余 = false。</summary>
    private object? CancelResult() => _kind == Kind.Input ? null : (object?)false;

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (_tcs == null) return;
        Close(_kind == Kind.Input ? CardInput.Text : (object?)true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(CancelResult());

    /// <summary>点遮罩 = 取消（卡片是遮罩的兄弟节点 ⇒ 卡片内点击不会冒到这里）。</summary>
    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
        => Close(CancelResult());

    /// <summary>卡片键盘：ESC = 取消；回车 = 确定（输入卡片由输入框自行处理，防重复触发）。</summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tcs == null) return;
        if (e.Key == Key.Escape)
        {
            Close(CancelResult());
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _kind != Kind.Input)
        {
            Close(true);
            e.Handled = true;
        }
    }

    /// <summary>输入框键盘：回车 = 确定；ESC = 取消（冒泡到卡片前先处理掉）。</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tcs == null) return;
        if (e.Key == Key.Enter)
        {
            Close(CardInput.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
