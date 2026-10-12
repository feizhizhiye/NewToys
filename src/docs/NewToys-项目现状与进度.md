# NewToys 项目现状与进度

> v3.19 · 2026-10-12 · 台账
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：开发需求（宪法）/ 开发规范 / AI 协作提示词 / AGENTS.md（跨 AI 入口）/ skill/（跨 AI 知识库）

**最近三次变更**
- **v3.17.2**：🗂 **台账瘦身** —— §三 旧版本(v3.12–v3.14)完整 `[x]` 验收清单折叠为一行式摘要（v3.12 密度基线已入规范 §2.1、v3.13/v3.14 已本地编译通过），保留 v3.15/v3.16 完整；最高频读取的台账减重约 25 行，跨 AI 每次状态加载更省 token。
- **v3.17.1**：📂 **AI 协作体系重构为「中立 skill/ + 双客户端」** —— 仓库根新增中立 `skill/` 目录（`SKILL.md` 路由表 + `references/` 下 `collab-method.md`(通用方法论)+`newtoys-spec.md`(NewToys 专属约束) 深度约束 + `scripts/check.ps1` 静态自检），作为 **WorkBuddy 与 Hermes(DeepSeek Harness) 共享知识库**；`AGENTS.md` 退为路由入口（指向 skill/）；`.workbuddy/skills/newtoys/` 改为**薄指针**指向 `skill/`；`src/docs` 四份仍唯一权威。跨 AI 知识全部进仓库，私有记忆仅留进行态。
- **v3.18**：🗂 **待办体系拆分（规划 / 执行双模式）** —— 新增 `src/docs/backlog/`（NT-xxx 明细 + README 格式与双模式/草稿闸门/批量协议）；台账三、退化为「待执行待办索引」（NT-id/标题/优先级/状态/链接），细节全进 backlog；`skill/references/collab-method.md` §二 与 `AI协作提示词.md` 改双模式：规划期只写文档（📝 草稿不执行）、执行期显式「执行全部就绪待办」批量跑（⛔ 不用 automation）。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.19 ｜ 规范 v3.13 ｜ 提示词 v3.13 ｜ 本台账 v3.19 |
| 技术栈 | .NET 10 + Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 7 项目 / 约 154 个 `.cs+.axaml+.csproj` / 约 19400 行 |
| 仓库根 | 能看到 `src/` 的那一级 |
| 版本控制 | ✅ git（分支 `main`） |
| 构建分工 | 🧑‍💻 构建 / 发布由用户本地执行（`pwsh -File .\scripts\build.ps1`）；AI 只改 `src/` |

**编译状态**：✅ v1.14 / v1.16 本地 0 错；⚠️ v1.21~v3.12 尚未由用户本地复跑 → 待跑；**v3.13 / v3.14 已于 2026-10-12 本地编译通过 ✅**。

**当前主线**：UI 从仿微信三栏重构为**仿浏览器标签页 + 树状左菜单栏**（v3.0 起）→ 视频工具三页重构（v3.8）→ 状态栏下沉（v3.9）→ **视频预览页 + 两页详情卡统一 + 密度收紧（v3.12）**。更早脉络看 `git log`。

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
- **视觉规范**：共享样式已按 **v3.12 密度基线**收紧（表格行高 28、卡片内边距 12、工具条 12,6、筛选条 12,4）；**一级菜单矢量图标**已落地，标题栏 / 标签页图标仍为 emoji。

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
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**视频预览**/1/✅ ｜ `video-batch`**批量处理**/2/✅ |
| 设置 | settings | 999 | `IconSettings` | `settings-general`通用/0/✅ ｜ `settings-theme`主题/1/✅ ｜ `settings-path`路径/2/✅ ｜ `settings-launch`一键按钮/3/✅ ｜ `settings-dns`加密DNS/4/✅ ｜ `settings-package`软件源/5/✅ ｜ `settings-profile`配置/6/✅ ｜ `settings-data`数据管理/7/✅ ｜ `settings-about`关于/8/✅ ｜ `settings-all`功能清单/9/✅ |

