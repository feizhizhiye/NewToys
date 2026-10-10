# NewToys 开发规范

> v1.33 · 2026-10-10 · 「开发需求（宪法）」的技术解读 + 编程规范
> 工程：NewToys（Avalonia 12 + CommunityToolkit.Mvvm + .NET 10）
>
> **约束分两类**：
> - **〔A〕硬约束**：不可变，违反=交付失败。
> - **〔B〕折衷约束**：可变但非必要不变；变更需理由+用户同意+登记第 11 章台账。
>
> **两条铁律**：① 代码是唯一真相源（文档与 `src/` 冲突以 `src/` 为准并回写）；② 本文件是唯一约定源，不参照外部工程。

---

## 0. 文档体系与更新纪律

**四份文档**（不得删除/移动/改名）：
- `NewToys开发需求.md`（宪法）：定位/边界/原则。极少改。
- `NewToys开发规范.md`（本文件）：技术标准/架构/UI/编码/模块装配/必须禁止清单/折衷台账。架构/约定/栈变化时改。
- `NewToys-项目现状与进度.md`（台账）：当前基线/已完成/待办。**每完成一个功能/一次交付更新一次**。
- `NewToys-AI协作提示词.md`：会话启动包。流程变化时改。

**更新节奏**：

| 事件 | 需求 | 规范 | 台账 | 提示词 |
|---|---|---|---|---|
| 定位/边界级变化 | ✅改 | 同步 | 同步 | 视情 |
| 架构/约定/栈变化 | 引用 | ✅改 | 同步 | 视情 |
| **完成一个功能/一次交付** | — | 视情 | ✅**更新** | — |
| **仅修报错/错别字/格式** | — | 视情 | **不回写正文**，并入下次功能更新追记 | — |
| 协作流程变化 | — | 视情 | — | ✅改 |

- 🔒 **指导性文件不乱改**（需求/规范/提示词）：不为单个任务的措辞/微调而改，仅当内容真的过时或与代码冲突才改；**《开发需求》为基石，冻结**——非定位/边界变化不动（其版本号也不随之递增）。
- **各文档版本号相互独立**：仅当该文档内容真的变化才自增，⛔ 不做「四份齐步递增」。提交信息格式 `<type>: <简述>`，type∈{feat,fix,docs,refactor,chore,style}。
- Git 仓库根=能看到 `src/` 的那一级（已 `git init`，分支 `main`）；`.gitignore` 忽略 `artifacts/ production/ bin/ obj/ Modules/ *.patch /.workbuddy/`；两个 props 必须入 git。

---

## 1. 宪法 → 技术决策（判断取舍先回到这里）

| 宪法条文 | 技术解读 |
|---|---|
| 个人工具≈带 GUI 的脚本管理器 | 页面=「命令行调用+config配置封装」；不超命令行上限 |
| 不用第三方 UI 库 | 全局样式/控件自己在 `Shell/App.axaml` 写 |
| 模块化/插件化/可热加载 | 分层 `Shared/Core/Shell` + `Module.*`；启动双轨装配+运行期热加载，失败隔离 |
| 配置即数据可备份 | 设置一律落 `config/*.json`，模块只读 |
| 失败可见 | 每页至少一行 StatusText；外部命令报错翻译后显示 |
| 可演进加/停功能 | 框架层冻结，功能只在模块内长 |
| 预留跨平台 | 平台相关能力集中封装，便于整体替换 |

---

## 2. 技术栈 〔A〕

版本集中在 `src/Directory.Packages.props`（中央包管理，`ManagePackageVersionsCentrally=true`）：

| 类别 | 组件 | 版本 |
|---|---|---|
| 运行时 | .NET | net10.0 |
| UI | Avalonia | 12.1.3 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection | 9.0.0 |
| 本地数据库 | Microsoft.Data.Sqlite | 9.0.0 |

