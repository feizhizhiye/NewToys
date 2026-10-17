# NewToys 项目状态与路线图

> v3.75 · 2026-10-17 · 台账
> **标准角色（术语见《AI协作手册》【术语对照】）**：本文件 = **项目状态与路线图**（Status & Roadmap），三、节 = **Backlog 工作项台账**。
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：需求规格（宪法）/ 工程规范 / AI 协作手册 / AGENTS.md（跨 AI 入口）/ skill/（跨 AI 知识库）

**最近三次变更**
- **v3.75（🗂 整理期 2026-10-17）**：**文档搬入 skill + 任务卡归档策略修订 + 表述收敛**（用户「把doc文件移到skill里，封装，删除原有doc目录」）—— ① 🔴 **`src/docs/` → `skill/docs/`** 整体搬迁（git mv，4 份文档 + `backlog/`），`src/` 从此只放代码；`AGENTS.md` / `README.md` / `skill/SKILL.md` / `skill/references/*` / `check-techstack.ps1` / `.workbuddy/skills/newtoys/SKILL.md` 的路径引用同步改写，铁律 1 例外扩到 `skill/`（`accept.ps1` 改动范围判定原本已放行 `skill/`，零改动）。② 🔴 **归档策略修订**（原 2026-10-14「全量保留 ⛔不删」定调作废）：任务卡**默认保留**；**内容被后续工作项整体取代 / 明确作废的卡删除**，台账行标 `🗑 已作废（卡已删）`——本批已删 **NT-004 / NT-017 / NT-028 / NT-031 / NT-043 / NT-048** 共 6 张（NT-067 保留：`ScriptRunner` / `SysInfoGrid` 仍在使用）。③ **skill 与代码对齐**：`newtoys-spec.md` v1.5（补 `NewToys.UI` 程序集 / 数据落点改 `profiles\<配置>` + `app.json` 应用根 / 底栏 28px 口径 / 5 条踩坑硬约束 / G 规则号订正）；`tech-guard.md` 删「§七 与本文件重叠（待收敛）」过期告警 + 标注 `G-12`/`G-13` 未启用 + §三 补 `InitializeComponent` 覆盖；`check-techstack.ps1` 注释规则号订正。④ **本台账自身收敛**：按本文件自定规则「⛔ 只保留最近三次变更」**截断 v3.72 及更早**（归档为一行，明细看 `git log`）；工作项索引表按 NT 号升序重排（原 NT-025/026 倒置、NT-058~068 压在 NT-056 后）。⑤ **脚本评估结论**：`check.ps1` 新增 **`.axaml.cs` 是否调用 `InitializeComponent()`** 检查（NT-063 两次「整块 XAML 静默不加载」事故的静态兜底，`App.axaml` 豁免），实跑 0 告警；其余 3 个检查与 `accept.ps1` 编排无缺口，⛔ 未改构建脚本。文档：需求 v3.72 / 手册 v3.23 / 本台账 v3.75。
- **v3.74（📝 规划期 2026-10-17）**：**NT-068「脚本管理器（改名 + 数据库化 + 集合 + 信息面板分节）」立项并升 ✅ 就绪**（用户六轮讨论后拍板「放进一个 NT 里，一次执行」）—— ① 🔴 **模块整体改名**：`Module.PcTools` → `Module.ScriptManager`（程序集 / 命名空间 / 模块键 `pc`→`script` / 菜单 Key `pc-*`→`script-*` / 中文页名：脚本收集→**脚本库**、命令收集→**命令库**、信息展示→**信息面板**、软件包管理→**软件包**），授权例外；模块外需同步 `NewToys.slnx` / `Shell.csproj` / `App.axaml.cs` / `FavoritesService.cs` 4 处 + 注释 4 处；✅ `scripts/build*.ps1` 走 `Module.*` 通配，不受影响。② **四层模型**：采集器 → **集合** → 视图 → 页面（术语沿用「集合」，代码 `PcCollection` / `pc_collections` 隔离）。③ **数据分两处**：`config\script\script.json`（手工数据、进备份）+ `data\script\script.db`（观测 / 运行日志、不进备份）；⛔ 不改 `ConfigStoreService` / `SqliteDatabase`（框架层冻结），模块内起子目录 + 自建连接。④ **集合页独立菜单项** `script-collections`：主从两栏 + 搜索 / 置顶 / 最近使用 / 标签筛选（几十个集合靠这套扛，⛔ 不做层级）+ 成员勾选即时生效 + `📤 发送到信息面板`。⑤ **信息面板 = 集合选择框 + 按采集器分节**：顶部只有一个 `集合 ▾`（工具条 Combo；★置顶 → 最近使用 → 名称 排序）、一次显示一个集合、切集合整屏换、记住 `lastCollectionId`；🔴 **⛔ 不要横向排列的按钮组**（用户否决 Chip 条：「除了标签页栏和工具条……做成选择框吧」）+ 节折叠 + 节头状态着色 / 时间 / 行数 / 局部刷新 + 分节模式**关分页**（底栏改摘要）。⑥ **`collectable` 可采集判定**（自动规则 + 用户覆盖）+ **手动扫描三态** + 「仅可采集」筛选。⑦ **执行频率三层**（采集器 / 集合 / 视图）+ **保留策略**（`latest` / `count` / `days`，只读项强制 `latest`）。⑧ 命令即采集器（`capture` + 黑名单 + 强制超时 + 解析器）。⑨ ⛔ **不做旧数据迁移**（用户「无所谓老用户」）；NT-067 的 `sysinfo.manifest.json` 模型被取代（`ScriptRunner` / `InterpreterResolver` / `SysInfoGrid` 保留）。🔴 卡内 S1~S8 分步实施，**S1改名必须单独 commit + 用户 build 通过才进 S2**。⛔ 未动 `src/`。明细见 `backlog/NT-068.md`。
- **v3.73（🚧 实施期 2026-10-17）**：**NT-067「信息展示页（脚本输出呈现器）」实施完成**（用户「执行 NT-067」；D1/D2/D3/D5 按卡内假设落定）—— ① **执行底座**（全在 `Module.PcTools`，⛔ 不触 Shared/Core/Shell 代码）：`Services/InterpreterResolver`（解释器 → exe+args：配置路径 / 探安装位 / PATH 回退，Auto 按扩展名）+ `Services/ScriptRunner`（`UseShellExecute=false` + 重定向 + **stdout 严格 UTF-8 解码**（非法字节 ⇒ 判该脚本失败）+ `PYTHONIOENCODING`/`PYTHONUTF8`/`NEWTOYS_SYSINFO_DIR` + 超时/取消/退出码）+ `Services/SysInfoOutputParser`（三种外壳宽松解析）+ `Services/ISysInfoManifestService`（加载/校验/列宽回写）+ `Services/SysInfoSeeder`（**只加不覆盖** + `.seeded`）；② **manifest 驱动**：`columns[].key` = 1-based 稳定列位、`scripts[].interval/auto/timeout`，坏 manifest ⇒ 空态 + 状态栏 ⚠ 不崩；③ **动态列**：新增模块内 `Controls/SysInfoGrid`（`Grid` 子类，规格串 → `ColumnDefinitions`，表头 = 排序按钮 / 行 = 单元格，文本走绑定 ⇒ 就地刷新不重建）；⚠️ **偏差**：卡里写的是「ItemsControl 横向 StackPanel」，但 `2*` 星号列宽在 StackPanel 里表达不了 ⇒ 改用 Grid 子类，**列宽拖拽仍不接**（改宽走「⚙ 列设置」写回 manifest）；④ **定时刷新**：单 `DispatcherTimer`(1s) 只跑 `auto:true` 且到期的脚本，并发上限 4、同 `ScriptId` 跳过；**页面挂/脱可视树即起/停**（⚠️ 偏差：未单独判窗口最小化）；按 `RowKey` **就地改值** + `Items` 增量合并 ⇒ 选中项 / 详情卡不丢、不闪；⑤ **内置采集器保留**（`builtin` = 「应用路径」组，D3）；os/cpu/mem/disk/net/runtime 6 个默认脚本随包落 `Shell/defaults/sysinfo/`（🔴 **UTF-8 带 BOM** —— PS 5.1 读无 BOM 的 UTF-8 按 ANSI 解码 ⇒ 中文乱码）；⑥ 删 `Models/PcInfoEntry.cs`、VM 不再注入 `StartupService`（开机启动项改由 `os.ps1` 读注册表）；菜单标题「系统信息」→「**信息展示**」（`pc-info` Key 不变）；⑦ 顺手修 lint：`check-axaml-props.ps1` 的「本仓自定义属性」正则不吃**嵌套泛型**（`StyledProperty<IReadOnlyList<T>?>` 漏收 ⇒ 误报属性名不存在）。AI 侧 4 项静态检查 0 ERROR / 0 WARN；⏳ 待用户本地 `build.ps1` / `accept.ps1` 验收。明细见 `backlog/NT-067.md`。
- **v3.72 ~ v3.54（归档）**：NT-063 控件库（P0-a/P1/P2-a）/ NT-064 分页 / NT-067 信息展示页 / NT-062 设置归位与树状设置页 / NT-055~059 工具条·详情卡 IconBar·脚本索引库 / 标签栏改版 / 首页标签退出标签栏 —— 逐条明细见 `git log`（本台账只留最近三次变更）。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.72 ｜ 规范 v3.47 ｜ 协作手册 v3.23 ｜ 本台账 v3.75 |
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
- **7 项目**：`Shared/Core/Shell` + 4 模块；分层单向。
- 中央包版本管理 + 版本号编译自动生成。
- DI 容器 + `IModule` 插件机制 + 菜单 / 导航 / 主题服务。
- **模块加载双轨**：Debug 显式类型清单 / Release 目录扫描。
- **运行期热加载**（Modules + 刷新按钮）：✅ 已实装（NT-005，v3.37 验收）—— `Core/Services/ModuleLoader.cs` 走 collectible `AssemblyLoadContext`。

