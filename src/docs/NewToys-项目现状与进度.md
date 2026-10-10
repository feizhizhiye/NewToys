# NewToys 项目现状与进度

> v1.33 · 最后更新 2026-10-10 · 台账（4 份文档中最"活"）
> **更新纪律**：
> - **更新单位 = 一个功能/一次交付**。完成后动「当前基线/已完成/待办」三节。
> - **仅修报错/错别字/格式，不回写正文**，并入下次功能更新追记。
> - 逐条实现记「六、变更流水」，正文只反映功能级变化。
> - 逐次变更历史由 **git** 记录；本台账只做功能级/里程碑更新，不逐次追记文档级改动。
> 配套：开发需求（宪法）/ 开发规范 / AI 协作提示词

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v1.30（🔒冻结）｜规范 v1.32 ｜ 提示词 v1.30 ｜ 本台账 v1.33 |
| 技术栈 | .NET 10 + Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 |
| 代码规模 | 7 项目 / 131 个 `.cs+.axaml+.csproj`（98 cs + 26 axaml + 7 csproj）/ 约 10599 行 |
| 仓库根 | 能看到 `src/` 的那一级；两个 props 在 `src/` 内 |
| 版本控制 | ✅ **已 `git init`**（分支 `main`，首次提交 `cf96b8a`）；`.gitignore` 忽略 `artifacts/ production/ bin/ obj/ Modules/ *.patch /.workbuddy/` |
| 构建分工 | 🧑‍💻 构建/发布由用户本地执行；AI 只改 `src/` |

**编译状态**：
- ✅ v1.14 用户本地 Debug/Release 均 0/0；v1.16 本地 Debug 0/0。
- ❌ v1.20 首次失败（`INavigationService` 缺 Navigated、元组元素名不一致）→ 已修复。
- ⚠️ **v1.21 / v1.25 / v1.32 / v1.33 改动后尚未由用户本地复跑** → 待跑 `build.ps1`。

**基线演进脉络**：
v1.14 工程约定统一 → v1.15 清除外部参照+构建职责移交 → v1.16 修复二级菜单列 → v1.17 补🏠首页 → v1.18 主题三态+强调色+即时生效 → v1.19 软件包管理+视频库+软件源+一键按钮收敛 → v1.20 类型中立化 Package* → v1.21 功能清单+收藏夹服务化 → v1.22 删除全部二级页头 → v1.24 电脑工具模块改造 → v1.25 列表页右键增删列 → v1.26 文档体系优化（无代码）→ v1.27 增补模块热加载（文档层待实装）→ **v1.28 面向本地执行 AI 客户端精简四份文档（无代码）** → **v1.29 优化 AI 协作流程：分级读档/变更声明/分型预检/停手规则（无代码）** → **v1.30 启用 git 版本管理（无代码）** → **v1.31 修订文档更新纪律：不逐次改、独立版本、需求冻结（无代码）** → **v1.32 设置与脚本可运行 + 快捷设置改为系统自带设置列表页 + 收窄两页分工 + 收藏夹简化** → **v1.33 列表页详情栏可开合 + 收藏夹改按钮墙 + 两页命令改本地配置化（defaults/ + 脚本目录 + 解释器路径）+ 软件源页直接操作系统 winget + 修 winget 中文表头解析（列表空白）**。

---

## 二、已完成

### 2.1 架构与骨架
- **7 项目**：`Shared/Core/Shell` + 4 模块；分层单向 `Shared←Core←Shell←Module.*`。
- 中央包版本管理 + 版本号编译自动生成。
- DI 容器 + `IModule` 插件机制 + 菜单/导航/主题服务。
- **模块加载双轨**：Debug 显式类型清单 / Release 目录扫描；**运行期热加载（Modules+刷新按钮）⏳ 待实装**。
- 命名统一（v1.14）：命名空间 `NewToys.*`，`.Module` 段已清除。