> **SQLite（v1.34 引入）**：库文件 `<应用根>\data\newtoys.db`。**放 `data\` 不放 `config\`** —— `config\` 会被数据管理页整体备份/导入，而库里的内容（如视频媒体信息索引）是**可随时重建的缓存**，不该进备份。唯一入口 `Core/Services/SqliteDatabase.cs`（单例；`IsAvailable/LastError`，打不开只置位不抛）。既有栈版本一律不动。

**硬规则**：
- ⛔ 禁止升级/降级栈版本；`csproj` 不写 `<PackageVersion>`（`NU1010`）。
- ⛔ 禁止引入任何第三方 UI 组件库（Material/Suki/FluentAvalonia/Semi 等）。
- `LangVersion=latest`、`Nullable=enable`、`ImplicitUsings=enable`（`Directory.Build.props`）。
- **编译绑定当前关闭**：Shell 与 4 个模块 csproj 显式 `AvaloniaUseCompiledBindingsByDefault=false` → XAML 一律 `{Binding Xxx}`，**不写 `x:DataType`**（见 B1）。

---

## 3. UI 规范

### 3.1 三栏仿微信 PC 版 〔A〕

主窗口 **1180×760**，最小 900×600；`ColumnDefinitions="60,140,*"` / `RowDefinitions="40,*"`。

| 列 | 宽 | 内容 |
|---|---|---|
| 第一列 | 60px | 深色主菜单：**顶部🏠首页(BtnHome)** + 中部模块图标(48×48, `nav-btn`) + 底部⚙(BtnSettings) |
| 第二列 | 140px | 二级菜单面板（头56px，行高40px）。⚠️ v1.34 由 210 收窄到 140 |
| 第三列 | * | 内容区（`ContentControl` 承载 `CurrentContent`） |

自定义标题栏 40px（`WindowDecorations=None` + `ExtendClientAreaToDecorationsHint=True` + `ExtendClientAreaTitleBarHeightHint=40`），按钮 46×40；不使用过的 `SystemDecorations`。

### 3.2 关键例外规则 〔A〕

- **首页隐藏第二列**：点🏠 → `SubPanel.IsVisible=false` + 内容区 `ColumnSpan=2`；点模块切回三栏。
- 🔴 **第二列可见性唯一出口：`ShellView.UpdatePanelVisibility()`**。`CurrentGroup`/`IsHomePage` 任一变化都必须调它；⛔ 不能只调 `UpdateSubMenu()`。
- 🔴 **🏠 常驻第一列顶部**（`BtnHome→GoHomeCommand`）；⛔ 不许删。
- 🔴 **导航高亮用 `active` class**（`ShellView.ApplyNavState()` 只增删 class）；⛔ 不许 code-behind 写 `Background/Foreground` 本地值（会让 `:pointerover` 失效）。首页态=模块都不高亮。
- **⚙ 固定第一列最底**（`RegisterModule("settings",…,order:999,isBottom:true)`）；复用 XAML 的 `BtnSettings`，code-behind 不再 `Click+=`。

### 3.3 尺寸与样式

- 卡片 `Border.card`：圆角 10px、内边距 18px。
- 按钮 `primary`/`ghost` 圆角 6px；`nav-btn` 圆角 10px；首页一键按钮高 76px。
- 第二列行内边距 12px、`Margin="8,2"`；内容区 `Margin="24,16"`。
- **取消页面页头**（页名由第二列体现），原页头控件移内容区顶部右对齐一行。

全局样式在 `Shell/App.axaml`：`Button.nav-btn/.active` / `Button.win-btn/.close` / `Button.primary/.ghost` / `Border.card` / `Button.col-header` / `Border.list-header` / `TextBlock.list-cell` / `ListBox.list-page`。⛔ 页面里不复制同名样式；本页专用样式写 `UserControl.Styles` 里加更具体选择器。除第一列外所有颜色绑 `DynamicResource`。

### 3.4 主题 〔A〕

- **三模式**（`IThemeService.ThemeMode`）：`Light`/`Dark`/`System`（跟随系统），持久化 `config/theme.json`；默认深色；`IsDark` 是冗余快照。
- **强调色单一数据源**：`Shared/Services/AccentPalette.cs`（6 预设）；Shell 与主题页不许各写一份色值表。
- **Core 不引用 Avalonia**：
  - `Core/ThemeService`：只持状态、读写 json、广播 `ThemeChanged`。
  - `Shell/App.WatchSystemTheme()`：唯一探测系统明暗的地方。
  - `Shell/App.ApplyTheme()`：按 `(IsDark,AccentId)` **纯代码构建** `ResourceDictionary` 注入，幂等。
- ⛔ 不用独立 `.axaml` 资源字典+`AvaloniaXamlLoader.Load`（Avalonia12 抛 `No precompiled XAML found`）；⛔ 切主题只移除上次字典，禁 `Resources.MergedDictionaries.Clear()`。
- ⭐ code-behind `new` 出来的控件是本地值，换主题不自动变 → 必须挂重建逻辑或用 `Classes`+样式（优先）；⛔ code-behind 写死 `#RRGGBB`，用 `FindResource("XxxBrush")` 并兜底。
- **主题资源键**一律 `*Brush` 语义键，共 32 个（`ContentBrush/PanelBrush/SubSiderBrush/SiderBrush/PrimaryTextBrush/SecondaryTextBrush/AccentBrush/AccentHoverBrush/DangerBrush/DividerBrush/InputBorderBrush…`）；新增 Light/Dark 两套必须同时补齐；引用不存在键**编译不报错、运行期才失败**。
- 废弃旧键：`AppBackground/TitleBarBackground/TextPrimary/TextSecondary/BorderColor/CardBackground`（长键）、`AppBg/TitleBarBg/Text1/Text2/CardBg/PanelBg/SidebarBg/HoverBg/Border`（短键）。

### 3.5 设置项：即时生效 vs 保存 〔A〕

**判据：改完能不能立刻看到结果。**

- **即时生效**（主题/强调色/开关/DoH）：控件变→`partial void OnXxxChanged`→立刻写服务/配置→界面反映；⛔ **禁止放「应用/保存/确认」**。
- **复杂输入**（路径配置/多行清单）：显式点「💾 保存」提交；「还原默认」只预填。
- **动作/危险操作**（备份/导入/打开目录）：直接执行；不可逆才二次确认。

每页至少一行 StatusText；⛔ 只做了持久化、系统级行为（开机启动/托盘等）没实现的，须如实标注。

### 3.6 列表页布局 〔A〕

凡是「一堆条目+对条目操作」的页面一律用此布局，不创第二套：主区 `ColumnDefinitions="*,Auto"`（右列详情**固定 340px 写在 Border 上**）；上为工具条+可排序表头（`Button.col-header`），左为 `ListBox.list-page`（`ContextMenu` 行菜单），右为选中项详情。

> **右列宽度写在 Border 而不是 Grid 列上**（v1.33）：`ColumnDefinitions` 第二列用 `Auto`，详情 Border 自己 `Width="340" IsVisible="{Binding IsDetailVisible}"`；收起时该列不占位 → 列表自动铺满。
> ⛔ 不要写回 `"*,340"`：那样即使 `IsVisible=false` 也仍占 340px 空白。
> **详情栏默认收起**（`IsDetailVisible` 初值 false，`<pageKey>` 读 `IColumnLayoutService.LoadFlag(..., FlagDetailPanel)`）。**开合入口 = 表头最右侧一个无文字的 `▤` 小按钮**（`Content="▤"`，绑 `ToggleDetailCommand`；v1.34 起⛔ 工具条上不再放「▤ 详情」文字按钮），点它 = 展开/收起 + **自动选中列表第一项**；右键菜单首项「▤ 显示详情栏」调 `ShowDetailCommand`（不切换、只确保展开并定位）。点选结果落 `SaveFlag` 记住。⛔ `Grid.ColumnDefinitions` 是 CLR 属性，XAML 里不能绑定 / 也写不了 `IsVisible`（`AVLN3000`）。

