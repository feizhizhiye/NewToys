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

## 目录

- `references/collab-method.md` —— **通用协作方法论（跨项目母本）**：铁律、两阶段工作流、变更声明、静态自检、交付要求、分级读档
- `references/newtoys-spec.md` —— **NewToys 特定约束**：项目事实、密度基线、高频坑、任务型→规范章节、四文档路由
- `scripts/check.ps1` —— 交付前静态自检（axaml 合法性、`.axaml`↔`.cs` 配对、主题键白名单核对）

## 执行流程

1. 读仓库根 `AGENTS.md`（定位 + 铁律 + 四文档路由表）。
2. 读 `src/docs/NewToys-项目现状与进度.md` 的「一、当前基线」。
3. 先读 `references/collab-method.md`（通用纪律），再按 `references/newtoys-spec.md` 的「任务型→规范章节」读 `src/docs/NewToys开发规范.md` 对应节。
4. 动手前给变更声明：① 文件清单（增/改/删逐条）② 验收标准 ③ 已知风险/待用户拍板点。
5. 只改 `src/`；只做静态自检。⛔ 不代跑编译、不谎称「已编译通过」。
6. 交付前跑 `pwsh skill/scripts/check.ps1`，或人工等价检查。

## 铁律（通用见 `references/collab-method.md`，项目特定见 `references/newtoys-spec.md`）

1. **只改 `src/`**；不读写 Downloads；不碰项目目录外的任何位置。
2. **变更顺序**：新需求 → 先写《开发需求》→ 再改代码 → 最后更新台账。⛔ 先改代码后补文档。
3. **框架层（Shared/Core/Shell）冻结**，新功能只在 Module.*。停手仅 5 种；其余按最合理假设推进并标注。
4. **git commit**（`&lt;type&gt;: &lt;简述&gt;`），⛔ 不写 md 日志；**代码 &gt; 文档**。
5. **评估/讨论不进文档**。