---

## 三、待办与优先级

> 本轮范围外的项（详情卡上提 / 搜索框 / 历史栈 / 图标矢量化）见**需求 §4.11**，此处不重复登记。
> **待办来源（准备期产出）**：每次新需求，由 AI 对比《开发需求》应然 vs 本节「一、基线」生成；逐条可验收、带分级（P0/P1…）。
> 流程定义见 `skill/references/collab-method.md` §二（**规划 / 执行双模式 + 草稿闸门 + 批量协议**）；本表 = **待办项**（紧凑），细节统一在 `src/docs/backlog/NT-xxx.md`。
> 生命周期：📝 草稿 → ✅ 就绪 → 🔄 执行中 → ✔ 已完成（待验收）；验收后台账待办项翻 ✔、细节文件按 `backlog/README.md` 清理（删 / 留最近 3）。台账只翻状态，不随细节删。

### 待执行待办（backlog 索引）
> 详细规格与验收清单见 `src/docs/backlog/NT-xxx.md`；**执行模式只跑其中 `✅ 就绪` 项**（`📝 草稿` 不执行）。
> 睡前开会话发「**执行全部就绪待办**」即可让 agent 批量跑（⛔ 不用 automation 定时触发）。

| NT | 标题 | 优先级 | 状态 | 详情 |
|---|---|---|---|---|
| NT-001 | web-http 实装（唯一剩余骨架页） | P2 | 📝 草稿 | backlog/NT-001.md |
| NT-002 | pc-package 收尾（Parser 实机 + 批量队列） | P2 | 📝 草稿 | backlog/NT-002.md |
| NT-003 | settings-package 软件源（退出码验证） | P2 | 📝 草稿 | backlog/NT-003.md |
| NT-004 | defaults\ 与脚本目录播种确认 | P2 | 📝 草稿 | backlog/NT-004.md |
| NT-005 | 模块热加载实装（规范 §3.7） | P2 | 📝 草稿 | backlog/NT-005.md |
| NT-006 | 快捷设置：运行改造（可见 pwsh+回显 / GUI 隐藏 / 切换 shell）+ 导入导出 / 清除缓存 | P1 | ✔ 已完成（待验收） | backlog/NT-006.md |
| NT-007 | 设置与脚本：导入导出 + 右键编辑 / 打开方式 + 运行如双击 | P1 | ✔ 已完成（待验收） | backlog/NT-007.md |
| NT-008 | 软件包：可升级对齐 winget update + Pin 显示 + 已装移状态栏 + 详情 bug + 已装导入导出 | P1 | 📝 草稿 | backlog/NT-008.md |
| NT-009 | 首页：收藏配色 / 一键操作固定底部 / 删工具箱行 / 状态栏模块功能数 | P2 | 📝 草稿 | backlog/NT-009.md |
| NT-010 | 通用样式统一：激活强调色加深 / 按钮配色 / 导入导出控件一致 / 输入框水印 / 弹层样式 | P1 | ✔ 已完成（待验收） | backlog/NT-010.md |
| NT-011 | 视频列表：删扫描按钮 / 数千视频优化 / 入库去重 / 删清空媒体缓存 | P1 | ✔ 已完成（待验收） | backlog/NT-011.md |
| NT-012 | 视频预览：旋转播放按钮 + 命令卡旋转 + 按钮标题去后缀 | P2 | 📝 草稿 | backlog/NT-012.md |
| NT-013 | ffmpeg 卡：剪辑转码输入对齐 + 转码默认 H.265/CRF23/_H265/默认宽高 | P2 | 📝 草稿 | backlog/NT-013.md |

> 上表均为 📝 草稿：讨论可继续追加细节，升 `✅ 就绪` 后才会被批量执行。已落盘待编译的项见下方 P0 v3.15 / v3.16。

