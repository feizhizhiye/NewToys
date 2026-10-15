# NewToys 项目状态与路线图

> v3.40 · 2026-10-15 · 台账
> **标准角色（术语见《AI协作手册》【术语对照】）**：本文件 = **项目状态与路线图**（Status & Roadmap），三、节 = **Backlog 工作项台账**。
> **只回答「做到哪了」**——基线 / 已完成 / 待办三节。
> ⛔ **只保留最近三次变更需求**（更早的逐次记录一律看 `git log`）。
> ⛔ **评估结论不写进本台账**——评估在会话里讨论，用户拍板后才转为需求。
> 配套：需求规格（宪法）/ 工程规范 / AI 协作手册 / AGENTS.md（跨 AI 入口）/ skill/（跨 AI 知识库）

**最近三次变更**
- **v3.40（文档清理 2026-10-15）**：🧹 **台账去重 + 与代码对齐**（⛔ 纯文档，未动 `src/` 代码）。① **去重**：删 §三 内已完成的 v3.12~v3.22 六个 P0 批次块（80 行，与「二、已完成」+「五、变更流水」+ `git log` 三处重复）；删「P2 · 功能待办」存根（自身即 NT-001~005 索引的重复）；删「P3 · 待验证」6 项（已由「编译通过 = 验收通过」覆盖）；NT-048 已作废 ⇒ 移出工作项表改列脚注；NT-043 清理已并入 NT-049 ⇒ 行内去「⛔ 待拍板」。② **对齐代码**：热加载 ⏳待实装 → ✅ 已实装（NT-005）；Web 工具箱骨架 1 → 0、页面合计 23/12 → 35/0（`WebHttpView` 已存在）；菜单清单 36 → 37 项（补 `settings-media-assets`）；媒体资产一级图标 `🗂 emoji` → `IconMediaAssets`；§2.2「标题栏 / 标签页图标仍为 emoji」→ 框架层已全矢量（NT-045/046）；基线行文档版本「提示词 v3.17」→「协作手册 v3.19」（改名 + 版本均已过时）。③ **任务卡**：NT-043 `📝 草稿` → ✔ 已验收；NT-045「待验收」→ 已验收；NT-047 二级菜单 34 → 36 处（`RegisterFunction` 实测计数）。④ **旧名扫尾**：`提示词` → `协作手册` —— `NewToys-工程规范.md`「更新节奏」表末列（→ v3.24）+ `NewToys-AI协作手册.md` [5] 交付 ④⑤（→ v3.20）。
- **v3.37（✅ 已验收 2026-10-14）**：🌙 **批量执行 8 个就绪待办**（用户睡前授权「逐项全部执行、无需确认、回来统一批量确认」）。**NT-004** 恢复被 v1.35 误删的 `defaults\` 播种文件（5 个）；**NT-003** 软件源页实机验证通过（`winget source list` 中文表头解析无误）+ 防御性 catch；**NT-002** pc-package 批量勾选队列（Pick 列 + 三态表头 ☑ + 「⬇ 安装选中 / ⬆ 升级选中」逐条串行、失败隔离）+ 修 `IdLike` 白名单误杀（实测合法 Id 带空格 / 花括号 / 中文）；**NT-020** 存储源页落地（在线探测 + 统计 + 扫描入队跳任务中心 + 移除仅移索引）；**NT-021** 模块设置页 `settings-media-assets`（自注册 settings 组 Order 100）+ `MetadataService`（ffprobe / 图片头尺寸）+ `ThumbnailService`（ffmpeg 抽帧缩放，有界并发 4）+ 回收站到期清理；**NT-022** 资产列表 JSON 导入导出 + 用户数据（标签/分类/集合/关联）导出恢复（R1 缓解）；**NT-026** 多库融合（ATTACH 只读合并 + VACUUM INTO 另存 + 勾选软删除，未决 1 按"主库即工作副本"裁决）；**NT-005** 模块运行期热加载（collectible ALC + 左树重建 + 标题栏入口，🔴 框架层改动 = 本批首审项）。需求 §6.6 三处过时表述同步修正（→ v3.35）。**编译验收轮修 5 处**（用户本地 `build.ps1` 逐轮暴露，AI 立即补丁）：CS0759 漏 `_conflictPolicy` 字段（`be10bf4`）/ CS8602 `LoaderExceptions` 元素可空（`d445575`）/ CS0103 ×2 + CS8618 ×4 + CS0168（`7004ac3`）/ AVLN2000 ComboBox `SelectedValuePath` → `SelectedItem`（`73bfa04`）/ AVLN2000 `Path.StrokeLineJoin` → `StrokeJoin`（`1dd6d9d`）。新增自查脚本 `skill/scripts/check-axaml-props.ps1`（XAML 属性名 vs Avalonia ref 程序集，专治 Avalonia≠WPF 名字差异，已接入 AGENTS.md / SKILL.md / 协作手册[4]）。
- **v3.36（✅ 已验收 2026-10-14）**：🗂 **NT-019 物理操作 + 任务中心 + 回收站**。新增 `FileOperationService` —— **全模块唯一动磁盘的地方**：`Rename` / `Move` / `Copy` / `DeleteToRecycle` / `PermanentDelete` + `ConflictPolicy{AppendNumber,ShortHash,Skip,Overwrite}` + `IsInside()` 防穿越；🔴 **日志先行**（`operation_logs(pending)` → 动磁盘 → 更新 `file_locations` → 校验 → success/failed，⛔ 失败不自动回滚）；🔴 防穿越（重命名只改文件名；移动/复制要求目标落在**已登记存储源**根内）；删除默认送**系统回收站**（`SHFileOperationW`，视频工具同款）。**`assets-tasks`（任务中心）** 三块 = 运行态队列（可取消 / 清理已结束）+ 最近扫描记录（右键**重扫=重试**）+ 操作日志。**`assets-trash`（回收站）** = `status='trashed'` 列表 + `TrashDetailView`（资产信息 + **受影响文件位置** + 恢复 / 永久删除）；「♻ 恢复」= 只改库状态，「🗑 永久删除 / 🧹 清空」= 真删磁盘 + 删索引行（二次确认 → 排进队列）。**全部资产页新增「↗ 应用到文件」**（= 把名字真写到磁盘；与「💾 保存」分工明确）。⚠️ **3 处偏差待拍板**：① 移动 / 复制的**服务已实现但无 UI 入口**（缺"选目标目录"对话框）；② 回收站详情未显示「删除时间」（`AssetRow` 无该字段）；③ **重试只对扫描任务开放**，物理操作不做通用重试。自查 `check.ps1` 通过（42 个 `.axaml`）；✅ **用户本地编译通过（= 按本轮约定视为验收通过）**。
- **v3.35 / v3.34 / v3.33 / v3.32**（归档）：NT-018 三页 + 逻辑操作 / NT-017 薄壳类型页 / 媒体资产数据层与骨架 / 视频工具精简 + 示例数据 —— 明细见 `git log`。

---

## 一、当前基线

| 项 | 内容 |
|---|---|
| 工程版本 | 编译自动生成 `1.<yyyyMMdd>.<HH>.<m0>`（`src/Directory.Build.props`），禁手写 |
| 文档版本 | 需求 v3.41 ｜ 规范 v3.24 ｜ 协作手册 v3.20 ｜ 本台账 v3.42 |
| 技术栈 | .NET 10 + Avalonia 12.1.4 + CommunityToolkit.Mvvm 8.4.2 + MEDI 9.0.0 + Microsoft.Data.Sqlite 9.0.0 |
| 代码规模 | 7 项目 / 251 个 `.cs+.axaml+.csproj` / 约 42600 行 |
| 仓库根 | 能看到 `src/` 的那一级 |
| 版本控制 | ✅ git（分支 `main`） |
| 构建分工 | 🧑‍💻 构建 / 发布由用户本地执行（`pwsh -File .\scripts\build.ps1`）；AI 只改 `src/`（**例外：`scripts/` 构建脚本经用户明确授权可改**——2026-10-13 授权，Settings 物理必备改造） |

**编译状态**：✅ v1.14 / v1.16 本地 0 错；⚠️ v1.21~v3.12 尚未由用户本地复跑 → 待跑；**v3.13 / v3.14 已于 2026-10-12 本地编译通过 ✅**；**v3.15 / v3.16 已于 2026-10-13 经用户本地验收通过 ✅**（v3.15 附 3 条整改 → v3.20）；**v3.20 / v3.21 / v3.22 已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（v3.21 的 pin / 列宽 / 筛选 / 关闭按钮四项在 v3.22 重做后一并验收）；**v3.24 标题栏第三轮 + 渲染修复已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（commit `b658c39`）；**v3.27（NT-023 框架 + NT-024 多实例）已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（commit `0ded557`）；**v3.28（NT-025 窗口缩放 + 系统贴靠 + bounds 记忆）已于 2026-10-13 经用户本地编译 + 实机验收通过 ✅**（含五轮实机复验）。**v3.37（夜间批量 8 项 + 5 处编译修复）已于 2026-10-14 经用户本地编译通过 ✅**；**v3.29 ~ v3.36 已于 2026-10-14 经用户本地编译通过并按「编译通过 = 验收通过」验收 ✅**（🔴 用户 2026-10-14 定调：本轮起**编译通过即视为验收通过**，不再逐项实机核验）。

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
| 系统设置 | 10 | **10** | 0 |
| PC 工具箱 | 5 | **5** | 0 |
| Web 工具箱 | 5 | **5** | 0 |
| 视频工具箱 | 3 | **3** | 0 |
| 媒体资产 | 12 | **12** | **0** |
| **合计** | **35** | **35** | **0** |

> **骨架页**：无（web-http 已由 NT-001 落成真实页；媒体资产 11 页已由 NT-017~022 逐页落地 + NT-016 `assets-all` 真实列表页，零骨架）。

### 2.4 菜单清单（37 项）

> 权威来源 = 各模块 `Initialize()` 的 `RegisterModule/RegisterFunction`；冲突以代码为准。

| 分组 | Key | Order | 图标键 | 二级菜单（Key 标题/View/Order/度） |
|---|---|---|---|---|
| 首页 | — | — | `IconHome` | （树的叶子节点；也是首个固定标签页） |
| 电脑工具 | pc | 20 | `IconPc` | `pc-favorites`收藏夹/0/✅ ｜ `pc-shortcuts`设置与脚本/1/✅ ｜ `pc-quicksettings`快捷设置/2/✅ ｜ `pc-package`软件包/3/✅ ｜ `pc-info`系统信息/99/✅ |
| 网络工具 | web | 30 | `IconWeb` | `web-http`HTTP测试/1/✅ ｜ `web-bookmark`书签/2/✅ ｜ `web-ping`网络Ping/3/✅ ｜ `web-scrape`网页抓取/4/✅ ｜ `web-hosts`Hosts管理/5/✅ |
| 视频工具 | video | 40 | `IconVideo` | `video-list`视频列表/0/✅ ｜ `video-cut`**视频预览**/1/✅ ｜ `video-batch`**批量处理**/2/✅ |
| 媒体资产 | assets | 50 | `IconMediaAssets` | `assets-all`全部资产/0/✅ ｜ `assets-video`视频/1/✅ ｜ `assets-music`音乐/2/✅ ｜ `assets-book`书籍/3/✅ ｜ `assets-image`图片/4/✅ ｜ `assets-document`文档/5/✅ ｜ `assets-tags`标签管理/6/✅ ｜ `assets-categories`分类管理/7/✅ ｜ `assets-collections`集合管理/8/✅ ｜ `assets-storages`存储源/9/✅ ｜ `assets-tasks`任务中心/10/✅ ｜ `assets-trash`回收站/11/✅ |
| 设置 | settings | 999 | `IconSettings` | `settings-general`通用/0/✅ ｜ `settings-theme`主题/1/✅ ｜ `settings-path`路径/2/✅ ｜ `settings-launch`一键按钮/3/✅ ｜ `settings-dns`加密DNS/4/✅ ｜ `settings-package`软件源/5/✅ ｜ `settings-profile`配置/6/✅ ｜ `settings-data`数据管理/7/✅ ｜ `settings-about`关于/8/✅ ｜ `settings-all`功能清单/9/✅ ｜ `settings-media-assets`媒体资产/100/✅（媒体资产模块自注册） |

---

## 三、待办与优先级

> 本轮范围外的项（详情卡上提 / 搜索框 / 历史栈 / 图标矢量化）见**需求 §4.11**，此处不重复登记。
> **工作项来源（规划期产出）**：每次新需求立项，由 AI 对比《需求规格》应然 vs 本节「一、基线」生成；逐条可验收、带分级（P0/P1…）。
> 流程定义见 `skill/references/collab-method.md` §二（**规划 / 执行双模式 + 就绪闸门（DoR）+ 批量协议**）；本表 = **Backlog 工作项台账**（紧凑索引），**任务卡**统一在 `src/docs/backlog/NT-xxx.md`。
> 生命周期：**📝 Draft（草稿）** → **✅ Ready（就绪，过 DoR）** → **🔄 In Progress（执行中）** → **✔ Done（已完成待验收）** → 用户验收后 **Accepted（已验收）**；验收后台账工作项翻 ✔、任务卡**保留**（⛔ 不删）。台账只翻状态，不随任务卡删。
> 术语（立项 / 工作项 / 任务卡 / 就绪闸门 / 完成定义 …）见《AI协作手册》【术语对照】。

### 工作项索引（Backlog）
> 任务卡（验收标准 / 技术要点 / 依赖 / 范围）见 `src/docs/backlog/NT-xxx.md`；**实施期只跑其中 `✅ 就绪` 项**（`📝 草稿` 不执行）。
> 睡前开会话发「**执行全部就绪待办**」即可让 agent 批量跑（⛔ 不用 automation 定时触发）。

| NT | 标题 | 优先级 | 状态 | 详情 |
|---|---|---|---|---|
| NT-001 | web-http 实装（唯一剩余骨架页） | P2 | ✔ 已完成（已验收） | backlog/NT-001.md |
| NT-002 | pc-package 收尾（Parser 实机 + 批量队列） | P2 | ✔ 已完成（已验收） | backlog/NT-002.md |
| NT-003 | settings-package 软件源（退出码验证） | P2 | ✔ 已完成（已验收） | backlog/NT-003.md |
| NT-004 | defaults\ 与脚本目录播种确认 | P2 | ✔ 已完成（已验收） | backlog/NT-004.md |
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
| NT-017 | 5 个类型页（视频 / 音乐 / 书籍 / 图片 / 文档）= **薄壳 + 预设类型**，共用 `assets-all` 实现（v3.34；⛔ 网格视图不做、类型专属列延后） | P1 | ✔ 已完成（已验收） | backlog/NT-017.md |
| NT-018 | 标签 / 分类 / 集合三页 + 逻辑操作 + `operation_logs`（⚠️ 含 2 处偏差：集合排序用 ⬆⬇ 按钮、智能集合规则未实装） | P1 | ✔ 已完成（已验收） | backlog/NT-018.md |
| NT-019 | 物理操作（`FileOperationService` 日志先行 + 防穿越 + 冲突策略 + 回收站）+ 任务中心 + 回收站页（⚠️ 含 3 处偏差：移动/复制无 UI 入口、删除时间未显示、重试仅扫描任务） | P1 | ✔ 已完成（已验收） | backlog/NT-019.md |
| NT-020 | 存储源页 + 移动硬盘离线 / 上线 + 外部变更校准 | P2 | ✔ 已完成（已验收） | backlog/NT-020.md |
| NT-021 | 媒体资产设置页（`settings-media-assets`，View/VM 归本模块、自注册进 settings 组）+ 元数据 + 缩略图 | P2 | ✔ 已完成（已验收） | backlog/NT-021.md |
| NT-022 | 列表 JSON 导入 / 导出 + 用户数据导出（R1 缓解） | P2 | ✔ 已完成（已验收） | backlog/NT-022.md |
| NT-023 | 框架：MenuService 跨组延迟补登 + Settings 必备化 + 启动健全性检查 / 无模块降级 | P1 | ✔ 已完成（已验收） | backlog/NT-023.md |
| NT-024 | 多实例：`--profile` 启动参数 + 每配置单例 + 切换新语义（不写全局默认）+「设为默认」/「🚀 新开」+ 标题带配置名 | P1 | ✔ 已完成（已验收） | backlog/NT-024.md |
| NT-026 | 媒体资产：多库融合（工作副本模型 = 打开库 A → 再打开库 B → 💾 保存；「减」= 勾选后删除；合并走 `ATTACH DATABASE`） | P1 | ✔ 已完成（已验收） | backlog/NT-026.md |
| NT-025 | 窗口：自绘缩放抓取区（四边 5px/四角 10px，BeginResizeDrag 八向）+ 分屏贴靠验证 + bounds 按配置记忆 | P1 | ✔ 已完成（已验收） | backlog/NT-025.md |
| NT-027 | 视频工具精简：删视频库(.db) / 标签 / 收藏页 → 收窄为「简易查看器 + 送批量任务」（含旧表一次性清理） | P0 | ✔ 已完成（已验收） | backlog/NT-027.md |
| NT-028 | 示例数据 + 详情卡默认示例态（批次 1~4 已落：视频两页 / `assets-all` / PcTools 4 页 / WebTools 4 页 / `settings-path`；批次 5 类型页随 NT-017；含框架层 `ListPageViewModelBase` 授权改动） | P1 | ✔ 已完成（已验收） | backlog/NT-028.md |
| NT-029 | 回收站显示删除时间（NT-019 偏差②闭合） | P1 | ✔ 已完成（已验收） | backlog/NT-029.md |
| NT-030 | 移动/复制 UI 入口（NT-019 偏差①） | P1 | ✔ 已完成（已验收） | backlog/NT-030.md |
| NT-031 | 视频类型专属列（NT-017 试点） | P2 | ✔ 已完成（已验收） | backlog/NT-031.md |
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
| NT-043 | 标题栏是否改用 `TitleBarBrush`（✅ 拍板**不启用**，清理并入 NT-049） | P2 | ✔ 已完成（已验收 · 并入 NT-049） | backlog/NT-043.md |
| NT-044 | 标题栏加全屏按钮（⛶）+ `F11` / `Esc` 快捷键 | P2 | ✔ 已完成（已验收） | backlog/NT-044.md |
| NT-045 | 图标实现方式统一（C 方案：全框架矢量 `Geometry` 库；窗口按钮弃图标字体 / 标签 ✕ 与树展开符弃字符；折叠菜单→panel-left、模块加载→拼图） | P2 | ✔ 已完成（已验收） | backlog/NT-045.md |
| NT-046 | 图标收尾：最小化退化与居中修复 / 拼图与全屏改型（两箭头）/ 左树去展开符 / 「框架层图标只用 `Path`」写进规范 | P2 | ✔ 已完成（已验收） | backlog/NT-046.md |
| NT-047 | 数据驱动图标矢量化（二级功能菜单 34 项 / 首页一键按钮 / 模块页内数据行；⛔ 静态装饰 emoji 不动） | P2 | 📝 草稿 | backlog/NT-047.md |
| NT-049 | 清理 3 个未引用标题栏主题键（`TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush`；主题键 34 → 31） | P2 | ✔ 已完成（待本地编译 + 验收） | backlog/NT-049.md |
| NT-050 | 设置归位：模块专属设置迁模块（软件源 View/VM → PcTools，媒体资产先例模式）+ settings 内「媒体资产」改名「媒体资产设置」 | P1 | ✅ 就绪 | backlog/NT-050.md |
| NT-051 | 设置页合并：主题外观 + 关于 并入 通用设置（单页三节；键 `settings-general` 不变，删 `settings-theme`/`settings-about` 两项） | P2 | ✅ 就绪 | backlog/NT-051.md |
| NT-052 | 「🔄 重载模块」移入设置（标题栏 3→2 按钮；入口 = **功能清单页**「增加功能」操作区；方案 A：Shared/Core 新增 `IModuleReloadService` 契约，2026-10-15 用户拍板） | P2 | ✅ 就绪 | backlog/NT-052.md |
| NT-053 | 设置页窄界面适配：最窄 960×640（内容宽 720）与默认 1280×800 两种可用（根 ScrollViewer / 星号列 / 去超宽固定容器） | P2 | ✅ 就绪 | backlog/NT-053.md |

> `📝 草稿` 需继续展开细节，升 `✅ 就绪` 后才会被批量执行；`✔ 已完成（待验收）` 经用户验收后台账翻 ✔（任务卡保留、⛔ 不删）。
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
| 2026-10-15 | **验收** | **窗口装饰定案 ✅ 已验收（2026-10-15 04:31 用户实机）** —— 本地 `build.ps1` 编译通过；实机反馈「这次反而解决的比较完美了，只有一点点瑕疵」⇒ **官方 CSD（`Full` + 隐式键空主题）+ `WM_NCCALCSIZE` 零内缩（Windows-only）** 定案；功能全过（贴靠 / 拖拽 / 八向缩放 / 最大化还原 / 全屏进出 / 方角）。唯一残留 = **首次分屏左右下偏大 1~2px**，与 Firefox 同级（系统取整 / 动画帧落位）⇒ **接受，⛔ 不做外部纠偏**（NT-048 方向已证伪）。⇒ 本条入口由「零内缩复归」行转为已验收状态。 |
| 2026-10-15 | **零内缩复归** | **贴靠缝定位 + 加回零内缩（🟡 已落盘 · 待本地编译）** —— 实测规律（两种实现都一样）：**贴靠第 1 次缝小 ≈1px、第 2 次变大 ≈8px** ⇒ 8px = Avalonia CSD 按 `WS_CAPTION\|WS_THICKFRAME` 扣出的**非客户区**边框（贴靠态系统不画那圈 → 露缝）；第 2 次才变大 = 它按「外框 = 客户区 + 边框」的记账回写几何、赢过系统给的贴靠矩形。修法：**只加回一处 `WM_NCCALCSIZE` 零内缩**（`SnapFrameWndProc` 分支 + 常量；钩子只在 Windows 装得上 = 天然平台门控），官方 CSD 其余全保留（`Full` + 隐式键空主题 + 不补样式位 + 不搞几何纠偏）。残留 ≈1px 为系统取整（Firefox 同现）⇒ 接受。文档：需求 §4.1.1 ② / 规范 §2.4 ②④ + §7 B17 同步。 |
| 2026-10-15 | **定案** | **窗口装饰：采纳官方 CSD（🟡 已落盘 · 待本地编译）** —— 用户拍板「搞 CSD 重构，直接改」。**依据**：跨平台评估（目标 Debian 13）—— Wayland 无服务端装饰、贴靠交合成器管、**无 DWM / `WS_*` / GDI region** 概念 ⇒ Win32 补丁路线在 Linux 整体作废；且「贴靠态 1px 残差」已证为系统级（Firefox 同复现）⇒ 不再为它堆补丁。**代码**：`ShellView.axaml` 回 `Full` + `WindowDecorationsTheme="{DynamicResource {x:Type chrome:WindowDrawnDecorations}}"`；`App.axaml` 隐式键空 `ControlTheme`（不给 `Template`）；`ShellView.axaml.cs` 删 `ApplySnapEligibleStyles()`（+ `GetWindowLongPtr` / `GWL_STYLE` / 6 个 `WS_*` 常量 / 两处调用）与 `WM_NCCALCSIZE` 零内缩分支（+ 常量），**保留** NT-034 三保险（方角）、NT-039 自绘外框、NT-044 全屏；NT-048 全套纠偏保持删除。**文档**：需求 §4.1.1 / 规范 §2.4 改写为官方 CSD + 新增「跨平台（Debian 13）注意」小节（两条迁 Linux 必修项）、§7 B17 改为「Win32 补丁残留」、台账 NT-040/041 恢复「已完成」、NT-048 作废理由改为「系统级」。 |
| 2026-10-15 | **返工**（已被上一行取代） | **窗口装饰路线反转（回到 NT-034 基线，🟡 已落盘 · 待本地编译）** —— 用户拍板：「NT-034 之后的是我为了想向官方方案靠拢准备的**重构**，结果重构不能完美实现了」⇒ 按 NT-034 重新实现当前 UI。**代码**：`ShellView.axaml` 装饰由 `Full` 回 `BorderOnly` + `WindowDecorationsTheme` 指向**命名空主题** `NoDrawnWindowDecorations`（`App.axaml` 里隐式键 CSD 主题删除）；`ShellView.axaml.cs` **恢复** `ApplySnapEligibleStyles()`（+ `GetWindowLongPtr` / `WS_*` 常量 / 两处调用）与 `SnapFrameWndProc` 的 `WM_NCCALCSIZE` **零内缩**分支，**删除** NT-048 全套纠偏（`RememberWantedFrame` / `CorrectSnapFrame` / `CorrectSnapGeometry` / `MONITORINFO` + 2 个 P/Invoke / `ClearSquareRegion` / `ApplyWindowFrame` 的基准钉位 / `WM_WINDOWPOSCHANGING` 分支，净删 187 行）。静态自检 `check.ps1`（44 axaml / 31 键，0 问题）+ `check-axaml-props.ps1`（6363 属性名，0 问题）通过。⚠️ 同日经跨平台评估后**被上方「定案」行取代**（官方 CSD 而非 NT-034 基线）；其中「删除 NT-048 纠偏」这一点被定案继承。 |
| 2026-10-15 | **验收** | **NT-045 / NT-046 ✅ 已验收（2026-10-15 00:53 用户实机）** —— NT-046 回归 5 项全过（最小化 = 横杠且与 □ ✕ ⛶ 同一水平线；全屏两箭头 + `F11`/`Esc`；重载模块为可辨识拼图；左树行首无箭头、一级/二级行对齐、分组仍可展开折叠；折叠态 48px 只剩图标），唯一遗留项「最小化横杠偏上」由 `Stretch="None"` + 16×16 控件坐标（`M2,8 H14`）修掉并复验通过 ⇒ **两项均关闭**。NT-045 的图标统一（含「裸主题里 `Path` 渲染不出来 = ∞ 约束 NaN」的模板改造）随本次实机一并验收。 |
| 2026-10-15 | **NT-046** | **框架层图标收尾（🟡 已落盘 · 待本地编译）** —— 用户对 NT-045 的实机反馈逐条落实：① **最小化图标显示成一个「点」**（截图实证）→ 真因不是"裸主题 Path 不渲染"，而是 `IconWinMinimize` 写成 `M4,12 H20` = **零高度包围盒** + `Stretch=Uniform` 等比缩放退化 ⇒ 改**闭合细矩形**（`M4,11.5 H20 V12.5 H4 Z`），并在规范里写死「框架层几何必须有非零宽高」；② 重载模块拼图在 16px 下糊成一团（Lucide `puzzle` 8 段弧）→ 改**自绘三凸块拼图**；③ 全屏图标按用户要求由「四角括弧」改**两个斜箭头**（Lucide `maximize-2` / `minimize-2`，朝外=进入 / 朝内=退出）；④ **左树行首展开箭头去掉**（只有一级有图标，行首再占 12px 造成一级/二级行不对齐）→ `Grid ColumnDefinitions="Auto,Auto,*"` → `"Auto,*"`，`TreeMenuItem.ShowChevronDown/Right` 删除，`IconChevronDown/Right` 两键留备。🔴 **顺带取证**：截图里窗口按钮的方框 / ✕ 正常显示 ⇒ **NT-045「裸主题里 `Path` 渲染不出来 = ∞ 约束 NaN」的推断成立**（规范 §2 由"推断 + 兜底"改判为"实机确认"）。🔴 **新约束**：规范 §2 写死「**Shell 框架层图标只用 `Path`**」—— ⛔ 图标字体 / 字符 / emoji / `Rectangle` 拼几何；数据驱动图标（首页「常用功能」「一键按钮」）按用户要求**暂不纳入**（技术债）。 |
| 2026-10-15 | **NT-045** | **图标实现方式统一（C 方案，🟡 已落盘 · 待本地编译）** —— 用户拍板「全框架只用一套矢量 `Geometry` 库」，框架层图标由 4 种载体收敛为 **1 种**（`Path` + `App.axaml` 的 `Icon*` 键）：① 窗口按钮 `— □ ✕ ⛶` 由**图标字体** `Segoe MDL2 Assets` 改矢量（`IconWinMinimize/WinMaximize/WinRestore/WinClose/FullScreen/ExitFullScreen`）② 标签关闭 ✕、新建 ＋、左树展开指示 `▾ ▸`、首页 `★ ⚡ ⚙` 由**字符 / emoji** 改矢量（`IconTabClose/Plus/ChevronDown/ChevronRight/Star/Zap/Gear`）③ 折叠菜单按钮图案按用户指定改为「面板 + 左栏」(`IconMenuPanel`，Lucide `panel-left`)；重载模块保持拼图 `IconPuzzle`。几何统一 **24×24 网格**，颜色 / 粗细一律由各按钮的后代选择器样式给（⛔ 不逐处写描边）。🔴 **顺带处理 v3.22 的「`Path` 在裸主题里渲染不出来」**：按**推断**（模板把 `HorizontalContentAlignment` 绑到 ContentPresenter 自身对齐 → ∞ 约束下 `Stretch=Uniform` 算出 NaN 尺寸）把模板改成 Fluent 等价写法；**未实机验证**，已备兜底（改 `Stretch="None"` 或退回图标字体）。结构改动：`TreeMenuItem.Glyph`（字符）→ `ShowChevronDown/ShowChevronRight`，左树一级图标删 emoji 回退分支。⚠️ 范围外（技术债）：首页「常用功能 / 一键按钮」的图标是**用户 / 模块配置数据**，仍为字符串。 |
| 2026-10-14 | **验收** | **NT-041 / NT-044 ✅ 已验收（2026-10-14 23:38 用户实机，4 项全过）** —— 本地 `build.ps1` 编译通过；**NT-041**：贴半屏 / 四分屏 / `Win+方向键` 全部可用、贴靠后三边不露缝、八向缩放无渲染异常、最大化还原正常 ⇒ 官方 `Full` 装饰确实自带贴靠样式位，`ApplySnapEligibleStyles` 属真冗余，**本项关闭**（②③ 两处补丁仍被需要，⛔ 不再追删）；**NT-044**：`⛶` 铺满屏覆盖任务栏、再点还原，`F11` 切 / 全屏态 `Esc` 退出、非全屏 Esc 不被吞，全屏外框收掉 + 拖边不缩放，🔴 **全屏 → 退出 → 再贴靠仍可用**。 |
| 2026-10-14 | **NT-041 / NT-044** | **NT-041 第二次尝试（只删样式位补丁，🟡 已落盘 · 待本地编译）** —— 删 `ApplySnapEligibleStyles()` + 两处调用 + 6 个样式位常量 + `GetWindowLongPtr`；`WM_NCCALCSIZE`（贴靠缝隙）与 resize 兜底**保留**（首轮一次性删三处实测：贴靠 OK 但**露缝**、八向缩放渲染异常 → 已回滚 `f132687`）。**NT-044 全屏按钮**（🟡 已落盘 · 待本地编译）—— 标题栏加 `⛶`（`BtnMin` 左边，46×40，`Border` 四角括弧图标：朝外=进入 / 朝内=退出）+ `F11` 切 / `Esc` 仅全屏态退出（**隧道**注册 `KeyDown`）；⛔ **未设** `ElementRole="FullScreenButton"`（父 `StackPanel` 已标 `User`，切换走我们的 `Click`，避免与框架重复触发）。🔴 顺带两处必须修正：① `ApplySquareRegion` 加 `_isFullScreen`（全屏时 `showCmd` 未必是 `SW_SHOWMAXIMIZED`，沿用旧判定 → **旧矩形区域裁掉内容**）② `SaveWindowBounds` 全屏态按最大化存 `rcNormalPosition`（否则下次启动是「铺满屏的普通窗口」）。**NT-042** 基准测试开关 `UseSquareRegionHack` 已备（置 `false` 即可 A/B：圆角复发 vs DWM 阴影回归；⛔ 刻意不用 `const`，防 CS0162 + TreatWarningsAsErrors），结论待用户本地测。静态自检 `check.ps1` + `check-axaml-props.ps1` 全过。 |
| 2026-10-14 | **UI/配置批** | **NT-034~038（5 项就绪待办批量执行，🟡 已落盘 · 待本地编译）** —— **NT-034** 窗口圆角（✅ 已验收；**6 轮定位后定案**：`SetWindowRgn` 矩形窗口区域强制方角 + DWM 偏好压制 + Avalonia 官方开关三保险。🔴 排查结论：① 圆角 = `WS_THICKFRAME|WS_CAPTION` 样式位带来的，与贴靠资格**绑死**，删样式位走不通（改 `WindowDecorations=None` 反使分屏露背景更明显，已回滚）② Avalonia 每次 `ShowWindow`→`ExtendClientArea()` 会回写圆角偏好 → 必须在**它处理完之后**再压 ③ 两条死诊断：`Window.Title`（无边框自绘不显示）、`DwmGetWindowAttribute(33)`（不支持读）；**NT-035** 媒体资产一级图标矢量化（`App.axaml` 加 `IconMediaAssets`，注册改传键名，去 emoji）；**NT-036** 标签关闭 ✕ 不再压字（`tab-btn` 右侧预留 28px，文字左移、✕ 落入右侧空白、宽度常驻不跳）；**NT-037** 左树一级展开整组 `BringIntoView`（VM 抛 `TreeNodeExpanded` → ShellView 滚入视野，防底部子项被裁）；**NT-038** 内置三配置档（`ConfigStoreService.EnsureBuiltInProfiles/ResetBuiltInProfile`，debug=复制 default 的 `config`、只增不覆盖，启动播种 + 设置页「♻ 重置为初始态」；🔴 框架层 Core/Shared 改动）。需求 §3.5 / §2.1（远期规划）/ §6.2 同步（v3.37）。静态自检 `check.ps1`（44 axaml，0 非法）+ `check-axaml-props.ps1`（6365 属性名，0 问题）全过。**NT-034~038 全部 ✅ 已验收**（用户本地编译通过 + 逐条实机确认；NT-038 于 2026-10-14 20:31 确认通过）。 |
| 2026-10-14 | **fix** | **NT-032 + NT-033（深入审查两项 P1 逻辑缺陷修复，✅ 已落盘 + 本地编译通过 ✅）** —— 扫描"大小+修改时间"移动识别键改多值字典、仅**唯一候选**认作移动（修"复制且保留时间戳"误把原文件位置改写到副本、原文件变 missing）；多库融合在事务内清除"零文件位置"的重复资产（含其标签/分类/集合/关联），修重合并同一库 / 合并共享文件两库产生的孤儿资产，类注释"幂等"表述同步修正。静态自查 check.ps1 + check-axaml-props.ps1 全过；**用户本地 `build.ps1` 编译通过，按本轮约定视为验收通过 ✔** |
| 2026-10-14 | **v3.36** | **NT-019 物理操作 + 任务中心 + 回收站（✅ 已验收）** —— 新增 `FileOperationService`（全模块唯一动磁盘：`Rename`/`Move`/`Copy`/`DeleteToRecycle`/`PermanentDelete` + `ConflictPolicy` + `IsInside` 防穿越；🔴 日志先行 `pending→磁盘→file_locations→success/failed`，⛔ 失败不回滚；删除默认送 `SHFileOperationW` 系统回收站）；`assets-tasks`（运行态队列可取消 + 扫描记录可重扫 + 操作日志）；`assets-trash`（`status='trashed'` + `TrashDetailView` 含受影响文件位置，恢复=逻辑 / 永久删除·清空=真删磁盘并排进队列）；全部资产页新增「↗ 应用到文件」（把名字真写到磁盘）。⚠️ 3 处偏差：移动/复制无 UI 入口、删除时间未显示、重试仅扫描任务 |
| 2026-10-15 | **立项** | **设置重构立项 NT-050~053（🟡 规划期 · 纯文档，未动 `src/`）** —— 用户 5 项诉求：① 模块专属设置像媒体资产一样归模块自注册 → **NT-050**（软件源迁 PcTools；加密 DNS 经评估为 app.json 应用级⛔不迁；一键按钮/功能清单为 Shell 级⛔不迁）② settings 内「媒体资产」混淆一级菜单 → 改名「媒体资产设置」（NT-050）③ 设置页合并评估 → **NT-051**（主题+关于并入通用；配置+数据管理经评估**不合并**：配置切换=重启本窗、语义重）④ 重载模块非常用 → 移入设置（**NT-052**，标题栏 3→2；⚠️ 框架层最小新增 `IModuleReloadService` 待用户确认故列草稿）⑤ 页面容器窄界面适配 → **NT-053**（基线 960×640/内容宽 720；实测设置页均已有根 ScrollViewer、固定宽容器均在 720 内，疑似点=ProfileView 无根 ScrollViewer、DataView 固定列）。**文档**：需求 v3.40（§4.1 标题栏两按钮、§4.9 三条新规则、§6.5 重写设置清单）、台账 v3.41。⛔ 规划期不改代码 |
| 2026-10-15 | **定案** | **NT-052 拍板：方案 A + 入口改功能清单页（🟡 规划期 · 纯文档，未动 `src/`）** —— 用户选**方案 A**（框架层加契约：`Shared/Services/IModuleReloadService`（`HotReload()` + `Reloaded` 事件）+ `Core/Services/ModuleReloadService` 包装现有 `ModuleLoader.HotReload`；`ShellViewModel`（已核实 `AddSingleton` 进程级单例 ⇒ 订阅无泄漏）订阅事件重建左树 + 底栏消息）。**对比结论**：性能三方无差异（点击一次的操作）；维护性/复用性 A 优（契约显式、事件模式有 `INavigationService.Navigated` 先例、未来托盘/命令行可复用）；C（Shell 自营设置页）破 §6.5「设置页归 Module.Settings」归口且 Shell 零 `RegisterFunction` 先例；B（直接调 `ModuleLoader` 静态类）因 `IMenuService` 无任何事件 ⇒ 左树不刷新 = 静默失效，不成立。**入口**：标题栏 3→2 按钮，「🔄 重载模块」落**功能清单页**（`settings-all`）顶部「增加功能」操作区（重载 = 给清单增加功能，语义最顺）⇒ 与 NT-051 **依赖解除**，NT-052 升 `✅ 就绪`；重载后本页 `BuildGroups()` 刷新卡片。**文档**：需求 v3.41（§4.1 / §6.5）、台账 v3.42。⛔ 仍未动 `src/` |
