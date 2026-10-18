# NewToys 项目状态与路线图

> v3.78 · 2026-10-18 · 台账
> **标准角色（术语见《AI协作手册》【术语对照】）**：本文件 = **项目状态与路线图**（Status & Roadmap），三、节 = **Backlog 工作项台账**。
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：`index.md`（文档地图）/ 需求规格 / `framework.md` / `ui.md` / `modules/<模块>.md` / AI 协作手册 / AGENTS.md（跨 AI 入口）

**最近三次变更**
- **v3.78（🗂 整理期 2026-10-18）**：**文档一致性治理 + 任务卡归档**（用户「统一 skill 与 src、清理已作废旧信息」）—— ① 模块设置文件名校正：`script-manager.json` / `web-tools.json` / `video-tools.json` / `media-assets.json`（⛔ 不是 `script.json` / `web.json` / `video.json`；`script` 模块的分区名 ≠ 模块键）；② `index.md` 新增 **§四「使用层」写入规范**（用户口述的新功能 / 交互行为 / 使用场景 / 功能说明 / 踩坑各有唯一落点 + 当场落 + 改行为双写）；③ 软件说明书改定位为**使用层 · 用户手册**（576→467 行，删「外部依赖」「重写指引」两节，数据节压成「数据在哪 / 备份什么」）；④ **删卡 9 张**：`framework/NT-054/055/057/058`（已验收，结论在 `ui.md` §7 §8 + `framework.md` §4.5 §6.3 §6.4）、`settings-app/NT-009/050/051/053/056`（模块已撤销，整目录删除）；⑤ 需求规格现行段落旧称全量校正 + 版本记录压到最近三次；⑥ 台账 / `AGENTS.md` 版本记录同步压缩。⛔ 未动 `src/`。
- **v3.76（🗂 整理期 2026-10-18）**：**文档按「框架层 / UI / 每模块一份」重构**（用户「拆分文档，框架层 / ui / 各功能模块各一个 md，待办细节每模块只留最新 5 份」）—— ① 原《NewToys-工程规范.md》（892 行）拆为 **`framework.md`**（框架层：程序集 / 启动 / 菜单 / 加载 / UI 契约 / 配置 / 构建 / 技术债 B1~B17 / **跨模块待办 §11**）+ **`ui.md`**（尺寸密度 / 主题键 / 图标 / CSD / 列表页 / 详情卡 / 工具条 / 样式坑），原文件删除；② 新增 **`modules/script-manager.md` / `media-assets.md` / `video-tools.md` / `web-tools.md` / `settings-app.md`**（统一 6 节模板：职责 / 当前功能 / 实现逻辑 / 数据 / 坑点 / 待办）+ **`index.md`**（文档地图 + 模块清单表）；③ 任务卡按模块分目录 `backlog/<模块>/`（跨模块 `backlog/framework/`），**归档策略改滑动窗口：每目录 ≤5 张已验收卡、未验收不参与淘汰、淘汰前必须先蒸馏进模块 md**（10-14「全量保留」/10-17「默认保留」均废止），本批已蒸馏并删除 27 张卡（framework 18 / media-assets 8 / script-manager 1）；④ **台账与代码对齐**：§2.3 页面全景、§2.4 菜单清单按 `src/` 现状重写（脚本管理器 `pc-*`→`script-*` 6 功能页 + 设置、媒体资产 5 类型页已删 → 7 页、菜单项 34 项），工作项详情列改新路径；⑤ `AGENTS.md` v1.7 / `skill/SKILL.md` / `.workbuddy/skills/newtoys/SKILL.md` / `newtoys-spec.md` / `collab-method.md` / `README.md` / 手册 v3.24 的文档路由与归档策略同步改写。⛔ 未动 `src/`。
- **v3.75（🗂 整理期 2026-10-17）**：**文档搬入 skill + 任务卡归档策略修订 + 表述收敛**（用户「把doc文件移到skill里，封装，删除原有doc目录」）—— ① 🔴 **`src/docs/` → `skill/docs/`** 整体搬迁（git mv，4 份文档 + `backlog/`），`src/` 从此只放代码；`AGENTS.md` / `README.md` / `skill/SKILL.md` / `skill/references/*` / `check-techstack.ps1` / `.workbuddy/skills/newtoys/SKILL.md` 的路径引用同步改写，铁律 1 例外扩到 `skill/`（`accept.ps1` 改动范围判定原本已放行 `skill/`，零改动）。② 🔴 **归档策略修订**（原 2026-10-14「全量保留 ⛔不删」定调作废）：任务卡**默认保留**；**内容被后续工作项整体取代 / 明确作废的卡删除**，台账行标 `🗑 已作废（卡已删）`——本批已删 **NT-004 / NT-017 / NT-028 / NT-031 / NT-043 / NT-048** 共 6 张（NT-067 保留：`ScriptRunner` / `SysInfoGrid` 仍在使用）。③ **skill 与代码对齐**：`newtoys-spec.md` v1.5（补 `NewToys.UI` 程序集 / 数据落点改 `profiles\<配置>` + `app.json` 应用根 / 底栏 28px 口径 / 5 条踩坑硬约束 / G 规则号订正）；`tech-guard.md` 删「§七 与本文件重叠（待收敛）」过期告警 + 标注 `G-12`/`G-13` 未启用 + §三 补 `InitializeComponent` 覆盖；`check-techstack.ps1` 注释规则号订正。④ **本台账自身收敛**：按本文件自定规则「⛔ 只保留最近三次变更」**截断 v3.72 及更早**（归档为一行，明细看 `git log`）；工作项索引表按 NT 号升序重排（原 NT-025/026 倒置、NT-058~068 压在 NT-056 后）。⑤ **脚本评估结论**：`check.ps1` 新增 **`.axaml.cs` 是否调用 `InitializeComponent()`** 检查（NT-063 两次「整块 XAML 静默不加载」事故的静态兜底，`App.axaml` 豁免），实跑 0 告警；其余 3 个检查与 `accept.ps1` 编排无缺口，⛔ 未改构建脚本。文档：需求 v3.72 / 手册 v3.23 / 本台账 v3.75。
- **v3.74 ~ v3.54（归档）**：NT-063 控件库（P0-a/P1/P2-a）/ NT-064 分页 / NT-067 信息展示页 / NT-062 设置归位与树状设置页 / NT-055~059 工具条·详情卡 IconBar·脚本索引库 / 标签栏改版 / 首页标签退出标签栏 —— 逐条明细见 `git log`（本台账只留最近三次变更）。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.72 ｜ framework v4.0 ｜ ui v4.0 ｜ 模块 md v1.0 ｜ 协作手册 v3.24 ｜ 本台账 v3.76 |
| 技术栈 | .NET 10 + Avalonia 12.1.4 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 8 项目 / 274 个 `.cs+.axaml+.csproj` / 约 51100 行 |
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
- 明暗主题全局切换，键一律 `*Brush`（**31 个**：v3.38 由 32 增至 34，新增 `WindowBorderBrush` / `WindowBorderInactiveBrush`；NT-049 再删 3 个零引用键 → 31），默认深色。
- **视觉规范**：共享样式已按 **v3.12 密度基线**收紧（表格行高 28、卡片内边距 12、工具条 12,6、筛选条 12,4）；**框架层图标已全矢量**（`Path` + `App.axaml` 的 `Icon*` 键：一级菜单 / 标题栏 / 标签页 / 左树，NT-035 / NT-045 / NT-046）；⚠️ **数据驱动图标**（二级菜单 / 首页一键按钮 / 模块页内数据行）仍是字符串 → 技术债 NT-047。

