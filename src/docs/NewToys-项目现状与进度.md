# NewToys 项目现状与进度

> v3.30 · 2026-10-14 · 台账
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：开发需求（宪法）/ 开发规范 / AI 协作提示词 / AGENTS.md（跨 AI 入口）/ skill/（跨 AI 知识库）

**最近三次变更**
- **v3.30**：🛠 **NT-016 工具条收口（⚠️ 待本地编译 + 实机验收）** —— ① **扫描入口合并**：「📁 扫描文件夹」+「📄 扫描文件」两个独立按钮 → 单条 **SplitButton「📁 扫描」**（主钮 = 选文件夹 / 下拉 = 「📁 文件夹…」·「📄 文件…」），样式对齐视频列表「📂 导入」（`SplitButton.ghost`）；接线上 `ScanSplit.Click` + 两个 `MenuItem.Click`。🔴 **更正旧偏离**：此前文档写"SplitButton 主按钮无 Click 故拆两按钮"是**错的**（VideoTools 一直用 `ImportSplit.Click`），该偏离作废。② **标题栏 ⟳ tooltip** 改「重建当前页（丢掉筛选 / 排序 / 未保存的编辑，回到刚打开时的状态）」—— 只改 tooltip，行为不变（两级：页面「🔄 刷新」= 重读数据库 / 标题栏 ⟳ = 重建页面）。③ **NT-026 按用户定调重写**：库的合并 = **打开库 A → 再打开库 B → 💾 保存**（工作副本模型）；"**减**" = 勾选行后删除；🔴 原「加 `assets.origin` 列 + 按来源拆库」方案**作废**。需求 §6.6 补「工具条组成」+「多库融合」两条（需求 v3.28）。
- **v3.29**：🆕 **执行 NT-014（模块骨架）+ NT-015（数据层）（⚠️ 待本地编译 + 实机验收）** —— **NT-014**：新建 `Module.MediaAssets`（csproj + `MediaAssetsModule.cs`）+ **三处登记**（slnx / Shell.csproj / App 必载清单）+ 一级分组 `assets`（「媒体资产」🗂，Order 50）+ **12 个二级项 / 12 组空页面**（View+VM 成对，`ObservableObject + IStatusProvider`）。页面数 23 → **35**（骨架 13）。**NT-015**：落 `data\newtoys.db`（与视频工具共库、表级隔离）的 **13 表 + 10 索引**（`AssetDatabase` 幂等建表 + 老库补 10 个可空列）+ `AssetService`（CRUD + **四级匹配**：相对路径 → quick_hash+size → inode+device → 完整 hash+size）+ `ScanService`（手写目录遍历 + 忽略规则 + 五级匹配入库 + 收尾标 missing，**⛔ 不删 Asset**）+ `TaskQueue`（串行异步队列 + 取消 + 进度）；模型 `Storage` → **`StorageSource`**（`Storage` 撞 Avalonia 的 `Avalonia.Platform.Storage`）。自查修掉 3 处缺陷：`UpsertStorage` 更新分支 `ExecuteScalar` 把 id 覆盖成 0 / `Cancel` 误杀在跑任务 / `TaskItem` 非可观测导致进度不刷新。**NT-016**：`assets-all` 落成完整列表页（工具条 + 筛选条 + 表头 + 空态示例行 + 列表，8 列）+ `AssetDetailView`；`ColumnGrid` 出**第五份副本**；`AssetRow` 行模型 + `AssetService.QueryRows`（恒定 4 次查询，⛔ 不 N+1）+ `ScanService.IndexFilesAsync`。两处有意偏离文档并在代码注明：① **详情面板改由 Shell 承载**（`IDetailPanelProvider`）→ 页面内 ⛔ 不再画右栏（否则两个详情卡）；② （⚠️ v3.30 已撤回此偏离）曾把导入拆成两个独立按钮而非 SplitButton（当时误判"SplitButton 主按钮无 Click"——实则 `VideoTools` 一直用 `ImportSplit.Click`）
  - **复修（用户本地编译 + 实机验收后，⚠️ 待复验）**：① **NT-016 扫描性能重写** —— 原实现对每个未匹配文件算**完整内容哈希**（整文件读）→ 首次扫描几分钟不动 + 硬盘狂响 + 状态栏卡死。改为：零 IO 优先（相对路径 → size+mtime）、quick_hash 加守卫（仅当库里同大小位置带 quick_hash 才算 → 首次扫描**完全不读内容**）、新文件不算完整哈希、匹配全在内存 `StorageIndex` 做（4 次查询而非 4 万次）、批量事务落库 + 批量 touch、进度改按 200ms 节流；另加**手输 / 粘贴路径 + 回车执行**。② **NT-025 验收后缺陷**：`Win+←/→` 贴靠后左右下有缝隙 —— 读 Avalonia 源码定论，`WM_NCCALCSIZE` 在扩展客户区且样式带 `WS_CAPTION` 时会把边框厚度**保留在客户区外**，贴靠后 DWM 又不画这圈框；首轮"贴靠后按实测厚度外撑外框"**无效**，第二轮改为**接管 `WM_NCCALCSIZE`**（换窗口过程，非最大化时客户区拉满整窗）+ bounds 存取改用外框（⛔ 不动 `WS_THICKFRAME`，以免丢贴靠资格）
  - **第二轮（同日，⚠️ 待复验）**：① **NT-016 改「预览式扫描」**（用户定调：整体逻辑与视频列表一致）——📁/📄 扫描**只进内存**（草稿态，⛔ 不写库）→「💾 加入库」（事务批量落库）/「🧹 清除列表」（换文件夹重扫）；「➕ 登记文件夹」攒待扫列表 +「🔄 扫描全部」直接入库；新增 ☑ 含子目录（默认开）；「🔄 刷新」明示"重读数据库、⛔ 不扫盘"。② **贴靠缝改走根治**：首轮"事后外撑"实测无效 → 改为**接管 `WM_NCCALCSIZE`**（换窗口过程，非最大化时客户区拉满整窗）+ bounds 存取改用**外框**（否则每次启动窗口变大 16px）；参照 Watt Toolkit 的做法是 fork 整个 Avalonia，我们不 fork
  - **第三轮（凌晨，⚠️ 待复验）**：① 「🧹 清除列表」改**任何状态可用**（库视图置灰被当成坏了）：草稿态=丢弃预览、库视图=只清显示；② 新增「📂 打开库文件」（系统默认程序开 newtoys.db）；③ **容器间距规则**：WrapPanel 必须 `ItemSpacing=8`+`LineSpacing=4` 且⛔再给元素写右 Margin（写进需求 §4.10.4，全项目适用）；④ 「批量加标签/集合」改名「加标签/加集合」（有勾选框后勾几个操作几个，NT-018 落地勾选框列）；⑤ 详情卡规则改写：允许**编辑+保存+「↗ 应用到文件」**（NT-018/019 已按此增补，覆盖旧"零操作按钮"）；⑥ **打开类动作归右键**（用户定调）：工具条撤掉「打开库文件」→ 列表右键菜单加 `📂 打开` / `📁 打开所在文件夹` / `🗄 打开库文件…`；规则写进需求 **§4.8 硬规则 9**（工具条只放页面级功能，单条动作一律归右键）