**五条硬规则**：
1. 表头与行模板**完全相同的星号列定义**（逐字一致）；动态列用 `<v:ColumnGrid ColumnSpecs="{Binding VisibleColumnSpecs}">`。
2. 表头与行单元格左右内边距一致（12px），行里不写 `Padding/Margin`。
3. `ListBoxItem` 拉伸（已在 App.axaml 设 `HorizontalAlignment=Stretch`），页面不覆盖。
4. 表头双交互：① 左键点→切换排序（`SortCommand`+CommandParameter，VM 追加▲▼）；② 右键点→`ContextMenu` 勾选列显隐（`ToggleType=CheckBox`，`IsChecked` 显式 `Mode=OneWay`）。⛔ **五处列表页**（pc-package / video-list / pc-shortcuts / pc-quicksettings / **settings-path**）必须都支持；隐藏列用宽度 0（不删列保一致）。
5. 空态与选中态显式处理：无选中显示引导；有选中显示详情；空列表用居中 StatusText，不留白。

行右键菜单提供该条目全部可执行动作（与详情按钮一一对应）。

- ⭐ `ContextMenu` 挂在独立弹出树，不保证继承宿主 DataContext → 必须在 View 的 code-behind `OnDataContextChanged` 里手工同步（行菜单+表头菜单都要）。
- **例外的按钮页（`pc-favorites`）**：收藏 = 常用功能 → **不用卡片也不用列表**，就是一排按钮（`ItemsControl` + `WrapPanel`，模板里只有一个 `Button Content="{Binding Title}"`，点一下即跑），⛔ 不放版本/来源/简介等复杂信息。按钮的右键菜单在 code-behind 的 `ContextRequested` 里**现场构造**（`new ContextMenu`+`MenuItem` 直接挂 `Command`/`CommandParameter`，`menu.Open(button)`）—— 模板内的 `ContextMenu` 与 `$parent[UserControl]` 在弹出树里都会断链，现场构造最稳。
- 列显隐 + **页面级开关**：元数据 `Shared/Models/ListColumn`、服务 `IColumnLayoutService/ColumnLayoutService`（落 `config/columnlayout.json`，自动进备份）。同一个文件里存两类：① 列显隐 `LoadPage/SavePage`（键 = 列键）；② 页面级开关 `LoadFlag/SaveFlag(FlagDetailPanel)`（内部加保留前缀 `__`，与列键不会撞）。⛔ 禁把 `ColumnDefinitions` 绑 `{Binding}`（`AVLN3000`）、禁 WPF 的 `IsCheckable`（`AVLN2000`）。⚠️ `Views/ColumnGrid.cs` 因**模块间禁互引**而在 `Module.PcTools` / `Module.VideoTools` / `Module.Settings` **各存一份，改动必须同步三份**。

---

## 4. 架构与命名 〔A〕

### 4.1 依赖方向（单向，禁反向）

```
Shared（契约/模型，不引 Avalonia）
  ▲
Core（服务实现，不引 Avalonia）
  ▲
Shell（宿主，引 Avalonia，AssemblyName=NewToys → NewToys.exe）
  ▲
Module.*（功能模块，只引 Shared+Core）
```

- ⛔ 模块禁 `using NewToys.Shell.*`（循环依赖）；⛔ 模块之间禁互引；跨模块协作走 `Shared` 契约。
- 公共基类/契约放 `Shared`。

### 4.2 目录结构

`src/` 是纯净源码区：7 个项目 + `NewToys.slnx` + `Directory.Build.props` + `Directory.Packages.props` + `docs/`（4 份文档）。

```
src/
├── Directory.Build.props / Directory.Packages.props / NewToys.slnx / docs/
├── Shared/           # 契约层：Models/Services/Input
├── Core/             # 服务实现层：Services
├── Shell/            # 宿主：App.axaml(.cs) Program.cs app.manifest + ViewModels/Views/Converters/
├── Module.PcTools/ Module.WebTools/ Module.VideoTools/ Module.Settings/
```

模块内部（按需创建）：`XxxModule.cs` + `Models/`+`Services/`+`ViewModels/`+`Views/`（可选 `Behaviors/Converters/Resources/Common/`）。

> 两个 props 必须在 `src/` 内。
> ⛔ `Shell/Views/ErrorView.axaml`、`PlaceholderViewModel`、`Shell/Services/NavigationService` 源码已不存在；`NavigationService` 现在位于 `Core/Services/`。

### 4.3 框架层冻结

- `Shared/Core/Shell` 稳定后默认不再改，新功能只在 `Module.*` 内。
- 确需改框架层（新增全局服务/契约、调全局布局）**必须先获用户明确同意**，交付里独立说明。

### 4.4 命名约定

| src 目录 | AssemblyName | RootNamespace | 产物 |
|---|---|---|---|
| Shared/ | NewToys.Shared | NewToys.Shared | NewToys.Shared.dll |
| Core/ | NewToys.Core | NewToys.Core | NewToys.Core.dll |
| Shell/ | **NewToys** | NewToys.Shell | NewToys.exe |
| Module.\<X\>/ | Module.\<X\> | NewToys.\<X\> | Module.\<X\>.dll |

- ⛔ C# 命名空间 `NewToys.<模块>.Views/.ViewModels`，**不得出现 `.Module` 段**；新增文件跟同目录已有文件写法。
- 资源 URI 按 `AssemblyName` 寻址（`avares://`），不是 RootNamespace、也不是 C# 命名空间。
- 模块私有 vs 全局契约：被 ≥2 模块用或属应用级能力才放 `Shared`（接口）+`Core`（实现）；判断不清默认放模块内部。

