# NewToys 开发规范

> v3.10 · 2026-10-11 · 技术实现指南
> 权威源：需求（做什么 + 硬性框架约束）；**本文件只做「怎么具体实现」**——编码模式/像素/类名/API 签名/易错点。
> 约束分两类：〔A〕硬约束（引需求）｜〔B〕折衷约束（本文件台账）
> **两条铁律**：① 代码是唯一真相源（文档与 `src/` 冲突以 `src/` 为准并回写）；② 本文件是唯一实现约定源。
>
> **v3.0 变更**：随 UI 重构（仿浏览器标签页 + 树状侧栏 + 视觉规范）同步重写实现细节；删除旧三栏「60,140,*」、`ColumnSpan` 补丁、第一列 ⚙/🏠 常驻等已废弃规则；新增图标、标签栏、树、`IDetailPanelProvider`/`IHistoryProvider` 契约实现细则。
> **v3.1 变更（范围收敛）**：与需求 v3.1 对齐——本轮只做「Shell 外壳 + 全站视觉基线 + 列表页详情上提」，**图标矢量化延后**（§2.3 本轮沿用 emoji）、**搜索框不实装**、**历史栈只定义契约**（前进/后退灰显）；修正 B4、与代码事实对齐。
> **v3.2 变更**：**列表页详情上提本轮暂缓**（新增 B13）；更正"5 个列表页"为实际 **12 个**（§3.6）；§3.8 补"下一版迁移方式"。
> **v3.3 变更**：标签栏并入标题栏（§2.1 尺寸 / §2.2 配色改 `Sider*`）；一级菜单矢量图标落地（§2.3、§3.2 的 `icon` 语义实际启用）；补 `TryFindResource` 的 Try 模式坑（附录 A）。
> **v3.4 变更**：标题栏**底色改回主题背景**（`ContentBrush`）→ 标题栏/标签项配色**从 `Sider*` 系切回主题色系**（§2.2，成套改）；标签项去图标、`✕` 悬停显示；左树一级/二级样式区分 + 一级默认折叠 + 二级激活联动父级高亮（§2.1 / §2.2）。
> **v3.5 变更**：删标题栏前进/后退；标签栏两侧固定拖拽空白；左树**手风琴**（展开态只展开当前一级）+ 折叠态一级显示主题色 + 折叠即全收（§2.2）；新增**多配置**（`profiles\<名>\{config,data,logs}` + `active-profile.txt`，切换 = 重启）（§4）。
> **v3.6 变更**：**撤销手风琴**（切标签只确保当前一级展开、不收起其它；展开态恢复落盘）（§4.1）；标签栏两侧留白 16→24px、**切换后最小滚动让当前标签可见**、**关闭按钮不占位**（叠在文字上、悬停才现）（§2.1 / §2.2）。
> **v3.7 变更**：紧凑化（标签项 10/4、标签字号 13px）+ **左树 240→180px**（§2.1）；列表页**三列布局**（工具条收进列表列、详情卡抬到顶）先在 `video-list` / `pc-package` 两个样板页落地（§3.6）；视频工具重构（删转码页 / 加批量处理页 / 剪辑改命令生成页）**只写需求、代码下一批**。
> **v3.8 变更**：视频工具三页重构落地 —— 「格式转码」**改名为**「批量处理」（菜单 key `video-batch`；列表用 `VideoTaskBase` **合并**剪辑 + 转码；运行按类型分派 `BuildCutArgs` / `BuildTranscodeArgs`）；「视频剪辑」改「命令生成」（去列表、单任务编辑卡）。⚠️ 原 `TranscodeView.axaml` / `.axaml.cs` / `TranscodeViewModel.cs` 三个文件**改名**为 `VideoBatch*`（等同删旧名、建新名）。
> **v3.9 变更**：**底部状态栏**（Shell 承载，横贯内容区 + 详情区）+ `IStatusProvider` 契约（`Shared/Services/`；`ListPageViewModelBase` 统一实现，覆盖 12 个列表页；**各页删除顶部状态行**）；**左树底部版本号区**（占位 `1.26.10`，折叠态隐藏）；**收藏夹按钮**改「透明底 + 强调色描边，悬停实心」（§2.2 / §2.1 样式类）。
> **v3.10 变更**：视频工具「命令生成」页再设计（需求 §6.4.1）—— 新增实现细则 **§3.9**：类播放器预览 + 三条时间条的**布局与滑条同步**、**详情卡（任务类型多选 + 可折叠剪辑/转码设置区 + 可编辑命令）**、**命令生成纯函数库**（分辨率吸附 / 码率建议 / 编码器映射）。⚠️ **只写需求与规范，代码下一批。**

