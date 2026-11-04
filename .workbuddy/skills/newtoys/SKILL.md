---
name: newtoys
description: NewToys 项目（.NET 10 + Avalonia 12.1.5 Windows 桌面工具管理器）的开发协作技能（WorkBuddy 薄指针）。正文与深度路由表在仓库根 AGENTS.md 与 skill/ 目录，跨 AI 共享（WorkBuddy 与 Hermes/DeepSeek Harness 共用）。涉及本仓库任务——页面/列表页/设置页、任意模块（ScriptManager / WebTools / VideoTools / MediaAssets / SystemTools / BackupCenter / Viewer / Reading / 应用级设置）改动、主题样式、编译报错、文档台账——动手前先加载本技能，按其指引读仓库根 AGENTS.md 与 skill/SKILL.md。触发词：NewToys、src/、模块文档、framework.md、ui.md、需求规格、台账、build.ps1。
agent_created: true
---

# NewToys 开发协作（WorkBuddy 薄指针）

⚠️ **本文件不含正文**，只负责触发 + 导航。正文与约束**唯一源** = 仓库根 `AGENTS.md` 与 `skill/`。⛔ 不在本文件重复铁律/硬约束（重复内容会漂移，且每轮额外注入）。

## 读取顺序

1. 仓库根 `AGENTS.md`（定位 + 铁律 + 文档路由表）
2. `skill/SKILL.md`（中立导航 + 客户端接入表）
3. `skill/docs/index.md`（文档地图 + 模块清单）+ `skill/docs/NewToys-项目状态与路线图.md`「一、当前基线」
4. 按任务读 `skill/docs/framework.md` / `skill/docs/ui.md` / `skill/docs/modules/<模块>.md` 对应节
5. 交付前 `pwsh skill/scripts/accept.ps1`（静态验收，不含构建）

🔴 权威源 = `skill/docs/`（index / framework / ui / modules / 需求规格 / 台账 / 手册）+ `src/` 源码；**规划 / 执行双模式**唯一定义处 = `skill/docs/NewToys-AI协作手册.md`。冲突以源码为准。