- **v3.28**：🛠 **NT-023 / NT-024 验收通过 ✅ + 执行 NT-025（⚠️ 待本地编译 + 实机验收）** —— **NT-025 窗口缩放**：根 Grid 叠加 `ResizeGrips`（四边 5px + 四角 10px 透明热区）→ `BeginResizeDrag` 八向缩放 + `StandardCursorType` 光标；最大化时整层关命中测试；`WindowBoundsService` 按配置记 `config\window.json`（关闭存 `ClientSize`、最小化态不覆盖、离屏位置夹回、尺寸不小于 960×640）。自检修掉 1 处 `OnPropertyChanged` **重复 override**（CS0111）。分屏贴靠 / Win+方向键为**实机验证项**（⛔ 不做 Snap Layouts 钩子）。**NT-023/024 已由用户本地编译 + 实机验收通过**（`0ded557`）。**实机复验（同日）**：缩放 / 回归 / 大小记忆 ✅；**位置记忆** ✅；**系统贴靠**改走官方装饰路线后**已可用** ✅（`WindowDecorations="Full"` + 标题栏/抓取区标 `ElementRole`）；随之的副作用（Avalonia 自绘窗框导致**标题文字重叠 + 多一个会破坏状态的按钮**）已修（`WindowDecorationsTheme` 指向**空装饰主题**）；并按用户要求把**配置名**挪到**左树底部与版本号同显**（`TreeVersionText` = `<配置名> · <版本号>`，版本与「关于」同源，复验 ✅）。**第四轮**：`Full` 下 Avalonia 自绘窗框（标题文字重叠 + 多一个点坏状态的按钮）改 `WindowDecorations="BorderOnly"`（读 `Window.cs::ComputeDecorationParts()` 定论：TitleBar 部件只在 `Full` 时加入 → BorderOnly 部件集合为 None、Avalonia 不画）＋ code-behind 手工补 `WS_CAPTION` 保贴靠资格（状态变化时重补）——复验：**外观 ✅ / 贴靠 ✅**；但 `TitleBar` 角色让整块标题栏按系统标题栏处理 → 里面的按钮与标签条收不到点击，**已修**：交互容器（左按钮组 / 标签条 / 右窗口按钮组）补 `ElementRole="User"`（放行点击），24px 留白仍保持 `TitleBar`（保住贴靠拖动）。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.28 ｜ 规范 v3.21 ｜ 提示词 v3.15 ｜ 本台账 v3.30 |
| 技术栈 | .NET 10 + Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 7 项目 / 约 156 个 `.cs+.axaml+.csproj` / 约 20400 行 |
| 仓库根 | 能看到 `src/` 的那一级 |
| 版本控制 | ✅ git（分支 `main`） |
| 构建分工 | 🧑‍💻 构建 / 发布由用户本地执行（`pwsh -File .\scripts\build.ps1`）；AI 只改 `src/`（**例外：`scripts/` 构建脚本经用户明确授权可改**——2026-10-13 授权，Settings 物理必备改造） |