---

## 5. 编码规范

### 5.1 C#

- 文件作用域命名空间（`namespace X;`）；`using` 置顶，`System.*` 优先。
- 命名：公共成员/类型/方法 `PascalCase`；私有字段/局部/参数 `camelCase`（私有字段前缀 `_`）；异步方法 `Async` 结尾。
- 带 `[ObservableProperty]`/`[RelayCommand]` 的类**必须 `partial`**（否则 `CS0260`）。
- 属性优先 `[ObservableProperty]` 私有字段；命令 `[RelayCommand]`。
- 构造注入为主；`Ioc.Default` 仅在 code-behind/回调等无法构造注入处使用。
- 集合 `ObservableCollection<T>`；公共 API 写 XML 注释；复杂逻辑注释「为什么」。
- ⚠️ 改 `[ObservableProperty]` 字段名/语义时全仓搜旧名（属性名+`_字段名`+`nameof`）；构造里 `_xxx=…` 合法，别顺手修。

### 5.2 XAML（.axaml）

- 扩展名必须 `.axaml`（Avalonia12 不认 `.xaml`）；所有 `xmlns` 声明在根元素。
- 编译绑定关闭：写 `{Binding Xxx}`，**不写 `x:DataType`**（见 B1）。
- ⛔ WPF 专有写法（`AVLN2000`）：`ItemContainerStyle`、`<Style TargetType>`（用 `Selector`）、`Border.HorizontalContentAlignment`、`RowDefinitions` 短标签 `<Auto/>`、`IsCheckable`。
- 颜色一律 `DynamicResource`；文案一律中文。
- XAML 与 code-behind 分离；交互复杂的列表在 code-behind 构建。
- 🔴 改 `.axaml` 必同步改 `.axaml.cs`（`x:Name`/控件类型/`Grid.Row`、`Column` 越界 → `CS1061/CS0103`）。

### 5.3 跨平台与路径

- 一律 `Path.Combine`，⛔ 硬编码 `\` 或 `/`。
- **用户数据**由 `ConfigStoreService` 决定（不在仓库内）：应用根 `%LocalAppData%\NewToys`（环境变量 **`NEWTOYS_HOME`** 整根切换，主要用于 test vs prod）→ `config/data/logs` 三层。
  - ⛔ 模块不自己拼路径，向 `IAppConfigService.DataDirectory`/`ConfigStoreService` 取值。
- 平台相关能力（winget/ms-settings:/ffmpeg）集中封装，便于替换为 Debian 实现。
- 路径穿越防御：外部相对路径 `Path.GetFullPath` 后校验落在目标目录内。

### 5.4 异常处理

- 导航/模块加载/主题应用等边界处 `try-catch`，失败不崩溃；至少写日志或兜底 UI，不得吞异常无反馈。
- 全局兜底：`AppDomain.UnhandledException`/启动异常捕获，崩溃给可读错误窗。

### 5.5 构建纪律

- 构建/发布/环境切换**由用户本地执行**（可用 `scripts/build.ps1 -Configuration Debug|Release`）；AI 改完 `src/` 后列出需用户验证的点，不代跑构建。
- ⛔ 禁止声称「已编译通过」；没跑过写「未编译，请用户本地执行 `build.ps1` 验证」。
- 改代码先静态自查：`.axaml` ↔ `.axaml.cs` 同步；`using`/类型/命名空间对得上；引用主题键在键表里；新增模块登记三处。
- 用户反馈编译失败 → 先修完再继续下一需求，不把坏基线带进下一轮。
- 纯文档变更（只动 `docs/`）注明「不涉及编译」。

---

## 6. 模块开发

### 6.1 模块接口

```csharp
public interface IModule {
    void RegisterServices(IServiceCollection services);
    void Initialize(IServiceProvider services);   // 这里注册菜单
}
```

模块类命名 `XxxModule`，命名空间 `NewToys.<模块名>`（不带 `.Module` 段）。

### 6.2 菜单注册 API

```csharp
AppMenuGroup RegisterModule(string key, string title, string icon, int order,
                           bool isBottom=false, bool isHome=false);
void RegisterFunction(string moduleKey, string key, string title, string icon,
                      string viewName, int order=0, string desc="");
```

- 先 `RegisterModule` 拿分组，再 `RegisterFunction`（找不到分组抛 `InvalidOperationException`）。
- `viewName` 传**视图类名**（如 `"PcInfoView"`），不是 VM 名、不是程序集限定名。
- ⭐ **命名铁律：`Shared/Models` 里的模型名不得与所用库（Avalonia / CommunityToolkit / BCL）的公开类型重名。** 菜单模型即因此定名 `AppMenuGroup`/`AppMenuItem`（若叫 `MenuGroup`/`MenuItem`，凡同时 `using Avalonia.Controls` 与 `NewToys.Shared.Models` 的 code-behind 裸写该名会 `CS0104` 不明确引用，v1.32 已踩过一次）。新增模型先查一次库内同名。
- 菜单**只能在 `Initialize` 注册**，`RegisterServices` 禁注册。
- 菜单 Key：`<模块缩写>-<页面名>` 全小写连字符（`pc-shortcuts`/`settings-theme`/`video-list`）。
- 分组 Order 现状：`pc`=20 ｜ `web`=30 ｜ `video`=40 ｜ **`settings`=999, isBottom:true**（永远最底）。新增分组**必须显式给 `Order`**，否则顺序随程序集加载漂移。
- 排序：`OrderBy(IsBottom?1:0).ThenBy(Order)`；分组内 `Items` 按 `Order` 重排。
- `desc`（功能简介）务必填写：功能清单页展示、首页收藏卡复用。
- 首页入口 = 第一列顶部 `BtnHome→GoHomeCommand`；`AppMenuGroup.IsHome` 契约预留，4 个模块均未用。
- 菜单点击路径：`SelectGroupCommand`/`SelectItemCommand`（参数=菜单Key）→ VM 内 `ResolveView` 写 `CurrentContent`，**全程不经过 `NavigationService`**（见 B4）。

### 6.3 模块加载（启动双轨）

`App.OnFrameworkInitializationCompleted()`：
1. 建 `ServiceCollection` 注册核心服务；
2. `ModuleLoader.LoadAll(services, GetDebugModuleTypes())`：Debug 用 `#if DEBUG` 显式 `typeof` 清单；Release（传 null）扫 `<程序目录>\Modules\`，跳过已加载同名；
3. `BuildServiceProvider()` → `AppServices.Initialize(sp)`；
4. `ModuleLoader.InitializeAll(sp)`（**必须在 3 之后**）；
5. 应用主题（`IThemeService.IsDark→ApplyTheme`，订阅 `ThemeChanged`）；
6. `new ShellView{DataContext=ShellViewModel}→win.Show()`；
7. 首导由 `ShellViewModel` 构造函数末尾 `GoHomeCommand.Execute(null)` 触发。

