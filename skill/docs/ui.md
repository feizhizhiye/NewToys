# NewToys UI 规范

> v4.6 · 2026-10-30 · **骨架行为订正**：`SettingsSectionHost` 滚动 ⛔ 不改展开状态（「滚动自动展开」已移除），§11.2 与 §11 骨架表格同步。
> v4.5 · 2026-10-26 · **新增 §6.10 列表多选** —— `Ctrl` 增选 / `Shift` 连选 / `Ctrl+A` 全选的标准写法（隧道 `PointerPressed` 接管 + `SelectionChanged` 回抛 + 重建后按稳定键回填），与 Windows 资源管理器一致；现状落地页 = 网址收藏、Hosts 管理。
> v4.4 · 2026-10-24 · **全局滚动条换版（v3.80）** —— `App.axaml` 隐式 `{x:Type ScrollBar}` 主题：8px 槽 / 6px 圆角胶囊滑块 / 轨道透明无箭头 / 常驻；新增主题键 `ScrollThumbBrush`、`ScrollThumbHoverBrush` 与 `ScrollThumbRatioConverter`（滑块长度系数）；§9 补 4 条坑（`Track` 零绑定 / `Value` 必须 TwoWay / 缩长度禁 ScaleTransform / `IsExpanded` 恒真）。
> 本文件 = **一切 `.axaml` / 视觉 / 控件尺寸 / 样式约定的唯一入口**（原 `NewToys-工程规范.md` §2 / §3.6 / §3.8 渲染层 / §3.10）。
> 契约接口定义（`IDetailCardSource` / `IToolbarSource` …）在 `framework.md` §6；模块专属版式在 `modules/<模块>.md`。
> 权威源 = `src/` 源码，冲突以源码为准并回写本文。

## 目录

- §1 XAML 通用模式
- §2 尺寸与密度基线
- §3 主题键
- §4 窗口装饰（CSD）
- §5 图标
- §6 列表页
- §7 详情卡（声明式渲染）
- §8 工具条（声明式承载）
- §9 样式坑与已修 bug
- §10 交互反馈约定（跨模块操作逻辑）
- §11 设置页骨架（`SettingsSectionHost`）

---

## §1 XAML 通用模式

- 扩展名必须 `.axaml`（Avalonia 12 不认 `.xaml`）；所有 `xmlns` 声明在根元素。
- **编译绑定关闭**（B1）：写 `{Binding Xxx}`，**不写 `x:DataType`**。
- ⛔ WPF 专有写法（`AVLN2000`）：`ItemContainerStyle`、`<Style TargetType>`（用 `Selector`）、`Border.HorizontalContentAlignment`、`RowDefinitions` 短标签 `<Auto/>`、`IsCheckable`。
- 颜色一律 `DynamicResource`；文案一律中文。
- XAML 与 code-behind 分离；交互复杂的列表在 code-behind 构建。
- 🔴 **改 `.axaml` 必同步改 `.axaml.cs`**（`x:Name` / 控件类型 / `Grid.Row,Column` 越界 → `CS1061` / `CS0103`）。
- **列定义**：⛔ 禁把 `ColumnDefinitions` 绑 `{Binding}`（`AVLN3000`）；动态列用列宽规格串 + `ColumnGrid`。
- 🔴🔴 **`UserControl` 必须显式写 `public Xxx(){ InitializeComponent(); }`** —— 不会自动生成，漏了整块 XAML **静默不加载（无报错）**。`check.ps1` 已加静态检查兜底。
- 🔴 **跨程序集 xmlns 必须写 `using:`**：`xmlns:v="using:NewToys.UI.Controls"`（⛔ 不写 `clr-namespace:`）。
- ⛔ 页面里不复制同名样式；本页专用样式写 `UserControl.Styles` 里加更具体选择器。

---

## §2 尺寸与密度基线

**主窗口**：`1280×800`，最小 `960×640`，可自由缩放（官方 CSD，见 §4）。
**全局布局**：标题栏 **32px**（与系统原生标题栏齐平；标签栏并入其中段）、左树 **180px**（折叠 48px）、右详情 **340px**、底部状态栏 **26px**、页面根 `Margin="12"`。

| 元素 | 尺寸 |
|---|---|
| 卡片 `Border.card` | 圆角 8px、内边距 **12px** |
| 按钮 `primary` / `ghost` | 圆角 6px；内边距 **12,6** / **10,5** |
| 输入框 | 圆角 4px |
| 标签页项 | 圆角 4px、内边距 8,3；只有标题（无图标）、文字 13px |
| 标签关闭按钮 | 22×22、**叠在标题上层贴右端（不参与布局宽度）**；默认 `Opacity=0` + `IsHitTestVisible=False`，`Border.tab-item:pointerover` 时显形 |
| 树节点行高 | **30px**；缩进每级 16px |
| 工具条 / 筛选条内边距 | `12,6` / `12,4` |
| 表头行高 / 表格行高 | **32px / 28px**；单元格左右内边距 **8px**（表头与行必须一致） |
| 详情卡 | 宽 340px、内边距 **12px**；内容 `Margin="12,12,4,12"`（给覆盖式滚动条留位） |
| 标题栏按钮 | **40×40**；图标 = 16px `Path`；窗口控制按钮 46×40 |
| 底栏 `ListFooterBar` | 高 **28px** |

### 2.1 两种基准宽度下的可用宽度（🔴 排版前先按这张表算）

> 固定件：左树 180（折叠 48）、右详情 340、页面根 `Margin="12"`、`Border.toolbar` 内边距 `12,6`、`Border.filter-bar` 内边距 `12,4`。
> 🔴 左树**两块 padding 互不继承**（v3.69）：滚动区 `Border Padding="8,8"`（要给滚动条让位）、底部版本行 `Border.tree-version Padding="4,0"`（折叠态 `12,0`，让齿轮居中）。改一处⛔ 别指望另一处跟着变。
> 🔴 左树滚动条走廊（v3.70 / 2026-10-18）：`ScrollViewer Padding="0,0,8,0"` —— Fluent 滚动条是**自动隐藏浮层**、不占布局列，走廊一没就压在树行按钮上（激活药丸被竖线穿过）。⛔ 别把右侧 padding 清成 0。
> ⇒ **列表列 = 窗口宽 − 左树 − 详情 − 24**；**工具条 / 筛选条可用宽 = 列表列 − 24**。

| 基准 | 窗口 | 列表列（详情展开） | 工具条 / 筛选条可用宽 |
|---|---|---|---|
| **默认** | 1280×800 | **736** | **712** |
| **最窄** | 960×640 | **416** | 🔴 **392（所有固定宽控件的判据下限）** |
| 默认 · 详情收起 | 1280×800 | 1076 | 1052 |
| 最窄 · 详情收起 | 960×640 | 756 | 732 |

**两条设计逻辑**：
1. **工具条 = 能用多少宽度就显示多少按钮**（实现见 §8 折叠算法）⇒ 一套按钮在 392 / 712 / 1052 下自动表现为「少 → 多 → 全部」。
2. **筛选条 = 一行排死、自适应收缩**：392px 是下限；筛选项一律**等分星号列 + `MaxWidth`**，⛔ 不写固定宽。
   ⚠️ 设置类页面**无详情卡**，可用宽 ≠ 上表。

### 2.2 全局样式位置与类名

- 位置：`Shell/App.axaml`（+ `Shell/Styles/Controls.axaml` 复用样式表）。
- 在用类名：`Button.title-btn`、`Border.tab-item`/`.active`、`Button.tab-btn`、`Button.tab-close`、`Button.tree-row`/`.active`/`.sub`、`Border.tree-panel.collapsed`、`Border.tree-version`、`Button.win-btn`/`.close`、`Border.status-bar` + `TextBlock.status-bar-text`、`Button.primary`/`.ghost`/`.fav-btn`、`Border.card`、`Button.col-header`、`Border.list-header`、`TextBlock.list-cell`、`ListBox.list-page`、`Button.detail-toggle`、`TextBlock.hint-text`、`Button.icon-btn`(+`.active`/`.icon-danger`)、`Border.icon-sep`、`Button.section-toggle`、`Button.chip-toggle`(+`.selected`/`.swatch`)、`Button.list-row`(+`.link`)、`Button.ghost`、`Button.danger-btn`、`Button.src-btn`(+`.selected`)。
- ⚠️ 已废弃：`Button.nav-btn`、`TreeView.tree-nav`（左树改 `ItemsControl` + `Button.tree-row` 自绘）、`io-bar` / `io-btn`（工具条统一后无引用）。

---

## §3 主题键（数量以白名单为准，⛔ 除白名单外不新增）

> 🔴 **不在文档里写死数量**（历史踩过 31/32/34 三个数字并存）：实际条数 = 跑 `pwsh skill/scripts/check.ps1`，首行 `[主题键] 白名单 N 个 *Brush 键` 即权威值。