**编译状态**：✅ v1.14 / v1.16 本地 0 错；⚠️ v1.21~v3.12 尚未由用户本地复跑 → 待跑；**v3.13 / v3.14 已于 2026-10-12 本地编译通过 ✅**；**v3.15 / v3.16 已于 2026-10-13 经用户本地验收通过 ✅**（v3.15 附 3 条整改 → v3.20）；**v3.20 / v3.21 / v3.22 已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（v3.21 的 pin / 列宽 / 筛选 / 关闭按钮四项在 v3.22 重做后一并验收）；**v3.24 标题栏第三轮 + 渲染修复已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（commit `b658c39`）；**v3.27（NT-023 框架 + NT-024 多实例）已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（commit `0ded557`）；**v3.28（NT-025 窗口缩放 + 系统贴靠 + bounds 记忆）已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（含五轮实机复验：位置记忆 / 缩放 / 贴靠 / 标题栏交互 / 无副作用）。

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
| 媒体资产 | 12 | **1** | **11** |
| **合计** | **35** | **23** | **12** |

> **骨架页**：Web 的 `web-http`（判据：VM 仅有 Title/Description/StatusText/IsBusy 与 PrimaryAction/Refresh 样板命令）+ **媒体资产 11 页**（NT-014 骨架，NT-017~022 逐页落地；`assets-all` 已由 NT-016 落成真实列表页）。

### 2.4 菜单清单（36 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是首个固定标签页） |
| 电脑工具 | pc | 20 | `IconPc` | `pc-favorites`收藏夹/0/✅ ｜ `pc-shortcuts`设置与脚本/1/✅ ｜ `pc-quicksettings`快捷设置/2/✅ ｜ `pc-package`软件包/3/✅ ｜ `pc-info`系统信息/99/✅ |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP测试/1/🔧 ｜ `web-bookmark`书签/2/✅ ｜ `web-ping`网络Ping/3/✅ ｜ `web-scrape`网页抓取/4/✅ ｜ `web-hosts`Hosts管理/5/✅ |
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**视频预览**/1/✅ ｜ `video-batch`**批量处理**/2/✅ |
| 媒体资产 | assets | 50 | 🗂 emoji（矢量键留白） | `assets-all`全部资产/0/✅ ｜ `assets-video`视频/1/🚧 ｜ `assets-music`音乐/2/🚧 ｜ `assets-book`书籍/3/🚧 ｜ `assets-image`图片/4/🚧 ｜ `assets-document`文档/5/🚧 ｜ `assets-tags`标签管理/6/🚧 ｜ `assets-categories`分类管理/7/🚧 ｜ `assets-collections`集合管理/8/🚧 ｜ `assets-storages`存储源/9/🚧 ｜ `assets-tasks`任务中心/10/🚧 ｜ `assets-trash`回收站/11/🚧 |
| 设置 | settings | 999 | `IconSettings` | `settings-general`通用/0/✅ ｜ `settings-theme`主题/1/✅ ｜ `settings-path`路径/2/✅ ｜ `settings-launch`一键按钮/3/✅ ｜ `settings-dns`加密DNS/4/✅ ｜ `settings-package`软件源/5/✅ ｜ `settings-profile`配置/6/✅ ｜ `settings-data`数据管理/7/✅ ｜ `settings-about`关于/8/✅ ｜ `settings-all`功能清单/9/✅ |

---

## 三、待办与优先级

