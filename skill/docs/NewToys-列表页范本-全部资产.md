# NewToys 列表页范本 · 全部资产（`assets-all`）

> v1.2 · 2026-11-05 · **新增 §八 同族页矩阵**：把 21 页的四段齐全度变成机器清单，由 `lint.py --only family` 与 `accept.ps1` 第 10 项对账。v1.1：表格实现已统一（全部资产改用 `TableViewHost`，不再自绘表头 / 列宽 / 右键菜单；§2.2 §三 §六 同步）。把散在 `ui.md` / `framework.md` / `modules/media-assets.md` / 软件说明书 的规范，按一个真实页面实例化，供后续扩展复用。
> 🔴 本文**不重复规范正文**：每条只给「现状事实 + 唯一源指针 + 本页取舍 + 验收点」。冲突时以 `src/` 源码为准，并回写对应唯一源。
> 读法：新建 / 扩展标准列表页、推广分页与筛选条、加详情卡字段之前先读本文；实现取值仍回 `ui.md`。

## 一、定位与边界

- 范本 = `assets-all`（模块 `media-assets`，`Views/AssetAllView.axaml` + `ViewModels/AssetAllViewModel.*.cs`）。
- 选它的理由：唯一同时具备「顶部筛选条 + 原生表格 + SQL 分页底栏 + 声明式详情卡 + 多选批量」的页。`ui.md` §6.1 已指定它为骨架样板，§6.9 指明仅它与软件包走底栏分页。
- ⛔ 不套本范本：`AssetTasksView`（三块堆叠 + ScrollViewer）、`assets-browse`（资源管理器形态）、视频 / 媒体工具强定制页（`ui.md` §6.1）。
- 本页**不带右栏详情卡**：详情列由 Shell 承载（VM 实现 `IDetailPanelProvider`）；页内再自带一栏会出现两张卡。

**唯一源地图**（⛔ 本文不另立数值 / 契约）

| 要找什么 | 唯一源 |
|---|---|
| 尺寸密度 / 字号角色 / 主题键 / 图标 / 边框圆角 | `ui.md` §2 §2.3 §3 §5 §12.4 |
| 列表页骨架 / 列宽 / 底栏 / 分页数据层 / 多选 | `ui.md` §6（6.1~6.11） |
| 详情卡描述符 / 渲染 / 11 种 Kind | `ui.md` §7 + `framework.md` §6.3 + 需求 §4.5.1 |
| 筛选条控件契约 | `ui.md` §6.3d |
| 工具条 / 交互反馈 / 写入时机 / 状态栏 | `ui.md` §8 §10 |
| 页面操作契约（命令 / 状态 / 约束） | `modules/media-assets.md` §2.5 |
| 用户语言（怎么用） | `NewToys-软件说明书.md` §四 §五 |
| 程序集边界 / 跨模块传参 / 配置路径 | `framework.md` §2 §6 §7 |

## 二、页面解剖（四段）

| 段 | 控件 / 代码锚点 | 数据契约 | 规范唯一源 |
|---|---|---|---|
| ① 顶部筛选条 | `ListPageSkeleton.TopArea` → `UI/Controls/FilterBar`；页面根节点 `ui:FilterDropDownAlign.Attach="True"` | 4 个 `FilterOption` 下拉 + `ResetFieldsOnClear` + `ClearFiltersCommand` | `ui.md` §6.3d §6.11 |
| ② 表头 + 行区 | `UI/Controls/TableViewHost`（`AssetAllView.axaml` 的 `<v:TableViewHost>`）；表头由其自带 ⇒ 骨架 `HeaderArea` 留空 | `AssetColumns.All`（12 列）经 `ColumnLayout` 按索引对齐；`IsXxxVisible` | `ui.md` §6.3c §6.10 |
| ③ 底部分页条 | `ListPageSkeleton` 下发分页参数 → `UI/Controls/ListFooterBar` | `ShowFooter` / `TotalCount` / `PageText` / `PageCount` / `PageSize` / 四个翻页命令 | `ui.md` §6.8 §6.9 |
| ④ 详情卡 | `AssetAllViewModel.Detail.cs` `BuildCard()` → Shell `DetailCardHost` | `IDetailCardSource` / `IDetailPanelProvider` / `ISelectionStatusProvider` | `ui.md` §7、`framework.md` §6.3 |

### 2.1 顶部筛选条