### P0 · 视频工具两页详情卡统一 + 全局密度收紧（v3.12）✅ 已落盘
> v3.12 完整验收清单已折叠：跨页投递改 `OnAttachedToVisualTree` 取件（修"刷新才生效"）；「命令生成」更名「视频预览」(菜单 key 留 `video-cut`)；两页详情卡统一(分区 剪辑→转码→输出→命令、勾选框进 `Expander.Header`、详情卡零操作按钮)；详情开关移工具条末位 `Button.detail-toggle`；全局密度收紧(行 28/表头 32/卡片 12，基线见规范 §2.1)。逐项见 `git log` / 变更流水。

### P0 · v3.13 视频工具精细化 ✅ 代码已落盘 + 本地编译通过
> v3.13 完整验收清单已折叠：预览页 UI 重构(三条时间条常显 + 类播放器按钮 + 预览区 MinHeight 340)；两页详情卡对齐(宽 340 / `ComboBox` 不挤出)；批量→预览反向发送；`VideoCatalogService`(表 `video_catalog`) + 批量任务导入导出。逐项见 `git log` / 变更流水。

### P0 · v3.14 视频列表「📂 导入」拆分按钮（合并文件夹 / 视频文件 / 视频库）✅ 代码已落盘 + 本地编译通过
> v3.14 完整验收清单已折叠：工具条「📂 导入」改 `SplitButton`(文件夹 / 视频文件 / 视频库.db 三项)；`ImportVideoFiles` 由路径建 `VideoItem` 追加去重；已本地 `build.ps1` 验证通过。逐项见 `git log` / 变更流水。

### P0 · v3.15 视频工具增强（默认目录 / 标签 / 改名 / 表头 / 收藏）✅ 代码已写完，待本地 `build.ps1` 验证

**v3.15**
- [x] **设置：导入导出默认目录（全局）** —— `IAppConfigService.VideoDbFolder` + `settings-path` 新增「导入导出默认目录」(`PathItemKind.Folder`)；`VideoListView` 的 `.db` 选择器与 `VideoBatchView` 的 `.json` 导入 / 导出都用 `SuggestedStartAsync()`（`TryGetFolderFromPathAsync` → `SuggestedStartLocation`）。
- [x] **视频标签** —— `video_catalog` 加 `tags` 列（`Upsert` 写、`ReadFile` 读）；`VideoCatalogService` 新增 `UpdateTags` / `GetTags` / `HasPath`；视频列表详情卡「标签」可编辑（`EditTags` + `SaveTagsCommand`，回车 / 失焦提交）；表头「标签」列；筛选走 `FieldText("Tags")`。⛔ 未入视频库的项提示先「💾 加入视频库」，不静默丢。
- [x] **详情卡内改名 + 同步磁盘** —— 新增 `VideoFileService.RenameToAsync`（不弹框版，防穿越 / 同名检查同 `RenameAsync`，返回新全路径）；详情卡「文件名」可编辑（`EditName` + `SaveNameCommand`）→ 更新 `VideoItem.Name/FullPath` + `catalog.MovePath` + `fav.MovePath`；⛔ 只重渲染列表，**不整目录重扫**（保住勾选与已读媒体信息）。
- [x] **表头扩充** —— 视频列表 7 列（标签 / 帧率 / 容器格式 / 音频编码 / 路径 / 创建时间 / 已收藏），批量列表 3 列（源文件 / 命令摘要 / 创建时间）；均复用 `ListPageViewModelBase` 列显隐 + `columnlayout.json`。新增模型字段：`VideoItem.Fps/ContainerFormat/AudioCodec/Created`、`VideoTaskBase.Created/CommandSummaryDisplay`。⛔ `Container`/`Audio`/`Path`/`Command` 列键与模型属性名不一致 → 已显式写进 `FieldText`，不能靠基类反射兜底。
- [x] **预览 / 批量页详情卡统一复查** —— 已比对两页详情卡控件构成。
  - ⚠️ **发现漂移 1 处（转 P1 #6）**：预览页两个 `Expander.Header` 是「勾选框 + 标题」（需求 §6.4.1 第 2 条），**批量页只有标题、没有勾选框**；批量页要补就得先给 `VideoBatchViewModel` 加"该段是否参与命令"的可写标志（与 §6.4.1 第 5 条"批量页类型由选中任务决定"存在语义张力）→ **停手待用户定夺**，未擅自改。命令区（最后一块）两页结构一致，无漂移。