> 本轮范围外的项（详情卡上提 / 搜索框 / 历史栈 / 图标矢量化）见**需求 §4.11**，此处不重复登记。
> **待办来源（规划模式产出）**：每次新需求，由 AI 对比《开发需求》应然 vs 本节「一、基线」生成；逐条可验收、带分级（P0/P1…）。
> 流程定义见 `skill/references/collab-method.md` §二（**规划 / 执行双模式 + 草稿闸门 + 批量协议**）；本表 = **待办项**（紧凑），细节统一在 `src/docs/backlog/NT-xxx.md`。
> 生命周期：📝 草稿 → ✅ 就绪 → 🔄 执行中 → ✔ 已完成（待验收）；验收后台账待办项翻 ✔、细节文件按 `backlog/README.md` 清理（删 / 留最近 3）。台账只翻状态，不随细节删。

### 待执行待办（backlog 索引）
> 详细规格与验收清单见 `src/docs/backlog/NT-xxx.md`；**执行模式只跑其中 `✅ 就绪` 项**（`📝 草稿` 不执行）。
> 睡前开会话发「**执行全部就绪待办**」即可让 agent 批量跑（⛔ 不用 automation 定时触发）。

| NT | 标题 | 优先级 | 状态 | 详情 |
|---|---|---|---|---|
| NT-001 | web-http 实装（唯一剩余骨架页） | P2 | ✔ 已完成（待验收） | backlog/NT-001.md |
| NT-002 | pc-package 收尾（Parser 实机 + 批量队列） | P2 | 📝 草稿 | backlog/NT-002.md |
| NT-003 | settings-package 软件源（退出码验证） | P2 | 📝 草稿 | backlog/NT-003.md |
| NT-004 | defaults\ 与脚本目录播种确认 | P2 | 📝 草稿 | backlog/NT-004.md |
| NT-005 | 模块热加载实装（规范 §3.7） | P2 | 📝 草稿 | backlog/NT-005.md |
| NT-006 | 快捷设置：运行改造（可见 pwsh+回显 / GUI 隐藏 / 切换 shell）+ 导入导出 / 清除缓存 | P1 | ✔ 已完成（待验收） | backlog/NT-006.md |
| NT-007 | 设置与脚本：导入导出 + 右键编辑 / 打开方式 + 运行如双击 | P1 | ✔ 已完成（待验收） | backlog/NT-007.md |
| NT-008 | 软件包：可升级对齐 winget update + Pin 显示 + 已装移状态栏 + 详情 bug + 已装导入导出 | P1 | ✔ 已完成（待验收） | backlog/NT-008.md |
| NT-009 | 首页：收藏配色 / 一键操作固定底部 / 删工具箱行 / 状态栏模块功能数 | P2 | ✔ 已完成（待验收） | backlog/NT-009.md |
| NT-010 | 通用样式统一：激活强调色加深 / 按钮配色 / 导入导出控件一致 / 输入框水印 / 弹层样式 | P1 | ✔ 已完成（待验收） | backlog/NT-010.md |
| NT-011 | 视频列表：删扫描按钮 / 数千视频优化 / 入库去重 / 删清空媒体缓存 | P1 | ✔ 已完成（待验收） | backlog/NT-011.md |
| NT-012 | 视频预览：旋转播放按钮 + 命令卡旋转 + 按钮标题去后缀 | P2 | ✔ 已完成（待验收） | backlog/NT-012.md |
| NT-013 | ffmpeg 卡：剪辑转码输入对齐 + 转码默认 H.265/CRF23/_H265/默认宽高 | P2 | ✔ 已完成（待验收） | backlog/NT-013.md |
| NT-014 | 媒体资产模块骨架（Module.MediaAssets 项目 + 三处登记 + 12 菜单 + 12 空页面；一级图标暂 emoji 占位） | P1 | ✔ 已完成（待验收） | backlog/NT-014.md |
| NT-015 | 媒体资产数据层（13 表 + 索引）+ AssetService + ScanService + TaskQueue | P1 | ✔ 已完成（待验收） | backlog/NT-015.md |
| NT-016 | AssetAllView 落地（列表 + 详情 + 工具条 + 筛选 + 排序 + 列显隐） | P1 | ✔ 已完成（待验收） | backlog/NT-016.md |
| NT-017 | 5 个类型页（视频 / 音乐 / 书籍 / 图片 / 文档；图片页可切网格） | P1 | 📝 草稿 | backlog/NT-017.md |
| NT-018 | 标签 / 分类 / 集合三页 + 逻辑操作 + operation_logs | P1 | 📝 草稿 | backlog/NT-018.md |
| NT-019 | 物理操作（FileOperationService + 日志先行）+ 任务中心 + 回收站 | P1 | 📝 草稿 | backlog/NT-019.md |
| NT-020 | 存储源页 + 移动硬盘离线 / 上线 + 外部变更校准 | P2 | 📝 草稿 | backlog/NT-020.md |
| NT-021 | 媒体资产设置页（`settings-media-assets`，View/VM 归本模块、自注册进 settings 组）+ 元数据 + 缩略图 | P2 | 📝 草稿 | backlog/NT-021.md |
| NT-022 | 列表 JSON 导入 / 导出 + 用户数据导出（R1 缓解） | P2 | 📝 草稿 | backlog/NT-022.md |
| NT-023 | 框架：MenuService 跨组延迟补登 + Settings 必备化 + 启动健全性检查 / 无模块降级 | P1 | ✔ 已完成（已验收） | backlog/NT-023.md |
| NT-024 | 多实例：`--profile` 启动参数 + 每配置单例 + 切换新语义（不写全局默认）+「设为默认」/「🚀 新开」+ 标题带配置名 | P1 | ✔ 已完成（已验收） | backlog/NT-024.md |
| NT-026 | 媒体资产：多库融合（工作副本模型 = 打开库 A → 再打开库 B → 💾 保存；「减」= 勾选后删除；合并走 `ATTACH DATABASE`） | P1 | 📝 草稿 | backlog/NT-026.md |
| NT-025 | 窗口：自绘缩放抓取区（四边 5px/四角 10px，BeginResizeDrag 八向）+ 分屏贴靠验证 + bounds 按配置记忆 | P1 | ✔ 已完成（已验收） | backlog/NT-025.md |

