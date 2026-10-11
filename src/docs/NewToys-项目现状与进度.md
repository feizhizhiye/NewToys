# NewToys 项目现状与进度

> v3.10 · 2026-10-11 · 台账
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> 逐次变更历史一律看 git；本台账只做**功能级/里程碑级**更新。
> 配套：开发需求（宪法）/ 开发规范 / AI 协作提示词
>
> **v3.0 变更**：登记「UI 重构（仿浏览器标签页 + 树状侧栏 + 视觉规范）」为 P0；基线版本号同步刷新。
> **v3.1 变更**：范围收敛——本轮只做「Shell 外壳 + 全站视觉基线 + 列表页详情上提」，**功能不改、图标沿用 emoji**；搜索框 / 历史栈实导航 / 图标矢量化延后为下一版。
> **v3.2 变更**：**列表页详情上提本轮暂缓**（实际 12 页，v3.1 误写 5 个）；Shell 外壳重写（批次 1）已落盘，待本地编译验证。
> **v3.3 变更**：标签栏并入标题栏中段（+ 滚轮切标签）、一级菜单矢量图标、二级菜单去图标、折叠只留一级图标；文档起执行「**新需求先入文档、再改代码**」的顺序。
> **v3.4 变更**：标题栏精简（删 🏠/⚙/🌓）+ 底色改主题背景（配色从 `Sider*` 切回主题色系）；标签页去图标 / `✕` 悬停显示 / 两侧留空隙；左树一级默认折叠 + 层级样式区分 + 二级激活联动父级高亮；标题栏图标放大到 18px。
> **v3.5 变更**：删前进/后退按钮；标签栏两侧固定留白；左树**手风琴**（展开态只展开当前一级、折叠态一级显主题色、折叠即全收）+ 展开态不再落盘；**新增「设置 → 配置」多配置**（`profiles\<名>\{config,data,logs}` + `active-profile.txt`，切换 = 重启）+ 旧数据自动迁移。
> **v3.6 变更**：**撤销手风琴**（切标签只确保当前一级展开、不收起其它；展开态恢复落盘）；标签栏两侧留白 16→24px、**切换后自动滚到可见**（最小滚动）、**关闭按钮不占位**（叠在文字上、悬停才现）。
> **v3.7 变更**：紧凑化（标签字号 13px、标签项 10/4、toolbar/filter-bar 内边距 24→16）+ **左树 240→180px**；列表页**三列布局**（工具条收进列表列、详情卡抬到顶）**已铺到 10 页**（`video-cut`/`video-transcode` 除外）；视频工具重构（删转码页 / 加批量处理页 / 剪辑改命令生成页）**本版只写需求**。
> **v3.8 变更**：视频工具重构**开始落地** —— 「格式转码」改名改造为「批量处理」（key `video-batch`，列表 = 剪辑 + 转码混排统一队列；原 `Transcode*` 三文件已删）；视频列表页发送入口改向；「命令生成」页（原视频剪辑改造）**下一批**。
> **v3.9 变更**：**底部状态栏**（Shell 承载，横贯内容区 + 详情区；`IStatusProvider` 契约，**列表页 12 页 + 非列表页 9 页全部接入**，页面顶部状态行已删除）；**左树底部版本号区**（占位 `1.26.10`）；**收藏夹按钮**改「描边 + 悬停实心」。
> **v3.10 变更**：视频工具「命令生成」页**再设计**（需求 §6.4.1 / 规范 §3.9）—— 改「类播放器预览 + 三条时间条 ｜ 详情卡」两列；删抽帧缩略图墙 + 工具条任务类型单选；任务类型移入详情卡（剪辑/转码可多选）、两套设置区可各自折叠、**命令可编辑**、转码参数补足「格式 / 编码器(CPU·GPU) / 分辨率 / 码率」、视频列表送来**默认预览**；**评估并纳入**同源多段剪辑（一期"多段 = 多条命令"）与命令生成函数（一期"单任务吸附"、二期"类 Excel 填充"）。**📝 只写文档，代码下一批。**

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.10 ｜ 规范 v3.10 ｜ 提示词 v3.9 ｜ 本台账 v3.10 |
| 技术栈 | .NET 10 + Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 7 项目 / 约 154 个 `.cs+.axaml+.csproj` / 约 19400 行 |
| 仓库根 | 能看到 `src/` 的那一级 |
| 版本控制 | ✅ `git init`（分支 `main`，首次提交 `cf96b8a`） |
| 构建分工 | 🧑‍💻 构建/发布由用户本地执行；AI 只改 `src/` |