### 2.2 UI（仿微信）
- 三栏 `60,210,*`，行定义 `40,*`（标题栏 40px）。
- 一级菜单无二级项时第二列收 0（`UpdatePanelVisibility()` 唯一出口）。
- ⚙ 设置最底；🏠 首页最顶（v1.17 补回）。
- 自定义标题栏（最小化/最大化/关闭、拖拽、双击最大化）。
- 明暗主题全局切换，键一律 `*Brush`（32 个），默认深色。
- 全局样式在 `Shell/App.axaml`；选中态 `active` class。

### 2.3 首页
- 欢迎卡 + 6 张收藏卡（`IFavoritesService`）+ 3 个一键按钮（逻辑可用）。

### 2.4 导航与视图解析
- 菜单点击 → `ShellViewModel.ResolveView` → `CurrentContent`；失败返回红字错误控件（无占位页）。
- 可先注册菜单、后补页面。

### 2.5 已实现服务
`ConfigStoreService`（配置目录/备份/导入，路径穿越防御）、`AppConfigService`（app.json，v1.33 增脚本运行环境：python/pwsh/ps5.1 路径 + 脚本目录）、`LaunchConfigService`（launch.json）、`EncryptedDnsService`（DoH）、`DohProviders`（5 家）、`ThemeService`（三态+强调色）、`AppServices`（服务定位器）、`LaunchRunner`（批量启动）、`PackageManagerService`+`PackageOutputParser`（winget；v1.33 支持源的增删重置 + 中英表头解析）、`PackageSettingsService`、`ColumnLayoutService`（列显隐 + **页面级开关**如详情栏开合）、`VideoScanner`（视频扫描）、`QuickActionService`（两页清单；v1.33 **命令内容不写死在代码里**，默认清单随程序发布在 `defaults\`；**运行**：命令 / 脚本，cmd 执行 + UAC 提权 + 输出摘要）。

### 2.6 页面全景

| 模块 | 页面数 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 系统设置 | 9 | **9** | 0 |
| PC 工具箱 | 5 | **4** | 1 |
| Web 工具箱 | 6 | 0 | **6** |
| 视频工具箱 | 3 | **1** | 2 |
| **合计** | **23** | **14** | **9** |

> **9 骨架** = Web 全 6 页 + `pc-info` + `video-cut` + `video-transcode`。
> 骨架判据：VM 仅有 Title/Description/StatusText/IsBusy 与 PrimaryAction/Refresh 样板命令。
> **14 真实页**：设置 9 页（通用/主题/路径/一键按钮/加密DNS/软件源/数据管理/关于/功能清单）+ `pc-package`+`pc-shortcuts`+`pc-quicksettings`+`pc-favorites`+`video-list`。
>
> **PC 工具箱两页分工（v1.33）**：`pc-quicksettings`「快捷设置」= **命令行命令**（系统自带设置 / 工具入口：`ms-settings:`、控制面板、管理工具，点「▶ 打开」即用）；`pc-shortcuts`「设置与脚本」= **脚本文件**（脚本目录下的 .py / .ps1 / .bat，解释器路径在「设置 → 路径配置」配）。两页**共用** `config\quickactions.json`（按 `QuickAction.Page` 分栏），逻辑共用 `ViewModels/QuickActionListViewModel.cs` 基类；**清单内容不写死在代码里**，默认值随程序发布在 `Shell\defaults\`。
>
> **列表页右栏详情可开合（v1.33）**：`pc-package`/`video-list`/`pc-shortcuts`/`pc-quicksettings` 四页的工具条上有「▤ 详情 / ▤ 收起详情」按钮，**默认收起**（列表占满宽度），选择落 `config\columnlayout.json` 并记住。

---

## 三、菜单清单（23 项）

> 权威来源=各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。
> 图例：✅真实 ｜ 🔧骨架。

| 分组 | Key | Order | 图标 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | 🏠 | （无二级，第一列顶部 `BtnHome→GoHomeCommand`） |
| 电脑工具 | pc | 20 | 🖥 | `pc-favorites`收藏夹★FavoritesView/0/✅ ｜ `pc-shortcuts`设置与脚本▤MyShortcutsView/1/✅ ｜ `pc-quicksettings`快捷设置⚙QuickSettingsView/2/✅ ｜ `pc-package`软件包📦PackageManagerView/3/✅ ｜ `pc-info`系统信息💻PcInfoView/99/🔧 |
| 网络工具 | web | 30 | 🌐 | `web-home`首页🏠WebHomeView/0/🔧 ｜ `web-http`HTTP测试🧪WebHttpView/1/🔧 ｜ `web-bookmark`书签🔖BookmarkView/2/🔧 ｜ `web-ping`Ping📡PingView/3/🔧 ｜ `web-scrape`抓取🕷ScrapeView/4/🔧 ｜ `web-dns`DNS🛡DnsView/5/🔧 |
| 视频工具 | video | 40 | 🎬 | `video-list`视频库📁VideoListView/0/✅ ｜ `video-cut`剪辑✂VideoCutView/1/🔧 ｜ `video-transcode`转码🔄TranscodeView/2/🔧 |
| 设置 | settings | 999（底） | ⚙ | `settings-general`通用⚙GeneralView/0/✅ ｜ `settings-theme`主题🎨ThemeView/1/✅ ｜ `settings-path`路径📁PathView/2/✅ ｜ `settings-launch`一键按钮🚀LaunchConfigView/3/✅ ｜ `settings-dns`加密DNS🛡DnsView/4/✅ ｜ `settings-package`软件源📦PackageSourceView/5/✅ ｜ `settings-data`数据管理💾DataView/6/✅ ｜ `settings-about`关于ℹAboutView/7/✅ ｜ `settings-all`功能清单📋AllFunctionsView/8/✅ |

---

## 四、待办与优先级

### P0 · 版本控制 ✅ 已完成
- 已 `git init`（`main`）+ 首次提交 `cf96b8a`；此后每次交付以 git 提交记录变更（⛔ 不再写 md 日志）。

### P1 · 技术债
| # | 问题 | 建议 |
|---|---|---|
| 1 | 编译绑定被逐项目关闭（23 View 无 `x:DataType`） | 二选一写进规范：全开补 `x:DataType`，或承认默认关闭（B1） |
| 2 | 全局样式键用 `Classes` 隐式样式 | 收益有限暂挂（B3） |
| 3 | `NavigationService` 主流程闲置（`ShellViewModel` 自解析） | 删闲置服务或改用它（属框架层需同意，B4） |
| 4 | `BoolInversionConverter` 未使用 | 保留/删除均可（B8） |
| 5 | 仓库根残留旧 `NewToys.slnx` | 删除，唯一入口 `src/NewToys.slnx`（B6） |

### P2 · 功能待办
1. **`web-dns` 接线**：复用 `IEncryptedDnsService.ResolveAsync`（骨架已就绪）。
2. **通用设置「系统级行为」**：开机启动/托盘/拦截关闭/联网检查（当前只持久化）。
3. **模块页面逐个实装**：按节奏推（"贴一个页面源码 → 出一个补丁"）。
4. **视频工具箱**：`video-cut`/`video-transcode`，依赖 `FfmpegService` 落地。
5. **`pc-package` 收尾**：`PackageOutputParser` 实机验证（v1.33 已修中文表头 + `ARP\`/`MSIX\` 类 Id，待跑）；批量勾选+队列评估。
6. **`settings-package` 软件源**：已改为**直接操作系统 winget**（增删源 / 恢复默认，二次确认），需实机验证 `source list` 解析与 `source add/remove/reset` 的退出码。
7. **`defaults\` 与脚本目录**：确认**发布产物**里 `defaults\**` 被复制到程序目录（Release 走 `artifacts\release\defaults\`），以及首次运行时 `%LocalAppData%\NewToys\scripts\` 的示例脚本播种。
8. **模块热加载**（需求第四节第6条）：扩展 `ModuleLoader`（独立可卸载 ALC+跳过同名+逐模块失败隔离）、刷新入口、加载后重建两侧菜单。⚠️ B10 限制：本版只做「新增」，同名不替换不卸载。

---

## 五、关键文件索引

| 关注点 | 文件 |
|---|---|
| 启动/DI/模块装配/主题字典(32键)/系统明暗监听 | `src/Shell/App.axaml.cs` |
| 全局样式 | `src/Shell/App.axaml` |
| 主窗口（三栏/列切换/手工导航） | `src/Shell/Views/ShellView.axaml(+cs)` |
| Shell 状态（IsHomePage/CurrentGroup/CurrentItem/CurrentContent） | `src/Shell/ViewModels/ShellViewModel.cs` |
| 首页 | `src/Shell/Views/HomeView.axaml`+`ViewModels/HomeViewModel.cs` |
| 模块加载（Debug清单/Release扫描/热加载接点） | `src/Core/Services/ModuleLoader.cs` |
| 菜单服务 | `src/Core/Services/MenuService.cs` |
| 主题服务/强调色 | `Core/Services/ThemeService.cs`+`Shared/Services/AccentPalette.cs` |
| 配置存储（目录/备份/导入/穿越防御） | `src/Core/Services/ConfigStoreService.cs` |
| 列显隐 + 页面级开关（详情栏开合） | `Core/Services/ColumnLayoutService.cs`+`Shared/Services/IColumnLayoutService.cs`+`Shared/Models/ListColumn.cs`+模块内 `Views/ColumnGrid.cs` |
| 设置与脚本 / 快捷设置（两页共用基类 + 运行） | `Core/Services/QuickActionService.cs`+`Shared/Models/QuickAction.cs`+`Module.PcTools/ViewModels/QuickActionListViewModel.cs`+`Views/MyShortcutsView.axaml`/`Views/QuickSettingsView.axaml`/`Views/FavoritesView.axaml` |
| **默认数据（不清算在代码里）** | `src/Shell/defaults/quickactions.default.json` + `src/Shell/defaults/scripts/*`（发布到 `<程序目录>\defaults\`，首次运行播种到 `config\` 与 `<应用根>\scripts\`；`Shell.csproj` 用 `None Update` 复制） |
| 运行环境（解释器路径 + 脚本目录） | `Shared/Services/IAppConfigService.cs`+`Core/Services/AppConfigService.cs`（app.json）+`Module.Settings/Views/PathView.axaml` |
| winget | `Core/Services/PackageManagerService.cs`+`PackageOutputParser.cs`+`PackageSettingsService.cs`；软件源页 `Module.Settings/Views/PackageSourceView.axaml`（直接操作系统 winget） |
| 加密 DNS（DoH+ConnectCallback） | `src/Core/Services/EncryptedDnsService.cs` |
| 模块菜单注册（Key权威源） | 各模块 `XxxModule.cs` |
| 构建属性/版本号/包版本 | `src/Directory.Build.props`/`src/Directory.Packages.props` |
| 文档体系 | `src/docs/` 4 份 |

---

## 六、变更流水

| 日期 | 版本 | 内容 |
|---|---|---|
| 2026-10-10 | v1.25 | 列表页升级「左键排序+右键增删列」双交互（pc-package/video-list/pc-shortcuts）。 |
| 2026-10-10 | v1.26 | 文档体系优化：需求→宪法、规范分「硬约束/折衷台账」、台账更新纪律、提示词精简。**无代码**。 |
| 2026-10-10 | v1.27 | 增补「模块热加载」：宪法第6条+验收第4问；规范6.6+硬约束+折衷B10/B11；台账P2-7。**无代码**。 |
| 2026-10-10 | **v1.28** | **面向本地执行 AI 客户端精简四份文档**：砍下载链接/快照导出/zip 兜底等面向无执行能力 AI 的段落、删 ASCII 布局图、删大段示例代码/历史事故复盘；保留硬约束/折衷台账/接口签名/关键尺寸/启动时序。**无代码**。 |
| 2026-10-10 | **v1.29** | **优化 AI 协作流程（降 token + 提成功率）**：① 提示词 [0] 改「分型+分级读档」（只读任务相关章节，不整篇读）；② 删 [3] 与规范的重复速记，改为精简「高频易错」+ 章节指针；③ 新增 [2] 变更声明闸门、[4] 分型静态预检、[5]⑤ 停手规则；④ 规范 §11.1-1 由「完整读 4 份」改为「分级读档」。**无代码**。 |
| 2026-10-10 | **v1.30** | **启用 git 版本管理**：`git init`（`main`）+ 首次提交 `cf96b8a`（源码 + v1.29 文档）；`.gitignore` 补忽略 `/.workbuddy/`；协作流程改为「git 提交记录变更」取代 md 日志（提示词 [5]③ + 规范 §0）；台账 P0 结项。**无代码**。 |
| 2026-10-10 | **v1.31** | **修订文档更新纪律**：指导性文件（需求/规范/提示词）不再逐次改；各文档独立版本号、取消「四份齐步」；《开发需求》冻结；逐次变更历史交由 git。**无代码**。 |
| 2026-10-10 | **v1.32** | **电脑工具两页分工 + 运行能力 + 收藏夹收窄**：①「设置与脚本」「快捷设置」行内 + 详情卡 + 右键均可「▶ 运行」（`cmd /c`，需管理员走 UAC）；②`pc-quicksettings` 从骨架改为**列表页**，内容=系统自带设置/工具入口（`ms-settings:` / 控制面板 / 管理工具），布局与「设置与脚本」一致（抽出共用基类 `QuickActionListViewModel` + 新增「操作」列，两页共用 `quickactions.json`，按 `QuickAction.Page` 分栏、旧文件按分类回填）；③两页右键可「★ 添加到收藏夹」；④收藏夹卡片只留「▶ 运行」，右键菜单「运行 / ✕ 取消固定」。 |
| 2026-10-10 | **v1.33** | **列表页详情开合 + 收藏夹按钮化 + 两页命令配置化 + 软件源直连系统 winget**：①四个列表页（`pc-package`/`video-list`/`pc-shortcuts`/`pc-quicksettings`）工具条新增「▤ 详情」开合按钮，**默认收起**（列表占满宽度），状态落 `columnlayout.json` 并记住（`IColumnLayoutService` 新增 `LoadFlag/SaveFlag`，用保留前缀 `__` 与列键共存一个文件）；②收藏夹由卡片改**按钮墙**（一排按钮只显名称，点一下即跑；右键「运行 / ✕ 取消固定」）；③两页命令**不再硬编码**：默认清单随程序发布 `Shell\defaults\quickactions.default.json`、示例脚本 `Shell\defaults\scripts\*`，首次运行播种；「快捷设置」全为命令行命令、「设置与脚本」为脚本文件（`QuickActionKind`/`ScriptInterpreter`）；`app.json` 新增 python/pwsh/PS5.1 路径与脚本目录（留空=系统默认），落「设置 → 路径配置」；④「设置 → 软件源」**直接操作系统 winget**（列表读 `source list`、行内 ✕ 停用 = `source remove`、添加 = `source add`、恢复默认 = `source reset --force`，后两者二次确认），`package.json` 去掉源开关只留查询偏好；⑤`PackageOutputParser` 修**中文表头**（`名称/ID/版本/可用`）与 `IdLike` 不认 `\`（`ARP\`/`MSIX\` 条目被整批丢弃）两个导致「列表空白」的根因，并修 `winget show` 的 `发布服务器 URL` 粘进「发布者」。 |

---

## 七、下一步建议

1. 用户本地 `pwsh -File .\scripts\build.ps1`（Debug+Release）确认 0/0；v1.33 改动面较大（列表页四页 XAML + winget 解析 + defaults 复制 + 软件源页重写），建议先 Debug 跑一遍再 Release 验 `defaults\` 落地。
2. ~~`git init` 建版本控制~~ ✅ 已完成（分支 `main`，首次提交 `cf96b8a`）。
3. 按 P2 顺序逐页实装（一次一个页面/一个功能，完成再改本文件）。
4. 启动模块热加载改造，参照规范 6.6 先做最小可用：扫 `Modules\` → 独立 ALC 装载新增 DLL → 重建两侧菜单 → 失败隔离；先支持「新增」，同名替换/卸载留 B10 升级。
