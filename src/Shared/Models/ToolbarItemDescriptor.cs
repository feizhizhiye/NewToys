namespace NewToys.Shared.Models;

using System.Collections.Generic;

/// <summary>工具条里一项的类型（v3.53 / NT-055，见需求 §4.8.5）。</summary>
public enum ToolbarItemKind
{
    /// <summary>普通按钮（默认）。</summary>
    Button,

    /// <summary>拆分按钮：主按钮直接执行 + 右侧箭头下拉菜单。</summary>
    SplitButton,

    /// <summary>勾选框（如「含子目录」）。</summary>
    Toggle,

    /// <summary>单行输入框（可配「回车即执行」的命令）。</summary>
    TextField,

    /// <summary>下拉框（如预设 / DNS 预设 / 请求方法）。</summary>
    Combo,

    /// <summary>纯文字标签（次要色，用于「源」「脚本目录」这类前缀）。</summary>
    Label,

    /// <summary>竖分隔线（分组用）。</summary>
    Separator
}

/// <summary>
/// 工具条里的**一项**（v3.53 / NT-055）—— 纯数据（⛔ 不引 Avalonia），命令用 BCL <see cref="System.Windows.Input.ICommand"/>。
/// <para>
/// 所有 <c>*Path</c> 都是**相对页面 VM** 的属性名（与 NT-054 详情卡描述符同一口径；
/// 描述符只描述"长什么样、绑什么"，渲染在 <c>Shell/Controls/ToolbarHost</c>）。
/// </para>
/// <para>🔴 加新 Kind 是**加法**：确实需要新交互时才加（配一个渲染分支），不影响既有页。</para>
/// </summary>
public sealed class ToolbarItemDescriptor
{
    /// <summary>项类型。</summary>
    public ToolbarItemKind Kind { get; init; } = ToolbarItemKind.Button;

    /// <summary>静态文案（含图标，如 <c>🔄 重读</c>）。</summary>
    public string? Text { get; init; }

    /// <summary>动态文案：绑 VM 上的属性（与 <see cref="TextFormat"/> 组合成 <c>StringFormat</c>）。</summary>
    public string? TextPath { get; init; }

    /// <summary>动态文案格式，如 <c>➡ 批量处理 ({0})</c>。</summary>
    public string? TextFormat { get; init; }

    /// <summary>备用文案（与 <see cref="Text"/> 互斥显示；如「▶ 播放预览」↔「⏸ 停止」）。</summary>
    public string? AltText { get; init; }

    /// <summary>备用文案何时显示（绑 VM 上的 bool 属性；真 = 显示 <see cref="AltText"/>）。</summary>
    public string? AltTextPath { get; init; }

    /// <summary>样式类（空格分隔），默认 <c>ghost</c>：可用 <c>primary</c> / <c>danger-btn</c> / <c>io-btn</c> 等。</summary>
    public string? Classes { get; init; }

    /// <summary>固定宽度（不写 = 由内容决定）。</summary>
    public double? Width { get; init; }

    /// <summary>悬停提示。</summary>
    public string? ToolTip { get; init; }

    /// <summary>悬停提示（动态：绑 VM 属性）。</summary>
    public string? ToolTipPath { get; init; }

    /// <summary>禁用时也弹出悬停提示（"需先勾选"这类按钮**必须**开，⛔ 否则用户不知道为什么要先勾）。</summary>
    public bool ShowToolTipOnDisabled { get; init; }

    /// <summary>命令（绑 VM 上的 <c>ICommand</c> 属性）。</summary>
    public string? CommandPath { get; init; }

    /// <summary>命令参数（绑 VM 属性；⛔ 别用字面量喂 <c>RelayCommand&lt;T&gt;</c>）。</summary>
    public string? CommandArgPath { get; init; }

    /// <summary>可用条件（绑 VM 上的 bool 属性）。</summary>
    public string? IsEnabledPath { get; init; }

    /// <summary>可见条件（绑 VM 上的 bool 属性）。</summary>
    public string? IsVisiblePath { get; init; }

    /// <summary>固定可用性（<c>false</c> = 永久置灰的占位按钮，如「🧠 存为智能集合」）；不写 = 可用。</summary>
    public bool? IsEnabled { get; init; }

    // ==================== TextField ====================

    /// <summary>输入框水印。</summary>
    public string? Placeholder { get; init; }

    /// <summary>只读（纯展示，如「脚本目录」）。</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>回车即执行的命令（绑 VM 上的 <c>ICommand</c> 属性）。</summary>
    public string? EnterCommandPath { get; init; }

    /// <summary>输入框双向绑定的值 / 勾选框的 IsChecked（绑 VM 属性）。</summary>
    public string? ValuePath { get; init; }

    // ==================== Combo ====================

    /// <summary>下拉数据源（绑 VM 属性）。</summary>
    public string? ItemsPath { get; init; }

    /// <summary>下拉每项显示的属性名（候选对象用；不写 = 直接 ToString）。</summary>
    public string? DisplayPath { get; init; }

    /// <summary>下拉选中项（双向，绑 VM 属性；字符串列表用）。</summary>
    public string? SelectedItemPath { get; init; }

    /// <summary>下拉选中值（双向，绑 VM 属性；对象列表按值绑定用）。</summary>
    public string? SelectedValuePath { get; init; }

    /// <summary>按哪个属性取值（配 <see cref="SelectedValuePath"/>，如 <c>Id</c>）。</summary>
    public string? SelectedValueBindingPath { get; init; }

    // ==================== Button / SplitButton 的菜单 ====================

    /// <summary>按钮 / 拆分按钮的下拉菜单项。</summary>
    public IReadOnlyList<ToolbarMenuItemDescriptor>? Items { get; init; }
}

/// <summary>工具条按钮 / 拆分按钮的下拉菜单项（v3.53 / NT-055）。</summary>
public sealed class ToolbarMenuItemDescriptor
{
    /// <summary>菜单文字。</summary>
    public string Text { get; init; } = "";

    /// <summary>命令（绑 VM 上的 <c>ICommand</c> 属性）。</summary>
    public string? CommandPath { get; init; }

    /// <summary>悬停提示。</summary>
    public string? ToolTip { get; init; }

    /// <summary>悬停提示（动态：绑 VM 属性，如「打开当前库文件」要显示库文件路径）。</summary>
    public string? ToolTipPath { get; init; }
}