- 形态：**一行 4 个常用下拉**（类型 / 存储源 / 状态 / 标签），`Grid` 四等分、`Classes="filter-combo"`、`MaxWidth=120`、`MinWidth=0`；⛔ 无滚动条、⛔ 不换行。
- 每个下拉 `ItemsSource` = VM 的 `ObservableCollection<FilterOption>`，**第 0 项恒为「全部」**（`Value=""`）。`SelectedItem` 走 `Mode=TwoWay`。
- 一键复位：`FilterBar ResetFieldsOnClear="True" ClearCommand="{Binding ClearFiltersCommand}"`。控件把内容槽里 `filter-combo` 用 `SetCurrentValue` 复位到 `ItemsSource[0]`（⛔ 直接赋值会顶掉 TwoWay 绑定），再执行 `ClearCommand`。搜索框与库内数据不动。
- 本页不绑 `QuickItemsSource` ⇒ 快捷筛选下拉隐藏；chips 形态已全项目撤除。
- 联动：改「类型」⇒ `OnFilterTypeChanged` 收窄存储源 / 标签选项（`ListStorageOptionsForType` / `ListTagOptionsForType`）+ 切列集合 + 换状态栏前缀；重建期间 `_rebuildingOptions` 抑制重取。
- 取数：四个下拉、搜索框、基类 `Filters` 在 `BuildQuery()` 里是**「且」叠加**，同一个 `AssetQuery` 同时供 `COUNT(*)` 与取页（⛔ 两份 WHERE 会漂移）。草稿态（扫描预览）走 `ApplyViewMemory()` 内存切片。
- 验收点：改任一条件即时重取并回第一页；`🧹 清空` 后 4 个下拉都显示「全部」；类型切到「音乐」时另两个下拉不含只有视频的盘 / 标签。

### 2.2 表头 + 行区

- 表格本体 = `TableViewHost`（`TableView` 的项目派生类，即 `ui.md` §6.3c 的默认写法）：列显隐、表头文案与排序箭头、列宽拖拽与自适应、表头⇄行右键菜单全部由控件按 `ColumnLayout` 提供；本页只声明 `TableViewColumn` 与行功能菜单；原表头右键分部类已删除。
- 密度配色取全局 `Styles/TableViewHost.axaml`（行 28 / 字 12 / 表头 `Bold` 次要色 / 选中 `CardSelectedBrush` / 悬停 `ListRowHoverBrush`）；本页局部样式只保留筛选条两条。
- 表头右键 = 列宽自适应 / 恢复本页默认列宽 / 列显隐（`TableViewHost.BuildHeaderItems` 生成，命令走基类 `ToggleColumnCommand`）；行右键 = 页内功能项。弹出树不继承宿主 DataContext ⇒ 控件与 code-behind 各同步一次。
- 列显隐兜底：`TableViewColumn` 不在可视树，OneWay 绑定通知不可靠 ⇒ `AssetAllView.axaml.cs` 的 `SyncColumnVisibility()` 按 VM 通知直接写回；⛔ 别删这段。
- 多选 = 列表原生 `SelectionMode="Multiple"`：`Ctrl` 增选 / `Shift` 连选 / `Ctrl+A` 全选当前页；无修饰键单击 = 单选替换（code-behind 隧道接管）。右键点在未选中行 ⇒ 单选替换，点在已选中行 ⇒ 保留整批。
- 🔴 列键 ≠ 属性名的列（Tags / Categories / Collections / Modified / Storage）必须在 `FieldText` 显式映射，否则基类反射取到 null ⇒ 该条筛选**静默失效**。
- 验收点：表头排序下推到 SQL（⛔ 只排当页内存）；拖列边界改宽；隐藏列走 `IsVisible`（⛔ 宽度 0 是旧口径）；多选批量动作只吃当前页。

### 2.3 底部分页条