### 2.2 UI
- 三栏 `60,140,*` → **标题栏 40px（含标签页）+ 左树 180 / 折叠 48 + 主内容区 + 右详情 340 + 底部状态栏 26px**。
- 明暗主题全局切换，键一律 `*Brush`（**31 个**：v3.38 由 32 增至 34，新增 `WindowBorderBrush` / `WindowBorderInactiveBrush`；NT-049 再删 3 个零引用键 → 31），默认深色。
- **视觉规范**：共享样式已按 **v3.12 密度基线**收紧（表格行高 28、卡片内边距 12、工具条 12,6、筛选条 12,4）；**框架层图标已全矢量**（`Path` + `App.axaml` 的 `Icon*` 键：一级菜单 / 标题栏 / 标签页 / 左树，NT-035 / NT-045 / NT-046）；⚠️ **数据驱动图标**（二级菜单 / 首页一键按钮 / 模块页内数据行）仍是字符串 → 技术债 NT-047。

### 2.3 页面全景

| 模块 | 页面数 | 真实逻辑 | 骨架 |
|---|---|---|---|
| 系统设置 | 7 | **7** | 0 |
| PC 工具箱 | 5 | **5** | 0 |
| Web 工具箱 | 5 | **5** | 0 |
| 视频工具箱 | 3 | **3** | 0 |
| 媒体资产 | 12 | **12** | **0** |
| **合计** | **32** | **32** | **0** |