- [x] **视频预览页「📸 取当前帧」** —— 工具条新增按钮 → `FfmpegService.ExtractFrameAsync` 抽当前帧 → 落 `data\thumbs\{sha1(path)}.jpg`（先写 `.tmp.jpg` 再替换，避开收藏页 Bitmap 占用）→ `_fav.Add` + `VideoTaskStore.EnqueueFrame`。
  - ⚠️ **与需求 §6.4.2 的一处偏差**：需求写"经任务通道**送**视频收藏页建卡"，实现为**登记后不自动跳转**（用户正在拖着看画面，拽走体验差）；卡片由收藏页在 `OnAttachedToVisualTree` 取件时建好（规范 §4.8 硬规则 8）。若想改成"抽完立刻跳到收藏页"，改一行 `NavigateTo("video-fav")` 即可。
- [x] **视频收藏页（新）** —— `video-fav`；`VideoFavoritesService`（表 `video_favorites(path,thumb,added_at)`，缩略图磁盘 `data\thumbs`，含 `MovePath` 跟住改名）；`VideoFavoritesView` = `ItemsControl` + `WrapPanel` 卡片网格（⛔ 不是列表页，不继承 `ListPageViewModelBase`）；详情卡与视频列表同套字段（含标签 / 文件名编辑 / 💾入视频库）；视频列表工具条「★ 收藏」为唯一来源（要求已在 `video_catalog` 里）。
- ⚠️ **验收点**：① 视频库 `.db` **与**批量任务 `.json` 的导入/导出对话框启动即定位到设置的默认目录；② 打标签后详情卡 / 表头 / 收藏页三处一致；③ 详情卡改名磁盘文件同步、视频库路径同步、不重扫；④ 表头右键能显隐新增列；⑤ 预览取帧 → 收藏页出现带缩略图卡片；⑥ 收藏页详情卡与视频列表详情卡同结构。
- ⚠️ **全部改动只做了静态自查，尚未跑 `build.ps1`**（编译由用户本地验证）。

### P0 · v3.16 视频工具六项（导入独立 / 状态栏 / 详情卡一致 / 表头 ☑ / 清除）🚧 代码已落盘，待本地 `build.ps1`