> `📝 草稿` 需继续展开细节，升 `✅ 就绪` 后才会被批量执行；`✔ 已完成（待验收）` 经用户验收后清理细节、台账翻 ✔。已落盘待编译的项见下方 P0 v3.15 / v3.16。

### P0 · 视频工具两页详情卡统一 + 全局密度收紧（v3.12）✅ 已落盘
> v3.12 完整验收清单已折叠：跨页投递改 `OnAttachedToVisualTree` 取件（修"刷新才生效"）；「命令生成」更名「视频预览」(菜单 key 留 `video-cut`)；两页详情卡统一(分区 剪辑→转码→输出→命令、勾选框进 `Expander.Header`、详情卡零操作按钮)；详情开关移工具条末位 `Button.detail-toggle`；全局密度收紧(行 28/表头 32/卡片 12，基线见规范 §2.1)。逐项见 `git log` / 变更流水。

### P0 · v3.13 视频工具精细化 ✅ 代码已落盘 + 本地编译通过
> v3.13 完整验收清单已折叠：预览页 UI 重构(三条时间条常显 + 类播放器按钮 + 预览区 MinHeight 340)；两页详情卡对齐(宽 340 / `ComboBox` 不挤出)；批量→预览反向发送；`VideoCatalogService`(表 `video_catalog`) + 批量任务导入导出。逐项见 `git log` / 变更流水。

### P0 · v3.14 视频列表「📂 导入」拆分按钮（合并文件夹 / 视频文件 / 视频库）✅ 代码已落盘 + 本地编译通过
> v3.14 完整验收清单已折叠：工具条「📂 导入」改 `SplitButton`(文件夹 / 视频文件 / 视频库.db 三项)；`ImportVideoFiles` 由路径建 `VideoItem` 追加去重；已本地 `build.ps1` 验证通过。逐项见 `git log` / 变更流水。

### P0 · v3.15 视频工具增强（默认目录 / 标签 / 改名 / 表头 / 收藏）✅ 用户验收通过（附 3 条整改 → v3.20）

> 2026-10-13 用户验收：**除 3 条整改外全部通过**；整改项 = ①「🏷 管理标签」按钮入详情卡、② 每标签预设颜色、③ 删「自动调整列宽」改表头拖拽调宽 —— 已转 **v3.20**（见下）。

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

### P0 · v3.16 视频工具六项（导入独立 / 状态栏 / 详情卡一致 / 表头 ☑ / 清除）✅ 用户验收通过

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

### P0 · v3.20 视频工具整改（用户 v3.15 验收的 3 条 + v3.16 通过）✅ 用户验收通过（2026-10-13）