> **骨架页**：无（web-http 已由 NT-001 落成真实页；媒体资产 11 页已由 NT-017~022 逐页落地 + NT-016 `assets-all` 真实列表页，零骨架）。
> v3.66：**「设置」一级菜单项删除** ⇒ 系统设置 7 页里 6 页（六个节）合并为一个**应用级设置页**、`settings-all` 仍是独立页；四个模块设置页挂回各自模块（页面数不变）。
> v3.57：系统设置 **8 → 7**（NT-056：「路径配置」+「配置」合并为一页**「路径与配置」**，菜单项 `settings-profile` 移除，其余 Order 顺延 6 / 7）。
> v3.43：系统设置 10 → 7（NT-050 软件源迁 PcTools；NT-051 主题 / 关于并入通用）；媒体资产设置页 `settings-media-assets` 按菜单归「设置」分组、按代码归 `Module.MediaAssets`，§2.3 沿旧口径未单列（12 = `assets-*` 页）。

### 2.4 菜单清单（34 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是**默认页**，v3.67 起 ⛔ 不进标签栏，入口 = 标签栏 `＋`） |
| 电脑工具 | pc | 20 | `IconPc` | `pc-favorites`收藏夹/0/✅ ｜ `pc-shortcuts`设置与脚本/1/✅ ｜ `pc-quicksettings`快捷设置/2/✅ ｜ `pc-package`软件包/3/✅ ｜ **`settings-pc-tools`电脑工具设置/100/✅** ｜ `pc-info`**信息展示**/99/✅ |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP测试/1/✅ ｜ `web-bookmark`书签/2/✅ ｜ `web-ping`网络Ping/3/✅ ｜ `web-scrape`网页抓取/4/✅ ｜ `web-hosts`Hosts管理/5/✅ ｜ **`settings-web-tools`网络工具设置/100/✅** |
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**视频预览**/1/✅ ｜ `video-batch`**批量处理**/2/✅ ｜ **`settings-video-tools`视频工具设置/100/✅** |
| 媒体资产 | assets | 50 | `IconMediaAssets` | `assets-all`全部资产/0/✅ ｜ `assets-video`视频/1/✅ ｜ `assets-music`音乐/2/✅ ｜ `assets-book`书籍/3/✅ ｜ `assets-image`图片/4/✅ ｜ `assets-document`文档/5/✅ ｜ `assets-tags`标签管理/6/✅ ｜ `assets-categories`分类管理/7/✅ ｜ `assets-collections`集合管理/8/✅ ｜ `assets-storages`存储源/9/✅ ｜ `assets-tasks`任务中心/10/✅ ｜ `assets-trash`回收站/11/✅ ｜ **`settings-media-assets`媒体资产设置/99/✅** |
| ~~设置~~ | — | — | — | 🔴 **v3.66：该一级菜单项已删除**。应用级设置 = **一个整页**（入口 = 左树版本行齿轮 / `Ctrl+,` / 标题栏搜索按钮），**模块设置页已挂回各自模块**（见表内加粗项）。 |
| **宿主分组（不渲染）** | system | 999 | `IconSettings` | `IsHome=true` ⇒ 左树 / 首页模块计数 / 功能清单页都跳过；只承载寻址：节锚点 `settings-general`通用/0 ｜ `settings-path`**运行环境**（配置档 + 外部程序路径）/2 ｜ `settings-launch`一键按钮/3 ｜ `settings-dns`加密DNS/4 ｜ `settings-data`数据管理/6 ＋ `settings-all`功能清单/7（v3.66 起**首页「显示更多」就地展开**它；独立页保留给标题栏 `＋` 与「关于」节按钮）。🔴 **所有 Key ⛔ 一律未改** |

---

## 三、待办与优先级

> 本轮范围外的项（详情卡上提 / 搜索框 / 历史栈 / 图标矢量化）见**需求 §4.11**，此处不重复登记。
> **工作项来源（规划期产出）**：每次新需求立项，由 AI 对比《需求规格》应然 vs 本节「一、基线」生成；逐条可验收、带分级（P0/P1…）。
> 流程定义见 `skill/docs/NewToys-AI协作手册.md`【研发流程】（**规划 / 实施双模式 + 就绪闸门（DoR）+ 批量协议**）；本表 = **Backlog 工作项台账**（紧凑索引），**任务卡**统一在 `skill/docs/backlog/NT-xxx.md`。
> 生命周期：**📝 Draft（草稿）** → **✅ Ready（就绪，过 DoR）** → **🔄 In Progress（执行中）** → **✔ Done（已完成待验收）** → 用户验收后 **Accepted（已验收）**；验收后台账工作项翻 ✔、任务卡**默认保留**（被后续工作项整体取代 / 明确作废的卡删除，台账行标 `🗑 已作废`）。台账只翻状态，不随任务卡删。
> 术语（立项 / 工作项 / 任务卡 / 就绪闸门 / 完成定义 …）见《AI协作手册》【术语对照】。