**v3.16**（用户本轮提的 6 条，逐条可验收）
- [x] **①「🗄 导入视频库(.db)」独立按钮**（需求 §6.4.3）—— 从「📂 导入」的 `SplitButton` 下拉里拿掉第三项；工具条新增独立按钮（点即弹选择器、`SuggestedStartLocation` = 「导入导出默认目录」、⛔ 无输入框）；code-behind `OnPickCatalogDb` 改挂到 `ImportCatalogBtn`；**文件夹输入框 `FolderBox` 保留**（用户明确要留）。
- [x] **② 修 bug「导入视频库后列表没反应」** —— `VideoCatalogService.ReadFile` 返回 `CatalogReadResult`（`Items` + `Error`）：无 `video_catalog` 表时报"该文件里的表：…"、`PRAGMA table_info` 拿实际列缺列用 `NULL AS xxx` 顶上（⛔ 不再因缺 `tags` 整表读不出来）、打开/查询异常回传 SQLite 原文；构造期 `EnsureColumn("tags"/"modified")` 给老库补列；`Upsert` 写 `modified`、`ReadFile` 读它（⛔ 不再一律 `DateTime.Now`），顺带按路径补 `Extension`；`FilePicker` 取不到本地路径 / 没选文件也给提示（⛔ 不许静默 return）。
- [x] **③ 视频预览页状态栏常驻当前视频信息**（需求 §6.4.2）—— `StatusText` 改手写 setter：写入动作消息、读出「🎬 文件名 · 时长 · 完整路径 · 消息」；未载入时不加前缀；换文件 / 探测到时长后 `RefreshStatusPrefix()` 刷新；"已载入"那条消息去掉与前缀重复的冗余文案。
- [x] **④ 两页详情卡完全一致**（需求 §6.4.1，P1 #6 由此关闭）—— 批量页两 `Expander` 改 `IsVisible="{Binding HasSelection}"` **常显**、标题行补 `CheckBox`（`EditIsCut` / `EditIsTranscode`，**可交互**）；`ApplyTaskKind()` 按勾选组合切任务类型（`VideoTaskStore.Replace` 保队列位置，`_converting` 防递归，两段都不勾 → 强制保留转码）；`TranscodeTask.UseTrim` 新增（`HasTrim = UseTrim || TrimEnd > TrimStart`，兼容老数据），`Save` 里区间以勾选框为准写 `UseTrim`；`FfmpegService.BuildTranscodeArgs` 终点留空时不再拼负的 `-to`；转码段内重复的「剪辑区间」整块删除、`最大高度` 移到同位置，预览页补 `EditMaxHeight` 字段（两页字段集合一致）。
- [x] **⑤ 表头 ☑ 的勾选菜单**（需求 §6.4.10）—— 两页表头 ☑ 各挂 `ContextMenu`（全选 / 取消选择 / 反选），code-behind `OnDataContextChanged` 同步它的 DataContext（⛔ 弹出树不继承）；视频列表删掉工具条「☑ 全选 / 🔄 反选 / ☐ 清空」；批量页新增行首勾选列（`VideoTaskBase.IsChecked` + `Columns` 首项 `Pick("34")` + `IsPickVisible`，表头/行模板列索引 0~9 → 1~10）。
- [x] **⑥ 清除列表 / 清除缓存**（需求 §6.4.11）—— 视频列表 `ClearListAsync`（清 `_all`/`Videos`/筛选/搜索/选中，二次确认，⛔ 不动磁盘与库）；批量页 `ClearCacheAsync`（`VideoTaskStore.ClearCache()` 清两类，**⛔ 不落盘**，运行中先提示停止，二次确认）；两页工具条各一个按钮。
  - **v3.16.1 更名改语义**（用户提）：按钮从「🧹 清除队列」改「**🧹 清除缓存**」—— 只不再显示这些任务，`video-tasks.json` **原样留着**，与旁边「清理已结束」区分开。
  - **v3.16.1 启动不自动载入**：`VideoTaskStore` 构造**不再 `Load()`**（json 只是缓存 / 备份，不是工作现场），批处理页刚打开就是空白；原来的 `Load` 改成 public `LoadFromFile()` 留着手动调用（需求 §6.4.4）。
- [x] **⑦ 预览页三条时间条再收紧**（v3.16.1）—— `Spacing 2→0`、每条 `Slider` `Height=22`+`Padding=0`（Fluent 默认留白太大）、组上方外边距 `6→4`，纵向空间还给预览画面。
- ⚠️ **验收点**：① 点「🗄 导入视频库(.db)」直接弹选择器且定位到默认目录；② 挑一个**非视频库的 .db**（或老库）导入 → 状态栏给出**具体原因**而不是没反应；③ 预览页随便点几个按钮，状态栏始终能看出当前是哪个文件；④ 批量页详情卡勾/取消剪辑、转码 → 任务类型随之变化且不崩；⑤ 表头 ☑ 右键三项都生效；⑥ 清除按钮二次确认后列表/队列清空、文件没删。
- ⚠️ **全部改动只做了静态自查，尚未跑 `build.ps1`**（编译由用户本地验证）。