- 一律 `*Brush` 语义键：`ContentBrush`/`PanelBrush`/`SubSiderBrush`/`SiderBrush`/`PrimaryTextBrush`/`SecondaryTextBrush`/**`PlaceholderTextBrush`**（输入框水印）/`AccentBrush`/`AccentHoverBrush`/`DangerBrush`/`DividerBrush`/`InputBorderBrush`/`CardHoverBrush`/`CardSelectedBrush`/`WindowBorderBrush`/`WindowBorderInactiveBrush` …（**完整清单以源码为准**）。
- ✅ **白名单免维护**：`check.ps1` 动态扫描（`x:Key="*Brush"` + 代码里 `S("*Brush")` 调用），在 `ApplyTheme()` 补了色值就算入白名单，⛔ 不需要改脚本。
- 明暗两套**必须同时补齐**；引用不存在键**编译不报错、运行期才失败**。
- ⛔ 新增键前先想「能不能复用现有键」；确实要加，两套同时补齐 + 写进本表。
- ⛔ `MergedDictionaries.Clear()`。

**元素 → 键映射**：

| 元素 | 键 |
|---|---|
| 标题栏 / 标签栏背景 | `ContentBrush`（底部 1px `DividerBrush`） |
| 输入框水印（`PlaceholderText` 与只读假水印） | `PlaceholderTextBrush` |
| 标题栏按钮 | 文字 / 图标 `PrimaryTextBrush`；hover 底 `CardHoverBrush`；关闭 hover `TitleBarCloseHoverBrush` |
| 标签项（普通 / 激活） | 文字 `SecondaryTextBrush` / 背景 `CardSelectedBrush` + 文字 `PrimaryTextBrush` + 下方 2px `AccentBrush` 指示条 |
| 树一级行 | 文字 `PrimaryTextBrush` + 加粗；图标 `SecondaryTextBrush` |
| 树一级行（激活 / 折叠） | 行内 `Path` Stroke = `AccentBrush`（`Button.tree-row.active Path`） |
| 树二级行 | 文字 `SecondaryTextBrush`、缩进 16px |
| 树节点（激活 / hover） | `CardSelectedBrush` + `AccentBrush` / `ContentBrush` |
| 右详情面板背景 | `PanelBrush`，与内容区之间 `DividerBrush` 1px 竖线 |
| 窗口外框 | 激活 `WindowBorderBrush` ｜失焦 `WindowBorderInactiveBrush`；厚度 1px，最大化收掉；选择器 `Border.window-frame`（+ `.maximized` / `.inactive`），⛔ XAML 不写本地值 |
| 输入框边框 | `InputBorderBrush`（`DividerBrush` 在暗色下看不出框） |
| **滚动条滑块**（v3.80） | 常态 `ScrollThumbBrush`（暗 `#4A4A4A` / 亮 `#C6CAD0`）；悬停 `ScrollThumbHoverBrush` = 强调色 |

- ⚠️ 标题栏底色跟随主题（`ContentBrush`）⇒ 文字必须用 `PrimaryTextBrush` 等主题色，⛔ 不要再用 `Sider*` 系（那是深色侧栏专用）。若以后改回深色标题栏，**底色与文字配色要成套同步改**。
- 废弃旧键：`AppBackground` / `TitleBarBackground` / `TextPrimary` / `TextSecondary` / `BorderColor` / `CardBackground`（长键）、`AppBg` / `TitleBarBg` / `Text1` / `Text2` / `CardBg` / `PanelBg` / `SidebarBg` / `HoverBg` / `Border`（短键）、`TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush`（NT-049 已删）。

---

## §4 窗口装饰（CSD）

> 🔴 **2026-10-15 定案采纳官方 CSD**：Wayland 无服务端装饰、无 DWM / `WS_*` / GDI region ⇒ Win32 补丁路线在 Linux 上整体作废。口径 = **框架能做的交给框架**。

**① 官方四件套（12.1.4 已核实）**

| API | 说明 |
|---|---|
| `Window.ExtendClientAreaToDecorationsHint` | 激活客户区扩展；`ElementRole` 生效的前提 |
| `Window.ExtendClientAreaTitleBarHeightHint` | 自绘标题栏高度（-1 = OS 默认） |
| `Window.WindowDecorations` | 枚举实测只有 `None` / `BorderOnly` / **`Full`** |
| `Window.WindowDecorationsTheme` | 应用画窗框的正门（`ControlTheme` `TargetType="chrome:WindowDrawnDecorations"`） |
| `Window.WindowDecorationMargin` / `OffScreenMargin` | 边框 + 标题栏占掉的厚度 / 最大化时甩到屏外的边框厚度 |
| `WindowDecorationProperties.ElementRole` | `TitleBar` / `User` / `ResizeN…SW` / `CloseButton` / `MinimizeButton` / `MaximizeButton` / `FullScreenButton` / `DecorationsElement` |
| `chrome:WindowDrawnDecorations` | `Template` / `DefaultFrameThickness` / `DefaultShadowThickness` / `DefaultTitleBarHeight` / 运行时 `FrameThickness` / `ShadowThickness` / `HasBorder` / `HasShadow` / `HasTitleBar` / `EnabledParts` / `EffectiveGeometryChanged` |

**② Win32 硬契约（踩过坑，别再试）**

- Win32 上 `RequestedDrawnDecorations` **只有 `TitleBar`** ⇒ **`:has-border` / `:has-shadow` 恒 `false`**；边框 / 阴影 / resize 抓手归 **DWM**。依赖这两个伪类的样式在 Windows **不会触发** ⇒ 阴影 / 边框必须写**无条件选择器**。
- 开了客户区扩展后 **DWM 把边框交给应用画** ⇒ 应用不画就没有边界（NT-039 自绘 1px 外框的由来）。
- DWM 窗口阴影在客户区扩展下**本来就不存在** ⇒ ⛔ 别把它算作 `SetWindowRgn` 的代价。
- 模板 PART 缺失会在 `OnApplyTemplate` 抛 NRE → 前提是**有模板**；本配置不设 `Template`，该风险不成立。
- 🔴 **贴靠几何**：**≈8px（左右下）= 非客户区边框** ⇒ 由「零内缩」消除 ✅ 已验收；**16px 半屏偏宽** = 系统按「外框 = 客户区 + 8px/边」算贴靠矩形的必然多算 ⇒ 由 `ShellView.Win32.cs` 的 `FixTiledGeometry` 纠回精确半屏/四分屏（2026-10-21 实测左右贴靠均 960×1040）。判据容差 **32px 是 96 DPI 基准**、按 `GetDpiForWindow()`/96 缩放（200% 屏 = 64px），⛔ 别再退回固定常量。⛔ 别再试「去掉零内缩」（露缝复发）或「DwmExtendFrameIntoClientArea 清零」（无效）。🔴 **实现细节 / 补丁清单 / 死路 = `framework.md` §12**（本节只留结论）。
  **取消贴靠还原**：同一个「外框 = 客户区 + 8px/边」换算在取消贴靠时反向生效（退回后窗口大一圈）⇒ `FixTiledGeometry` 自己记账：进入贴靠前冻结自由外框（`_preTileFrame`），取消贴靠的第一个矩形请求改写回该外框。⚠️ 必须排除「铺满显示器」的请求（最大化走同一条路径，⛔ 别把双击最大化拽回来）。

**③ 现役配置（`ShellView.axaml`）**

```
WindowDecorations="Full"
ExtendClientAreaToDecorationsHint="True"
ExtendClientAreaTitleBarHeightHint="40"
WindowDecorationsTheme="{DynamicResource {x:Type chrome:WindowDrawnDecorations}}"
```

- `Full` 只为了拿**完整标准窗口样式**（贴靠资格 / 系统菜单 / 最小化动画）；可见 UI 仍是自绘标题栏。
- 主题 = `App.axaml` 的 `ControlTheme`：**只顶掉默认主题，🔴 故意不给 `Template`**（无模板 → 不画任何装饰视觉）。
- 🔴 **键必须是隐式键 `{x:Type chrome:WindowDrawnDecorations}`**（自定义字符串键**不生效**）。
- 🔴 **12.1.4 版本事实**：`Avalonia.Controls.Chrome` 公共类型只有 6 个；⛔ **没有 `WindowDrawnDecorationsTemplate` 具体类**（internal），`Template` 是**接口** ⇒ 自定义装饰模板这条路不存在（`AVLN2000`）。
- 主题引用用 **`DynamicResource`**（合并字典完成前 `StaticResource` 取不到）。

**④ Win32 手段清单（其余一律删除）**

| 手段 | 现状 |
|---|---|
| `Win32Properties.SetWindowCornerPreference` | ✅ 保留 |
| `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33)` = `DONOTROUND`(1)（写 2 = 强制圆角，踩过） | ✅ 保留，必须在 Avalonia 处理完之后压（`PostChromeReassert`） |
| `SetWindowRgn` 矩形区域 | ✅ 保留（技术债 B16） |
| 手工补 `WS_CAPTION` / `WS_THICKFRAME` 样式位 | ⛔ 已删（官方 `Full` 自带） |
| 接管 `WM_NCCALCSIZE` 做「零内缩」 | ✅ 保留（Windows 独有例外） |
| 贴靠几何外部纠偏 | ⛔ 已删（NT-048 已证伪） |
| `DwmGetWindowAttribute(33)` | ⛔ 不支持读，永远 -1 |
| 诊断写 `Window.Title` | ⛔ 无边框自绘窗口不显示 Title |

**⑤ 跨平台（Debian 13 / Wayland）迁前必修**

- Windows 侧保留的方角三保险在 Linux 上整体无效（`DllNotFoundException` 被吞）。
- 🔴 **自绘 1px 外框必须门控**：`WindowDecorationMargin.Top > 0` 时 ⛔ 不重复画 —— ⚠️ **当前未实装**（`Border.window-frame` 无条件绘制）。
- 🔴 窗口位置 / 大小记忆走 user32 ⇒ 非 Windows 失效；迁前回退 Avalonia 的 `Position` / `ClientSize` / `Resize()`。
- 门控判据 = **`WindowDecorationMargin.Top > 0`**；⛔ 不用 `OperatingSystem.IsWindows()` / `#if` / `x:OnPlatform`。

---

## §5 图标

> **当前状态**：**框架层（一级菜单 / 首页 / 标题栏 / 标签页 / 左树）= 矢量**；**二级菜单不显示图标**；⚠️ **数据驱动图标**（二级功能菜单 `icon` / 首页一键按钮 / 模块页内数据行）仍是 emoji → 技术债 **NT-047**（⛔ 静态装饰 emoji 不动）。

- **定义**：`Shell/App.axaml` 的 `<Application.Resources>` 里 `<StreamGeometry x:Key="IconXxx">路径数据</StreamGeometry>`（24×24 网格、**描边型**）。
- **渲染**：`<Path Data="{Binding VectorIcon}" Stroke="{DynamicResource SecondaryTextBrush}" StrokeThickness="1.5" Stretch="Uniform" Width="16" Height="16"/>`。⚠️ 描边型**不要**用填充型的 `PathIcon`。
- **键名规范** `Icon<用途>`。已定义（完整清单以 `App.axaml` 为准）：`IconHome`/`IconPc`/`IconWeb`/`IconVideo`/`IconSettings`/`IconMediaAssets`/`IconMenuPanel`/`IconRefresh`/`IconPuzzle`/`IconWinMinimize`/`IconWinMaximize`/`IconWinRestore`/`IconWinClose`/`IconFullScreen`/`IconExitFullScreen`/`IconTabClose`/`IconPlus`/`IconChevronDown`/`IconChevronRight`/`IconStar`/`IconZap`/`IconGear`。
- 🔴 **Shell 框架层图标只用 `Path`**：⛔ 图标字体 / 字符 / emoji / `Rectangle` 拼几何；粗细 / 端帽 / 拐角 / 颜色一律由按钮的**后代选择器样式**给，⛔ XAML 写本地 `Stroke`（本地值会压过 `:pointerover` / `.active`）。
- 🔴 **`Stretch="Uniform"` 两坑**：几何必须有**非零宽高**（`M4,12 H20` 零高度包围盒会退化成「一个点」）；**扁几何**（横杠 / 线）顶边对齐偏上 ~7px ⇒ 线类用 `Stretch="None"` + 16×16 坐标（唯一例外 `IconWinMinimize` = `M2,8 H14`）。
- **取不到键要回退**：VM 解析失败返回 `null` → XAML 用 `IsVisible="{Binding HasVectorIcon}"` 在「矢量 / emoji」间切换（⛔ 不能让未知键显示成 `IconXxx` 文本）。
- 尺寸基线：一级菜单 / 首页 / 标题栏 **16px**｜树节点 14px｜标签页 14px｜标签关闭 10px。
- 状态图标（最大化/还原、全屏/退出）用**两个 `Path` 叠放 + `IsVisible`** 切换，切换在 code-behind（`ShellView.UpdateMaxIcon()`）。
- `IconChevronDown/Right` 目前无引用（左树展开箭头已去掉），留备后续可折叠区块，⛔ 别因"没人用"删除。
- **来源**：公开线性图标集（Lucide / Feather，MIT）取路径数据，只取数据不引依赖。
- 无状态装饰图标（首页 ★ / ⚡ / ⚙ 这类不参与状态的）可用 `Path.icon-accent` / `Path.icon-primary` 类给死色。

---

## §6 列表页

### 6.1 骨架（`NewToys.UI/Controls/ListPageSkeleton`）

- 件：`ListPageSkeleton.axaml(.cs)` + `EmptyStateBlock.axaml(.cs)` + `ListFooterBar.axaml(.cs)`，经 `xmlns:v="using:NewToys.UI.Controls"` 使用。
- **四段容器**：顶工具条区（`TopArea`，Auto 行，没有则高 0）/ 列表区（`HeaderArea` 表头 + `ListArea` 列表，card 内同格）/ 空态区（`EmptyStateBlock`，与 `ListArea` 同格，`IsVisible` 绑 `IsEmpty`）/ 底栏区（`ListFooterBar`，`ShowFooter` 绑 VM `HasFooterContent`，默认 false）。
- 页面写法（样板 = `AssetAllView`）：根元素换成 `<v:ListPageSkeleton EmptyMessage="…" EmptyActionText="…" EmptyActionCommand="{Binding …}" IsEmpty="{Binding !HasItems}">`，原 card / 表头 Border / 空态 StackPanel 拆进三个属性元素内容；⛔ 行模板仍各页自写（骨架不吞）。
- 🔴 **`x:Name` 在属性元素内容里照样注册 namescope** ⇒ 表头 / 列表的 code-behind 接线原样可用。
- ⛔ 不切的两类：`AssetTasksView`（三块堆叠 + ScrollViewer，非标准列表页）、视频 / 媒体工具的强定制页。

### 6.2 列表页硬规则

1. 表头与行模板**完全相同的星号列定义**（逐字一致）；动态列用 `<v:ColumnGrid ColumnSpecs="{Binding VisibleColumnSpecs}">`。
2. 表头与行单元格左右内边距一致（8px），行里不写 `Padding/Margin`。
3. `ListBoxItem` 拉伸（已在 `App.axaml` 设），页面不覆盖。
4. 表头双交互：左键 → 切换排序（`SortCommand` + `CommandParameter`，VM 追加 ▲▼）；右键 → `ContextMenu` 勾选列显隐（`ToggleType=CheckBox`，`IsChecked` 显式 `Mode=OneWay`）。
5. 空态与选中态显式处理：无选中显示引导；有选中显示详情；空列表用居中 StatusText / `EmptyStateBlock`，不留白。
6. `ContextMenu` 挂在独立弹出树，不保证继承宿主 DataContext ⇒ 必须在 code-behind `OnDataContextChanged` 里手工同步（行菜单 + 表头菜单都要）。
7. 列显隐：元数据 `Shared/Models/ListColumn`、服务 `IColumnLayoutService`（落 `config/columnlayout.json`）。隐藏列用宽度 0（不删列保一致）。
8. 🔴 **跨页投递的数据在 `OnAttachedToVisualTree` 取**（不是 `OnDataContextChanged`）：标签页已存在时切回来只重新挂可视树，`DataContext` 不变 ⇒ 后者不触发，症状是"送过去的文件要刷新页面才出现"。⚠️ 必须写 `protected override`（基类 protected，写 public 报 CS0507）。
9. 🔴 **任何"批量加条目"之后必须清筛选**（清 `SearchText` / 分类归"全部" / 清表头筛选 / 选中第一条新增）—— `script-library` 导入"没反应"的真因就是没清。

### 6.3 列宽三态 + 手动拖拽

- 三态：**星号比例**（`Columns` 元数据默认值，如 `3.2*`）→ **手动像素**（拖表头边界产生，规格串写 `NNN`）→ **隐藏**（`0`，列索引保持不动）。
- 表头两列交界处 5px 透明把手：`ColumnGrid.IsResizable="True"` 时自动生成（**只生成在相邻两个可见列之间**，被锁成表头宽的列**也生成**）；`Cursor = StandardCursorType.SizeWestEast`；拖动**直接调 `DataContext`（须是 `ListPageViewModelBase`）的 `ResizeColumns`** → `VisibleColumnSpecs` 重出 → 表头与行**同规格重建** —— 🔴 **页面不必再写 code-behind 接线**（`ColumnResize` / `HeaderMeasured` 事件保留给非列表场景）。每列 ≥ `max(24, 表头文字宽)`（拖到最窄也不会裁掉表头文字）、左右总宽守恒；⛔ 不拖最后一列右缘。
- **双击把手 = 该列自适应**：宽 = `max(表头文字宽, 示例值宽) + 4`，右侧列吸收差额（总宽守恒）。示例值缺省的列退化为「表头宽」。
- ⛔ **没有「自动调整全部列宽」开关**（长内容列按未截断全文测宽会把其余列挤到接近 0，像"整行消失"）；单列等效行为只走**双击把手**，且只认示例值宽、⛔ 不测真实行。
- 手调列宽**只存会话**（⛔ 不落 `columnlayout.json`）：切标签页不丢、重启还原。
- ✅ `ColumnGrid` **全库仅一份** = `src/UI/Controls/ColumnGrid.cs`（NT-063 P0-a 已合并，此前 5 份 2086 行）。列数防线（`ColumnDefinitions.Count` ≥ 子元素最大 `Grid.Column` + 1）**原样保留**（合并时最易丢的一段）。

#### 6.3b 运行时动态列 `DynamicColumnGrid`（NT-085 上提，2026-10-20）

- `src/UI/Controls/DynamicColumnGrid.cs`（含单元格模型 `SysInfoCell` / `SysInfoHeaderCell`，同文件同命名空间 `NewToys.UI.Controls`）：**列数运行时才知道**（采集器列 schema / 爬虫面板 `RULES` 字段）时用 —— `ColumnSpecs`（可绑定规格串）→ `ColumnDefinitions`，`HeaderCells` 渲染一行 `Button.col-header`、`Cells` 渲染一行 `TextBlock.list-cell`（第 1 列带 `accent`）。
- 🔴 与 `ColumnGrid` 分工：那个 = 静态列 + 拖拽 + 示例值锁宽（列表页骨架用）；这个 = **任意列数 + 规格串 → 网格**，⛔ 不接拖拽。
- 🔴 样式类 `col-header` / `list-cell` / `accent` 在 Shell `App.axaml`（运行时解析）—— UI 库 ⛔ 不自带、⛔ 不写死色值。
- 用法：信息面板（ScriptManager）+ 爬虫面板（WebTools）；跨程序集 xmlns 必须 `using:`（如 `xmlns:v="using:NewToys.UI.Controls"`）。
- 🔴 行选择 / 右键用**外层 `Border`** 包（`Border.info-row` + 事件 / `ContextMenu`）；控件本身 ⛔ 不加这类参数（NT-085 铁律：只搬现状）。

#### 6.3c 列表本体 `TableViewHost`（原生 TableView，**列表页默认写法**）

- 组件：`src/UI/Controls/TableViewHost.cs`（继承 `Avalonia.Controls.TableView`）+ `src/UI/Styles/TableViewHost.axaml`（密度 / 配色，按类型选择器只作用本控件；`App.axaml` 用 `StyleInclude` 引入，并在 `Application.Resources` 里压平 `TableViewRowPadding` / `TableViewCellPadding` / `TableViewColumnResizerMargin`）。
- 页面写法：`<v:ListPageSkeleton.ListArea>` 里放 `<v:TableViewHost ItemsSource=… SelectedItem=… SelectionMode="Single">`，列用 `<v:TableViewHost.Columns>` 内联 `TableViewColumn`（`Width` + `Binding` 或 `CellTemplate`）。**骨架的 `HeaderArea` 不填**（表头由 TableView 自带且粘顶）。
- 🔴 **列顺序铁律**：XAML 里列的声明顺序必须与 VM 的 `ListPageViewModelBase.ColumnLayout` 完全一致（显隐 / 表头文案 / 排序键都按下标对齐）。
- 控件自动完成：① 按 `ColumnLayout[i].Key` 绑定 `Is{Key}Visible`（列显隐）；② 表头文案 = `ListColumn.Header` + 排序箭头（读基类 `SortColumnKey` / `IsSortDescending`，⛔ 不再绑各页 `XxxHeader`）；③ 点表头 → `SortCommand(key)`；④ 拖列边界改列宽（相邻列互补、下限 = 表头文字宽）；⑤ 右键菜单顶部自动插「▤ 列」勾选子菜单（走 `ToggleColumnCommand`，首列固定显示）；⑥ `ContextMenu.DataContext` 自动同步。
- 页面仍需自写的：`CellTemplate`（每列内容）、行右键菜单（单条动作 + 页内筛选项）、行内按钮（`$parent[UserControl].DataContext.XxxCommand`，⛔ 不用 `$parent[ListBox]`）。
- 自备表头控件（如软件包页的三态 `CheckBox`）在 code-behind 里写 `Columns[0].Header = <Control>`；控件认「Header 是 Control」就跳过文案覆盖。
- 仍在用 `ColumnGrid` 的场景：非列表页的固定表头（信息面板 / 爬虫面板 / 规则编辑 / 任务中心）与尚未迁移的磁贴式列表（`AssetBrowseView` / `VideoBatchView` / `VideoListView`）。

### 6.4 列宽锁定（示例值唯一用途）

- 每列给 `ListColumn.Sample`（示例值）—— **唯一用途 = 列宽判定**；基类暴露 `SampleSpecs`（';' 分隔，⛔ 不用逗号）。
- ⛔ **示例值不显示在任何地方**（v3.60 / NT-060 已删「示例行 / 水印行」与详情卡示例态）。
- 表头 `ColumnGrid` 绑 `SampleSpecs`；首次布局测出表头宽与示例宽（临时 `TextBlock` `Measure(∞)`，字号 12 + 单元格内边距 16；测不到记 `double.MaxValue`）→ `HeaderMeasured` → VM `LockColumnsBySample(header, sample)`：示例宽 ≤ 表头宽 → 锁成表头宽，且不小于 `MinLockedWidth = 34`。
- 锁定列**照样生成把手、可拖、可双击自适应**：一动手即解除锁定（手动宽优先；`LockColumnsBySample` 此后跳过该列，不会把它锁回去）。`LockedColumnMask` → `ColumnGrid.LockedColumns` 保留，含义只是「这一列当前仍是表头宽」。

### 6.5 空态引导（v3.60 / NT-060）

- 用 `EmptyStateBlock`（骨架内）或页内 `StackPanel`：`IsVisible="{Binding !HasItems}"`（记得 `OnPropertyChanged(nameof(HasItems))`）；内含一行 `TextBlock.hint-text`（讲"这页放什么"）+ 一个 `Button.ghost`（讲"怎么开始"），命令**直接绑该页既有命令**。
- ⛔ 不新增 VM 状态、⛔ 不新增命令（需求 §4.8.3）。

### 6.6 详情开关的位置

- **工具条末位一个常驻小按钮** `Button.detail-toggle`，`❯` = 展开右侧详情、`❮` = 收起（旧「表头透明热区」做法已废除）。
- `ListPageViewModelBase.Columns` 里**不再追加** `("Detail","","36")`；基类里 `if (c.Key != "Detail")` 特判**全部删掉**。
- ⛔ 别删 `Border.detail-card` 的宽度；改内边距会波及所有列表页滚动条位置。
- ⛔ **详情开关只有一处**（Shell 的 `ToolbarHost` 浮层）⇒ 迁移页必须删掉页内 `detail-toggle` 块，⛔ 不允许两处都有。

### 6.7 详情卡滚动条（覆盖式）

- Avalonia 12 的 `ScrollBar` 浮在内容之上，槽落在 `ScrollViewer` 右缘之外 8px（Fluent 模板写死）。
- **统一走「收在卡内」**：`NewToys.UI/Controls/DetailCardHost.axaml` 内容 `Padding="0,0,14,0"` + `Margin="12,12,4,12"` —— **只写在这一处**，迁移页不必也不能再覆盖 `Padding`。v3.80 起滚动条槽宽 8 / 滑块 6，14px 留位仍然够（⛔ 别缩到 8，滑块贴字难看）。

### 6.8 底栏 `ListFooterBar`

- **三段 `*,Auto,Auto`** = 左计数 `*` ／ 分页链 ／ 「每页」组；**分页链靠右紧挨「每页」**（⛔ 不做"整条居中"浮层）；左列是 `*` ⇒ 窄空间优先牺牲左计数。
- 页码按钮 `Padding=(0,3,0,0)`、箭头 `Padding="0,0,0,4"` = **光学居中**（数字无降部偏上 ~2px、箭头偏下 ~2px）。
- 页大小下拉 `Width=78`（64 会切掉「全部」的「部」字）。
- 28px 底栏内的 `TextBox` / `ComboBox` 必须显式 `MinWidth=0 MinHeight=0`（隐式样式 `MinHeight=28` 会压掉 `Height=22`，控件撑满整条并把顶边线压断）。
- 输入框边框用 `InputBorderBrush`（`DividerBrush` 在暗色下看不出框）。
- 🔴 **页大小下拉的 `ItemsSource` 必须是恒定实例**（内部一个 `ObservableCollection` 就地增删改）。⛔ 运行时整体换 `ItemsSource`：下拉展开、用户点选的那一瞬集合被换 ⇒ Avalonia 用旧索引取新集合 ⇒ `ArgumentOutOfRangeException`（`ItemsSourceView.GetAt` ← `ListBoxItem.OnPointerReleased`），在输入事件栈里无法 catch，**整个进程直接退出**（2026-10-19 软件包页选「50」闪退实锤）。
- 🔴 **「当前值不在候选项就补进下拉」这条兜底会诱发上一条**：页大小落盘是全局一份，各页预设不同（软件包 20/50/100/全部）。VM 侧必须在构造期把读到/默认的页大小**收成本页预设的第一档**（`ListPageViewModelBase.SnapPageSizeToChoices`），⛔ 不要靠底栏去"补一个非预设值"。

### 6.9 分页数据层（NT-064）

- 三段分工：**SQL `LIMIT/OFFSET`** 取当页 / **独立 `COUNT(*)`** 出总数 / **排序必须下推到 SQL**（⛔ 只排当页内存 = 排错序，这是分页最隐蔽的坑）。
- `WHERE` 只写一份，计数与取页共用同一条件对象（两份漂移 ⇒ 总数与行数对不上）。
- 底栏**默认隐藏**，行数超出页底才显示；批量操作**只作用于当前页选中项**。
- 页大小落 `shell` 分区（`shell.json`），⛔ 不落 `columnlayout.json`。
- 现状：仅「全部资产」与「软件包」两页走底栏分页，其余列表页仍是内存切片 + 滚动（量级小，暂无推广需求；原推广卡 NT-065 已作废）。

### 6.10 多选（Ctrl / Shift / Ctrl+A）

- 适用范围：**行能承接批量动作**的列表（现值：网址收藏 `BookmarkView`、Hosts 管理 `HostsView`）。分页页多选只作用于当前页（§6.9）。
- 交互口径（对齐 Windows 资源管理器）：
  - 无修饰键 ⇒ 放行默认逻辑，只重设**连选基准**（`_anchor`）；
  - `Ctrl` ⇒ 切换这一行；`Shift` ⇒ 从基准行连选到这一行；`Ctrl+Shift` ⇒ 追加连选；
  - `Ctrl+A`（`KeyDown`）⇒ 本侧条目行全选。
- 实现固定三件（照抄 `BookmarkView.axaml.cs` / `HostsView.axaml.cs`）：
  1. `SelectionMode="Multiple"` + `AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel)` —— 修饰键点击在**隧道**阶段接管，⛔ 不去改 `ListBoxItem` 主题；
  2. `SelectionChanged` → `vm.SetSelectionFromView(side, list.SelectedItems.OfType<T>().ToList())`，用 `_syncingSelection` 护栏防回抛；
  3. 行集合重建后 VM 抛 `SelectionRestoreRequested` → View 按**稳定键**（行号 / `Id`）回填 `SelectedItems` —— 重建产生新对象，⛔ 靠引用保持选中。
- `SelectedItem` 只服务**详情卡**（单一侧别）；多选快照单独一份，⛔ 两者混用。
- 多栏 / 多个列表 **互斥**：一侧开始多选即清空另一侧（详情卡只认一侧）。
- 右键语义：点在**已选中**行 ⇒ 保持整批（批量命令吃整批）；点在未选中行 ⇒ 改为单选它。
- 计数写**列表内计数文本**（「共 N 行 · 已选 M」），⛔ 进状态栏（§10.4）。
- 批量动作 **一次** 生效 / 一次确认（§10.2），⛔ 逐行弹窗；批量命令只吃**条目行**，空行 / 注释行自动跳过并给提示。

### 6.11 筛选栏下拉的展开位置

- `Classes="filter-combo"` 的下拉吃主题模板的 `Placement=Bottom`（水平居中）⇒ 候选比框宽时向两侧同时伸，选项文字长短一变列表就左右跳。
- 统一用 UI 库附加行为 **`NewToys.UI.Behaviors.FilterDropDownAlign`**：页面根节点设 `ui:FilterDropDownAlign.Attach="True"`，页内所有 `filter-combo` 自动改 `PlacementMode.BottomEdgeAlignedLeft`。
- 实现写的是 `Popup` **实例**的本地值（优先级高于模板里的字面值）；⛔ 靠 XAML 样式改不动模板目标，⛔ 别在页面 code-behind 里再抄一份遍历逻辑。

---

## §7 详情卡（声明式渲染）

> 契约在 `framework.md` §6.3；规格唯一源 = 需求 §4.5.1（三区模型 / 11 种 Kind / 3 种提交语义 / 刷新契约 / 16 张覆盖基线）。

### 7.1 渲染层口径（`DetailCardRender.cs`）

- 描述符里的路径是**字符串**，XAML 写不出动态 `{Binding <字符串>}` ⇒ 渲染层在 C# 侧把 `ValuePath` / `VisibleWhen` / `EnabledWhen` / `OptionsPath` / `Chip*Path` / `*IsExpanded` 解析成真实值，行对象（`TextFieldRow` / `PairFieldRow` / `CheckFieldRow` / `SelectFieldRow` / `IndexSelectFieldRow` / `NumberFieldRow` / `ChipsFieldRow` / `HintFieldRow` / `DividerFieldRow` / `ListFieldRow` / `IconBarFieldRow` / `EditableListFieldRow`）**只暴露强类型属性** ⇒ 模板里**零字符串路径**。
- `DetailSectionRender.CreateRow` 是 **Kind → 行对象**的**唯一分派点**；加新 Kind = 加一个 `case` + 一个 `DataTemplate`。
- 刷新契约：渲染器**订阅源 VM 一次**（`PropertyChanged`）→ 任一属性变化整体重解析。⛔ 动作级改动（落盘 / 加标签 / 删除）由**页面 VM 自己回刷**，描述符不参与。
- 提交语义：`Instant` / `Explicit` → 值写回绑 `PropertyChanged`（`Explicit` 写的是 VM 的**编辑态草稿**，落盘由工具条「💾 保存修改」负责）；`OnBlur` → 模板挂 `LostFocus="OnTextLostFocus"` → `TextFieldRow.Commit()`。
- 外壳（340px 容器 / 覆盖式滚动条留位 / 空态 / 分区与字段间距）**只实现一次**（`DetailCardHost.axaml`）。
- 🔴 **字号三级单调递减**（2026-10-30 定，改任一级必须三处同看）：分区标题 **13 SemiBold 主色**（`Button.section-toggle` / `TextBlock.section-title`）＞ 字段值 **12 主色**（`.field-value`）＞ 字段标签 / 提示 **11 次要色**（`.field-label` / `.hint-text`）。⛔ 分区标题与字段值同号 —— 节标题会"沉"进正文、层级消失（旧版都是 12）。
- 树状感靠**左侧 1px 引导线 + 内容缩进 15**（`DividerBrush` @0.7，仅 `HasHeader` 的节才画）；⛔ 不靠加框分层（340px 装不下）。
- ⛔ 不给 `DetailCardHost` 设 `DataContext`。

### 7.2 描述符关键口径

- 🔴 **路径优先用 `nameof(VM 属性)`**；只有跨对象路径（`"DetailTask.StatusDisplay"`、候选项 `"Id"` / `"Name"` / `"Label"`）才用字符串。`check-detail-paths.ps1` 只查字符串那一批，`nameof` 那批靠编译器 —— 两者互补，⛔ 别把 `nameof` 退回字符串。
- 🔴 **`OptionsPath` 指向的集合必须是「稳定实例」**（静态表 / 常驻 `ObservableCollection`）。渲染器只在**集合实例换了**时才重建候选；每次取属性 `new List<>()` ⇒ ComboBox 把 `SelectedItem` 清掉并经 TwoWay **回写**，等于把页面 VM 的值抹掉。
- 🔴 **空候选不回写**：候选为空时行对象**拒绝回写**（否则一进页面就把预设 / 编码器选择抹空）。
- `Select` 两个落值通道：值（`SelectFieldRow`：`SelectedItem` + `OptionValuePath` 比对）与索引（`IndexSelectFieldRow`：`SelectedIndex` + `LiteralOptions` 或 `OptionsPath`）；`CreateRow` 按 `IndexPath` 是否为空分派。
- `Hint` 支持**动态行**（`ValuePath` + `Prefix`），替代 XAML 的 `StringFormat='前缀{0}'`；`List` 的单项五格（左标签 / 主文本 / 固定符号 / 右侧强调值 / 第二行说明）全由 `Item*Path` 取数，**符号也走描述符**（⛔ 模板里不写死 `→`）。
- `Chips` **分组**：`DetailField.ChipGroupPath`（候选项上的分组键，如标签的 `Namespace`）⇒ 渲染层建 `DetailChipGroup{Header, IsSwatch, Chips}`；⛔ **候选必须先按分组键排序**（分组靠"键连续"）；`IsSwatch` 透传到组对象（模板只往上找一层，⛔ 不用链式 `$parent`）。
- 样式类：`Button.section-toggle` / `Button.chip-toggle(.selected)` / `Button.chip-toggle.swatch` / `Button.list-row(.link)` / `Button.icon-btn(.active/.icon-danger)` / `Border.icon-sep`（见 `Shell/Styles/Controls.axaml` ⑭）。
- ⚠️ `Button.list-row.link` 悬停**只改背景** —— 模板里 `TextBlock` 有本地 `Foreground`，样式压不过本地值。

### 7.3 IconBar（第 9 种 Kind）

- 件：`FieldKind.IconBar` + `DetailField.IconActions` + `DetailIconAction`（`IconKey` / `Text` / `ToolTip` / `CommandPath` / `IsEnabledPath` / `IsVisiblePath` / `IsActivePath` / `ConfirmText` / `IsDanger` / `IsSeparator`，全走路径字符串）。
- 🔑 **图标资源键只能 C# 侧解析**：`Application.Current.TryFindResource(key, out var v)`（Try 模式 + `using Avalonia.Controls;`），行对象暴露 `Geometry Icon` ⇒ 模板 `Path.Data` 直接绑。键不存在 = 无图标，静默跳过。
- 🔑 `ShowToolTipOnDisabled` 是本仓自定义属性 ⇒ **XAML 必须写 `ToolTip.ShowOnDisabled="True"`**（写裸名报 `AVLN2000`）。

### 7.3b EditableList（第 10 种 Kind，2026-10-19）

- 件：`FieldKind.EditableList`（`Shared`）+ `EditableListFieldRow` / `EditableListItemRow`（`UI`）。
- 取数：`OptionsPath`（候选，必须**稳定实例**）→ 每行 `ItemLabelPath`（只读左标签）+ `ItemTextPath`（**可编辑文本，双向写回候选项**）。
- 提交：模板挂 `LostFocus="OnEditableListLostFocus"` → `EditableListItemRow.Commit()`（**只提交真的改过的行**）→ 写回候选项 + 执行 `ItemCommandPath`（参数 = 候选项）。
- 候选集合变了（切节 / 列变化）会自动重读；🔴 提交命令里⛔ **不要重建候选集合**，否则用户点下一个输入框时它会失焦。

### 7.3c Pair（第 11 种 Kind，2026-10-24）

- 件：`FieldKind.Pair`（`Shared`）+ `DetailField.SecondValuePath / SecondPlaceholder / PairSeparator` + `PairFieldRow`（`UI`）。
- 布局：`Grid ColumnDefinitions="*,Auto,*"` = 左框 + 中间符号（默认 `×`）+ 右框；左框仍用 `ValuePath`、右框用 `SecondValuePath`。
- 提交：与 `Text` 一致（`Commit=OnBlur` 时模板挂 `LostFocus="OnPairLostFocus"`，其余即时写回）。
- 用途：**天然成对的数值**（宽 × 高 / 上下限）。⛔ 别把两个不相关的字段塞进 `Pair`。

### 7.3d 动态水印 / 零值视同未填（2026-10-24）

- 件：`DetailField.PlaceholderPath` / `SecondPlaceholderPath`（`FieldKind.Text` / `Pair`）+ `ZeroAsPlaceholder` + 行对象上**可变**的 `Placeholder` / `FirstPlaceholder` / `SecondPlaceholder`。
- 用途：字段「留空 / 0 = 不改」，但用户必须看得见**实际生效的值** ⇒ 值留空时框里是空的，水印写真实值（视频卡：原片宽高 / 预设 CRF / 自动命名后的输出路径）。
- `ZeroAsPlaceholder=true`：值为 `0` / `0.0` / 空 → 显示空（露出水印）；提交空文本写回 **`0`**（⛔ 写空串 —— `DetailPath.Coerce` 转 int 失败会被静默丢弃，表现为"清空无效"）。
- 刷新：`PlaceholderPath` 在 `RefreshCore()` 里重解析 ⇒ VM 只要 `OnPropertyChanged(<水印属性>)` 即可，⛔ 不必重建卡片。

### 7.3e 分区级显隐（`DetailSection.VisiblePath`，2026-10-30）

- 件：`DetailSection.VisiblePath`（`Shared/Details/DetailCardDescriptor.cs`）+ `DetailSectionRender.IsVisible`（`UI/Controls/DetailCardRender.Sections.cs`）+ `DetailCardHost.axaml` 分区根节点一条 `IsVisible` 绑定。
- 语义：`null` = 恒可见（既有卡零影响）；非空 = **整节连抬头一起**按路径显隐。与 `IsExpandedPath` 的分工：可见性决定「存不存在」，展开态决定「展开还是收起」。
- 用途：只在某种模式下才有意义的节（批量队列的「画面 / 音轨 / 码率与体积」= 仅 `EditIsTranscode`）。⛔ 别拿它当「收起」用 —— 收起走 `IsExpandedPath`。
- 🔴 已登记进 `check-detail-paths.ps1` 的 `$pathProps`：`VisiblePath = "..."` 的字符串路径会被静态校验，`nameof` 那批靠编译器。

### 7.4 迁移页三件事（缺一不可）

1. VM 实现 `IDetailPanelProvider` + `IDetailCardSource`，并加一个**会通知 Shell** 的开合命令（⛔ 直接用基类 `ToggleDetailCommand`：只翻 `IsDetailVisible`、**不发 `DetailChanged`**）；`ToggleDetail` / `OpenDetail` 建议**显式实现接口**（基类已有同名 private 方法）。
2. `DetailContent` 返回 `null`，删掉页内右列 XAML，根 `Grid` 收成单列。
3. 页内原来自绘的按钮（`StorageProvider` 选文件等）→ 改成 VM 上的 `ICommand` **属性**，由 View 在 `OnDataContextChanged` 注入实现（幂等），声明式卡用 `DetailAction.Command` 调它。

- ⚠️ **已知布局降级（用户已接受）**：原「宽 / 高」同行并排字段改纵向堆叠；输入框右侧内联按钮收进 Actions 区。要 1:1 还原需给描述符加「同行并排 / 字段内联动作」两个能力（⛔ 未做）。
- 🔴 描述符路径改名**不报错、不崩溃**，只是白板 ⇒ 交付前必跑 `check-detail-paths.ps1`（扫含 `IDetailCardSource|DetailCardDescriptor` 的 `.cs`，路径首段与全仓属性 / 字段名比对）。

---

## §8 工具条（`ToolbarHost` 声明式承载）

> 契约在 `framework.md` §6.4；需求 §4.8.5。

### 8.1 项类型与字段

| `ToolbarItemKind` | 渲染成 | 专用字段 |
|---|---|---|
| `Button` | `Button`（可带 Flyout 菜单） | `Items`（`ToolbarMenuItemDescriptor[]`） |
| `SplitButton` | 主钮 + 下拉箭头 | `Items` |
| `Toggle` | `CheckBox` | `ValuePath`（bool） |
| `TextField` | `TextBox` | `ValuePath` / `Placeholder` / `IsReadOnly` / `PlaceholderWhenIdle` / `EnterCommandPath` |
| `Combo` | `ComboBox` | `ItemsPath` / `DisplayPath` / `SelectedItemPath` / `SelectedValuePath` + `SelectedValueBindingPath` |
| `ChipGroup` | 一排按钮（`src-btn`） | `ItemsPath` / `DisplayPath` / `SelectedPath` / `CommandPath` |
| `Label` | 次要色文字 | ⚠️ **已无页面使用**，⛔ 别再加 |
| `Separator` | 竖线 | — |

通用字段：`Text` / `TextPath` + `TextFormat` / `AltText` + `AltTextPath`（双文案互斥，如 ▶/⏸）/ `Classes`（**只表达配色**）/ `Width` / `ToolTip` / `ToolTipPath` / `ShowToolTipOnDisabled` / `CommandPath` / `CommandArgPath` / `IsEnabledPath` / `IsVisiblePath` / `IsEnabled`（静态置灰占位）/ `Items`。
- 🔴 `Classes` 只写 `ghost` / `danger-btn`；`src-btn` + `selected` 只归 `ChipGroup` 内部用（`ChipGroup` 的选中态在 C# 里绑不了 `Classes`：渲染层订阅数据项 `INotifyPropertyChanged` 切类，见 `ToolbarHost.BindSelectedClass`）。

### 8.2 🔴 尺寸 / 配色 / 宽度（一处给，页面⛔ 不自造）

| 量 | 值 |
|---|---|
| 字号 | 12 |
| 按钮内边距 / 最小高 / 圆角 | `10,5` / 28 / 6 |
| 输入框·下拉框内边距 | `8,4`（最小高 28、垂直居中，与按钮基线齐） |
| 工具条容器 | `Border.toolbar` = `Padding 12,6` + 底部 1px `DividerBrush` |
| 输入框 / 搜索框宽度 | **`88`（铁律）** |
| 下拉框宽度 | 按内容（预设 220 / DNS 200），⛔ 不硬套 88 |
| 详情节流按钮 | `Width/MinWidth 20`、`Padding 0`、`Margin 0,-2,-6,0`（贴右上角） |

- 尺寸由 `ToolbarHost.axaml` 的 `<UserControl.Styles>` **一处给**（`Button` / `SplitButton` / `CheckBox` / `TextBox` / `ComboBox` 五条无类名选择器）；控件内样式优先级高于 App 级 ⇒ `primary` 的 12,6、`danger-btn` / `src-btn` 的 11 + 10,4 **全被压成同一档**。
- ⇒ 描述符 `Classes` **只表达配色**；⛔ 别用 `io-btn`（已删）/ ⛔ 别靠换类改大小。
- 🔴 **配色只剩两个在用变体**：`ghost`（默认，**也是主按钮**）/ `danger-btn`（红，只给会丢数据的动作）；`src-btn` + `selected` 只给 `ChipGroup` 高亮。⛔ 工具条不用 `primary`（实心绿只留给详情卡动作与对话框），⛔ 不用 `primary-outline`。
- 🔴 **水印色 = `PlaceholderTextBrush`**（比 `SecondaryTextBrush` 再淡一档），由 `Controls.axaml` 的 `TextBox` 隐式样式统一设 `PlaceholderForeground`；⛔ 别在页面里单设。
- 🔴 **输入框 / 搜索框宽度统一 `Width = 88`**；下拉框不受此限。
- 🔴 **工具条里⛔ 不放任何说明 / 计数文字**：`Label` 项仍在描述符里但**已无页面使用**；含义写进按钮 `ToolTip`。
- ⛔ 逃生舱 `web-http` 的页内工具条不享受这套统一（已知例外）。

**顺序铁律**（前段固定：入口动作按钮 → 路径 / URL 输入框 → 搜索框，三者挨着排最前；样板 = `assets-all`：📁 扫描 → 路径框 → 搜索框）：

1. 只有搜索框的管理页（`categories` / `collections` / `tags` / `trash` / `storages` / `tasks` / `script-info`）⛔ 不凑三项。
2. 入口动作就是「导入」的页：导入按钮在前、框紧跟。
3. 预设 / 筛选类 `Combo` ⛔ 不许插在「入口动作 → 搜索框」之间 ⇒ 挪到第一个 `Separator` 之后。
4. `script-library` / `script-commands` 由基类 `QuickActionListViewModel` 拼装：`ToolbarExtras`（入口动作 / 输入框）→ 共用搜索框 → `ToolbarTailExtras`（框的配套动作）→ 导出 / 导入 / 清空；⛔ 配套动作别塞进 `ToolbarExtras`。
5. ⚠️ 已知例外（`script-packages`）：首位是源 `ChipGroup`，顺序为「源 → 关键字框 → 🔍 联网搜索」（用户确认保持不动）。

### 8.3 渲染与折叠

- 每个控件 `control.Bind(prop, new Binding(path){ Source = 页面VM })` **显式指定 Source**（Flyout 菜单在独立弹出树，DataContext 不继承）。
- ⛔ 不给 `ToolbarHost` 设 `DataContext`。
- `TextField` 的「回车即执行」= `KeyDown` + 反射取 `EnterCommandPath` 对应的 `ICommand`（全仓唯一一处反射，只用于这条）。
- **折叠算法（两条防抖必须保留）**：
  - 项宽**首帧缓存一次**（任一项量到 0 就整体放弃缓存）；容量**幂等短路**（`_appliedCapacity`）。
  - 预算**按行**算：第一行减去详情开关浮层安全区（⛔ 不许遮按钮）、第二行减去末尾「⋯」宽度；两行放不下才折尾部。
  - 「⋯」由 `ToolbarHost` 自建、**必须是 `ItemsPanel` 的最后一个子元素**；「展开全部」是 **Host 本地状态**（⛔ 不挂 VM）。
  - ⛔ 不用 `ToggleButton` 做开合（会掉回 Fluent 默认外观）；⛔ 不把被折控件搬进 `MenuFlyout`（弹出树不继承 DataContext）。
- 逃生舱：描述符表达不了的布局（现仅 `web-http` 表单式撑满）不迁移，保留页内 `Border.toolbar` —— 逐页 opt-in、⛔ 不双渲染。

### 8.4 输入框三态（`TextField`）

| 态 | 用在哪 | 描述符 | 实现（`BuildTextField`） |
|---|---|---|---|
| **普通** | 搜索框 / 关键字框 | `ValuePath` +（可选）`EnterCommandPath` | `Text` TwoWay 绑 VM |
| **只读假水印** | 展示配置路径（命令库「数据文件」） | `IsReadOnly` + `Placeholder` + `ValuePath` | 真实值 OneWay 绑 `Tag`；`Text = Placeholder` + 类 `watermark-idle`（走水印色）；`GotFocus` → 去类 + `Text = Tag` + `SelectAll()`；`LostFocus` → 还原 |
| **空闲态水印** | **可编辑**但常态不必看值（脚本库「脚本目录」） | `ValuePath` + `Placeholder` + `PlaceholderWhenIdle` | 失焦：**先 commit、再解挂 TwoWay 绑定** → `Text = ""`（水印自然出现）；聚焦：重挂绑定 → 从 VM 读回真实值 + 光标到末尾 |

- 🔑 `PlaceholderText`（= Avalonia 12 的 `PlaceholderForeground` 那一层）**只在 `Text` 为空时才显示** ⇒ "水印化"⛔ 不能只把 `Foreground` 设成 `Transparent`（会把真实值一起弄没）。
- 🔴 **失焦顺序铁律**：`commit()` **先于**清空。反了的话 commit 读到空串 ⇒ 配置被重置（脚本目录会莫名变默认）。
- 只读框 ⛔ 不是"能随时改"：脚本目录 / 数据文件的**唯一写入点在设置页**；`PlaceholderWhenIdle` 的框可编辑，但落盘仍走 `EnterCommandPath`（回车 + 失焦）。
- ⚠️ 可编辑框的初始态就是失焦态 ⇒ 构造时直接 `Text = ""`，不预绑（否则页面一打开就露出长路径）。

### 8.5 工具条交付自检

1. 只用了描述符 + `ToolbarExtras` / `ToolbarTailExtras`，⛔ 没在页里自写 `Border.toolbar`（除 `web-http`）。
2. 没给按钮写尺寸 / 字号 / 内边距（`Width` 只给输入框 88 与 `detail-toggle` 20）。
3. 顺序符合 §8.2 顺序铁律；没有 `Label` 文字项。
4. 输入框宽度 88；水印色没被页面覆盖。
5. 新加/改动的 `*Path` 路径都能在 VM 上找到（`check-detail-paths.ps1` 不报）。

---

## §9 样式坑与已修 bug

1. **`MenuItem` 用 `ItemsSource` 生成子菜单时，`ItemContainerTheme` 必须写 `BasedOn="{StaticResource {x:Type MenuItem}}"`** —— 不写就是一份没有模板的全新 `ControlTheme`，子项画不出来（表现 = 「点了不弹出子菜单」）。全仓 11 处筛选子菜单都犯过。
2. **覆写 `:pointerover` 被 Fluent 盖掉时改后代选择器提优先级**：`Border.tab-item Button.tab-close:pointerover` 能压过 `Button:pointerover`。
3. 🔴 **后代选择器也压不住时换「裸控件主题」**：`App.axaml` 的 `BareButtonTheme`（只有 `ContentPresenter`，零状态绘制），控件上写 `Theme="{StaticResource BareButtonTheme}"`。凡是要完全自绘的小按钮一律套它。
4. **裸主题里 `Path` 渲染不出来真因**：模板把 `HorizontalContentAlignment` 绑到 `ContentPresenter` **自身**的 `HorizontalAlignment` → 用无限约束测量 → `Path`（`Stretch=Uniform`）算出 NaN 尺寸。改法 = TemplateBinding 到 `HorizontalContentAlignment` / `VerticalContentAlignment`（与 Fluent 模板一致）。✅ 已修。
5. ⚠️ 历史留档：曾有期间用 `Segoe MDL2 Assets` 字形规避，NT-045 起**图标字体已全部弃用**。
6. 🔴 **不可编译的坑**：`Classes.xxx="{Binding ...}"` 里的 `xxx` 是**本仓样式类名**、不是控件属性 ⇒ 会被 `check-axaml-props.ps1` 误报，**该脚本已加 `Classes.` 前缀豁免**，⛔ 别删这行豁免。
7. **`IndexSelectFieldRow` 只认 `LiteralOptions` 的 bug** ⇒ 给 `OptionsPath` 的索引下拉渲染空白。✅ 已修（补 `OptionsPath` 分支）。
8. **`ApplyView()` 里 `Items.Clear()` 会让 ListBox 的 `SelectedItem` 双向绑定回写 null** ⇒ 点收藏 / 切分类后详情卡掉回示例态。✅ 已修（新增 `ApplyViewKeepSelection()`）。
9. **`BuildContent` 的动态文案绑定必须显式给 `Source`**（`ToolbarHost` ⛔ 无 DataContext）⇒ 漏了就渲染成空白按钮。
10. 🔴 **全局滚动条（v3.80）= `App.axaml` 里隐式键 `{x:Type ScrollBar}` 的 `ControlTheme`**（8px 槽 / 6px 圆角胶囊滑块 / 轨道透明无箭头 / `AllowAutoHide=False` 常驻），点滑块悬停变强调色。⇒ 全应用（含 `Module.*` 页、`NewToys.UI` 控件、`ComboBox` 下拉）自动生效，⛔ 页面里别再自写 `ScrollBar` 样式；槽宽 8 与滚动条走廊（§2.2 / 本文件第 64 行）对齐，⛔ 改宽会压内容。
11. 🔴 **`AllowAutoHide=False` ⇒ `ScrollBar.IsExpanded` 恒为 `true`**（源码 `UpdateIsExpandedState`）。所以滚动条悬停态**只认 `:pointerover`**，⛔ 别用 `^[IsExpanded=true]` 选择器 —— 它会永远命中，滑块变常年强调色。
12. 🔴 **`Track` 的属性不会自动接上 `ScrollBar`**（`ScrollBar.cs` 与 `Track` 之间零绑定，`OnThumbDragDelta` 只发 `Scroll` 事件、**不写值**）：自定义 `ScrollBar` 模板时 `Track` 的 `Minimum` / `Maximum` / `Value` / `ViewportSize` **必须逐个 `TemplateBinding`**；其中 **`Value` 必须 `Mode=TwoWay`** —— 拖拽是 `Track` 自己 `SetCurrentValue(Value)`，单向绑定会把它立刻覆盖回去 ⇒ **滑块能拖、内容纹丝不动**（2026-10-24 实锤）。`PART_PageUpButton` / `PART_PageDownButton` 两个 `RepeatButton` 也必须留（否则点轨道不翻页）。
13. 🔴 **滑块长度要缩只能改 `Track` 的 `ViewportSize`（过 `ScrollThumbRatioConverter`），⛔ 别用 `Thumb` 的 `ScaleTransform`**：Track 用 `ViewportSize / (Maximum + ViewportSize)` 算长度、用**同一个长度**算位置 ⇒ 用 render 缩放只改视觉不改布局，滑块「居中缩」后拖到底部会空出一截，现象就是**拖不到底**（2026-10-24 实锤）。系数在 `App.axaml` 的 `ScrollThumbRatio`（默认 0.4，越小滑块越短）。
    - **最小长度**：`Track.ComputeScrollBarLengths` 在 `ViewportSize` 有值时读 `Thumb.MinHeight` / `MinWidth`（`IsSet` 才生效，源码兜底只有 10px）⇒ 全局在 `ScrollBar` 主题里按方向各给一条 `^[Orientation=Vertical] /template/ Thumb` 的 `MinHeight=32`（横向 `MinWidth=32`）。⛔ 别在 `Thumb` 主题里不分方向地写 `MinHeight`：横向条的滑块高度会被顶大。
14. 🔴 **`Track.IsDirectionReversed` 必须按方向给（竖向 `true` / 横向 `false`）**：`Track` 垂直方向默认「值增大 ⇒ 滑块**上**移」，与滚动条语义相反 ⇒ 现象是「滑块初始停在最下、往下滚反而向上爬」。`ScrollBar` 自身没有这个属性，所以模板里走 `VerticalDirectionConverter` 从 `Orientation` 转：`IsDirectionReversed="{TemplateBinding Orientation, Converter={StaticResource VerticalReversed}}"`，⛔ 别无脑 `IsDirectionReversed="True"`（会把横向滚动条反过来）。

---

## §10 交互反馈约定（跨模块操作逻辑）

> 本节 = **所有页面共用的操作语义**。逐页的具体按钮 / 右键项见 `modules/<模块>.md` §2.5。
> 新增页面或改交互时**必须**满足本节；与本节冲突的实现视为 bug。

### 10.1 动作入口分工

| 粒度 | 入口 | 说明 |
|---|---|---|
| 动**一条** | 行右键菜单 | 菜单项作用对象 = 当前选中行（`CommandParameter="{Binding Selected}"` + code-behind `ContextRequested` 选行） |
| 动**整页** | 工具条 | 批量 / 页面级动作；⛔ 不放单条动作 |
| 查看态 | 表头右键 | 列显隐 / 筛选 / 「仅看已安装」这类视图开关 |
| 勾选态 | 表头 `☑` 右键 | 全选 / 取消选择 / 反选 |

- 工具条顺序铁律：**入口动作 → 输入框 → 搜索框 → 页专属尾项 → 📥 导入 → 📤 导出 → 🧹 清空**（见 §8）。
- 右键菜单在**弹出树**上，DataContext 不继承 ⇒ 菜单在 XAML 里声明时必须 code-behind 补 `menu.DataContext = DataContext`；动态构造的菜单（如收藏夹按钮墙）直接 `new MenuItem` 并写死命令。

### 10.2 二次确认的判据

**问「删完还能不能从别处恢复」**，不看动作名字：

| 场景 | 确认 | 理由 |
|---|---|---|
| 删**索引行**（磁盘文件还在 / 可从原处重建） | ⛔ 不弹 | 打扰用户；`ConfirmBeforeDelete = false` |
| 删**配置里的真数据**（json 记录，删了就没了） | ✅ 弹 | 文案写清「删的是哪个文件里的记录、磁盘文件不动」 |
| 🧹 清空本页 / 🗑 批量卸载 / 切换配置 / 从磁盘删除 | ✅ 弹 | 不可撤销；文案写清**清什么、不清什么** |
| 批量动作 | ✅ 只弹**一次** | ⛔ 不逐条弹窗 |

### 10.3 写入时机（三种，⛔ 混用）

| 类型 | 时机 | 例子 |
|---|---|---|
| 设置页路径类 | **即时生效**：失焦 / 回车即校验并落盘，⛔ 无保存按钮；非法值 ⇒ 状态栏 `⚠` + **不写盘** | 脚本目录、解释器路径 |
| 表单式编辑 | 显式 `💾 保存修改` | 详情卡改字段 |
| 勾选式关系 | **勾选即生效**，无保存按钮 | 集合成员增减 |

- 配置写入点唯一：`config\*.json` 只由对应设置服务写；页面上的同类输入框只是**同一份值的视图**，不得成为第二个写入点。

### 10.4 状态反馈

- 每个动作**必须**写状态栏三态之一，禁止静默：成功「已…」／失败「⚠ …」／取消「已取消…」。
- `⚠` 前缀 = 出错 / 需要注意（v3.65 / NT-061 起统一）。
- 执行中：相关命令 `IsEnabledPath = "!" + nameof(IsBusy)` 全禁用，防重入。
- 按钮为何灰：`ShowToolTipOnDisabled = true` + 写清前置条件的 ToolTip，⛔ 让用户猜。
- 状态栏 ⛔ 放计数指示（「已勾选 N 个」这类）——勾选状态在行上一眼可见。

### 10.5 两级重载

| 层级 | 位置 | 语义 |
|---|---|---|
| 页内重载 | 工具条 `🔄 <动词><来源>` | 重跑本页数据源，**保留**选择 / 列宽 / 筛选 |
| 彻底重置 | 标题栏 `⟳` | 重置页面状态 |

- 两者 ⛔ 互相替代；页内重载文案必须带来源（`🔄 重扫脚本目录`，不写「刷新」）。

### 10.6 空态

- 空态 = `hint-text` **说明为什么空** + 一个 `ghost` 主动作按钮（`EmptyStateBlock` / `ListPageSkeleton`），⛔ 只写「暂无数据」。
- 空态按钮点了要有去处（去设置页 / 导入 / 新建），⛔ 死链。

### 10.7 弹出卡片（确认 / 提示 / 输入，NT-084）

- 全局唯一呈现器 = `Shell/Controls/DialogCardHost`（遮罩 + 居中卡片，挂在 `ShellView` 最外层 Grid 顶层）。**所有页面共用**：模块的 `DialogHooks.ConfirmAsync` / `InputAsync`、详情卡动作确认（`DetailCardHooks` 转接）、`DataView` 导入确认、启动检查更新提示，全部落到这一层。
- ⛔ 页面 / 模块**不得自造弹窗**（`new Window` + `ShowDialog` 已全清）：系统窗口标题栏是系统装饰，明暗主题下与主窗口割裂。
- 卡片配色**只许 DynamicResource 引主题键**：底 `PanelBrush` / 描边 `DividerBrush` / 圆角 8（同 `Border.card`）/ 投影 `#66000000`；标题行 = 强调色圆底白符号（三角=确认 / i=提示 / 铅笔=输入）；按钮沿用全局 `primary` / `ghost`；遮罩 = `LoadingOverlayBrush`。明暗切换自动跟随。
- 交互口径：点遮罩 / `Esc` = 取消；`Enter` = 确定（输入框内回车 = 确定）；消息区超 320 高才出滚动条。
- 钩子未接上（主窗口未构造）时一律按「用户取消」（Confirm=false / Input=null / Notify 静默），⛔ 不抛。
- 卡片宽 460；`DialogCardHost` 未展示时整层 `IsVisible=False` ⇒ 不吃命中测试，缩放热区 / 标题栏按钮照常。


## §11 设置页骨架（`SettingsSectionHost`）

> NT-071（2026-10-18）：**所有设置页只有一套实现** —— 应用级设置页与 4 个模块设置页都用
> `NewToys.UI/Controls/SettingsSectionHost`。⛔ 页面不得再自写「节卡片 + 节标题」。

### 11.1 结构

| 层 | 是什么 | 谁给 |
|---|---|---|
| 节模型 | `NewToys.UI.Settings.SettingsSection`（`Key` / `Title` / `Icon` / `Desc` / `Content` / `ViewModels` / `IsExpanded`） | 应用级 = VM 代码构建；模块页 = **XAML 属性元素**（见 11.2） |
| 骨架 | `SettingsSectionHost`：节卡片渲染 + 折叠（只点不改滚）+ 磁吸抬头 + Push + 全部收起 + 节内工具条 + 滚动定位 | UI 库（一份） |
| 节内工具条 | `ToolbarTemplate` 插槽（DataContext = 节 VM） | Shell 传 `ToolbarHost`；模块页不给 = 无工具条 |

- 节抬头 = `⌄/› + 图标(14) + 标题(14 SemiBold) + 说明(12 次要色，右对齐)`，底色 `ContentHeaderBrush`、圆角 8；
- 卡片 = `Border.card`（`PanelBrush` / 圆角 8）+ `Padding="0"` + `Margin="0,0,0,10"`；
- 正文 = `Margin="14,10,14,12"`；展开时抬头与正文之间 1px `DividerBrush` 分隔线；
- 页根 = `Panel(ContentBrush)` + `ClipToBounds`（⛔ 不透明背景，防切页透出旧页）；滚动内容 `Margin 12`，**铺满**（⛔ 不再 `MaxWidth=760`）。

### 11.2 两种用法

| 用法 | 写法 | `Sticky` |
|---|---|---|
| 应用级设置页（6 节） | `ItemsSource="{Binding Sections}"` + `ToolbarTemplate` | 默认 `true` |
| 模块设置页（2~3 节） | `UserControl.Resources` 放节内容 + code-behind `Add(...)` 建节 | **`false`** |
| 模块设置页（≥5 节，如媒体资产 7 节） | 同上 | 默认 `true` |

```xml
<!--  模块页：节内容写成 UserControl.Resources 里的命名面板；节元数据在 code-behind 建  -->
<UserControl x:Class="NewToys.Xxx.Views.SettingsXxxView">
  <UserControl.Resources>
    <StackPanel x:Key="Sec0" Spacing="10"> …字段… </StackPanel>
    <StackPanel x:Key="Sec1" Spacing="10"> …字段… </StackPanel>
  </UserControl.Resources>
  <ui:SettingsSectionHost x:Name="Host" Sticky="False" />
</UserControl>
```
```csharp
public SettingsXxxView()
{
    InitializeComponent();
    Add("扫描", "🔍", "一句话说明", "Sec0");
    Add("设置文件", "📄", "配置落在哪", "Sec1");
}

private void Add(string title, string icon, string desc, string key)
    => Host.Sections.Add(new SettingsSection
    {
        Title = title, Icon = icon, Desc = desc, IsExpanded = true,
        Content = (Control)Resources[key]!,
    });
```
- 🔴 **为什么不在 XAML 里直接写节**（2026-10-18 实测两次翻车）：`<ui:SettingsSectionHost.Sections>` 属性元素 / `<ui:SettingsSection>` 隐式内容都会让
  该页 XAML **不被预编译**（编译通过、运行时报 `No precompiled XAML found for …SettingsXxxView`）；给 `Sections` 标 `[Content]`
  则会抢掉 `UserControl.Content` ⇒ `AVLN3000`。⇒ 模块页一律「Resources 放内容 + code-behind 建节」。
- 节内容的 DataContext 由骨架补成页面 VM（见 11.3），⛔ 手写 `DataContext`。
- `Sticky=false` ⇒ ⛔ 不挂浮层、⛔ 不显示「全部收起」，只保留**点抬头开收**。（滚动 ⛔ 改展开状态 —— 与 `Sticky` 无关，两种模式都一样）
- 模块页节内容**直接放字段**（`StackPanel Classes="field"` / `CheckBox` / 按钮），⛔ 再包一层 `Border.card`（会套两层卡片）。

### 11.3 🔴 硬规则

- **节内容 DataContext**：节内容的父级是 `ContentControl`（DataContext = 节模型）⇒ XAML 内容会继承到节模型。
  宿主会自动给 `DataContext == null` 的节内容补页面 DataContext；应用级节 View 已显式设 VM ⇒ 不被冲掉。
  ⇒ 模块页 XAML **不要**给节内容手写 `DataContext`。
- **折叠 / 磁吸逻辑 ⛔ 进 VM**：全在骨架的 code-behind；应用级 VM 通过 `SectionToggled` 事件拿结果
  （只为刷新状态栏聚合），⛔ 不再有 `ToggleSectionCommand` 之类的命令。
- 类名 `sec-head`（code-behind 找抬头）、`sec-body`（正文区）⛔ 别删；`StuckHeader.DataContext = null` ⛔ 别删（漏了整页挂载中断）。
