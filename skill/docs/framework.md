# NewToys 框架层（Framework）

> v4.2 · 2026-10-18 · **标准角色：工程规范（框架层部分）**
> v4.2：新增 **§6.6 `IFileOperationService`** —— 跨模块磁盘动作（改名/移动/复制/回收站删除）下沉 `Core`，两处 `SHFileOperationW` 合一（v3.66 / NT-069）。
> v4.1：新增 **§4.6 应用级设置 / 首页 / 功能清单** —— 三者属 `src/Shell`（`Module.Settings` 已于 NT-062 撤销），原 `modules/settings-app.md` 内容并入本节并删除该文件；`modules/` 只剩 4 个真模块。
> 本文件 = **框架层（Shared / Core / Shell / NewToys.UI）的实现约定**，外加**跨模块待办**的唯一收口处。
> 原 `NewToys-工程规范.md` 已于 2026-10-18 拆分：**框架层 → 本文件**｜**UI 规范 → `ui.md`**｜**各模块 → `modules/<模块>.md`**。
> 权威源 = `src/` 源码；代码与文档冲突以源码为准并回写本文。
> 流程与术语唯一定义处 = `NewToys-AI协作手册.md`【研发流程】【术语对照】；本文件只写「怎么实现」。

## 目录

- §0 文档体系与更新纪律
- §1 编码规范（C#）
- §2 程序集与依赖边界
- §3 启动顺序与模块加载
- §4 菜单注册与导航（含 **§4.6 应用级设置 / 首页 / 功能清单**）
- §5 页面解析与 VM 定位
- §6 契约（Shared 侧接口）
- §7 配置与路径
- §8 异常处理
- §9 构建与版本
- §10 技术债台账
- §11 跨模块待办
- 附录 A 高频编译错误速查

---

## §0 文档体系与更新纪律

### 0.1 文档地图（2026-10-18 重构）

| 文档 | 角色 | 内容 | 何时读 |
|---|---|---|---|
| `index.md` | 文档地图 | 模块清单表 + 全部文档索引 | **会话开始** |
| `NewToys-需求规格.md` | 需求规格（宪法） | 定位 / 边界 / 技术框架 / UI 框架 / 导航模型 / 功能域 | 涉及功能与边界 |
| **本文件 `framework.md`** | 工程规范 · 框架层 | 程序集边界 / 启动 / 菜单 / 加载 / 契约 / 配置 / 构建 / 技术债 / **跨模块待办** | 改 Shared/Core/Shell 或新建模块 |
| **`ui.md`** | 工程规范 · UI | 尺寸密度 / 主题键 / 图标 / CSD / 列表页 / 详情卡 / 工具条 | 改任何 `.axaml` |
| **`modules/<模块>.md`** | 模块说明 | 该模块的职责 / 当前功能 / **操作逻辑（§2.5）** / 实现逻辑 / 数据 / 坑点 / 待办 | **改哪个模块读哪个** |
| `NewToys-项目状态与路线图.md` | 工作项台账 | 基线 / 已完成 / Backlog 索引 | 会话开始读「一、当前基线」 |
| `backlog/<模块>/NT-xxx.md` | 任务卡 | 实施期工作稿 | 只有实施该卡才读 |
| `NewToys-AI协作手册.md` | 协作手册 | 研发流程 + 术语对照（🔴 唯一源） | 流程/术语有疑问 |
| `NewToys-软件说明书.md` | **使用层**（用户手册） | 用户视角：每页干什么 / 谁会用 / 典型场景 / 数据在哪（⛔ 无实现细节） | 问「怎么用」、写用法说明 |

**拆分原则**：一个功能模块一个 md；新增模块 = 新增 `modules/<模块>.md` + `backlog/<模块>/`。

### 0.2 更新节奏

| 事件 | 需求 | framework | ui | modules/<模块> | 台账 | 手册 |
|---|---|---|---|---|---|---|
| 定位 / 边界 / 框架级变化 | ✅改 | ✅改 | 视情 | 视情 | 同步 | 视情 |
| 实现方式变化 | 引用 | ✅改 | ✅改 | ✅改 | 同步 | — |
| **完成一个功能** | — | 视情 | 视情 | ✅**改**（功能/逻辑/坑） | ✅**更新** | — |
| 仅修报错 / 错别字 | — | ⛔ | ⛔ | ⛔ | ⛔ 不回写 | — |
| 协作流程变化 | — | — | — | — | — | ✅改 |

- **各文档版本号相互独立**，只有内容真变了才自增，⛔ 不做齐步递增。
- 🔒 **指导性文件不乱改**：仅为单个任务的措辞微调不擅改。
- **提交信息** `<type>: <简述>`，type ∈ {feat, fix, docs, refactor, chore, style}。
- ⛔ **评估 / 讨论结论不进文档**，只在会话里谈，用户拍板后才转为需求。

### 0.3 任务卡生命周期与滑动窗口（2026-10-18 修订）

- 卡放 `backlog/<模块>/`（跨模块卡放 `backlog/framework/`），**每目录最多 5 张**。
- **淘汰规则**：新增卡使目录超 5 张时，删除**最旧且已验收**的那张；`📝草稿 / ✅就绪 / 🔄执行中 / ✔已完成（待验收）` 的卡**不参与淘汰**。
- **验收即蒸馏**：卡验收后，把它的「结论 / 偏差 / 坑点 / 关键文件」写进 `modules/<模块>.md` 对应节，卡才可被淘汰。
- 被后续工作项整体取代 / 明确作废的卡 → 直接删除，台账行标 `🗑 已作废（卡已删）`。
- 台账行**始终保留**（只翻状态）。
- ⛔ 已废止：2026-10-14「全量保留」、2026-10-17「默认保留 + 作废删除」（被本条的滑动窗口取代）。

---

## §1 编码规范（C#）

- 文件作用域命名空间（`namespace X;`）；`using` 置顶，`System.*` 优先。
- 命名：公共成员 / 类型 / 方法 `PascalCase`；私有字段 / 局部 / 参数 `camelCase`（私有字段前缀 `_`）；异步方法 `Async` 结尾。
- 带 `[ObservableProperty]` / `[RelayCommand]` 的类**必须 `partial`**（否则 `CS0260`）。
- 属性优先 `[ObservableProperty]` 私有字段；命令 `[RelayCommand]`。
- 构造注入为主；`Ioc.Default` 仅在 code-behind / 回调等无法构造注入处使用。
- 集合 `ObservableCollection<T>`；公共 API 写 XML 注释；复杂逻辑注释「为什么」。
- ⚠️ 改 `[ObservableProperty]` 字段名 / 语义时全仓搜旧名（属性名 + `_字段名` + `nameof`）；构造里 `_xxx=…` 合法，别顺手修。
- `NewToys.Shared` / `NewToys.Core` **⛔ 不引 Avalonia**；模块 ⛔ 禁引 Shell、禁互引。

