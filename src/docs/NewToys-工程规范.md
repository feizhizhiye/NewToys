# NewToys 工程规范

> v3.32 · 2026-10-15 · 技术实现指南
> **标准角色（术语见《AI协作手册》【术语对照】）**：本文件 = **工程规范**（Dev Guide + 技术债台账）。
> 🔴 **工作流与术语不在此定义**：`研发流程（规划期 / 实施期）` + 状态机 + 术语对照的唯一定义处 = 《AI协作手册》；本文件只管「怎么具体实现」。
> 权威源：需求规格（做什么 + 硬性框架约束）；**本文件只做「怎么具体实现」**——编码模式/像素/类名/API 签名/易错点。
> 约束分两类：〔A〕硬约束（引需求）｜〔B〕技术债（本文件台账，原「折衷约束」）
> **两条铁律**：① 代码是唯一真相源（文档与 `src/` 冲突以 `src/` 为准并回写）；② 本文件是唯一实现约定源。
>
> **v3.32 变更**：**NT-054 的 L2 批量迁移**（页内自绘 12 页迁掉 11 页，**L1 能力零扩展**）—— 沉淀出**可照抄的迁移四件事**：① VM 实现 `IDetailPanelProvider` + `IDetailCardSource` + **自己的** `ToggleDetailPanel` / `OpenDetailPanel`（⛔ 基类 `ToggleDetailCommand` 不发 `DetailChanged`）+ 显式接口实现；② View 删右列、页根 Grid 收单列、工具条末位改绑 `ToggleDetailPanelCommand`；③ code-behind 的卡内按钮接线**改成把实现注入 VM 的 `XxxCommand` 属性**（`OnDataContextChanged` 里挂，幂等）；④ **`StringFormat` → VM 派生属性**、**`ObjectConverters.IsNotNull` → `HasXxx` 布尔派生**并补通知。另两条口径：**「动态按钮文案」拆成两个互斥可见的 `DetailAction`**（⭐/☆ 收藏，不加新能力）、**`RelayCommand<T>` 型命令在描述符里包 lambda 传选中项**。`pc-shortcuts` 与 `pc-quicksettings` 共用 VM 基类 ⇒ **契约实现一次、`BuildCard()` 由子类 override**。
> **v3.31 变更**：**NT-054 的 L2 第 2 张迁移**（视频工具·视频预览 `video-cut`）—— 与打样页同构，**L1 能力零扩展**（无需新 Kind / 新通道）；§3.9 的「迁移前页内两列布局」条文正式作废并改写为**迁移后形态**（页内单列 + Shell 右列 / 工具条末位绑 `ToggleDetailPanelCommand` / VM 已实现 `IDetailPanelProvider` + `IDetailCardSource`）。
> **v3.30 变更**：⑦ **NT-054 的 L2 打样页落地**（视频工具·批量队列，需求 §4.5.1）—— 描述符补**第 8 种 Kind `List`**（只读列表：键值行 / 对比行）+ `Hint` 动态行（`ValuePath` + `Prefix`）+ `DetailSection.ToggleEnabledPath` + `Select` 的**索引通道**（`IndexPath` + `LiteralOptions`）；渲染器加 `ListFieldRow` / `IndexSelectFieldRow`；§3.8 新增「**L2 打样补记**」（路径优先 `nameof` / `OptionsPath` 必须稳定实例 / 空候选不回写 / 迁移页三件事 / 布局降级）。
> **v3.29 变更**：⑥ **详情卡声明式渲染实装（NT-054 的 L0 + L1）** —— 新增 `Shared/Details/DetailCardDescriptor.cs`（描述符 POCO）+ `Shared/Services/IDetailCardSource.cs`（来源契约）+ `Shell/Controls/DetailCardHost.axaml(.cs)` / `DetailCardRender.cs`（渲染器：字段 `DataTemplate` + Kind→行对象唯一分派 + 路径解析 + 属性级刷新）；`ShellViewModel.RefreshDetail` 改**两轨择一**（声明式优先，老的自绘 `DetailContent` 保留给未迁移页）；`Shell/Styles/Controls.axaml` 新增 ⑭（`Button.section-toggle` / `Button.chip-toggle`）；§3.8 新增「`IDetailCardSource` + 详情卡声明式渲染」小节（件与归属 / 两轨 / 渲染层口径 / 路径静态兜底）；§3.6 详情卡滚动条写明**只写在 Shell 一处**。配套 `skill/scripts/check-detail-paths.ps1` 新增，`check-axaml-props.ps1` 加 `Classes.` 前缀豁免。
> **v3.28 变更**：① §2.1 新增「**两种基准宽度下的可用宽度**」推导表（默认 1280×800 / 最窄 960×640 → 列表列 736 / 416、工具条与筛选条可用宽 712 / 392）＋ 两条设计逻辑（工具条"能用多少就显示多少"、筛选条"一行排死自适应收缩"）；② §3.6 ① 改为**按容器宽度自动折叠**：项宽**首次布局后缓存一次** + 贪心算两行容量 ⇒ ⛔ 没有"档位数字"可调、⛔ 不用 `Measure(∞)` 每帧重测（循环 / 抖动的来源）。
> **v3.27 变更**：§3.6 新增「**主显示区（工具条 / 筛选条）实现范例**」（v3.41 / v3.42 用户定调，对应需求 §4.8.4）：工具条**按容器宽度分档折叠**（`ApplyToolbarLayout()`：≥620px→12 项 / 450~620px→9 项 / <450px→6 项，末尾「⋯」= 全部显示；⛔ 不用 `ToggleButton`、⛔ 不把控件搬进弹出树、⛔ 不改回"逐项测量"版）；筛选条**恒一行无滚动**（等分星号 `Grid` + `MinWidth=0` 覆盖全局 `ComboBox.filter-combo` 的 `MinWidth=110`）；「需先勾选」的按钮用 **`ToolTip.ShowOnDisabled`** 保证禁用态提示词可读。
>
> ⛔ **只保留最近三次变更**，更早的逐次记录看 `git log`；需求侧章节号见《需求规格》目录（v3.11 起 §4 重排：4.5 详情卡 / 4.6 状态栏 / 4.7 主题 / 4.8 列表页 / 4.9 设置页 / 4.10 视觉规范 / 4.11 未实装）。

---

## §0 文档体系与更新纪律

**四份文档**（不得删除/移动/改名；括号内为**标准角色**）：
- `NewToys-需求规格.md`（**需求规格** · 宪法）：定位/边界/**技术框架/UI 框架/导航模型/功能域**。极少改。
- `NewToys-工程规范.md`（本文件 · **工程规范**）：编码模式/实现细节/**技术债台账**/高频错误。
- `NewToys-项目状态与路线图.md`（**项目状态与路线图** · 含 Backlog 工作项台账）：当前基线/已完成/工作项。
- `NewToys-AI协作手册.md`（**AI 协作手册**）：会话启动包 + 🔴 流程与术语唯一定义处。

**更新节奏**：

| 事件 | 需求 | 规范 | 台账 | 协作手册 |
|---|---|---|---|---|
| 定位/边界/框架级变化 | ✅改 | 引用同步 | 同步 | 视情 |
| 实现方式变化 | 引用 | ✅改 | 同步 | 视情 |
| **完成一个功能/一次交付** | — | 视情 | ✅**更新** | — |
| **仅修报错/错别字/格式** | — | 视情 | **不回写**，并入下次 | — |
| 协作流程变化 | — | 视情 | — | ✅改 |

- 🔒 **指导性文件不乱改**：仅为单个任务的措辞/微调不擅改；仅当内容真的过时或与代码冲突才改。
- **各文档版本号相互独立**：仅当该文档内容真的变化才自增，⛔ 不做「四份齐步递增」。
- **提交信息** `<type>: <简述>`，type∈{feat,fix,docs,refactor,chore,style}。
- Git 仓库根=能看到 `src/` 的那一级（已 `git init`，分支 `main`）；`.gitignore` 忽略 `artifacts/ production/ bin/ obj/ Modules/ *.patch /.workbuddy/`；两个 props 必须入 git。

---

## §1 编码规范（C#）

- 文件作用域命名空间（`namespace X;`）；`using` 置顶，`System.*` 优先。
- 命名：公共成员/类型/方法 `PascalCase`；私有字段/局部/参数 `camelCase`（私有字段前缀 `_`）；异步方法 `Async` 结尾。
- 带 `[ObservableProperty]`/`[RelayCommand]` 的类**必须 `partial`**（否则 `CS0260`）。
- 属性优先 `[ObservableProperty]` 私有字段；命令 `[RelayCommand]`。
- 构造注入为主；`Ioc.Default` 仅在 code-behind/回调等无法构造注入处使用。
- 集合 `ObservableCollection<T>`；公共 API 写 XML 注释；复杂逻辑注释「为什么」。
- ⚠️ 改 `[ObservableProperty]` 字段名/语义时全仓搜旧名（属性名+`_字段名`+`nameof`）；构造里 `_xxx=…` 合法，别顺手修。

---

## §2 XAML 实现模式

- 扩展名必须 `.axaml`（Avalonia12 不认 `.xaml`）；所有 `xmlns` 声明在根元素。
- **编译绑定关闭**（B1）：写 `{Binding Xxx}`，**不写 `x:DataType`**。
- ⛔ WPF 专有写法（`AVLN2000`）：`ItemContainerStyle`、`<Style TargetType>`（用 `Selector`）、`Border.HorizontalContentAlignment`、`RowDefinitions` 短标签 `<Auto/>`、`IsCheckable`。
- 颜色一律 `DynamicResource`；文案一律中文。
- XAML 与 code-behind 分离；交互复杂的列表在 code-behind 构建。
- 🔴 **改 `.axaml` 必同步改 `.axaml.cs`**（`x:Name`/控件类型/`Grid.Row,Column` 越界 → `CS1061/CS0103`）。
- **列定义**：⛔ 禁把 `ColumnDefinitions` 绑 `{Binding}`（`AVLN3000`）；需要动态列时用列宽规格串 + 自定义 `ColumnGrid`。

### 2.1 具体尺寸与样式（需求 §4.10 的实现基线）

**主窗口**：`1280×800`，最小 `960×640`。**可自由缩放**：走官方 `ElementRole=ResizeN…SW`（系统处理，见 **§2.4**）；自绘抓取区（四边 5px + 四角 10px + `BeginResizeDrag`，八向光标，最大化时关闭）**降级为兜底**，NT-041 验证后删除。
🔴 v3.22 修正：旧结论「Avalonia 12.1.3 托管装饰在 Win32 上不可用 → ⛔ 别走托管路线」**已过时**，正确结论见 §2.4（Win32 上框架只接管标题栏，边框 / 阴影归 DWM）。
**全局布局**：**标题栏 40px（标签栏并入其中段，v3.3）**、左树 **180px**（折叠 48px）、右详情 340px。

> **v3.4 落地**：本节尺寸为实施基线；图标见 §2.3（一级菜单已矢量）。

| 元素 | 尺寸 |
|---|---|
| 卡片 `Border.card` | 圆角 8px、内边距 **12px** |
| 按钮 `primary`/`ghost` | 圆角 6px；内边距 **12,6** / **10,5** |
| 输入框 | 圆角 4px |
| 标签页项 | 圆角 4px、内边距 水平 **8px** / 垂直 **3px**；**只有标题**（无图标）、**文字 13px**（v3.7） |
| 左树展开宽 | **180px**（折叠 48px）（v3.7 由 240 收窄） |
| 标签关闭按钮 | 22×22、**叠在标题上层贴右端（不参与布局宽度 → 标签只按文字算宽）**；默认 `Opacity=0` + `IsHitTestVisible=False`，`Border.tab-item:pointerover` 时才显形（v3.6） |
| 树节点行高 | **30px**；缩进每级 16px |
| 内容区外边距 | 列表页根 `Grid` **`Margin="12"`**（v3.12 由 16 收窄） |
| 工具条 / 筛选条内边距 | `12,6` / `12,4` |
| 表头行高 / 表格行高 | **32px / 28px**；单元格左右内边距 **8px**（表头与行必须一致） |
| 详情卡 | 宽 340px、内边距 **12px**；本页覆盖 `Padding="12,12,4,12"` + 滚动内容 `Margin="0,0,4,0"`（给覆盖式滚动条留位，见 §3.6） |
| 标题栏按钮 | **40×40**（与标题栏同高，`Button.title-btn` 样式 Width/Height=40）；图标 = **16px `Path`**（NT-045 起矢量，粗细/端帽/拐角由 `Button.title-btn` 样式给）；窗口控制按钮 46×40 |

**两种基准宽度下的可用宽度（v3.42 收口 · 🔴 排版前先按这张表算，⛔ 别凭感觉给控件定宽）**

> **全局固定件**：左树 **180px**（折叠 48px）、右详情 **340px**、页面根 `Margin="12"`（左右各 12）、
> `Border.toolbar` 内边距 `12,6`、`Border.filter-bar` 内边距 `12,4`。
> ⇒ **列表列 = 窗口宽 − 左树 − 详情 − 24**；**工具条 / 筛选条可用宽 = 列表列 − 24**。

