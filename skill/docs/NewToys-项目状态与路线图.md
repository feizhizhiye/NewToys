# NewToys 项目状态与路线图

> v3.86 · 2026-10-26 · 台账
> **标准角色（术语见《AI协作手册》【术语对照】）**：本文件 = **项目状态与路线图**（Status & Roadmap），三、节 = **Backlog 工作项台账**。
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：`index.md`（文档地图）/ 需求规格 / `framework.md` / `ui.md` / `modules/<模块>.md` / AI 协作手册 / AGENTS.md（跨 AI 入口）

**最近三次变更**
- **v3.86（✅ 实施 2026-10-26）**：**Hosts 管理页多选 + 批量校验**（NT-112）—— 两栏 `Ctrl` 增选 / `Shift` 连选 / `Ctrl+A` 全选（两栏互斥、计数「已选 N」）；右键 `🧪 检测选中`（只验选中条目、结论进「状态」列、选中即测含禁用行）/ `✅ 启用选中` / `⏸ 禁用选中`。通用多选口径新增 **`ui.md` §6.10**。需求规格升 v3.98。⛔ 未动框架层。
- **v3.85（✅ 实施 2026-10-26）**：**Hosts 管理合单页 + 源清单进设置**（NT-111）—— 撤 `web-hosts-system` 页，功能并入 `web-hosts`；`web-hosts` 改双列表工作台（左系统 hosts/备份、右拉取方案，条目增删改 + 差异标记 + 草稿一次写入）；备份只针对系统原文并作为左栏数据源；设置页新增「Hosts 开源项目源」节（预设可删 / 可新建）。需求规格升 v3.97。⛔ 未动框架层。
- **v3.84（🧭 立项 2026-10-26）**：**网络工具两处调整立项**（用户「把书签管理改为网址收藏；hosts 管理功能要扩展些，主要用于修改系统 hosts，增加备份、展示、按需替换，预设一些开源 GitHub 加速 hosts 项目链接，要能检测可用性 / 失效 / 安全」）—— ① 「书签管理」显示名改「网址收藏」（菜单 Key `web-bookmark` ⛔ 不改）；② 新增 `web-hosts-system`「系统 Hosts」页（展示 + 受管块高亮 + 手动编辑 + 备份 / 还原 + 清除受管块）；③ 方案页加源订阅（预设 + 镜像 fallback）、三项检测（可用 / 失效 / 静态安全）、diff 预检 + 二次确认 + 自动备份。登记 **NT-109 / NT-110**，需求规格升 v3.96（新增 §6.3.3）。⛔ 未动 `src/`。
- **v3.82（🧭 立项 2026-10-22）**：**模块重划分 v1.1 立项**（用户「把脚本管理器抽象成新模块、webtools 的脚本/集合集成进脚本管理器」→ 拍板 4 项 →「立项」）—— 需求规格升 **v3.86** 并**新增 §2.3**（唯一权威口径）+ §6.7 系统工具，§6.2 / §6.3 加迁移指针；🔴 用户口头「流水线」= **§2.2 L3 命令链**（复用集合 + `▶ 依次运行`，⛔ 不新造 Step 模型 / ⛔ 不连线 / **线性 only**）；显示名改「**脚本中心**」（Key 全不改）；`script-commands` + `script-favorites` → 新 `system` 模块（Order 25）；web 三页（`web-scrape` / `web-rule-editor` / `web-crawl`）→ script；WebTools 收窄为「网络配置与诊断」+ 新增出口代理。登记 **NT-088 ~ NT-094**；NT-088（执行器上提 Core）= 改框架层，**待用户批准**才可执行。⛔ 未动 `src/`。
- **v3.81（🗂 整理期 2026-10-21）**：**全库过时内容清理**（用户「清理文档中过时的内容」，⛔ 未改代码）—— ① 关键文件索引订正：`QuickActionListViewModel` 路径 `Module.PcTools` → `Module.ScriptManager`、winget 服务 `Core/` → `Module.ScriptManager/Services/`（NT-074 下放）；② `framework.md` §11 已完成项（NT-064/065/066/067/068/075/076/077/078/085）收拢为「已结清」一行，NT-085 状态按本台账订正；③ `ui.md` §10 子节顺序订正（`10.6 空态` 归位到 `10.7 弹出卡片` 前）；④ 需求规格：系统级配置二次确认举例换成 Hosts、删 `VideoDbFolder` 与「5 个类型页」旧表述、版本记录压到三次；⑤ 模块文档：网络工具页数 5/6 → **7 页** + Order 订正、脚本管理器删已删文件 `PackageSourceViewModel` 并重编坑点编号（原有重复编号 7/7、13/13、17/17）；⑥ `index.md` 模块状态列与代码/台账对齐；⑦ NT-080 卡删除已作废的旧方案行（原 #3~#10、验收 #2~#4）。
- **v3.79（2026-10-19 · NT-080）**：**软件包定位收敛 + winget 源模型重写** —— ① 集合页**删「软件包」类别**（`ScriptCollectionKind.Package` 删除；老数据 `kind=package` 归 `info`，`role=package` 不进集合候选池）；② 软件包页**后端只支持 winget**，跨平台（apt 等）另开独立页面；③ 🔴 修「工具条源多选框勾 store 源不生效」：源多选改为**只写 `package.json → defaultSource`**（⛔ 不再勾一下就跑 `source enable` + 重读列表把状态冲掉），全勾 / 全不勾 = 全部源 ⇒ 搜索不传 `--source`；④ 修源分类：`winget`→社区源、`winget-font`→**字体源**、`msstore`→商店源、其它→自定义源，**互斥只发生在社区源内部与字体源内部**（`/fonts` 判定必须先于 `winget`）；⑤ **商店源不可切换**（开关置灰）；⑥ 实测 winget 1.29 **无 `source enable/disable`** ⇒ 关 = `source remove`、开 = 按 `winget source export` 快照 `source add`（回放 `--type` / `--explicit`），两向都要管理员权限（UAC）。改动局限在 `Module.ScriptManager`（未碰框架层）。
- **v3.78（🗂 整理期 2026-10-18）**：**文档一致性治理 + 任务卡归档**（用户「统一 skill 与 src、清理已作废旧信息」）—— ① 模块设置文件名校正：`script-manager.json` / `web-tools.json` / `video-tools.json` / `media-assets.json`（⛔ 不是 `script.json` / `web.json` / `video.json`；`script` 模块的分区名 ≠ 模块键）；② `index.md` 新增 **§四「使用层」写入规范**（用户口述的新功能 / 交互行为 / 使用场景 / 功能说明 / 踩坑各有唯一落点 + 当场落 + 改行为双写）；③ 软件说明书改定位为**使用层 · 用户手册**（576→467 行，删「外部依赖」「重写指引」两节，数据节压成「数据在哪 / 备份什么」）；④ **删卡 9 张**：`framework/NT-054/055/057/058`（已验收，结论在 `ui.md` §7 §8 + `framework.md` §4.5 §6.3 §6.4）、`settings-app/NT-009/050/051/053/056`（模块已撤销，整目录删除）；⑤ 需求规格现行段落旧称全量校正 + 版本记录压到最近三次；⑥ 台账 / `AGENTS.md` 版本记录同步压缩。⛔ 未动 `src/`。
- **v3.76（🗂 整理期 2026-10-18）**：**文档按「框架层 / UI / 每模块一份」重构**（用户「拆分文档，框架层 / ui / 各功能模块各一个 md，待办细节每模块只留最新 5 份」）—— ① 原《NewToys-工程规范.md》（892 行）拆为 **`framework.md`**（框架层：程序集 / 启动 / 菜单 / 加载 / UI 契约 / 配置 / 构建 / 技术债 B1~B17 / **跨模块待办 §11**）+ **`ui.md`**（尺寸密度 / 主题键 / 图标 / CSD / 列表页 / 详情卡 / 工具条 / 样式坑），原文件删除；② 新增 **`modules/script-manager.md` / `media-assets.md` / `video-tools.md` / `web-tools.md` / `settings-app.md`**（统一 6 节模板：职责 / 当前功能 / 实现逻辑 / 数据 / 坑点 / 待办）+ **`index.md`**（文档地图 + 模块清单表）；③ 任务卡按模块分目录 `backlog/<模块>/`（跨模块 `backlog/framework/`），**归档策略改滑动窗口：每目录 ≤5 张已验收卡、未验收不参与淘汰、淘汰前必须先蒸馏进模块 md**（10-14「全量保留」/10-17「默认保留」均废止），本批已蒸馏并删除 27 张卡（framework 18 / media-assets 8 / script-manager 1）；④ **台账与代码对齐**：§2.3 页面全景、§2.4 菜单清单按 `src/` 现状重写（脚本管理器 `pc-*`→`script-*` 6 功能页 + 设置、媒体资产 5 类型页已删 → 7 页、菜单项 34 项），工作项详情列改新路径；⑤ `AGENTS.md` v1.7 / `skill/SKILL.md` / `.workbuddy/skills/newtoys/SKILL.md` / `newtoys-spec.md` / `collab-method.md` / `README.md` / 手册 v3.24 的文档路由与归档策略同步改写。⛔ 未动 `src/`。
- **v3.74 ~ v3.54（归档）**：NT-063 控件库（P0-a/P1/P2-a）/ NT-064 分页 / NT-067 信息展示页 / NT-062 设置归位与树状设置页 / NT-055~059 工具条·详情卡 IconBar·脚本索引库 / 标签栏改版 / 首页标签退出标签栏 —— 逐条明细见 `git log`（本台账只留最近三次变更）。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.96 ｜ framework v4.0 ｜ ui v4.0 ｜ 模块 md v1.1 ｜ 协作手册 v3.24 ｜ 本台账 v3.85 |
| 技术栈 | .NET 10 + Avalonia 12.1.4 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 8 项目 / 272 个 `.cs+.axaml+.csproj` / 约 51000 行 |
| 仓库根 | 能看到 `src/` 的那一级 |
| 版本控制 | ✅ git（分支 `main`） |
| 构建分工 | 🧑‍💻 构建 / 发布由用户本地执行（`pwsh -File .\scripts\build.ps1`）；AI 只改 `src/`（**例外：`scripts/` 构建脚本经用户明确授权可改**——2026-10-13 授权，Settings 物理必备改造） |