---

## §2 程序集与依赖边界

| 程序集 | 职责 | 依赖 |
|---|---|---|
| `NewToys.Shared` | 契约接口（`Services/`）/ 模型 POCO / 详情卡描述符 | 无（零 Avalonia） |
| `NewToys.Core` | 服务实现（配置 / 主题 / 菜单 / 加载 / 列布局 / winget / ffmpeg 封装） | Shared |
| `NewToys.UI` | **共享控件库**（NT-063） | Shared + Avalonia（⛔ 不引 Core / Shell） |
| `NewToys`（Shell，exe） | 启动 / DI / 主窗口 / 导航 / 应用级设置页 | Shared + Core + UI |
| `Module.<模块>` | 功能模块 | Shared + Core + UI（⛔ 不引 Shell） |

- 单向依赖：`Shared ← Core ← Shell ← Module.*`；`Shared ← UI`。
- **接口一律放 `Shared/Services/`**；POCO 放 `Shared/Models/`。
- 🔴 **`NewToys.UI` 纪律**：被所有模块引用 ⇒ 与 Shared/Core 同级 ⇒ **改一次 = 全模块重编**。只放**写完即冻结的稳定控件**；`App.axaml` 的 31 个 `*Brush` 主题刷子 ⛔ 不搬，UI 内 XAML 一律 `DynamicResource` 引用主题键。
- ⛔ 禁第三方 UI 库 / 图标字体。包版本只在 `src/Directory.Packages.props`。

---

## §3 启动顺序与模块加载

### 3.1 启动顺序（`App.OnFrameworkInitializationCompleted()`，不可颠倒）

1. 建 `ServiceCollection` 注册核心服务；
2. `ModuleLoader.LoadAll(services, GetMandatoryModuleTypes())`；
3. `BuildServiceProvider()` → `AppServices.Initialize(sp)`；
4. `ModuleLoader.InitializeAll(sp)`（**必须在 3 之后**）；
5. 应用主题（订阅 `ThemeChanged`）；
6. `new ShellView{DataContext=ShellViewModel} → win.Show()`；
7. 首导由 `ShellViewModel` 构造函数末尾 `OpenTab("home")` 触发。

### 3.2 双轨加载

| | Debug | Release |
|---|---|---|
| 模块源 | `Shell.csproj` Debug 条件 `ProjectReference` | `<程序目录>\Modules\` DLL |
| 必备模块 | **无**（v3.62 起应用级设置页归 Shell） | 无 |
| 需要 `Modules\` | 否 | 是（无则兜底程序目录） |

**硬规则**：
- Debug 的功能模块必须走显式类型清单（`App.axaml.cs` 的 `#if DEBUG`），⛔ 不能只靠 `AppDomain.GetAssemblies()`。
- 跳过一个功能模块要改两处：`App.axaml.cs` 的 `typeof(XxxModule)` + `Shell.csproj` 的 `ProjectReference`。
- Release 路径用 `AppContext.BaseDirectory` 拼绝对路径；模块 DLL 命名 `Module.<模块名>.dll`。
- ⛔ **`Modules\` 是模块产物唯一去处，全程零拷贝**（`Directory.Build.props` 把模块 Release `OutDir` 直设为 `$(NewToysReleaseDir)Modules\`）。
- Release 关 pdb（`DebugType=none`）消除并发写 `MSB3026`。
- **健全性检查**：`InitializeAll` 后若**宿主分组 `system`** 不存在（Shell 自身注册失败）→ 日志 + 状态栏一行警告，⛔ 不静默（常量 `ModuleLoader.HostGroupKey = "system"`）。

### 3.3 新增模块步骤

1. 建 `src/Module.Xxx/Module.Xxx.csproj`：`RootNamespace=NewToys.<Xxx>`、`AvaloniaUseCompiledBindingsByDefault=false`，引 `Shared + Core + UI`。
2. 写 `XxxModule.cs : IModule`，命名空间 `NewToys.<Xxx>`。
3. `src/NewToys.slnx` 加 `<Project Path="Module.Xxx/Module.Xxx.csproj" Type="C#" />`。
4. **Debug 链路两处登记**：`Shell.csproj` Debug 条件 `ProjectReference` + `App.axaml.cs` 的 `#if DEBUG` 清单。
5. `Initialize` 里 `RegisterModule` + `RegisterFunction`，`viewName` = View 类名，`icon` = Geometry 键。
6. **有设置页时**：View/VM 放**本模块** `Views/SettingsXxxView.axaml(+.cs)` + `ViewModels/SettingsXxxViewModel.cs`；`RegisterFunction(<本模块分组键>, "settings-<模块键>", …, Order)` 注册进**本模块的一级菜单**（⛔ 不跨组，Order 排本组末位）。设置落 `config\<模块>.json`，模块只读、唯一写入点 = 该设置页。🔴 菜单标题**保持长名**（如「脚本管理器设置」）—— 它是独立标签页标题。
7. **建 `modules/<模块>.md`**（照 7 节模板，含 §2.5 操作逻辑）+ `backlog/<模块>/` 目录。

```csharp
public interface IModule {
    void RegisterServices(IServiceCollection services);
    void Initialize(IServiceProvider services);   // 这里注册菜单
}
```

- 模块类命名 `XxxModule`，命名空间 `NewToys.<模块名>`（不带 `.Module` 段）。

### 3.4 热加载

- 目录只有一个：`Path.Combine(AppContext.BaseDirectory,"Modules")`；只认 `Module.<模块名>.dll`。
- ⛔ 跳过已加载同名程序集 → 本版**只做「新增」**（B10）。
- 每个待加载 DLL 用**独立 `AssemblyLoadContext`**（`isCollectible:true`）；`Shared` / `Core` 一律由**默认 ALC 共享**（ALC 的 `Load` 覆写里把契约程序集转交默认上下文），否则 `IModule` 强转必失败。
- ⛔ 已 `BuildServiceProvider()` 的容器无法追加服务 → 热加载模块 `RegisterServices` 可跳过；优先**页面型模块**。
- 加载后调 `IMenuService.RegisterModule/RegisterFunction`，通知 `ShellViewModel` 重建左树（`BuildTree()`）。
- **失败隔离（硬要求）**：单 DLL 异常 `try-catch` 兜住，只记日志 + 一行 StatusText；完成后报「本次新增 N / 跳过 M / 失败 K」。
- ⛔ 只读本地 `Modules\`；不联网；加载前 `Path.GetFullPath` 校验路径在 `Modules\` 内。
- **入口**：功能清单页（`settings-all`）「增加功能」区的「🔄 重载模块」→ `AllFunctionsViewModel.ReloadModulesCommand` → `IModuleReloadService.HotReload()`；`ShellViewModel` 订阅 `Reloaded` 重建左树（进程级单例，无需退订）。设置页 VM ⛔ 不直接调 `ModuleLoader`。

---

## §4 菜单注册与导航

### 4.1 API

```csharp
AppMenuGroup RegisterModule(string key, string title, string icon, int order,
                           bool isBottom=false, bool isHome=false);
