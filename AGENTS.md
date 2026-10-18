# AGENTS.md — NewToys 项目协作入口

> v1.7 · 2026-10-18
> v1.7：🗂 **文档按「框架层 / UI / 每模块一份」重构** —— 原《NewToys-工程规范.md》（892 行）拆为 **`skill/docs/framework.md`**（框架层 + 跨模块待办）+ **`skill/docs/ui.md`**（UI 规范），并新增 **`skill/docs/modules/<模块>.md`**（一模块一份：职责 / 当前功能 / 实现逻辑 / 数据 / 坑点 / 待办）+ **`skill/docs/index.md`**（文档地图）。任务卡按模块分目录 `skill/docs/backlog/<模块>/`，**每目录 ≤5 张已验收卡**（滑动窗口 + 验收即蒸馏）。原《工程规范》已删除。
> v1.6 · 2026-10-17
> v1.6：🗂 **文档搬家 + 归档策略修订** —— 全部权威文档 `src/docs/` → **`skill/docs/`**（skill 自封装，`src/` 只放代码；铁律 1 的例外同步扩到 `skill/`）；任务卡归档由「全量保留」改为「**默认保留 + 被后续工作项整体取代/作废的卡删除**」（原定调作废，已删 NT-004/017/028/031/043/048）。
> v1.5 · 2026-10-16
> v1.5：交付前门禁由「手记两条 Lint 命令」改为**一键验收** `pwsh skill/scripts/accept.ps1`（`skill/scripts/accept.ps1` = 4 个静态检查 + 构建 + 产物核对 + 改动范围，输出结论与退出码 0/1；`-Run` 追加冒烟）。
> v1.4 · 2026-10-14
> v1.4：🏷 **术语标准化** —— 项目内自造说法统一映射到业界标准术语（立项 / 工作项 / 任务卡 / Backlog / 就绪闸门 DoR / 完成定义 DoD / 阻塞上报 / 技术债 …）；四份文档各标注**标准角色**。🔴 术语与研发流程的**唯一定义处** = `skill/docs/NewToys-AI协作手册.md`【术语对照】+【研发流程】；本文件只引用。
> v1.3：入口去重——本文件为**唯一自包含入口**（必须全文可独立读懂）；`skill/SKILL.md` 与 `.workbuddy/skills/newtoys/SKILL.md` 降为**导航 / 指针**，不再重复正文；原「高频硬约束」节整节下沉至 `skill/references/newtoys-spec.md`。
> v1.2：铁律 1 加例外——`scripts/` 构建脚本**经用户明确授权可改**（2026-10-13 授权：Settings 物理必备改造，NT-023）。
> 深度约束 / 坑点 / 静态检查（Lint）脚本在仓库根 `skill/` 目录（`references/collab-method.md` 通用方法论 + `references/newtoys-spec.md` NewToys 专属约束 + `scripts/check.ps1` + `scripts/check-axaml-props.ps1`）；WorkBuddy 经 `.workbuddy/skills/newtoys/` 指针、Hermes(DeepSeek Harness) 经本文件路由读取。
> 本文件是**跨 AI 工具的通用入口**（Codex / Cursor / Devin / Jules / Amp 等自动读取 AGENTS.md）。
> 权威规格在 `skill/docs/`，本文件只做「定位 + 铁律 + 路由」；**冲突时以 skill/docs 与 src/ 源码为准**。
> 🔴 **工作流与术语不在此定义**：`研发流程（规划期 / 实施期）` + 状态机 + **术语对照（Glossary）** 的**唯一定义处** = `skill/docs/NewToys-AI协作手册.md`【研发流程】+【术语对照】节。本文件与 `skill/` 只做定位 / 路由 / 约束，⛔ 不另写一份流程、⛔ 不另立术语——保证不同 AI 读任一入口都落到同一套工作流。
> WorkBuddy 用户：项目级技能 `.workbuddy/skills/newtoys/SKILL.md` 仅为**薄指针**（触发后指回本文件与 `skill/`），⛔ 不重复正文——避免同一内容多份注入、多份漂移。