- `ListPageSkeleton` 下发：`ShowFooter="{Binding HasFooterContent}"`、`TotalCount` / `SelectedCount` / `PageText` / `ShowPaging` / `PageCount` / `CurrentPage` / `JumpToPageCommand` / `First`·`Prev`·`Next`·`LastCommand` / `PageSizes` / `PageSize`。
- `ListFooterBar` = 三段 `*,Auto,Auto`：左计数 `N 条 · N 页` ／ 分页链（矢量 Path 箭头） ／ 「每页[ ]」+ `Go[ ]`；分页链靠右紧挨「每页」。底栏 28px。
- 🔴 **「已选 M」不进底栏**，选中数经 `ISelectionStatusProvider` 进 Shell 状态栏右端。
- 数据层：`SQL LIMIT/OFFSET` 取当页 + 独立 `COUNT(*)` 出总数 + 排序下推 SQL；`ClampPageIndex()` 收敛越界页码。页大小落 `shell.json`（⛔ 不落 `columnlayout.json`）。
- 🔴 `PageSizes` 必须是**恒定集合实例**（就地增删改）；运行时整体换 `ItemsSource` 会在下拉点选那一瞬抛 `ArgumentOutOfRangeException` 直接退出进程。落盘值由 `SnapPageSizeToChoices` 收成本页预设档。
- 28px 底栏内的 `TextBox` / `ComboBox` 必须显式 `MinWidth=0 MinHeight=0`。
- 验收点：底栏只在超过页底时出现；改筛选 / 搜索后回第一页；总数与当前页行数同源；页大小切换不闪退。

### 2.4 详情卡

- VM 实现 `IDetailCardSource.Card`（`BuildCard()` 返回 `DetailCardDescriptor`，**C# 对象字面量**，⛔ 运行时 JSON / XAML 加载），Shell `DetailCardHost` 统一渲染；外壳 300px / 覆盖式滚动条 / 空态只实现一次。
- 本页结构：基本信息节（文件名可编辑 + 只读扩展名后缀 / 类型·大小·修改时间·存储源用 `Inline` 行内「标签：值」/ 状态下拉 / 文件路径 / **只读上下文三行** = 库内名称·资产 Id·最近更新，全取自现有 `AssetRow`；最近更新为空时整行隐藏并给一句 Hint，⛔ 留空白行 / 视频专属三列 `VisibleWhen=IsVideoType` / 丢失警告 Hint）→ 三组 `Chips`（标签 / 分类 / 集合，带色块 + 空候选 Hint）→ `IconBar`（收藏 / 评分即时写库 · 保存 · 读媒体信息…）。
- 路径一律 `nameof(VM 属性)`；只有跨对象路径（`"DetailRow.TypeText"`）才用字符串。`OptionsPath` 指向的集合必须是稳定实例；空候选不回写。
- 提交语义：收藏 / 评分**即时写库**；描述 / 状态 / 三组 chips 进编辑态草稿，点「💾 保存」统一落库（⛔ 不动磁盘）；文件名 `OnBlur` 进编辑态，点「应用到文件」才真改磁盘。
- 保存后从库读回重填卡并按 `Id` 认领选中行，⛔ 不让详情卡跳走。
- 验收点：切选中行详情卡整体刷新且不闪烁（重解析 ≠ 重建）；视频字段只在类型 = 视频时出现；无标签 / 分类 / 集合时显示空候选提示；⛔ 不给 `DetailCardHost` 设 `DataContext`。

## 三、样式取值（本页实测，数值唯一源仍是 `ui.md`）

| 位置 | 本页取值 | 唯一源 |
|---|---|---|
| 筛选条 | `Border.filter-bar` + `Padding=8,2`（本页局部覆盖）；`ComboBox.filter-combo` `Height=22` `FontSize=12` `MinHeight=0` | `ui.md` §2.2 §6.3d |
| 筛选下拉弹出 | `PlacementMode.BottomEdgeAlignedLeft`（`FilterDropDownAlign` 附加行为，写 Popup 实例本地值） | `ui.md` §6.11 |
| 表头 / 行 | 行高 28、字号 12、表头 `Bold` + 次要色；选中 `CardSelectedBrush`、悬停 `ListRowHoverBrush`；行间⛔ 不画分隔线 | `ui.md` §6.3c §3 §12.4 |
| 底栏 | 28px；输入框 `MinWidth=0 MinHeight=0`；页大小下拉 `Width=78`；边框 `InputBorderBrush` | `ui.md` §6.8 |
| 详情卡 | 300px；节 16 `SemiBold` 主色 ＞ 标签 14 `SemiBold` 次要色 ＞ 值 12 主色；内容缩进 15 | `ui.md` §7.1 §2.3 |
| 颜色 | 一律 `DynamicResource` 主题键；⛔ 写死色值 | `ui.md` §1 §3 |

## 四、操作逻辑（契约索引，⛔ 不复述正文）

