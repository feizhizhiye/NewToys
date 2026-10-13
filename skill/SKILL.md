---
name: newtoys
description: NewToys 项目（.NET 10 + Avalonia 12.1.3 Windows 桌面工具管理器）协作技能，中立载体，供 WorkBuddy 与 Hermes(DeepSeek Harness) 等可读本地文件的 AI 客户端按路由表使用。涉及 Module.VideoTools/PcTools/WebTools/Settings 改动、页面/列表页/设置页、主题样式、编译报错、文档台账——动手前先读本文件与仓库根 AGENTS.md，按 references 取细则。触发词：NewToys、src/、视频工具、PcTools、WebTools、开发需求、开发规范、台账、build.ps1。
agent_created: true
---

# NewToys 协作技能（中立载体）

本目录是**跨 AI 共享的知识库**，与具体客户端无关：

- **WorkBuddy**：经 `.workbuddy/skills/newtoys/` 薄指针自动加载
- **Hermes / DeepSeek Harness**：选仓库根为工作区后，由根目录 `AGENTS.md` 路由读取本目录

权威源仍是 `src/docs/` 四份文档；本目录是**摘要 + 路由表**。冲突以 `src/docs` 与 `src/` 代码为准。
🔴 **本目录不定义工作流**：`规划 / 执行双模式` + 状态机 + 验收清理的**唯一定义处** = `src/docs/NewToys-AI协作提示词.md`【规划 / 执行双模式工作流】节；本目录只做路由与约束，⛔ 不另写一份（避免与 `src/docs` 漂移、不同 AI 读到不同版本）。

## 目录

- `references/collab-method.md` —— **通用协作方法论（跨项目母本）**：铁律、**规划 / 执行双模式**、草稿闸门、变更声明、静态自检、交付要求、分级读档
- `references/newtoys-spec.md` —— **NewToys 特定约束**：项目事实、密度基线、高频坑、任务型→规范章节、四文档路由
- `scripts/check.ps1` —— 交付前静态自检（axaml 合法性、`.axaml`↔`.cs` 配对、主题键白名单核对）
- `src/docs/backlog/` —— **可执行待办明细（NT-xxx）**：规划模式只写、执行模式随 `src/` 更新；`README.md` 定义格式与生命周期，台账三、为索引

## 执行流程

1. 读仓库根 `AGENTS.md`（定位 + 铁律 + 四文档路由表）。
2. 读 `src/docs/NewToys-项目现状与进度.md` 的「一、当前基线」。
3. 先读 `references/collab-method.md`（通用纪律），再按 `references/newtoys-spec.md` 的「任务型→规范章节」读 `src/docs/NewToys开发规范.md` 对应节。
4. 动手前给变更声明：① 文件清单（增/改/删逐条）② 验收标准 ③ 已知风险/待用户拍板点。
5. 只改 `src/`（例外：`scripts/` 构建脚本经用户明确授权可改）；只做静态自检。⛔ 不代跑编译、不谎称「已编译通过」。
6. 交付前跑 `pwsh skill/scripts/check.ps1`，或人工等价检查。

## 铁律（通用见 `references/collab-method.md`，项目特定见 `references/newtoys-spec.md`）

1. **只改 `src/`**（例外：`scripts/` 构建脚本经用户明确授权可改）；不读写 Downloads；不碰项目目录外的任何位置。
2. **规划 / 执行双模式 + 变更顺序**（🔴 工作流唯一定义见《AI协作提示词》，本目录只引用）：新需求 → 先写《开发需求》→ 台账三、登记待办项 + `src/docs/backlog/NT-xxx.md` 写细节 → 升 `✅ 就绪` → 再改代码 → 最后更新台账 → 验收后清理细节（默认删 / 留最近 3）。默认规划模式只写文档、⛔ 不改 `src/`；仅当用户发「执行全部就绪待办」才进执行模式；`📝 草稿` 不执行。
3. **框架层（Shared/Core/Shell）冻结**，新功能只在 Module.*。停手仅 5 种；其余按最合理假设推进并标注。
4. **git commit**（`&lt;type&gt;: &lt;简述&gt;`），⛔ 不写 md 日志；**代码 &gt; 文档**。
5. **评估/讨论不进文档**。