### P1 · 技术债
| # | 问题 | 建议 |
|---|---|---|
| 1 | 编译绑定逐项目关闭（View 无 `x:DataType`） | 二选一写进规范（B1） |
| 2 | 全局样式键用 `Classes` 隐式样式 | 收益有限暂挂（B3） |
| 3 | `NavigationService` 主流程闲置 | 删闲置服务或改用它（属框架层需同意，B4） |
| 4 | `BoolInversionConverter` 未使用 | 保留 / 删除均可（B8） |
| 5 | 仓库根残留旧 `NewToys.slnx` | 删除（B6） |
| 6 | ~~v3.15 复查发现：批量页详情卡两个分区标题行缺勾选框~~ **v3.16 已解决**：用户拍板"勾选框可交互、勾即改任务类型"，已落地（`EditIsCut` / `EditIsTranscode` + `VideoTaskStore.Replace`） |

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
| 视频工具 | `Module.VideoTools/Views/{VideoList,VideoPreview,VideoBatch}View.axaml` + `Services/{VideoScanner,FfmpegService,VideoTaskStore}.cs` |
| 运行环境 | `Shared/Services/IAppConfigService.cs` + `Core/Services/AppConfigService.cs` |
| winget | `Core/Services/PackageManagerService.cs` + `PackageOutputParser.cs` + `PackageSettingsService.cs` |
| 加密 DNS | `src/Core/Services/EncryptedDnsService.cs` |
| 模块菜单注册（Key 权威源） | 各模块 `XxxModule.cs` |

---

## 五、变更流水（只留最近三次；逐次历史看 git）

| 日期 | 版本 | 内容 |
| 2026-10-12 | **v3.19** | **需求大批量整理（规划期）**：合并「运行可见终端 / GUI 不弹窗 / 切换 shell」(R0) 与本轮 23 条需求，写进《开发需求》§6.1/§6.2.1~6.2.3/§4.10.4/§6.4.12~6.4.14；拆 NT-006~013 待办（📝 草稿）；未碰代码，待「执行 NT-xxx」落地 ||---|---|---|
| 2026-10-12 | **v3.17.2** | **台账瘦身**：§三 旧版本(v3.12–v3.14)完整 `[x]` 验收清单折叠为一行式摘要（v3.12 密度基线已入规范 §2.1、v3.13/v3.14 已本地编译通过），保留 v3.15/v3.16 完整清单；最高频读取的台账减重约 25 行，跨 AI 每次状态加载更省 token |
| 2026-10-12 | **v3.17.1** | **AI 协作体系重构为中立 skill/ + 双客户端**：仓库根新增 `skill/`（`SKILL.md` 路由表 + `references/` 下 `collab-method.md`(通用)+`newtoys-spec.md`(专属) 约束 + `scripts/check.ps1` 静态自检），WorkBuddy 与 Hermes(DeepSeek Harness) 共享；`AGENTS.md` 指向 skill/；`.workbuddy/skills/newtoys/` 改薄指针；跨 AI 知识全进仓库，私有记忆仅留进行态 |
| 2026-10-12 | **v3.17** | **文档体系新增 AI 协作双入口**：仓库根 `AGENTS.md`（跨 AI 开放标准，Codex/Cursor/Devin/Jules 等自动读；内容=定位 + 8 条铁律 + 四份文档路由表 + 分级读档 + 任务型→规范章节表 + 高频硬约束 + 交付要求）+ `.workbuddy/skills/newtoys/` 项目级技能（WorkBuddy 自动触发，与 AGENTS.md 同源）；`src/docs` 四份一字未改、继续为唯一权威源，入口仅摘要并声明「冲突以 src/docs 与 src/ 为准」；需求 §10 登记（含「入口例外」条款）；`.gitignore` 放行 `.workbuddy/skills/` 使技能随仓库分发 |