| 动作 | 触发 | 结果 | 契约唯一源 |
|---|---|---|---|
| 筛选 / 搜索 / 排序 | 4 下拉 / 搜索框 / 点表头 | 即时重取并回第一页，三者「且」叠加 | `modules/media-assets.md` §2.5；说明书 §四 4.5 |
| 一键复位 | `🧹 清空` | 4 下拉回「全部」+ 清基类 `Filters`；搜索不动 | `ui.md` §6.3d；说明书 §四 4.5 |
| 多选 / 批量 | `Ctrl`·`Shift`·`Ctrl+A`·右键 | 批量动作只作用于当前页选中行 | `ui.md` §6.10；`modules/media-assets.md` §2.5 |
| 分页 | 翻页箭头 / `Go` / 每页下拉 | SQL 取当页 + 独立 COUNT | `ui.md` §6.9；说明书 §五 |
| 详情卡编辑 | 卡内字段 / chips / IconBar | 按 §2.4 三种提交语义 | `ui.md` §7.1 §10.3；`modules/media-assets.md` §2.5 |
| 右键分工 | 表头 / 行 | 表头只管列、行只管功能 | `ui.md` §10.1 |
| 状态反馈 | 状态栏 / 底栏 | 总量·页数进底栏，选中数进 Shell 状态栏 | `ui.md` §6.8 §10.4 |

> 🔴 **改用户可见交互必须双写**：`modules/media-assets.md` §2.5（契约）+ 说明书 §四/§五（用户语言）；只改一处 = 交付未完成。规则唯一源 = `index.md` §四。

## 五、坑点（本页实测，细节回唯一源）

| 症状 | 原因 / 约束 | 唯一源 |
|---|---|---|
| 整块 XAML 静默不加载 | `UserControl` 漏写 `public Xxx(){ InitializeComponent(); }` | `ui.md` §1 |
| 筛选条 / 表头 / 列表整页发白 | 内容槽用祖先绑定 ⇒ `ListPageSkeleton` 静默空内容；一律 code-behind `Source=this` | `ui.md` §6.1 §6.3d |
| 复位后下拉不再跟 VM 变 | 直接给 `SelectedItem` 赋值顶掉 TwoWay 绑定；用 `SetCurrentValue` | `ui.md` §6.3d |
| 选页大小闪退（进程直接退出） | 运行时整体换 `PageSizes` 实例 | `ui.md` §6.8 |
| 某条筛选静默失效 | 列键 ≠ 属性名，`FieldText` 未显式映射 | `modules/media-assets.md` §3 |
| 详情卡下拉把 VM 值抹空 | `OptionsPath` 每次返回新集合 / 空候选回写 | `ui.md` §7.2 |
| 总数与行数对不上 | COUNT 与取页写了两份 WHERE | `ui.md` §6.9；`AssetAllViewModel.cs` `BuildQuery()` |
| 标签筛选恒 0 行 | 「全部标签」占位项按 `Label` 传进 SQL；判空必须按 `Value` | `modules/media-assets.md` §5 |
| 弹出菜单命令不触发 | `ContextMenu` 在独立弹出树，不继承宿主 DataContext | `ui.md` §6.2 §10.1 |

## 六、扩展落点（后续任务照这张表走）

| 要扩什么 | 改哪些文件 | 契约 / 注意 | 必须双写 |
|---|---|---|---|
| 加一个筛选维度 | `AssetAllView.axaml`（Grid 加列 + `filter-combo` ComboBox）、`AssetAllViewModel`（Options / Filter 属性 / `OnXxxChanged` / `BuildQuery` 参数）、`AssetService.Query`（SQL WHERE） | 「全部」恒为第 0 项；草稿态 `ApplyViewMemory` 同步加一段；⛔ 两份 WHERE | §2.5 + 说明书 §四 |
| 加一列 | `AssetColumns.All`、`AssetAllView.axaml` `TableViewColumn`、必要时 `FieldText` 映射、`SyncColumnVisibility` 的 states 数组 | 列键 ≠ 属性名必须映射；列顺序必须与 `ColumnLayout` 一致；隐藏列走 `IsVisible` | §2.5 + 说明书 §四 |
| 加详情卡字段 | `AssetAllViewModel.Detail.cs` `BuildCard()` + VM 属性；新 Kind 才动 `DetailCardRender`（框架层） | 路径用 `nameof`；`OptionsPath` 稳定实例 | §2.5 + 说明书 §四 |
| 推广筛选条到其它页 | 页面 XAML 挂 `FilterBar` + `ResetFieldsOnClear`；下拉加 `filter-combo` 类 | 无维度页⛔ 不挂空条；快捷项走 `QuickItemsSource` 下拉 | 各模块 §2.5 + 说明书 |
| 推广分页到其它页 | VM 继承分页契约 + `ListPageSkeleton` 下发参数 + 服务端 SQL | 落入 `Shared` / `Shell` / `NewToys.UI` ⇒ **框架层，先展示方案等确认** | §2.5 + 说明书 |
| 改控件本身（`FilterBar` / `TableViewHost` / `ListFooterBar` / `DetailCardHost`） | `src/UI/Controls/` | 框架层 `NewToys.UI`，⛔ 先展示「改哪个文件 / 加什么契约 / 谁受影响 / 为何绕不开」再动手；改 `TableViewHost` 影响全部列表页 | `ui.md` §6.3c 等对应节 |

