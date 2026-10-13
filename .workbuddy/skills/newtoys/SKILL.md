---
name: newtoys
description: NewToys 项目（.NET 10 + Avalonia 12.1.3 Windows 桌面工具管理器）的开发协作技能（指针）。实际知识与深度路由表在仓库根 `skill/` 目录，跨 AI 共享（WorkBuddy 与 Hermes/DeepSeek Harness 共用）。涉及本仓库任务——页面/列表页/设置页、Module.VideoTools/PcTools/WebTools/Settings 改动、主题样式、编译报错、文档台账——动手前先加载本技能，按其指引读仓库根 AGENTS.md 与 skill/SKILL.md。触发词：NewToys、src/、视频工具、PcTools、WebTools、开发需求、开发规范、台账、build.ps1。
agent_created: true
---

# NewToys 开发协作（指针）

实际知识库与路由表在仓库根的 `skill/` 目录（跨 AI 中立载体，WorkBuddy 与 Hermes 共用）：

1. 读 `AGENTS.md`（仓库根，定位 + 铁律 + 四文档路由表）
2. 读 `skill/SKILL.md`（执行流程 + 目录说明）
3. 按 `skill/references/newtoys-constraints.md` 的「任务型→规范章节」读 `src/docs/NewToys开发规范.md`

权威源仍是 `src/docs/`；本项目只改 `src/`（例外：`scripts/` 构建脚本经用户明确授权可改）。



## 用途

为本仓库的 AI 协作提供统一入口：项目定位、不可协商铁律、四份权威文档的路由表。权威规格在 `src/docs/`；本技能与根目录 `AGENTS.md` 同为入口摘要，**冲突时以 src/docs 与 src/ 源码为准**。

## 执行流程

1. 读仓库根目录 `AGENTS.md`（跨 AI 通用入口：定位 + 铁律 + 路由表）。
2. 读 `src/docs/NewToys-项目现状与进度.md` 的「一、当前基线」。
3. 按 `AGENTS.md` 的「任务型 → 规范章节」表，读《开发需求》《开发规范》相关章节。
4. 动手前给变更声明：① 文件清单（增/改/删逐条一句话）② 验收标准 ③ 已知风险/需用户拍板点。
5. 只改 `src/`（例外：`scripts/` 构建脚本经用户明确授权可改）；只做静态自检。⛔ 不代跑编译、不谎称「已编译通过」。
6. 交付 = 变更清单 + 前置基线 + 用户本地验证命令（`pwsh -File .\scripts\build.ps1`）；直接写工作区并用 read/ls 确认落盘；⛔ 不打 zip、不列下载链接、不做 sources md 快照。

## 铁律（与 AGENTS.md 同源，权威以 src/docs 为准）

1. **只改 `src/`**（例外：`scripts/` 构建脚本经用户明确授权可改）；不读写 Downloads；不碰项目目录外的任何位置。
2. **规划 / 执行双模式 + 变更顺序**（🔴 工作流唯一定义见《AI协作提示词》，本文件只引用）：新需求 → 先写《开发需求》→ 在《现状》三、登记待办项 + `src/docs/backlog/NT-xxx.md` 写细节 → 升 `✅ 就绪` → **再改代码** → 最后更新台账 → 用户验收后清理细节（默认删 / 留最近 3）。默认**规划模式**只写文档、⛔ 不改 `src/`；仅当用户发「执行全部就绪待办」/「执行 NT-xxx」才进执行模式；`📝 草稿` 不执行。⛔ 不先改代码后补文档。
3. **框架层（Shared/Core/Shell）冻结**，新功能只在 Module.*。停手仅 5 种：改框架层 / 删用户已确认的实现 / 需求自相矛盾 / 文档与代码冲突且影响本次 / 缺关键标识符；其余按最合理假设推进并在交付里标注假设。
4. **版本记录走 git commit**（`<type>: 简述`），⛔ 不写 md 日志；指导性文档仅过时/冲突才改，各文档独立版本号，**代码 > 文档**。
5. **评估/讨论类内容不进文档**，只在会话里谈。

## 高频硬约束（摘要，细则以规范/需求为准）

- 栈锁：.NET10 / Avalonia 12.1.3 / CT.Mvvm 8.4.2 / MEDI 9.0.0 / Microsoft.Data.Sqlite 9.0.0；分层单向 Shared←Core←Shell←Module.*，模块禁引 Shell、禁互引；命名空间一律 `NewToys.*`。
- `.axaml` 改动必同步 `.axaml.cs`；编译绑定关闭 → 只写 `{Binding}`，⛔ 不写 `x:DataType`、⛔ WPF 写法。
- 颜色一律 DynamicResource；主题键只用 32 个 `*Brush` 语义键（不新增、不用旧键）；code-behind ⛔ 写死 `#RRGGBB`。
- 数据落 `%LocalAppData%\NewToys\{config,data,logs}`，`NEWTOYS_HOME` 可整根切换；设置类 `config\*.json` 模块只读，唯一写入点 = 设置页。
- 跨页投递的数据在 `OnAttachedToVisualTree` 取（⛔ 不是 `OnDataContextChange`；必须 `protected override`，public 报 CS0507）。
- v3.12 密度基线：行高 28 / 表头 32 / 单元格内边距 8 / 卡片内边距 12 / 页面根 Margin 12 / 右详情卡宽 340。
- 列表页约定：左键排序、右键列显隐；详情开关在工具条末位（`Button.detail-toggle`）；⛔ `Columns` 不加 `Detail` 占位列。
