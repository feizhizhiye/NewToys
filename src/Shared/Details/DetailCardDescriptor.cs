using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace NewToys.Shared.Details;

/// <summary>
/// 字段 Kind（**7 + 1 种**，需求 §4.5.1 / NT-054）。
/// <para>
/// 🔴 **加新 Kind 是「加法」**：新需求优先用「现有 Kind + 修饰位」表达；确实需要新交互时才加一个枚举值
/// （配一个 <c>DataTemplate</c>），不影响既有卡。
/// </para>
/// <para>
/// ⚠️ 2026-10-15 打样补记：<see cref="List"/> 是**用户拍板新增的第 8 种** —— 详情卡里的「只读列表」
/// （VideoBatch 的「处理前 → 处理后」、资产卡的「关联 / 文件位置」）用原来 7 种表达不了，
/// 而它不是"新交互"而是既有形态 ⇒ 补一个 Kind 比让整卡走 <see cref="DetailCardDescriptor.Custom"/> 更划算。
/// </para>
/// </summary>
public enum FieldKind
{
    /// <summary>单行 / 多行文本；只读时配 <see cref="DetailField.Selectable"/>，密码框配 <see cref="DetailField.Password"/>。</summary>
    Text,

    /// <summary>勾选框（含分区标题处的勾选框，见 <see cref="DetailSection.TogglePath"/>）。</summary>
    Check,

    /// <summary>下拉框：简单值（直接写回）或对象集合（配 <see cref="DetailField.OptionValuePath"/>）。</summary>
    Select,

    /// <summary>数值 / 评分（可配取值区间 <see cref="DetailField.Min"/> / <see cref="DetailField.Max"/>）。</summary>
    Number,

    /// <summary>集合编辑器（chips：点一下打上 / 取消）。</summary>
    Chips,

    /// <summary>纯提示文字（含空态提示）。</summary>
    Hint,

    /// <summary>分隔线。</summary>
    Divider,

    /// <summary>
    /// **只读列表**（键值 / 对比行）：主文本 + 可选左标签 + 可选右侧强调值 + 可选第二行说明。
    /// 单项路径见 <see cref="DetailField.ItemTextPath"/> 等；数据源复用 <see cref="DetailField.OptionsPath"/>。
    /// </summary>
    List,

    /// <summary>
    /// **图标动作组**（NT-058）：一行方形小图标按钮（32×32），给「收藏 / 点赞 / 点踩 / 保存 / 刷新 / 删除」
    /// 这类**高频、语义靠图标就够**的动作用，替掉一排撑满宽度的文字按钮。
    /// <para>
    /// 成员见 <see cref="DetailField.IconActions"/>（列表 → 顺序即渲染顺序、数量不限、可出现多次）；
    /// 溢出自动换行（WrapPanel）。**只固定视觉样式**，⛔ 不固定排列。
    /// </para>
    /// </summary>
    IconBar
}

/// <summary>
/// 提交语义（**统一为 3 种**，需求 §4.5.1）。⛔ 现状的 4 种随意实现（失焦即存 / 显式提交 / 即时落盘 / 按钮提交）
/// 一律收敛到本枚举。
/// </summary>
public enum EditCommitMode
{
    /// <summary>双向绑定即落盘（勾选框 / 下拉，选完即改）。</summary>
    Instant,

    /// <summary>失焦提交（改名框、参数框）。</summary>
    OnBlur,

    /// <summary>
    /// 需工具条「💾 保存修改」显式提交（批量队列参数 / 命令）。
    /// <para>
    /// 渲染器把值写回**页面 VM 的编辑态属性**（草稿，如 <c>EditDescription</c>）；
    /// **落盘**由页面工具条的保存命令负责 —— 描述符不参与刷新（需求 §4.5.1「刷新契约」）。
    /// </para>
    /// </summary>
    Explicit
}

/// <summary>
/// 详情卡描述符（NT-054）：**声明式**描述一张详情卡的「三区模型」= <see cref="Sections"/>（Fields 区）
/// + <see cref="Actions"/>（动作区）+ <see cref="Custom"/>（逃生舱）。
/// <para>
/// 🔴 走 **C# 对象字面量**声明（⛔ 不做运行时 JSON / XAML 加载）—— 享受编译期检查 + IDE 补全。
/// ⚠️ 绑定路径（<c>ValuePath</c> / <c>VisibleWhen</c> …）是**字符串**：属性改名不报错、只是白板，
/// 所以必须跑 <c>skill/scripts/check-detail-paths.ps1</c> 做静态兜底。
/// </para>
/// </summary>
public sealed class DetailCardDescriptor
{
    /// <summary>Fields 区：分区列表（每区 = 可选标题 + 可选标题勾选框 + 可折叠 + 若干字段）。</summary>
    public List<DetailSection> Sections { get; } = new();

