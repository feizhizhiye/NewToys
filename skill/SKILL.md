---
name: newtoys
description: NewToys 项目（.NET 10 + Avalonia 12.1.4 Windows 桌面工具管理器）协作技能，跨 AI 中立载体，供 WorkBuddy 与 Hermes(DeepSeek Harness) 等可读本地文件的 AI 客户端按路由表使用。涉及 Module.VideoTools/PcTools/WebTools/Settings 改动、页面/列表页/设置页、主题样式、编译报错、文档台账——动手前先读仓库根 AGENTS.md，再按 references 取细则。触发词：NewToys、src/、视频工具、PcTools、WebTools、需求规格、工程规范、Backlog、任务卡、立项、build.ps1。
agent_created: true
---

# NewToys 协作技能（中立载体 · 导航）

本目录 = **跨 AI 共享知识库**（与具体客户端无关）。
🔴 **本文件只做导航**：正文（定位 / 铁律 / 路由表）唯一源 = 仓库根 `AGENTS.md`；细则唯一源 = `references/*`。⛔ 不在此重复正文——同一内容多份会漂移，且每多一份就多一份注入成本。

## 客户端接入

| 客户端 | 入口 |
|---|---|
| AGENTS.md-aware（Codex / Cursor / Devin / Jules / Amp / Gemini CLI / Windsurf / Cline / Roo） | 自动读仓库根 `AGENTS.md` ✅ |
| WorkBuddy | `.workbuddy/skills/newtoys/SKILL.md`（薄指针，自动触发）→ 本文件 |
| Hermes(DeepSeek Harness) / 自配 Agent | 以仓库根为工作区，读 `AGENTS.md` → 本文件 |
| Claude Code | ⚠️ 不读 `AGENTS.md`；如需支持须另建 `CLAUDE.md` |

## 读取顺序

1. 仓库根 `AGENTS.md`（定位 + 铁律 + 四文档路由表）
2. `src/docs/NewToys-项目状态与路线图.md`「一、当前基线」
3. `references/collab-method.md`（通用纪律）→ `references/newtoys-spec.md`（项目约束 + 任务型→规范章节）
4. 按「任务型→规范章节」读 `src/docs/NewToys-工程规范.md` 对应节
5. 术语 / 流程有疑问 → `src/docs/NewToys-AI协作手册.md`【术语对照】【研发流程】
6. 交付前 `pwsh skill/scripts/check.ps1` + `pwsh skill/scripts/check-axaml-props.ps1` + `pwsh skill/scripts/check-techstack.ps1`

## 目录

- `references/collab-method.md` —— 通用协作方法论（跨项目母本；**§七 回复格式（编号段落）与会话成本管控 · 长会话提醒** —— 每次回复前对照）
- `references/newtoys-spec.md` —— NewToys 特定约束（项目事实 / 密度基线 / 高频坑 / **31 个主题键** / 任务型→章节 / 四文档路由）
- `scripts/check.ps1` —— 交付前静态检查 / Lint（axaml 合法性、`.axaml`↔`.cs` 配对、主题键白名单）
- `scripts/check-axaml-props.ps1` —— XAML 属性名核对（每个属性名到 Avalonia ref 程序集搜存在性；专治 Avalonia ≠ WPF 的名字差异，如 `SelectedValuePath` / `StrokeLineJoin`）
- `../src/docs/backlog/` —— **任务卡**（NT-xxx）：验收标准 / 技术要点 / 依赖 / 范围

🔴 权威源 = `src/docs/` 四份 + `src/` 源码，冲突以源码为准；**研发流程（规划期 / 实施期）与术语对照（Glossary）** 唯一定义处 = `src/docs/NewToys-AI协作手册.md`【研发流程】+【术语对照】节。