| 基准 | 窗口 | 列表列（详情卡**展开**） | 工具条 / 筛选条可用宽 |
|---|---|---|---|
| **默认** | 1280×800 | 1280 − 180 − 340 − 24 = **736** | **712** |
| **最窄** | 960×640 | 960 − 180 − 340 − 24 = **416** | 🔴 **392（所有固定宽控件的判据下限）** |
| 默认 · 详情收起 | 1280×800 | 1076 | 1052 |
| 最窄 · 详情收起 | 960×640 | 756 | 732 |

**由此推出的两条设计逻辑**：

1. **工具条 = "能用多少宽度，就显示多少按钮"**：容器宽 × 2 = 两行可容纳的像素总量 ⇒ 决定显示几项（实现见 §3.6 ①）。
   同一套按钮序列在 **392 / 712 / 1052** 三种宽度下自动表现为 **少 → 多 → 全部**，⛔ 不需要为不同宽度各写一套布局。
2. **筛选条 = 一行排死、自适应收缩**（需求 §4.8.4 第 4 条）：**392px 是它的可用宽下限**；筛选项一律
   **等分星号列 + `MaxWidth`**，⛔ 不写固定宽（固定宽在 392px 下必然溢出成滚动或换行）。
   ⚠️ 设置类页面**无详情卡**，可用宽 ≠ 上表（判据见需求 §4.9「窄界面适配基线」）。

> 🔴 **首页标签是唯一固定元素（v3.45，用户定并收窄）**：标签栏里**首页标签固定显示**（`ShellViewModel.HomeTab` → `ContentControl` 单独一列，⛔ 不进 `ScrollViewer`）；**其余标签与 `＋`（功能清单）按钮全部参与横向滚动**。⛔ **不做「固定其他标签页」功能**（「固定」需求走首页收藏夹 / 常用功能）⇒ 不需要通用「固定位」机制。实现口径：`ScrollableTabs` = `Tabs` 去首页的**镜像集合**（VM 里随 `Tabs.CollectionChanged` 同步，⛔ 不在各开 / 关命令里分别维护）；标签项模板 = 共享资源 `TabItemTemplate`（`ShellView.axaml` 的 `Window.Resources`，⛔ 不复制两份防样式漂移）；`ScrollActiveTabIntoView` 按 `ScrollableTabs` 索引找容器（首页 `IndexOf` = -1 → 跳过，天然正确）；🔴 **滚动区 `ItemsControl` 必须显式写横排 `ItemsPanel`**（默认竖排 + 竖滚禁用 = 只显示第一个，v3.44 踩过）。

**全局样式位置**：`Shell/App.axaml`（+ 复用样式表 `Shell/Styles/Controls.axaml`）。当前样式类：
`Button.title-btn`（标题栏工具按钮）、`Border.tab-item`/`.active`（标签项）、`Button.tab-btn`（标签）、`Button.tab-close`（标签关闭）、`Button.tree-row`/`.active`/`.sub`（左树行）、`Border.tree-panel.collapsed`（左树折叠态）、**`Border.tree-version`（左树底部「配置 · 版本」，v3.9 / v3.28 改）**、`Button.win-btn`/`.close`（窗口控制）、**`Border.status-bar` + `TextBlock.status-bar-text`（Shell 底部状态栏，v3.9）**、`Button.primary`/`.ghost`/`.fav-btn`、`Border.card`、`Button.col-header`、`Border.list-header`、`TextBlock.list-cell`、`ListBox.list-page`、**`Button.detail-toggle`（工具条末位的详情开关，v3.12；取代 v1.35 的 `Border.detail-hotspot` + `Button.header-toggle`）**。

> ⚠️ **已废弃（v3.3）**：`Button.nav-btn`（旧第一列图标）、`TreeView.tree-nav`。左树现在用 `ItemsControl` + `Button.tree-row` 自绘，**不用 `TreeView`**。

- ⛔ 页面里不复制同名样式；本页专用样式写 `UserControl.Styles` 里加更具体选择器。
- ⛔ 除标题栏图标外所有颜色绑 `DynamicResource`。

### 2.2 主题键（31 个，⛔ 除白名单外不新增）

主题资源键一律 `*Brush` 语义键，共 **31 个**（NT-049 删除零引用的 `TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush`；`ContentBrush/PanelBrush/SubSiderBrush/SiderBrush/PrimaryTextBrush/SecondaryTextBrush/AccentBrush/AccentHoverBrush/DangerBrush/DividerBrush/InputBorderBrush…`，完整清单以源码为准；白名单同步在 `skill/scripts/check.ps1`）。

> ⚠️ **v3.38 增补 2 个键**（需求 §4.1.1 ③ / §4.7，工作项 NT-039）：
> `WindowBorderBrush`（窗口外框 · **激活态**）｜`WindowBorderInactiveBrush`（窗口外框 · **失焦态**，更淡）。
> ⚠️ 明暗两套必须**同时补齐**；⛔ 除这两个键外不得再新增 —— 需要新色先想「能不能复用现有键」。
> ✅ **白名单免维护**：`skill/scripts/check.ps1` 是**动态扫描**（`x:Key="*Brush"` + 代码里 `S("*Brush")` 调用），
>    在 `ApplyTheme()` 里补了色值就算入白名单，**⛔ 不需要改脚本**。

**v3.0 新元素映射（不新增键，复用现有）**：

| 新元素 | 使用的键 |
|---|---|
| 标题栏背景 | **`ContentBrush`**（与窗口背景一致；底部 1px `DividerBrush`）（v3.4 由 `SiderBrush` 改回） |
| 标题栏按钮（工具 / 窗口） | 文字 / 图标 `PrimaryTextBrush`；hover 底 `CardHoverBrush`；关闭按钮 hover 仍用 `TitleBarCloseHoverBrush` |
| 标签栏背景 | 并入标题栏 → 同 `ContentBrush`（v3.4） |
| 标签项（普通） | 文字 `SecondaryTextBrush`；hover 底 `CardHoverBrush`（v3.4） |
| 标签项（激活） | 背景 `CardSelectedBrush`；文字 `PrimaryTextBrush`；下方 2px `AccentBrush` 指示条（v3.4） |
| 树一级行 | 文字 `PrimaryTextBrush` + **加粗**；图标 `SecondaryTextBrush`（v3.4） |
| 树一级行（激活 / 折叠态） | **行内 `Path` 图标 Stroke = `AccentBrush`**（`Button.tree-row.active Path` 选择器），折叠态也生效（v3.5） |
| 树二级行 | 文字 `SecondaryTextBrush`、缩进 16px（v3.4） |
| 树节点（激活） | `CardSelectedBrush` 背景 + `AccentBrush` 文字；**二级激活时其父级一级同用此态**（v3.4） |
| 树节点（hover） | `ContentBrush` 背景 |
| 右详情面板背景 | `PanelBrush`；与内容区之间用 `DividerBrush` 1px 竖线 |
| **窗口外框**（NT-039） | 激活 = `WindowBorderBrush`｜**失焦 = `WindowBorderInactiveBrush`**；厚度 1px，**最大化时收掉**。样式选择器 `Border.window-frame`（+ `.maximized` / `.inactive` 两个伪类），⛔ XAML 里不写本地值 |

> ⚠️ **v3.4 起标题栏底色跟随主题（`ContentBrush`）** → 标题栏上的文字 / 图标必须用 `PrimaryTextBrush` 等**主题色**，
> ⛔ **不要再用 `Sider*` 系**（那是深色侧栏专用；浅色主题下标题栏是浅底，白字会看不见）。
> 反之，若以后又把标题栏改回深色，**必须整体切回 `Sider*` 系**——**标题栏底色与文字配色要成套同步改**。

- 新增 Light/Dark 两套必须**同时补齐**；引用不存在键**编译不报错、运行期才失败**。
- 废弃旧键：`AppBackground/TitleBarBackground/TextPrimary/TextSecondary/BorderColor/CardBackground`（长键）、`AppBg/TitleBarBg/Text1/Text2/CardBg/PanelBg/SidebarBg/HoverBg/Border`（短键）。

### 2.3 图标（v3.25：框架层已全矢量；数据驱动图标待 NT-047）

> **当前状态**：**框架层（一级菜单 / 首页 / 标题栏 / 标签页 / 左树）= 矢量**（`Icon*` Geometry，描边型，NT-035 / NT-045 / NT-046 已实装）；**二级菜单不显示图标**；⚠️ **数据驱动图标**（二级功能菜单 `icon` / 首页一键按钮 / 模块页内数据行）仍是 **emoji 字符串** → 技术债 **NT-047**（⛔ 静态装饰 emoji 不动）。

- **矢量图标定义**：`Shell/App.axaml` 的 `<Application.Resources>` 里，`<StreamGeometry x:Key="IconXxx">路径数据</StreamGeometry>`（24×24 网格、**描边型**）。
- **渲染**：`<Path Data="{Binding VectorIcon}" Stroke="{DynamicResource SecondaryTextBrush}" StrokeThickness="1.5" Stretch="Uniform" Width="16" Height="16"/>` —— ⚠️ 描边型**不要**用填充型的 `PathIcon`。
- **键名规范**：`Icon<用途>`。框架层已定义（完整清单以 `App.axaml` 为准）：`IconHome`/`IconPc`/`IconWeb`/`IconVideo`/`IconSettings`/`IconMediaAssets`/`IconMenuPanel`/`IconRefresh`/`IconPuzzle`/`IconWinMinimize`/`IconWinMaximize`/`IconWinRestore`/`IconWinClose`/`IconFullScreen`/`IconExitFullScreen`/`IconTabClose`/`IconPlus`/`IconChevronDown`/`IconChevronRight`/`IconStar`/`IconZap`/`IconGear`。
- 🔴 **Shell 框架层图标只用 `Path`**（NT-045/046）：⛔ 图标字体 / 字符 / emoji / `Rectangle` 拼几何；粗细 / 端帽 / 拐角 / 颜色一律由按钮的后代选择器样式给，⛔ XAML 写本地 `Stroke`。
- 🔴 **`Stretch="Uniform"` 两坑**（NT-046）：几何必须有**非零宽高**（`M4,12 H20` 零高度包围盒会退化成「一个点」）；**扁几何**（横杠 / 线）顶边对齐偏上 ~7px ⇒ 线 / 横杠类用 `Stretch="None"` + 16×16 坐标（唯一例外 `IconWinMinimize` = `M2,8 H14`）。
- **取不到键要回退**：VM 解析失败返回 `null` → XAML 用 `IsVisible="{Binding HasVectorIcon}"` 在「矢量 / emoji」两种图标间切换（⛔ 不能让未知键显示成一串 `IconXxx` 文本）。
- 尺寸基线：**一级菜单 / 首页 / 标题栏 16px** ｜ 树节点 14px ｜ 标签页 14px ｜ 标签关闭 10px。
- 颜色：由 `Foreground` 继承，⛔ 不写死色值。
- **来源**：从公开线性图标集（如 Lucide / Feather，MIT 许可）取路径数据，**只取路径数据不引依赖**。风格统一（线性、同笔画粗细）。

---

### 2.4 窗口装饰（CSD）实现基线（v3.22 立 · NT-040 · 🔴 2026-10-15 定案采纳官方 CSD）

> 立项背景：标题栏在 v3.28 / v3.29 / NT-034 三轮里叠了 4 层 Win32 P/Invoke 补丁，越改越脆 ⇒ NT-039~042 回归框架正门。
> 🔴 **2026-10-15 定案**：当日曾短暂回退 NT-034 基线（`BorderOnly` + 样式位 + 零内缩），随后据**跨平台评估**（目标 Debian 13）重新采纳官方 CSD ——
> Wayland **无服务端装饰**、装饰要么合成器画要么客户端 CSD，贴靠 / 平铺由 Mutter / KWin 管，且**没有 DWM / `WS_*` 样式位 / GDI region** 这些概念
> ⇒ Win32 补丁路线在 Linux 上整体作废，官方 CSD 是唯一能对接的路线。口径：**框架能做的交给框架**（用户定调）。

**① 官方四件套（12.1.3 已核实存在；v3.43 栈升 12.1.4，API 面不变，待本地编译复核）**

| API | 说明 |
|---|---|
| `Window.ExtendClientAreaToDecorationsHint` | 激活客户区扩展；也是 `ElementRole` 生效的前提 |
| `Window.ExtendClientAreaTitleBarHeightHint` | 自绘标题栏高度（-1 = 用 OS 默认） |
| `Window.WindowDecorations` | 枚举实测只有 `None` / `BorderOnly`（有边框无标题栏）/ **`Full`** |
| `Window.WindowDecorationsTheme` | 应用画窗框的正门（`ControlTheme` `TargetType="chrome:WindowDrawnDecorations"`） |
| `Window.WindowDecorationMargin` | 边框 + 标题栏占掉的厚度（内容应 inset 的 margin） |
| `Window.OffScreenMargin` | 最大化时被甩到屏幕外的边框厚度 |
| `WindowDecorationProperties.ElementRole` | `TitleBar` / `User` / `ResizeN…SW` / `CloseButton` / `MinimizeButton` / `MaximizeButton` / `FullScreenButton` / `DecorationsElement` |
| `chrome:WindowDrawnDecorations` | `Template` / `DefaultFrameThickness` / `DefaultShadowThickness` / `DefaultTitleBarHeight` / 运行时 `FrameThickness` / `ShadowThickness` / `HasBorder` / `HasShadow` / `HasTitleBar` / `EnabledParts` / `EffectiveGeometryChanged` |

