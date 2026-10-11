# NewToys 项目现状与进度

> v3.11 · 2026-10-11 · 台账
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：开发需求（宪法）/ 开发规范 / AI 协作提示词

**最近三次变更**
- **v3.11**：📝 **文档重构（无功能改动）** —— 《开发需求》删版本流水与附录、需求融进功能章节、§4.0 布局总览按「左菜单栏 / 标题栏 / 主内容区 / 详情卡 / 状态栏」重写；评估内容（多段剪辑、命令生成函数）撤出文档改为会话讨论；台账只留最近三次。
- **v3.10**：📝 **视频工具「命令生成」页再设计（只写需求 / 规范，代码下一批）** —— 改「类播放器预览 + 三条时间条 ｜ 详情卡」两列；删抽帧缩略图墙 + 工具条任务类型单选；任务类型移入详情卡（剪辑 / 转码可多选、两套设置可折叠）；命令可手动编辑；转码参数补足「格式 / 编码器(CPU·GPU) / 分辨率 / 码率」；视频列表送来默认预览。
- **v3.9**：**底部状态栏 + 版本号区 + 收藏按钮**（代码已落盘，待本地 `build.ps1`）—— Shell 统一承载底栏（`IStatusProvider`，列表页 + 非列表页全部接入、删顶部状态行）；左菜单栏底部版本号占位 `1.26.10`；收藏夹按钮改「描边 + 悬停实心」。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.11 ｜ 规范 v3.11 ｜ 提示词 v3.9 ｜ 本台账 v3.11 |
| 技术栈 | .NET 10 + Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 7 项目 / 约 154 个 `.cs+.axaml+.csproj` / 约 19400 行 |
| 仓库根 | 能看到 `src/` 的那一级 |
| 版本控制 | ✅ git（分支 `main`） |
| 构建分工 | 🧑‍💻 构建 / 发布由用户本地执行（`pwsh -File .\scripts\build.ps1`）；AI 只改 `src/` |

**编译状态**：✅ v1.14 / v1.16 本地 0 错；⚠️ **v1.21 起（含 v3.x 全部批次）尚未由用户本地复跑** → 待跑 `build.ps1`。

**当前主线**：UI 从仿微信三栏重构为**仿浏览器标签页 + 树状左菜单栏**（v3.0 起）→ 视频工具三页重构（v3.8）→ 状态栏下沉（v3.9）→ **命令生成页再设计（v3.10/v3.11，代码待做）**。更早脉络看 `git log`。

---

## 二、已完成

### 2.1 架构与骨架
- **7 项目**：`Shared/Core/Shell` + 4 模块；分层单向。
- 中央包版本管理 + 版本号编译自动生成。
- DI 容器 + `IModule` 插件机制 + 菜单 / 导航 / 主题服务。
- **模块加载双轨**：Debug 显式类型清单 / Release 目录扫描。
- **运行期热加载（Modules + 刷新按钮）⏳ 待实装**（P2-5）。

### 2.2 UI
- 三栏 `60,140,*` → **标题栏 40px（含标签页）+ 左树 180 / 折叠 48 + 主内容区 + 右详情 340 + 底部状态栏 26px**。
- 明暗主题全局切换，键一律 `*Brush`（32 个），默认深色。
- **视觉规范**：共享样式已落地（卡片圆角 8 / 内边距 14、表格行高 32）；**逐页间距 / 字号统一**延后；**一级菜单矢量图标**已落地，标题栏 / 标签页图标仍为 emoji。

### 2.3 页面全景

| 模块 | 页面数 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 系统设置 | 10 | **10** | 0 |
| PC 工具箱 | 5 | **5** | 0 |
| Web 工具箱 | 5 | **4** | 1 |
| 视频工具箱 | 3 | **3** | 0 |
| **合计** | **23** | **22** | **1** |

> **唯一剩下的骨架** = Web 的 `web-http`（判据：VM 仅有 Title/Description/StatusText/IsBusy 与 PrimaryAction/Refresh 样板命令）。

### 2.4 菜单清单（24 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是首个固定标签页） |
| 电脑工具 | pc | 20 | `IconPc` | `pc-favorites`收藏夹/0/✅ ｜ `pc-shortcuts`设置与脚本/1/✅ ｜ `pc-quicksettings`快捷设置/2/✅ ｜ `pc-package`软件包/3/✅ ｜ `pc-info`系统信息/99/✅ |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP测试/1/🔧 ｜ `web-bookmark`书签/2/✅ ｜ `web-ping`网络Ping/3/✅ ｜ `web-scrape`网页抓取/4/✅ ｜ `web-hosts`Hosts管理/5/✅ |
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**命令生成**/1/✅ ｜ `video-batch`**批量处理**/2/✅ |
| 设置 | settings | 999 | `IconSettings` | `settings-general`通用/0/✅ ｜ `settings-theme`主题/1/✅ ｜ `settings-path`路径/2/✅ ｜ `settings-launch`一键按钮/3/✅ ｜ `settings-dns`加密DNS/4/✅ ｜ `settings-package`软件源/5/✅ ｜ `settings-profile`配置/6/✅ ｜ `settings-data`数据管理/7/✅ ｜ `settings-about`关于/8/✅ ｜ `settings-all`功能清单/9/✅ |

