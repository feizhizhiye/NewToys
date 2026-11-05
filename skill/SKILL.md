---
name: newtoys
description: NewToys 项目（.NET 10 + Avalonia 12.1.5 Windows 桌面工具管理器）协作技能，跨 AI 中立载体，供 WorkBuddy 与 Hermes(DeepSeek Harness) 等可读本地文件的 AI 客户端按路由表使用。涉及任意模块（ScriptManager / WebTools / VideoTools / MediaAssets / SystemTools / BackupCenter / Viewer / Reading / 应用级设置）改动、页面/列表页/设置页、主题样式、编译报错、文档台账——动手前先读仓库根 AGENTS.md，再按 skill/docs/index.md 定位。触发词：NewToys、src/、模块文档、framework.md、ui.md、需求规格、Backlog、任务卡、立项、build.ps1、accept.py。
agent_created: true
---

# NewToys 协作技能（中立载体 · 导航）

本目录 = **跨 AI 共享知识库 + 自封装文档**（与具体客户端无关）。
🔴 **本文件只做导航**：正文（定位 / 铁律 / 路由表）唯一源 = 仓库根 `AGENTS.md`；细则唯一源 = `references/*`。⛔ 不在此重复正文——同一内容多份会漂移，且每多一份就多一份注入成本。

## 客户端接入

| 客户端 | 入口 |
|---|---|
| AGENTS.md-aware（Codex / Cursor / Devin / Jules / Amp / Gemini CLI / Windsurf / Cline / Roo） | 自动读仓库根 `AGENTS.md` ✅ |
| WorkBuddy | `.workbuddy/skills/newtoys/SKILL.md`（薄指针，自动触发）→ 本文件 |
| Hermes(DeepSeek Harness) / 自配 Agent | 以仓库根为工作区，读 `AGENTS.md` → 本文件 |
| Claude Code | ⚠️ 不读 `AGENTS.md`；如需支持须另建 `CLAUDE.md` |

## 读取顺序

1. **一次打包（首选）**：`python skill/scripts/ctx.py --task <feature|ui|interaction|fix|usage|verify|card> --module <模块>` —— 输出铁律 + 模块行 + 基线 + 该任务相关章节 + 任务卡。⛔ 不要逐份 Read 文档
2. 打包里缺的 → 按 `docs/index.md` §三 定位，用 `ctx.py --doc <文件> --section <节号>` 精读单节
3. 涉及功能 / 边界 → **`docs/NewToys-需求摘要.md`**（150 行常读基线；原文 1331 行只按它的定位表 grep）
4. `references/collab-method.md`（通用纪律）→ `references/newtoys-spec.md`（项目约束 + 任务型速记）
5. 按任务读 **`docs/framework.md`**（框架层）/ **`docs/ui.md`**（UI）/ **`docs/modules/<模块>.md`**（改哪个模块读哪个）对应节
6. 术语 / 流程有疑问 → `docs/NewToys-AI协作手册.md`【术语对照】【研发流程】
7. **交付前自检：`python skill/scripts/accept.py`**（= 11 项静态检查 + 文档一致性对账 + 改动范围；退出码 0/1）。该脚本**纯静态**，编译与运行由用户本地 `scripts/build.ps1` / `run.ps1` 执行

## 目录

- `references/collab-method.md` —— 通用协作方法论（跨项目母本；**§七 回复格式（编号段落）与会话成本管控 · 长会话提醒** —— 每次回复前对照）
- `references/newtoys-spec.md` —— NewToys 特定约束（项目事实 / 密度基线 / 高频坑 / 主题键**数量以 lint 白名单为准** / 任务型→章节 / 文档路由）
- `references/tech-guard.md` —— 技术栈硬伤唯一源（`G-01`~`G-11` + `G-14`~`G-17`，与 `lint.py --only techstack` 一一对应）
- **`scripts/accept.py` —— 🔴 静态验收入口**：串起下面 6 个静态检查 + 文档一致性 + 改动范围，输出结论与退出码（0 = 通过 / 1 有 FAIL）。🔴 **不含构建 / 测试 / 产物核对 / 冒烟** —— skill/ 下脚本只做静态检查，编译一律走用户本地 `scripts/build.ps1`。手动用：终端里不带参数直接跑会弹**交互菜单**（全部 / 只静态 / 只文档 / 文档修复 / 只范围；跑完停住等按键，R 回菜单）；单项 = `-Only all|static|docs|fix|scope`；AI 一律 `-Brief`（一行结论，不弹菜单不等待）。
- `scripts/lint.py` —— `.axaml` 合法性、`.axaml`↔`.axaml.cs` 配对、`InitializeComponent` 调用、主题键白名单
- **`scripts/ctx.py` —— 🔴 上下文打包（省 token 首选）**：`python skill/scripts/ctx.py --task ui --module viewer` 一次输出该任务需要的文档段落（铁律 + 模块行 + 相关章节 + 任务卡），**替代逐份 Read 文档**；`--doc ui.md --section 12` 精读单节；`--list` 看任务类型。
- **`scripts/docs-sync.py` —— 文档同步检查 / 生成**：`menu`（四处 AUTO 块 ⇄ 代码 `RegisterModule` / `RegisterFunction` + 模块 md 头部与页数）、`keys`（代码 ⇄ 模块 md ⇄ 说明书）、`paths`（文档路径引用 ⇄ src）、`cards`（每目录卡数 / §2.5 存在性）、`ledger`（NT 编号）。默认只读，`--only keys|menu|paths|cards|ledger`；**`--fix` 只重写 `<!-- AUTO:… -->` 块内部**（index §二 / framework §4.2 / 台账 §一文档版本 与 §2.3），块外手写内容不动。
- `scripts/lint.py --only props` —— XAML 属性名核对（每个属性名到 Avalonia ref 程序集搜存在性；专治 Avalonia ≠ WPF 的名字差异）
- `scripts/lint.py --only techstack` —— 技术栈硬伤 `G-01`~`G-14`（ERROR / WARN 分级；`-All` 看 WARN 明细）
- `scripts/lint.py --only detail-paths` —— 声明式详情卡的字符串绑定路径首段是否存在（`-All` 看全）
- `docs/` —— 🔴 **权威文档（自封装）**：
  - `index.md` 文档地图（**先读**）
  - `framework.md` 框架层工程规范 + **跨模块待办** ｜ `ui.md` UI 工程规范
  - `modules/<模块>.md` **一模块一份**（职责 / 功能 / 逻辑 / 数据 / 坑点 / 待办）
  - `NewToys-需求规格.md` / `NewToys-项目状态与路线图.md` / `NewToys-AI协作手册.md` / `NewToys-软件说明书.md`
  - `backlog/<模块>/NT-xxx.md` 任务卡（**每目录 ≤5 张已验收卡**，滑动窗口）

🔴 权威源 = `skill/docs/` + `src/` 源码，冲突以源码为准；**研发流程与术语对照**唯一定义处 = `skill/docs/NewToys-AI协作手册.md`【研发流程】+【术语对照】节。