**编译状态**：
- ✅ v1.14 用户本地 Debug/Release 均 0/0；v1.16 本地 Debug 0/0。
- ⚠️ **v1.21 起的改动尚未由用户本地复跑** → 待跑 `build.ps1`。
- 🚧 **批次 1（Shell 外壳）本地编译已通过**；**v3.3 追加改动待本地 `build.ps1` 验证**。

**里程碑脉络**（关键节点）：
v1.14 工程约定统一 → v1.17 补🏠首页 → v1.18 主题三态+强调色 → v1.20 类型中立化 → v1.25 列表页右键增删列 → v1.30 启用 git → v1.32 设置与脚本可运行 → v1.33 列表页详情开合 + 收藏夹按钮化 + 软件源直连系统 winget → v1.34 引入 SQLite + 视频库媒体信息 + 两页拆文件 → v1.35 全局样式表 + 列表页统一基类 + 通用设置系统级行为生效 + 系统信息页实装 + 视频剪切/转码实装 → **v3.0 UI 从仿微信三栏重构为仿浏览器标签页 + 树状侧栏 + 视觉规范（进行中）→ v3.1 范围收敛（布局先行，功能/图标不动）→ v3.2 详情上提暂缓（实际 12 页）＋ Shell 外壳重写落盘**。

---

## 二、已完成

### 2.1 架构与骨架
- **7 项目**：`Shared/Core/Shell` + 4 模块；分层单向。
- 中央包版本管理 + 版本号编译自动生成。
- DI 容器 + `IModule` 插件机制 + 菜单/导航/主题服务。
- **模块加载双轨**：Debug 显式类型清单 / Release 目录扫描。
- **运行期热加载（Modules+刷新按钮）⏳ 待实装**（P2-5）。

### 2.2 UI（v3.3 布局落地）
- ~~三栏 `60,140,*`~~ → **标题栏 40px（标签栏并入其中段，v3.3）+ 树状左栏（可折叠 240/48）+ 内容区 + 右详情（可开合 340）**。
- ~~一级菜单无二级项时第二列收 0~~ → 标签页驱动 + 树折叠。
- 明暗主题全局切换，键一律 `*Brush`（32 个），默认深色。
- **视觉规范（v3.0 / v3.3）**：**共享样式部分已落地**（卡片圆角 8 / 内边距 14、详情卡圆角 8 / 内边距 14、表格行高 32）；**逐页间距 / 字号统一（内容区 `20,16` 等）延后至下一版**；图标矢量化——**一级菜单已落地**（v3.3），标题栏 / 标签页**延后**。

### 2.3 页面全景

| 模块 | 页面数 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 系统设置 | 10 | **10** | 0 |
| PC 工具箱 | 5 | **5** | 0 |
| Web 工具箱 | 5 | **4** | 1 |
| 视频工具箱 | 3 | **3** | 0 |
| **合计** | **23** | **22** | **1** |

> **唯一剩下的骨架** = Web 的 `web-http`。
> 骨架判据：VM 仅有 Title/Description/StatusText/IsBusy 与 PrimaryAction/Refresh 样板命令。