---

## 三、待办与优先级

> 本轮范围外的项（详情卡上提 / 搜索框 / 历史栈 / 图标矢量化）见**需求 §4.11**，此处不重复登记。

### P0 · 视频工具「命令生成」页再设计 🚧 进行中

**v3.11（📝 文档重构，无功能改动）**
- [x] **《开发需求》瘦身**：删版本变更流水与附录 A/B，需求变更**融进对应功能章节**；§4.0 布局总览按「左菜单栏 / 标题栏 / 主内容区 / 详情卡 / 状态栏」重写并明确各区职责。
- [x] **评估内容撤出文档**：「同源多段剪辑」「命令生成函数（分辨率吸附等）」从需求 / 规范删除，改在会话内讨论；未拍板前不落文档。
- [x] **台账只留最近三次**变更需求（v3.9 / v3.10 / v3.11）。

**v3.10（📝 只写需求 / 规范，代码下一批）**
- [ ] **「命令生成」页再设计**（需求 §6.4.1 / 规范 §3.9）：
  - 布局改「主内容区 = 类播放器预览（大画面 + **三条可拖时间条**：预览位置 / 开始 / 结束）｜ 右侧详情卡」；
  - ⛔ 删掉抽帧缩略图墙（`ItemsControl` + `FrameThumb` 一条）；
  - 顶部工具条**只留三项**：`📂 选择视频` / `➡ 发送到批处理` / `↺ 区间重置`（删「剪辑 / 转码」单选）；
  - **任务类型移入详情卡**，剪辑 / 转码**可多选**；两套设置区**各自可折叠**（`Expander`）；
  - **命令可手动编辑**（`CommandPreview` 与 `EditableCommand` 分离 + `↺ 重置为自动`）；
  - 转码参数补足「格式 / 编码器(CPU·GPU) / 分辨率 / 码率」；
  - 视频列表送来的文件 → **默认进入预览**（自动探测时长 + 抽当前帧）。

**v3.9（✅ 代码已落盘，待你本地 `build.ps1` 验证）**
- [x] **底部状态栏**：新增 `Shared/Services/IStatusProvider.cs`；`ListPageViewModelBase` **实现契约**并把 `StatusText` **提到基类**（各列表页 VM 里手写的 `_statusText` 已删）；`ShellViewModel` 切标签时探测并订阅；`ShellView.axaml` 在**内容区 + 详情区下方**加横贯状态栏（26px、`PanelBrush` + 上边框）。
- [x] **左树底部版本号**：`ShellViewModel.TreeVersionText` = 占位 **`1.26.10`**（与「设置 → 关于」的版本号**无关**）；折叠态靠 `Border.tree-panel.collapsed Border.tree-version` 隐藏。
- [x] **收藏夹按钮**：新增 `Button.fav-btn.accent`（透明底 + **强调色描边**，悬停 / 按下**强调色实心**），只用在 `FavoritesView` —— 首页「常用功能」那排按模块着色的按钮不受影响。
- [x] **各列表页删除顶部状态行**（Row0 留空不动行号）；**非列表页接入**契约：设置 7 页（Data / LaunchConfig / PackageSource / General / Dns / Theme / Profile）+ 收藏夹 + HTTP 测试。

### P1 · 技术债
| # | 问题 | 建议 |
|---|---|---|
| 1 | 编译绑定逐项目关闭（View 无 `x:DataType`） | 二选一写进规范（B1） |
| 2 | 全局样式键用 `Classes` 隐式样式 | 收益有限暂挂（B3） |
| 3 | `NavigationService` 主流程闲置 | 删闲置服务或改用它（属框架层需同意，B4） |
| 4 | `BoolInversionConverter` 未使用 | 保留 / 删除均可（B8） |
| 5 | 仓库根残留旧 `NewToys.slnx` | 删除（B6） |