**v3.20**（用户 2026-10-13 验收意见；需求 §6.4.6 / §6.4.15，规范 §3.6 v3.16）
- [x] **①「🏷 管理标签」入详情卡** —— 按钮从工具条移入**视频列表详情卡**标签编辑区同行右侧；收藏页详情卡同款同位（两页详情卡同结构铁律）；弹层内容不变（查看 / 改名 / 删除 / 新建）。
- [x] **② 标签颜色** —— 新增 `Module.VideoTools/Services/TagPalette.cs`（8 色固定板 + 标签名 FNV-1a **稳定**取色；数据色板，仿 `AccentPalette` 单一色源先例，⛔ 不走主题键、⛔ 不在 XAML 写死）；`Models/TagChip.cs` 色片出现在：列表「标签」列（横排、超宽裁剪、⛔ 不换行撑高行；>8 个折叠 `+N` 灰片）、详情卡编辑框下方、收藏页卡片、标签管理弹层（行首色点）；`VideoItem.Tags` setter 连带重建 chips（各处直接赋值都自动跟上）。
- [x] **③ 删「⇔ 自动调整全部列宽」** —— 基类 `AutoFitColumns` / `IsAutoFit` / `_autoFit` + **11 个列表页**右键菜单项全删（根因：可见列整体 `Auto` 时，长内容列按**未截断全文**测宽，把其余列挤到接近 0 → 表头文字被裁没，像整行消失）。
- [x] **④ 表头拖拽调列宽** —— `ColumnGrid.IsResizable`（默认 false；**四份副本同步**）在相邻**可见**列边界生成 5px 透明把手（`SizeWestEast` 光标、指针捕获、每列 ≥24px、左右列总宽守恒、隐藏列边界无把手）；`ColumnResize` 事件 → 基类 `ResizeColumns` 写「手动列宽」→ `VisibleColumnSpecs` 输出像素宽 → 表头与所有行同规格重建；视频列表 + 批量处理两页表头开启；⛔ 手调宽只存会话（切标签页不丢、重启还原）。
- [x] **⑤ 标签管理操作后内存同步** —— 视频列表 `RefreshTagsInMemory`（改名 / 删除后按库刷新列表行色片，⛔ 不重扫）；收藏页 `Load()` 现读。
- ⚠️ **验收点**：① 详情卡标签区右侧有「🏷 管理标签」；② 弹层行首色点颜色 = 列表 / 详情卡 chip 颜色（同标签同色）；③ 列表标签列 chip 超宽裁剪、不撑行；④ 表头列交界出现 ⇔ 光标可拖，拖完表头与行不错位、每列 ≥24px；⑤ 右键菜单无「自动调整列宽」；⑥ 收藏页卡片 / 详情卡 chips 同色同源。
- ✅ **已由用户本地编译 + 实机验收通过**（2026-10-13）。交付前静态自查：`skill/scripts/check.ps1` 通过（28 个 `.axaml` XML 合法、主题键全在白名单）。

### P0 · v3.21 标签升级 + 列宽 / 筛选 / 小修（用户拍板批 + 验收批）✅ 用户验收通过（标签卡 / 详情卡 / 标签搜索通过；列宽 / pin / 筛选 / 关闭按钮转 v3.22 重做并已通过）

