# NewToys 项目现状与进度

> v1.30 · 最后更新 2026-10-10 · 台账（4 份文档中最"活"）
> **更新纪律**：
> - **更新单位 = 一个功能/一次交付**。完成后动「当前基线/已完成/待办」三节。
> - **仅修报错/错别字/格式，不回写正文**，并入下次功能更新追记。
> - 逐条实现记「六、变更流水」，正文只反映功能级变化。
> 配套：开发需求（宪法）/ 开发规范 / AI 协作提示词

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 四份均 v1.30 |
| 技术栈 | .NET 10 + Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 |
| 代码规模 | 7 项目 / 130 个 `.cs+.axaml+.csproj`（97 cs + 26 axaml + 7 csproj）/ 约 9234 行 |
| 仓库根 | 能看到 `src/` 的那一级；两个 props 在 `src/` 内 |
| 版本控制 | ✅ **已 `git init`**（分支 `main`，首次提交 `cf96b8a`）；`.gitignore` 忽略 `artifacts/ production/ bin/ obj/ Modules/ *.patch /.workbuddy/` |
| 构建分工 | 🧑‍💻 构建/发布由用户本地执行；AI 只改 `src/` |

**编译状态**：
- ✅ v1.14 用户本地 Debug/Release 均 0/0；v1.16 本地 Debug 0/0。
- ❌ v1.20 首次失败（`INavigationService` 缺 Navigated、元组元素名不一致）→ 已修复。
- ⚠️ **v1.21 / v1.25 改动后尚未由用户本地复跑** → 待跑 `build.ps1`。

**基线演进脉络**：
v1.14 工程约定统一 → v1.15 清除外部参照+构建职责移交 → v1.16 修复二级菜单列 → v1.17 补🏠首页 → v1.18 主题三态+强调色+即时生效 → v1.19 软件包管理+视频库+软件源+一键按钮收敛 → v1.20 类型中立化 Package* → v1.21 功能清单+收藏夹服务化 → v1.22 删除全部二级页头 → v1.24 电脑工具模块改造 → v1.25 列表页右键增删列 → v1.26 文档体系优化（无代码）→ v1.27 增补模块热加载（文档层待实装）→ **v1.28 面向本地执行 AI 客户端精简四份文档（无代码）** → **v1.29 优化 AI 协作流程：分级读档/变更声明/分型预检/停手规则（无代码）** → **v1.30 启用 git 版本管理（无代码）**。

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
`ConfigStoreService`（配置目录/备份/导入，路径穿越防御）、`AppConfigService`（app.json）、`LaunchConfigService`（launch.json）、`EncryptedDnsService`（DoH）、`DohProviders`（5 家）、`ThemeService`（三态+强调色）、`AppServices`（服务定位器）、`LaunchRunner`（批量启动）、`PackageManagerService`+`PackageOutputParser`（winget）、`PackageSettingsService`、`ColumnLayoutService`（列显隐）、`VideoScanner`（视频扫描）、`QuickActionService`（设置与脚本）。

### 2.6 页面全景

| 模块 | 页面数 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 系统设置 | 9 | **9** | 0 |
| PC 工具箱 | 5 | **3** | 2 |
| Web 工具箱 | 6 | 0 | **6** |
| 视频工具箱 | 3 | **1** | 2 |
| **合计** | **23** | **13** | **10** |

> **10 骨架** = Web 全 6 页 + `pc-info`+`pc-quicksettings`+`video-cut`+`video-transcode`。
> 骨架判据：VM 仅有 Title/Description/StatusText/IsBusy 与 PrimaryAction/Refresh 样板命令。
> **13 真实页**：设置 9 页（通用/主题/路径/一键按钮/加密DNS/软件源/数据管理/关于/功能清单）+ `pc-package`+`video-list`+`pc-shortcuts`+`pc-favorites`。

---

## 三、菜单清单（23 项）

> 权威来源=各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。
> 图例：✅真实 ｜ 🔧骨架。

| 分组 | Key | Order | 图标 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | 🏠 | （无二级，第一列顶部 `BtnHome→GoHomeCommand`） |
| 电脑工具 | pc | 20 | 🖥 | `pc-shortcuts`设置与脚本▤MyShortcutsView/0/✅ ｜ `pc-favorites`收藏夹★FavoritesView/1/✅ ｜ `pc-package`软件包📦PackageManagerView/2/✅ ｜ `pc-info`系统信息💻PcInfoView/3/🔧 ｜ `pc-quicksettings`快捷设置⚙QuickSettingsView/99/🔧 |
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
5. **`pc-package` 收尾**：`PackageOutputParser` 实机验证；批量勾选+队列评估。
6. **`settings-package` 默认查询源**：确认"用户从不进设置页时行为与旧版一致"。
7. **模块热加载**（需求第四节第6条）：扩展 `ModuleLoader`（独立可卸载 ALC+跳过同名+逐模块失败隔离）、刷新入口、加载后重建两侧菜单。⚠️ B10 限制：本版只做「新增」，同名不替换不卸载。

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
| 列显隐 | `Core/Services/ColumnLayoutService.cs`+`Shared/Models/ListColumn.cs`+模块内 `Views/ColumnGrid.cs` |
| winget | `Core/Services/PackageManagerService.cs`+`PackageOutputParser.cs`+`PackageSettingsService.cs` |
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

---

## 七、下一步建议

1. 用户本地 `pwsh -File .\scripts\build.ps1`（Debug+Release）确认 v1.25 基线 0/0（属验基线，不变更台账）。
2. ~~`git init` 建版本控制~~ ✅ 已完成（分支 `main`，首次提交 `cf96b8a`）。
3. 按 P2 顺序逐页实装（一次一个页面/一个功能，完成再改本文件）。
4. 启动模块热加载改造，参照规范 6.6 先做最小可用：扫 `Modules\` → 独立 ALC 装载新增 DLL → 重建两侧菜单 → 失败隔离；先支持「新增」，同名替换/卸载留 B10 升级。
