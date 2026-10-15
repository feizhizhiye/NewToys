# NewToys 特定约束（skill/references）

> 与 NewToys 项目绑定；通用协作纪律见 `collab-method.md`。
> v1.4 · 2026-10-14
> v1.4：术语标准化（工作项 / 任务卡 / 立项 / 就绪闸门 DoR / 完成定义 DoD / 技术债 …）+ 主题键 32→34（新增 `WindowBorderBrush` / `WindowBorderInactiveBrush`）；🔴 术语唯一定义处 = 《AI协作手册》【术语对照】，本文件只引用、不另立。
> v1.3：§七 新增「Avalonia ≠ WPF 的 XAML 属性名」坑 + 指向 `scripts/check-axaml-props.ps1`。
> v1.2：铁律 1 加例外——`scripts/` 构建脚本经用户明确授权可改（2026-10-13，Settings 物理必备改造，NT-023）。
> 🔴 **本文件不定义工作流**：`研发流程（规划期 / 实施期）` 唯一定义处 = 《AI协作手册》（《AI协作手册》）；本文件只做约束 + 路由。

---

## 一、项目事实（锁死，勿擅改）

- 程序集：`NewToys.Shared/Core`、`NewToys`(=Shell→NewToys.exe)、`Module.<名>`；命名空间一律 `NewToys.*`
- 分层单向：`Shared ← Core ← Shell ← Module.*`；模块禁引 Shell、禁互引；`Shared/Core` 不引 Avalonia
- 栈：.NET 10 / Avalonia 12.1.4 / CommunityToolkit.Mvvm 8.4.2 / MEDI 9.0.0 / Microsoft.Data.Sqlite 9.0.0；版本只在 `src/Directory.Packages.props`
- 编译绑定关闭 → XAML 不写 `x:DataType`；禁第三方 UI 库 / 图标字体
- 数据落 `%LocalAppData%\NewToys\{config,data,logs}`，`NEWTOYS_HOME` 可整根切换；路径一律 `Path.Combine`
- SQLite 唯一入口 `Core/Services/SqliteDatabase.cs`：库 = `<应用根>\data\newtoys.db`（data 非 config）；失败不抛（IsAvailable/LastError）
- 版本号编译时生成 `1.<yyyyMMdd>.<HH>.<m0>`，禁手写

---

## 二、铁律（项目特定）

1. **只改 `src/`**（例外：`scripts/` 构建脚本经用户明确授权可改——2026-10-13 授权，Settings 物理必备改造）；不读写 Downloads；不碰项目目录外的任何位置。
2. **不代跑编译**：构建由用户本地 `pwsh -File .\scripts\build.ps1`；AI 只静态自检，⛔ 不谎称通过。

---

## 三、四份权威文档（src/docs/）路由

| 文档 | 角色（**标准角色**） | 何时读 |
|---|---|---|
| NewToys-需求规格.md | WHAT：唯一权威规格、验收基线（**需求规格** PRD+SRS） | 涉及功能/框架/边界，至少读相关章节 |
| NewToys-工程规范.md | HOW：实现细节、类名/像素/API/易错点 + 技术债台账（**工程规范**） | 编码前按「任务型→章节」读 |
| NewToys-项目状态与路线图.md | STATUS + **Backlog 工作项索引**（任务卡见 `src/docs/backlog/NT-xxx.md`）+ 已完成（**项目状态与路线图**） | 会话开始读「一、当前基线」；实施期逐条勾工作项 |
| NewToys-AI协作手册.md | 流程 + 术语：**研发流程（规划期 / 实施期）**（🔴 **唯一定义处**，含状态机、验收清理、Glossary）+ 静态自检（**AI 协作手册**） | 流程 / 术语权威，必读其【研发流程】与【术语对照】节 |
| src/docs/backlog/NT-xxx.md | **任务卡**：验收标准 / 技术要点 / 依赖 / 范围（实施期工作稿，随 `src/` 更新） | 立项 / 展开 / 验收归档时 |