## 这个项目是什么

NewToys = 带 GUI 的 Windows 桌面工具管理器（脚本管理器定位，不做社交/推荐/埋点/云同步）。

- 技术栈（锁定）：.NET 10 / Avalonia 12.1.4 / CommunityToolkit.Mvvm 8.4.2 / MEDI 9.0.0 / Microsoft.Data.Sqlite 9.0.0
- 分层单向依赖：Shared ← Core ← Shell ← Module.*；模块禁引 Shell、禁互引；Shared/Core 不引 Avalonia
- 工作区根 = 能看到 `src/` 的那一级；**只改 `src/`**
- 包版本只在 `src/Directory.Packages.props`；禁第三方 UI 库 / 图标字体

## 铁律（不可协商）

1. **只改 `src/`**（例外，均经用户明确授权：`scripts/` 构建脚本 / **`skill/` 知识库与 `skill/docs/` 权威文档**——2026-10-17 文档从 `src/docs/` 整体搬入 skill 封装）；不读写 Downloads；不碰项目目录外的任何位置。
2. **不代跑编译**。构建由用户本地执行 `pwsh -File .\scripts\build.ps1`；AI 只做静态检查（Lint），⛔ 不谎称「已编译通过」。
3. **Spec-first（先立规格后实现）+ 研发流程**（🔴 流程与术语唯一定义见《AI协作手册》，本文件只引用）：
   **需求提出 → 立项**（写《需求规格》）**→ 建工作项 + 写任务卡**（《状态与路线图》三、+ `skill/docs/backlog/NT-xxx.md`）**→ 过就绪闸门**（升 `✅ 就绪`）**→ 才实施**（改 `src/`）**→ 回写台账 → 验收**。
   默认**规划期**只写文档、⛔ 不改 `src/`；仅当用户发「执行全部就绪待办」/「执行 NT-xxx」才进**实施期**；`📝 草稿` 不执行。⛔ 不先改代码后补文档。任务卡按模块放 `backlog/<模块>/`，**每目录 ≤5 张已验收卡**（滑动窗口；未验收卡不参与淘汰），淘汰前必须**先蒸馏进 `modules/<模块>.md`**；跨模块卡放 `backlog/framework/` 并在 `framework.md` §11 登记（2026-10-18 修订，取代 10-14「全量保留」与 10-17「默认保留」）。
4. **框架层（Shared/Core/Shell）冻结**，新功能只在 Module.*。
5. **阻塞上报（Blocker）仅 5 种**：改框架层 / 删用户已确认的实现 / 需求自相矛盾 / 文档与代码冲突且影响本次 / 缺关键标识符。其余按最合理假设推进，并在交付里标注假设。
6. **版本记录走 git commit**（`<type>: 简述`），⛔ 不写 md 日志。
7. **指导性文档不逐次改**：仅过时/与代码冲突才改；各文档独立版本号；**代码 > 文档**。
8. **评估/讨论类内容不进文档**，只在会话里谈。

## 权威文档（`skill/docs/`）与路由

| 文档 | 角色（**标准角色**） | 何时读 |
|---|---|---|
| **`index.md`** | 文档地图 + 模块清单表 | **会话开始**（先看它定位） |
| NewToys-需求规格.md | WHAT：唯一权威规格、验收基线（**需求规格** PRD+SRS） | 涉及功能/框架/边界，至少读相关章节 |
| **`framework.md`** | HOW · 框架层（**工程规范**）：程序集边界 / 启动 / 菜单 / 加载 / UI 契约 / 配置 / 构建 / 技术债 / **跨模块待办** | 改 Shared/Core/Shell/UI 或新建模块 |
| **`ui.md`** | HOW · UI（**工程规范**）：尺寸密度 / 主题键 / 图标 / CSD / 列表页 / 详情卡 / 工具条 / 样式坑 | **改任何 `.axaml` 前必读** |
| **`modules/<模块>.md`** | 模块说明（职责 / 当前功能 / 实现逻辑 / 数据 / 坑点 / 待办） | **改哪个模块读哪个** |
| NewToys-项目状态与路线图.md | **Backlog 工作项台账**：基线 + 工作项索引 + 已完成（**项目状态与路线图**） | 会话开始读「一、当前基线」；实施期逐条勾工作项 |
| `skill/docs/backlog/<模块>/` | **任务卡**（NT-xxx）：验收标准 / 技术要点 / 依赖 / 范围 | 只读本次要实施的那张；格式与生命周期见其 `README.md` |
| NewToys-AI协作手册.md | 流程 + 术语：**研发流程**（🔴 **唯一定义处**，含状态机、滑动窗口、Glossary）+ 静态检查（**AI 协作手册**） | 流程 / 术语权威，必读其【研发流程】【术语对照】节 |