**② Win32 硬契约（踩过坑，别再试）**

- Win32 上 `RequestedDrawnDecorations` **只有 `TitleBar`** → **`:has-border` / `:has-shadow` 恒 `false`**；边框 / 阴影 / resize 抓手归 **DWM**（或应用自绘）。
- ⇒ 任何依赖这两个伪类的样式在 Windows **不会触发**；要阴影 / 边框必须写**无条件选择器**。
- ⇒ 开了客户区扩展后 **DWM 把边框交给应用画** → **应用不画就没有边界**（NT-039 自绘 1px 外框的由来）。
- ⇒ DWM 窗口阴影在客户区扩展下**本来就不存在**（NT-042 基准实测：不套 region 时同样没有）→ ⛔ 别把它算作 `SetWindowRgn` 的代价。
- 模板 PART 缺失会在 `OnApplyTemplate` **抛 NRE** → 前提是**有模板**；本配置不设 `Template`，该风险不成立。
- 🔴 **贴靠残留分两种，别混**（2026-10-15 实测：贴靠第 1 次缝小 ~1px、第 2 次变大 ~8px）：
  - **≈8px（左右下）= 非客户区那圈边框**：Avalonia CSD 按 `WS_CAPTION|WS_THICKFRAME` 扣出 ≈8px 非客户区，贴靠态系统不画那圈 → 露缝；**由「零内缩」消除**（Windows-only 例外）✅ **2026-10-15 已验收**（连续贴靠 8px 消失）。第 2 次才变大 ⇒ 是 Avalonia 按「外框 = 客户区 + 边框」的记账回写了几何、赢过系统的贴靠矩形。
  - **≈1~2px（仅第一次分屏、左右下偏大；常规态 = 左半屏重叠 / 右半屏缝隙）= 系统取整 / 动画帧落位**，**Firefox 同复现** ⇒ **接受**，⛔ 不做外部几何纠偏（NT-048 那套已证伪并删除）；「首次贴靠偏宽」同属系统行为。

**③ 现役配置（`ShellView.axaml`）**

```
WindowDecorations="Full"
ExtendClientAreaToDecorationsHint="True"
ExtendClientAreaTitleBarHeightHint="40"
WindowDecorationsTheme="{DynamicResource {x:Type chrome:WindowDrawnDecorations}}"
```

- `Full` 只为了拿到**完整标准窗口样式**（贴靠资格 / 系统菜单 / 最小化动画）；可见 UI 仍是自绘标题栏。
- 主题 = `App.axaml` 的 `ControlTheme`（`TargetType="chrome:WindowDrawnDecorations"`）：**只顶掉默认主题，🔴 故意不给 `Template`**（无模板 → 框架不应用 → 不画任何装饰视觉）。
- 🔴 **键必须是隐式键 `{x:Type chrome:WindowDrawnDecorations}`**（⛔ 自定义字符串键 + `WindowDecorationsTheme` 指它 **不生效**，Fluent 默认装饰主题照画 —— 2026-10-14 实测：标题文字 / 窗口图标 / caption 按钮全叠上来）。
- 🔴 **12.1.3 版本事实（编译实测，⛔ 别再试；12.1.4 同）**：`Avalonia.Controls.Chrome` 公共类型只有 6 个（`WindowDrawnDecorations` / `WindowDrawnDecorationsContent` / `IWindowDrawnDecorationsTemplate` / `DrawnWindowDecorationParts` / `WindowDecorationProperties` / `ResizeGripLayer`）；⛔ **没有 `WindowDrawnDecorationsTemplate` 具体类**（internal），`Template` 属性是**接口** ⇒ 写自定义装饰模板必报 `AVLN2000`。⇒ 「自定义装饰模板」这条路不存在。
- 主题引用用 **`DynamicResource`**：装饰主题在合并字典完成前就可能被解析，`StaticResource` 那一刻取不到（AtomUI 指南 §15 的已知坑）。

**④ Win32 手段清单（其余一律删除）**

| 手段 | 现状 |
|---|---|
| `Win32Properties.SetWindowCornerPreference`（官方通道） | ✅ 保留（NT-034 ①） |
| `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33)` = `DONOTROUND`(**1**)；写成 2 = 强制圆角（踩过） | ✅ 保留（NT-034 ②；必须在 Avalonia **处理完之后**压，见 `PostChromeReassert`） |
| `SetWindowRgn` 矩形区域（真正保证方角那一环） | ✅ 保留（NT-034 ③ / NT-042 裁决；技术债 **B16**） |
| 手工补 `WS_CAPTION` / `WS_THICKFRAME` 样式位（`ApplySnapEligibleStyles`） | ⛔ **已删**（NT-041）：官方 `Full` 自带贴靠所需样式位 |
| 接管 `WM_NCCALCSIZE` 做「零内缩」 | ✅ **保留（Windows 独有的例外）**：Avalonia CSD 扣掉 ≈8px 非客户区，贴靠态系统不画那圈 → 第 2 次贴靠起露 ≈8px 缝；零内缩让「客户区 = 外框」消除之（钩子仅在 Windows 装得上，Linux 自然不走） |
| 贴靠几何外部纠偏（期望比对 / 屏幕几何自检） | ⛔ **已删**（NT-048）：「首次偏宽」是系统行为（Firefox 同复现） |
| `DwmGetWindowAttribute(33)` | ⛔ **不支持读**，永远 -1，别当判据 |
| 诊断写 `Window.Title` | ⛔ 无边框自绘窗口标题栏不显示 Title |

**⑤ 跨平台（Debian 13 / Wayland）注意 —— 迁 Linux 前必修**

- 官方 CSD 是**唯一**能对接 Wayland 的路线（Linux 无 DWM / `WS_*` / GDI region）；Windows 侧保留的方角三保险在 Linux 上**整体无效**（会走 `DllNotFoundException` 被吞）。
- 🔴 **自绘 1px 外框必须门控**：框架已画边框时（`WindowDecorationMargin.Top > 0`，见 §3.7 / 需求 §4.1.1 ③）⛔ 不重复画 —— ⚠️ **当前未实装**（全仓无 `WindowDecorationMargin` 引用，`Border.window-frame` 无条件绘制）⇒ 迁 Linux 前必须补。
- 🔴 **窗口位置 / 大小记忆**现走 user32（`SetWindowPos` / `GetWindowRect` / `GetWindowPlacement`）→ 非 Windows 抛异常被吞 ⇒ **记忆失效**；迁 Linux 前需回退 Avalonia 的 `Position` / `ClientSize` / `Resize()`（并重算「外框 vs 客户区」）。

**⑥ 跨平台门控**：判据 = **`WindowDecorationMargin.Top > 0`**（详见需求 §3.7）；⛔ 不用 `OperatingSystem.IsWindows()` / `#if` / `x:OnPlatform`（加载期一次性求值，响应不了运行期切换）。

---

## §3 模块实现细节

### 3.1 IModule 接口

```csharp
public interface IModule {
    void RegisterServices(IServiceCollection services);
    void Initialize(IServiceProvider services);   // 这里注册菜单
}
```

模块类命名 `XxxModule`，命名空间 `NewToys.<模块名>`（不带 `.Module` 段）。

### 3.2 菜单注册 API

```csharp
AppMenuGroup RegisterModule(string key, string title, string icon, int order,
                           bool isBottom=false, bool isHome=false);
void RegisterFunction(string moduleKey, string key, string title, string icon,
                      string viewName, int order=0, string desc="");
```

- 先 `RegisterModule` 拿分组，再 `RegisterFunction`（找不到分组抛 `InvalidOperationException`）。
- `viewName` 传**视图类名**（如 `"PcInfoView"`），不是 VM 名。
- ⭐ **命名铁律**：`Shared/Models` 里的模型名不得与所用库（Avalonia / CommunityToolkit / BCL）公开类型重名（菜单模型即因 `MenuItem` 撞名而改名 `AppMenuGroup`/`AppMenuItem`）。
- **菜单只能在 `Initialize` 注册**。
- 菜单 Key：`<模块缩写>-<页面名>` 全小写连字符。
- **`icon` 字段（v3.3 起实际启用）**：**一级分组** `RegisterModule` 的 `icon` 传 **Geometry 资源键**（如 `"IconPc"` / `"IconWeb"` / `"IconVideo"` / `"IconSettings"`）；**二级功能** `RegisterFunction` 的 `icon` **仍是 emoji**（左树不显示；首页收藏卡 / 功能清单页继续用它）。
- 分组 Order 现状：`pc`=20 ｜ `web`=30 ｜ `video`=40 ｜ **`settings`=999, isBottom:true**。`isBottom` 现仅语义标记「设置组」，**不再影响视觉位置**（v3.0 标题栏 ⚙ 直接打开设置页；树里设置组排在末尾）。新增分组**必须显式给 Order**。
- 排序：`OrderBy(Order)`；分组内 `Items` 按 `Order` 重排。
- 🔴 **跨组注册 + 延迟补登（v3.20，需求 §3.4）**：模块可把菜单项注册进**别的模块的分组**（典型 = 模块设置页进 `settings` 组）。`RegisterFunction` 发现分组未注册时**暂存**该菜单项（⛔ 不抛 `InvalidOperationException`——`ModuleLoader.InitializeAll` 的逐模块 try-catch 会把它吞掉，Release 目录扫描顺序不保证 → 菜单项**静默丢失**）；待 `RegisterModule` 建立该分组时**补登**（去重 + 按 Order 重排）。分组始终未建立（如 `Module.Settings` 缺失）→ 暂存项不显示，由启动健全性检查报一行兜底（§3.3）。
- `desc`（功能简介）务必填写。
- 菜单点击路径：`ShellViewModel.OpenTab(key)` → 新建/激活 `TabItemViewModel` → View 解析 → 写入 Tab 的 `Content`。

### 3.3 模块加载（启动双轨）

`App.OnFrameworkInitializationCompleted()` 顺序（不可颠倒）：

1. 建 `ServiceCollection` 注册核心服务；
2. `ModuleLoader.LoadAll(services, GetMandatoryModuleTypes())`：**必载清单**（`Module.Settings` 置顶 + Debug 追加其余功能模块）始终加载，**再扫** `<程序目录>\Modules\` 补功能模块（跳过已加载同名，如 Shared/Core）；
3. `BuildServiceProvider()` → `AppServices.Initialize(sp)`；
4. `ModuleLoader.InitializeAll(sp)`（**必须在 3 之后**）；
5. 应用主题（订阅 `ThemeChanged`）；
6. `new ShellView{DataContext=ShellViewModel}→win.Show()`；
7. 首导由 `ShellViewModel` 构造函数末尾 `OpenTab("home")` 触发。

| | Debug | Release |
|---|---|---|
| 模块源 | `Shell.csproj` Debug 条件 `ProjectReference` | `<程序目录>\Modules\` DLL |
| **`Module.Settings`（例外 = 框架组件）** | **无条件 `ProjectReference`，随 `NewToys.exe` 出根目录** | 同左；props 开例外，⛔ 不进 `Modules\` |
| 需要 `Modules\` | 否 | 是（无则兜底程序目录） |
| MSBuild 拷贝 | 否 | 否（OutDir 直出） |

**硬规则**：
- Debug 的功能模块必须走显式类型清单（⛔ 不能只靠 `AppDomain.GetAssemblies()`）。
- 跳过**功能模块**改两处：`App.axaml.cs` 的 `typeof(XxxModule)` + `Shell.csproj` 的 `ProjectReference`。⛔ `Module.Settings` 不适用此条——它是无条件引用 + 必载清单置顶，跳过它 = 卸框架。
- Release 路径用 `AppContext.BaseDirectory` 拼绝对路径。
- 模块 DLL 命名 `Module.<模块名>.dll`。
- ⛔ **`Modules\` 是模块产物唯一去处，全程零拷贝**：`src/Directory.Build.props` 把模块 Release `OutDir` 直设为 `$(NewToysReleaseDir)Modules\`。（`Module.Settings` 例外：框架组件直出根目录，见下条。）
- Release 关 pdb（`DebugType=none`）消除并发写 `MSB3026`。
- **必备模块（物理必备，v3.25 用户拍板；需求 §3.4）**：`Module.Settings` = **框架组件**——`Shell.csproj` **无条件** `ProjectReference`（⛔ 不再走 Debug 条件组）；`src/Directory.Build.props` 的模块 `OutDir` 规则对它**开例外**（Release ⛔ 不进 `Modules\`，只随 `NewToys.exe` 出**程序根目录**）；启动**第一个加载**（必载清单置顶）；缺失 = 启动失败（同 Shared/Core 硬依赖，⛔ 不做降级）。`scripts/build.ps1` 的产物计数校验**同步改为只数功能模块**（2026-10-13 用户授权改构建脚本；`Modules\` 自此只含功能模块 DLL）。`InitializeAll` 后**健全性检查**：`settings` 分组仍不存在（典型 = `SettingsModule.Initialize` 自身抛异常被逐模块 try-catch 吞了）→ 日志 + Shell 状态栏一行警告，⛔ 不静默。

### 3.4 新增模块步骤

1. 建 `src/Module.Xxx/Module.Xxx.csproj`：`RootNamespace=NewToys.<Xxx>`、`AvaloniaUseCompiledBindingsByDefault=false`，引 `Shared+Core`。
2. 写 `XxxModule.cs : IModule`，命名空间 `NewToys.<Xxx>`。
3. `src/NewToys.slnx` 加 `<Project Path="Module.Xxx/Module.Xxx.csproj" Type="C#" />`。
4. **Debug 链路两处登记**：`Shell.csproj` Debug 条件 `ProjectReference` + `App.axaml.cs` 的 `#if DEBUG` 清单。Release 不需登记。（`Module.Settings` 是框架组件，不走本条——见 §3.3 必备模块。）
5. `Initialize` 里 `RegisterModule`+`RegisterFunction` 注册菜单，`viewName`=View 类名，`icon`=Geometry 键。
6. **（有设置页时，v3.20）** 设置 View/VM 放**本模块** `Views/SettingsXxxView.axaml(+.cs)` + `ViewModels/SettingsXxxViewModel.cs`；`Initialize` 里 `RegisterFunction("settings", "settings-<模块键>", …, Order)` 注册进 `settings` 分组——**Order 从 100 起、每模块独占一段（+10）**，0~99 保留给框架设置（跨组注册走延迟补登，§3.2）。设置模型类被两模块共用时放 `Shared`（⛔ 双真相源）；设置落 `config\*.json`，模块只读、唯一写入点 = 该设置页。