### 2.4 菜单清单（24 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。
> 图例：✅真实 ｜ 🔧骨架。图标列在**下一版**换为 `Icon*` 键（本轮沿用 emoji，见规范 §2.3）。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是首个固定标签页） |
| 电脑工具 | pc | 20 | `IconPc` | `pc-favorites`收藏夹/`IconFavorite`/0/✅ ｜ `pc-shortcuts`设置与脚本/`IconScript`/1/✅ ｜ `pc-quicksettings`快捷设置/`IconQuick`/2/✅ ｜ `pc-package`软件包/`IconPackage`/3/✅ ｜ `pc-info`系统信息/`IconInfo`/99/✅ |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP测试/`IconHttp`/1/🔧 ｜ `web-bookmark`书签/`IconBookmark`/2/✅ ｜ `web-ping`网络Ping/`IconPing`/3/✅ ｜ `web-scrape`网页抓取/`IconScrape`/4/✅ ｜ `web-hosts`Hosts管理/`IconHosts`/5/✅ |
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**命令生成**/1/✅ ｜ `video-batch`**批量处理**/2/✅ |
| 设置 | settings | 999 | `IconSettings` | `settings-general`通用/0/✅ ｜ `settings-theme`主题/1/✅ ｜ `settings-path`路径/2/✅ ｜ `settings-launch`一键按钮/3/✅ ｜ `settings-dns`加密DNS/4/✅ ｜ `settings-package`软件源/5/✅ ｜ **`settings-profile`配置/6/✅** ｜ `settings-data`数据管理/7/✅ ｜ `settings-about`关于/8/✅ ｜ `settings-all`功能清单/9/✅ |

---

## 三、待办与优先级

### P0 · 布局重构（v3.0 → v3.3）🚧 进行中

> **v3.3 范围**：标签栏并入标题栏、一级菜单矢量图标、二级菜单去图标；功能不改。详见需求「〇、本版落地范围（v3.3）」。

**v3.9 追加（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **底部状态栏**：新增 `Shared/Services/IStatusProvider.cs`；`ListPageViewModelBase` **实现契约**并把 `StatusText` **提到基类**（11 个列表页 VM 里手写的 `_statusText` 已删）；`ShellViewModel` 切标签时探测并订阅；`ShellView.axaml` 在**内容区 + 详情区下方**加横贯状态栏（26px、`PanelBrush` + 上边框）。
- [x] **左树底部版本号**：`ShellViewModel.TreeVersionText` = 占位 **`1.26.10`**（与「设置 → 关于」的版本号**无关**）；折叠态靠 `Border.tree-panel.collapsed Border.tree-version` 隐藏。
- [x] **收藏夹按钮**：新增 `Button.fav-btn.accent`（透明底 + **强调色描边**，悬停 / 按下**强调色实心**），只用在 `FavoritesView` —— **首页「常用功能」那排按模块着色的按钮不受影响**。
- [x] **12 个列表页删除顶部状态行**（Row0 留空不动行号）；**7 个非列表页保留**（下一批接入契约）。
- [ ] **「命令生成」页**（视频工具重构第 2 步）—— **下一批**。
- [x] **非列表页接入**：设置 7 页（Data / LaunchConfig / PackageSource / General / Dns / Theme / Profile）+ 收藏夹 + HTTP 测试，各自实现 `IStatusProvider`（类声明 + `StatusChanged` + `OnStatusTextChanged`）；对应 View 的顶部状态行一并删除。
  （`About` / 「功能清单」本来就没有状态行，无需接。）