## 分级读档顺序（省 token）

1. 本文件 + `skill/docs/index.md` + 《项目状态与路线图》「一、当前基线」
2. 涉及功能/边界 → 《需求规格》相关章节（定位边界 → 一~二章；技术框架 → 三章；UI → 四章；导航 → 五章；功能域 → 六章）
3. 按任务型读 `framework.md` / `ui.md` / `modules/<模块>.md` 对应节（见下表）
4. 文档与代码冲突 → 以 `src/` 源码为准，并回写文档

## 任务型 → 文档

| 任务 | 读什么 |
|---|---|
| 改某模块的一个页面 | `modules/<模块>.md` → `ui.md`（列表页 §6 / 详情卡 §7 / 工具条 §8） |
| 改/加页面 | `ui.md` §2 §6 §7 §8 |
| 设置类页面 | `modules/settings-app.md` → `ui.md` §2 §6 |
| 列表页 | `ui.md` §6（骨架 / 列宽 / 空态 / 底栏） |
| 新增模块 | `framework.md` §3.3 §5 → 新建 `modules/<模块>.md` |
| 图标 / 样式 / 主题键 | `ui.md` §1 §3 §5 |
| 框架层（先问用户） | `framework.md` §2 §3 §4 + 需求 §3.3 |
| 标签栏 / 树 / 导航 | `framework.md` §4 + 需求 §4.1~4.3 §5 |
| 热加载模块 | `framework.md` §3.4 |
| 接口 / 契约 | `framework.md` §6 |
| 配置 / 路径 / 多配置 | `framework.md` §7 |
| 修编译报错 | `framework.md` 附录 A + `modules/<模块>.md` §5 |
| 纯文档改动 | — |

## 高频硬约束

🔴 **不在此重复正文**：命名空间 / View-VM 成对 / 主题键 / 密度基线 / 列表页约定 / 分析器坑等，唯一源 = `skill/references/newtoys-spec.md`（§一 项目事实 · §五 密度基线 · §六 高频硬约束 · §七 分析器坑）。

- 摸底一律 grep/read **定向检索**：⛔ 不整读文档、⛔ 不读 `artifacts/` `bin/` `obj/`。

## 交付要求

- 变更清单（增/改/删逐条一句话）+ 前置基线 + 用户本地验证命令。
- 直接写工作区，用 read/ls 确认落盘；⛔ 不打 zip、不列下载链接、不做 sources md 快照。
- 功能级交付 git commit（`<type>: 简述`）；纯修报错不回写台账。

## 会话启动 checklist

- [ ] 已读本文件 + `skill/docs/index.md` + 《项目状态与路线图》「一、当前基线」
- [ ] 已定位本次任务对应的 `modules/<模块>.md` / `ui.md` / `framework.md` 章节（定向读，⛔ 不整读）
- [ ] 动手前已给变更声明（文件清单 + 验收标准 + 风险/待拍板点）
- [ ] 交付前跑**一键验收** `pwsh skill/scripts/accept.ps1`（= 4 个静态检查 + 构建 + 产物核对 + 改动范围；只过 Lint 加 `-SkipBuild`；退出码 0/1。详见《AI协作手册》[4][6]）
- [ ] 交付时同步更新 `modules/<模块>.md`（功能 / 逻辑 / 坑点变了才改）