### 工作项索引（Backlog）
> 任务卡（验收标准 / 技术要点 / 依赖 / 范围）见 `skill/docs/backlog/NT-xxx.md`；**实施期只跑其中 `✅ 就绪` 项**（`📝 草稿` 不执行）。
> 睡前开会话发「**执行全部就绪待办**」即可让 agent 批量跑（⛔ 不用 automation 定时触发）。

| NT | 标题 | 优先级 | 状态 | 详情 |
|---|---|---|---|---|
| NT-001 | web-http 实装（唯一剩余骨架页） | P2 | ✔ 已完成（已验收） | backlog/NT-001.md |
| NT-002 | pc-package 收尾（Parser 实机 + 批量队列） | P2 | ✔ 已完成（已验收） | backlog/NT-002.md |
| NT-003 | settings-package 软件源（退出码验证） | P2 | ✔ 已完成（已验收） | backlog/NT-003.md |
| NT-004 | defaults\ 与脚本目录播种确认 | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-005 | 模块热加载实装（规范 §3.7） | P2 | ✔ 已完成（已验收） | backlog/NT-005.md |
| NT-006 | 快捷设置：运行改造（可见 pwsh+回显 / GUI 隐藏 / 切换 shell）+ 导入导出 / 清除缓存 | P1 | ✔ 已完成（已验收） | backlog/NT-006.md |
| NT-007 | 设置与脚本：导入导出 + 右键编辑 / 打开方式 + 运行如双击 | P1 | ✔ 已完成（已验收） | backlog/NT-007.md |
| NT-008 | 软件包：可升级对齐 winget update + Pin 显示 + 已装移状态栏 + 详情 bug + 已装导入导出 | P1 | ✔ 已完成（已验收） | backlog/NT-008.md |
| NT-009 | 首页：收藏配色 / 一键操作固定底部 / 删工具箱行 / 状态栏模块功能数 | P2 | ✔ 已完成（已验收） | backlog/NT-009.md |
| NT-010 | 通用样式统一：激活强调色加深 / 按钮配色 / 导入导出控件一致 / 输入框水印 / 弹层样式 | P1 | ✔ 已完成（已验收） | backlog/NT-010.md |
| NT-011 | 视频列表：删扫描按钮 / 数千视频优化 / 入库去重 / 删清空媒体缓存 | P1 | ✔ 已完成（已验收） | backlog/NT-011.md |
| NT-012 | 视频预览：旋转播放按钮 + 命令卡旋转 + 按钮标题去后缀 | P2 | ✔ 已完成（已验收） | backlog/NT-012.md |
| NT-013 | ffmpeg 卡：剪辑转码输入对齐 + 转码默认 H.265/CRF23/_H265/默认宽高 | P2 | ✔ 已完成（已验收） | backlog/NT-013.md |
| NT-014 | 媒体资产模块骨架（Module.MediaAssets 项目 + 三处登记 + 12 菜单 + 12 空页面；一级图标暂 emoji 占位） | P1 | ✔ 已完成（已验收） | backlog/NT-014.md |
| NT-015 | 媒体资产数据层（13 表 + 索引）+ AssetService + ScanService + TaskQueue | P1 | ✔ 已完成（已验收） | backlog/NT-015.md |
| NT-016 | AssetAllView 落地（列表 + 详情 + 工具条 + 筛选 + 排序 + 列显隐） | P1 | ✔ 已完成（已验收） | backlog/NT-016.md |
| NT-017 | 5 个类型页（视频 / 音乐 / 书籍 / 图片 / 文档）= **薄壳 + 预设类型**，共用 `assets-all` 实现（v3.34；⛔ 网格视图不做、类型专属列延后） | P1 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-018 | 标签 / 分类 / 集合三页 + 逻辑操作 + `operation_logs`（⚠️ 含 2 处偏差：集合排序用 ⬆⬇ 按钮、智能集合规则未实装） | P1 | ✔ 已完成（已验收） | backlog/NT-018.md |
| NT-019 | 物理操作（`FileOperationService` 日志先行 + 防穿越 + 冲突策略 + 回收站）+ 任务中心 + 回收站页（⚠️ 含 3 处偏差：移动/复制无 UI 入口、删除时间未显示、重试仅扫描任务） | P1 | ✔ 已完成（已验收） | backlog/NT-019.md |
| NT-020 | 存储源页 + 移动硬盘离线 / 上线 + 外部变更校准 | P2 | ✔ 已完成（已验收） | backlog/NT-020.md |
| NT-021 | 媒体资产设置页（`settings-media-assets`，View/VM 归本模块、自注册进 settings 组）+ 元数据 + 缩略图 | P2 | ✔ 已完成（已验收） | backlog/NT-021.md |
| NT-022 | 列表 JSON 导入 / 导出 + 用户数据导出（R1 缓解） | P2 | ✔ 已完成（已验收） | backlog/NT-022.md |
| NT-023 | 框架：MenuService 跨组延迟补登 + Settings 必备化 + 启动健全性检查 / 无模块降级 | P1 | ✔ 已完成（已验收） | backlog/NT-023.md |
| NT-024 | 多实例：`--profile` 启动参数 + 每配置单例 + 切换新语义（不写全局默认）+「设为默认」/「🚀 新开」+ 标题带配置名 | P1 | ✔ 已完成（已验收） | backlog/NT-024.md |
| NT-025 | 窗口：自绘缩放抓取区（四边 5px/四角 10px，BeginResizeDrag 八向）+ 分屏贴靠验证 + bounds 按配置记忆 | P1 | ✔ 已完成（已验收） | backlog/NT-025.md |
| NT-026 | 媒体资产：多库融合（工作副本模型 = 打开库 A → 再打开库 B → 💾 保存；「减」= 勾选后删除；合并走 `ATTACH DATABASE`） | P1 | ✔ 已完成（已验收） | backlog/NT-026.md |
| NT-027 | 视频工具精简：删视频库(.db) / 标签 / 收藏页 → 收窄为「简易查看器 + 送批量任务」（含旧表一次性清理） | P0 | ✔ 已完成（已验收） | backlog/NT-027.md |
| NT-028 | 示例数据 + 详情卡默认示例态（批次 1~4 已落：视频两页 / `assets-all` / PcTools 4 页 / WebTools 4 页 / `settings-path`；批次 5 类型页随 NT-017；含框架层 `ListPageViewModelBase` 授权改动） | P1 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-029 | 回收站显示删除时间（NT-019 偏差②闭合） | P1 | ✔ 已完成（已验收） | backlog/NT-029.md |
| NT-030 | 移动/复制 UI 入口（NT-019 偏差①） | P1 | ✔ 已完成（已验收） | backlog/NT-030.md |
| NT-031 | 视频类型专属列（NT-017 试点） | P2 | ✔ 已完成（已验收） | 🗑 已作废（卡已删） |
| NT-032 | 修复扫描"大小+修改时间"移动识别键碰撞（误配复制/改名） | P1 | ✔ 已完成（已验收） | backlog/NT-032.md |
| NT-033 | 修复多库融合重复/重叠并入产生孤儿资产 | P1 | ✔ 已完成（已验收） | backlog/NT-033.md |
| NT-034 | 窗口圆角修复（最大化/分屏方角） | P1 | ✔ 已完成（已验收） | backlog/NT-034.md |
| NT-035 | 左树一级图标矢量化（媒体资产去 emoji，统一矢量方案） | P2 | ✔ 已完成（已验收） | backlog/NT-035.md |
| NT-036 | 修复标签关闭按钮与标签文字重叠（✕ 落入标签间隙，不挪左侧/不用带圈） | P2 | ✔ 已完成（已验收） | backlog/NT-036.md |
| NT-037 | 左树一级展开整组 BringIntoView（防裁切） | P2 | ✔ 已完成（已验收） | backlog/NT-037.md |
| NT-038 | 三配置档（debug/default/release）：运行时永久驻留（debug=复制 default 播种；商店版转远期） | P1 | ✔ 已完成（已验收） | backlog/NT-038.md |
| NT-039 | 窗口外框（自绘 1px）+ 激活 / 失焦两态（主题键 32→34；最大化收边框） | P0 | ✔ 已完成（已验收） | backlog/NT-039.md |
| NT-040 | 窗口装饰回归 Avalonia 12 官方 CSD 路线（`WindowDecorations="Full"` + 隐式键同键覆盖默认主题） | P0 | ✔ 已完成（已验收）· 🔴 2026-10-15 **重新采纳**（跨平台评估定案：Wayland / Debian 13 只有官方 CSD 接得上） | backlog/NT-040.md |
| NT-041 | 清理 Win32 窗口补丁（贴靠样式位 / `WM_NCCALCSIZE` / resize 兜底），逐处删 + 逐处回归 | P1 | ✔ 已完成（已验收）· 2026-10-15 恢复生效（`ApplySnapEligibleStyles` 保持删除） | backlog/NT-041.md |
| NT-042 | DWM 阴影与方角方案复核（`SetWindowRgn` 去留；阴影优先） | P1 | ✔ 已完成（**已验收** 2026-10-15 · 裁决：**保留** region，阴影代价已证伪） | backlog/NT-042.md |
| NT-043 | 标题栏是否改用 `TitleBarBrush`（✅ 拍板**不启用**，清理并入 NT-049） | P2 | ✔ 已完成（已验收 · 并入 NT-049） | 🗑 已作废（卡已删） |
| NT-044 | 标题栏加全屏按钮（⛶）+ `F11` / `Esc` 快捷键 | P2 | ✔ 已完成（已验收） | backlog/NT-044.md |
| NT-045 | 图标实现方式统一（C 方案：全框架矢量 `Geometry` 库；窗口按钮弃图标字体 / 标签 ✕ 与树展开符弃字符；折叠菜单→panel-left、模块加载→拼图） | P2 | ✔ 已完成（已验收） | backlog/NT-045.md |
| NT-046 | 图标收尾：最小化退化与居中修复 / 拼图与全屏改型（两箭头）/ 左树去展开符 / 「框架层图标只用 `Path`」写进规范 | P2 | ✔ 已完成（已验收） | backlog/NT-046.md |
| NT-047 | 数据驱动图标矢量化（二级功能菜单 34 项 / 首页一键按钮 / 模块页内数据行；⛔ 静态装饰 emoji 不动） | P2 | 📝 草稿 | backlog/NT-047.md |
| NT-049 | 清理 3 个未引用标题栏主题键（`TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush`；主题键 34 → 31） | P2 | ✔ 已完成（已验收） | backlog/NT-049.md |
| NT-050 | 设置归位：模块专属设置迁模块（软件源 View/VM → PcTools，媒体资产先例模式）+ settings 内「媒体资产」改名「媒体资产设置」 | P1 | ✔ 已完成（已验收） | backlog/NT-050.md |
| NT-051 | 设置页合并：主题外观 + 关于 并入 通用设置（单页三节；键 `settings-general` 不变，删 `settings-theme`/`settings-about` 两项） | P2 | ✔ 已完成（已验收） | backlog/NT-051.md |
| NT-052 | 「🔄 重载模块」移入设置（标题栏 3→2 按钮；入口 = **功能清单页**「增加功能」操作区；方案 A：Shared/Core 新增 `IModuleReloadService` 契约，2026-10-15 用户拍板） | P2 | ✔ 已完成（已验收） | backlog/NT-052.md |
| NT-053 | 设置页窄界面适配：最窄 960×640（内容宽 720）与默认 1280×800 两种可用（根 ScrollViewer / 星号列 / 去超宽固定容器） | P2 | ✔ 已完成（已验收） | backlog/NT-053.md |
| NT-054 | 详情卡声明式规范：**三区模型**（`Fields` 字段区 / `Actions` 动作区 / `Custom` 逃生舱）+ 7 种字段 Kind + 3 种提交语义（`Instant`/`OnBlur`/`Explicit`）+ Shell 统一渲染，替代 16 张卡逐页手写 XAML（含债 B13 详情面板上提） | P1 | ✔ **已完成（已验收 2026-10-15）**（2026-10-15：📝 草稿 → ✅ 就绪 → **L0 + L1 交付并编译验收通过** → **L2 全数迁完 16/16**（打样页 `video-batch` 已验收 + 页内自绘 12 页 + MediaAssets 4 张独立视图；**L1 按用户拍板做了加法扩展**：chips 色块 / chips 命令参数取值 / List 项级命令 / chip 悬停提示；**L3 已删 8 个停用视图**，`Custom` 渲染分支仍未接）；**用户本地编译 + 实机回归通过（2026-10-15）；技术债 B13 结清**；遗留 = `Custom` 逃生舱渲染分支仍未接（现无页面需要）） | backlog/NT-054.md |
| NT-055 | 工具条统一：Shell 声明式承载（`IToolbarSource` + `ToolbarHost`）+ 详情开关浮动（新契约 + Shell 新控件，**框架层首审项**；**已迁 18 个 View / 覆盖 23 页**（`settings-path` 另见 NT-056；三轮补迁 MediaAssets 6 页）；仅 `web-http` 走逃生舱） | P1 | ✔ 已完成（已验收）· commit `8da4462` | backlog/NT-055.md |
| NT-056 | 设置总表：路径配置 + 配置 合并为一表（分类列区分行类型 / 决定工具可用性；菜单 35 → 34） | P1 | ✔ **已完成（已验收 2026-10-16）**· commit `66faa28` | backlog/NT-056.md |
| NT-057 | 系统托盘增强：左键切显示 / 隐藏 + 右键五项菜单（首页 · 设置 · 用另一个配置打开… · 退出）+ **托盘图标改用应用图标**（与 exe / 窗口同源，⛔ 不再代码画、⛔ 不写死强调色） | P2 | ✔ **已完成（已验收 2026-10-16）**· 2026-10-15：`CreateTrayIcon` → 唯一图标工厂 `CreateAppIcon(accent)`（窗口 + 托盘同源，随强调色）+ 新增 `Resources/app.ico`（exe 图标）；托盘五项菜单（②③ 先拉起窗口再导航，④ 子菜单排除当前配置 / 无其他配置时置灰）；技术债：**形状两处定义**（C# 常量与 ICO 口径，改一处须同步另一处） | backlog/NT-057.md |
| NT-058 | 详情卡图标动作组：**第 9 种 `FieldKind.IconBar`**（32×32 图标按钮组）+ chips 分组（`ChipGroupPath`）+ 「全部资产」页打样定稿（**框架层首审项**） | P1 | ✔ **已完成（已验收 2026-10-16）** | backlog/NT-058.md |
| NT-059 | 「设置与脚本」页改**脚本索引库**：导入收成 `SplitButton`「📂 导入」+ 导入后清筛选 + 详情卡 `IconBar` + 新增「分类」列 + 删除语义分叉 | P1 | ✔ **已完成（已验收 2026-10-16）** | backlog/NT-059.md |
| NT-060 | **干净启动**：删示例数据（列表示例行 + 详情卡示例态）改**空态引导**（说明 + 入口按钮）+ 取消 `defaults\` 默认清单播种（⚠️ 实含**框架层减法**：`DetailCardDescriptor.SamplePath`/`SampleText` + `DetailCardRender.IsSample`） | P1 | ✔ 已完成（⏳ 待本地 build 验收） | backlog/NT-060.md |
| NT-061 | **配置下沉**：Core 新增 **`IOptionsStore`**（KV + 按模块分区，先 json 后可换 SQLite）+ **五模块各一个设置页**注册进 `settings` + 功能入口页**数据源框可编辑**（回车 / 失焦提交，路径无效 ⇒ 状态栏红字 + ⛔ 不写盘） | P1 | 🚧 已实施（⏳ 待本地 build 验收） | backlog/NT-061.md |
| NT-062 | **应用级设置归位**：`Module.Settings` **撤销** → 6 页迁入 Shell（`settings` 分组改由 Shell 创建）+ **「运行环境」**（外部程序路径上提应用根 `app.json`，与配置档同节）+ 设置项**显式分类** `SettingsCategory` + 设置页**搜索** / 每行**打开配置目录** / 外部程序缺失**惰性探测** + **功能清单移首页** + **软件源并入电脑工具设置** + 删 `GpuAcceleration` + 左树**设置齿轮**（`Ctrl+,`） | P1 | ✅ **已实施**（2026-10-16；①-⑥ 全部落地 = 删 `Module.Settings` + 6 页迁 Shell + Shell 建 `settings` 组 + `app.json` 上提应用根 + 解释器路径回迁 + 删 `GpuAcceleration` + **一页多节/节路由** + **`SettingsCategory` 显式分类** + 设置页搜索 + 每行「打开配置目录/文件」 + **外部程序惰性探测 + 状态栏 `⚠` 可点** + 齿轮/`Ctrl+,` + **功能清单移首页** + **软件源并入电脑工具设置**；AI 侧四项静态检查 0 ERROR / 0 WARN；⏳ 待用户本地 `build.ps1` / `accept.ps1` 验收） | backlog/NT-062.md |
| NT-063 | **控件库 NewToys.UI + 列表页控件化**：ColumnGrid 五合一（2086 行）+ 详情卡渲染搬家 + 新底栏 ListFooterBar（P0-a）→ 列表页骨架（P1）→ 类型页合并 / 设置行控件（P2） | P1 | 🔵 **P0-a + P1 + P2-a 已实施**（2026-10-16；⏳ 待本地 build 验收。P2-a = 5 类型页整体删除 + 筛选栏类型切换驱动列 / 详情卡 / 状态栏；**P2-b 设置节 / 行控件未做** = 与 UI 库纪律冲突 + 重名 + 文档未载，待拍板；UI 库纪律 = 只放稳定控件 + 主题刷子留 Shell；⛔ 构建加速不做） | backlog/NT-063.md |
| NT-064 | **列表分页**：UI 底栏归 NT-063，本卡做**数据层** —— SQL LIMIT/OFFSET + **排序下推** + 独立 COUNT(*)；底栏**默认隐藏、列表超出页底才显示**；批量操作只作用于当前页选中项 | P1 | 🔄 **已实施**（2026-10-16；样板页「全部资产」：基类分页状态 + `AssetService.AssetQuery`/`CountAssets`/`QueryPage`（WHERE 只写一份）+ 底栏插槽接线 + 页大小落 `shell` 分区；其余 15 页未迁；AI 侧 4 项静态检查 0/0；⏳ 待本地 build 验收） | backlog/NT-064.md |
| NT-065 | **分页推广到「脚本收集 / 命令收集」两页**（`pc-shortcuts` / `pc-quicksettings`）：两页挂 `ListPageSkeleton` 的底栏 + VM 分页（数据源是 **JSON 清单 ⇒ 内存切片**，⛔ 不做 SQL）；同步菜单改名「设置与脚本→脚本收集」「快捷设置→命令收集」 | P1 | 📝 草稿（2026-10-17 建卡，待升就绪） | backlog/NT-065.md |
| NT-066 | **标题栏搜索扩范围**：可搜**二级菜单项** + **设置页的节点**（现只搜一级 / 已注册项）；跨 Shell（框架层）⇒ 需用户授权 | P1 | 📝 草稿（2026-10-17 建卡，待摸底 + 授权） | backlog/NT-066.md |
| NT-067 | **信息展示页（脚本输出呈现器，原系统信息 `pc-info`）**：`ScriptRunner` 底座（PcTools）+ manifest 驱动（columns/scripts + per-script interval + auto/timeout）+ 多脚本合并 + 按 RowKey 就地定时刷新 + 自定义表头 + 默认采集脚本播种 | P1 | ✅ **已实施**（2026-10-17 实施完成；D1/D2/D3/D5 按卡内假设落定；偏差 = 列渲染改用模块内 `SysInfoGrid`（Grid 子类，星号列宽必需）、定时刷新以「页面挂/脱可视树」为准（未单独判窗口最小化）、「⏱ 刷新间隔」= 会话内改 `auto` 脚本 interval（manifest 仍唯一持久化）；⏳ 待用户本地 build/accept） | backlog/NT-067.md |
| NT-068 | **脚本管理器（改名 + 数据库化 + 集合 + 信息面板分节）**：`Module.PcTools` → `Module.ScriptManager`（程序集 / 命名空间 / 模块键 `pc`→`script` / 菜单 Key `pc-*`→`script-*` / 中文页名）；四层模型「采集器 → 集合 → 视图 → 页面」取代四份扁平 JSON；模块独立数据目录（`config\script\` 手工数据 + `data\script\script.db` 观测缓存）；新增集合页 `script-collections`（主从两栏 + 置顶 / 最近 / 搜索 + 发送到信息面板）；信息面板改**集合选择框 + 按采集器分节**（顶部唯一 `集合 ▾`，一次一个集合，⛔ 无 Chip 条；节头状态着色 / 局部刷新 + 关分页）；`collectable` 可采集判定 + 手动扫描三态 + 「仅可采集」筛选；执行频率三层 + 保留策略（`latest` / `count` / `days`）；命令即采集器；⛔ 不做旧数据迁移 | P0 | 🔨 **实施中**（2026-10-17 立项并升就绪；用户拍板「放进一个 NT 里，一次执行」）；**S1 改名已完成**（commit `9eea91f`，用户 build 通过；程序集名保持 `Module.ScriptManager.dll`）；**S2 数据层已完成**（`ScriptStore` 模型 + `ScriptStoreService`（`config\script\script.json` 原子写）+ `ScriptDatabase`（`data\script\script.db` 两表 + 保留策略三态）+ 内置集合播种 + 启动即生成，commit `6bfe6e8`，用户 build 通过）；**S3 切库 + 扫描 + 判定已完成**（`QuickActionService` 改 `script.json` 适配器、`CollectableEvaluator`、`ScriptScanner` 三态、库页「可采集」列 + 「仅可采集」筛选 + 页内扫描候选面板，commit `4630b4b`，用户 build 通过）；**S4 集合页已完成**（`script-collections` 菜单项 + 主从两栏 + 勾选即增删 + 发送到信息面板 + 详情卡；偏差=详情卡用标准右侧列而非"成员表下方"；⏳ 待用户本地 build） | backlog/NT-068.md |

> `📝 草稿` 需继续展开细节，升 `✅ 就绪` 后才会被批量执行；`✔ 已完成（待验收）` 经用户验收后台账翻 ✔（任务卡默认保留；作废卡已删并标 `🗑`）。
> NT-048（分屏贴靠首次几何不收敛）**已作废**：现象为**系统级**（Firefox 同复现）⇒ 外部纠偏方向错误，⛔ 不复活；任务卡保留备查。

### P1 · 技术债（未结）
> 已结项不在此重复登记（编译绑定 = 规范 B1；旧 `NewToys.slnx` = B6；`BoolInversionConverter` = B8；v3.15 批量页勾选框 = v3.16 已落地），明细见《工程规范》§7 与「二、已完成」。

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
| 2026-10-15 | **v3.45** | **首页标签固定不滚动（v3.44 收窄 + bug 修复，✅ 已验收 2026-10-15）** —— 用户反馈「搞复杂了」⇒ **只固定 home 页，其余全参与滚动；⛔ 不做「固定其他标签页」功能**（固定需求走首页收藏夹）。**代码**：`＋` 按钮从固定右列**回归滚动区**；标签栏 = 「首页固定列 + 滚动区」两列。🔴 **修 v3.44 bug**：重构时丢了 `ItemsControl.ItemsPanel`（横排）⇒ 默认竖排 StackPanel + 竖滚禁用 = **只显示第一个标签**；已补回（规范 §2.1 已写「横排 ItemsPanel 必须显式写」）。**文档**：需求 §4.2 / 规范 §2.1 规则收窄为「首页是唯一固定元素」；需求 v3.42 / 规范 v3.26 / 台账 v3.45。 |
| 2026-10-15 | **v3.43** | **NT-050~053 设置重构四项批量执行（🟡 已落盘 · 待本地编译）** —— 用户「执行全部就绪待办」。**NT-050**：软件源页（View/VM + DI + 菜单自注册）由 Module.Settings 迁入 Module.PcTools（Key / Order / 图标 / 描述⛔不改）+ settings 内「媒体资产」→「媒体资产设置」；**NT-051**：主题外观 + 关于并入通用设置（VM 合并去重、删 6 文件、菜单 37→35；两处平移偏差：恢复默认按钮移入卡头、About 死按钮删）；**NT-052**：「🔄 重载模块」由标题栏移入功能清单页「增加功能」操作区（新增 `IModuleReloadService` + `ModuleReloadService`，标题栏 3→2 按钮，`ShellViewModel` 订阅 `Reloaded` 重建左树；⚠️ 事件签名细化为带计数元组）；**NT-053**：设置页窄界面适配审计（8 页全合规、0 修改）。🔴 框架层改动（Shared/Core 契约 + ShellViewModel）= 本批首审项。静态自检三条全过。文档：规范 v3.25 / NT-047 卡片去两行 / 台账计数同步。 |
| 2026-10-15 | **12.1.4** | **Avalonia 12.1.3 → 12.1.4 + 窗口钩子迁移官方 API + tech-guard 自检基建（🟡 补记 · 上一轮落盘漏提交）** —— `Directory.Packages.props` 升 12.1.4（AboutView 技术栈文案同步）；`ShellView.axaml.cs` 手搓子类化（`SetWindowLongPtr` + `CallWindowProc` + 静态委托字段）整体迁移到官方 `Win32Properties.AddWndProcHookCallback`（天然平台门控）；新增 `skill/references/tech-guard.md`（G-01~G-14 硬伤清单 + §五 编译错误速查）+ `skill/scripts/check-techstack.ps1`（第三条静态自检）；AGENTS / SKILL / spec / 协作手册 v3.21 / 规范表述同步。⚠️ 12.1.4 升级本身尚未经用户本地编译复核（随本批 `build.ps1` 一并验）。 |