    /// <summary>Actions 区：**与该卡内容强绑定**的卡内动作（⛔ 页面级功能归工具条）。</summary>
    public List<DetailAction> Actions { get; } = new();

    /// <summary>
    /// 逃生舱：**非空时整卡走自定义 View**，<see cref="Sections"/> / <see cref="Actions"/> 被忽略。
    /// 粒度 = 每页一个。
    /// </summary>
    public object? Custom { get; set; }

    /// <summary>没选中项时的引导文案（空态）。</summary>
    public string? EmptyText { get; set; }

    /// <summary>示例态提示文案（v3.32 §4.8.3）。</summary>
    public string? SampleText { get; set; }

    /// <summary>「详情正文是否可见」的布尔路径（如 <c>ShowDetailBody</c>）；null = 恒可见。</summary>
    public string? BodyVisiblePath { get; set; }

    /// <summary>「示例态」的布尔路径（如 <c>IsDetailSample</c>）；null = 恒非示例态。示例态下整卡 IsEnabled=false。</summary>
    public string? SamplePath { get; set; }
}

/// <summary>详情卡的一个分区（Fields 区的分组）。</summary>
public sealed class DetailSection
{
    /// <summary>分区标题（可空：无标题即一段裸字段组）。</summary>
    public string? Header { get; set; }

    /// <summary>
    /// 分区标题处的**勾选框**的布尔路径（如批量队列的 <c>EditIsCut</c>）→ 标题行右侧渲染一个 CheckBox。
    /// </summary>
    public string? TogglePath { get; set; }

    /// <summary>分区标题勾选框的**可用性**路径（如 <c>HasSelection</c>）；null = 恒可用。</summary>
    public string? ToggleEnabledPath { get; set; }

    /// <summary>分区是否展开的布尔路径（可折叠分区）；null = 恒展开（不显示折叠箭头）。</summary>
    public string? IsExpandedPath { get; set; }

    /// <summary>本区字段（顺序即渲染顺序）。</summary>
    public List<DetailField> Fields { get; } = new();
}

/// <summary>
/// 详情卡的一个**声明式字段行**。
/// <para>
/// 路径一律**相对卡片 DataContext**（= 页面 VM），支持 <c>"!"</c> 前缀取反，支持 <c>"A.B"</c> 多级（如 <c>DetailRow.TypeText</c>）。
/// </para>
/// </summary>
public sealed class DetailField
{
    /// <summary>字段小标签（<c>field-label</c> 文案）；<see cref="FieldKind.Divider"/> / <see cref="FieldKind.Hint"/> 不用。</summary>
    public string? Label { get; set; }

    /// <summary>值的绑定路径（如 <c>"DetailRow.TypeText"</c> / <c>"EditDescription"</c>）。</summary>
    public string? ValuePath { get; set; }

    /// <summary>字段形态（7 种）。</summary>
    public FieldKind Kind { get; set; } = FieldKind.Text;

    /// <summary>提交语义（3 种）。</summary>
    public EditCommitMode Commit { get; set; } = EditCommitMode.OnBlur;

    /// <summary>可见性布尔路径（替代 <c>IsVisible</c> 绑定）；null = 恒可见。</summary>
    public string? VisibleWhen { get; set; }

    /// <summary>可用性布尔路径（替代 <c>IsEnabled</c> 绑定）；null = 恒可用。</summary>
    public string? EnabledWhen { get; set; }

    /// <summary>输入框占位文案（<see cref="FieldKind.Text"/> / <see cref="FieldKind.Select"/>）。</summary>
    public string? Placeholder { get; set; }

    /// <summary>字段下方的补充提示（<c>hint-text</c>）；<see cref="FieldKind.Hint"/> 时它就是提示正文。</summary>
    public string? Hint { get; set; }

    /// <summary>
    /// 显示后缀前缀（**仅 <see cref="FieldKind.Hint"/> 的动态行用**，如「实际会用：」+ 值）。
    /// 用于替代 XAML 的 <c>StringFormat='实际会用：{0}'</c>。
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>多行文本的最小高度（<see cref="MultiLine"/> 时生效；默认 52）。</summary>
    public int Rows { get; set; }

    // ===== Select / Chips 用 =====

    /// <summary>候选集合的路径（<see cref="FieldKind.Select"/> / <see cref="FieldKind.Chips"/> / <see cref="FieldKind.List"/>）。</summary>
    public string? OptionsPath { get; set; }

    /// <summary>候选项「值」的路径（对象集合时用，如 <c>"Id"</c>）；null = 直接把候选项本身写回。</summary>
    public string? OptionValuePath { get; set; }

    /// <summary>候选项「显示文案」的路径（对象集合时用，如 <c>"Name"</c>）；null = <c>ToString()</c>。</summary>
    public string? OptionDisplayPath { get; set; }