### 3.5 页面开发与 VM 定位

- View：`UserControl`，放模块 `Views/`，扩展名 `.axaml`+`.axaml.cs`。
- VM：新增 VM **跟同模块已有 VM 保持一致**（现状模块内多直接继承 `ObservableObject`，见 B2）。
- 定位：`NewToys.<模块>.Views.XxxView` ↔ `NewToys.<模块>.ViewModels.XxxViewModel`，必须成对。
- 解析（`ShellViewModel.ResolveView`）：① 按 `ViewTypeName` 遍历**全部已加载程序集**匹配 `Type.Name`（`Control` 子类）**取首个命中** → ② `ActivatorUtilities.CreateInstance` 建视图 → ③ 推导 VM 时**优先限定在 View 自己的程序集**（`.Views`→`.ViewModels`、`XxxView`→`XxxViewModel`），找不到才降级全 `AppDomain` 扫类名。
  > ⚠️ **View 解析这一步未按模块限定程序集** → 各模块出现同名 View 时会撞车（现状两处 `DnsView`）。
- 无占位页：兜底是 `ShellViewModel.BuildErrorView()` 现场构造的红字+`ScrollViewer`。
- 可以先注册菜单、后补页面。
- **v3.0 缓存语义**：View 实例**由 `TabItemViewModel` 持有并缓存**，切标签不重建；**关闭标签时才销毁**（若 VM 实现 `IDisposable` 则调用）。

### 3.6 列表页实现细则（v3.2 调整）

> 需求 §4.8：列表页内部**不再自绘 340px 右列**，改由 Shell 右详情面板承载 —— **⏸ 本轮暂缓**。本节以下为**目标形态**；本轮各页维持页内右列，不迁移。

**三列布局（v3.7 落地版）**：详情仍留在页内，那就把它做成「**列表列 ｜ 详情列**」+ 左树 = 三列：
- 页面根 Grid **保持单行**；把原来的 `Row0~Row2`（状态行 / 工具条 / 筛选条）**整体挪进左列** —— 左列自己再分 `RowDefinitions="Auto,Auto,Auto,*"`；
- 详情卡 `Grid.Column="1"` **垂直拉伸**（`VerticalAlignment="Stretch"`），从页面顶到底；
- ⛔ 工具条**不再横贯整页宽**；⛔ 不要改共享样式 `Border.detail-card`（宽度 / 内边距仍由它给）；
- ✅ 已改 **10 页**（样板 `video-list` / `pc-package` + `pc-shortcuts` / `pc-quicksettings` / `pc-info` / `web-bookmark` / `web-ping` / `web-scrape` / `web-hosts` / `settings-path`）；⏳ `video-cut` / `video-transcode` 待视频工具重构后再定。

**两个 Avalonia 样式坑（v3.22 实测）**：

1. **`MenuItem` 用 `ItemsSource` 生成子菜单时，`ItemContainerTheme` 必须写 `BasedOn="{StaticResource {x:Type MenuItem}}"`** —— 不写就是一份**没有模板**的全新 ControlTheme，子项画不出来 → 表现为「点了不弹出子菜单」。全仓 11 处筛选子菜单都犯过这个错。
2. **给控件覆写 `:pointerover` 等伪类样式，若被 Fluent 主题盖掉（实测表现为"图标还是白的、背景还在"），就改用**后代选择器**提优先级**：`Border.tab-item Button.tab-close:pointerover` 能压过主题的 `Button:pointerover`。
3. 🔴 **后代选择器也压不住时（Fluent 的 `Button` 会在模板状态里自己画背景 + 改前景），就换"裸控件主题"**：`App.axaml` 的 `BareButtonTheme`（只有 `ContentPresenter`，零状态绘制）→ 控件上写 `Theme="{StaticResource BareButtonTheme}"`。标签页关闭 ✕ 就是这样才真正做到"透明底 + 红图标"。**以后凡是要完全自绘的小按钮，一律套它。**
4. 🔴 **「裸主题里 `Path` 渲染不出来」（v3.22 旧结论）—— NT-045 定位 + 修掉，NT-046 实机取证**：
   - 真因（NT-045 推断，**NT-046 已由用户实机截图取证**）：原 `BareButtonTheme` 模板把 Button 的 `HorizontalContentAlignment`
     绑到 `ContentPresenter` **自身**的 `HorizontalAlignment` → Presenter 不再撑满、改用**无限约束**测量内容
     → `Path`（`Stretch=Uniform`）在 ∞ 约束下算出 NaN 尺寸 → 图标整个不渲染（这解释了「`TextBlock` / `Rectangle` 正常、唯独 `Path` 消失」）。
   - 改法：TemplateBinding 到 `HorizontalContentAlignment` / `VerticalContentAlignment`（与 Fluent 模板一致）。
     ✅ 2026-10-15 用户实机截图：窗口按钮的方框 / ✕ 等 `Path` 图标**全部正常显示** ⇒ 结论成立，裸主题里可以放心用 `Path`。
   - ⚠️ 历史留档：v3.22~NT-044 期间该位曾用 `TextBlock` + 系统图标字体 `Segoe MDL2 Assets` 字形规避（`U+E921/E922/E923/E8BB/E740/E73F`）；
     NT-045 起**图标字体已全部弃用**。
5. 🔴 **框架层图标硬约束（NT-045 统一，NT-046 写死）—— `Shell` 框架层图标「只用 `Path`」**：
   **Shell 框架层（标题栏 / 窗口按钮 / 标签栏 / 左树 / 首页 / 状态栏）的图标，一律是
   `<Path Data="{StaticResource IconXxx}" …/>` + 本仓 `App.axaml` 的 `StreamGeometry` 键，别无第二种载体。**
   - ⛔ 不用图标字体（`Segoe MDL2 Assets` 那套已废）、⛔ 不用字符（`≡ ✕ ▾ ▸ ＋ ★ ⚡ ⚙`）、⛔ 不用 emoji、
     ⛔ 不用 `Rectangle` / `Border` 拼几何。
   - 几何**一律 24×24 网格**（与 Lucide / Feather 同网格，摘公开图标集路径数据不用换算）。
   - 🔴 **几何必须有非零宽高**（NT-046 实机踩坑）：`Stretch="Uniform"` 靠 2D 包围盒算等比缩放，
     **零高度 / 零宽度**的几何会退化成一个点（最小化图标原写作 `M4,12 H20` 就是这样变成"一个点"的）。
   - 🔴 **等比缩放还会把"扁几何"贴到方框顶部**（NT-046 第二坑）：包围盒只有 1~2 单位高的几何（横杠）用 `Stretch="Uniform"`
     时缩放后**顶边对齐方框顶边** → 图标偏上（实机偏上约 7px）。
     ⇒ **唯一的例外**：`IconWinMinimize`（横杠）几何写在 **16×16 控件坐标**里（`M2,8 H14`，`y=8` 居中），
     用法是 `<Path Width="16" Height="16" Stretch="None" …/>` —— 不做缩放、坐标即像素，居中由数据本身保证。
     ⛔ 其余图标仍一律 24×24 网格 + `Stretch="Uniform"`。
   - 使用方式固定：`<Path Data="{StaticResource IconXxx}" Width="16" Height="16" Stretch="Uniform"/>`；
     粗细 / 端帽 / 拐角 / 颜色**全由各自按钮的后代选择器样式给**（`Button.title-btn Path` / `Button.win-btn Path` /
     `Button.tab-new Path` / `Button.tab-close Path` / `Button.tree-row Path`）→ XAML 里只留 `Data` + 尺寸，⛔ 不逐处写描边。
     ⛔ 也不写本地 `Stroke`（本地值会压过 `:pointerover` / `.active`）；例外只有**无状态装饰图标**
     （首页 ★ / ⚡ / ⚙ 这类不参与状态的），可用 `Path.icon-accent` / `Path.icon-primary` 类给死色。
   - 状态图标（最大化 / 还原、全屏 / 退出全屏）用**两个 `Path` 叠放 + `IsVisible` 切换**，切换在 code-behind 里做
     （`ShellView.UpdateMaxIcon()`）。
   - 键名必须在本仓 `App.axaml` 定义 —— 模块 `RegisterModule` 的一级 `icon`、左树 `ResolveIcon()`、设置页「功能清单」都按键名取；
     **键名不存在就地没图标**（emoji 回退分支已删）。`IconChevronDown/Right` 目前**无引用**（左树行首展开箭头已按用户要求去掉，NT-046），
     留备后续可折叠区块，⛔ 别因"没人用"删除。
   - 范围外（登记技术债，⛔ 不得当作违反本约束）：**数据驱动图标** —— 首页「常用功能」按钮（`AppMenuItem.Icon`）与
     「一键按钮」（用户在设置页配置的 `Icon`）仍是字符串 / emoji。用户 2026-10-15 明确：**其他没改的以后再改**
     → 已登记 **§7 B15**，收敛工作项 **NT-047**。

**列表页内的硬规则（保留）**：
1. 表头与行模板**完全相同的星号列定义**（逐字一致）；动态列用 `<v:ColumnGrid ColumnSpecs="{Binding VisibleColumnSpecs}">`。
2. 表头与行单元格左右内边距一致（8px），行里不写 `Padding/Margin`。
3. `ListBoxItem` 拉伸（已在 App.axaml 设 `HorizontalAlignment=Stretch`），页面不覆盖。
4. 表头双交互：① 左键点→切换排序（`SortCommand`+CommandParameter，VM 追加▲▼）；② 右键点→`ContextMenu` 勾选列显隐（`ToggleType=CheckBox`，`IsChecked` 显式 `Mode=OneWay`）。
5. 空态与选中态显式处理：无选中显示引导；有选中显示详情；空列表用居中 StatusText，不留白。
6. `ContextMenu` 挂在独立弹出树，不保证继承宿主 DataContext → 必须在 View 的 code-behind `OnDataContextChanged` 里手工同步（行菜单+表头菜单都要）。
7. 列显隐：元数据 `Shared/Models/ListColumn`、服务 `IColumnLayoutService/ColumnLayoutService`（落 `config/columnlayout.json`）。隐藏列用宽度 0（不删列保一致）。

**列宽三态 + 手动拖拽（v3.20，需求 §6.4.15）**：

- 三态：**星号比例**（`Columns` 元数据默认值，如 `3.2*`）→ **手动像素**（拖表头边界产生，规格串写 `NNN`）→ **隐藏**（`0`，列索引保持不动）。
- 表头两列交界处 5px 透明把手：`ColumnGrid.IsResizable="True"` 时自动生成（**只生成在相邻两个可见列之间**，隐藏列边界没有）；`Cursor = new Cursor(StandardCursorType.SizeWestEast)`；拖动经 `ColumnResize` 事件 → VM `ResizeColumns(i, w, i+1, w)`（基类 `ListPageViewModelBase` 提供）→ `VisibleColumnSpecs` 重出 → 表头与行**同规格重建**。每列 ≥24px、左右列总宽守恒；⛔ 不拖最后一列右缘。
- ⛔ **没有「自动调整列宽」开关**（v3.20 全模块删除：可见列整体切 `Auto` 时，长内容列按**未截断全文**测宽，把其余列挤到接近 0 → 表头文字被裁没，像"整行消失"）。
- 手调列宽**只存会话**（⛔ 不落 `columnlayout.json`）：切标签页不丢、重启还原；列显隐切换不影响已手调宽度。
- 🔴 `Views/ColumnGrid.cs` 因**模块间禁互引**在 `Module.PcTools` / `Module.WebTools` / `Module.VideoTools` / `Module.Settings` / `Module.MediaAssets` **各存一份（共五份），改动必须同步五处**。
8. 🔴 **跨页投递的数据在 `OnAttachedToVisualTree` 取**（不是 `OnDataContextChanged`）：标签页已存在时切回来只重新挂可视树，`DataContext` 不变 → 后者**不再触发**，症状是"送过去的文件 / 任务要刷新页面才出现"。`VideoTaskStore.TakePendingCut/TakePendingTranscode` 是取一次即清空的，重复调安全。