**v3.21**（需求 §4.2 / §4.8.1 / §4.8.2 / §6.2.3 / §6.4.6；用户 2026-10-13 拍板：② 色板方案由我定（= 沿用数据色板 `TagPalette`）、③ 手调列宽只存会话、① `VideoPreviewView` 那处标题改动是他手改的）
- [x] **① 标签能保存**：新增表 `video_tags(name, created_at)`；`VideoCatalogService` 加 `CreateTag`（真入库）/ `DeleteTagIndex`；`GetAllTags` = **标签库 ∪ 已使用标签**合并计数（新建未用的显示「尚未使用」）；`RenameTag` / `DeleteTag` 同步标签库。
- [x] **② 详情卡不手打、直接选标签**：`VideoListViewModel` / `VideoFavoritesViewModel` 的 `EditTags` / `SaveTags` / `TagsBox` **全部删除**，改 `TagPicks` + `ToggleTagCommand` —— 点色片即打上 / 取消（即时写 `video_catalog.tags`，⛔ 无保存按钮、⛔ 无输入框）；已打上 = 本色彩片，未打上 = 淡显；没入库的提示先「💾 加入视频库」。
- [x] **③ 标签卡丰富**：弹层加**搜索框**（`TagManagerFilter`，按名称过滤）；说明文案改「新建直接存进标签库」。
- [x] **④ 列宽：固定表头宽列**（需求 §4.8.1）：`ListColumn.FixedToHeader`（声明式，⛔ 不做运行时逐行测宽 —— 虚拟化只测可见行、滚动会让列宽抖动）；表头 `ColumnGrid` 首次布局实测各列表头内容 `DesiredSize.Width` → `HeaderMeasured` 事件 → VM `LockColumnsToHeader()` → `VisibleColumnSpecs` 输出像素（优先级高于手动 / 星号）；`LockedColumnMask` → `ColumnGrid.LockedColumns` → 锁定列**边界不放把手**、`ResizeColumns` 也拒绝。视频列表先标 3 列（类型 / 帧率 / 已收藏），**能力对所有列表页可用**（每页给"内容短且定长"的列标 `fixedToHeader: true` 即可）。
- [x] **⑤ 修「仅看 Pin」**（§6.2.3）：语义 = **已安装包里被 Pin 住的** —— 不在已安装列表时自动切过去再筛；旧实现在搜索 / 可升级模式下要么筛的不是已安装集合、要么必然为空。
- [x] **⑥ 筛选统一叫法 + 扩项**（§4.8.2）：PcTools 两页「分类」→「类型」（列键不变，避免丢列显隐偏好）；PcInfo / PathView / 批量页把**类型值**列进右键筛选；视频列表把**类型（扩展名）+ 标签**列进筛选（标签走 `Contains` —— 逗号串用"等于"会漏）。
- [x] **⑦ 标签关闭按钮**（§4.2）：悬停时 20→24px、字号 11→14、**背景恒透明**、图标 `DangerBrush` 红色 ✕。
- ⚠️ **验收点**：① 新建标签后重启仍在（真入库）；② 详情卡只有色片没有输入框，点一下列表 / 卡片色片即时变；③ 类型 / 帧率 / 已收藏三列**拖不动**且宽度正好是表头文字宽（若内容被截短 → 告诉我哪列，去掉它的 `fixedToHeader`）；④ 表头右键筛选里有「类型：…」「标签：…」，点一下生效；⑤ 软件包勾「仅看 Pin」= 已安装里被 Pin 的；⑥ 悬停标签关闭按钮 = 红 ✕ 且更大。
- ✅ **已由用户本地编译 + 实机验收通过**（2026-10-13）；交付前静态自查 check.ps1 通过。

### P0 · v3.22 反馈修订批（Pin 命令化 + 列宽示例值判定 + 两个样式 bug + 标题栏视觉）✅ 用户验收通过（2026-10-13）