**批次纪律**：一个 NT = 一次用户本地 `pwsh -File .\scripts\build.ps1`；样式 / 文案 / 绑定微调攒批，⛔ 中途要 build；AI 只跑 `pwsh skill/scripts/accept.ps1`（纯静态）。版本走 git commit，⛔ 不写 md 日志。

## 七、交付自检

- [ ] 改动只落 `src/` 与授权的 `skill/`；框架层改动已先展示方案并经确认。
- [ ] 改 `.axaml` 同步改 `.axaml.cs`；`UserControl` 显式构造；跨程序集 xmlns 用 `using:`；颜色全 `DynamicResource`。
- [ ] 改用户可见交互已双写 §2.5 + 说明书；增删模块 / 页面后跑 `python skill/scripts/docs-sync.py --fix`（AUTO 块⛔ 手改）。
- [ ] `pwsh skill/scripts/accept.ps1`（FAIL 0）；`pwsh -File .\scripts\build.ps1` 由用户本地执行；实机冒烟（筛选 / 分页 / 详情卡 / 多选）。
- [ ] 按 `ui.md` §12 人工扫一遍「该不该加元素 / 文案」（Lint 抓不到）。

## 八、同族页矩阵（机器对账）

一行一页，覆盖当前全部 `ListPageSkeleton` / `TableViewHost` 列表页（21 页）。列 ①~④ = §二 的四段；`✓` = 工作区代码里有，`—` = 没有并在末列写明原因。**只记事实 + 唯一源指针**，⛔ 不复述规范正文；`lint.py --only family` 逐页复算本表，是矩阵不漂移的唯一保障。