### 2.3 页面全景（2026-10-18 按 `src/` 现状重写）

| 模块 | 菜单项 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 脚本管理器（`script`） | 7（6 功能页 + 设置） | **7** | 0 |
| 网络工具（`web`） | 6（5 功能页 + 设置） | **6** | 0 |
| 视频工具（`video`） | 4（3 功能页 + 设置） | **4** | 0 |
| 媒体资产（`assets`） | 8（7 功能页 + 设置） | **8** | 0 |
| 应用级设置（宿主 `system`） | 8 节锚点（六节 + 功能清单） | **8** | 0 |
| 首页（`home`） | 1 | **1** | 0 |
| **合计** | **34** | **34** | **0** |

> **骨架页**：无（`web-http` 已由 NT-001 落成真实页）。
> v3.76：**媒体资产 5 个类型页已删**（`assets-video/-music/-book/-image/-document`，NT-063 P2-a）⇒ 8 = 7 功能页 + 设置；类型切换改由「全部资产」筛选栏驱动。

### 2.4 菜单清单（34 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是**默认页**，与其他标签一样常驻标签栏、不可关闭） |
| 脚本管理器 | script | 20 | `IconPc` | `script-favorites`收藏夹/0/✅ ｜ `script-library`脚本库/1/✅ ｜ `script-commands`命令库/2/✅ ｜ `script-packages`软件包/3/✅ ｜ `script-collections`集合/4/✅ ｜ `script-info`**信息面板**/99/✅ ｜ **`settings-script`脚本管理器设置/100/✅** |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP 测试/1/✅（工具条逃生舱）｜ `web-bookmark`书签管理/2/✅ ｜ `web-ping`网络 Ping/3/✅ ｜ `web-scrape`网页抓取/4/✅ ｜ `web-hosts`Hosts 管理/5/✅ ｜ **`settings-web-tools`网络工具设置/100/✅** |
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
| NT-001 | web-http 实装（唯一剩余骨架页） | P2 | ✔ 已完成（已验收） | backlog/web-tools/NT-001.md |
| NT-002 | pc-package 收尾（Parser 实机 + 批量队列） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-003 | settings-package 软件源（退出码验证） | P2 | ✔ 已完成（已验收） | backlog/script-manager/NT-003.md |
| NT-004 | defaults\ 与脚本目录播种确认 | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-005 | 模块热加载实装（规范 §3.7） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-006 | 快捷设置：运行改造（可见 pwsh+回显 / GUI 隐藏 / 切换 shell）+ 导入导出 / 清除缓存 | P1 | ✔ 已完成（已验收） | backlog/script-manager/NT-006.md |
| NT-007 | 设置与脚本：导入导出 + 右键编辑 / 打开方式 + 运行如双击 | P1 | ✔ 已完成（已验收） | backlog/script-manager/NT-007.md |
| NT-008 | 软件包：可升级对齐 winget update + Pin 显示 + 已装移状态栏 + 详情 bug + 已装导入导出 | P1 | ✔ 已完成（已验收） | backlog/script-manager/NT-008.md |
| NT-009 | 首页：收藏配色 / 一键操作固定底部 / 删工具箱行 / 状态栏模块功能数 | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-010 | 通用样式统一：激活强调色加深 / 按钮配色 / 导入导出控件一致 / 输入框水印 / 弹层样式 | P1 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-011 | 视频列表：删扫描按钮 / 数千视频优化 / 入库去重 / 删清空媒体缓存 | P1 | ✔ 已完成（已验收） | backlog/video-tools/NT-011.md |
| NT-012 | 视频预览：旋转播放按钮 + 命令卡旋转 + 按钮标题去后缀 | P2 | ✔ 已完成（已验收） | backlog/video-tools/NT-012.md |
| NT-013 | ffmpeg 卡：剪辑转码输入对齐 + 转码默认 H.265/CRF23/_H265/默认宽高 | P2 | ✔ 已完成（已验收） | backlog/video-tools/NT-013.md |
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
| NT-027 | 视频工具精简：删视频库(.db) / 标签 / 收藏页 → 收窄为「简易查看器 + 送批量任务」（含旧表一次性清理） | P0 | ✔ 已完成（已验收） | backlog/video-tools/NT-027.md |
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
| NT-047 | 数据驱动图标矢量化（二级功能菜单 34 项 / 首页一键按钮 / 模块页内数据行；⛔ 静态装饰 emoji 不动） | P2 | 📝 草稿 | backlog/framework/NT-047.md |
| NT-049 | 清理 3 个未引用标题栏主题键（`TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush`；主题键 34 → 31） | P2 | ✔ 已完成（已验收） | 🗑 已归档（卡已删，内容已蒸馏进文档） |
| NT-050 | 设置归位：模块专属设置迁模块（软件源 View/VM → PcTools，媒体资产先例模式）+ settings 内「媒体资产」改名「媒体资产设置」 | P1 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-051 | 设置页合并：主题外观 + 关于 并入 通用设置（单页三节；键 `settings-general` 不变，删 `settings-theme`/`settings-about` 两项） | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-052 | 「🔄 重载模块」移入设置（标题栏 3→2 按钮；入口 = **功能清单页**「增加功能」操作区；方案 A：Shared/Core 新增 `IModuleReloadService` 契约，2026-10-15 用户拍板） | P2 | ✔ 已完成（已验收） | backlog/framework/NT-052.md |
| NT-053 | 设置页窄界面适配：最窄 960×640（内容宽 720）与默认 1280×800 两种可用（根 ScrollViewer / 星号列 / 去超宽固定容器） | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-054 | 详情卡声明式规范：**三区模型**（`Fields` 字段区 / `Actions` 动作区 / `Custom` 逃生舱）+ 7 种字段 Kind + 3 种提交语义（`Instant`/`OnBlur`/`Explicit`）+ Shell 统一渲染，替代 16 张卡逐页手写 XAML（含债 B13 详情面板上提） | P1 | ✔ **已完成（已验收 2026-10-15）**（2026-10-15：📝 草稿 → ✅ 就绪 → **L0 + L1 交付并编译验收通过** → **L2 全数迁完 16/16**（打样页 `video-batch` 已验收 + 页内自绘 12 页 + MediaAssets 4 张独立视图；**L1 按用户拍板做了加法扩展**：chips 色块 / chips 命令参数取值 / List 项级命令 / chip 悬停提示；**L3 已删 8 个停用视图**，`Custom` 渲染分支仍未接）；**用户本地编译 + 实机回归通过（2026-10-15）；技术债 B13 结清**；遗留 = `Custom` 逃生舱渲染分支仍未接（现无页面需要）） | 🗑 已蒸馏（卡已删） |
| NT-055 | 工具条统一：Shell 声明式承载（`IToolbarSource` + `ToolbarHost`）+ 详情开关浮动（新契约 + Shell 新控件，**框架层首审项**；**已迁 18 个 View / 覆盖 23 页**（`settings-path` 另见 NT-056；三轮补迁 MediaAssets 6 页）；仅 `web-http` 走逃生舱） | P1 | ✔ 已完成（已验收）· commit `8da4462` | 🗑 已蒸馏（卡已删） |
| NT-056 | 设置总表：路径配置 + 配置 合并为一表（分类列区分行类型 / 决定工具可用性；菜单 35 → 34） | P1 | ✔ **已完成（已验收 2026-10-16）**· commit `66faa28` | 🗑 已作废（卡已删） |
| NT-057 | 系统托盘增强：左键切显示 / 隐藏 + 右键五项菜单（首页 · 设置 · 用另一个配置打开… · 退出）+ **托盘图标改用应用图标**（与 exe / 窗口同源，⛔ 不再代码画、⛔ 不写死强调色） | P2 | ✔ **已完成（已验收 2026-10-16）**· 2026-10-15：`CreateTrayIcon` → 唯一图标工厂 `CreateAppIcon(accent)`（窗口 + 托盘同源，随强调色）+ 新增 `Resources/app.ico`（exe 图标）；托盘五项菜单（②③ 先拉起窗口再导航，④ 子菜单排除当前配置 / 无其他配置时置灰）；技术债：**形状两处定义**（C# 常量与 ICO 口径，改一处须同步另一处） | 🗑 已蒸馏（卡已删） |
| NT-058 | 详情卡图标动作组：**第 9 种 `FieldKind.IconBar`**（32×32 图标按钮组）+ chips 分组（`ChipGroupPath`）+ 「全部资产」页打样定稿（**框架层首审项**） | P1 | ✔ **已完成（已验收 2026-10-16）** | 🗑 已蒸馏（卡已删） |
| NT-059 | 「设置与脚本」页改**脚本索引库**：导入收成 `SplitButton`「📂 导入」+ 导入后清筛选 + 详情卡 `IconBar` + 新增「分类」列 + 删除语义分叉 | P1 | ✔ **已完成（已验收 2026-10-16）** | backlog/script-manager/NT-059.md |
| NT-060 | **干净启动**：删示例数据（列表示例行 + 详情卡示例态）改**空态引导**（说明 + 入口按钮）+ 取消 `defaults\` 默认清单播种（⚠️ 实含**框架层减法**：`DetailCardDescriptor.SamplePath`/`SampleText` + `DetailCardRender.IsSample`） | P1 | ✔ 已完成（⏳ 待本地 build 验收） | backlog/framework/NT-060.md |
| NT-061 | **配置下沉**：Core 新增 **`IOptionsStore`**（KV + 按模块分区，先 json 后可换 SQLite）+ **五模块各一个设置页**注册进 `settings` + 功能入口页**数据源框可编辑**（回车 / 失焦提交，路径无效 ⇒ 状态栏红字 + ⛔ 不写盘） | P1 | 🚧 已实施（⏳ 待本地 build 验收） | backlog/framework/NT-061.md |
| NT-062 | **应用级设置归位**：`Module.Settings` **撤销** → 6 页迁入 Shell（`settings` 分组改由 Shell 创建）+ **「运行环境」**（外部程序路径上提应用根 `app.json`，与配置档同节）+ 设置项**显式分类** `SettingsCategory` + 设置页**搜索** / 每行**打开配置目录** / 外部程序缺失**惰性探测** + **功能清单移首页** + **软件源并入电脑工具设置** + 删 `GpuAcceleration` + 左树**设置齿轮**（`Ctrl+,`） | P1 | ✅ **已实施**（2026-10-16；①-⑥ 全部落地 = 删 `Module.Settings` + 6 页迁 Shell + Shell 建 `settings` 组 + `app.json` 上提应用根 + 解释器路径回迁 + 删 `GpuAcceleration` + **一页多节/节路由** + **`SettingsCategory` 显式分类** + 设置页搜索 + 每行「打开配置目录/文件」 + **外部程序惰性探测 + 状态栏 `⚠` 可点** + 齿轮/`Ctrl+,` + **功能清单移首页** + **软件源并入电脑工具设置**；AI 侧四项静态检查 0 ERROR / 0 WARN；⏳ 待用户本地 `build.ps1` / `accept.ps1` 验收） | backlog/framework/NT-062.md |
| NT-063 | **控件库 NewToys.UI + 列表页控件化**：ColumnGrid 五合一（2086 行）+ 详情卡渲染搬家 + 新底栏 ListFooterBar（P0-a）→ 列表页骨架（P1）→ 类型页合并 / 设置行控件（P2） | P1 | 🔵 **P0-a + P1 + P2-a 已实施**（2026-10-16；⏳ 待本地 build 验收。P2-a = 5 类型页整体删除 + 筛选栏类型切换驱动列 / 详情卡 / 状态栏；**P2-b 设置节 / 行控件未做** = 与 UI 库纪律冲突 + 重名 + 文档未载，待拍板；UI 库纪律 = 只放稳定控件 + 主题刷子留 Shell；⛔ 构建加速不做） | backlog/framework/NT-063.md |
| NT-064 | **列表分页**：UI 底栏归 NT-063，本卡做**数据层** —— SQL LIMIT/OFFSET + **排序下推** + 独立 COUNT(*)；底栏**默认隐藏、列表超出页底才显示**；批量操作只作用于当前页选中项 | P1 | 🔄 **已实施**（2026-10-16；样板页「全部资产」：基类分页状态 + `AssetService.AssetQuery`/`CountAssets`/`QueryPage`（WHERE 只写一份）+ 底栏插槽接线 + 页大小落 `shell` 分区；其余 15 页未迁；AI 侧 4 项静态检查 0/0；⏳ 待本地 build 验收） | backlog/framework/NT-064.md |
| NT-065 | **分页推广到「脚本收集 / 命令收集」两页**（`pc-shortcuts` / `pc-quicksettings`）：两页挂 `ListPageSkeleton` 的底栏 + VM 分页（数据源是 **JSON 清单 ⇒ 内存切片**，⛔ 不做 SQL）；同步菜单改名「设置与脚本→脚本收集」「快捷设置→命令收集」 | P1 | 📝 草稿（2026-10-17 建卡，待升就绪） | backlog/script-manager/NT-065.md |
| NT-066 | **标题栏搜索扩范围**：可搜**二级菜单项** + **设置页的节点**（现只搜一级 / 已注册项）；跨 Shell（框架层）⇒ 需用户授权 | P1 | 📝 草稿（2026-10-17 建卡，待摸底 + 授权） | backlog/framework/NT-066.md |
| NT-067 | **信息展示页（脚本输出呈现器，原系统信息 `pc-info`）**：`ScriptRunner` 底座（PcTools）+ manifest 驱动（columns/scripts + per-script interval + auto/timeout）+ 多脚本合并 + 按 RowKey 就地定时刷新 + 自定义表头 + 默认采集脚本播种 | P1 | ✅ **已实施**（2026-10-17 实施完成；D1/D2/D3/D5 按卡内假设落定；偏差 = 列渲染改用模块内 `SysInfoGrid`（Grid 子类，星号列宽必需）、定时刷新以「页面挂/脱可视树」为准（未单独判窗口最小化）、「⏱ 刷新间隔」= 会话内改 `auto` 脚本 interval（manifest 仍唯一持久化）；⏳ 待用户本地 build/accept） | backlog/script-manager/NT-067.md |
| NT-068 | **脚本管理器（改名 + 数据库化 + 集合 + 信息面板分节）**：`Module.PcTools` → `Module.ScriptManager`（程序集 / 命名空间 / 模块键 `pc`→`script` / 菜单 Key `pc-*`→`script-*` / 中文页名）；四层模型「采集器 → 集合 → 视图 → 页面」取代四份扁平 JSON；模块独立数据目录（`config\script\` 手工数据 + `data\script\script.db` 观测缓存）；新增集合页 `script-collections`（主从两栏 + 置顶 / 最近 / 搜索 + 发送到信息面板）；信息面板改**集合选择框 + 按采集器分节**（顶部唯一 `集合 ▾`，一次一个集合，⛔ 无 Chip 条；节头状态着色 / 局部刷新 + 关分页）；`collectable` 可采集判定 + 手动扫描三态 + 「仅可采集」筛选；执行频率三层 + 保留策略（`latest` / `count` / `days`）；命令即采集器；⛔ 不做旧数据迁移 | P0 | ✔ **S1~S8 已完成**（⏳ 待本地 build 验收；2026-10-17 立项并升就绪，用户拍板「放进一个 NT 里，一次执行」）；**S1 改名已完成**（commit `9eea91f`，用户 build 通过；程序集名保持 `Module.ScriptManager.dll`）；**S2 数据层已完成**（`ScriptStore` 模型 + `ScriptStoreService`（`config\script\script.json` 原子写）+ `ScriptDatabase`（`data\script\script.db` 两表 + 保留策略三态）+ 内置集合播种 + 启动即生成，commit `6bfe6e8`，用户 build 通过）；**S3 切库 + 扫描 + 判定已完成**（`QuickActionService` 改 `script.json` 适配器、`CollectableEvaluator`、`ScriptScanner` 三态、库页「可采集」列 + 「仅可采集」筛选 + 页内扫描候选面板，commit `4630b4b`，用户 build 通过）；**S4 集合页已完成**（`script-collections` 菜单项 + 主从两栏 + 勾选即增删 + 发送到信息面板 + 详情卡；偏差=详情卡用标准右侧列而非"成员表下方"）；**S5 信息面板分节已完成**（`CollectorService` 采集执行 + `InfoRowFactory` 只读呈现内核 + `BuiltinCollectorSeeder`；工具条 `集合 ▾`/`视图 ▾` + 每采集器一节 + 列 schema 自动生成 + 关分页；偏差=补了内置 `info` 集合「系统信息」、节头未吸顶、regex 按 line、删导出/导入包）；**S6 频率三层 + 保留策略已完成**（视图 > 集合 > 条目，只读才许 auto，2~3600s，只刷未折叠节）；**S7 未实施（待拍板）**（软件包页列/筛选/批量勾选全部保持现状，未换只读内核）；**S8 清理 + 规范 §4.1 回写已完成**（删 manifest 5 文件、旧命名清理、`config/` 清单重写）；**S7' 软件包详情卡动作收敛已完成**（只读字段全可复制 + 安装/升级按 `IsInstalled` 二选一 + Pin 三态 + 卸载仅已安装 + 「📋 复制信息」；另修 `edg` 截断 = 解析器续行合并 + 已安装前缀修复；包管理器切换评估见卡 §十，未实施）；**S7'' 动词层已完成**（工具条按通用动词重排 + 文案去后端名 + 补 3 个空缺动词「刷新索引 / 全部升级 / 卸载选中」；抽象落点 = 命令模板常量 `{exe}`/`{id}` 占位符，⛔ 零框架层改动：新命令走模块内 `ScriptRunner`，确认复用 `QuickActionService.ConfirmHandler`；🔴 坑：`winget.exe` 是 App Execution Alias ⇒ 必须解析绝对路径）；⏳ 待用户本地 build 逐条验收 | backlog/script-manager/NT-068.md |

| NT-069 | **跨模块文件操作契约 `IFileOperationService`**（v3.66 配置整改第 ② 批次）：磁盘动作（改名/移动/复制/回收站删除）下沉 `Core` 一份实现，`Shared` 定接口；`VideoFileService` 删掉自带 `SHFileOperationW`；确认框与库表记账留在各模块 | P1 | ⏸ **暂缓**（2026-10-18；批次 2 已落地 = 接口 + Core 实现 + DI + 视频工具改造 + 3 个 VM 别名修 CS0104，`check.ps1` 0 告警，⏳ 待本地 build；**后续批次（媒体资产委托 / `IVideoHandoff` / defaults 示例 / 文档回写）按用户「以后讨论后再实现」暂停**） | backlog/framework/NT-069.md |
| NT-070 | **启动提速（模块初始化两段式）**：`IModule` 增 `InitializeDeferred`（默认空实现）—— `Initialize` 只留菜单注册（首帧前），建库/清表/读配置/种子播种/扫描器接线/到期清理入队全部搬进 `InitializeDeferred`，由 Shell 在 `Window.Show()` 之后用 `DispatcherPriority.Background` 驱动；带启动打点写 `logs\startup-*.log` | P1 | 🔄 **已实施**（2026-10-18；改 `Shared/IModule` + `Core/ModuleLoader`（新增 `InitializeAllDeferred`，热加载同步补跑）+ `Shell/App`（打点 + Show 后 Post）+ ScriptManager / VideoTools / MediaAssets 三模块搬重活；WebTools 只有菜单注册，未覆盖；**实测 2959ms → 787~826ms（-73%）**：模块 IO 仅 58~64ms，真正大头 = 2173ms 的「程序目录全量 DLL 扫描」⇒ `ScanModulesDirectory` 改只枚举 `Module.*.dll`（Debug 下 49 个 DLL 逐个 LoadFrom+GetTypes），复测降到 **9~11ms**；🔴 排查坑 = 同二进制「冷启动 2173 / 热启动 93」，单次日志不足为凭；剩余 775ms = 主窗口构造 442~472 + 窗口显示 ~345（Avalonia 首帧固有，建议到此为止） | backlog/framework/NT-070.md |

> `📝 草稿` 需继续展开细节，升 `✅ 就绪` 后才会被批量执行；`✔ 已完成（待验收）` 经用户验收后台账翻 ✔（任务卡默认保留；作废卡已删并标 `🗑`）。
> NT-048（分屏贴靠首次几何不收敛）**已作废**：现象为**系统级**（Firefox 同复现）⇒ 外部纠偏方向错误，⛔ 不复活；任务卡保留备查。

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
| 脚本 / 命令共用基类 + 运行 | `Core/Services/QuickActionService.cs`（NT-068 起为 `script.json` 适配器）+ `Shared/Models/QuickAction.cs` + `Module.PcTools/ViewModels/QuickActionListViewModel.cs` |
| 默认数据 | `src/Shell/defaults/sysinfo/*`（内置采集脚本，UTF-8 带 BOM + `.seeded` 只加不覆盖）；⛔ 无 `.default.json` 清单（v3.60 起） |
| 本地数据库 | `Core/Services/SqliteDatabase.cs` + `Module.VideoTools/Services/VideoMediaIndex.cs` |
| 会话 / 运行钩子 | `Core/Services/DialogHooks.cs` + `Module.VideoTools/Services/VideoFileService.cs` |
| 视频工具 | `Module.VideoTools/Views/{VideoList,VideoPreview,VideoBatch}View.axaml` + `Services/{VideoScanner,FfmpegService,VideoTaskStore}.cs` |
| 运行环境 | `Shared/Services/IAppConfigService.cs` + `Core/Services/AppConfigService.cs` |
| 脚本管理器数据层 | `Module.ScriptManager/Services/{ScriptStoreService,ScriptDatabase,ScriptScanner,CollectableEvaluator}.cs` |
| winget | `Core/Services/PackageManagerService.cs` + `PackageOutputParser.cs` + `PackageSettingsService.cs` |
| 加密 DNS | `src/Core/Services/EncryptedDnsService.cs` |
| 模块菜单注册（Key 权威源） | 各模块 `XxxModule.cs` |

---