**v3.22**（用户 2026-10-13 反馈：标签卡 ✔ / 详情卡 ✔ 通过；pin 重做；关闭按钮重修；筛选点不出；列宽按"示例数据行"判定；**暂不拓展到其它标签页**；刷新按钮评估**同意我的判断** = 页内 🔄 保留）
- [x] **① Pin 命令化（§6.2.3）**：删除「仅看 Pin」勾选框（`ShowPinned` 属性 + 过滤逻辑全删）；工具条新增 **「📌 Pin 列表」** —— 跑 `winget pin list`，结果**并入 `winget list` 已安装列表**：已存在的贴 📌、不在列表里的追加一行（pin list 只给包名，名称/版本可能空）。本页定位明确为「常用 winget 命令封成按钮 + 下方一个列表清晰显示」。
- [x] **② 列宽判定重做（§4.8.1）**：`ListColumn.FixedToHeader` → **`ListColumn.Sample`（示例值）**；**示例宽 ≤ 表头宽** 才锁成表头宽，锁定宽 **不小于 34px**（保证窄列的边界把手仍可显示、可拖）。列表**为空时**显示一条**示例行（水印，Opacity 0.5）**：⛔ 不在数据集合里、不参与统计 / 排序 / 筛选 / 勾选 / 导入导出，有真实数据后整条消失。视频两页落地（列表 16 列 / 批量 11 列都填了示例值）。
- [x] **③ 筛选子菜单**：11 处 `MenuItem.ItemContainerTheme` 补 `BasedOn="{StaticResource {x:Type MenuItem}}"`（缺它 = 子项没模板 → 点了不弹）。
- [x] **④ 标签关闭按钮（二次修）**：后代选择器仍压不住 —— Fluent 的 `Button` **模板状态**自己画底并把前景置白。改成套**裸按钮主题** `BareButtonTheme`（`App.axaml` 里只有 `ContentPresenter`、零状态绘制），控件写 `Theme="{StaticResource BareButtonTheme}"` → 红 ✕ + 透明底 + 放大才真正生效。
- [x] **④b 激活态简化（用户二次意见）**：激活标签页**只把文字改成强调色**，跟普通标签一样 —— **删掉 CardSelectedBrush 底色**（那块底色正是"关闭按钮看着有背景"的来源）+ **删掉下方 2px 强调色指示条**（用户："我还以为那是阴影呢"）。
- [x] **⑥ 标题栏视觉（v3.22，用户二次纠正）**：**窗口控制按钮维持 46×40 原尺寸**（本来就撑满），⛔ **只去圆角** + 关闭图标加粗（描边 2）；真正偏小的是 **`≡` / `⟳`（36×32、图标 18 → 40×40、图标 22）** 与 **标签按钮（占 24px → MinHeight 30 + Stretch 撑满，标签项上下留白 4→2）**；标题栏域圆角全部归 0。
- [x] **⑥b 标题栏第三轮 + 渲染修复（v3.24，commit `b658c39`，✅ 已验收）**：用户截图三次纠正后的终态（**图标方案以本条为准**，⑥ 的「≡/⟳ 40×40 + 图标 22」被推翻）——五图标统一 16×16 Path；窗口三按钮因 **Path 在 `BareButtonTheme` 的 ContentPresenter 里渲染不出来**（连踩三轮定位）改用 **Rectangle(最小化)/Border 方(最大化·还原)/TextBlock ✕(关闭)**；`BareButtonTheme` 模板包 Border 接住 Background（修「关闭按钮 hover 背景画不出」）；标签关闭 = 透明底 + 红✕；左树一级 15 / 二级 13、标签 13、激活标签文字加粗；`win-btn.close:pointerover` 改 Foreground 属性继承；tree-version 高 26。
- ✅ **①~⑥ + ⑥b 已由用户本地编译 + 实机验收通过**（2026-10-13）；交付前静态自查 check.ps1 通过。
- ⚠️ **验收点**：① 点「📌 Pin 列表」→ 已安装列表里出现被 Pin 的包（📌），状态栏说明并入了几项；② 空列表时有一条半透明示例行，导入视频后消失；③ 类型/帧率/已收藏这类"示例比表头短"的列拖不动且宽 ≥34；④ 表头右键「🔎 筛选」能弹出子菜单并可选；⑤ 悬停标签关闭按钮 = **红 ✕、无背景**；激活标签页只有**强调色文字**，没有底色、没有指示条。
- 📌 **刷新按钮结论（评估，不进需求文档）**：标题栏 ⟳ = 重建标签页（丢状态），页内 🔄 = 只重载数据 → **页内刷新保留**；后续只把 ⟳ 的 ToolTip 写清"重建页面"。

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
|---|---|---|
| 2026-10-14 | **v3.30** | **NT-016 工具条收口（⚠️ 待本地编译 + 实机验收）**：① 扫描入口合并为单条 `SplitButton「📁 扫描」`（主钮 = 选文件夹 / 下拉 = 「📁 文件夹…」·「📄 文件…」，样式照视频列表「📂 导入」`SplitButton.ghost`）—— 🔴 撤回旧偏离②（"SplitButton 主按钮无 Click"是**错的**）；② 标题栏 ⟳ tooltip 改「重建当前页」；③ **NT-026 重写**：库合并 = 工作副本模型（打开 A → 再打开 B → 💾 保存），"减" = 勾选后删除，原 `origin` 列方案作废 |
| 2026-10-13 | **v3.29** | **执行 NT-014 + NT-015 + NT-016（⚠️ 待本地编译 + 实机验收）**：媒体资产模块骨架（新模块 + 三处登记 + 12 菜单 + 12 空页面，页面 23→35）+ 数据层（13 表 / 10 索引 + `AssetService` 四级匹配 + `ScanService` 扫描闭环 + `TaskQueue` 串行队列）+ **`assets-all` 样板列表页**（8 列 + 工具条 / 筛选条 / 空态示例行 / `AssetDetailView`；详情面板改由 Shell 承载；`ColumnGrid` 第五份副本）；模型 `Storage`→`StorageSource` 避 Avalonia 命名空间撞名 |
| 2026-10-13 | **v3.28** | **NT-023/024 验收通过 ✅ + NT-025 窗口缩放（✅ 已实机验收）**：窗口缩放抓取区（四边 5px / 四角 10px 自绘透明热区 → `BeginResizeDrag` 八向 + 光标；最大化整层关命中测试）+ `WindowBoundsService` 按配置记 `config\window.json`（关闭存 ClientSize、最小化不覆盖、离屏夹回、尺寸 ≥960×640）。自检修掉 1 处 `OnPropertyChanged` 重复 override（CS0111）。分屏贴靠 / Win+方向键列为实机验证项 |