**编译状态**：✅ v1.14 / v1.16 本地 0 错；⚠️ v1.21~v3.12 尚未由用户本地复跑 → 待跑；**v3.13 / v3.14 已于 2026-10-12 本地编译通过 ✅**；**v3.15 / v3.16 已于 2026-10-13 经用户本地验收通过 ✅**（v3.15 附 3 条整改 → v3.20）；**v3.20 / v3.21 / v3.22 已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（v3.21 的 pin / 列宽 / 筛选 / 关闭按钮四项在 v3.22 重做后一并验收）；**v3.24 标题栏第三轮 + 渲染修复已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（commit `b658c39`）；**v3.27（NT-023 框架 + NT-024 多实例）已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（commit `0ded557`）；**v3.28（NT-025 窗口缩放 + 系统贴靠 + bounds 记忆）已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（含五轮实机复验）。**v3.37（夜间批量 8 项 + 5 处编译修复）已于 2026-10-14 经用户本地编译通过 ✅**；**v3.29 ~ v3.36 已于 2026-10-14 经用户本地编译通过并按「编译通过 = 验收通过」验收 ✅**（🔴 用户 2026-10-14 定调：本轮起**编译通过即视为验收通过**，不再逐项实机核验）。**v3.40~v3.42 为纯文档批次**（无 `src/` 代码改动）；**v3.43（NT-050~053）+ Avalonia 12.1.4 升级 + 官方钩子迁移 + v3.44 / v3.45（首页标签固定及其修复）已于 2026-10-15 下午经用户本地 `build.ps1` 编译通过，按「编译通过 = 验收通过」验收 ✅**。

**当前主线**：UI 从仿微信三栏重构为**仿浏览器标签页 + 树状左菜单栏**（v3.0 起）→ 视频工具三页重构（v3.8）→ 状态栏下沉（v3.9）→ **视频预览页 + 两页详情卡统一 + 密度收紧（v3.12）**。更早脉络看 `git log`。

---

## 二、已完成

### 2.1 架构与骨架
- **8 项目**：`Shared` / `Core` / **`UI`（控件库）** / `Shell` + 4 模块；分层单向 `Shared ← Core ← Shell ← Module.*`，`Shared ← UI`。
- 中央包版本管理 + 版本号编译自动生成。
- DI 容器 + `IModule` 插件机制 + 菜单 / 导航 / 主题服务。
- **模块加载双轨**：Debug 显式类型清单 / Release 目录扫描。
- **运行期热加载**（Modules + 刷新按钮）：✅ 已实装（NT-005，v3.37 验收）—— `Core/Services/ModuleLoader.cs` 走 collectible `AssemblyLoadContext`。

### 2.2 UI
- 三栏 `60,140,*` → **标题栏 40px（含标签页）+ 左树 180 / 折叠 48 + 主内容区 + 右详情 340 + 底部状态栏 26px**。
- 明暗主题全局切换，键一律 `*Brush`（条数以 `check.ps1` 白名单为准，⛔ 文档不写死数量），默认深色。
- **视觉规范**：共享样式已按 **v3.12 密度基线**收紧（表格行高 28、卡片内边距 12、工具条 12,6、筛选条 12,4）；**框架层图标已全矢量**（`Path` + `App.axaml` 的 `Icon*` 键：一级菜单 / 标题栏 / 标签页 / 左树，NT-035 / NT-045 / NT-046）；⚠️ **数据驱动图标**（二级菜单 / 首页一键按钮 / 模块页内数据行）仍是字符串 → 技术债 NT-047。

### 2.3 页面全景（2026-10-18 按 `src/` 现状重写）

| 模块 | 菜单项 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 脚本中心（`script`） | 7（6 功能页 + 设置） | **7** | 0 |
| 网络工具（`web`） | 5（4 功能页 + 设置） | **5** | 0 |
| 视频工具（`video`） | 4（3 功能页 + 设置） | **4** | 0 |
| 媒体资产（`assets`） | 8（7 功能页 + 设置） | **8** | 0 |
| **系统工具（`system-tools`）** | 4（3 功能页 + 设置） | **4** | 0 |
| 应用级设置（宿主 `system`） | 8 节锚点（六节 + 功能清单） | **8** | 0 |
| 首页（`home`） | 1 | **1** | 0 |
| **合计** | **37** | **37** | **0** |

> **骨架页**：无（`web-http` 已由 NT-001 落成真实页）。
> v3.76：**媒体资产 5 个类型页已删**（`assets-video/-music/-book/-image/-document`，NT-063 P2-a）⇒ 8 = 7 功能页 + 设置；类型切换改由「全部资产」筛选栏驱动。

### 2.4 菜单清单（37 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/Order/✅） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是**默认页**，与其他标签一样常驻标签栏、不可关闭） |
| 脚本中心 | script | 20 | `IconPc` | `script-library`脚本库/1/✅ ｜ `script-collections`脚本集合/4/✅ ｜ `script-info`**信息面板**/99/✅ ｜ `web-scrape`**抓取规则**/5/✅ ｜ `web-rule-editor`**调试集合**/6/✅ ｜ `web-crawl`**运行结果**/7/✅ ｜ **`settings-script`脚本中心设置/100/✅** |
| **系统工具** | system-tools | 25 | `IconSystem` | `script-favorites`收藏夹/1/✅ ｜ `script-commands`命令库/2/✅ ｜ `script-packages`软件包/3/✅ ｜ **`settings-system`系统工具设置/100/✅**（🔴 `system-quick` 快捷命令页 NT-097 已删） |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP 测试/1/✅（工具条逃生舱）｜ `web-bookmark`**网址收藏**（原书签管理）/2/✅ ｜ `web-ping`网络 Ping/3/✅ ｜ `web-hosts`**Hosts 管理**/7/✅（双列表工作台；原 `web-hosts-system` 已撤，NT-111）｜ **`settings-web-tools`网络工具设置/100/✅** |
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**视频预览**/1/✅ ｜ `video-batch`**批量处理**/2/✅ ｜ **`settings-video-tools`视频工具设置/100/✅** |
| 媒体资产 | assets | 50 | `IconMediaAssets` | `assets-all`全部资产/0/✅ ｜ `assets-tags`标签管理/6/✅ ｜ `assets-categories`分类管理/7/✅ ｜ `assets-collections`集合管理/8/✅ ｜ `assets-storages`存储源/9/✅ ｜ `assets-tasks`任务中心/10/✅ ｜ `assets-trash`回收站/11/✅ ｜ **`settings-media-assets`媒体资产设置/99/✅** |
| ~~设置~~ | — | — | — | 🔴 **v3.66：该一级菜单项已删除**。应用级设置 = **一个整页**（入口 = 左树版本行齿轮 / `Ctrl+,` / 标题栏搜索按钮），**模块设置页已挂回各自模块**（见表内加粗项）。 |
| **宿主分组（不渲染）** | system | 999 | `IconSettings` | `IsHome=true` ⇒ 左树 / 首页模块计数 / 功能清单页都跳过；只承载寻址（8 项）：`settings-general`**启动与退出**/0 ｜ `settings-update`更新/1 ｜ `settings-theme`主题外观/1 ｜ `settings-path`**路径与配置**（配置档 + 外部程序路径）/2 ｜ `settings-launch`一键按钮/3 ｜ `settings-dns`加密 DNS/4 ｜ `settings-data`数据管理/6 ＋ `settings-all`功能清单/7（**首页「显示更多」就地展开**；独立页保留给标题栏 `＋` 与「关于」节按钮）。🔴 **所有 Key ⛔ 一律未改**；「关于」节无锚点 Key |

---

## 三、待办与优先级

> 本轮范围外的项（详情卡上提 / 搜索框 / 历史栈 / 图标矢量化）见**需求 §4.11**，此处不重复登记。
> **工作项来源（规划期产出）**：每次新需求立项，由 AI 对比《需求规格》应然 vs 本节「一、基线」生成；逐条可验收、带分级（P0/P1…）。
> 流程定义见 `skill/docs/NewToys-AI协作手册.md`【研发流程】（**规划 / 实施双模式 + 就绪闸门（DoR）+ 批量协议**）；本表 = **Backlog 工作项台账**（紧凑索引），**任务卡**按模块放 `skill/docs/backlog/<模块>/`（跨模块 → `backlog/framework/`）。
> 生命周期：**📝 Draft（草稿）** → **✅ Ready（就绪，过 DoR）** → **🔄 In Progress（执行中）** → **✔ Done（已完成待验收）** → 用户验收后 **Accepted（已验收）**；验收后台账工作项翻 ✔、任务卡先**蒸馏进 `modules/<模块>.md`** 再进 **每目录 ≤5 张已验收卡**的滑动窗口（未验收卡不参与淘汰；作废卡直接删除并标 `🗑`）。台账只翻状态，不随任务卡删。
> 术语（立项 / 工作项 / 任务卡 / 就绪闸门 / 完成定义 …）见《AI协作手册》【术语对照】。