**v3.10 追加（📝 只写文档，代码下一批）**
- [ ] **「命令生成」页再设计**（需求 §6.4.1 / 规范 §3.9）：改「类播放器预览 + 三条时间条 ｜ 详情卡」两列；删抽帧缩略图墙、删工具条任务类型单选（只留「📂 选择视频 / ➡ 发送到批处理 / ↺ 区间重置」）；任务类型移入详情卡（剪辑·转码**可多选**）；剪辑 / 转码设置区**各自可折叠**；**命令可手动编辑**（`↺ 重置为自动`）；转码参数补足「格式 / 编码器(CPU·GPU) / 分辨率 / 码率」；视频列表送来**默认预览**。
- [ ] **命令生成函数**（一期）：新增 `Services/CommandRules.cs` 纯函数 —— `SnapResolution` / `SuggestBitRate` / `PickEncoder`（分辨率吸附、码率建议、CPU/GPU 编码器映射）。
- [ ] **同源多段剪辑**（一期）：多段 = 多条 `CutTask` 入队（**`CutTask` 结构不动**）。
- ⏳ **二期（评估已过，未排期）**：多段**合成单文件**（`-filter_complex` `trim`+`concat`）；**类 Excel 批量填充**（批量页表格式参数编辑）。

**v3.8 追加（✅ 两页都已落盘，待你本地 `build.ps1` 验证）**
- [x] **「格式转码」改名改造为「批量处理」**：新增 `VideoBatchViewModel` + `VideoBatchView.axaml(.cs)`；原 `TranscodeView.axaml` / `.axaml.cs` / `TranscodeViewModel.cs` **三文件已删**；菜单 key 由 `video-transcode` 改为 **`video-batch`**（标题「批量处理」）。
- [x] **列表 = 剪辑 + 转码混排**：用公共基类 `VideoTaskBase` 合并 `CutTasks` + `TranscodeTasks`（**没动 `VideoTaskStore` 的存储结构**）；给模型加 `KindDisplay` / `ParamDisplay` / `BeforeDisplay` 三列；运行按类型分派 `BuildCutArgs` / `BuildTranscodeArgs`。
- [x] **批量页直接采用 v3.7 三列布局**（工具条收进列表列、详情卡从顶到底）。
- [x] **视频列表页发送入口改向**：「➡ 发送到转码」→「➡ 批量处理·转码」（导航 `video-batch`）；「➡ 发送到剪切」→「✂ 命令生成·剪辑」（导航 `video-cut`）。
- [x] **「命令生成」页**（v3.9 落地）：`VideoCutView` / `VideoCutViewModel` **整体重写** —— 去掉列表与批量运行；**不再继承 `ListPageViewModelBase`**（改 `ObservableObject, IStatusProvider`，自己实现底栏契约）；**保留**关键帧预览 / 时间轴步进 / 帧标记这三大块；顶部加「剪辑 / 转码」类别切换 + 自带「📂 选择视频」；「➡ 发送到批量处理」按类型 `AddCut` / `AddTranscode`；从视频列表送来的 pending 只载入第一个。

**v3.7 追加（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **紧凑化**：标签项 Margin 2,4 / Padding 10,4、**标签字号 13px**（并删掉标签标题上的本地 `FontSize`，否则压住样式）；`✕` 22→20；`Border.toolbar` 内边距 24,10→**16,8**、`filter-bar` 24,6→**16,6**。
- [x] **左树 240 → 180px**（`ShellViewModel.TreePanelWidth`）。
- [x] **三列布局铺到 10 页**：`video-list`、`pc-package`、`pc-shortcuts`、`pc-quicksettings`、`pc-info`、`web-bookmark`、`web-ping`、`web-scrape`、`web-hosts`、`settings-path` —— 根 Grid 改 `ColumnDefinitions="*,Auto"`，状态行/工具条/筛选条收进左列，详情卡移到左列之外从顶到底。
- [~] **视频工具重构**（删 `video-transcode` / 加「批量处理」/ `video-cut` 改命令生成页）—— v3.7 只写需求；**v3.8 起落地**（见上方 v3.8 段）。
- ⏳ `video-cut` / `video-transcode` 的三列改造**故意没做**（下一批就要大改/删除）。