    /// <summary>
    /// 下拉选中的**索引**路径（<see cref="FieldKind.Select"/> 的第二种落值通道，int 属性）。
    /// <para>用于页面 VM 上本来就是「第几个选项」这种字段（如 <c>EncoderIndex</c> / <c>BitrateModeIndex</c>）；
    /// 用它时配 <see cref="LiteralOptions"/>，且 ⛔ **不要再给 <see cref="ValuePath"/>**（二者互斥）。</para>
    /// </summary>
    public string? IndexPath { get; set; }

    /// <summary>内联候选项文案（<see cref="IndexPath"/> 通道专用：把 XAML 里写死的几个 <c>ComboBoxItem</c> 挪进来）。</summary>
    public IList<string>? LiteralOptions { get; set; }

    // ===== List 专用（只读列表的单项取数） =====

    /// <summary>列表项「左标签」的路径（次要色，可空）。</summary>
    public string? ItemLabelPath { get; set; }

    /// <summary>列表项「主文本」的路径（可复制；可空 —— 空则只渲染 <see cref="ItemSubPath"/>）。</summary>
    public string? ItemTextPath { get; set; }

    /// <summary>列表项「右侧强调值」的路径（强调色，可空，如对比行的「后」）。</summary>
    public string? ItemSubPath { get; set; }

    /// <summary><see cref="ItemTextPath"/> 与 <see cref="ItemSubPath"/> 之间的固定符号（如 <c>"→"</c>）；null = 不显示。</summary>
    public string? ItemSeparator { get; set; }

    /// <summary>列表项「第二行说明」的路径（<c>hint-text</c>，可空）。</summary>
    public string? ItemDetailPath { get; set; }

    /// <summary>
    /// **列表项级命令**的路径（指向页面 VM 上的 <see cref="ICommand"/>，如 <c>OpenAssetCommand</c>）；null = 整表只读。
    /// <para>非空时每个列表项渲染成**可点击行**（Shell 给 <c>Button.list-row.link</c> 样式：悬停高亮 + 强调色 + 手型）。</para>
    /// </summary>
    public string? ItemCommandPath { get; set; }

    /// <summary>
    /// 列表项级命令的**参数**路径（相对候选项，如 <c>"Id"</c>）；null = 把**候选项本身**当参数（先例：<c>OpenAssetCommand</c> 收整项）。
    /// </summary>
    public string? ItemCommandArgPath { get; set; }

    // ===== Chips 专用 =====

    /// <summary>chip 文案路径（<see cref="FieldKind.Chips"/>；null = <c>ToString()</c>）。</summary>
    public string? ChipTextPath { get; set; }

    /// <summary>chip 选中态的布尔路径（如 <c>"IsSelected"</c>）。</summary>
    public string? ChipSelectedPath { get; set; }

    /// <summary>chip 点击命令的路径（指向页面 VM 上的 <see cref="ICommand"/>，如 <c>"ToggleTagCommand"</c>）。</summary>
    public string? ChipCommandPath { get; set; }

    /// <summary>
    /// chip 点击命令的**参数**路径（相对候选项，如色板的 <c>"Hex"</c>）；null = 把**候选项本身**当参数
    /// （先例：<c>ToggleTagCommand</c> 收整项）。
    /// </summary>
    public string? ChipCommandArgPath { get; set; }

    /// <summary>
    /// chip **色块**的路径（相对候选项，如 <c>"Brush"</c>，值为 <c>IBrush</c>）；非空即视为**色块模式**
    /// （Shell 给 <c>Button.chip-swatch</c> 样式，chip 内渲染一个 8×8 圆角色块）。
    /// <para>⚠️ 只要色块不要文字时，把 <see cref="ChipTextPath"/> 设成**空串**（<c>""</c>）—— 空路径解析不出值 ⇒ 文本为空、自动隐藏。</para>
    /// </summary>
    public string? ChipBrushPath { get; set; }

    /// <summary>chip 悬停提示的路径（相对候选项，如色板的 <c>"Hex"</c>）；null = 无提示。</summary>
    public string? ChipToolTipPath { get; set; }

    /// <summary>
    /// chip **分组**的路径（相对候选项，如标签的 <c>"Namespace"</c>；NT-058）。
    /// <para>非空 = 按这个键**分组显示**（每组一个小标题 + 一组 chips，空键归到「其它」）；null = 一大排不分组。</para>
    /// </summary>
    public string? ChipGroupPath { get; set; }

    // ===== IconBar 专用（NT-058：一行图标动作） =====