### 工作项索引（Backlog）
> 任务卡（验收标准 / 技术要点 / 依赖 / 范围）见 `skill/docs/backlog/<模块>/NT-xxx.md`；**实施期只跑其中 `✅ 就绪` 项**（`📝 草稿` 不执行）。
> 睡前开会话发「**执行全部就绪待办**」即可让 agent 批量跑（⛔ 不用 automation 定时触发）。

| NT | 标题 | 优先级 | 状态 | 详情 |
|---|---|---|---|---|
| NT-001 | web-http 实装（唯一剩余骨架页） | P2 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删 → `modules/web-tools.md` §2.5 HTTP 测试） |
| NT-002 | pc-package 收尾（Parser 实机 + 批量队列） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-003 | settings-package 软件源（退出码验证） | P2 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删） |
| NT-004 | defaults\ 与脚本目录播种确认 | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-005 | 模块热加载实装（规范 §3.7） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-006 | 快捷设置：运行改造（可见 pwsh+回显 / GUI 隐藏 / 切换 shell）+ 导入导出 / 清除缓存 | P1 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删） |
| NT-007 | 设置与脚本：导入导出 + 右键编辑 / 打开方式 + 运行如双击 | P1 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删） |
| NT-008 | 软件包：可升级对齐 winget update + Pin 显示 + 已装移状态栏 + 详情 bug + 已装导入导出 | P1 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删） |
| NT-009 | 首页：收藏配色 / 一键操作固定底部 / 删工具箱行 / 状态栏模块功能数 | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-010 | 通用样式统一：激活强调色加深 / 按钮配色 / 导入导出控件一致 / 输入框水印 / 弹层样式 | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-011 | 视频列表：删扫描按钮 / 数千视频优化 / 入库去重 / 删清空媒体缓存 | P1 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.8） |
| NT-012 | 视频预览：旋转播放按钮 + 命令卡旋转 + 按钮标题去后缀 | P2 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.8） |
| NT-013 | ffmpeg 卡：剪辑转码输入对齐 + 转码默认 H.265/CRF23/_H265/默认宽高 | P2 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.8） |
| NT-014 | 媒体资产模块骨架（Module.MediaAssets 项目 + 三处登记 + 12 菜单 + 12 空页面；一级图标暂 emoji 占位） | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-015 | 媒体资产数据层（13 表 + 索引）+ AssetService + ScanService + TaskQueue | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-016 | AssetAllView 落地（列表 + 详情 + 工具条 + 筛选 + 排序 + 列显隐） | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-017 | 5 个类型页（视频 / 音乐 / 书籍 / 图片 / 文档）= **薄壳 + 预设类型**，共用 `assets-all` 实现（v3.34；⛔ 网格视图不做、类型专属列延后） | P1 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-018 | 标签 / 分类 / 集合三页 + 逻辑操作 + `operation_logs`（⚠️ 含 2 处偏差：集合排序用 ⬆⬇ 按钮、智能集合规则未实装） | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-019 | 物理操作（`FileOperationService` 日志先行 + 防穿越 + 冲突策略 + 回收站）+ 任务中心 + 回收站页（⚠️ 含 3 处偏差：移动/复制无 UI 入口、删除时间未显示、重试仅扫描任务） | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-020 | 存储源页 + 移动硬盘离线 / 上线 + 外部变更校准 | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-021 | 媒体资产设置页（`settings-media-assets`，View/VM 归本模块、自注册进 settings 组）+ 元数据 + 缩略图 | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-022 | 列表 JSON 导入 / 导出 + 用户数据导出（R1 缓解） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-023 | 框架：MenuService 跨组延迟补登 + Settings 必备化 + 启动健全性检查 / 无模块降级 | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-024 | 多实例：`--profile` 启动参数 + 每配置单例 + 切换新语义（不写全局默认）+「设为默认」/「🚀 新开」+ 标题带配置名 | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-025 | 窗口：自绘缩放抓取区（四边 5px/四角 10px，BeginResizeDrag 八向）+ 分屏贴靠验证 + bounds 按配置记忆 | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-026 | 媒体资产：多库融合（工作副本模型 = 打开库 A → 再打开库 B → 💾 保存；「减」= 勾选后删除；合并走 `ATTACH DATABASE`） | P1 | ✔ 已完成（已验收） | backlog/media-assets/NT-026.md |
| NT-027 | 视频工具精简：删视频库(.db) / 标签 / 收藏页 → 收窄为「简易查看器 + 送批量任务」（含旧表一次性清理） | P0 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.8） |
| NT-028 | 示例数据 + 详情卡默认示例态（批次 1~4 已落：视频两页 / `assets-all` / PcTools 4 页 / WebTools 4 页 / `settings-path`；批次 5 类型页随 NT-017；含框架层 `ListPageViewModelBase` 授权改动） | P1 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-029 | 回收站显示删除时间（NT-019 偏差②闭合） | P1 | ✔ 已完成（已验收） | backlog/media-assets/NT-029.md |
| NT-030 | 移动/复制 UI 入口（NT-019 偏差①） | P1 | ✔ 已完成（已验收） | backlog/media-assets/NT-030.md |
| NT-031 | 视频类型专属列（NT-017 试点） | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-032 | 修复扫描"大小+修改时间"移动识别键碰撞（误配复制/改名） | P1 | ✔ 已完成（已验收） | backlog/media-assets/NT-032.md |
| NT-033 | 修复多库融合重复/重叠并入产生孤儿资产 | P1 | ✔ 已完成（已验收） | backlog/media-assets/NT-033.md |
| NT-034 | 窗口圆角修复（最大化/分屏方角） | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-035 | 左树一级图标矢量化（媒体资产去 emoji，统一矢量方案） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-036 | 修复标签关闭按钮与标签文字重叠（✕ 落入标签间隙，不挪左侧/不用带圈） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-037 | 左树一级展开整组 BringIntoView（防裁切） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-038 | 三配置档（debug/default/release）：运行时永久驻留（debug=复制 default 播种；商店版转远期） | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-039 | 窗口外框（自绘 1px）+ 激活 / 失焦两态（主题键 32→34；最大化收边框） | P0 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-040 | 窗口装饰回归 Avalonia 12 官方 CSD 路线（`WindowDecorations="Full"` + 隐式键同键覆盖默认主题） | P0 | ✔ 已完成（已验收）· 🔴 2026-10-15 **重新采纳**（跨平台评估定案：Wayland / Debian 13 只有官方 CSD 接得上） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-041 | 清理 Win32 窗口补丁（贴靠样式位 / `WM_NCCALCSIZE` / resize 兜底），逐处删 + 逐处回归 | P1 | ✔ 已完成（已验收）· 2026-10-15 恢复生效（`ApplySnapEligibleStyles` 保持删除） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-042 | DWM 阴影与方角方案复核（`SetWindowRgn` 去留；阴影优先） | P1 | ✔ 已完成（**已验收** 2026-10-15 · 裁决：**保留** region，阴影代价已证伪） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-043 | 标题栏是否改用 `TitleBarBrush`（✅ 拍板**不启用**，清理并入 NT-049） | P2 | ✔ 已完成（已验收 · 并入 NT-049） | 🗑 已作废（卡已删） |
| NT-044 | 标题栏加全屏按钮（⛶）+ `F11` / `Esc` 快捷键 | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-045 | 图标实现方式统一（C 方案：全框架矢量 `Geometry` 库；窗口按钮弃图标字体 / 标签 ✕ 与树展开符弃字符；折叠菜单→panel-left、模块加载→拼图） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-046 | 图标收尾：最小化退化与居中修复 / 拼图与全屏改型（两箭头）/ 左树去展开符 / 「框架层图标只用 `Path`」写进规范 | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-047 | 数据驱动图标矢量化（二级功能菜单 34 项 / 首页一键按钮 / 模块页内数据行；⛔ 静态装饰 emoji 不动） | P2 | 📝 草稿（**用户 2026-10-19：暂时不做**，不排期） | backlog/framework/NT-047.md |
| NT-049 | 清理 3 个未引用标题栏主题键（`TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush`；主题键 34 → 31） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-050 | 设置归位：模块专属设置迁模块（软件源 View/VM → PcTools，媒体资产先例模式）+ settings 内「媒体资产」改名「媒体资产设置」 | P1 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-051 | 设置页合并：主题外观 + 关于 并入 通用设置（单页三节；键 `settings-general` 不变，删 `settings-theme`/`settings-about` 两项） | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-052 | 「🔄 重载模块」移入设置（标题栏 3→2 按钮；入口 = **功能清单页**「增加功能」操作区；方案 A：Shared/Core 新增 `IModuleReloadService` 契约，2026-10-15 用户拍板） | P2 | ✔ 已完成（已验收） | 🗑 已蒸馏（卡已删） |
| NT-053 | 设置页窄界面适配：最窄 960×640（内容宽 720）与默认 1280×800 两种可用（根 ScrollViewer / 星号列 / 去超宽固定容器） | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-054 | 详情卡声明式规范：**三区模型**（`Fields` 字段区 / `Actions` 动作区 / `Custom` 逃生舱）+ 7 种字段 Kind + 3 种提交语义（`Instant`/`OnBlur`/`Explicit`）+ Shell 统一渲染，替代 16 张卡逐页手写 XAML（含债 B13 详情面板上提） | P1 | ✔ **已完成（已验收 2026-10-15）**（2026-10-15：📝 草稿 → ✅ 就绪 → **L0 + L1 交付并编译验收通过** → **L2 全数迁完 16/16**（打样页 `video-batch` 已验收 + 页内自绘 12 页 + MediaAssets 4 张独立视图；**L1 按用户拍板做了加法扩展**：chips 色块 / chips 命令参数取值 / List 项级命令 / chip 悬停提示；**L3 已删 8 个停用视图**，`Custom` 渲染分支仍未接）；**用户本地编译 + 实机回归通过（2026-10-15）；技术债 B13 结清**；遗留 = `Custom` 逃生舱渲染分支仍未接（现无页面需要）） | 🗑 已蒸馏（卡已删） |
| NT-055 | 工具条统一：Shell 声明式承载（`IToolbarSource` + `ToolbarHost`）+ 详情开关浮动（新契约 + Shell 新控件，**框架层首审项**；**已迁 18 个 View / 覆盖 23 页**（`settings-path` 另见 NT-056；三轮补迁 MediaAssets 6 页）；仅 `web-http` 走逃生舱） | P1 | ✔ 已完成（已验收）· commit `8da4462` | 🗑 已蒸馏（卡已删） |
| NT-056 | 设置总表：路径配置 + 配置 合并为一表（分类列区分行类型 / 决定工具可用性；菜单 35 → 34） | P1 | ✔ **已完成（已验收 2026-10-16）**· commit `66faa28` | 🗑 已作废（卡已删） |
| NT-057 | 系统托盘增强：左键切显示 / 隐藏 + 右键五项菜单（首页 · 设置 · 用另一个配置打开… · 退出）+ **托盘图标改用应用图标**（与 exe / 窗口同源，⛔ 不再代码画、⛔ 不写死强调色） | P2 | ✔ **已完成（已验收 2026-10-16）**· 2026-10-15：`CreateTrayIcon` → 唯一图标工厂 `CreateAppIcon(accent)`（窗口 + 托盘同源，随强调色）+ 新增 `Resources/app.ico`（exe 图标）；托盘五项菜单（②③ 先拉起窗口再导航，④ 子菜单排除当前配置 / 无其他配置时置灰）；技术债：**形状两处定义**（C# 常量与 ICO 口径，改一处须同步另一处） | 🗑 已蒸馏（卡已删） |
| NT-058 | 详情卡图标动作组：**第 9 种 `FieldKind.IconBar`**（32×32 图标按钮组）+ chips 分组（`ChipGroupPath`）+ 「全部资产」页打样定稿（**框架层首审项**） | P1 | ✔ **已完成（已验收 2026-10-16）** | 🗑 已蒸馏（卡已删） |
| NT-059 | 「设置与脚本」页改**脚本索引库**：导入收成 `SplitButton`「📂 导入」+ 导入后清筛选 + 详情卡 `IconBar` + 新增「分类」列 + 删除语义分叉 | P1 | ✔ **已完成（已验收 2026-10-16）** | 🗑 卡已淘汰（滑动窗口；已蒸馏进 `modules/script-manager.md` §5） |
| NT-060 | **干净启动**：删示例数据（列表示例行 + 详情卡示例态）改**空态引导**（说明 + 入口按钮）+ 取消 `defaults\` 默认清单播种（⚠️ 实含**框架层减法**：`DetailCardDescriptor.SamplePath`/`SampleText` + `DetailCardRender.IsSample`） | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删） |
| NT-061 | **配置下沉**：Core 新增 **`IOptionsStore`**（KV + 按模块分区，先 json 后可换 SQLite）+ **五模块各一个设置页**注册进 `settings` + 功能入口页**数据源框可编辑**（回车 / 失焦提交，路径无效 ⇒ 状态栏红字 + ⛔ 不写盘） | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删） |
| NT-062 | **应用级设置归位**：`Module.Settings` **撤销** → 6 页迁入 Shell（`settings` 分组改由 Shell 创建）+ **「运行环境」**（外部程序路径上提应用根 `app.json`，与配置档同节）+ 设置项**显式分类** `SettingsCategory` + 设置页**搜索** / 每行**打开配置目录** / 外部程序缺失**惰性探测** + **功能清单移首页** + **软件源并入电脑工具设置** + 删 `GpuAcceleration` + 左树**设置齿轮**（`Ctrl+,`） | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删） |
| NT-063 | **控件库 NewToys.UI + 列表页控件化**：ColumnGrid 五合一（2086 行）+ 详情卡渲染搬家 + 新底栏 ListFooterBar（P0-a）→ 列表页骨架（P1）→ 类型页合并 / 设置行控件（P2） | P1 | ✔ **P0-a + P1 + P2-a 已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删） |
| NT-064 | **列表分页**：UI 底栏归 NT-063，本卡做**数据层** —— SQL LIMIT/OFFSET + **排序下推** + 独立 COUNT(*)；底栏**默认隐藏、列表超出页底才显示**；批量操作只作用于当前页选中项 | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删） |
| NT-065 | **分页推广到「脚本收集 / 命令收集」两页**（`pc-shortcuts` / `pc-quicksettings`）：两页挂 `ListPageSkeleton` 的底栏 + VM 分页（数据源是 **JSON 清单 ⇒ 内存切片**，⛔ 不做 SQL）；同步菜单改名「设置与脚本→脚本收集」「快捷设置→命令收集」 | P1 | 🗑 **已作废（卡已删，2026-10-19）**：改名目标已被 NT-068 取代（两页现为「脚本库 / 命令库」，Key 亦 `pc-*`→`script-*`）；分页部分未做且不再需要（两库存量几十条，骨架已有一屏滚动） | 🗑 已作废 |
| NT-066 | **标题栏搜索扩范围**：可搜**全部二级菜单项** + **应用级设置页的节**（索引 = 已注册菜单项 + `SettingsViewModel.Catalog`；输入即出列表、↑↓/回车/双击打开）；Shell 改动经用户 2026-10-19 授权 | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删 → `framework.md` §4 标题栏搜索） |
| NT-067 | **信息展示页（脚本输出呈现器，原系统信息 `pc-info`）**：`ScriptRunner` 底座（PcTools）+ manifest 驱动（columns/scripts + per-script interval + auto/timeout）+ 多脚本合并 + 按 RowKey 就地定时刷新 + 自定义表头 + 默认采集脚本播种 | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 已蒸馏（卡已删 → `modules/script-manager.md` §信息展示） |
| NT-068 | **脚本管理器（改名 + 数据库化 + 集合 + 信息面板分节）**：`Module.PcTools` → `Module.ScriptManager`（程序集 / 命名空间 / 模块键 `pc`→`script` / 菜单 Key `pc-*`→`script-*` / 中文页名）；四层模型「采集器 → 集合 → 视图 → 页面」取代四份扁平 JSON；模块独立数据目录（`config\script\` 手工数据 + `data\script\script.db` 观测缓存）；新增集合页 `script-collections`（主从两栏 + 置顶 / 最近 / 搜索 + 发送到信息面板）；信息面板改**集合选择框 + 按采集器分节**（顶部唯一 `集合 ▾`，一次一个集合，⛔ 无 Chip 条；节头状态着色 / 局部刷新 + 关分页）；`collectable` 可采集判定 + 手动扫描三态 + 「仅可采集」筛选；执行频率三层 + 保留策略（`latest` / `count` / `days`）；命令即采集器；⛔ 不做旧数据迁移 | P0 | ✔ **S1~S8 已完成（已验收 2026-10-19）** | backlog/script-manager/NT-068.md |

| NT-069 | **跨模块文件操作契约 `IFileOperationService`**（v3.66 配置整改第 ② 批次）：磁盘动作（改名/移动/复制/回收站删除）下沉 `Core` 一份实现，`Shared` 定接口；`VideoFileService` 删掉自带 `SHFileOperationW`；确认框与库表记账留在各模块 | P1 | ✔ **已验收**（2026-10-19；批次 2 落地：接口 + `Core` 实现 + Shell 单例 + 视频工具去 `SHFileOperationW` + 3 个 VM 别名修 CS0104；defaults 示例随批次 3 一并落地；4 项静态检查 0 阻断，用户本地 build 通过；**后续批次 1/2/4（媒体资产委托 / `IVideoHandoff` / 文档回写）仍按「以后讨论后再实现」暂停**） | 🗑 已蒸馏（卡已删 → `framework.md` §6.6） |
| NT-070 | **启动提速（模块初始化两段式）**：`IModule` 增 `InitializeDeferred`（默认空实现）—— `Initialize` 只留菜单注册（首帧前），建库/清表/读配置/种子播种/扫描器接线/到期清理入队全部搬进 `InitializeDeferred`，由 Shell 在 `Window.Show()` 之后用 `DispatcherPriority.Background` 驱动；带启动打点写 `logs\startup-*.log` | P1 | ✔ **已完成（已验收 2026-10-19）** | 🗑 卡已淘汰（滑动窗口；已蒸馏进 `framework.md` §3.3） |

| NT-071 | **设置页骨架统一**：节结构（折叠卡片 + 磁吸抬头 + Push + 滚动自动展开 + 全部收起 + 节内工具条 + 滚动定位）抽成 UI 库 `SettingsSectionHost`，`SettingsSection` 上移 `NewToys.UI`；应用级设置页变薄壳（VM 删折叠命令，改 `SectionToggled` 回调）；4 个模块设置页改用同一骨架（Script/Web/Video `Sticky=False`，Assets 保留磁吸） | P1 | ✅ **已完成**（2026-10-18 用户 build 通过；🔴 三个实测坑：① 节内容父级 DataContext 是节模型 ⇒ 宿主对 `DataContext==null` 的节内容补页面 VM；② `Sections` 标 `[Content]` 抢掉 `UserControl.Content` ⇒ AVLN3000；③ XAML 里直接写节 ⇒ 该页不被预编译 ⇒ 运行时 `No precompiled XAML found`，最终 = Resources 面板 + code-behind 建节） | 🗑 已蒸馏（卡已删） |

| NT-072 | **集合页防误删**：左栏标记「其他页面使用中」的集合（= 信息面板 `lastCollectionId`）并**无条件沉底**；删除保留二次确认，使用中时文案加警示 | P2 | ✔ **已完成（已验收 2026-10-19）** | backlog/script-manager/NT-072.md |
| NT-073 | **脚本 / 命令「多库索引」+ kind 真正生效**（合并原规划 073/074）：信息面板只列 `kind=info` + 非 info 集合禁用刷新 / 视图字段；库 = `kind∈{script,command}` 的集合，**库页 = 该 kind 全部库的可采集成员并集**（带「所属库」列）；每库 `rootPath`（空 = 全局脚本目录）；内置两库改名「默认脚本库 / 默认命令库」且 kind 锁死；删库孤儿收回默认库；「在用」按 kind 分键 | P1 | ✔ **已完成（已验收 2026-10-19）** | backlog/script-manager/NT-073.md |

| NT-075 | **配置写入基建**：`AtomicJsonFile` 原子写（temp + `File.Move`）+ **默认值不落盘 / 空文件删除**（懒创建）+ 启动清理空壳 + `winget.json` 改名升级；`default` 配置 JSON **15 → ≤12** | P1 | ✔ **已完成（已验收 2026-10-19）**（17 处写入点统一 + `ConfigStoreService.PruneEmptyConfigFiles` + `ScriptStoreService` 去重；约定已蒸馏进 `framework.md` §7） | 🗑 已作废（卡已删 · 滑动窗口淘汰，内容全在 §7） |
| NT-076 | **单库收编**：`data\script\script.db` 并入 `newtoys.db` + 6 份内容型 JSON 建表 + `ILibrarySchemaContributor`（DDL 统一入口 / `user_version` / `VACUUM INTO` 迁移前快照）+ WAL + 表名前缀纪律（`asset_*`/`video_*`/`script_*`/`web_*`） | P0 | ✔ **两批已完成（已验收 2026-10-19）** | backlog/framework/NT-076.md |
| NT-077 | **配置分层**：机器级 `env.json`（+ 命令行/环境变量覆盖链）/ 框架级 `shell.json`（合并 ui·theme·window·favorites·columnlayout）/ 模块级 `modules\<模块>.json`；删 `app.json` 的 `VideoDbFolder`（写死 profile 绝对路径的现存缺陷） | P1 | ✔ **两批已完成（已验收 2026-10-19）** | backlog/framework/NT-077.md |
| NT-078 | **备份路由**：`IBackupSection` + zip 容器（manifest + 分节）+ **两入口**（设置备份默认 / 数据备份可选）+ 数据节 `VACUUM INTO` + 旧 v1 备份只读兼容；收编 `AssetAllViewModel` 的 db 导入导出 | P1 | ✔ **A/B/C 三批已完成（已验收 2026-10-19）** | backlog/framework/NT-078.md |
| NT-079 | **可成长性三件套**（可拆）：模块开关 `disabledModules`（不注册菜单 / 不跑 DDL）/ 命令面板 `Ctrl+K`（复用菜单索引）/ 采集器骨架生成（ps1 **UTF-8 带 BOM** + manifest 模板） | P2 | 📝 草稿（2026-10-19 建卡，先做哪项待拍板） | backlog/framework/NT-079.md |
| NT-080 | **软件包定位收敛 + winget 源模型重写**：① 集合页删「软件包」类别（`kind=package` 废弃）；② 软件包页**只支持 winget**（apt 等跨平台另开页面）；③ 🔴 **v3.80 返工（用户定调）**：源 = 系统 winget 配置 ⇒ **源开关 / 分类 / 互斥 / 定义快照 / 工具条源选择框全部删除**，改由「命令库 → `winget 源`」集合的命令执行（`NeedAdmin` ⇒ UAC + 可见控制台）；设置页「软件源」节 = 只读 6 条命令 + 「＋ 加入命令库」（幂等）；搜索固定不传 `--source` + 补查显式源 | P0 | ✔ 已完成（首轮已验收 2026-10-19；v3.80 返工 2026-10-25 编译通过） | backlog/script-manager/NT-080.md |
| NT-084 | **抓取规则两页（目录管理 + 规则编辑器）**：① `web-scrape`（Key ⛔ 不改）标题改「抓取规则」，退化为**规则目录管理**（标准列表页，⛔ 不动骨架）；② **新增** `web-rule-editor`「规则编辑器」三段式自绘（命令列表 / HTML 源码面板 / 规则编辑）；③ 规则载体 = `data\web-tools\rules\*.py`（`SOURCE` + `RULES` + 可选四段函数），helper `sel/abs_url/clean/http` 住 Python 侧；④ **类名全换防幻觉**：`ScrapeTask→RuleSetMeta` / `ScrapeService→RuleCatalogService` / `Scrape*View→RuleCatalog*` + 新增 `RuleEditor*` / `RuleFileService` / `RuleRunService`；⑤ `Type=python` 作万能节点 | P1 | ✔ **已完成（2026-10-25 核对；两页与规则服务已随 NT-089 迁入脚本中心）**（🗑 **批 D「导入书源」已撤销 2026-10-20**：随包 `convert_legado.py` / `ConvertLegadoAsync` / `⬆ 导入书源` / legado 默认规则方言全部删除，规则改由 AI 或手写生成）（批 A/B 已实施 2026-10-19：类名全换 `RuleSetMeta`/`RuleCatalogService`/`RuleFileService` + 目录页 + `.py` 载体 + 旧数据迁移；批 C 已实施（规则编辑器新页 `web-rule-editor` + Python 内嵌规则引擎 + GBK 解码）；🗑 **批 D 已撤销 2026-10-20 = 去 Legado**：`⬆ 导入书源` / 随包 `convert_legado.py` / `ConvertLegadoAsync` / legado 默认规则方言全部删除，规则改由 AI 或手写生成）；**收尾批已实施**：`json:` 规则（Python 标准库自实现）/ HTML 查找（选区高亮 + 第 n/m 个）/ 🎯 定位命中；行号栏降级为「行 N / 共 M 行」；✔ **已完成（已验收 2026-10-20 build 通过）** —— 其中「规则编辑器」页由 NT-086 批 C 重构为调试集合） | backlog/web-tools/NT-084.md |
| NT-085 | **`SysInfoGrid` 上提 UI 库**（改名 `DynamicColumnGrid`）：供爬虫面板复用动态列；改 `src/UI` + `Module.ScriptManager` ⇒ 全模块重编，须与 NT-086 同批 | P2 | ✅ **已完成（已验收 2026-10-20 随 NT-086 同批 build 通过）**：`SysInfoGrid` → `src/UI/Controls/DynamicColumnGrid.cs` | backlog/framework/NT-085.md |
| NT-086 | **爬虫脚本面板（期①）**：① 目录页加分类 / 说明 / 角色筛选（分类值动态生成）；② 新增 `web-crawl`「爬虫面板」（脚本 ▾ + 三节：链接·目录 / 试下载 / 下载 + 底栏摘要）；③ `web-rule-editor` 重构为「**调试集合**」（五段槽位 `entry/pick/toc/content/save` + 试跑推荐 + 保存 flow）+ 撤出菜单；④ 脚本头配置（`# role/desc/category/works/artifact/ua/header/concurrency/delay-ms/timeout`）；⑤ **错误回显 + 📋 复制诊断**；⑥ P0 七个预设（rss/opds/sitemap/wp-json/mediawiki/links/images）+ Seeder + 探测；⑦ 三操作符 `text:/link:/url:`；⑧ runs 落盘 + 导出 CSV/JSON | P1 | 🔄 **执行中**（2026-10-20 用户指令执行，过 DoR；需求规格 §6.3.1 已按新范式改写；**批 A/B/C 已实施且 build 通过（2026-10-20 验收）**；**批 D/E/F/G 全部已实施且 ✅ 已验收 2026-10-20 build 通过**） | backlog/web-tools/NT-086.md |
| NT-087 | **爬虫脚本下载（期②）**：`🧪 试下载`（单条预览，不落产物）+ `⬇ 批量下载`（`downloads\<脚本>\<条目>\`）+ `next` 分页原语（去重 / `# max-pages` / 连续空停）+ 链式模式（无 toc 时跟进累积）+ 并发/间隔读脚本头 + 单条失败不停批 / 连续失败 3 次停批 + 可取消；下载器**内置** ⛔ 不脚本化 | P1 | ✅ **已完成（已验收 2026-10-20 build 通过）**：新增 `DownloadService`（内置下载器：并发 / 间隔 / 去重 / 续跑 / 取消）+ `RunStepAsync(mode=step)` + 面板 ②③ 节 + 行勾选 + `FlowService.ReferencesOf` 删除引用检查；`downloads\`/`trials\`/`cache\`/`runs\` ⛔ 不进备份（只有 `rules\` 进）。返修 3 处（类漏 `partial` / `CrawlEntry` 漏继承 `ObservableObject` / `MaxPages` 是 int 不是 int?） | backlog/web-tools/NT-087.md |
| NT-088 | **执行器上提 Core（`ScriptRunner` + `InterpreterResolver`：Module.ScriptManager → NewToys.Core）**：为 system 模块（收藏夹 / 快捷命令）提供跨模块执行契约（模块禁互引），顺带为命令链铺路；🔴 **改框架层，须用户明确批准才可执行** | P0 | ✅ **已完成（2026-10-22 用户本地编译通过）**：`ScriptRunner` + `InterpreterResolver` 已落 `NewToys.Core/Services`，脚本中心与系统工具各注册一份实例 | backlog/framework/NT-088.md |
| NT-089 | **脚本中心：规则侧搬家** —— `web-scrape` 退化为脚本库 `category=rule` 筛选态；`rules\*.py` 迁入 `<DataDir>\script\rules\`；`RuleFileService` / `RuleCatalogService` / `RuleRunService` 迁 Module.ScriptManager；预设遷入脚本库 | P1 | ✅ **S1 已完成（2026-10-22 用户本地编译通过）**：抓取能力整体平移入脚本中心；S2（`web-scrape` 规则页终态）已拆出 ⇒ **NT-095**（📝 待拍板，A/B/C 三选一） | backlog/script-manager/NT-089.md |
| NT-090 | **脚本中心：运行台搬家** —— `web-crawl` 泛化为「运行结果」（④ 运行记录节）；`flows\*.json` / `runs\` → `<DataDir>\script\`（含一次性搬迁）；集合新增类别 `crawl`（「抓取集合」） | P1 | ✅ **已完成（2026-10-22 用户本地编译通过）**：「运行结果」页（`web-crawl` 泛化）+ 运行记录节 | backlog/script-manager/NT-090.md |
| NT-091 | **新建 `Module.SystemTools`（系统工具，Order 25）** —— 命令库 `script-commands` + 收藏夹 `script-favorites` 平移 + 新增 `system-quick` 快捷命令 + `settings-system`（`config\system\*`）+ 新增一级菜单图标 `IconSystem`；收藏夹 = 本模块命令条目收藏 | P0 | ✅ **已完成（2026-10-22 用户本地编译通过）**：`Module.SystemTools` 建档（`system` / Order 25 / `IconSystem`）+ 命令库 / 收藏夹 / 快捷命令 / 设置四页；详见 `modules/system-tools.md` | backlog/system-tools/NT-091.md |
| NT-092 | **命令链 L3** —— 集合加执行语义：工具条 `▶ 依次运行` + `capture` 输出 + 变量占位符传参 + 失败策略（遇错停止 / 跳过继续）+ 可取消；⛔ 不新造 Step 模型、⛔ 不连线、线性 only；新增 **node(.js) 解释器**支持 | P1 | 📝 **草稿**（待细化；依赖 NT-089 + NT-090） | 📝 待建卡（backlog/script-manager/NT-092.md） |
| NT-093 | **出口代理与防反爬（WebTools）** —— `web-egress` 出口管理页 + `IEgressProvider`（Core/Shared 契约，WebTools 实现）+ 轮询 / 失败切换策略 + 出口 IP 探活；`py_worker.py` 加 proxy / 节流 / 429-503 退避 / UA 池；🔴 netsh / 网卡类操作降级为「生成命令供用户自跑」（无提权） | P1 | 📝 **草稿**（待细化；依赖 NT-089 + NT-092） | 📝 待建卡（backlog/web-tools/NT-093.md） |
| NT-094 | **设置拆分与文案收尾** —— WebTools 设置瘦身（超时 / 并发 / 解析器 → script）；一级菜单显示名「脚本中心」；新二级图标落地；备份段重复显示检查 | P2 | 📝 **草稿**（待细化；依赖以上全部） | 📝 待建卡（backlog/fr
| NT-095 | **`web-scrape` 抓取规则页终态**（NT-089 S2 拆出）—— 三选一：**A** 保留独立目录页（089 就此结项）／**B** 合并退化为脚本库 `category=rule` 筛选态（规则页 10 项专属命令经基类 `ToolbarExtras` 插槽整体平移，功能零回退）／**C** 撤出左树、保留注册供跳转。🔴 属「删 / 改用户已确认的实现」⇒ **待拍板** | P2 | 📝 **草稿**（⏸ 待拍板，见卡 §二） | backlog/script-manager/NT-095.md |amework/NT-094.md） |
| NT-096 | **软件包页迁入系统工具 + 系统工具定位收紧**（2026-10-22 用户定调）：`script-packages`（winget GUI）整体从脚本中心平移到 `Module.SystemTools`（Key / 标题 ⛔ 不改）；~~「软件源（winget）」节从「脚本管理器设置」迁到「系统工具设置」~~（🔴 **同日返工：该节删除**，用户「系统工具模块下有软件设置页的节，没必要」）；软件包页的「集合」表达（工具条 `集合 ▾` + 行右键 `📥 加入集合`）删除；🔴 系统工具定位 = **当前操作系统的常用设置与快速命令**，⛔ 不收应用级设置页的功能 | P0 | ✔ **已完成（2026-10-25 编译通过）** —— 2026-10-22 用户指令「把软件包页移动到系统工具里」；返工：系统工具设置回到 2 节（执行 / 数据文件），源操作回归 `winget source …`；**组键 `system` → `system-tools`**（撞 Shell 隐藏宿主组，应用级设置 8 页曾被渲染进本组） | backlog/system-tools/NT-096.md |
| NT-097 | **系统工具页面重构**（2026-10-23 用户指令）：① **删 `system-quick` 快捷命令页**（内容并入命令库预设）；② **命令库对标脚本库**重做（列显隐 / 表头排序 / 行内 ▶ / Shell 工具条 / 详情卡，VM 继承 `ListPageViewModelBase`）；③ **随包预设** Windows 常用命令 + 快捷设置 + winget（`commands.seeded` 只播种一次）；④ **收藏夹升为模块下第一个页面**（Order 1） | P0 | ✔ **已完成（2026-10-25 编译通过）** —— 代码已落地（改动全落 `Module.SystemTools`），等用户本地 `accept.ps1`（与 NT-096 同批一次 build） | backlog/system-tools/NT-097.md |
| NT-098 | **命令编辑增强（视频工具）**：① `CommandLint` 参数预检（copy+滤镜/`-crf`+nvenc/`.gif`+x264/双 `-vf`/crf 越界…）→ 命令区上方黄字；② 预览页 `🧪 试跑 5 秒`（同源参数真转小样 + 右栏画面并排）+ `▶ 播放小样`（外部播放器 / `ffplay`）；③ 修 `-progress` 前置、进度改读 `out_time_us`/`out_time`、copy 剪切补 `-avoid_negative_ts make_zero`；批量页卡内 `🧪 试跑选中` | P1 | ✔ **已完成（2026-10-25 编译通过）** | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.5） |
| NT-099 | **转码新增 GIF 输出**：`TranscodePreset` 加 `gif`（无声 / 循环 / `.gif`）+ 详情卡「GIF 帧率 / 宽度」+ **两遍调色板**（`palettegen` → `paletteuse`，批量分派前置阶段）+ 复用区间（剪一段转 GIF）；⛔ 不新增任务类型 | P1 | ✔ **已完成（2026-10-25 编译通过）** | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.5） |
| NT-100 | **字幕：软字幕 / 硬字幕烧入**（2026-10-24 用户指令「视频工具下一批」）：`TranscodeTask` 加 `SubtitlePath / SubtitleMode / SubtitleFontSize / SubtitleMarginV`；软字幕 = 第二条输入 + `-map 0 -map 1 -c copy -c:s srt`（输出强制 `.mkv`、不重编码）；硬字幕 = 滤镜末位 `subtitles='…':force_style='…'`（copy 预设自动升级 libx264）；两页卡加「字幕文件 / 方式 / 字号 / 边距」+ Actions `📝 选择字幕文件…`；预检加 4 条。**同批 UI 增量**：详情卡新增 `FieldKind.Pair`（宽 × 高一行两个框），视频两页的分辨率 / 默认宽高上限改用 `Pair` | P1 | ✔ **已完成（2026-10-25 编译通过）** | 🗑 已蒸馏（卡已删 → `modules/video-tools.md` §3.6） |
| NT-101 | **多段剪切 + 拼接**（2026-10-24 同批）：`CutTask.SegmentsText`（一行一段 `开始-结束`）+ `CutSegments.Parse` + 缓存 `Segments`；≥2 段 ⇒ `BuildCutArgs` 转 `-filter_complex`（`trim/atrim` + `setpts/asetpts` → `concat`，强制 libx264 + aac）；两页卡加「多段剪切」多行框 + 解析提示行；多段时忽略开始 / 结束 | P1 | ✔ **已完成（2026-10-25 编译通过）** | backlog/video-tools/NT-101.md |
| NT-102 | **画面裁剪 + 黑边探测**（2026-10-24 用户指令「视频工具下一批」）：`TranscodeTask` 加 `CropWidth / CropHeight / CropX / CropY`；`FfmpegService.CropFilter`（宽高取偶）+ `DetectCropAsync`（2 秒 `cropdetect=24:2:0`，解析最后一条 `crop=W:H:X:Y`）；滤镜顺序 旋转 → 裁剪 → 缩放 → 字幕；copy 预设遇裁剪自动升级 libx264；两页卡加「裁剪（宽×高）」「裁剪起点（X×Y）」+ Actions `🖼 探测黑边`；预检加 3 条 | P1 | ✔ **已完成（2026-10-25 编译通过）** | backlog/video-tools/NT-102.md |
| NT-103 | **音轨：静音 / 音量增益 / 替换**（同批）：`TranscodeTask` 加 `AudioMode`（`AudioModes.Keep/Mute/Volume/Replace`，下拉单选保证互斥）+ `VolumeDb` + `ReplaceAudioPath`；静音 → `-an`；音量 → `-af volume=±NdB`（copy 预设音频改 aac）；替换 → 第二条 `-i` + `-map 0:v:0 -map 1:a:0` + `-c:a aac`；Actions `🎵 选择音频文件…`（挑完自动切「替换音轨」）；预检加 4 条 | P1 | ✔ **已完成（2026-10-25 编译通过）** | backlog/video-tools/NT-103.md |
| NT-104 | **按目标体积压缩**（同批）：`TranscodeTask.TargetSizeMb` + `FfmpegService.AutoBitrateKbps`（`MB × 8192 ÷ 秒 − 音频 − 8`，最低 50k）；优先级 体积 > 目标码率 > CRF，命令 `-b:v Nk -maxrate 1.5Nk -bufsize 2Nk`（单遍 ABR）；卡片填 MB + 反算提示行；预检缺时长 → 红字 | P1 | ✔ **已完成（2026-10-25 编译通过）** | backlog/video-tools/NT-104.md |
| NT-105 | **时间轴区间标记（视频预览）**（2026-10-24 用户指令「按你推荐的做一版」）：新增自绘控件 `Controls/TimelineTrack` 替掉「预览位置」滑条 —— 底轨 + 刻度尺（0.5s~4h 自动选档）+ 播放头 + **剪辑区间块**（单段整块 / 多段 ≥2 段逐段色块 / 未勾选「剪辑」35% 淡显）；点一下或拖动定位；VM 加 `SegmentList`（`CutSegments.Parse` 缓存，与命令同源）。开始 / 结束滑条保留 | P1 | ✔ **已完成（2026-10-25 用户本地 build 通过）** —— 改动全落 `Module.VideoTools` | backlog/video-tools/NT-105.md |
| NT-083 | **Python 运行时底座**：`env.json` 增 `python` 项（空 = PATH 探测，平台中立）/ 共享环境 **`%LOCALAPPDATA%\NewToys\tools\py`**（与 `env.json` 同级，⛔ 不进 profiles）/ 长驻 worker（行分隔 JSON + `-u -X utf8` + 超时杀进程树 + traceback 回传 + 空闲 10 分钟自动退出）/ **依赖安装交给随包脚本**（C# ⛔ 不写 pip 逻辑，播种成集合「环境维护」）/ 「运行环境」节显示共享环境状态 / ⛔ 不引 pythonnet；供 web-tools 与 script-manager 共用 ⇒ **落 Core（已批准）** | P1 | ✔ **批 1~4 已实施（2026-10-19）**：运行时 + worker + 环境脚本集合 + 「Python 共享环境（venv）」状态行；2026-10-25 编译通过；实机功能验收待用户 | backlog/framework/NT-083.md |
| NT-108 | **书签「移动到其他节点」**（2026-10-26 用户指令）：行右键新增 `📁 移动到` 子菜单 —— 目标 = 「（根级 · 未分组）」+ 树里全部文件夹（排除当前所在文件夹），选中文件夹行时置灰；执行 = 改 `BookmarkItem.Folder` 全路径 → 落盘 → 重建树 → 目标祖先自动展开 + 选中跟到新行。配套修「右键默认不改选中项」：`ContextRequested` 先把命中行设为 `SelectedNode`。**追加（用户拍板 1.1）**：列表改 `SelectionMode=Multiple`（`Ctrl`/`Shift`）⇒ 同一子菜单对整批生效（磁盘只写一次），多选高亮重建后按 `BookmarkItem.Id` 回填、`Materialize` 期间忽略 View 回抛。⛔ 不做行拖拽（页面是扁列表，不是 `TreeView`） | P1 | ✔ **已完成（2026-10-26 实施，待用户本地 build）** —— 改动全落 `Module.WebTools` | backlog/web-tools/NT-108.md |
| NT-109 | **「书签管理」显示名改「网址收藏」**（2026-10-26 用户指令）：菜单项 / 说明书 / 状态栏 / 空态文案统一为「网址收藏」；🔴 菜单 Key `web-bookmark` 与类名 `Bookmark*` **⛔ 不改**（改 Key 会让 `columnlayout.json` 偏好失效、改类名零收益）；「导入浏览器导出的书签 HTML」动作仍叫「导入书签」（文件语义）。功能零变化 | P2 | ✔ **已完成（2026-10-26 实施，待用户本地 build）** | backlog/web-tools/NT-109.md |
| NT-111 | **Hosts 管理合单页 + 源清单进设置**（2026-10-26 用户指令「删除当前的系统hosts页，把对应的功能都放到hosts管理页面；在网络工具设置里添加开源项目的编辑列表，预设的开源项目可以删除，也可以新建；备份windows内置的那份」）：① 撤 `web-hosts-system`（菜单 / View / VM 删）—— 展示 / 文本编辑 / 备份 / 还原 / 清除受管块全部并入 `web-hosts`；② `web-hosts` 改**双列表工作台**（左「当前系统 hosts 或备份」/ 右「方案」）＋ 条目级 `＋`/`－`/`≠` 差异 + 「差异」过滤 + 详情卡条目增删改；左栏改动进内存草稿，`✅ 写入系统 hosts` 一次确认 + 一次备份落盘；③ 备份只针对系统 hosts 原文，备份即左栏数据源的一项（可选 / 可还原 / 可删）；④ 源清单迁入设置页「Hosts 开源项目源」节（预设可删、可新建 / 改地址 / 测试拉取），Hosts 页只留只读下拉 + 拉取 | P1 | ✔ **已完成（2026-10-26 实施，待用户本地 build）** —— 改动全落 `Module.WebTools` | backlog/web-tools/NT-111.md |
| NT-112 | **Hosts 管理页多选 + 批量校验**（2026-10-26 用户指令「hosts管理页也应支持shift，Ctrl等多选，并能实现校验是否有效，应把shift，Ctrl多选写进文档，这样的体验就类似windows资源管理器了」）：① 两栏列表 `SelectionMode=Multiple` + **隧道阶段接管修饰键**（`Ctrl` 增选 / `Shift` 连选 / `Ctrl+A` 全选），两栏互斥、计数追加「已选 N」、重建后按行号回填高亮；② `🧪 检测选中`（只验选中条目，结论进「状态」列；未多选 ⇒ 退回整份方案检测；选中即测含禁用行）；③ `✅ 启用选中` / `⏸ 禁用选中`（左栏改草稿、右栏改方案内存）；④ 左栏检测结论走页面缓存（草稿文本不存结论）；⑤ 通用多选口径新增 `ui.md` §6.10，双写说明书 §四 / §五 | P1 | ✔ **已完成（2026-10-26 实施，待用户本地 build）** —— 改动全落 `Module.WebTools` | backlog/web-tools/NT-112.md |
| NT-113 | **存储分层 + 按模块拆库**（2026-10-26 用户指令「媒体资产模块的数据也放进对应的配置文件夹内…用 newtoys.db 会搞混淆的吧」→ 拍板最彻底方案）：① 目录分层 `<配置根>\{config, store, cache, logs}` —— `store\` = 用户数据（模块库 / 规则 / 流程 / 运行记录 / 脚本实体），`cache\` = 可重建（缩略图 / 页面缓存 / 下载中间产物 / 升级前快照 / 恢复副本），旧 `data\` 撤销；② **一个模块一个库**：`store\assets\assets.db`（13 表）/ `store\video\video.db` / `store\web\web.db` / `store\script\script.db`，各模块派生 `AssetsDb`/`VideoDb`/`WebDb`/`ScriptDb` 注册为单例（⛔ Shell 不再注册全局 `SqliteDatabase`；⛔ 无选库 UI —— NT-076 否决的是多库切换，不是一模块一库）；③ `ILibrarySchemaContributor` 加 `DatabaseKey`、`EnsureSchema()` 返回 `SchemaOutcome`，快照下沉到 `SqliteDatabase.ApplySchema`（`cache\db-backups\`，只对旧库升级做）；④ 迁移器 `StorageLayoutV2`：整目录 rename → 逐表拆库（目标空表才搬、DDL 幂等化、旧库改名 `store\legacy\newtoys-v1.db`）→ 可重建内容归 `cache\` → `store\.layout-v2.done` 标记，可重入；⑤ 备份节 Id 全不变（`data.db` 改扫 `store\*\*.db` 逐个 VACUUM INTO），恢复落 `store\restore-<时间戳>\`；⑥ 设置页「路径配置」新增「缓存目录」行 | P1 | ✔ **已实施（2026-10-26，待用户本地 build）** —— 落 Shared/Core/Shell + 4 模块，静态检查 PASS | backlog/framework/NT-113.md |
| NT-110 | **Hosts 管理扩展**（2026-10-26 用户指令）：① 新增 `web-hosts-system`「系统 Hosts」页 = 系统 hosts 现状展示（受管块高亮）+ 手动编辑保存 + **备份 / 还原 / 删除 / 打开目录** + 清除受管块；② 方案页加**源订阅**（预设 GitHub 加速类项目 + 镜像 fallback + 自定义源）、**三项检测**（条目可用性 DNS 对比 + TCP、源失效跌幅判定、静态安全规则）、**diff 预检 + 二次确认 + 自动备份**的按需替换；③ 导入本地文件 / 导出到文件 / 条目搜索 / 「健康」列 | P1 | ✔ **已完成（2026-10-26 实施，待用户本地 build）** —— 改动全落 `Module.WebTools` | backlog/web-tools/NT-110.md |
| NT-114 | **全部资产：清理重复 + 媒体信息 + 多选开关**（2026-10-27 用户口述三条痛点）：① `⤫ 从库移除` = 只删库存记录（`assets` + `file_locations` + 挂接），**⛔ 磁盘零操作** —— 补上"整理索引"缺失的那条手段（回收站要搬系统回收站、从磁盘删除是真删，都不能用）；② `🔍 查重合并` 把 `MergeDuplicatePaths` 的入口搬到列表页（原先只在「存储源」页）；③ `📊 读媒体信息`（勾选批量，走 TaskQueue）+ 详情卡 `⚡`（单条），复用 `MetadataService`，**只跑还没读过的** ⇒ 幂等；④ **资源管理器式多选**：撤勾选列与「多选开关」，列表 `Multiple` + 隧道接管 `Ctrl` 增选 / `Shift` 连选 / `Ctrl+A` 全选，选中真相 = VM `SelectedRows`（高亮 == 详情 == 批量对象） | P0 | ✔ **已完成（2026-10-27 实施，待用户本地 build）** —— 改动全落 `Module.MediaAssets`；静态检查 PASS | backlog/media-assets/NT-114.md |
| NT-107 | **页面注册编译期化**（2026-10-25 用户指令「按你推荐的继续」）：`IMenuService.RegisterFunction` 改泛型 `RegisterFunction<TView>`（视图名取 `typeof(TView).Name`，**删除字符串版**）⇒ View 类改名漏改注册 = 编译不过，不再靠 lint 兜；36 处注册点机械迁移（MediaAssets 8 / ScriptManager 7 / Shell 8 / WebTools 5 / VideoTools 4 / SystemTools 4）；`check-view-pairs.ps1` P-02 改判泛型实参；新增 `MenuServiceTests`（视图名 / 排序 / 同 Key 更新语义 / 跨组暂存补登）。**零可见变化** | P1 | ✔ **已完成（2026-10-25 用户本地 build 通过）** —— 改动落 Shared/Core + 6 模块注册点 | backlog/framework/NT-107.md |
| NT-106 | **书签树状展开（整页单树）**（2026-10-24 用户定调「不要左树，整页按树展开」）：`BookmarkItem.Folder` 由「末级名」升为「`/` 分隔全路径」（老数据零迁移）；修 `ImportHtml` —— 改成**按 `<A>` 祖先链反推**全路径（不再依赖 H3/DL 遍历顺序，两种导出解析形态都认）；列表改**树**（`BookmarkNode` 视图节点混排文件夹行/书签行，缩进 16/级，行高 28，一期无图标）—— ⚠️ 实现用「先序拍平 + 缩进列」而非 `TreeView`（`TreeViewItem` 自动缩进会把整行右移、列必然与表头错位）；筛选重建只留命中+祖先并自动展开；排序收窄为同级内、「分组」列置灰；树形态取消分页只留计数（计数落 Shell 状态栏）。**追加（同批）**：工具条 `打开方式` 下拉复用「设置 → 启动与行为」的浏览器1/2 —— 文件夹行 `全部打开`/`全部检测` 走 `LaunchRunner.OpenBrowser` **临时调用、不写配置**，书签行 `写入浏览器配置` 经 `ILaunchConfigService.Save` **追加去重**（app.json `Launch` 增第二写入点，用户已授权） | P1 | ✔ **已完成（2026-10-25 用户本地 build 通过）** —— 改动全落 `Module.WebTools` | backlog/web-tools/NT-106.md |
| NT-082 | **软件包集合（跨后端包清单预设）+ 集合右键发送到对应页面**：① 集合类别加回 `package`（成员 `role=package`，`Target`=包 Id；与包条目一对一容器）；② 「📤 发送到信息面板」→「📤 发送到对应页面」，按 kind 跳 info / script / command / packages 并写 `lastCollectionIdByKind`；左栏集合行右键同项（2026-10-19 追加微调：右键文案动态化「📤 发送到{目标页名}」，随选中集合类别变）；③ 软件包页 `集合 ▾` 显示包清单（状态按 `winget list` 比对）+ 行右键 `📥 加入集合`（幂等）。源命令（含中科大）已由 NT-080 提供，本次未改 | P1 | ✔ **已完成（2026-10-25 编译通过）** | backlog/script-manager/NT-082.md |

| NT-081 | **跨平台中立化**：RID 条件化 / Windows-only P/Invoke 守卫收口 / `IProcessRunner` 平台底座（取消进程树 + 编码统一；三类进程用法分立）/ 解析分层（winget 启发式 + `FixedWidthTable` 通用机械 + `TsvRows` 机读）/ 后端适配器（`AptPackageService` 只读，按平台二选一注册）。日常生效部分 = `framework.md` §2.1 六条硬约束 | P3 | 🔄 **执行中**（步骤 1~5 已实施 2026-10-19：RID 条件化 + Shell 守卫 + `IProcessRunner` 迁 2 处 + 解析三层拆分 + apt 只读适配器；⏳ 待本地 build；未做：Linux 实机验证 / P/Invoke 物理集中 / apt 写操作） | backlog/framework/NT-081.md |

> `📝 草稿` 需继续展开细节，升 `✅ 就绪` 后才会被批量执行；`✔ 已完成（2026-10-25 编译通过）` 经用户验收后台账翻 ✔（任务卡默认保留；作废卡已删并标 `🗑`）。
> NT-048（分屏贴靠首次几何不收敛）**已作废**（当时的「系统级无法可解」结论已被推翻）：2026-10-21 实测根因为「系统按外框 = 客户区 + 8px/边 算贴靠矩形 × 本窗口零内缩」⇒ 由 `FixTiledGeometry` 纠回精确半屏，NT-048 那套「按屏幕自检」仍⛔ 不复活。

### P1 · 技术债（未结）
> 已结项不在此重复登记（编译绑定 = 规范 B1；旧 `NewToys.slnx` = B6；`BoolInversionConverter` = B8；v3.15 批量页勾选框 = v3.16 已落地），明细见 `framework.md` §10 与「二、已完成」。

| # | 问题 | 状态 / 建议 |
|---|---|---|
| B1 | 编译绑定逐项目关闭（View 无 `x:DataType`） | 保持关闭（已写入规范 B1） |
| B3 | 全局样式键用 `Classes` 隐式样式 | 收益有限暂挂 |
| B4 | `NavigationService` 主流程闲置 | 删闲置服务或改用它（属框架层需同意） |

> v3.12 ~ v3.22 各批（视频工具详情卡统一 / 精细化 / 导入拆分 / 增强 / 六项 / 整改批 / 标签升级 / 反馈修订）**全部已完成并验收**，逐次明细见 `git log` 与「五、变更流水」，此处不再重复登记（原文 80 行已删）。

---

## 四、关键文件索引

| 关注点 | 文件 |
|---|---|
| 启动 / DI / 模块装配 / 主题字典(31 键) / 系统明暗监听 / 图标 Geometry 定义 | `src/Shell/App.axaml.cs` + `src/Shell/App.axaml` |
| 主窗口（标题栏 / 标签栏 / 左树 / 内容区 / 右详情 / 状态栏） | `src/Shell/Views/ShellView.axaml(+cs)` |
| Shell 导航状态（Tabs / ActiveTab / OpenTab / CloseTab …） | `src/Shell/ViewModels/ShellViewModel.cs` |
| 标签页模型 | `src/Shell/ViewModels/TabItemViewModel.cs` |
| **UI 契约** | `src/Shared/Services/IDetailPanelProvider.cs` / `IHistoryProvider.cs` / `IStatusProvider.cs` |
| 首页 | `src/Shell/Views/HomeView.axaml` + `ViewModels/HomeViewModel.cs` |
| 模块加载 / 菜单服务 | `src/Core/Services/ModuleLoader.cs` / `MenuService.cs` |
| 主题 / 强调色 | `Core/Services/ThemeService.cs` + `Shared/Services/AccentPalette.cs` |
| 配置存储 / 多配置 | `src/Core/Services/ConfigStoreService.cs` + `ProfileService.cs` + `Shared/Services/IProfileService.cs` |
| 列显隐 + 页面级开关 + 左树状态 | `Core/Services/ColumnLayoutService.cs` + `Shared/Services/IColumnLayoutService.cs` |
| 脚本 / 命令共用基类 + 运行 | `Core/Services/QuickActionService.cs`（NT-068 起为 `script.json` 适配器）+ `Shared/Models/QuickAction.cs` + `Module.ScriptManager/ViewModels/QuickActionListViewModel.cs` |
| 默认数据 | `src/Shell/defaults/sysinfo/*`（内置采集脚本，UTF-8 带 BOM + `.seeded` 只加不覆盖）；⛔ 无 `.default.json` 清单（v3.60 起） |
| 本地数据库 | `Core/Services/SqliteDatabase.cs` + `Module.VideoTools/Services/VideoMediaIndex.cs` |
| 会话 / 运行钩子 | `Core/Services/DialogHooks.cs` + `Module.VideoTools/Services/VideoFileService.cs` |
| 视频工具 | `Module.VideoTools/Views/{VideoList,VideoPreview,VideoBatch}View.axaml` + `Services/{VideoScanner,FfmpegService,VideoTaskStore}.cs` |
| 运行环境 | `Shared/Services/IAppConfigService.cs` + `Core/Services/AppConfigService.cs` |
| 脚本管理器数据层 | `Module.ScriptManager/Services/{ScriptStoreService,ScriptDatabase,ScriptScanner,CollectableEvaluator}.cs` |
| winget | `Module.ScriptManager/Services/PackageManagerService.cs` + `PackageOutputParser.cs` + `PackageSettingsService.cs`（NT-074：整套从 Core / Shared 下放；NT-080 v3.80 后源管理已删除） |
| 加密 DNS | `src/Core/Services/EncryptedDnsService.cs` |
| 模块菜单注册（Key 权威源） | 各模块 `XxxModule.cs` |

---