### P2 · 功能待办
1. **`web-http` 实装**：唯一剩下的骨架页。
2. **`pc-package` 收尾**：`PackageOutputParser` 实机验证；批量勾选 + 队列评估。
3. **`settings-package` 软件源**：需实机验证 `source list/add/remove/reset` 的退出码。
4. **`defaults\` 与脚本目录**：确认发布产物里 `defaults\**` 被复制；首次运行 `scripts\` 播种。
5. **模块热加载**：参照规范 §3.7 实装；刷新点为重建左树。

### P3 · 待验证（本地 `build.ps1` 后逐项过）
1. **`Microsoft.Data.Sqlite 9.0.0` 能否还原**。
2. **旧单文件迁移**：`quickactions.json` → `pc-scripts.json` + `pc-quick.json`，旧文件应变成 `.migrated`。
3. **`data\newtoys.db` 生成**。
4. **视频工具**：并发 ffprobe、二次扫描缓存命中（判据 = 大小相同且 mtime 差 ≤1 秒）；`SHFileOperationW` 回收站编组；多段剪辑的命令正确性。
5. **`settings-path` 列表页**：8 项路径浏览 / 保存 / 还原默认。
6. **`pc-package` 的 Pin 列**：`winget list` 的 Pin 状态解析。

---

## 四、关键文件索引

| 关注点 | 文件 |
|---|---|
| 启动 / DI / 模块装配 / 主题字典(32键) / 系统明暗监听 / 图标 Geometry 定义 | `src/Shell/App.axaml.cs` + `src/Shell/App.axaml` |
| 主窗口（标题栏 / 标签栏 / 左树 / 内容区 / 右详情 / 状态栏） | `src/Shell/Views/ShellView.axaml(+cs)` |
| Shell 导航状态（Tabs / ActiveTab / OpenTab / CloseTab …） | `src/Shell/ViewModels/ShellViewModel.cs` |
| 标签页模型 | `src/Shell/ViewModels/TabItemViewModel.cs` |
| **UI 契约** | `src/Shared/Services/IDetailPanelProvider.cs` / `IHistoryProvider.cs` / `IStatusProvider.cs` |
| 首页 | `src/Shell/Views/HomeView.axaml` + `ViewModels/HomeViewModel.cs` |
| 模块加载 / 菜单服务 | `src/Core/Services/ModuleLoader.cs` / `MenuService.cs` |
| 主题 / 强调色 | `Core/Services/ThemeService.cs` + `Shared/Services/AccentPalette.cs` |
| 配置存储 / 多配置 | `src/Core/Services/ConfigStoreService.cs` + `ProfileService.cs` + `Shared/Services/IProfileService.cs` |
| 列显隐 + 页面级开关 + 左树状态 | `Core/Services/ColumnLayoutService.cs` + `Shared/Services/IColumnLayoutService.cs` |
| 两页共用基类 + 运行 | `Core/Services/QuickActionService.cs` + `Shared/Models/QuickAction.cs` + `Module.PcTools/ViewModels/QuickActionListViewModel.cs` |
| 默认数据 | `src/Shell/defaults/*.default.json` + `defaults/scripts/*`（`None Update` 复制） |
| 本地数据库 | `Core/Services/SqliteDatabase.cs` + `Module.VideoTools/Services/VideoMediaIndex.cs` |
| 会话 / 运行钩子 | `Core/Services/DialogHooks.cs` + `Module.VideoTools/Services/VideoFileService.cs` |
| 视频工具 | `Module.VideoTools/Views/{VideoList,VideoCut,VideoBatch}View.axaml` + `Services/{VideoScanner,FfmpegService,VideoTaskStore}.cs` |
| 运行环境 | `Shared/Services/IAppConfigService.cs` + `Core/Services/AppConfigService.cs` |
| winget | `Core/Services/PackageManagerService.cs` + `PackageOutputParser.cs` + `PackageSettingsService.cs` |
| 加密 DNS | `src/Core/Services/EncryptedDnsService.cs` |
| 模块菜单注册（Key 权威源） | 各模块 `XxxModule.cs` |

---

## 五、变更流水（只留最近三次；逐次历史看 git）

| 日期 | 版本 | 内容 |
|---|---|---|
| 2026-10-11 | **v3.9** | **底部状态栏 + 版本号区 + 收藏按钮**：Shell 统一承载底栏（`IStatusProvider` 全量接入、删顶部状态行）；左树底部版本号占位 `1.26.10`；收藏夹按钮改描边 + 悬停实心 |
| 2026-10-11 | **v3.10** | **视频工具「命令生成」页再设计（只写文档）**：改「类播放器预览 + 三条时间条 ｜ 详情卡」两列；删缩略图墙 + 工具条任务类型单选；任务类型入详情卡（可多选 / 可折叠）；命令可编辑；转码参数补足四组；默认预览 |
| 2026-10-11 | **v3.11** | **文档瘦身（无功能改动）**：《开发需求》删版本流水与附录、变更融进功能章节、布局总览按五区职责重写；评估内容撤出文档改为会话讨论；台账只留最近三次 |