**详情开关的位置（v3.12：表头 → 工具条末位）**

- 旧做法：表头最后一列 `Border.detail-hotspot`（64px 透明热区）套 `Button.header-toggle`，收起态只露 8px、鼠标靠近才弹出 34px。
  **已废除** —— 它① 占掉一整列表头宽度（`Columns` 里还得追加一个 `Detail` 占位列，宽度 36）；② 不把鼠标移过去就点不到；③ 和表头"排序 / 列显隐"两种交互挤在一处。
- 现做法（需求 §4.5 / §4.8 硬规则 8）：**工具条末位一个常驻小按钮** `Button.detail-toggle`，图标 `❯` = 展开右侧详情、`❮` = 收起。
- 工具条布局：`Border.toolbar` 内换成 `Grid ColumnDefinitions="*,Auto"` —— 左列放原来的 `WrapPanel`（命令区），右列放开关（`HorizontalAlignment="Right"`）。
- 相应地，`ListPageViewModelBase.Columns` 里**不再追加** `new("Detail","","36")`；基类里 `if (c.Key != "Detail")` 这类特判**全部删掉**（`SaveVisibility` / `NotifyColumnsChanged` / `EnsureFilterColumns` 各一处）。
- ⛔ **别删 `Border.detail-card` 的宽度**；改它的内边距会波及 12 个列表页的滚动条位置（见下一条）。

**主显示区（工具条 / 筛选条）实现范例（v3.41，需求 §4.8.4；样板 = 媒体资产「全部资产」页）**

> 参考实现：`Module.MediaAssets/Views/AssetAllView.axaml`（工具条 + 筛选条）+ `AssetAllViewModel`（`IsToolbarExpanded` / `ToggleToolbarCommand`）。
> 该页被 5 个类型薄壳页（视频 / 音乐 / 书籍 / 图片 / 文档）内嵌复用（`<views:AssetAllView/>` + VM 继承 `AssetAllViewModel`）
> ⇒ **一份实现覆盖 6 页**；⛔ 不要逐页复制工具条 / 筛选条。

**① 工具条「最多两行 + 按宽度自动折叠」**

- 容器结构：`Border.toolbar` → `Grid ColumnDefinitions="*,Auto"` → 左列 `WrapPanel x:Name="ToolbarPanel"`（`ItemSpacing=8` + `LineSpacing=4`）+ 右列 `Button.detail-toggle`。
- 🔴 **约定：`ToolbarPanel` 的「最后一个子元素」就是那个「⋯」按钮**；其余按 XAML 顺序 = **优先级顺序**（越靠前越不会被折）。
- **判定逻辑 = code-behind 的 `ApplyToolbarLayout()` + `FitTwoRows()`**（`Module.MediaAssets/Views/AssetAllView.axaml.cs`）：
  **各项宽度在首次布局后缓存一次** → 之后每次只按"容器宽 + 缓存宽"贪心算"**两行**内能放下几项"（先给末尾「⋯」留出占位）→
  **放得下就全显示、放不下才折尾部**。
  展开态（`IsToolbarExpanded == true`）= ⛔ 不受宽度限制、全显示；容量没变化时**幂等短路**（⛔ 不重复赋 `IsVisible`）。
- **触发点**：`ToolbarPanel.LayoutUpdated`（⚠️ 用 `LayoutUpdated` 而⛔ 不是 `SizeChanged` —— 首次必须等**子元素**量出宽度）+ `OnAttachedToVisualTree` + `OnDataContextChanged`（换 VM 时 `HookToolbarVm()` 重挂 `IsToolbarExpanded` 监听并重置容量缓存）。
- 🔴 **为什么不会布局循环、不会抖动**（⛔ 别改成"每次重新测量"的写法）：项宽**只在首次布局后测一次并缓存**，
  之后折叠结果**不反过来影响计算输入**（容器宽由窗口决定，与"折了哪些按钮"无关）⇒ 不会出现
  "改可见性 → 重新布局 → 再测量"的循环，也不会在窗口拖动时让按钮反复进出「⋯」。
  ⛔ **也不要用 `Measure(∞)` 每帧重新测**：那正是循环 + 滞回的来源，而工具条按钮宽度基本不变，毫无收益。
- 「⋯」按钮：`<Button Classes="ghost" Width="32" MinWidth="32" Command="{Binding ToggleToolbarCommand}">`，内容用 `Panel` 叠两个
  `TextBlock`（`⋯` / `▲`）按 `IsToolbarExpanded` 切 `IsVisible`（写法同 `Button.detail-toggle` 的 `❯/❮`）；固定宽度 ⇒ 开合不引起重新换行。
- ⛔ **XAML 里这些控件⛔ 不要写 `IsVisible`**：可见性由 `ApplyToolbarLayout()` 运行期赋值，写了绑定会与本地值打架。
- 🔴 **⛔ 不要用 `ToggleButton` 做开合**：全局只定义了 `Button.ghost` / `SplitButton.ghost` 的样式，`ToggleButton` 会掉回 Fluent 默认外观。
- 🔴 **⛔ 不要把被折的控件搬进 `MenuFlyout` / `ContextMenu`**：弹出树不继承 `DataContext`（§3.8 老坑），`Click="…"` 接线还得逐条转发。
- `IsToolbarExpanded` **⛔ 不落盘**：每次进页面回到收起态。
- ⚠️ **调参口径**：**没有"档位数字"可调** —— 能显示几项由"容器宽 + 缓存项宽"算出来。要换"哪个按钮先被折"，
  改 XAML 里的排列位置（优先级）即可；⛔ 不要为了让某个按钮常显而给它写死 `IsVisible`（会被运行期赋值覆盖）。
- ⚠️ **改了按钮文案 / 字号后**（项宽会变）：缓存会在**下一次重建可视树**时自动重取（切页回来 / 重开标签即生效），
  ⛔ 不需要为此加"监听每个控件尺寸"的机制。

**② 筛选条「恒一行、无滚动条」**

- `Border.filter-bar` 内改成 `Grid ColumnDefinitions="Auto,*,*,*,*" ColumnSpacing="5"`（标签列 `Auto` + 各筛选项 1 份星号）⇒ 窗口变窄时星号列**自己收窄**，永不溢出 ⇒ 无需滚动条。
- 每个 `ComboBox`：`MinWidth="0" MaxWidth="120" Padding="4,1" FontSize="11" HorizontalAlignment="Left"`。
  🔴 `MinWidth="0"` **是必须的** —— 全局样式 `ComboBox.filter-combo` 给了 `MinWidth=110`，不覆盖它则星号列收不下去、窗口一窄就溢出。
- ⛔ 不要 `WrapPanel`（放不下就换行、条高抖动）；⛔ 不要 `ScrollViewer`（需求 §4.8.4 明确不要滚动条）。
- 间距用 `Grid.ColumnSpacing`（与 §4.10.4 同一条要求的口径），⛔ 不给每个控件写右 `Margin`。

**③ 「需先勾选」按钮的提示词（禁用态可读）**

- 这类按钮（加标签 / 加集合 / 移入回收站 …）必须同时写 `ToolTip.Tip="先在列表里勾选行，再点这里…"` + **`ToolTip.ShowOnDisabled="True"`**。
- 🔴 坑：Avalonia 的 `ToolTip` **默认不在 `IsEnabled=False` 的控件上显示** —— 不加该属性，"提示词"等于没写（用户只看到一个灰按钮、不知道要先勾选）。
- 因此 VM 侧**不需要** `CheckedText` 这类"未勾选 / 已勾选 N 个"指示值（v3.41 已从 `AssetAllViewModel` 删除；⚠️ `PcTools/PackageManagerViewModel` 里的同名属性属该模块自己的，⛔ 不要顺手删）。

**详情卡滚动条（覆盖式，v3.12 记法）**

- Avalonia 12 的 `ScrollBar` 浮在内容之上，**槽落在 `ScrollViewer` 右缘之外 8px**（偏移写死在 Fluent 模板的 `Margin` 里，改不动）。
- 于是只能二选一：① `ScrollViewer` 顶到卡片边 → 槽探出卡片 8px、拇指压在边框上；② 正常收在卡内 → 本页 `Border.detail-card` 覆盖 `Padding="12,12,4,12"`、滚动内容 `Margin="0,0,4,0"`。
- **统一走 ②**：需要滚动条的详情卡都按这两处写（`video-cut` 从 v3.10 起就这么做，v3.12 推广到 `video-batch`）。
- 🔴 **v3.29 / NT-054 起（声明式卡）**：这两处**只写在 Shell 一处** —— `Shell/Controls/DetailCardHost.axaml`（内容 `Margin="12,12,4,12"`）。
  迁移到声明式卡后页面**不必也不能**再覆盖 `Padding`（页面连卡片 XAML 都没有了）。

**v3.0 列表页右列改动**：

- 页面 `ColumnDefinitions` 简化为**单列（`*`）**；列表铺满内容区。
- **详情内容抽成独立 `UserControl`**，放 `Views/Detail/<Xxx>DetailView.axaml`（模块内），由 VM 通过 `IDetailPanelProvider.DetailContent` 暴露。
- **VM 实现 `IDetailPanelProvider`**（契约见 §3.8）。
- **表头 `▤` 按钮**：绑 `ToggleDetailCommand` → 调 `IDetailPanelProvider.ToggleDetail()`。
  ⚠️ **v3.12 已改**：不再放表头，改为**工具条末位的 `Button.detail-toggle`**（见 §3.6）。
- **右键菜单首项「▤ 显示详情栏」**：调 `IDetailPanelProvider.OpenDetail()`。
- **详情开合状态**：由 Shell 统一落 `config/columnlayout.json`（`FlagDetailPanel`，**按页面 Key 分别记忆**），列表页 VM 不再直接读写。
- 涉及页面（5 个）：`pc-package` / `video-list` / `pc-shortcuts` / `pc-quicksettings` / `settings-path`。

- **示例值 + 固定表头宽列（v3.22）**：
  - 每列给 `ListColumn.Sample`（示例值）；基类暴露 `SampleRowTexts`（示例行文本数组）/ `SampleSpecs`（';' 分隔，⛔ 不用逗号——示例值里可能有）/ `ShowSampleRow`（`HasSampleRow && 列表为空`）。
  - 表头 `ColumnGrid` 绑 `SampleSpecs="{Binding SampleSpecs}"`；首次布局测出 **表头宽**（各表头子元素 `DesiredSize`）与**示例宽**（临时 `TextBlock` 建来 `Measure(∞)`，字号 12 + 单元格内边距 16；测不到记 `double.MaxValue` 免得误锁）→ `HeaderMeasured` 事件 → VM `LockColumnsBySample(header, sample)`：**示例宽 ≤ 表头宽 → 锁成表头宽，且不小于 `MinLockedWidth = 34`**（表头文字很短的列也留得下拖拽把手）。
  - 锁定列的边界**不生成把手**、`ResizeColumns` 也拒绝（两侧任一被锁）；VM 的 `LockedColumnMask` → `ColumnGrid.LockedColumns`。
  - **示例行（水印）**：列表区上方单独一条 `Border + ColumnGrid`（⛔ **不进** `Items/Videos`，不参与统计 / 排序 / 筛选 / 勾选 / 导入导出），`IsVisible="{Binding ShowSampleRow}"`、`Opacity="0.5"`，逐列 `Text="{Binding SampleRowTexts[i]}"`。
  - 本轮只在**视频两页**落地；别的页要用：给列填 `Sample` + 加示例行 XAML + 绑 `SampleSpecs` + code-behind 接 `HeaderMeasured` 四步。
- ⚠️ `Views/ColumnGrid.cs` 因**模块间禁互引**而在 `Module.PcTools` / `Module.WebTools` / `Module.VideoTools` / `Module.Settings` / `Module.MediaAssets` **各存一份（共五份），改动必须同步五处**。

### 3.7 热加载实现