| | Debug | Release |
|---|---|---|
| 模块源 | `Shell.csproj` Debug 条件 `ProjectReference` | `<程序目录>\Modules\` DLL |
| 需要 `Modules\` | 否 | 是（无则兜底程序目录） |
| MSBuild 拷贝 | 否 | 否（OutDir 直出） |

**硬规则**：
- Debug 必须用显式类型清单（⛔ 不能只靠 `AppDomain.GetAssemblies()`——延迟加载会一个模块都扫不到）。
- 跳过某模块改两处：`App.axaml.cs` 的 `typeof(XxxModule)` + `Shell.csproj` 的 `ProjectReference`。
- Release 路径用 `AppContext.BaseDirectory` 拼绝对路径。
- 模块 DLL 命名 `Module.<模块名>.dll`。
- ⛔ **`Modules\` 是模块产物唯一去处，全程零拷贝**：`src/Directory.Build.props` 把模块 Release `OutDir` 直设为 `$(NewToysReleaseDir)Modules\`；禁在 csproj/脚本里加「拷贝到 Modules」目标。
- `Modules\` 里可能多出 `NewToys.Shared.dll`/`NewToys.Core.dll`——无害（加载器跳过同名）；Release 关 pdb（`DebugType=none`）消除并发写 `MSB3026`。
- **启动装配（本节）与运行期热加载（6.6）共用同一个 `Modules\` 目录与同一套「跳过已加载同名」规则。**

### 6.4 新增模块步骤

1. 建 `src/Module.Xxx/Module.Xxx.csproj`：`RootNamespace=NewToys.<Xxx>`、`AvaloniaUseCompiledBindingsByDefault=false`，引 `Shared+Core`；不写输出/拷贝路径。
2. 写 `XxxModule.cs : IModule`，命名空间 `NewToys.<Xxx>`。
3. `src/NewToys.slnx` 加 `<Project Path="Module.Xxx/Module.Xxx.csproj" Type="C#" />`。
4. **Debug 链路两处登记**：`Shell.csproj` Debug 条件 `ProjectReference` + `App.axaml.cs` 的 `#if DEBUG` 清单。Release 不需登记。
5. `Initialize` 里 `RegisterModule`+`RegisterFunction` 注册菜单，`viewName`=View 类名。

### 6.5 页面开发与 VM 定位

- View：`UserControl`，放模块 `Views/`，扩展名 `.axaml`+`.axaml.cs`。
- VM：新增 VM **跟同模块已有 VM 保持一致**（现状模块内多直接继承 `ObservableObject`，见 B2）。
- 定位：`NewToys.<模块>.Views.XxxView` ↔ `NewToys.<模块>.ViewModels.XxxViewModel`，必须成对。
- 解析（`ShellViewModel.ResolveView`）：① 按 `ViewTypeName` 遍历**全部已加载程序集**匹配 `Type.Name`（`Control` 子类）**取首个命中** → ② `ActivatorUtilities.CreateInstance` 建视图 → ③ 推导 VM 时**优先限定在 View 自己的程序集**（`.Views`→`.ViewModels`、`XxxView`→`XxxViewModel`），找不到才降级全 `AppDomain` 扫类名。
  > ⚠️ **View 解析这一步未按模块限定程序集**（只按类名取首个）→ 各模块出现同名 View 时会撞车（现状两处 `DnsView`：`Module.Settings` 与 `Module.WebTools`），取到的可能是另一模块的同名页；只有 VM 解析做了程序集限定。
- 无占位页：兜底是 `ShellViewModel.BuildErrorView()` 现场构造的红字+`ScrollViewer`。
- 可以先注册菜单、后补页面，补齐 View+VM 后自动生效。

### 6.6 运行期热加载模块（`modules/` + 刷新按钮）〔A〕

**目标**：宿主运行中，新 `Module.*.dll` 放进模块目录，点 UI「刷新/加载」即装配，**不重启**。