---

## §0 文档体系与更新纪律

**四份文档**（不得删除/移动/改名）：
- `NewToys开发需求.md`（宪法）：定位/边界/**技术框架/UI 框架/导航模型/功能域**。极少改。
- `NewToys开发规范.md`（本文件）：编码模式/实现细节/折衷台账/高频错误。
- `NewToys-项目现状与进度.md`（台账）：当前基线/已完成/待办。
- `NewToys-AI协作提示词.md`：会话启动包。

**更新节奏**：

| 事件 | 需求 | 规范 | 台账 | 提示词 |
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

### 2.1 具体尺寸与样式（需求 §4.9 的实现基线）

**主窗口**：`1280×800`，最小 `960×640`。
**全局布局**：**标题栏 40px（标签栏并入其中段，v3.3）**、左树 **180px**（折叠 48px）、右详情 340px。

> **v3.4 落地**：本节尺寸为实施基线；图标见 §2.3（一级菜单已矢量）。

| 元素 | 尺寸 |
|---|---|
| 卡片 `Border.card` | 圆角 8px、内边距 14px |
| 按钮 `primary`/`ghost` | 圆角 6px |
| 输入框 | 圆角 4px |
| 标签页项 | 圆角 4px、内边距 水平 **10px** / 垂直 **4px**；**只有标题**（无图标）、**文字 13px**（v3.7） |
| 左树展开宽 | **180px**（折叠 48px）（v3.7 由 240 收窄） |
| 标签关闭按钮 | 22×22、**叠在标题上层贴右端（不参与布局宽度 → 标签只按文字算宽）**；默认 `Opacity=0` + `IsHitTestVisible=False`，`Border.tab-item:pointerover` 时才显形（v3.6） |
| 树节点行高 | 32px；缩进每级 16px |
| 内容区外边距 | `Margin="20,16"` |
| 表格行高 | 32px |
| 标题栏按钮 | 高 32px、**图标 18px**（v3.4 由 16 放大）；窗口控制按钮沿用 46×40 |

**全局样式位置**：`Shell/App.axaml`（+ 复用样式表 `Shell/Styles/Controls.axaml`）。当前样式类：
`Button.title-btn`（标题栏工具按钮）、`Border.tab-item`/`.active`（标签项）、`Button.tab-btn`（标签）、`Button.tab-close`（标签关闭）、`Button.tree-row`/`.active`/`.sub`（左树行）、`Border.tree-panel.collapsed`（左树折叠态）、**`Border.tree-version`（左树底部版本号，v3.9）**、`Button.win-btn`/`.close`（窗口控制）、**`Border.status-bar` + `TextBlock.status-bar-text`（Shell 底部状态栏，v3.9）**、`Button.primary`/`.ghost`/`.fav-btn`、`Border.card`、`Button.col-header`、`Border.list-header`、`TextBlock.list-cell`、`ListBox.list-page`。

> ⚠️ **已废弃（v3.3）**：`Button.nav-btn`（旧第一列图标）、`TreeView.tree-nav`。左树现在用 `ItemsControl` + `Button.tree-row` 自绘，**不用 `TreeView`**。

- ⛔ 页面里不复制同名样式；本页专用样式写 `UserControl.Styles` 里加更具体选择器。
- ⛔ 除标题栏图标外所有颜色绑 `DynamicResource`。

### 2.2 主题键（32 个，⛔ 不新增）

主题资源键一律 `*Brush` 语义键，共 **32 个**（`ContentBrush/PanelBrush/SubSiderBrush/SiderBrush/PrimaryTextBrush/SecondaryTextBrush/AccentBrush/AccentHoverBrush/DangerBrush/DividerBrush/InputBorderBrush…`，完整清单以源码为准）。

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

> ⚠️ **v3.4 起标题栏底色跟随主题（`ContentBrush`）** → 标题栏上的文字 / 图标必须用 `PrimaryTextBrush` 等**主题色**，
> ⛔ **不要再用 `Sider*` 系**（那是深色侧栏专用；浅色主题下标题栏是浅底，白字会看不见）。
> 反之，若以后又把标题栏改回深色，**必须整体切回 `Sider*` 系**——**标题栏底色与文字配色要成套同步改**。

- 新增 Light/Dark 两套必须**同时补齐**；引用不存在键**编译不报错、运行期才失败**。
- 废弃旧键：`AppBackground/TitleBarBackground/TextPrimary/TextSecondary/BorderColor/CardBackground`（长键）、`AppBg/TitleBarBg/Text1/Text2/CardBg/PanelBg/SidebarBg/HoverBg/Border`（短键）。

### 2.3 图标（v3.3：一级菜单已矢量，其余仍 emoji）

> **当前状态**：**一级菜单 / 首页 = 矢量**（`Icon*` Geometry，描边型）；**二级菜单不显示图标**；⛔ 标题栏工具按钮与标签页**仍为 emoji**（待下一版）。

- **矢量图标定义**：`Shell/App.axaml` 的 `<Application.Resources>` 里，`<StreamGeometry x:Key="IconXxx">路径数据</StreamGeometry>`（24×24 网格、**描边型**）。
- **渲染**：`<Path Data="{Binding VectorIcon}" Stroke="{DynamicResource SecondaryTextBrush}" StrokeThickness="1.5" Stretch="Uniform" Width="16" Height="16"/>` —— ⚠️ 描边型**不要**用填充型的 `PathIcon`。
- **键名规范**：`Icon<用途>`。已定义 `IconHome`/`IconPc`/`IconWeb`/`IconVideo`/`IconSettings`；下一版再补 `IconBack`/`IconForward`/`IconRefresh`/`IconSearch`/`IconMenu`/`IconClose`/`IconPackage`/`IconFavorite` 等。
- **取不到键要回退**：VM 解析失败返回 `null` → XAML 用 `IsVisible="{Binding HasVectorIcon}"` 在「矢量 / emoji」两种图标间切换（⛔ 不能让未知键显示成一串 `IconXxx` 文本）。
- 尺寸基线：**一级菜单 / 首页 16px**；（下一版）标题栏 16px ｜ 树节点 14px ｜ 标签页 14px ｜ 标签关闭 10px。
- 颜色：由 `Foreground` 继承，⛔ 不写死色值。
- **来源**：从公开线性图标集（如 Lucide / Feather，MIT 许可）取路径数据，**只取路径数据不引依赖**。风格统一（线性、同笔画粗细）。

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
- `desc`（功能简介）务必填写。
- 菜单点击路径：`ShellViewModel.OpenTab(key)` → 新建/激活 `TabItemViewModel` → View 解析 → 写入 Tab 的 `Content`。

### 3.3 模块加载（启动双轨）

`App.OnFrameworkInitializationCompleted()` 顺序（不可颠倒）：

1. 建 `ServiceCollection` 注册核心服务；
2. `ModuleLoader.LoadAll(services, GetDebugModuleTypes())`：Debug 用 `#if DEBUG` 显式清单；Release（传 null）扫 `<程序目录>\Modules\`，跳过已加载同名；
3. `BuildServiceProvider()` → `AppServices.Initialize(sp)`；
4. `ModuleLoader.InitializeAll(sp)`（**必须在 3 之后**）；
5. 应用主题（订阅 `ThemeChanged`）；
6. `new ShellView{DataContext=ShellViewModel}→win.Show()`；
7. 首导由 `ShellViewModel` 构造函数末尾 `OpenTab("home")` 触发。

| | Debug | Release |
|---|---|---|
| 模块源 | `Shell.csproj` Debug 条件 `ProjectReference` | `<程序目录>\Modules\` DLL |
| 需要 `Modules\` | 否 | 是（无则兜底程序目录） |
| MSBuild 拷贝 | 否 | 否（OutDir 直出） |

**硬规则**：
- Debug 必须用显式类型清单（⛔ 不能只靠 `AppDomain.GetAssemblies()`）。
- 跳过某模块改两处：`App.axaml.cs` 的 `typeof(XxxModule)` + `Shell.csproj` 的 `ProjectReference`。
- Release 路径用 `AppContext.BaseDirectory` 拼绝对路径。
- 模块 DLL 命名 `Module.<模块名>.dll`。
- ⛔ **`Modules\` 是模块产物唯一去处，全程零拷贝**：`src/Directory.Build.props` 把模块 Release `OutDir` 直设为 `$(NewToysReleaseDir)Modules\`。
- Release 关 pdb（`DebugType=none`）消除并发写 `MSB3026`。

### 3.4 新增模块步骤

1. 建 `src/Module.Xxx/Module.Xxx.csproj`：`RootNamespace=NewToys.<Xxx>`、`AvaloniaUseCompiledBindingsByDefault=false`，引 `Shared+Core`。
2. 写 `XxxModule.cs : IModule`，命名空间 `NewToys.<Xxx>`。
3. `src/NewToys.slnx` 加 `<Project Path="Module.Xxx/Module.Xxx.csproj" Type="C#" />`。
4. **Debug 链路两处登记**：`Shell.csproj` Debug 条件 `ProjectReference` + `App.axaml.cs` 的 `#if DEBUG` 清单。Release 不需登记。
5. `Initialize` 里 `RegisterModule`+`RegisterFunction` 注册菜单，`viewName`=View 类名，`icon`=Geometry 键。

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

> 需求 §4.7：列表页内部**不再自绘 340px 右列**，改由 Shell 右详情面板承载 —— **⏸ 本轮暂缓**，实际涉及 **12 页**（下一版迁移）。本节以下为**目标形态**；本轮各页维持页内右列，不迁移。

**三列布局（v3.7 落地版）**：详情仍留在页内，那就把它做成「**列表列 ｜ 详情列**」+ 左树 = 三列：
- 页面根 Grid **保持单行**；把原来的 `Row0~Row2`（状态行 / 工具条 / 筛选条）**整体挪进左列** —— 左列自己再分 `RowDefinitions="Auto,Auto,Auto,*"`；
- 详情卡 `Grid.Column="1"` **垂直拉伸**（`VerticalAlignment="Stretch"`），从页面顶到底；
- ⛔ 工具条**不再横贯整页宽**；⛔ 不要改共享样式 `Border.detail-card`（宽度 / 内边距仍由它给）；
- ✅ 已改 **10 页**（样板 `video-list` / `pc-package` + `pc-shortcuts` / `pc-quicksettings` / `pc-info` / `web-bookmark` / `web-ping` / `web-scrape` / `web-hosts` / `settings-path`）；⏳ `video-cut` / `video-transcode` 待视频工具重构后再定。

**列表页内的硬规则（保留）**：
1. 表头与行模板**完全相同的星号列定义**（逐字一致）；动态列用 `<v:ColumnGrid ColumnSpecs="{Binding VisibleColumnSpecs}">`。
2. 表头与行单元格左右内边距一致（12px），行里不写 `Padding/Margin`。
3. `ListBoxItem` 拉伸（已在 App.axaml 设 `HorizontalAlignment=Stretch`），页面不覆盖。
4. 表头双交互：① 左键点→切换排序（`SortCommand`+CommandParameter，VM 追加▲▼）；② 右键点→`ContextMenu` 勾选列显隐（`ToggleType=CheckBox`，`IsChecked` 显式 `Mode=OneWay`）。
5. 空态与选中态显式处理：无选中显示引导；有选中显示详情（由 Shell 右列承载）；空列表用居中 StatusText，不留白。
6. `ContextMenu` 挂在独立弹出树，不保证继承宿主 DataContext → 必须在 View 的 code-behind `OnDataContextChanged` 里手工同步（行菜单+表头菜单都要）。
7. 列显隐：元数据 `Shared/Models/ListColumn`、服务 `IColumnLayoutService/ColumnLayoutService`（落 `config/columnlayout.json`）。隐藏列用宽度 0（不删列保一致）。

**v3.0 列表页右列改动**：

- 页面 `ColumnDefinitions` 简化为**单列（`*`）**；列表铺满内容区。
- **详情内容抽成独立 `UserControl`**，放 `Views/Detail/<Xxx>DetailView.axaml`（模块内），由 VM 通过 `IDetailPanelProvider.DetailContent` 暴露。
- **VM 实现 `IDetailPanelProvider`**（契约见 §3.8）。
- **表头 `▤` 按钮**：绑 `ToggleDetailCommand` → 调 `IDetailPanelProvider.ToggleDetail()`。
- **右键菜单首项「▤ 显示详情栏」**：调 `IDetailPanelProvider.OpenDetail()`。
- **详情开合状态**：由 Shell 统一落 `config/columnlayout.json`（`FlagDetailPanel`，**按页面 Key 分别记忆**），列表页 VM 不再直接读写。
- 涉及页面（5 个）：`pc-package` / `video-list` / `pc-shortcuts` / `pc-quicksettings` / `settings-path`。

- ⚠️ `Views/ColumnGrid.cs` 因**模块间禁互引**而在 `Module.PcTools` / `Module.VideoTools` / `Module.Settings` **各存一份，改动必须同步三份**。

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

### 3.8 UI 契约（v3.0 新增；v3.1 契约落地，v3.2 页面迁移暂缓）

> ⚠️ **本轮（v3.2）**：契约与 Shell 消费逻辑**已实装**；**页面迁移暂缓**。
> **下一版迁移方式**：让 `ListPageViewModelBase` 实现 `IDetailPanelProvider`（`HasDetailPanel` 默认 `false`，迁移页 `override` 为 `true`），并移除基类里的 detail flag 读写（改由 Shell 统一 `LoadFlag/SaveFlag`）——**改基类一处即可覆盖全部 12 页**；每页再把右列 XAML 抽成 `Views/Detail/<Xxx>DetailView.axaml` 即可。

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

### 3.9 视频工具「命令生成」页实现细则（v3.10，需求 §6.4.1）

> ⚠️ **只写规范，代码下一批**（与需求 v3.10 同步）。本节记**目标形态**；现状仍是"左预览 + 右参数卡 400px"。

**布局（页内两列 + 左树 = 三列）**
- 页根 Grid：`ColumnDefinitions="*,Auto"`；左列 = **预览列**（类播放器：大画面 + 三条时间条），右列 = **详情卡** `Border.detail-card`（340px、`VerticalAlignment="Stretch"`、可开合）。
- 顶部工具条**只留三项**：`📂 选择视频` / `➡ 发送到批处理` / `↺ 区间重置`；**删掉**原来的「剪辑 / 转码」单选。
- VM 仍**不继承 `ListPageViewModelBase`**（不是列表页）→ `ObservableObject, IStatusProvider`；**预留** `IDetailPanelProvider`（`HasDetailPanel` 暂 `false` → 先用**页内右列**；待列表页统一上提时，再随大流迁到 Shell 右栏）。

**三条时间条（预览位置 / 开始 / 结束）**
- ⛔ **滑条别直接绑模型属性** —— 一律走 VM 的**手动属性**（`PreviewTime` / `StartSeconds` / `EndSeconds`），setter 里改一处 → 时间码文本 + 大画面 + 命令预览**一起刷新**（沿用现有 VM 写法，避免"拖了滑条、时间码没跟上 → 保存被旧值覆盖"）。
- `Maximum` 绑 `TimelineMax`（时长未知时给**占位 1**）：⛔ 别在 `Maximum==Minimum==0` 时把 `Maximum` 直绑 `Duration` → `(Value-Min)/(Max-Min)=0/0=NaN` → **整页布局算不过去**（附录 A 有案）。
- 大画面 `Image Source="{Binding PreviewImage}"`（`Bitmap`），抽帧走 `FfmpegService.ExtractFrameAsync`；拖滑条用**"最新值优先"节流**（复用现有 `_extracting` + `_pendingPreview` 模式）。⛔ **不再铺 `ItemsControl` 缩略图墙**（删 `FrameThumb` 那一条）—— 只保留"当前帧"一张大图。

**详情卡（任务类型 + 可折叠设置区 + 可编辑命令）**
- **任务类型 = 两个独立 bool**（`IsCut` / `IsTranscode`，勾选框），**不是单选组**（允许既剪又转）；两者皆 false 时给提示并**禁止发送**。
- 两套设置区各用一个 `Expander`：`IsExpanded` 绑 VM 的 `IsCutSectionExpanded` / `IsTranscodeSectionExpanded`；**勾选联动**——取消勾转码 → 折叠转码区，只勾转码 → 折叠剪辑区。折叠只影响界面，**是否执行由勾选决定**。
- 转码参数补足四组：**格式/容器**、**编码器(CPU·GPU)**、**分辨率**、**码率**（CRF 或目标码率）。
- **命令可编辑**：VM 里把「参数生成值」与「可编辑文本」**分成两个属性**——`CommandPreview`（只读合成，参数变即刷新）与 `EditableCommand`（可写）+ `IsCommandManuallyEdited` 标记。手改后**参数变化不得覆盖** `EditableCommand`；`↺ 重置为自动` = `EditableCommand = CommandPreview` 并清标记。⛔ 手改后的命令必须**原样执行**。
- 从「视频列表」送来 → 载入后**自动触发一次预览**（探测时长 + 抽当前/首帧），不再让用户手点「抽帧」。

**命令生成纯函数（`Services/CommandRules.cs`，static + 纯函数 + 不碰 UI）**
- `SnapResolution(w, h)`：**默认"保比例、限长边"（不畸变）**；另给"吸附标准档"（2160/1440/1080/720/480/360）。例：2100×1500 两轴均 > 1920×1080 → 吸附 **1920×1080**。
- `SuggestBitRate(w, h, fps, quality)`：按分辨率 × 帧率 × 质量系数给目标码率建议。
- `PickEncoder(useGpu, codec)`：CPU/GPU 编码器映射（`libx264`↔`h264_nvenc`、`libx265`↔`hevc_nvenc` …）。
- ⛔ 规则表**不写死在 XAML**；⛔ 不引第三方库。多段剪辑（需求 §6.4.2）**一期只做"多段 = 多条任务"**，`CutTask` 结构不动。

---

## §4 配置与路径实现

- 一律 `Path.Combine`，⛔ 硬编码 `\` 或 `/`。
- **用户数据**由 `ConfigStoreService` 决定（不在仓库内）：应用根 `%LocalAppData%\NewToys`（环境变量 **`NEWTOYS_HOME`** 整根切换）。
  - **v3.5 起**：应用根之下再分一层「配置」—— `<应用根>\profiles\<配置名>\` → 其下才是 `config/data/logs`；当前配置名记在 **`<应用根>\active-profile.txt`**（在 `profiles\` 之外，否则切换会把自己也换掉）。
  - `ConfigStoreService` 在构造时：① 确保 `profiles\<active>\` 存在（首次还要跑旧结构迁移）；② 暴露 `ConfigDir` / `DataDirectory` / `LogsDir` 指向该配置；③ `ProfilesRoot` / `ActiveProfileName` 供「设置 → 配置」使用。
  - ⛔ 模块不自己拼路径，向 `IAppConfigService.DataDirectory`/`ConfigStoreService` 取值。
- **配置切换**：`IProfileService`（`Core`）负责列出 / 新增 / 重命名 / 删除 / 切换；**切换只写 `active-profile.txt`，然后重启进程**（`Process.Start(Environment.ProcessPath)` + 退出）。⛔ 不做运行时热切换。
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

## §7 折衷约束台账（B 类）

> 当前做法为「需求 vs 工程现实」妥协，非永久铁律。**变更条件满足+用户同意+本表登记**后可调；未登记不得擅改。

| 项 | 理想 | 当前折衷 | 变更条件 | 影响面 |
|---|---|---|---|---|
| **B1 编译绑定** | 开启编译绑定，性能更好 | 全工程关闭，不写 `x:DataType` | 用户拍板统一开启并补 `x:DataType` | 全部 View |
| **B2 VM 基类** | 统一继承 `Shared/ViewModelBase` | 模块内多直接继承 `ObservableObject`，仅 Shell/Home 用 `ViewModelBase` | 某模块改造时逐模块统一 | 各模块 VM |
| **B3 全局样式** | 显式 `x:Key` 样式键 | 用 `Classes` 隐式样式 | 收益足够大时整体改造 | `App.axaml` |
| **B4 导航服务** | 导航统一走 `INavigationService` | 主流程（侧栏 / 菜单点选）由 `ShellViewModel` 自解析；`NavigationService` 仅被**模块内跳转**（功能清单页、模块首页）经 `Navigated` 事件复用，**未成为统一入口** | 删闲置服务或改用它（属框架层需同意） | Shell/Core |
| **B5 平台后端** | 完全跨平台 | 后端仍调 winget/ffmpeg 命令行，类型已中立为 `Package*` | 迁 Debian 时替换 | Core 服务 |
| **B6 仓库根 slnx** | 唯一入口 `src/NewToys.slnx` | 仓库根残留旧 `NewToys.slnx` 副本 | 直接删除即可 | 仓库 |
| **B7 Modules 冗余 DLL** | `Modules\` 仅含模块 DLL | 会多出 `NewToys.Shared.dll`/`NewToys.Core.dll` | 无害暂留 | Release 产物 |
| **B8 闲置转换器** | 不留无用文件 | `Shell/Converters/BoolInversionConverter` 未被引用 | 保留或删除均可 | Shell |
| **B9 骨架页面** | 全部页面落地真实逻辑 | 仅剩 `web-http` 一个骨架 | 按节奏实装 | Module.WebTools |
| **B10 运行期热加载** | 任意模块随时加载/卸载/替换，服务随模块可扩 | 仅支持「新增未加载过的模块」；同名不卸载不替换；容器已 build，模块不能注册新服务 | 引入可卸载 ALC+模块级容器后升级 | ModuleLoader+Shell |
| **B11 反射使用** | 最小魔法、少反射 | 模块装配/热加载/视图解析三处用反射枚举 IModule 与 View/VM | 机制必需暂不改；⛔ 仅限这三处 | ModuleLoader/ShellViewModel |
| **B12 前进/后退**（v3.0 新增） | 所有页面都有丰富页内导航 | 现状仅 `video-list` 有目录语义；多数标签页前进/后退**灰显** | 各页逐步实现 `IHistoryProvider` 后自然改善 | 各模块页面 |
| **B13 详情面板上提**（v3.2 新增） | 详情由 Shell 右面板统一承载 | 契约与 Shell 容器已实装，但**页面迁移暂缓**——12 个页面仍用页内自绘右列 | 下一版迁移（改 `ListPageViewModelBase` 一处 + 每页抽 DetailView） | Shared + 各模块页面 |

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