---

## 四、任务型 → 规范章节

| 任务 | 读《工程规范》 |
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

---

## 五、密度基线（v3.12，改样式先对这张表）

| 项 | 值 |
|---|---|
| 表格行高 | 28 |
| 表头 | 32 |
| 单元格内边距 | 8 |
| 卡片内边距 | 12 |
| 工具条 Padding | `12,6` |
| 筛选条 Padding | `12,4` |
| primary 按钮 | `12,6` |
| ghost 按钮 | `10,5` |
| 树行 | 30 |
| 页面根 Margin | `12` |
| 右详情卡宽 | 340（⛔ 别动） |

列表页约定：左键排序、右键列显隐；详情开关在工具条末位（`Button.detail-toggle`）；⛔ `Columns` 不加 `Detail` 占位列。

---

## 六、高频硬约束

- `.axaml` 改动必同步 `.axaml.cs`；编译绑定关闭 → 只写 `{Binding}`，⛔ 不写 `x:DataType`、⛔ WPF 写法。
- 颜色一律 DynamicResource；主题键只用 **31 个** `*Brush` 语义键（v3.38 由 32 增至 34：`WindowBorderBrush` / `WindowBorderInactiveBrush`；NT-049 又删 `TitleBarBrush` / `TitleBarTextBrush` / `TitleBarButtonHoverBrush` ⇒ 现 31；⛔ 除此之外不新增、不用旧键 `AppBg/AppBackground/TextPrimary`）；code-behind ⛔ 写死 `#RRGGBB`。
- `App.ApplyTheme` 幂等，⛔ 禁 `MergedDictionaries.Clear()`。
- 菜单：`icon` 传几何资源键（24 网格描边型，`<Path Stroke=… Stretch="Uniform"/>`），不是 emoji；Key = 小写连字符；viewName = View 类名。
- 跨页投递数据在 `OnAttachedToVisualTree` 取（⛔ 不是 `OnDataContextChanged`；必须 `protected override`，public 报 CS0507）。
- 设置类 `config\*.json` 模块只读，唯一写入点 = 设置页；功能数据由功能页读写。
- 摸底一律 grep/read **定向检索**：⛔ 不整读文档、⛔ 不读 `artifacts/` `bin/` `obj/`（省 token、提速）。

---

## 七、分析器 / API 坑（写新代码先对照）

🔴 **本节已收敛，⛔ 不在此重复维护**：Avalonia / 分析器 / API 层面的**技术栈硬伤清单**（含「只有编译器能抓」的那批：CA1416 / CS0507 / CS0272 / CS0759 / CS8087 / MVVMTK0034 / AVLN2000 / NETSDK1022 等）+ 静态检测规则 **G-01~G-11**，唯一源 = **`skill/references/tech-guard.md`**（另含「只有跑起来才能抓」的窗口装饰 / 贴靠 / SQLite 清单）。

- 配套脚本 `skill/scripts/check-techstack.ps1`（`-All` 看 WARN 明细；规则 ID 与 tech-guard.md 一一对应）。
- 交付前三条自检一起跑：`pwsh skill/scripts/check.ps1` + `check-axaml-props.ps1` + `check-techstack.ps1`。
- ⛔ 升级 Avalonia / .NET 后必须复核 tech-guard.md（标「版本相关」的条目会失效甚至反转）。
- 编译器仍是唯一可信门禁：用户本地 `pwsh -File .\scripts\build.ps1`。

**列表页约定（非技术栈硬伤，留在本节）**

- 给列表插首列 = `Columns` 加 `Pick("34")` + 表头/行模板**所有 `Grid.Column` 索引 +1**。

---

## 八、触发词

NewToys、src/、视频工具、PcTools、WebTools、需求规格、工程规范、Backlog、任务卡、立项、build.ps1