- 目录只有一个：`Path.Combine(AppContext.BaseDirectory,"Modules")`。
- 只认 `Module.<模块名>.dll`，其余跳过。
- ⛔ 跳过已加载同名程序集 → 本版**只做「新增」**（B10）。
- 每个待加载 DLL 用**独立 `AssemblyLoadContext`**（`isCollectible:true`）。
- `NewToys.Shared`/`NewToys.Core` 一律由**默认 ALC 共享**（ALC 的 `Load` 覆写里把契约程序集转交默认上下文）——否则 `IModule` 强转必失败。
- ⛔ 已 `BuildServiceProvider()` 的容器无法再追加服务 → 热加载模块 `RegisterServices` 可跳过；优先**页面型模块**。
- 加载后调 `IMenuService.RegisterModule/RegisterFunction` 注册菜单；**v3.0 刷新点**：通知 `ShellViewModel` 重建左树（`BuildTree()`）+ 刷新标签页图标键映射（若新模块的标签已打开则无需处理）。
- **失败隔离（硬要求）**：单个 DLL 加载/注册异常 → `try-catch` 兜住，只记日志+一行 StatusText；完成后 StatusText 报「本次新增 N/跳过 M/失败 K」。
- ⛔ 只读本地 `Modules\`；不联网不下载；加载前 `Path.GetFullPath` 校验路径在 `Modules\` 内。
- **入口（NT-052，2026-10-15）**：**功能清单页**（`settings-all`）「增加功能」操作区的「🔄 重载模块」按钮（标题栏按钮已移入）→ `AllFunctionsViewModel.ReloadModulesCommand` 调 `IModuleReloadService.HotReload()`（`Shared` 契约 + `Core/Services/ModuleReloadService` 包装 `ModuleLoader.HotReload(sp)`）→ 完成后 `BuildGroups()` 刷新本页 + 触发 `Reloaded` 事件；**`ShellViewModel` 订阅 `Reloaded`**（进程级单例，无需退订）重建左树 + 写底栏「新增 N/跳过 M/失败 K」。设置页 VM ⛔ 不直接调 `ModuleLoader` / 不订阅事件（瞬态 VM）。

### 3.8 UI 契约（v3.0 新增；v3.1 契约落地，v3.2 页面迁移暂缓）

> ⚠️ **本轮（v3.2）**：契约与 Shell 消费逻辑**已实装**；**页面迁移暂缓**。
> **下一版迁移方式**：让 `ListPageViewModelBase` 实现 `IDetailPanelProvider`（`HasDetailPanel` 默认 `false`，迁移页 `override` 为 `true`），并移除基类里的 detail flag 读写（改由 Shell 统一 `LoadFlag/SaveFlag`）——**改基类一处即可覆盖全部列表页**；每页再把右列 XAML 抽成 `Views/Detail/<Xxx>DetailView.axaml` 即可。

#### `IDetailPanelProvider`（`Shared/Services/IDetailPanelProvider.cs`）

```csharp
public interface IDetailPanelProvider
{
    bool HasDetailPanel { get; }
    bool IsDetailOpen   { get; }
    object? DetailContent { get; }
    event EventHandler? DetailChanged;
    void ToggleDetail();
    void OpenDetail();
}
```

**Shell 侧职责**：
- 每次 `ActiveTab` 变化时，检测 `ActiveTab.ViewModel is IDetailPanelProvider p`。
- 是 → 显示右详情列 + 订阅 `p.DetailChanged`；`IsDetailOpen` 初值从 `ColumnLayoutService.LoadFlag(<pageKey>, FlagDetailPanel)` 读。
- 否 → 隐藏右详情列（宽度 0）。
- 订阅时**先解绑旧 Tab 的订阅**（防内存泄漏）。

**列表页 VM 侧实现模板**：

```csharp
public partial class XxxViewModel : ObservableObject, IDetailPanelProvider
{
    public bool HasDetailPanel => true;
    [ObservableProperty] private bool _isDetailOpen;
    public object? DetailContent => SelectedItem;
    public event EventHandler? DetailChanged;
    public void ToggleDetail() { IsDetailOpen = !IsDetailOpen; DetailChanged?.Invoke(this, EventArgs.Empty); }
    public void OpenDetail()   { IsDetailOpen = true;  DetailChanged?.Invoke(this, EventArgs.Empty); }
}
```

#### `IHistoryProvider`（`Shared/Services/IHistoryProvider.cs`）

```csharp
public interface IHistoryProvider
{
    object? CaptureState();
    void RestoreState(object state);
    event EventHandler? StatePushed;
}
```

**Shell 侧职责**：
- 每次 `ActiveTab` 变化时，检测 `ActiveTab.ViewModel is IHistoryProvider h`。
- 是 → 订阅 `h.StatePushed`；前进/后退按钮由 `TabItemViewModel.CanGoBack/CanGoForward` 控制。
- 否 → 前进/后退按钮禁用。
- 订阅时**先解绑旧 Tab 的订阅**。

**页面 VM 侧**：`video-list` 是唯一有实际页内导航语义的页（目录切换）；其他页不需要实现。

#### `IDetailCardSource` + 详情卡声明式渲染（v3.29 / NT-054）

> 🔴 **规格唯一源** = 需求 §4.5.1（三区模型 / **8 种 Kind** / 3 种提交语义 / 刷新契约 / 16 张覆盖基线）。
> 本节只写**怎么实现**。迁移进度：**L0（外壳上提）+ L1（渲染器）已实装**；L2（逐页迁移）/ L3（逃生舱 + 旧 XAML 清理）待做。

```csharp
// Shared/Services/IDetailCardSource.cs —— 与 IDetailPanelProvider **并列**，页面 VM 两个都实现
public interface IDetailCardSource
{
    DetailCardDescriptor? Card { get; }   // 非 null → 走声明式；null → 退回老的自绘 DetailContent
}
```

**件与归属（⛔ 别放错层：Shared 不引 Avalonia、模块禁引 Shell）**

| 件 | 归属 | 说明 |
|---|---|---|
| `DetailCardDescriptor` / `DetailSection` / `DetailField` / `DetailAction` / `FieldKind` / `EditCommitMode` | `src/Shared/Details/DetailCardDescriptor.cs` | 纯 POCO，**零 Avalonia 依赖**；`DetailAction.Command` 用 BCL 的 `System.Windows.Input.ICommand` |
| `IDetailCardSource` | `src/Shared/Services/IDetailCardSource.cs` | 模块侧来源契约 |
| `DetailCardHost.axaml(.cs)` + `DetailCardRender.cs` | `src/Shell/Controls/` | 渲染器（必须引 Avalonia，模块又禁引 Shell ⇒ 只此一处） |
| 16 张卡的 `Card` 声明 | 各页 VM | C# 对象字面量，每页一个实例 |

**Shell 侧两轨择一（`ShellViewModel.RefreshDetail`）**

1. 页面是 `IDetailCardSource` 且 `Card != null` → `IsDetailCardActive = true`、`DetailCard = Card`、`DetailCardSource = 页面 VM`，
   ⛔ **此时不读 `p.DetailContent`**（读它就白建一个自绘视图）；
2. 否则 → 老路：`DetailPanelContent = p.DetailContent`。
   `ShellView` 右列 = `Panel{ ContentControl(老路) + DetailCardHost(声明式) }`，两者按 `IsDetailCardActive` 互斥显隐。

**渲染层口径（`DetailCardRender.cs`，改 Kind 必须同步这里 + XAML）**

- 描述符里的路径是**字符串**，XAML 里写不出动态 `{Binding <字符串>}` ⇒ 渲染层在 C# 侧把
  `ValuePath` / `VisibleWhen` / `EnabledWhen` / `OptionsPath` / `Chip*Path` / `*IsExpanded` 解析成真实值，
  行对象（`TextFieldRow` / `CheckFieldRow` / `SelectFieldRow` / `IndexSelectFieldRow` / `NumberFieldRow` / `ChipsFieldRow` / `HintFieldRow` / `DividerFieldRow` / `ListFieldRow`）
  **只暴露强类型属性**给模板 ⇒ 模板里**零字符串路径**。
- `DetailSectionRender.CreateRow` 是 **Kind → 行对象**的**唯一分派点**；加新 Kind = 加一个 `case` + 一个 `DataTemplate`。
- 刷新契约：渲染器**订阅源 VM 一次**（`PropertyChanged`）→ 任一属性变化整体重解析。
  ⛔ 动作级改动（落盘 / 加标签 / 删除）由**页面 VM 自己回刷**，描述符不参与。
- 提交语义落地：`Instant`/`Explicit` → 值的写回绑 `PropertyChanged`（`Explicit` 写的是 VM 的**编辑态草稿**，落盘由工具条「💾 保存修改」负责）；
  `OnBlur` → 模板挂 `LostFocus="OnTextLostFocus"` → `TextFieldRow.Commit()`。
- 🔴 **不可编译的坑（v3.29 实测）**：`Classes.xxx="{Binding ...}"` 里的 `xxx` 是**本仓样式类名**、
  不是控件属性 —— 会被 `check-axaml-props.ps1` 的名字存在性检查误报，**该脚本已加 `Classes.` 前缀豁免**，⛔ 别把这行豁免删了。
- 外壳（340px 容器由 Shell 右列给 / 覆盖式滚动条右侧留位 / 空态 / 示例态 / 分区与字段间距）**只实现一次**（`DetailCardHost.axaml`）。
- 新增样式类：`Button.section-toggle`（分区折叠标题）+ `Button.chip-toggle(.selected)`（`Kind=Chips`）→ 见 `Shell/Styles/Controls.axaml` ⑭。

**L2 打样补记（v3.30 / 首个迁移页 = 视频工具·批量队列 `VideoBatchView`）**

- 🔴 **路径优先用 `nameof(VM 属性)`**：能写 `nameof` 就别写字符串字面量（改名时编译器就报错）；
  只有**跨对象路径**（如 `"DetailTask.StatusDisplay"`、候选项的 `"Id"` / `"Name"` / `"Label"`）才用字符串。
  `check-detail-paths.ps1` 只查字符串那一批，`nameof` 那批靠编译器 —— 两者互补，⛔ 别把 `nameof` 退回字符串。
- 🔴 **`OptionsPath` 指向的集合必须是「稳定实例」**（静态表 / 常驻 `ObservableCollection`）。渲染器只在**集合实例换了**时
  才重建候选；若每次取属性都 `new List<>()`，ItemsSource 每刷新一次就是新的 ⇒ ComboBox 把 `SelectedItem` / `SelectedIndex`
  清掉并经 TwoWay **回写**，等于把页面 VM 上的值抹掉。
- 🔴 **空候选不回写**：候选列表为空时（ComboBox 初始的 `null` / `-1`）行对象**拒绝回写** —— 否则一进页面就把预设 / 编码器选择抹空。
- 🧩 `Select` 的**两个落值通道** = 两个行对象 + 两个模板：值（`SelectFieldRow`：`SelectedItem`，配 `OptionValuePath` 比对）
  与索引（`IndexSelectFieldRow`：`SelectedIndex` + `LiteralOptions`）；`CreateRow` 按 `IndexPath` 是否为空分派。
- 🧩 `Hint` 支持**动态行**（`ValuePath` + `Prefix`），替代 XAML 的 `StringFormat='前缀{0}'`；
  `List` 的单项五格（左标签 / 主文本 / 固定符号 / 右侧强调值 / 第二行说明）全由 `Item*Path` 取数，**符号也走描述符**（⛔ 模板里不写死 `→`）。
- 🧩 **迁移页三件事（缺一不可）**：
  1. VM 实现 `IDetailPanelProvider` + `IDetailCardSource`，并加一个**会通知 Shell** 的开合命令
     （⛔ 直接用基类的 `ToggleDetailCommand` 不行：它只翻 `IsDetailVisible`、**不发 `DetailChanged`**，右列不会跟着开合）；
     `ToggleDetail` / `OpenDetail` 建议**显式实现接口**（基类已有同名 private 方法，隐式实现会撞名）。
  2. `DetailContent` 返回 `null`，删掉页内右列 XAML（Shell 右列改由 `DetailCardHost` 承载）；根 `Grid` 收成单列。
  3. 页内原来自绘的**按钮**（如「📂 选输出文件」＝ code-behind + `StorageProvider`）→ 改成 VM 上的 `ICommand` **属性**，
     由 View 在 `OnDataContextChanged` 注入实现，声明式卡用 `DetailAction.Command` 调它。
- ⚠️ **布局降级（用户 2026-10-15 拍板接受）**：原「宽 / 高」这种**同行并排**字段改为**纵向堆叠**；输入框右侧的**内联按钮**
  统一收进 Actions 区。要 1:1 还原版面，需再给描述符加「同行并排 / 字段内联动作」两个能力（本轮⛔ 未做）。
- ✅ 打样页覆盖面：3 个分区（2 个可折叠 + 标题勾选框）/ 对象预设下拉 ×2 / 索引下拉 ×2 / 条件可见 ×3 /
  多行 code ×3 / 只读列表 ×1 / Actions ×1 —— **未使用 `Custom` 逃生舱**。

**绑定路径的静态兜底（🔴 必跑）**

- 描述符路径改名**不报错、不崩溃**，只是白板 ⇒ 新增 `skill/scripts/check-detail-paths.ps1`：
  扫含 `IDetailCardSource|DetailCardDescriptor` 的 `.cs`，把路径**首段**与全仓库属性 / 字段名比对，不命中即 WARN。
- 交付前三件套 + 本脚本一起跑：`check.ps1` → `check-axaml-props.ps1` → `check-techstack.ps1` → `check-detail-paths.ps1`。

### 3.9 视频工具「视频预览」页与两页详情卡（v3.12 重写，需求 §6.4）

> 页面 key 仍是 `video-cut`（落 `columnlayout.json` 页键，改名会丢用户偏好）；**View / VM 的类名与文件名 v3.12 起已改为 `VideoPreviewView` / `VideoPreviewViewModel`**。
>
> 🔴 **v3.31 / NT-054 起**：本节描述的是**迁移前**的形态。**「批量队列」页（`video-batch`）与「视频预览」页（`video-cut`）均已完成 L2 迁移** ——
> 两页页内右列都已删（页根 Grid 收成单列），详情卡改由 Shell 的 `DetailCardHost` 按 `VideoBatchViewModel.BuildCard()` /
> `VideoPreviewViewModel.BuildCard()` 的描述符渲染。下面关于页内两列布局的条文**已作废**（保留作迁移前形态备查），其余条文（时间条 / 卡片分区顺序 / 参数语义）仍有效。

**布局（迁移后：页内单列 + Shell 右列）**
- 页根主体 Grid：**单列**（预览列）；详情卡 = Shell 右详情列（340px，由 `DetailCardHost` 渲染，可开合）。
- 顶部工具条（**页面级动作都归这里**）：`📂 选择视频` / `➡ 发送到批处理` / `↺ 区间重置` / `❮❯ 详情`（末位绑 `ToggleDetailPanelCommand`，**会通知 Shell**）；**卡内动作**（如 `📂 选择输出文件…`）走描述符 `Actions` 区。
- VM 仍**不继承 `ListPageViewModelBase`**（不是列表页）→ `ObservableObject, IStatusProvider`；**已实现** `IDetailPanelProvider` + `IDetailCardSource`（`HasDetailPanel => true`、`DetailContent => null`）。

**三条时间条（预览位置 / 开始 / 结束）**
- ⛔ **滑条别直接绑模型属性** —— 一律走 VM 的**手动属性**（`PreviewTime` / `StartSeconds` / `EndSeconds`），setter 里改一处 → 时间码文本 + 大画面 + 命令预览**一起刷新**（避免"拖了滑条、时间码没跟上 → 保存被旧值覆盖"）。
- `Maximum` 绑 `TimelineMax`（时长未知时给**占位 1**）：⛔ 别在 `Maximum==Minimum==0` 时把 `Maximum` 直绑 `Duration` → `(Value-Min)/(Max-Min)=0/0=NaN` → **整页布局算不过去**。
- 大画面 `Image Source="{Binding PreviewImage}"`（`Bitmap`），抽帧走 `FfmpegService.ExtractFrameAsync`；拖滑条用**"最新值优先"节流**（`_extracting` + `_pendingPreview` 模式）。⛔ **不铺 `ItemsControl` 缩略图墙** —— 只保留"当前帧"一张大图。

**两页详情卡 = 同一套结构（需求 §6.4.1）**

分区**严格按 ffmpeg 命令的书写顺序**排，⛔ 不另起"任务类型"块：

| 顺序 | 分区 | 内容 | 勾选框语义 |
|---|---|---|---|
| 1 | `Expander`「✂ 剪辑」 | 开始 / 结束、剪切预设、重新编码 CRF、自定义参数 | `IsCut`：勾上 → 命令里带 `-ss/-to` |
| 2 | `Expander`「🔁 转码」 | 预设、编码器(CPU·GPU)、容器、分辨率宽高、码率(CRF/目标) | `IsTranscode`：勾上 → 命令里带编码参数 |
| 3 | 输出 | 输出文件（留空自动命名，绝不覆盖已有文件） | — |
| 4 | 命令 | 完整命令行（`SelectableTextBlock`）+ 可编辑参数框 + `↺ 重置为自动` + `📋 复制命令` | — |

实现要点：
- **勾选框放在 `Expander` 的 `Header` 里**（`Header` 用 `StackPanel Orientation="Horizontal"` 装 `CheckBox` + 文字），不用单独分区 —— 勾选与折叠合成一处，`取消勾选 → 自动折叠`（`OnIsCutChanged` / `OnIsTranscodeChanged` 里改 `IsXxxSectionExpanded`）。折叠只影响界面，**是否执行由勾选决定**。
- **两页的 VM 属性刻意对齐**（同名同义），这样同一段 XAML 结构可以照抄：`EditStart/EditEnd`、`EditExtraArgs`、`EditOutput`、`EditArgs`（可编辑命令）、`IsArgsEdited`、`CommandPreview`、`CopyCommandLineCommand`、`ResetCommandCommand`。
  - 批量页的转码参数属性名用 `EditPresetId/EditCrf/EditMaxHeight`（它本来就是列表页、属性名已定型），⛔ 不要为了对齐去改名 —— 改名要全仓搜旧名。
- **命令可编辑**：`CommandPreview`（只读合成，参数变即刷新）与 `EditArgs`（可写）分离 + `IsArgsEdited` 标记。手改后参数变化**不得覆盖** `EditArgs`；`↺ 重置为自动` = 清标记并重新赋自动值。⛔ 手改后的命令必须**原样执行**（存进 `VideoTaskBase.RawArgs`，批量运行时优先用它）。
  - 自动值怎么算：⛔ 别直接给 `EditArgs` 赋自动串再靠比较反推"是否手改"（赋值即相等 → 永远判不出手改）。做法是 `EditArgs` 只在**用户真改了**时偏离，`IsArgsEdited` 由「用户输入 vs 当前自动串」的比较得出，见 `VideoPreviewViewModel.OnEditArgsChanged`。
- 🔴 **覆写可视树回调要写 `protected override`**：`Visual.OnAttachedToVisualTree` / `OnDetachedFromVisualTree` 都是 **protected**，写成 `public override` → **CS0507「当重写 protected 继承成员时，无法更改访问修饰符」**。
- **批量页的命令区**：选中任务变了要重算 `CommandPreview` / `EditArgs`；`RawArgs` 非空时 `CommandPreview` 直接显示 `RawArgs` 拼出来的那条。
- 从「视频列表」送来 → 载入后**自动触发一次预览**（探测时长 + 抽当前帧）。

**"两页其实一样"到什么程度**：只有**区间来源**与**类型决定方式**不同 —— 预览页区间由时间条驱动、类型由两个勾选框决定（可同时勾 → 剪辑+转码一条命令）；批量页区间与类型都由**选中任务**决定（`CutTask` 只有区间段、`TranscodeTask` 只有编码段）。分区顺序、折叠、命令区形态必须一致。

---

## §4 配置与路径实现

- 一律 `Path.Combine`，⛔ 硬编码 `\` 或 `/`。
- **用户数据**由 `ConfigStoreService` 决定（不在仓库内）：应用根 `%LocalAppData%\NewToys`（环境变量 **`NEWTOYS_HOME`** 整根切换）。
  - **v3.5 起**：应用根之下再分一层「配置」—— `<应用根>\profiles\<配置名>\` → 其下才是 `config/data/logs`；当前配置名记在 **`<应用根>\active-profile.txt`**（在 `profiles\` 之外，否则切换会把自己也换掉）。
  - `ConfigStoreService` 在构造时：① 确保 `profiles\<active>\` 存在（首次还要跑旧结构迁移）；② 暴露 `ConfigDir` / `DataDirectory` / `LogsDir` 指向该配置；③ `ProfilesRoot` / `ActiveProfileName` 供「设置 → 配置」使用。
  - ⛔ 模块不自己拼路径，向 `IAppConfigService.DataDirectory`/`ConfigStoreService` 取值。
- **配置切换（v3.26 改语义）**：`IProfileService`（`Core`）负责列出 / 新增 / 重命名 / 删除 / 切换 / **设为默认**；**切换 = 重启进程并显式传 `--profile <名>`**（`Process.Start(Environment.ProcessPath + " --profile " + name)` + `Environment.Exit(0)`），⛔ **不写 `active-profile.txt`**（多实例下写它会偷改别的实例下次无参启动的默认）；**设为默认**才写指针。⛔ 不做运行时热切换。
- **启动参数（v3.26）**：`NewToys.exe [--profile <配置名>]`——`App` 启动最早处解析，交给 `ConfigStoreService` 的 profileOverride；`ActiveProfileName` 解析顺序 = **参数 > `active-profile.txt` > `default`**；参数非法 / 配置不存在 → 用默认 + 状态栏一行提示（⛔ 不崩、不静默）。
- **每配置单例（v3.26）**：命名互斥 `Local\NewToys_Profile_<配置名小写>`（`new Mutex(false, name)` + `WaitOne(0)`）；已占用 → 提示「该配置已在运行」后 `Environment.Exit(0)`，⛔ 不静默、⛔ 不抢焦点。⛔ 别用 `Global\`（多会话 / 终端服务场景误伤）；配置名大小写不敏感 → mutex 名统一小写。
- **新开实例（v3.26）**：「设置 → 配置」页每行「🚀 新开」= `Process.Start(Environment.ProcessPath, $"--profile {name}")`（`UseShellExecute=true`）；窗口标题 = `NewToys · <配置名>`（Shell 启动时按当前配置名设 `Title`，任务栏区分）。
- **窗口 bounds 记忆（v3.26）**：`config\window.json`（`X/Y/Width/Height/WindowState`），按配置各存各的；关闭时存、最小化态不存位置；还原时夹进当前屏幕工作区（防离屏 / 超小）；多实例各记各的、⛔ 不共享。
- 平台相关能力（winget/`ms-settings:`/ffmpeg）集中封装。
- 路径穿越防御：外部相对路径 `Path.GetFullPath` 后校验落在目标目录内。

### 4.1 `config/` 文件清单

| 文件 | 归属 | 内容 |
|---|---|---|
| `theme.json` | 软件设置 | 主题模式+强调色 id |
| `app.json` | 软件设置 | ffmpeg/播放器/DNS 开关/脚本运行环境（python、pwsh、PS5.1 路径 + 脚本目录）。无视频库目录 |
| `launch.json` | 软件设置 | 三个一键按钮+4 个通用开关 |
| `package.json` | 软件设置 | 软件包查询偏好。源本身不在这里 |
| `favorites.json` | 用户功能数据 | 首页收藏夹 |
| `pc-scripts.json` | 用户功能数据 | 「设置与脚本」页清单 |
| `pc-quick.json` | 用户功能数据 | 「快捷设置」页清单 |
| `columnlayout.json` | 用户功能数据 | 列显隐偏好 + **页面级开关**（详情栏开合 `FlagDetailPanel`；**壳级页 Key `"shell"`**：左树折叠态 `treeCollapsed` + **各一级分组展开态 `treeGroup:<groupKey>`，默认 false**） |

- **解释器路径**（python / pwsh / PS5.1）留空 = 用系统 PATH 里的；只有填了"看起来像路径"的值才校验存在性。`.ps1` 未配 pwsh 时自动退回系统自带的 5.1。
- `defaults\` 播种：`Shell.csproj` 用 `<None Update="defaults\**" CopyToOutputDirectory="PreserveNewest" />`（⛔ 用 `Update` 不用 `Include`）。
- 脚本目录默认 `<应用根>\scripts`（`IAppConfigService.DefaultScriptFolder` 推出），可在「设置 → 路径配置」改。

---

## §5 异常处理

- 导航/模块加载/主题应用等边界处 `try-catch`，失败不崩溃；至少写日志或兜底 UI，不得吞异常无反馈。
- 全局兜底：`AppDomain.UnhandledException`/启动异常捕获，崩溃给可读错误窗。

---

## §6 构建与版本

- 构建/发布/环境切换**由用户本地执行**（`scripts/build.ps1 -Configuration Debug|Release`）；AI 改完 `src/` 后列出需用户验证的点，不代跑构建。
- ⛔ 禁止声称「已编译通过」；没跑过写「未编译，请用户本地执行 `build.ps1` 验证」。
- 纯文档变更注明「不涉及编译」。
- ⛔ **脚本只用 PowerShell 7（`.ps1`），禁止 `.cmd` / `.bat`**：cmd 对中文支持差、出错难排查；如确有不可替代理由必须用，须在文件头注释写明**为何不能用 `.ps1` 替代**，否则视为违规。脚本采用「fail-loud」（成功 `exit 0` 自关窗口、失败 `trap` 暂停），勿以 `.cmd` 重写。

**版本号**（`src/Directory.Build.props` 编译时自动生成，⛔ 禁止手写）：
- `InformationalVersion` = `1.<yyyyMMdd>.<HH>.<mm取整到十位>`（显示用）
- `FileVersion` = `1.<yyyy>.<MMdd>.<HHmm>`；`AssemblyVersion` 固定 `1.0.0.0`
- 「设置·关于」读 `InformationalVersion`。

**产物路径**：

| 产物 | 路径 |
|---|---|
| Debug 输出 | `artifacts/bin/<项目名>/Debug/net10.0/` |
| Debug 主程序 | `artifacts/bin/Shell/Debug/net10.0/NewToys.exe` |
| Release 输出根 | `artifacts/release/` |
| Release 模块 DLL | `artifacts/release/Modules/Module.*.dll`（直出，零拷贝） |
| 发布产物 | `production/NewToys_<运行时>/` |
| 测试环境根 | `artifacts/env/test/` |
| 用户数据 | ⚠️ **不在仓库内**：`%LocalAppData%\NewToys\{config,data,logs}` |

AI 只读 `src/`，⛔ 不读 `artifacts/ bin/ obj/ .vs/`。

---

## §7 技术债台账（B 类 · 原「折衷约束台账」）

> 🔴 **术语**：本表 = **技术债（Tech Debt）** —— 当前做法为「需求 vs 工程现实」妥协，非永久铁律。**变更条件满足 + 用户同意 + 本表登记**后可调；未登记不得擅改。
> 窗口装饰相关的 Win32 补丁（补样式位 / `WM_NCCALCSIZE` / `SetWindowRgn`）**若保留也必须登记在此**，写明：官方为什么不行 / 复现步骤 / 移除条件（需求 §4.1.1 ②）。

| 项 | 理想 | 当前折衷 | 变更条件 | 影响面 |
|---|---|---|---|---|
| **B1 编译绑定** | 开启编译绑定，性能更好 | 全工程关闭，不写 `x:DataType` | **保持关闭**（用户拍板：运行期暴露绑定错误，统一开启待定） | 全部 View |
| **B2 VM 基类** | 统一继承 `Shared/ViewModelBase` | 模块内多直接继承 `ObservableObject`，仅 Shell/Home 用 `ViewModelBase` | 某模块改造时逐模块统一 | 各模块 VM |
| **B3 全局样式** | 显式 `x:Key` 样式键 | 用 `Classes` 隐式样式 | 收益足够大时整体改造 | `App.axaml` |
| **B4 导航服务** | 导航统一走 `INavigationService` | 主流程（侧栏 / 菜单点选）由 `ShellViewModel` 自解析；`NavigationService` 仅被**模块内跳转**（功能清单页、模块首页）经 `Navigated` 事件复用，**未成为统一入口** | 删闲置服务或改用它（属框架层需同意） | Shell/Core |
| **B5 平台后端** | 完全跨平台 | 后端仍调 winget/ffmpeg 命令行，类型已中立为 `Package*` | 迁 Debian 时替换 | Core 服务 |
| **B6 仓库根 slnx** | 唯一入口 `src/NewToys.slnx` | 仓库根残留旧 `NewToys.slnx` 副本 | 直接删除即可 | 仓库 |
| **B7 Modules 冗余 DLL** | `Modules\` 仅含模块 DLL | 会多出 `NewToys.Shared.dll`/`NewToys.Core.dll` | 无害暂留 | Release 产物 |
| **B8 闲置转换器** | 不留无用文件 | `Shell/Converters/BoolInversionConverter` 未被引用 | **已删除**（2026-10-14，无任何 XAML/CS 引用） | Shell |
| **B9 骨架页面** | 全部页面落地真实逻辑 | 仅剩 `web-http` 一个骨架 | 按节奏实装 | Module.WebTools |
| **B10 运行期热加载** | 任意模块随时加载/卸载/替换，服务随模块可扩 | 仅支持「新增未加载过的模块」；同名不卸载不替换；容器已 build，模块不能注册新服务 | 引入可卸载 ALC+模块级容器后升级 | ModuleLoader+Shell |
| **B11 反射使用** | 最小魔法、少反射 | 模块装配/热加载/视图解析三处用反射枚举 IModule 与 View/VM | 机制必需暂不改；⛔ 仅限这三处 | ModuleLoader/ShellViewModel |
| **B12 前进/后退**（v3.0 新增） | 所有页面都有丰富页内导航 | 现状仅 `video-list` 有目录语义；多数标签页前进/后退**灰显** | 各页逐步实现 `IHistoryProvider` 后自然改善 | 各模块页面 |
| **B13 详情面板上提**（v3.2 新增） | 详情由 Shell 右面板统一承载 | 契约与 Shell 容器已实装，但**页面迁移暂缓**——12 个页面仍用页内自绘右列 | 下一版迁移（改 `ListPageViewModelBase` 一处 + 每页抽 DetailView） | Shared + 各模块页面 |
| **B14 媒体资产用户数据落库**（v3.19 新增） | 用户手动数据落 `config\`、进备份（需求 §3.5） | 标签 / 分类 / 集合 / 关联暂存 `data\newtoys.db`（需求 §3.5 称其"可随时重建"）——与 §3.5 定位冲突，以「导出用户数据 JSON」缓解（需求 §6.6 R1） | 用户拍板独立库 / 纳入备份策略时 | Module.MediaAssets + 数据管理 |
| **B15 数据驱动图标**（v3.22 新增） | 所有图标位都是矢量 `Geometry`（`App.axaml` 的 `Icon*` 键） | 二级功能菜单 `icon` / 首页一键按钮 / 模块页内数据行仍是 **emoji 字符串**（用户 2026-10-15「其他没改的以后再改」） | 收敛工作项 **NT-047**（键名不存在就地没图标，⛔ 不做 emoji 回退） | 各模块注册 + 首页 / 功能清单 / 资产集合页 |
| **B16 `SetWindowRgn` 方角硬手段**（v3.23 新增） | 方角由官方 `DWMWA_WINDOW_CORNER_PREFERENCE` 稳定生效 | 保留 `ShellView.ApplySquareRegion()`（矩形窗口区域）+ `SetCornerPreferenceNoRound()` 双保险 | 🔴 **NT-042 已裁决保留**：基准测试（完全不套 region）实测阴影**同样没有** ⇒ 原「代价 = 阴影消失」证伪，代价不成立、方角收益仍在。移除条件 = 官方 corner preference 在本窗口上能稳定生效（普通窗 + 分屏两态都不复发圆角） | `Shell/Views/ShellView.axaml.cs` |
| **B17 窗口装饰 Win32 补丁残留**（2026-10-15 新增） | 装饰全走 Avalonia 官方 CSD，Win32 侧零补丁 | 残留 = **方角两处**（官方开关 + `DwmSetWindowAttribute(33,1)` + `SetWindowRgn`，见 B16）+ **`WM_NCCALCSIZE` 零内缩一处**（Windows 独有例外，消 ≈8px 非客户区缝；靠"钩子只在 Windows 装得上"天然门控）；⛔ 样式位补位（NT-041）/ 贴靠几何纠偏（NT-048）**已删除** | 移除条件 = ① B16 满足一并清零 ② 零内缩在「官方 CSD 不再扣非客户区」（或 Avalonia 提供等价开关）时移除；另：迁非 Windows 平台前须补「自绘外框门控（`WindowDecorationMargin`）」与「窗口记忆回退 Avalonia API」（规范 §2.4 ⑤） | `Shell/Views/ShellView.axaml(.cs)` + `App.axaml` |

---

## 附录 A. 高频编译错误速查

| 错误 | 原因 | 解法 |
|---|---|---|
| NU1010 | CPM 下 csproj 写了 Version 或 props 未声明 | props 补 `<PackageVersion>`；csproj 不写版本 |
| CS0260 | 带 `[ObservableProperty]` 的类未 `partial` | 类声明加 `partial` |
| AVLN2000 | 用了 WPF 专有写法/`IsCheckable` | 改 Avalonia 语法（`Selector` 代 `TargetType`；列显隐用 `ToggleType=CheckBox`） |
| AVLN3000 | `ColumnDefinitions` 绑 `{Binding}` | 改用列宽规格串+自定义 ColumnGrid |
| CS1061 ItemsControl 未含 Children | `.axaml` 是 ItemsControl，code-behind 按 Panel 用 `.Children` | 两文件同步 |
| CS0103 当前上下文不存在 Xxx | XAML 元素没 `x:Name`，code-behind 引用 | 补 `x:Name` |
| No precompiled XAML found | 用 `AvaloniaXamlLoader.Load` 加载独立资源字典 | 主题改纯代码构建 ResourceDictionary 注入 |
| 菜单为空（一个模块都没） | 程序集延迟加载只扫 AppDomain 扫不到 | Debug 用显式类型清单；Release 先 `LoadFrom` |
| 侧栏菜单重复 N 次 | 模块被重复注册 | ModuleLoader 用同一实例/跳过已加载同名程序集 |
| 切主题后树/标签栏仍旧配色 | code-behind new 出的行是本地值 | 订阅 `ThemeChanged` 后重建；优先 Classes+样式 |
| 点刷新没反应 | 只扫目录没重建左树；或容器已 build 无法注册服务 | 加载后重建左树（`BuildTree()`）；页面型模块可不注册服务 |
| CS0104「Xxx」不明确引用 | 应用侧类型与所用库同名 | 给应用侧类型换名（`AppMenuItem`）；⛔ 别用别名掩盖 |
| NETSDK1022 重复项 | `defaults\**` 用了 `None Include` | 改 `None Update` |
| 软件包列表页空白（0 行） | winget 表头**本地化**（中文 Windows 打 `名称/ID/版本/可用`） | 列标签按「英文+中文别名」映射到规范名再切片 |
| 已安装列表少一大截 | `IdLike` 正则不认 `\` | 正则加 `\\`（`^[A-Za-z0-9][A-Za-z0-9._+\\-]*$`） |
| `winget show` 的「发布者」里带了 URL | `发布服务器 URL:` 被当成上一条多行值的续行 | 把不认识的 `标签:` 显式**终结**上一个字段并丢弃 |
| **`PathIcon` 不显示**（矢量化后） | `Data` 绑的 `Geometry` 键不存在或拼错 | 全仓搜键名，确认 `App.axaml` 已定义 `Icon*` |
| **`Application` 无 `TryFindResource` / 没有 1 个参数的重载**（v3.3） | Avalonia 12 里它是 `ResourceNodeExtensions` 对 `IResourceHost` 的**扩展方法**（宿在 `Avalonia.Base`，命名空间却是 `Avalonia.Controls`），且是 **Try 模式（`out` 参数）**，不直接返回 | ① 该文件加 `using Avalonia.Controls;`；② 写成 `if (app.TryFindResource(key, out var v) && v is Geometry g) …` |
| **树节点点击无反应**（v3.0） | `TreeView` 的 `SelectedItem` 未绑定或二级节点未用 `HierarchicalDataTemplate` | 检查 `ItemsSource`/`SelectedItem` 双向绑定，叶子节点绑 `OpenTabCommand` |
| **标签页重复打开**（v3.0） | `OpenTab` 未做单例判定 | `Tabs` 中按 `Key` 查重，存在则激活 |
| **关闭首页标签失败**（v3.0） | 未设 `CanClose=false` | `TabItemViewModel` 构造时首页 `CanClose=false`，`CloseTab` 检查 |
| **跨页投递的数据要刷新页面才出现**（v3.12） | 只在 `OnDataContextChanged` 里取件；标签页已存在时切回来 `DataContext` 不变 → 该回调不再触发 | 取件放 `OnAttachedToVisualTree`（每次进页面都会跑）；⚠️ 必须写 `protected override`（基类是 protected，写 public 报 CS0507） |
| **表头与行错位**（v3.12） | 改了 `Columns` 元数据但表头 / 行模板的 `Grid.Column` 没逐字同步 | 改列元数据后核对表头与行模板的列序列（需求 §4.8 硬规则 2） |
| **菜单项静默丢失**（v3.19 记，v3.20 换解法） | 模块 A 在 `Initialize` 里 `RegisterFunction` 到模块 B 的分组：B 尚未注册 → 旧实现抛 `InvalidOperationException`，被 `ModuleLoader.InitializeAll` 的逐模块 try-catch **吞掉** → Release（`Modules\` 目录扫描，`Directory.EnumerateFiles` 顺序不保证）下该项**静默消失**，Debug 显式清单反而可能正常 | v3.20 起 `MenuService` **延迟补登**（§3.2）：分组未注册时暂存、`RegisterModule` 建立分组时补登，⛔ 不抛异常；分组始终未建立（如 `Module.Settings` 缺失）→ 启动健全性检查报一行（§3.3），暂存项不显示 |
| **窗口不能拖边框缩放**（v3.26 记；🔴 **v3.22 修正结论**） | 12 时代 `ExtendClientAreaChromeHints` 已移除（11 的 API，运行时程序集探针实证）；`ElementRole` 在 12 上**确实生效**（前提是 `ExtendClientAreaToDecorationsHint="True"`，官方明确、issue #21202 已确认） | ✅ **正确解法 = 官方 CSD**：`WindowDecorations` + `ElementRole=ResizeN…SW` 由系统处理缩放，见 **§2.4**。⛔ v3.26 那条「托管装饰在 Win32 不可用 → 保持 `WindowDecorations="None"` 自绘」的结论**已过时**——它只实测到 `ElementRole` 早期版本的 bug，且漏了 `WindowDecorationsTheme` 这条正门。自绘抓取区（四边 5px + 四角 10px + `BeginResizeDrag`）降级为**兜底**，NT-041 验证后删除 |
| **`VisualRoot as Window` 恒为 null**（v3.26 记，Avalonia 12.x） | 视觉根是 `TopLevelHost`，`VisualRoot as Window` 永远转不成功（12.x 实测项目结论） | 取窗走逻辑树 `FindLogicalAncestorOfType<Window>()`（`includeSelf: true`） |
| **同配置多开互相踩数据**（v3.26 防） | 两实例同配置 → 共享 `config/*.json`（后写覆盖）+ 同一 `newtoys.db` 并发写 | **每配置单例**：命名互斥 `Local\NewToys_Profile_<小写配置名>`，`WaitOne(0)` 占不住就提示退出；SQLite 已有 `busy_timeout=3000` 兜底（`SqliteDatabase.cs`） |