| 页面 Key | 骨架 / 源文件 | ① 筛选条 | ② 表头行区 | ③ 底栏分页 | ④ 详情卡 | 不适用原因与取舍 |
|---|---|---|---|---|---|---|
| `assets-all` | `TableViewHost` · `Views/AssetAllView.axaml` | ✓ | ✓ | ✓ | ✓ | 范本页，四段齐全 |
| `assets-browse` | 自绘表头 · `Views/AssetBrowseView.axaml` | — | — | ✓ | ✓ | 表头走 `HeaderArea` 自绘（磁贴 / 详细视图双形态）；按目录翻查，⛔ 不挂全局筛选条；目录走服务端分页 |
| `assets-tags` | `TableViewHost` · `Views/AssetTagsView.axaml` | — | ✓ | — | ✓ | 标签总量受控、全量内存列表；搜索走工具条，⛔ 不挂空筛选条 / 不硬凑底栏 |
| `assets-categories` | `TableViewHost` · `Views/AssetCategoriesView.axaml` | — | ✓ | — | ✓ | 分类数受控、全量内存列表；同上 |
| `assets-collections` | `TableViewHost` · `Views/AssetCollectionsView.axaml` | — | ✓ | — | ✓ | 集合数受控、全量内存列表；同上 |
| `assets-storages` | `TableViewHost` · `Views/AssetStoragesView.axaml` | — | ✓ | — | ✓ | 存储源数受控、全量内存列表；同上 |
| `assets-trash` | `TableViewHost` · `Views/AssetTrashView.axaml` | — | ✓ | — | ✓ | 回收站按条件全量列出；同上 |
| `backup-plans` | `TableViewHost` · `Views/BackupPlansView.axaml` | — | ✓ | — | ✓ | 计划数受控、全量内存列表；同上 |
| `backup-archives` | `TableViewHost` · `Views/BackupArchivesView.axaml` | — | ✓ | — | ✓ | 归档包按任务分组，⛔ 不分页 |
| `reading-shelf` | `TableViewHost` · `Views/ShelfView.axaml` | ✓ | ✓ | — | ✓ | 筛选条挂状态 / 分组维度；书架本地收藏行数受控，⛔ 不分页 |
| `reading-sources` | `TableViewHost` · `Views/SourceListView.axaml` | ✓ | ✓ | — | ✓ | 筛选条挂书源分组维度；书源数受控，⛔ 不分页 |
| `script-collections` | `TableViewHost` · `Views/CollectionView.axaml` | — | ✓ | — | ✓ | 成员随集合切换、全量内存列表，⛔ 不分页 |
| `web-scrape` | `TableViewHost` · `Views/RuleCatalogView.axaml` | — | ✓ | — | ✓ | 规则集数量少、全量加载；同上 |
| `script-library` | `TableViewHost` · `Views/ScriptLibraryView.axaml` | — | ✓ | — | ✓ | 脚本库全量加载；详情卡由基类 `QuickActionListViewModel` 实现 `IDetailCardSource` |
| `script-packages` | `TableViewHost` · `Views/PackageView.axaml` | — | ✓ | ✓ | ✓ | ① 只有 `QuickItemsSource` 快捷项（仅看已安装），⛔ 无 `FieldsContent` 维度条；winget 结果集必须服务端分页 |
| `script-commands` | `TableViewHost` · `Views/SystemCommandLibraryView.axaml` | — | ✓ | — | ✓ | 命令条目少、全量加载；同上 |
| `video-batch` | 自绘表头 · `Views/VideoBatchView.axaml` | — | — | — | ✓ | 任务队列用自绘 `HeaderArea` 分组表头；① 只有快捷项；队列全量内存，⛔ 不分页 |
| `video-list` | 自绘表头 · `Views/VideoListView.axaml` | — | — | — | ✓ | 表头自绘；① 只有快捷项；导入结果集全量内存，⛔ 不分页 |
| `web-bookmark` | `TableViewHost` · `Views/BookmarkView.axaml` | — | ✓ | — | ✓ | 书签按文件夹浏览，⛔ 不分页 |
| `web-ping` | `TableViewHost` · `Views/PingView.axaml` | — | ✓ | — | ✓ | 目标列表行数受控；同上 |
| `settings-path` | `TableViewHost` · `Views/PathView.axaml` | — | ✓ | — | — | 设置页一页多节、右详情列在同页里无处安放 ⇒ `PathViewModel` 明确不实现 `IDetailCardSource` |

### 8.1 识别口径（先拿 3 页样本校准，再全量开）

| 段 | 代码事实 | 校准结论 |
|---|---|---|
| ① 筛选条 | XAML 出现 `FilterBar.FieldsContent` 内容槽 | 样本 `assets-all` / `reading-sources` / `reading-shelf` 三页全中；只挂 `QuickItemsSource` 的 `FilterBar` 是快捷项，⛔ 不算维度条 |
| ② 表头行区 | XAML 出现 `<v:TableViewHost`（含派生类） | 18 页；`ListPageSkeleton.HeaderArea` 自绘表头的 3 页另记「自绘」 |
| ③ 底栏分页 | `ListPageSkeleton` 标签下发 `ShowFooter` + `TotalCount` + `PageSize` 三个参数 | 3 页（`assets-all` / `assets-browse` / `script-packages`）；⛔ 不是 `ListFooterBar` 标签本身 |
| ④ 详情卡 | 页面 VM 的类声明或其基类实现 `IDetailCardSource` | 20 页；`settings-path` 的 VM 不实现 |

Key 来源：菜单 `RegisterFunction` 的 key；`assets-storages` 取 `AssetStoragesViewModel.PageKey`（未挂菜单），`settings-path` 取设置节注册 key。矩阵 ⛔ 不给页面 XAML 加自定义标记属性（会污染 21 页并多一次 build），也 ⛔ 不把「四段齐全」当所有页的及格线 —— 无筛选维度的页不挂空条、无分页需求的页不硬凑底栏。

### 8.2 三条对账规则

| 规则 | 触发 | 结果 |
|---|---|---|
| F-01 | 矩阵登记 ✓ 但代码里检不出（含源文件已删 / 改路径） | `[ERROR]` 阻断 |
| F-02 | 代码里存在列表骨架页但矩阵没登记（新增页 / 改造页忘同步） | `[WARN]` 防漂移 |
| F-03 | 矩阵登记 — 但代码里有（声明与实现不符） | `[ERROR]` 阻断 |

`accept.ps1` 第 10 项跑 `--only family`；单独复核用 `python skill/scripts/lint.py --only family` 或 `pwsh skill/scripts/accept.ps1 -Only family`。