**v3.6 追加（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **撤销 v3.5 的手风琴**：`ApplyTreeAccordion` → `EnsureActiveGroupExpanded` —— 切标签**只确保当前页所属一级展开、⛔ 不收起其它**；展开态**恢复落盘** `treeGroup:<groupKey>`（`BuildTree` 读回；`TreeClick` 与"折叠全收"时写入）。
- [x] **标签栏两侧留白 16 → 24px**（中段 `ColumnDefinitions="24,*,24"`）。
- [x] **标签栏跟随滚动**：`ShellView.axaml.cs` 订阅 `ActiveTab` → `Dispatcher.Post` 后 `TabsControl.ContainerFromIndex(i).BringIntoView()`（**最小滚动**，类 Chrome）。
- [x] **关闭按钮不占位**：标签项模板改**单格 `Grid` 叠放**（标题按钮撑宽度 + `✕` `HorizontalAlignment=Right` 浮在上层、不参与布局宽度）；平时 `Opacity=0` + 不吃鼠标，悬停该标签才显形。

**v3.5 追加（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **删标题栏前进/后退**（`GoBack`/`GoForward` 命令一并移除；`IHistoryProvider` 契约与订阅**保留**）。
- [x] **标签栏两侧固定留白**：中段改 `ColumnDefinitions="16,*,16"`，左右各一个透明占位列（标签再多也挤不掉）。
- [x] **左树手风琴**：`ApplyTreeAccordion` —— 展开态切标签只展开当前页所属一级；折叠态不展开、一级图标显主题色（`Button.tree-row.active Path` → `AccentBrush`）；`ToggleTree` 折叠时收起全部一级；**展开态不再落盘**。
- [x] **多配置（新功能）**：`ConfigStoreService` 加 profile 层（`<应用根>\profiles\<名>\{config,data,logs}` + `active-profile.txt`）+ 旧结构一次性迁移（失败则留在旧结构并提示）；新增 `IProfileService`/`ProfileService`；新增「设置 → 配置」页（`settings-profile`：新增 / 切换 / 重命名 / 删除，**切换 = 确认后重启**）。
- [x] 连带：`AppConfigService.DefaultScriptFolder` 改用**应用根**（脚本目录多配置共享）；崩溃日志改写到**当前配置**的 `logs\`。

**v3.4 追加（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **标题栏精简**：删 `🏠` / `⚙` / `🌓` 三个按钮；底色改 `ContentBrush`（底部 1px 分割线）；标签项 / 窗口按钮配色切回**主题色系**。
- [x] **标签页**：去图标（与二级菜单同风格）、`✕` **悬停才显示**、中段左右留 16px 空隙供拖拽。
- [x] **左树**：一级**默认折叠**；一级 = 图标 + 加粗 / 二级 = 缩进 + 次要色；**二级激活时其父级一级同高亮**。
- [x] **标题栏图标放大到 18px**。
- [x] 移除 `GoHome` / `OpenSettings` / `ToggleTheme` 三个命令（对应按钮已删）。

**v3.3 追加（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **标签栏并入标题栏中段**：`ShellView.axaml` 行定义 `40,36,*` → **`40,*`**；标签区 `ScrollViewer(Hidden)`，右侧空白仍可拖窗。
- [x] **滚轮切标签**：`ShellViewModel.NextTab/PrevTab` + `ShellView.axaml.cs` 以 **Tunnel** 注册 `PointerWheelChanged`（否则被 ScrollViewer 当横向滚动吃掉）。
- [x] **一级菜单矢量图标**：`App.axaml` 的 `Application.Resources` + 5 个 `Icon*`；4 个模块 `RegisterModule` 改传几何键；`TreeMenuItem.VectorIcon` + `ShellViewModel.ResolveIcon`。
- [x] **二级菜单不显示图标**；**左树折叠只留一级图标**。
- [x] **连带修复**：`AllFunctionsViewModel/View` 的分组图标改矢量（否则显示成 `IconPc` 文本）；标签项配色改 `Sider*`（标签栏落在深色标题栏上）。

**v3.2 批次（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **Shell 层全量重写**：`ShellView.axaml(+.cs)`（标题栏 / 标签栏 / 左树 / 内容区 / 右详情五区）、`ShellViewModel.cs`（`Tabs`/`ActiveTab`/`OpenTab`/`CloseTab`/`Refresh`/`ToggleTree`/`ToggleDetail`）、新增 `TabItemViewModel.cs` + `Shell/Models/TreeMenuItem.cs`。
- [x] **Shared 层新增契约**：`IDetailPanelProvider.cs`（已定义 + Shell 已消费）、`IHistoryProvider.cs`（仅定义）。
- [x] **`App.axaml`**：新增全局样式（`title-btn` / `tab-item(.active)` / `tab-btn` / `tab-close` / `tree-row(.active)` / `tree-panel.collapsed`）；删除已废弃 `nav-btn`。
- [x] **共享视觉基线**：`Border.card` 圆角 10→8 / 内边距 18→14；`Border.detail-card` 圆角 10→8 / 内边距 16→14；`TextBlock.list-cell` 行高→32。
- [x] **`columnlayout.json` 壳级 Flag**（左树折叠态 + 分组展开态，页 Key `"shell"`）。
- [ ] **回归验证**（见需求附录 A.6）。

**本轮不做（下一版）**
- [ ] **列表页详情上提**（⏸ 本轮暂缓）：实际 **12 页**（v3.1 误写 5 个）；迁移方式见规范 §3.6 / §3.8。
- [ ] **图标矢量化**：约 34 个 `Icon*` Geometry + 模块 `icon` 字段换键——本轮**沿用 emoji**。
- [ ] **全局搜索框**：不实装（标题栏该位置留空为拖拽区）。
- [ ] **历史栈实导航**：`video-list` 目录切换进历史栈；前进 / 后退本轮**一律灰显**。
- [ ] **逐页间距 / 字号统一**（内容区 `20,16` 等）。

### P1 · 技术债
| # | 问题 | 建议 |
|---|---|---|
| 1 | 编译绑定逐项目关闭（23 View 无 `x:DataType`） | 二选一写进规范（B1） |
| 2 | 全局样式键用 `Classes` 隐式样式 | 收益有限暂挂（B3） |
| 3 | `NavigationService` 主流程闲置 | 删闲置服务或改用它（属框架层需同意，B4） |
| 4 | `BoolInversionConverter` 未使用 | 保留/删除均可（B8） |
| 5 | 仓库根残留旧 `NewToys.slnx` | 删除（B6） |

### P2 · 功能待办
1. **`web-http` 实装**：唯一剩下的骨架页。
2. **`pc-package` 收尾**：`PackageOutputParser` 实机验证；批量勾选+队列评估。
3. **`settings-package` 软件源**：需实机验证 `source list/add/remove/reset` 的退出码。
4. **`defaults\` 与脚本目录**：确认发布产物里 `defaults\**` 被复制；首次运行 `scripts\` 播种。
5. **模块热加载**：参照规范 §3.7 实装；v3.0 刷新点为重建左树。

### P3 · 待验证（本地 `build.ps1` 后逐项过）
1. **`Microsoft.Data.Sqlite 9.0.0` 能否还原**。
2. **旧单文件迁移**：`quickactions.json` → `pc-scripts.json`+`pc-quick.json`，旧文件应变成 `.migrated`。
3. **`data\newtoys.db` 生成**。
4. **视频库**：并发 ffprobe、二次扫描缓存命中（判据 = 大小相同且 mtime 差 ≤1 秒）。
5. **`deleted` → 回收站**：`SHFileOperationW` P/Invoke 编组。
6. **`settings-path` 列表页**：8 项路径浏览/保存/还原默认。
7. **`pc-package` 的 Pin 列**：`winget list` 的 Pin 状态解析。

---

## 四、关键文件索引

| 关注点 | 文件 |
|---|---|
| 启动/DI/模块装配/主题字典(32键)/系统明暗监听/图标 Geometry 定义 | `src/Shell/App.axaml.cs` + `src/Shell/App.axaml` |
| 主窗口（**v3.0 标题栏/标签栏/树/内容/右详情五区**） | `src/Shell/Views/ShellView.axaml(+cs)` |
| Shell 状态（**v3.0 Tabs/ActiveTab/OpenTab/CloseTab 等**） | `src/Shell/ViewModels/ShellViewModel.cs` |
| **标签页模型（v3.0 新增）** | `src/Shell/ViewModels/TabItemViewModel.cs` |
| **UI 契约（v3.0 新增）** | `src/Shared/Services/IDetailPanelProvider.cs` / `IHistoryProvider.cs` |
| 首页 | `src/Shell/Views/HomeView.axaml`+`ViewModels/HomeViewModel.cs` |
| 模块加载 | `src/Core/Services/ModuleLoader.cs` |
| 菜单服务 | `src/Core/Services/MenuService.cs` |
| 主题/强调色 | `Core/Services/ThemeService.cs`+`Shared/Services/AccentPalette.cs` |
| 配置存储 | `src/Core/Services/ConfigStoreService.cs` |
| **多配置（v3.5）** | `Core/Services/ProfileService.cs` + `Shared/Services/IProfileService.cs` + `Module.Settings/Views/ProfileView.axaml` |
| 列显隐 + 页面级开关 + **左树状态（v3.0）** | `Core/Services/ColumnLayoutService.cs`+`Shared/Services/IColumnLayoutService.cs` |
| 两页共用基类 + 运行 | `Core/Services/QuickActionService.cs`+`Shared/Models/QuickAction.cs`+`Module.PcTools/ViewModels/QuickActionListViewModel.cs` |
| **默认数据** | `src/Shell/defaults/*.default.json`+`defaults/scripts/*`（`None Update` 复制） |
| 本地数据库 | `Core/Services/SqliteDatabase.cs`+`Module.VideoTools/Services/VideoMediaIndex.cs` |
| 会话/运行钩子 | `Core/Services/DialogHooks.cs`+`Module.VideoTools/Services/VideoFileService.cs` |
| 视频库 | `Module.VideoTools/Views/VideoListView.axaml`+`ViewModels/VideoListViewModel.cs`+`Services/VideoScanner.cs` |
| 运行环境 | `Shared/Services/IAppConfigService.cs`+`Core/Services/AppConfigService.cs` |
| winget | `Core/Services/PackageManagerService.cs`+`PackageOutputParser.cs`+`PackageSettingsService.cs` |
| 加密 DNS | `src/Core/Services/EncryptedDnsService.cs` |
| 模块菜单注册（Key 权威源） | 各模块 `XxxModule.cs` |
| 构建属性/版本号/包版本 | `src/Directory.Build.props`/`src/Directory.Packages.props` |

---

## 五、变更流水（精简；逐次历史看 git）

> v3.0 起本节只记**里程碑级**变更。逐次提交内容一律 `git log`。

| 日期 | 版本 | 内容 |
|---|---|---|
| 2026-10-10 | v1.32 | 电脑工具两页分工 + 设置与脚本/快捷设置可运行 + 收藏夹简化 |
| 2026-10-10 | v1.33 | 列表页详情开合 + 收藏夹按钮墙 + 两页命令本地配置化（`defaults/`）+ 软件源直连系统 winget + 修 winget 中文表头解析 |
| 2026-10-10 | v1.34 | 引入 SQLite（`data\newtoys.db`）+ 视频库媒体信息（ffprobe 并发 + 缓存）+ 两页拆两个配置文件 + Pin 列 + 列表页统一 |
| 2026-10-11 | v1.35 | 全局样式表 + `ListPageViewModelBase`（列显隐/详情开合/排序/**多项筛选**）+ 通用设置四项系统级行为真正生效 + 首页常用功能按钮墙 + 系统信息页实装 + 视频剪切/转码实装 |
| 2026-10-11 | **v3.0** | **UI 从仿微信三栏重构为仿浏览器标签页 + 树状侧栏 + 视觉规范**（需求/规范/提示词/台账同步）——进行中 |
| 2026-10-11 | **v3.1** | **范围收敛**：本轮只做「Shell 外壳 + 全站视觉基线 + 列表页详情上提」，**功能不改、图标沿用 emoji**；搜索框 / 历史栈实导航 / 图标矢量化延后（需求/规范/提示词/台账同步） |
| 2026-10-11 | **v3.2** | **再收敛 + 事实更正**：详情上提本轮暂缓（实际 **12 页**，v3.1 误写 5 个）；**Shell 外壳重写（批次 1）已落盘**（含 `IDetailPanelProvider`/`IHistoryProvider` 契约、标签页/左树、共享样式视觉基线），待本地编译验证 |
| 2026-10-11 | **v3.3** | **布局微调 + 图标起步**：标签栏并入标题栏中段（+ 滚轮切标签）、一级菜单矢量图标、二级菜单去图标、折叠只留一级图标；文档起执行「新需求先入文档、再改代码」 |
| 2026-10-11 | **v3.4** | **标题栏精简 + 左树层级**：删 🏠/⚙/🌓 + 底色改主题背景；标签页去图标、✕ 悬停显示、两侧留空；左树一级默认折叠 + 层级样式区分 + 二级激活联动父级高亮；标题栏图标 18px |
| 2026-10-11 | **v3.5** | **左树手风琴 + 多配置**：删前进/后退；标签栏两侧固定留白；左树展开态只展开当前一级 / 折叠态一级显主题色 / 折叠即全收；新增「设置 → 配置」多配置（profiles 目录 + 切换重启）+ 旧数据自动迁移 |
| 2026-10-11 | **v3.6** | **标签栏体验 + 撤销手风琴**：切标签只确保当前一级展开（不收起其它）+ 展开态恢复落盘；标签栏两侧留白 24px、切换后自动滚到可见、关闭按钮改不占位（叠在文字上） |
| 2026-10-11 | **v3.7** | **紧凑化 + 三列布局 + 视频工具重构（需求）**：标签字号 13px / 标签项 10/4 / toolbar+filter-bar 内边距 16 + 左树 240→180px；列表页三列布局铺到 **10 页**；视频工具重构（删转码页/加批量处理页/剪辑改命令生成页）**只写需求、代码下一批** |
| 2026-10-11 | **v3.8** | **视频工具重构落地**：格式转码**改名改造**为「批量处理」（key `video-batch`，列表=剪辑+转码混排统一队列）；「视频剪辑」改「命令生成」（去列表、单任务卡、支持两类、自带选文件、发送到批量）；三处任务入口打通 |
| 2026-10-11 | **v3.9** | **底部状态栏 + 版本号区 + 收藏按钮**：Shell 统一承载底栏（`IStatusProvider`；列表页 12 + 非列表页 9 **全部接入**，删顶部状态行）；左树底部版本号占位 `1.26.10`；收藏夹按钮改描边 + 悬停实心 |
| 2026-10-11 | **v3.10** | **视频工具「命令生成」页再设计（📝 只写文档，代码下一批）**：改「类播放器预览 + 三条时间条 ｜ 详情卡」两列；删抽帧缩略图墙 + 工具条任务类型单选；任务类型移入详情卡（剪辑/转码可多选、两套设置可折叠）；**命令可编辑**；转码参数补足「格式/编码器(CPU·GPU)/分辨率/码率」；视频列表送来默认预览；**评估并纳入**「同源多段剪辑」与「命令生成函数（分辨率吸附等）」 |