void RegisterFunction(string moduleKey, string key, string title, string icon, string viewName,
                      int order=0, string desc="", SettingsCategory category=SettingsCategory.None);
```

- `category`（追加在参数表末尾、带默认值）：`通用｜一键按钮｜运行环境｜网络｜数据｜关于｜模块设置｜None`。取代「0~99 框架 / 100+ 模块」的 Order 段位隐式约定；`Order` **只作组内排序**。
- 先 `RegisterModule` 拿分组，再 `RegisterFunction`；⛔ 菜单只能在 `Initialize` 注册。
- `viewName` 传**视图类名**（如 `"ScriptLibraryView"`），不是 VM 名。
- 菜单 Key 规范：`<模块缩写>-<页面名>`，全小写连字符。🔴 **Key 一经发布 ⛔ 不改**（改 = 用户列布局 / 收藏 / 标签偏好全丢）。
- **`icon`**：一级分组传 **Geometry 资源键**（`"IconPc"` / `"IconWeb"` / `"IconVideo"` / `"IconMediaAssets"` / `"IconSettings"`）；二级功能仍传 emoji（左树不显示；首页收藏卡 / 功能清单页用）。
- `desc`（功能简介）务必填写。
- 点击路径：`ShellViewModel.OpenTab(key)` → 新建 / 激活 `TabItemViewModel` → `ResolveView` → 写入 Tab 的 `Content`。

### 4.2 分组现状

| 分组 | Key | Order | 图标 |
|---|---|---|---|
| 脚本管理器 | `script` | 20 | `IconPc` |
| 网络工具 | `web` | 30 | `IconWeb` |
| 视频工具 | `video` | 40 | `IconVideo` |
| 媒体资产 | `assets` | 50 | `IconMediaAssets` |
| 宿主分组（**不渲染**） | `system` | 999 | `IconSettings` |

- 宿主分组 `IsHome=true` ⇒ 左树 / 首页模块计数 / 功能清单页都按 `!IsHome` 过滤；只承载应用级设置的节锚点 + `settings-all` 寻址。
- **v3.66 起左树无「设置」一级菜单项**；模块设置项挂回**本模块分组末位**（`assets`=99、其余=100）。
- `isBottom` 保留但当前**无分组使用**。
- 排序：`OrderBy(Order)`；分组内 `Items` 按 `Order` 重排。
- 🔴 **跨组注册 + 延迟补登**：`RegisterFunction` 发现分组未注册时**暂存**（⛔ 不抛，`InitializeAll` 的逐模块 try-catch 会吞掉 → 菜单项静默丢失），待 `RegisterModule` 建立分组时补登（去重 + 按 Order 重排）。分组始终未建立 → 启动健全性检查报一行。v3.66 起模块设置页不再跨组 ⇒ 本机制现只作「分组 key 打错」的兜底。

### 4.3 标签栏机制

- 🔴 **首页常驻标签栏**（v3.69）：`TabItemViewModel` **无可见性开关**（`ShowInTabStrip` / `IsVisibleInStrip` 已删），首页与其余标签一视同仁显示，仅 `CanClose=false`；零标签兜底照旧回首页。
- 标签栏 = `Grid ColumnDefinitions="Auto,Auto"`：`Auto` 列 `ScrollViewer(TabsScroll)` 内 `ItemsControl` **直接绑 `Tabs`**（横排 `ItemsPanel` 必须显式写，否则默认竖排 + 竖滚禁用 = 只显示第一个）；`Auto` 列 `＋`（`NewTabButton`）**紧跟最后一个标签**。
- 🔴 **宽度类浏览器行为**：标签少 ⇒ 滚动区按内容量，`＋` 贴着最后一个标签；标签溢出 ⇒ code-behind `SyncTabStripWidthCap()` 把 `TabsScroll.MaxWidth` 收到「标签栏宽 − `＋` 宽 − 左边距」⇒ 滚动区收窄、`＋` 常驻最右且始终可见（挂 `TabStrip` / `TabsControl` / `NewTabButton` 三处 `SizeChanged`）。⛔ 别再改回 `*` 列（`＋` 会被推到整条最右、脱离标签）。
- `＋` = `OpenHomeTabCommand` → `OpenTab(HomeKey)`（⛔ 不指 `settings-all`）。
- 滚轮切标签 = `PrevTabCommand` / `NextTabCommand`（仅此处使用），**到头即止、不循环**（`Math.Clamp`，v3.69）。
- ⛔ 不做「固定其他标签页」（固定需求走首页收藏夹）⇒ 无通用「固定位」机制。
- 旧机制 `ScrollableTabs` / `HomeTab` / `SyncTabMirror` / `SyncScrollableTabs` **全部已删**。
- 标签项模板 = 共享资源 `TabItemTemplate`（`ShellView.axaml` 的 `Window.Resources`，⛔ 不复制两份防样式漂移）。
- View 实例由 `TabItemViewModel` 持有并缓存，切标签不重建；关标签时才销毁（VM 实现 `IDisposable` 则调用）。

### 4.4 左树行为

- 左树用 `ItemsControl` + `Button.tree-row` **自绘**，⛔ 不用 `TreeView`。
- 一级分组展开态落 `columnlayout.json` 的壳级分区（`treeGroup:<groupKey>`，默认 false）；折叠态 `treeCollapsed`。
- **展开整组 `BringIntoView`**（NT-037）：点**底部**分组的展开后，对该组**容器整块**做 `BringIntoView`（`Dispatcher.Post`，参考 `ScrollActiveTabIntoView` 模式），保证分组头 + 子项末都可见；**收起时不跳**；顶部 / 中部分组行为不变。
- 版本号 `TreeVersionText` 在左树底部版本行（`ShellView.axaml`）；设置齿轮也在此行。

### 4.5 托盘（NT-057）

- 图标 = **唯一图标工厂 `CreateAppIcon(accent)`**（窗口 + 托盘同源，随强调色）+ `Resources/app.ico`（exe 图标）⇒ ⛔ 不再代码画、⛔ 不写死强调色。
- 左键 = 切显示 / 隐藏；右键五项菜单：首页 · 设置 · 用另一个配置打开…（子菜单排除当前配置 / 无其他配置时置灰，先拉起窗口再导航）· 退出。
- ⚠️ 技术债：图标**形状两处定义**（C# 常量与 ICO 口径），改一处须同步另一处。

### 4.6 应用级设置页 / 首页 / 功能清单（Shell 页面，不是模块）

> 🔴 **定位**：应用级设置归**框架层**（`src/Shell`）。`Module.Settings` 已于 NT-062 撤销，⛔ 别再当模块写、⛔ 不建 `modules/settings-app.md`。
> 入口 = 左树底部**版本行齿轮** / `Ctrl+,` / 标题栏搜索 / 首页一键区 `⚙`（跳 `settings-launch`，归一到页 Key `settings`）。**左树无「设置」一级菜单**；模块专属设置在本模块分组末位（如 `settings-script`）。

**7 个节锚点**（`Shell/Settings/AppSettingsRegistration.cs` 注册进不渲染的 `system` 组）

| 节锚点 Key | 标题 | Order | 分类 | 内容 |
|---|---|---|---|---|
| `settings-general` | 启动与退出 | 0 | `General` | 开机启动 / 托盘 / 关闭按钮行为 |
| `settings-theme` | 主题外观 | 1 | `Theme` | 明暗模式 / 强调色 |
| `settings-launch` | 一键按钮 | 3 | `Launch` | 首页三个一键按钮的配置 |
| `settings-path` | 路径与配置 | 2 | `Environment` | 运行环境路径 + 多配置档（分类列区分行） |
| `settings-dns` | 加密 DNS | 4 | `Network` | 加密 DNS / 网络 |
| `settings-data` | 数据管理 | 6 | `Data` | 备份 / 导入 `config` |
| `settings-update` | **版本与更新** | 8 | `Update` | 版本号 / 版本文件地址 / 检查更新 / 功能清单入口 / 重载模块 |

- 🔴 **所有 Key ⛔ 不改**：首页跳转 / 收藏白名单 / 托盘「打开设置」都按 Key 寻址。
- 「通用」已拆成 `settings-general` / `-update` / `-theme` **三节共用一个 VM**。
- 🔴 **v3.67：「更新」+「关于」合并 = 「版本与更新」**（8 节 → 7 节，用户拍板）—— Key 用原来的 `settings-update`；
  `settings-about` 退成**别名**（`SettingsViewModel.ResolveKey` + `ShellViewModel.SettingsSectionKeys`，旧链接仍能打开，
  ⛔ 不再注册菜单、⛔ 不再是一个节点）。该节由 `UpdateAboutView`（**拼装壳**）装 `UpdateView` + `AboutSettingsView`，
  子 View 的 DataContext 由 `Init(...)` 分别赋（⛔ 靠继承只能给到一个）；节上有**两个 VM** ⇒ `SettingsSection.ViewModels`
  是列表，状态栏 / 工具条契约按列表逐个聚合。
- 另有 `settings-all`（功能清单页，Order 7，`AllFunctionsView`）。

**页面结构机制**（`SettingsView.axaml(.cs)`）

- **树状手风琴滚动 + 抬头磁吸浮层**：逻辑在 code-behind；🔴 类名 `sec-head` / `sec-body` 别删，`StuckHeader.DataContext=null` 别删（磁吸浮层 DataContext 隔离）。
- 🔴 **v3.68：磁吸浮层是 `ScrollViewer` 的兄弟覆盖层**（`PageRoot` 的第二个孩子），⛔ **不许放回滚动内容里**——
  放里面只能靠 `RenderTransform` 反补 `Offset` 把位置"推算"出来，实测出「滚到顶残留空白浮层 / 顶部抬头被裁一半 /
  抬头与正文错位」。覆盖层由布局自己定位（`VerticalAlignment=Top` + code-behind 按卡片算左右 Margin），零补偿数学；
  代价 = 浮层上的滚轮要转发给 `SectionScroll`（`PointerWheelChanged`，漏了就"鼠标停顶部滚不动"）。
- 节点抬头有**独立底色**（`Button.sec-head` → `ContentHeaderBrush` + 展开时下边框 `DividerBrush`）⇒ 抬头与正文一眼分开；
  磁吸浮层里的抬头另走 `Button.sec-head.stuck`（透明底，底色由浮层 Border 给）。
- 每节内含工具条（`HasToolbar` 门控，`ToolbarHost` 挂在节抬头下方）；⛔ **工具条不上抛页顶**；节 View **⛔ 带内层 `ScrollViewer`**（会出现双滚动条 + 磁吸失效）。
- 切节 ⇒ `ActiveSection` 变 ⇒ `SectionChanged` ⇒ Shell 重绑工具条 / 状态栏 / 详情卡（漏了就"画得出来、点不动"）。
- 用户**手动收起**的节记 `UserCollapsed`，滚动自动展开时跳过（用户意图优先）。
- `settings-path` 节正文用 `ListPageSkeleton` 且 `Margin="0"`。

**三层优先级（v3.67，改这块前必须遵守）**

| 层 | 触发 | 行为 |
|---|---|---|
| 1 | 用户点抬头 / 「全部收起·全部展开」 | 立即生效；单节收起写 `UserCollapsed`（滚动不再弹开）；「全部收起」写 `CollapsedByAll`（**临时总览态**，滚到哪节照样展开哪节，v3.68） |
| 2 | 外部寻址（首页 ⚙ / 收藏 / 搜索） | 目标展开 + 其余折叠 + **滚到目标贴视口顶**（`ScrollRequested`） |
| 3 | 滚动 | **只有"往下滚"（`Offset.Y` 变大）才自动展开**，且每轮只开视口上半部最靠上的那一节；到底时放宽到整视口。⛔ 只展开、不自动折叠。`UserCollapsed` 拦、`CollapsedByAll` 放行 |

- 🔴 激活色 `SettingsSection.IsActive` **只由 View 写**（= 被磁吸钉住的那个节点）；⛔ 别用 `ActiveSection` 染色——
  滚动时两者会不一致（"钉着 A、亮着 B"）。标题色靠 `UserControl.Styles` 里的 `.sec-title` + `.active` 后代选择器，
  ⛔ 模板里不许硬写 `Foreground`（本地值压过样式 ⇒ 激活不变色）。
- 🔴 定位必须**两阶段**：先展开/折叠 → 等 `LayoutUpdated` → 再算 offset（`TranslatePoint(→SectionScroll).Y + Offset.Y`），
  最多重试 8 次收敛；⛔ 同步一次算完（高度还在变，算出来必错）。

**节 / VM 归属**

| 节 | View | VM |
|---|---|---|
| 启动与退出 / 主题外观 | `GeneralView` / `ThemeView` | `GeneralViewModel`（无自有 VM，共用） |
| 一键按钮 | `LaunchConfigView` | `LaunchConfigViewModel` |
| 路径与配置 | `PathView` | `PathViewModel` + `Models/PathSettingItem` |
| 加密 DNS | `DnsView` | `DnsViewModel` |
| 数据管理 | `DataView` | `DataViewModel` |
| 版本与更新 | **`UpdateAboutView`**（拼装 `UpdateView` + `AboutSettingsView`） | `GeneralViewModel` + `AboutSettingsViewModel`（**两个**） |
| 页宿主 | `SettingsView` | `SettingsViewModel` + `Models/SettingsSection` |

- 复合页工具条：节 VM 靠 **`IToolbarOwner.ToolbarOwner`** 当承载者（见 §6.4）。
- 设置模型类被两模块共用时放 `Shared`（⛔ 双真相源）；配置落 `config\theme.json` / `app.json` / `launch.json` / `columnlayout.json`（见 §7.1）。

**操作逻辑**

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 展开 / 折叠节 | 点节抬头 | 手风琴：展开一节其余收起 | 手动收起的节不被自动展开 |
| 切节 | 点另一节抬头 / 页内搜索命中 | 工具条 / 状态栏 / 详情卡重绑到该节 VM | 见上「SectionChanged」 |
| 滚动 | 滚轮 | 当前节抬头磁吸顶部 | — |
| 页内搜索 | 顶部搜索框 | 只覆盖已注册节与一级项，命中即跳节 | 二级菜单项搜索未做（NT-066） |

**各节写入方式**（每项只有本页一个写入点）

| 节 | 时机 | 备注 |
|---|---|---|
| 启动与退出 / 更新 / 主题外观 / 一键按钮 / 加密 DNS | 即时落盘 | 主题即时生效 |
| 路径与配置 | 编辑即落盘 | **切换配置二次确认 + 本窗重启**；`🚀 新开` 同时开两套；外部程序路径**惰性探测**，缺失 ⇒ 状态栏 `⚠` 可点提示，⛔ 不崩 |
| 数据管理 | 显式动作 | 备份**只打包 `config\`**（⛔ 不含可重建的 `data\`）；导入 config |

**功能清单页 `settings-all`**

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 重载模块 | 增加功能区 `🔄 重载模块` | 扫 `Modules\` 新增模块并加载 | **只新增**，移除要重启 |
| 当选择器用 | 标题栏 `＋` / 关于节按钮 | 选中功能开标签，**不切走当前页** | — |

**首页 `home`**

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 一键按钮 | 点一键卡 | 按 `config\launch.json` 启动 | 配置来自「设置 → 一键按钮」 |
| 收藏 | 功能项 ★ | 写 `config\favorites.json`（即时） | — |
| 显示更多 | 收藏卡尾部 | 就地展开功能清单 | ⛔ 不跳页 |
| 标签行为 | — | `CanClose=false`（其余同普通标签） | 零标签兜底回首页（见 §4.3） |

**坑点**

1. 🔴 内容区 = `ContentControl` + `{Binding ActiveTab.Content}`，⛔ 不自己 `Children.Add`、⛔ 不绑独立属性。修「不换页 / 双击」落点 = `OnActiveTabChanged`（重活包 try/catch + `finally` 补发 `OnPropertyChanged(nameof(ActiveTab))`）。
2. 菜单标题**保持长名**（如「脚本管理器设置」）—— 它是独立标签页标题。
3. Shell 自带页面也走 `ResolveView` ⇒ 🔴 **类名必须全局唯一**（历史两处 `DnsView` 撞车）。

---

## §5 页面解析与 VM 定位

- View：`UserControl`，放模块 `Views/`，`.axaml` + `.axaml.cs`。
- VM：`NewToys.<模块>.Views.XxxView` ↔ `NewToys.<模块>.ViewModels.XxxViewModel`，**必须成对**。
- 解析（`ShellViewModel.ResolveView`）：① 遍历全部已加载程序集匹配 `Type.Name`（`Control` 子类）**取首个命中** → ② `ActivatorUtilities.CreateInstance` → ③ 推导 VM 优先限定在 View 自己的程序集（`.Views`→`.ViewModels`），找不到才降级全 `AppDomain` 扫。
- 🔴 **类名必须全局唯一**：解析未按程序集限定，同名 View 会撞车（历史两处 `DnsView`）。
- 无占位页：兜底 = `ShellViewModel.BuildErrorView()` 现场构造的红字 + `ScrollViewer`。
- Shell 自带页面同样走 `ResolveView`（先例 `HomeView` / `HomeViewModel`）。
- 可以先注册菜单、后补页面。

---

## §6 契约（Shared 侧接口，全部放 `Shared/Services/`）

> 实现与渲染口径见 `ui.md`；本节约只写**契约定义与 Shell 侧职责**。

### 6.1 `IDetailPanelProvider`

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

- Shell：`ActiveTab` 变化时检测 `ActiveTab.ViewModel is IDetailPanelProvider p`；是 → 显示右列 + 订阅 `p.DetailChanged`，`IsDetailOpen` 初值从 `ColumnLayoutService.LoadFlag(<pageKey>, FlagDetailPanel)` 读；否 → 隐藏右列。订阅前**先解绑旧 Tab**。
- ⚠️ 基类 `ToggleDetailCommand` **不发 `DetailChanged`** ⇒ 迁移页必须自己的开合命令。

### 6.2 `IHistoryProvider`

```csharp
public interface IHistoryProvider
{
    object? CaptureState();
    void RestoreState(object state);
    event EventHandler? StatePushed;
}
```

- Shell：`ActiveTab` 变化时检测并订阅；`TabItemViewModel.CanGoBack/CanGoForward` 控制前进后退按钮。
- 现状仅 `video-list`（目录切换）有实际语义。

### 6.3 `IDetailCardSource`

```csharp
public interface IDetailCardSource
{
    DetailCardDescriptor? Card { get; }   // 非 null → 声明式；null → 退回自绘 DetailContent
}
```

**件与归属（⛔ 别放错层）**

| 件 | 归属 |
|---|---|
| `DetailCardDescriptor` / `DetailSection` / `DetailField` / `DetailAction` / `FieldKind` / `EditCommitMode` | `Shared/Details/DetailCardDescriptor.cs`（纯 POCO，命令用 BCL `ICommand`） |
| `IDetailCardSource` | `Shared/Services/` |
| `DetailCardHost.axaml(.cs)` + `DetailCardRender.cs` | `NewToys.UI/Controls/`（NT-063 P0-a 从 Shell 搬入） |
| 16 张卡的 `Card` 声明 | 各页 VM（C# 对象字面量） |

- 渲染器确认弹窗经 `DetailCardHooks` 由 Shell 启动时转接 `DialogHooks.ConfirmHandler`。
- **Shell 两轨择一**（`ShellViewModel.RefreshDetail`）：① 是实现 `IDetailCardSource` 且 `Card != null` → `IsDetailCardActive = true`，⛔ **此时不读 `p.DetailContent`**；② 否则走老路 `DetailPanelContent = p.DetailContent`。`ShellView` 右列 = `Panel{ ContentControl(老路) + DetailCardHost(声明式) }`，按 `IsDetailCardActive` 互斥显隐。

### 6.4 `IToolbarSource` + `IToolbarOwner`

| 件 | 位置 | 说明 |
|---|---|---|
| `IToolbarSource` | `Shared/Services/` | `Toolbar`（`IReadOnlyList<ToolbarItemDescriptor>?`，空 / null = 无工具条）+ `ToolbarChanged` |
| `IToolbarOwner` | `Shared/Services/` | 🔴 `object? ToolbarOwner` —— **复合页**把工具条承载者让给"有工具条的那一节 VM"（描述符的 `CommandPath` 相对节 VM 解析；复合页自己当承载者 ⇒ 工具条"画得出来、点不动"）。Shell 规则：页面 VM 实现本接口 ⇒ 用 `ToolbarOwner` 作承载者，否则用页面 VM 自己 |
| `ToolbarItemDescriptor` / `ToolbarMenuItemDescriptor` | `Shared/Models/` | 纯 POCO；`*Path` 一律相对页面 VM |
| `ToolbarHost` | `Shell/Controls/` | 渲染 + 自动折叠 + 详情开关浮层 |
| `ListPageViewModelBase` | `Shared/Models/` | 已实现 `IToolbarSource`（`public virtual Toolbar => null`） |

### 6.5 选择器钩子（VM ⛔ 不引 Avalonia）

统一走 `Core.DialogHooks` 的 3 个钩子：`PickFolderAsync` / `PickFilesAsync`（多模式用 `;` 分隔，如 `"*.html;*.htm"`）/ `SaveFileAsync`；由 `Shell/App.axaml.cs` 注入实现（未注入 = 按「用户取消」处理）。

### 6.6 `IFileOperationService`（v3.66 / NT-069，跨模块磁盘动作）

| 件 | 位置 | 说明 |
|---|---|---|
| `IFileOperationService` | `Shared/Services/` | `Rename` / `Move` / `Copy` / `DeleteToRecycle` / `PermanentDelete` / `EnsureDirectory` / `IsInside`；返回 `FileOpResult(bool Ok, string Message, string? Path)`（`Shared/Services/` 内，与枚举 `FileConflictPolicy` 同文件） |
| `FileConflictPolicy` | 同上 | `AppendNumber`（默认）/ `ShortHash` / `Skip` / `Overwrite` |
| 实现 | `Core/Services/FileOperationService.cs` | 纯磁盘动作：⛔ 不碰数据库、⛔ 不弹窗、⛔ 不写业务日志 |
| 注册 | `Shell/App.axaml.cs` | 单例 |

🔴 **分层纪律**：确认框与库表记账留在各模块自己那层——媒体资产用 `Module.MediaAssets/Services/FileOperationService`（带 `operation_logs` 日志 + 存储源防穿越），视频工具用 `VideoFileService`（带 `DialogHooks` 确认）。两者都只把**磁盘那一步**交给本接口。

🔴 **命名坑**：`NewToys.Core.Services.FileOperationService` 与 `NewToys.MediaAssets.Services.FileOperationService` 同名 ⇒ 模块内同时 `using` 两个命名空间会 CS0104；消费方显式取别名（`AssetAllViewModel` / `AssetTrashViewModel` / `SettingsMediaAssetsViewModel` 已加）。

⚠️ 未完成（待办）：媒体资产那层的磁盘动作仍是自己的实现（含 `ConflictPolicy` 私有枚举），尚未改为委托本接口——只有视频工具接上了。

---

## §7 配置与路径

- 一律 `Path.Combine`，⛔ 硬编码 `\` 或 `/`。
- **用户数据**由 `ConfigStoreService` 唯一决定（不在仓库内）：应用根 `%LocalAppData%\NewToys`（环境变量 **`NEWTOYS_HOME`** 整根切换）。
  - 应用根之下分「配置」：`<应用根>\profiles\<配置名>\` → 其下才是 `config / data / logs`；当前配置名记在 **`<应用根>\active-profile.txt`**（在 `profiles\` 之外，否则切换会把自己也换掉）。
  - `ConfigStoreService` 构造时：① 确保 `profiles\<active>\` 存在（首次跑旧结构迁移）；② 暴露 `ConfigDir` / `DataDirectory` / `LogsDir`；③ `ProfilesRoot` / `ActiveProfileName` 供设置页用。
  - ⛔ 模块不自己拼路径，向 `IAppConfigService.DataDirectory` / `ConfigStoreService` 取值。
- **配置切换**：`IProfileService`（Core）负责列出 / 新增 / 重命名 / 删除 / 切换 / 设为默认；**切换 = 重启进程并显式传 `--profile <名>`**（`Process.Start(Environment.ProcessPath + " --profile " + name)` + `Environment.Exit(0)`），⛔ 不写 `active-profile.txt`；**设为默认**才写指针。⛔ 不做运行时热切换。
- **启动参数**：`NewToys.exe [--profile <配置名>]`；解析顺序 = **参数 > `active-profile.txt` > `default`**；非法 / 不存在 → 用默认 + 状态栏一行提示（⛔ 不崩、不静默）。
- **每配置单例**：命名互斥 `Local\NewToys_Profile_<配置名小写>`（`new Mutex(false, name)` + `WaitOne(0)`）；已占用 → 提示「该配置已在运行」后 `Environment.Exit(0)`，⛔ 不静默。⛔ 别用 `Global\`；配置名大小写不敏感 → mutex 名统一小写。
- **新开实例**：每配置行「🚀 新开」= `Process.Start(Environment.ProcessPath, $"--profile {name}")`；窗口标题 = `NewToys · <配置名>`。
- **窗口 bounds 记忆**：`config\window.json`（`X/Y/Width/Height/WindowState`），按配置各存各的；关闭时存，最小化态不存位置；还原时夹进当前屏幕工作区。
- 平台相关能力（winget / `ms-settings:` / ffmpeg）集中封装；路径穿越防御：外部相对路径 `Path.GetFullPath` 后校验落在目标目录内。

### 7.1 `config/` 文件清单（模块分区见各模块 md）

| 文件 | 归属 | 内容 |
|---|---|---|
| `theme.json` | 应用级设置 | 主题模式 + 强调色 id |
| `app.json` | 应用级设置 | ffmpeg / 播放器 / DNS 开关 / 脚本运行环境（python、pwsh、PS5.1 路径 + 脚本目录）。无视频库目录 |
| `launch.json` | 应用级设置 | 三个一键按钮 + 4 个通用开关 |
| `package.json` | 脚本管理器 | 软件包查询偏好。软件源本身不在这里 |
| `script-manager.json` | 脚本管理器 | `IOptionsStore` 分区名 `script-manager`，唯一写入点 = `settings-script` 页：脚本目录。⚠️ 分区名 ≠ 模块键（模块键是 `script`），历史惯例 |
| `web-tools.json` | 网络工具 | `IOptionsStore` 分区 `web-tools`，唯一写入点 = `settings-web-tools` 页：默认 UA / 抓取超时 |
| `video-tools.json` | 视频工具 | `IOptionsStore` 分区 `video-tools`，唯一写入点 = `settings-video-tools` 页：最大并发数 |
| `media-assets.json` | 媒体资产 | 唯一写入点 = `settings-media-assets` 页：扫描 / 缩略图 / 命名模板 / 回收站 / 物理操作 / 元数据 |
| `favorites.json` | 首页 | 收藏夹 |
| `script\script.json` | 脚本管理器 | 手工数据（集合 / 条目 / 标签 / 视图 / 上次集合）。原子写（`.tmp` + rename） |
| `columnlayout.json` | 框架层 | 列显隐 + 页面级开关（详情栏开合 `FlagDetailPanel`；壳级页 Key `"shell"`：左树折叠态 `treeCollapsed` + 各一级分组展开态 `treeGroup:<groupKey>`，默认 false） |

- **`data\` 下是观测缓存**（可重建、不进备份），如 `data\script\script.db`。
- `IOptionsStore.Validate` 禁路径分隔符 / `.` / `..` ⇒ 模块设置只能落 `config\<模块>.json`，模块私有子目录另建（如 `config\script\`）。
- ⛔ 不为单个功能预置默认内容（v3.60）：首次运行清单为空，条目由用户导入 / 新建。唯一例外 = 内置采集脚本播种（见 `modules/script-manager.md`）。

### 7.2 三配置档（debug / default / release）

- 三档**永久驻留**开发机，随时切换、**不丢数据**（⛔ 任何自动清理）。
- **Debug 构建首启播种**：`default`（既有；缺失则按现状播种）、`debug`（= 复制 `default` 内容）、`release`（= 干净初始态）。
- 动作是 **「重置为初始态」**（按播种源填回），⛔ 不提供「删除」。
- ⛔ 商店版配置（条件播种 / 排除 dev 文件 / 仅 `default`）归需求 §2.1 远期，**未实现**。

---

## §8 异常处理

- 导航 / 模块加载 / 主题应用等边界处 `try-catch`，失败不崩溃；至少写日志或兜底 UI，**不得吞异常无反馈**。
- 全局兜底：`AppDomain.UnhandledException` / 启动异常捕获，崩溃给可读错误窗。

---

## §9 构建与版本

- 构建 / 发布 / 环境切换**由用户本地执行**（`pwsh -File .\scripts\build.ps1 -Configuration Debug|Release`）；AI 改完 `src/` 后列出需用户验证的点，**不代跑构建**。
- ⛔ 禁止声称「已编译通过」；没跑过写「未编译，请用户本地执行 `build.ps1` 验证」。纯文档变更注明「不涉及编译」。
- ⛔ **脚本只用 PowerShell 7（`.ps1`），禁止 `.cmd` / `.bat`**；fail-loud（成功 `exit 0` 自关窗口、失败 `trap` 暂停）。

**版本号**（`src/Directory.Build.props` 编译时自动生成，⛔ 禁止手写）：
- `InformationalVersion` = `1.<yyyyMMdd>.<HH>.<mm取整到十位>`（显示用）
- `FileVersion` = `1.<yyyy>.<MMdd>.<HHmm>`；`AssemblyVersion` 固定 `1.0.0.0`
- 「设置·关于」读 `InformationalVersion`；左树底部 `TreeVersionText` 显示同一值。

**产物路径**：

| 产物 | 路径 |
|---|---|
| Debug 输出 | `artifacts/bin/<项目名>/Debug/net10.0/` |
| Debug 主程序 | `artifacts/bin/Shell/Debug/net10.0/NewToys.exe` |
| Release 输出根 | `artifacts/release/` |
| Release 模块 DLL | `artifacts/release/Modules/Module.*.dll`（直出，零拷贝） |
| 发布产物 | `production/NewToys_<运行时>/` |
| 测试环境根 | `artifacts/env/test/` |
| 用户数据 | ⚠️ 不在仓库内：`%LocalAppData%\NewToys\{config,data,logs}` |

AI 只读 `src/`，⛔ 不读 `artifacts/ bin/ obj/ .vs/`。

---

## §10 技术债台账（B 类）

> 本表 = **技术债（Tech Debt）**：当前做法是「需求 vs 工程现实」的妥协，非永久铁律。**变更条件满足 + 用户同意 + 本表登记**后方可调；未登记不得擅改。

| 项 | 理想 | 当前折衷 | 变更条件 | 影响面 |
|---|---|---|---|---|
| **B1 编译绑定** | 开启编译绑定 | 全工程关闭，不写 `x:DataType` | **保持关闭**（用户拍板） | 全部 View |
| **B2 VM 基类** | 统一继承 `Shared/ViewModelBase` | 模块内多直接继承 `ObservableObject`，仅 Shell/Home 用 `ViewModelBase` | 某模块改造时逐模块统一 | 各模块 VM |
| **B3 全局样式** | 显式 `x:Key` 样式键 | 用 `Classes` 隐式样式 | 收益足够大时整体改造 | `App.axaml` |
| **B4 导航服务** | 统一走 `INavigationService` | 主流程由 `ShellViewModel` 自解析；`NavigationService` 仅被模块内跳转经 `Navigated` 事件复用 | 删闲置服务或改用它（属框架层需同意） | Shell/Core |
| **B5 平台后端** | 完全跨平台 | 后端仍调 winget / ffmpeg 命令行，类型已中立为 `Package*` | 迁 Debian 时替换 | Core 服务 |
| **B6 仓库根 slnx** | 唯一入口 `src/NewToys.slnx` | 仓库根残留旧 `NewToys.slnx` 副本 | 直接删除即可 | 仓库 |
| **B7 Modules 冗余 DLL** | `Modules\` 仅含模块 DLL | 会多出 `NewToys.Shared.dll` / `NewToys.Core.dll` | 无害暂留 | Release 产物 |
| **B10 运行期热加载** | 任意模块随时加载 / 卸载 / 替换 | 仅支持「新增未加载过的模块」；容器已 build，模块不能注册新服务 | 引入可卸载 ALC + 模块级容器 | ModuleLoader + Shell |
| **B11 反射使用** | 最小魔法 | 模块装配 / 热加载 / 视图解析三处用反射 | 机制必需暂不改；⛔ 仅限这三处 | ModuleLoader / ShellViewModel |
| **B12 前进/后退** | 所有页面丰富页内导航 | 仅 `video-list` 有目录语义；多数页灰显 | 各页逐步实现 `IHistoryProvider` | 各模块页面 |
| **B14 媒体资产用户数据落库** | 手动数据落 `config\` 进备份 | 标签 / 分类 / 集合 / 关联暂存 `data\newtoys.db`，与需求 §3.5 定位冲突 | 用户拍板独立库 / 纳入备份时 | Module.MediaAssets |
| **B15 数据驱动图标** | 全矢量 | 二级菜单 `icon` / 首页一键按钮 / 模块页内数据行仍 emoji | 收敛工作项 **NT-047** | 各模块注册 + 首页 |
| **B16 `SetWindowRgn` 方角硬手段** | 官方 corner preference 稳定生效 | 保留 `ApplySquareRegion()` + `SetCornerPreferenceNoRound()` 双保险 | NT-042 已裁决保留（阴影代价已证伪） | `ShellView.axaml.cs` |
| **B17 窗口装饰 Win32 补丁残留** | 零补丁 | 方角两处 + `WM_NCCALCSIZE` 零内缩（Windows 独有例外）；样式位补位 / 贴靠纠偏已删 | 见 B16 + Avalonia 提供等价开关；迁非 Windows 前须补外框门控与窗口记忆回退 | `ShellView.axaml(.cs)` + `App.axaml` |

**已结清并移出本表**：B8（闲置转换器，已删）/ B9（骨架页面，零骨架）/ B13（详情面板上提，16/16 声明式）—— 明细见 `git log`。

---

## §11 跨模块待办（唯一收口处）

> 涉及 2 个以上模块、或改 Shared / Core / Shell / UI 的待办一律登记在此，**不在模块 md 里重复**。
> 任务卡放 `backlog/framework/`（同样 5 张滑动窗口）。台账状态以《项目状态与路线图》三、为准。

| NT | 内容 | 状态 |
|---|---|---|
| NT-047 | **数据驱动图标矢量化**：二级功能菜单 34 项 `icon` / 首页一键按钮 / 模块页内数据行（⛔ 静态装饰 emoji 不动） | 📝 草稿（= B15） |
| NT-064 | **列表分页数据层**：SQL `LIMIT/OFFSET` + 排序下推 + 独立 `COUNT(*)`；批量操作只作用当前页选中项。样板 = 全部资产，其余页待迁 | 🔄 已实施（样板页） |
| NT-065 | **分页推广到脚本库 / 命令库**：挂 `ListPageSkeleton` 底栏 + VM 分页（JSON 清单 ⇒ 内存切片，⛔ 不做 SQL） | 📝 草稿 |
| NT-066 | **标题栏搜索扩范围**：可搜二级菜单项 + 设置页节点（现只搜一级 / 已注册项）；跨 Shell ⇒ 需用户授权 | 📝 草稿 |
| NT-067 | **信息展示脚本化底座**（原 `pc-info`）：`ScriptRunner` / `InterpreterResolver` / `SysInfoGrid` 已落地并被 NT-068 继承 | ✅ 已实施 |
| NT-068 | **脚本管理器重构**：改名 + 数据库化 + 集合 + 信息面板分节（S1~S8 全部分步完成） | ✔ 已完成（⏳ 待本地验收） |

---

## 附录 A 高频编译错误速查

| 错误 | 原因 | 解法 |
|---|---|---|
| NU1010 | CPM 下 csproj 写了 Version 或 props 未声明 | props 补 `<PackageVersion>`；csproj 不写版本 |
| CS0260 | 带 `[ObservableProperty]` 的类未 `partial` | 类声明加 `partial` |
| AVLN2000 | 用了 WPF 专有写法 / `IsCheckable` | 改 Avalonia 语法（`Selector` 代 `TargetType`；列显隐用 `ToggleType=CheckBox`） |
| AVLN3000 | `ColumnDefinitions` 绑 `{Binding}` | 改列宽规格串 + `ColumnGrid` |
| CS1061 ItemsControl 未含 Children | `.axaml` 是 ItemsControl，code-behind 按 Panel 用 `.Children` | 两文件同步 |
| CS0103 当前上下文不存在 Xxx | XAML 元素没 `x:Name`，code-behind 引用 | 补 `x:Name` |
| CS0104「Xxx」不明确引用 | 应用侧类型与所用库同名 | 给应用侧类型换名（`AppMenuItem`）；⛔ 别用别名掩盖 |
| CS0507 | `Visual.OnAttachedToVisualTree` 写成 `public override` | 基类是 protected ⇒ 写 `protected override` |
| No precompiled XAML found | 用 `AvaloniaXamlLoader.Load` 加载独立资源字典 | 主题改纯代码构建 ResourceDictionary 注入 |
| NETSDK1022 重复项 | `defaults\**` 用了 `None Include` | 改 `None Update` |
| 菜单为空（一个模块都没） | 程序集延迟加载，扫 AppDomain 扫不到 | Debug 用显式类型清单；Release 先 `LoadFrom` |
| 侧栏菜单重复 N 次 | 模块被重复注册 | ModuleLoader 跳过已加载同名程序集 |
| 切主题后树 / 标签栏仍旧配色 | code-behind new 出的行是本地值 | 订阅 `ThemeChanged` 后重建；优先 Classes + 样式 |
| 点刷新没反应 | 只扫目录没重建左树；或容器已 build 无法注册服务 | 加载后 `BuildTree()`；页面型模块可不注册服务 |
| 菜单项静默丢失 | 跨组注册时目标分组未建 → 异常被逐模块 try-catch 吞 | `MenuService` 延迟补登已兜住；分组始终未建 → 健全性检查报一行 |
| `PathIcon` 不显示 | `Data` 绑的 `Geometry` 键不存在或拼错 | 全仓搜键名，确认 `App.axaml` 已定义 `Icon*` |
| `Application` 无 `TryFindResource` | Avalonia 12 里是 `IResourceHost` 扩展方法（命名空间 `Avalonia.Controls`），Try 模式 | 加 `using Avalonia.Controls;`；写 `if (app.TryFindResource(key, out var v) && v is Geometry g)` |
| `VisualRoot as Window` 恒为 null | 视觉根是 `TopLevelHost` | 走逻辑树 `FindLogicalAncestorOfType<Window>(includeSelf: true)` |
| 标签页重复打开 | `OpenTab` 未做单例判定 | `Tabs` 中按 `Key` 查重，存在则激活 |
| 关闭首页标签失败 | 未设 `CanClose=false` | 首页构造时 `CanClose=false`，`CloseTab` 检查 |
| 窗口不能拖边框缩放 | — | 走官方 CSD（见 `ui.md` §4） |

> 模块专属编译坑见各 `modules/<模块>.md` §5 坑点。