    /// <summary>
    /// 图标动作组（<see cref="FieldKind.IconBar"/> 专用）：**顺序即渲染顺序**、数量不限、溢出自动换行。
    /// <para>⛔ 与本行其它取数属性（<c>ValuePath</c> / <c>OptionsPath</c> …）互斥 —— IconBar 只认这个集合。</para>
    /// </summary>
    public List<DetailIconAction> IconActions { get; } = new();

    // ===== Number 用 =====

    /// <summary>数值下限（<see cref="FieldKind.Number"/>）。</summary>
    public double? Min { get; set; }

    /// <summary>数值上限（<see cref="FieldKind.Number"/>）。</summary>
    public double? Max { get; set; }

    /// <summary>步进（<see cref="FieldKind.Number"/>；默认 1）。</summary>
    public double Step { get; set; } = 1;

    // ===== 修饰位（与 Kind 独立，按需组合） =====

    /// <summary>等宽字体（命令 / 路径 / 时间码）。</summary>
    public bool Mono { get; set; }

    /// <summary>多行（数量与宽度不限）。</summary>
    public bool MultiLine { get; set; }

    /// <summary>只读可复制（<c>SelectableTextBlock</c>）。只读文本一律「Text + Selectable」。</summary>
    public bool Selectable { get; set; }

    /// <summary>密码框。</summary>
    public bool Password { get; set; }
}

/// <summary>
/// 图标动作组（<see cref="FieldKind.IconBar"/>）里的**一个图标按钮**（NT-058）。
/// <para>
/// 🔴 全部走**路径字符串**（相对卡片 DataContext = 页面 VM），由 Shell 渲染层解析成强类型属性 —— 与
/// <see cref="DetailAction"/> 同一口径（其余 Kind 的路径也是这么解析的）。
/// </para>
/// <para>
/// ⚠️ 纯图标没有文字 ⇒ <see cref="ToolTip"/> **必填**（悬停讲语义，否则用户猜不出按钮是干什么的）。
/// </para>
/// </summary>
public sealed class DetailIconAction
{
    /// <summary>
    /// 图标资源键（`App.axaml` 的 <c>Icon*</c> <c>StreamGeometry</c> 键，如 <c>"IconStar"</c> / <c>"IconThumbUp"</c>）。
    /// <para>⛔ 键不存在 ⇒ 按钮没有图标（渲染层静默跳过，不抛）。</para>
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>可选文字（非空 = 「图标 + 文字」小按钮；null = 纯图标方形按钮）。</summary>
    public string? Text { get; set; }

    /// <summary>悬停提示（**必填**：纯图标靠它讲语义；置灰时也要讲原因）。</summary>
    public string? ToolTip { get; set; }

    /// <summary>命令路径（指向页面 VM 上的 <see cref="ICommand"/>，如 <c>"SaveAssetDetailCommand"</c>）。</summary>
    public string? CommandPath { get; set; }

    /// <summary>可用性布尔路径；null = 恒可用（命令自己的 <c>CanExecute</c> 仍然生效）。</summary>
    public string? IsEnabledPath { get; set; }

    /// <summary>可见性布尔路径；null = 恒可见。</summary>
    public string? IsVisiblePath { get; set; }

    /// <summary>激活态布尔路径（如「已收藏」）；true = 图标走强调色。</summary>
    public string? IsActivePath { get; set; }

    /// <summary>非空则执行前弹二次确认（走 <c>Core/Services/DialogHooks</c>）—— 删除这类不可逆动作必填。</summary>
    public string? ConfirmText { get; set; }

    /// <summary>危险动作（删除等）：图标走 <c>DangerBrush</c>。</summary>
    public bool IsDanger { get; set; }

    /// <summary>分隔符（渲染成一条竖线，把组内按钮分组；此时除本属性外全部忽略）。</summary>
    public bool IsSeparator { get; set; }
}

/// <summary>详情卡动作区的一个**声明式命令**。</summary>
public sealed class DetailAction
{
    /// <summary>按钮文案。</summary>
    public string? Text { get; set; }

    /// <summary>要执行的命令（页面 VM 上的 <c>ICommand</c>）。</summary>
    public ICommand? Command { get; set; }

    /// <summary>可用性布尔路径；null = 恒可用。</summary>
    public string? IsEnabledPath { get; set; }

    /// <summary>可见性布尔路径；null = 恒可见。</summary>
    public string? IsVisiblePath { get; set; }

    /// <summary>非空则执行前弹二次确认（走 <c>Core/Services/DialogHooks</c>）。</summary>
    public string? ConfirmText { get; set; }

    /// <summary>按钮样式类：<c>primary</c> / <c>ghost</c> / <c>danger-btn</c>（本项目的既有按钮类）。</summary>
    public string? Classes { get; set; }

    /// <summary>鼠标悬停提示。</summary>
    public string? ToolTip { get; set; }

    /// <summary>是否横向拉满（默认 true）。</summary>
    public bool Stretch { get; set; } = true;
}
