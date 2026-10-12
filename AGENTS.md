# AGENTS.md — NewToys 项目协作入口

> v1.0 · 2026-10-12
> 深度约束 / 坑点 / 静态自检脚本在仓库根 `skill/` 目录（`references/collab-method.md` 通用方法论 + `references/newtoys-spec.md` NewToys 专属约束 + `scripts/check.ps1`）；WorkBuddy 经 `.workbuddy/skills/newtoys/` 指针、Hermes(DeepSeek Harness) 经本文件路由读取。
> 本文件是**跨 AI 工具的通用入口**（Codex / Cursor / Devin / Jules / Amp 等自动读取 AGENTS.md）。
> 权威规格在 `src/docs/`，本文件只做「定位 + 铁律 + 路由」；**冲突时以 src/docs 与 src/ 源码为准**。
> WorkBuddy 用户：本文件内容已同步为项目级技能 `.workbuddy/skills/newtoys/`，会被自动触发，无需重复维护。

## 这个项目是什么

NewToys = 带 GUI 的 Windows 桌面工具管理器（脚本管理器定位，不做社交/推荐/埋点/云同步）。

- 技术栈（锁定）：.NET 10 / Avalonia 12.1.3 / CommunityToolkit.Mvvm 8.4.2 / MEDI 9.0.0 / Microsoft.Data.Sqlite 9.0.0
- 分层单向依赖：Shared ← Core ← Shell ← Module.*；模块禁引 Shell、禁互引；Shared/Core 不引 Avalonia
- 工作区根 = 能看到 `src/` 的那一级；**只改 `src/`**
- 包版本只在 `src/Directory.Packages.props`；禁第三方 UI 库 / 图标字体

## 铁律（不可协商）

1. **只改 `src/`**；不读写 Downloads；不碰项目目录外的任何位置。
2. **不代跑编译**。构建由用户本地执行 `pwsh -File .\scripts\build.ps1`；AI 只做静态自检，⛔ 不谎称「已编译通过」。
3. **变更顺序**：新需求 → 先写《开发需求》→ 再改代码 → 最后更新台账。⛔ 不先改代码后补文档。
4. **框架层（Shared/Core/Shell）冻结**，新功能只在 Module.*。
5. **停手仅 5 种**：改框架层 / 删用户已确认的实现 / 需求自相矛盾 / 文档与代码冲突且影响本次 / 缺关键标识符。其余按最合理假设推进，并在交付里标注假设。
6. **版本记录走 git commit**（`<type>: 简述`），⛔ 不写 md 日志。
7. **指导性文档不逐次改**：仅过时/与代码冲突才改；各文档独立版本号；**代码 > 文档**。
8. **评估/讨论类内容不进文档**，只在会话里谈。

## 四份权威文档（src/docs/）与路由

| 文档 | 角色 | 何时读 |
|---|---|---|
| NewToys开发需求.md | WHAT：唯一权威规格、验收基线 | 涉及功能/框架/边界，至少读相关章节 |
| NewToys开发规范.md | HOW：实现细节、类名/像素/API/易错点 | 编码前按下方「任务型 → 章节」表读 |
| NewToys-项目现状与进度.md | 台账：基线 + 待办 + 已完成 | 会话开始读「一、当前基线」；执行期逐条勾待办 |
| NewToys-AI协作提示词.md | 流程：两阶段工作流 + 静态自检 | 流程权威，细则以其为准 |

## 分级读档顺序（省 token）

1. 本文件 + 《项目现状与进度》「一、当前基线」
2. 《开发需求》至少相关章节：定位/边界 → 一~二章；技术框架 → 三章；UI → 四章；导航 → 五章；功能域 → 六章
3. 按任务型读《开发规范》对应章节（见下表）
4. 文档与代码冲突 → 以 `src/` 源码为准，并回写文档

## 任务型 → 规范章节

| 任务 | 读《开发规范》 |
|---|---|
| 改/加页面 | §2 §3.5 §3.6 |
| 设置类页面 | §2 §3.5 §4 |
| 列表页 | §3.6 §3.8 |
| 新增模块 | §3.4 |
| 图标/样式 | §2.1 §2.2 §2.3 |
| 主题键 | §2.2 |
| 框架层（先问用户） | 需求 §3.3 + 规范 §3.3 |
| 标签栏/树/导航 | 规范 §3.8 + 需求 §4.1~4.3 §5 |
| 热加载模块 | 规范 §3.7 |
| 接口/契约 | 规范 §3.1 §3.2 §3.8 |
| 修编译报错 | 规范 附录A |
| 纯文档改动 | — |

## 高频硬约束（摘要，细则以规范/需求为准）

- 命名空间 `NewToys.*`；View 与 VM 成对；`.axaml` 改必同步 `.axaml.cs`；编译绑定关闭 → 只写 `{Binding}`，⛔ 不写 `x:DataType`、⛔ WPF 写法。
- 颜色一律 DynamicResource；主题键只用 32 个 `*Brush` 语义键，不新增、不用旧键；code-behind ⛔ 写死 `#RRGGBB`。
- 数据落 `%LocalAppData%\NewToys\{config,data,logs}`，`NEWTOYS_HOME` 可整根切换；路径一律 `Path.Combine`；设置类 `config\*.json` 模块只读，唯一写入点 = 设置页。
- v3.12 密度基线：行高 28 / 表头 32 / 单元格内边距 8 / 卡片内边距 12 / 页面根 Margin 12 / 右详情卡宽 340。
- 列表页约定：左键排序、右键列显隐；详情开关在工具条末位（`Button.detail-toggle`）；⛔ `Columns` 不加 `Detail` 占位列。
- 跨页投递的数据在 `OnAttachedToVisualTree` 取（⛔ 不是 `OnDataContextChange`；必须 `protected override`，public 报 CS0507）。
- 易错：`[ObservableProperty]` 的 `OnXxxChanged` 只能写在定义类；`CommandParameter` 字面量是 string，别喂 `RelayCommand<T>`（T≠string 会静默失败）；插值串禁单个 `\`。
- 摸底用 grep/read 定向检索，⛔ 不整读、⛔ 不读 `artifacts/ bin/ obj/`。

## 交付要求

- 变更清单（增/改/删逐条一句话）+ 前置基线 + 用户本地验证命令。
- 直接写工作区，用 read/ls 确认落盘；⛔ 不打 zip、不列下载链接、不做 sources md 快照。
- 功能级交付 git commit（`<type>: 简述`）；纯修报错不回写台账。

## 会话启动 checklist

- [ ] 已读本文件 + 《项目现状与进度》「一、当前基线」
- [ ] 已按路由表读本次任务相关章节
- [ ] 动手前已给变更声明（文件清单 + 验收标准 + 风险/待拍板点）
- [ ] 交付前过静态自检（详见《AI协作提示词》[4][6]）