- 📌 **目录只有一个**：用户口语 `modules/` = 项目既有 `<程序目录>\Modules\`（Windows 大小写不敏感）。⛔ 不新增第二个目录。
- **扫描**：
  - 路径 `Path.Combine(AppContext.BaseDirectory,"Modules")`（绝对路径）。
  - 只认 `Module.<模块名>.dll`，其余跳过。
  - ⛔ 跳过已加载同名程序集 → 本版**只做「新增」**，不做同名替换/卸载（见 B10）。
- **加载**：
  - 每个待加载 DLL 用**独立 `AssemblyLoadContext`**（`isCollectible:true`，为将来卸载留口）。
  - `NewToys.Shared`/`NewToys.Core` 一律由**默认 ALC 共享**（ALC 的 `Load` 覆写里把契约程序集转交默认上下文）——否则两套类型标识，`IModule` 强转必失败。
  - 反射取 `IModule` 实例化；这是受控反射例外（见 B11）。
- **注册与刷新**：
  - ⛔ 已 `BuildServiceProvider()` 的容器无法再追加服务 → 本版热加载模块 `Initialize` 只依赖已注册服务，`RegisterServices` 可跳过；故热加载优先**页面型模块**（见 B10）。
  - 加载后调 `IMenuService.RegisterModule/RegisterFunction` 注册菜单。
  - UI 刷新：通知 `ShellViewModel` 重建第一列 `BuildNavButtons()` 与第二列 `UpdateSubMenu()`，并调 `UpdatePanelVisibility()`。
  - 刷新入口由 Shell 提供；挂哪个页面（建议「设置·关于」或「功能清单」）不锁死。
- **失败隔离（硬要求）**：单个 DLL 加载/注册异常 → `try-catch` 兜住，只记日志+一行 StatusText 提示模块名与原因；⛔ 宿主与已加载模块不得崩溃。完成后 StatusText 报「本次新增 N/跳过 M/失败 K」。
- **安全**：⛔ 只读本地 `Modules\`；不联网不下载；加载前 `Path.GetFullPath` 校验路径落在 `Modules\` 内（防穿越）；⛔ 不接受任意路径/手输路径加载。

---

## 7. 构建、运行与产物

### 7.1 脚本（PowerShell 7，用户本地执行）

| 脚本 | 说明 |
|---|---|
| `build.ps1 [-Configuration Debug|Release] [-Restore]` | 还原+编译+核对模块 DLL 计数，不含拷贝 |
| `run.ps1 [-Configuration] [-Env test|prod] [-NoBuild]` | 启动；`-Env test` 把本次进程 `NEWTOYS_HOME` 指到测试根 |
| `env.ps1 [test|prod]` | 整根切换 `NEWTOYS_HOME`（用户级持久）；空参查看 |
| `publish.ps1 -Target <dir> [-Runtime] [-SelfContained]` | 发布，用 `/p:NewToysReleaseDir=<dir>\` 直出（必须带结尾分隔符） |
| `clean.ps1` / `pack.ps1` | 清理 / 打源码包 |

### 7.2 产物路径

| 产物 | 路径 |
|---|---|
| Debug 输出 | `artifacts/bin/<项目名>/Debug/net10.0/` |
| Debug 主程序 | `artifacts/bin/Shell/Debug/net10.0/NewToys.exe` |
| Release 输出根 | `artifacts/release/`（可 `/p:NewToysReleaseDir=<dir>\` 覆盖） |
| Release 模块 DLL | `artifacts/release/Modules/Module.*.dll`（直出，零拷贝） |
| 发布产物 | `production/NewToys_<运行时>/` |
| 测试环境根 | `artifacts/env/test/` |
| 用户数据 | ⚠️ **不在仓库内**：`%LocalAppData%\NewToys\{config,data,logs}` |

AI 只读 `src/`，⛔ 不读 `artifacts/ bin/ obj/ .vs/`。

### 7.3 版本号

**编译时自动生成，禁止手写**（`src/Directory.Build.props`）：
- `InformationalVersion` = `1.<yyyyMMdd>.<HH>.<mm取整到十位>`（显示用）
- `FileVersion` = `1.<yyyy>.<MMdd>.<HHmm>`；`AssemblyVersion` 固定 `1.0.0.0`
- 「设置·关于」读 `InformationalVersion`；所有项目（含模块 DLL）共享同一版本号。

---

## 8. 启动与导航

### 8.1 启动时序（见 6.3）

顺序不可颠倒：`InitializeAll` 早于 `BuildServiceProvider` → `GetRequiredService<IMenuService>()` 拿不到；主题在 `ShellView` 之后 → 首帧闪错色。全程包 `try-catch`，启动异常弹错误窗。

### 8.2 防闪退铁律

1. ⛔ 禁在「菜单注册完成之前」调用导航（启动闪退）。
2. `NavigateTo` 找不到 key 不得抛异常。
3. 视图解析必须返回非 null `Control`（异常返回错误控件）。
4. 一级/二级菜单点击唯一入口 `SelectGroupCommand`/`SelectItemCommand`，单向命令，⛔ 不引入隐式双向绑定。
5. 切换首页/模块同步第一列选中态与第二列可见性（见 3.2）。

---

## 9. 数据与配置 〔A〕

`config/` 下配置：

| 文件 | 归属 | 内容 |
|---|---|---|
| `theme.json` | 软件设置 | 主题模式+强调色 id |
| `app.json` | 软件设置 | ffmpeg/播放器/DNS 开关/**脚本运行环境**（python、pwsh、PS5.1 路径 + 脚本目录）。⚠️ v1.34 起**没有视频库目录**（与视频模块解绑，每次手动选） |
| `launch.json` | 软件设置 | 三个一键按钮+4 个通用开关 |
| `package.json` | 软件设置 | 软件包查询偏好（默认源/静默安装/排序列；兼容旧 winget.json）。⚠️ v1.33 起**源本身不在这里**——源的增删直接作用于系统 winget |
| `favorites.json` | 用户功能数据 | 首页收藏夹 |
| `pc-scripts.json` | 用户功能数据 | 「设置与脚本」页清单（**只有带脚本文件的脚本**，v1.34 起独立成文件） |
| `pc-quick.json` | 用户功能数据 | 「快捷设置」页清单（命令行命令，v1.34 起独立成文件） |
| `columnlayout.json` | 用户功能数据 | 列表页列显隐偏好 **+ 页面级开关**（详情栏开合，键带保留前缀 `__`） |

**硬约束**：
- 设置类数据一律落 `<应用根>\config\*.json`（才能被数据管理页备份/导入）。
- **模块只读设置**，唯一写入点在该设置的设置页；功能入口页**只读**（⛔ 不许回写→双真相源）。
- 用户功能数据由对应功能页读写，同样落 `config\`、一样进备份。
- ⛔ **不许把"内容清单"写死在 C# 里**（v1.33）：默认内容一律做成文件，随程序发布在 `<程序目录>\defaults\*`，**首次运行**复制/播种成 `config\` 下的用户文件；此后一切以用户那份为准（用户删掉的条目不会被"复活"）。当前：`defaults\pc-quick.default.json` → `config\pc-quick.json`、`defaults\pc-scripts.default.json` → `config\pc-scripts.json`；`defaults\scripts\*` → 脚本目录（仅当目录不存在时才播种）。
  - 发布方式：`Shell.csproj` 用 `<None Update="defaults\**" CopyToOutputDirectory="PreserveNewest" />`（⛔ 用 `Update` 不用 `Include`，否则撞 `NETSDK1022`）。默认数据放 `Shell` 而不是 `Core`：Shell 是被引用终点，保证程序根目录只有一份。
  - **脚本目录**：默认 `<应用根>\scripts`（= config/data 同级；由 `IAppConfigService.DefaultScriptFolder` 推出），可在「设置 → 路径配置」改。脚本目录里的 `.py/.ps1/.bat` 就是「设置与脚本」页的条目目标。
  - **解释器路径**（python / pwsh / PowerShell 5.1）留空 = 用系统 PATH 里的；只有填了"看起来像路径"的值才校验存在性。`.ps1` 未配 pwsh 时自动退回系统自带的 5.1。
- **改系统级配置的页面必须二次确认**（v1.33）：如「设置 → 软件源」的增删源 / 恢复默认直接改系统 winget（与系统共用一套数据），破坏性操作走 `Core` 静态钩子 `PackageManagerService.ConfirmHandler` → Shell 模态框；⛔ 没接钩子时一律返回 false（宁可什么都不做）。

---

## 10. 交付与协作 〔A〕

1. **先落盘后声明**：交付件必须真实写入，用命令确认存在、大小正常；⛔ 没落盘不许说「已生成/就绪」。
2. **只改 `src/`**（`scripts/` 协作脚本除外）；⛔ 不改 `src/` 以外目录。
3. 构建由用户本地执行，AI 不代跑、不谎报「已编译通过」，列出需用户验证点。
4. 交付说明包含：变更清单（增/改/删逐条+一句话）+ 前置基线 + 用户需执行的验证命令。
5. **复杂功能先给可运行的最小实现**，再逐步扩展。
6. 框架层冻结，改框架层须先获用户同意。
7. 一次只改一个主题，规范/文档更新与代码更新合并进同一交付。
8. **基线管理**：以用户本地 HEAD 为基线，尚未 `git init` 时直接写入工作区文件；`git apply` 失败先问清实际基线，⛔ 禁盲目 `--3way` 或强推。

---

## 11. 硬约束总清单（A 类）

### 11.1 必须
1. 动代码前按任务型读 `src/docs/`（分级读档：先读「AI协作提示词」+ 台账「当前基线」，规范只读任务相关章节；精确流程见提示词 [0]）。⛔ 不要求整篇通读。
2. 保持既有 UI 不回退（三栏仿微信、首页隐第二列、⚙最底、🏠最顶、自定义标题栏、明暗主题三态+强调色、默认深色）。
3. 技术栈版本锁定，不擅升擅降。
4. 交付交用户本地跑 `build.ps1`（Debug+Release）验证；不代跑不谎报，列清需用户确认点。
5. 交付时含变更清单+前置基线+用户验证命令；先落盘再声明。
6. 框架层冻结，新功能只在 `Module.*`；改框架层须先获用户同意。
7. 代码是唯一真相源，写「现状」前先读源码，冲突以代码为准回写。
8. 动 `ShellView.axaml/.axaml.cs` 两文件必须同步。
9. 列表页按 3.6：可排序表头+`ListBox.list-page`+右侧详情（宽 340 写在 Border 且**默认收起、可开合**，**表头右侧无文字 `▤` 小按钮**切换，状态记忆）+行右键菜单（**增删改查齐全**）；表头与行同套列定义、内边距一致；`ContextMenu.DataContext` 在 code-behind 手工同步；表头支持右键增删列。
10. 设置数据必落 `config\*.json` 且可备份；模块只读，唯一写入点在设置页；内容清单不写死在 C#，默认值随程序发布在 `<程序目录>\defaults\*` 并首次运行播种（§9）。
11. 规范与代码同步更新；各文档版本号**独立递增**（不再齐步）。
12. 复杂功能先给可运行的最小实现。
13. 新增模块登记三处：`slnx` + `Shell.csproj`（Debug 条件组）+ `App.axaml.cs` 的 `#if DEBUG` 清单。
14. 热加载只从本地 `Modules\` 取 DLL，逐个 `try-catch` 隔离；加载后刷新两侧菜单并复用 `UpdatePanelVisibility()`。

### 11.2 禁止
1. 引入任何第三方 UI 库。
2. 臆造用户未要求的功能。
3. 删除/回退用户已确认的实现与文档。
4. ViewModel 构造函数里触发导航。
5. 视图解析返回 null 导致崩溃。
6. 模块引用 `Shell` 或模块互引。
7. `csproj` 写包 `Version`。
8. 使用 `.xaml` 扩展名、WPF 专有 XAML 写法。
9. 只改代码不改规范（或反之）。
10. 无正当理由改框架层。
11. 照搬任何参考工程的目录结构与命名（功能可参考，结构与命名以本规范为准）。
12. 交付件未落盘就声明「已生成/就绪」。
13. **凭记忆写「工程现状」**（尺寸、键名、API 签名、文件是否存在、能否编译）。
14. **未真实执行构建就宣称「编译通过」**。
15. 只改 `.axaml` 或只改 `.axaml.cs` 其中一个。
16. code-behind 写死颜色（`#RRGGBB`/`Colors.White`）。
17. 给「点一下立刻能看到效果」的设置加「应用/保存」按钮；反之把多字段表单做逐键落盘不给保存按钮。
18. **`Core` 里引用 Avalonia**（系统明暗/平台能力由 Shell 探测后注入）。
19. 自创第二套列表页布局；列表页缺「右键表头增删列」；表头 `ContextMenu.DataContext` 不同步。
20. 在功能入口页写设置（双真相源）。
21. csproj/脚本里写任何「拷 DLL 进 `Modules\`」的目标。
22. `src/` 之外新增业务代码。
23. **热加载联网下载模块，或从 `Modules\` 以外任意路径加载 DLL**（会滑向插件市场）。
24. **热加载失败让宿主崩溃、或整列菜单消失**（必须逐个隔离）。
25. **把内容清单硬编码进 C#**（应用默认内容一律落 `<程序目录>\defaults\*` 并首次运行播种，§9）。
26. **应用侧类型名与所用库的重名**（如自己定义 `MenuItem`/`Grid`/`Panel` → `CS0104`；应用侧菜单模型已改名 `AppMenuGroup`/`AppMenuItem`，铁律见 §6.2）。
27. **默认数据用 `None Include` 而非 `None Update`**（后者才是"只改复制属性"，`Include` 会撞 `NETSDK1022` 重复项）。

---

## 12. 折衷约束台账（B 类）

> 当前做法为「需求 vs 工程现实」妥协，非永久铁律。**变更条件满足+用户同意+本表登记**后可调；未登记不得擅改。新增折衷沿用 `B<n>` 编号。

| 项 | 理想 | 当前折衷 | 变更条件 | 影响面 |
|---|---|---|---|---|
| **B1 编译绑定** | 开启编译绑定，性能更好 | 全工程关闭，不写 `x:DataType` | 用户拍板统一开启并补 `x:DataType` | 全部 View |
| **B2 VM 基类** | 统一继承 `Shared/ViewModelBase` | 模块内多直接继承 `ObservableObject`，仅 Shell/Home 用 `ViewModelBase` | 某模块改造时逐模块统一 | 各模块 VM |
| **B3 全局样式** | 显式 `x:Key` 样式键 | 用 `Classes` 隐式样式 | 收益足够大时整体改造 | `App.axaml` |
| **B4 导航服务** | 导航统一走 `INavigationService` | 主流程 `ShellViewModel` 自解析，`NavigationService` 在 Core 注册但闲置 | 删闲置服务或改用它（属框架层需同意） | Shell/Core |
| **B5 平台后端** | 完全跨平台 | 后端仍调 winget/ffmpeg 命令行，类型已中立为 `Package*` | 迁 Debian 时替换 | Core 服务 |
| **B6 仓库根 slnx** | 唯一入口 `src/NewToys.slnx` | 仓库根残留旧 `NewToys.slnx` 副本 | 直接删除即可 | 仓库 |
| **B7 Modules 冗余 DLL** | `Modules\` 仅含模块 DLL | 会多出 `NewToys.Shared.dll`/`NewToys.Core.dll` | 无害暂留（加载器跳过） | Release 产物 |
| **B8 闲置转换器** | 不留无用文件 | `Shell/Converters/BoolInversionConverter` 未被引用 | 保留或删除均可 | Shell |
| **B9 骨架页面** | 全部页面落地真实逻辑 | 9/23 页仍为骨架 | 按节奏实装（见台账 P2） | 各模块 |
| **B10 运行期热加载** | 任意模块随时加载/卸载/替换，服务随模块可扩 | 仅支持「新增未加载过的模块」；同名不卸载不替换；容器已 build，模块不能注册新服务（优先页面型模块） | 引入可卸载 ALC+模块级容器（或可扩展容器）后升级 | ModuleLoader+Shell |
| **B11 反射使用** | 最小魔法、少反射 | 模块装配/热加载/视图解析三处用反射枚举 IModule 与 View/VM | 机制必需暂不改；⛔ 仅限这三处，不得扩散 | ModuleLoader/ShellViewModel |

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
| 切主题后二级菜单仍旧配色 | code-behind new 出的行是本地值 | 订阅 `ThemeChanged` 后重建；优先 Classes+样式 |
| 点刷新没反应 | 只扫目录没重建菜单；或容器已 build 无法注册服务 | 加载后重建两侧菜单；页面型模块可不注册服务（6.6） |
| CS0104「Xxx」不明确引用 | 应用侧类型与所用库同名（自己定义了 `MenuItem`，与 `Avalonia.Controls.MenuItem` 撞） | 给应用侧类型换名（`AppMenuItem`）；⛔ 别用 `using X = ...` 别名掩盖，改名才是根治 |
| NETSDK1022 重复项 | `defaults\**` 用了 `None Include`（已含在 SDK 默认通配里） | 改 `None Update`，只调 `CopyToOutputDirectory` |
| 软件包列表页空白（0 行） | winget 表头**本地化**（中文 Windows 打 `名称/ID/版本/可用`），解析器只认英文 → 找不到表头 | 列标签按「英文+中文别名」映射到规范名再切片 |
| 已安装列表少一大截 | `IdLike` 正则不认 `\`，`ARP\…` / `MSIX\…` 条目被整批丢掉 | 正则加 `\\`（`^[A-Za-z0-9][A-Za-z0-9._+\\-]*$`） |
| `winget show` 的「发布者」里带了 URL | `发布服务器 URL:` 被当成上一条多行值的续行 | 把不认识的 `标签:` 显式**终结**上一个字段并丢弃 |
