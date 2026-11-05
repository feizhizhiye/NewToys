# 模块 · 系统工具（System Tools）

> **程序集**：`src/Module.SystemTools`（`NewToys.SystemTools`，产物 `Module.SystemTools.dll`）
> **模块键**：`system-tools`（🔴 组键，非页 Key；⛔ 不是 `system` —— 那是 Shell 保留的宿主分组）｜ **一级菜单**：Order **25** · 图标 `IconSystem`（StreamGeometry，NT-091 加进 `App.axaml`）
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 已实施（NT-091 建模块 · NT-096 收编软件包 · **NT-097 页面重构**）｜⏳ 待用户本地 `accept.py` 验收
> **最后校对**：2026-10-23

---

## 1. 职责与边界

- **定位**（v3.87 用户定调 / v3.90 收紧）：**当前操作系统的常用设置与快速命令** —— 收藏夹（模块下第一个页面）/ 命令库（对标脚本库布局 + 随包预设）/ 软件包（按操作系统装软件）。判据 = 执行完**只产生副作用、不产出数据**（需求 §2.3.3）；产出可解析数据的条目一律归 `script` 脚本管理器。
- ⛔ **不收应用级设置页的功能**（全局通用 / 启动与外观 / 路径与配置 / 网络与 DNS / 数据管理仍归框架层 `src/Shell`，需求 §4.9）。
- ⛔ 不做：采集器 / 间隔刷新 / 输出解析器 / 集合 / 命令链编排（属 `script`）。
- ⛔ 不引 Shell、⛔ 不引其它模块（模块禁互引）。执行两条路：普通项走 Core 的 `ScriptRunner`（NT-088 上提，隐藏+捕获）；**提权项**走模块内 `ISystemCommandLauncher`（可见窗口 + `Verb=runas` 弹 UAC，与 ScriptManager 的 `QuickActionService.StartVisible` 同范式；Core `IProcessRunner` 契约刻意不含提权）。
- 页 Key 沿用从 script 迁来的 `script-commands` / `script-favorites` / `script-packages`（**⛔ 不改 Key**：落 `columnlayout.json`，改了用户列偏好失效）。
- 🔴 **`system-quick`「快捷命令」页已删**（NT-097 / v3.90）：十项快捷开关并入**命令库预设**（「快捷设置」类），App 不再单独维护一页按钮墙。

## 2. 当前功能（3 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View / VM | 说明 |
|---|---|---|---|---|
| `script-favorites` | 收藏夹 | **1**（模块下第一个页面） | `FavoritesView` / `FavoritesViewModel` | 按钮墙：**本模块命令条目**里 `IsFavorite` 的那些；左键点击执行，右键「▶ 运行 / ✕ 移出收藏夹」（与首页「常用功能」按钮同一套操作逻辑） |
| `script-commands` | 命令库 | 2 | `SystemCommandLibraryView` / `SystemCommandLibraryViewModel` | **对标脚本库**（NT-097 重做）：Windows 常用命令 / 控制面板 / 管理工具 / 快捷设置 / winget 的命令行清单；列显隐 / 表头排序 / 筛选 / 行内 ▶ 运行 / 增改删 / ★ 收藏 / 📋 复制 / 导入导出 json；**首次运行随包预设** |
| `script-packages` | 软件包 | 3 | `PackageView` / `PackageViewModel`（NT-096 自脚本中心迁入） | winget GUI：搜索 / 安装 / 升级 / 卸载 / 锁定 / 详情 / 导入导出已装清单；Linux 走 dpkg 只读适配器 |
| `settings-system` | 系统工具设置 | 100 | `SettingsSystemView` / `SettingsSystemViewModel` | 唯一写入点：命令超时 + 数据文件位置（🔴 2026-10-22 返工：**不再有「软件源」节**） |

## 2.5 操作逻辑（交互契约）

> **列表本体**：`<v:TableViewHost>`（原生表格）。点表头排序、拖列边界调列宽（会话内有效）；**表头右键只管列宽与列显隐，行右键只管功能；筛选统一走顶区 `FilterBar` 筛选条**；软件包页行首 `☑` 表头是三态（全选 / 取消 / 反选在行右键顶部）。详见 `../ui.md` §6.3c。

> 每页一张表：**操作 → 触发 → 结果 / 状态变化 → 约束**。跨模块共用语义见 `../ui.md` §10。

### 收藏夹 `script-favorites`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 执行 | 左键点按钮 | 普通项走 Core `ScriptRunner`；`NeedAdmin` 项弹 UAC 提权执行（优先 Windows 终端） | 执行中整排按钮禁用（`IsNotBusy`）；结果落状态条 |
| ▶ 运行 | 右键菜单 | 等同左键点按钮 | 菜单与首页「常用功能」同结构：主动作 + 分隔线 + 移除项 |
| ✕ 移出收藏夹 | 右键菜单 | 置 `IsFavorite = false` 并保存 ⇒ 本页即时移除 | ⛔ 无行内 ✕ 按钮（避免与主按钮误点）；右键菜单在 code-behind 现构造（弹出树取不到页面 VM） |

### 命令库 `script-commands`（NT-097 对标脚本库重做）

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| ▶ 运行 | 行内按钮 / 行右键 / 工具条 | **分两类**：① **起窗口类**（`start …` / `*.msc` / `*.cpl` / `ms-settings:` / `explorer` / `taskmgr` / `control` / `regedit` / `msconfig`）⇒ `ShellExecute` 直接打开，**不等待窗口关闭**、不占用 `IsBusy`；② 其余命令 ⇒ Core `ScriptRunner` 静默跑 + 捕获输出。**`NeedAdmin` 项弹 UAC 提权执行**（**优先 Windows 终端** `wt.exe <命令>`，没装 WT 才回退系统终端 conhost；跑完不自动关；UAC 取消则不执行） | 超时用设置页 `CommandTimeoutSeconds`（5~300，默认 30），只对 ② 生效；提权路忽略「隐藏终端窗口」开关（必须可见） |
| 增 / 改 / 删 | 工具条 ＋ / 详情卡 / 行右键 | 直接落 `config\system\commands.json`（无保存按钮） | 删除**二次确认**（`DialogHooks`） |
| 收藏 | 工具条 ★ / 行右键 | 置 `IsFavorite` ⇒ 收藏夹页出现 | 只影响本模块条目，⛔ 与 Core `IFavoritesService` 无关 |
| 复制命令 | 工具条 📋 / 行右键 | 复制到剪贴板 | `NeedAdmin` 的项运行时也自动复制 |
| 导入 / 导出 | 工具条 📤 / 📥 | 固定落数据文件同目录 `commands-export.json`；按 `Id` 合并（已有更新、没有的加入） | ⛔ 不弹对话框（与脚本库的 DialogHooks 版不同，见 §5） |
| 搜索 | 工具条关键字框 | 即时过滤（名称 / 功能 / 命令 / 简介） | 纯内存过滤 |
| 排序 | 点表头 | 名称 / 功能 / 分类(Shell) / 简介 / ★；再点反向 | ★ 首点即降序（收藏在前） |
| 列显隐 | 表头右键 | 勾选列；标识列（名称）不可隐藏 | 落 `columnlayout.json`，页键 `script-commands` |
| 筛选 | 顶区「筛选条」 | 快捷项 = 当前出现过的「功能」分类值 | 与基类多项筛选条件并存 |
| 详情栏 | 工具条末位详情开关 / 行右键「✏ 编辑」 | 右栏声明式详情卡（名称 / 功能 / Shell 下拉 / 简介 / 命令 / 需要管理员 / 隐藏窗口） | 开合状态落 `columnlayout.json` |
| 预设播种 | 首帧后 `InitializeDeferred` | `config\system\commands.seeded` 标记不存在 ⇒ 按 `Id` **只加不改**地播种一批预设 | 删了不复活；用户改过的不被覆盖 |

> 🔴 **快捷命令页已删**（NT-097）：原 `system-quick` 十项（电源计划 / 主题明暗 / 休眠 / 网卡启禁 / 磁盘清理 / UAC / 开发者模式 / 远程桌面 / 代理开关 / 时间区域）迁为命令库「快捷设置」类预设，并补全了原先缺 `/d` 取值的 `reg` 命令；「打开设置页」能力统一走 `start ms-settings:…`（Shell=cmd）。

### 软件包 `script-packages`（NT-096 自脚本中心平移，交互不变）

工具条：关键字框（回车 = 搜索）→ `🔍 搜索` / `📋 已安装` / `⬆ 可升级` / `🔒 已锁定` → `🔄 重新加载` / `🔃 刷新索引` → `⬇ 安装选中` / `⬆ 升级选中` / `⬆⬆ 全部升级` / `🗑 卸载选中` → `静默安装` 开关。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 三种列表 | 📋 / ⬆ / 🔒 | 都刷**同一个列表**（切一次就清空重来，⛔ 不叠加）；被锁定的包标 🔒 | 源**只影响 🔍 搜索**，不影响这三种 |
| 🔒 已锁定 | 点按钮 | `winget pin list` → 再跑一次 `winget list` 补齐名称 / 版本 / 源，只留被锁定的包 | 老行为是"把 pin 项并进上次结果" ⇒ 看不出哪些才是锁定的 |
| 分页 | 底部底栏 | 预设 **20 / 50 / 100 / 全部**（本页 override，⛔ 不动全局）；数据在内存 ⇒ 只切片不回查 | 页大小全局记忆读到不属于本页预设的值 ⇒ 构造期收成第一档；底栏显隐还需 `PackageView` 的 `SetContentOverflow` 回填 |
| 搜索 | 🔍 搜索 / 关键字回车 | **一律不传 `--source`** + 把系统里的**显式源**（如 `winget-font`）逐个补查再合并去重 | 在已安装 / 可升级列表里，关键字只是**本地过滤** |
| 源管理 | — | 本页**没有**源开关 / 源选择框，App 也**不再陈列源命令**；要改源直接 `winget source …`（需要 App 里跑就自己往命令库加一条，勾「需要管理员权限」） | 源是系统 winget 的配置（`source add/remove/reset` 全要 UAC），App 内不包 UI |
| 商店包（msstore） | 搜到的包 | Id 是 Store 的 12 位字符；**详情 / 安装都必须带 `--source`**；⛔ 不吃 `--silent`；⛔ 不进「已安装 / 可升级」列表 | 行的「源」列由 `winget search` 自带；缺了用 `StampSource` 补 |
| 刷新索引 | 🔃 刷新索引 | 拉各源元数据（≈ apt update） | 「可升级」的结果依赖它 |
| 重新加载 | 🔄 重新加载 | 按**当前所在列表**重跑 | — |
| 批量安装 / 升级 / 卸载 | **选中行**（Ctrl / Shift 多选）+ 对应按钮 | 逐条执行，**单个失败不影响其余**；卸载不可撤销 | 行首**无勾选列**（作用域 = 选中行）；只在有选中行时可用；卸载**只确认一次**；右键「☑ 全选当前页 / ☐ 取消选择 / ⇄ 反选当前页」 |
| 全部升级 | ⬆⬆ 全部升级 | 所有可升级包升到最新 | 已锁定跳过；执行前确认一次 |
| 锁定 / 解除 | 行右键 🔒 | Pin / Unpin（暂停更新） | — |
| 详情 / 主页 / 在线索引 | 行右键 🔎 / 🌐 / 🔗 | 读 `winget show` / 开主页 / 开 winget.run | 详情联网失败 ⇒ 状态栏提示，⛔ 不抛 |
| 导出 / 导入已装清单 | 工具条 📤 / 📥 | json（`PackageExportItem`：Id / Name / Version） | 默认目录 = 当前配置档 `store\`（本模块无自有子目录） |
| 🔴 已删除（NT-096） | — | 工具条 `集合 ▾`、行右键 `📥 加入集合` **都不再存在** | 原逻辑写的是脚本库 `script.json`（跨模块依赖，模块禁互引）；已存「软件包」类集合仍可在集合页看到并发送回本页 |

### 系统工具设置 `settings-system`（2 节）

| 节 | 操作 | 结果 | 约束 |
|---|---|---|---|
| 执行 | 改「单条命令的超时」+ 💾 | 落 `config\system.json` | 5~300 整数，非法 ⇒ 状态栏红字 + ⛔ 不写盘 |
| 数据文件 | 📂 打开数据目录 | 打开 `config\system\` | 只读展示配置 / 清单路径 |

> 🔴 **「软件源（winget）」节已删**（2026-10-22 用户返工：「系统工具模块下有软件设置页的节，没必要」）。原节 = 6 条只读命令 + `＋ 加入命令库（winget 源）`；删后源操作回到「一条 `winget source` 命令」，App ⛔ 不再代陈列、⛔ 不再代写命令库。

- 收藏夹 = **本模块命令条目的收藏**（`commands.json` 的 `IsFavorite`），本模块自管；
  🔴 与 Core `IFavoritesService`（首页卡片的「菜单功能 key」收藏）**是两套**，⛔ 不混用。
  ⇒ 已知回退：脚本库条目的收藏不再出现在收藏夹页（用户 2026-10-22 接受）。
  🔴 需要管理员的收藏项点按钮 = 弹 UAC 提权运行（NT-097 补充，2026-10-23 用户要求）；想手动跑可用命令库的「📋 复制命令」。
- 迁移（脚本侧 `role=command` 条目 → 本模块）**未自动做**：脚本侧条目原样保留（⛔ 未删 ⇒ 可逆）；
  要迁就用「脚本库 📤 导出 → 命令库 📥 导入」。
- 🔴 **命令库预设播种（NT-097）**：`SystemCommandSeeder` 在 `InitializeDeferred` 里跑；标记
  `config\system\commands.seeded`（内容 = 程序集版本）存在 ⇒ 整体跳过；按 `Id` 只加不改。
  预设分四类 + winget：**系统设置**（`ms-settings:`）/ **控制面板**（`.cpl`）/ **管理工具**（`.msc` 与系统程序）/
  **快捷设置** / **winget**（`source list` / `source reset` / `list` / `upgrade` / `upgrade --all` / `pin list` / `export` / `import`）。

## 3. 实现逻辑

- 依赖：Core `ScriptRunner`（NT-088）、`ConfigStoreService`、`IOptionsStore`、`DialogHooks`、`IColumnLayoutService`（列显隐 / 详情开合落盘）；UI 控件库（骨架 / 列 / 详情卡 / 底栏）。
- 数据两处：`config\system.json`（设置，`ISystemToolsSettingsService` 唯一写入点）+ `config\system\commands.json`（命令条目，`ISystemCommandStore`）。
  软件包偏好仍落 `config\package.json`（`IPackageSettingsService`，NT-074 时的路径，⛔ 不改 —— 老用户配置不丢）。
- ⛔ 不开 DB：本模块没有观测数据（需求 §6.7）。
- 包后端按平台二选一（NT-081 口径）：Windows = `PackageManagerService`（winget）；Linux = `AptPackageService`（dpkg/apt 只读）。页面 / VM 零改动。
- 页面构建：命令库按 `ui.md` §6 列表页标准，VM 继承 Shared 的 `ListPageViewModelBase`（NT-097 起重写，与脚本库同一套列显隐 / 排序 / 筛选 / 工具条 / 详情开合）；设置页用 `SettingsSectionHost`（节内容放 `UserControl.Resources` + code-behind 建节）。
- 执行两路（NT-097）：普通项 = Core `ScriptRunner`（隐藏 + 捕获）；`NeedAdmin` 项 = `ISystemCommandLauncher`（可见窗口 + `runas`，模块内自起进程 —— 这是 `IProcessRunner` 契约刻意不含提权的部分，与 `QuickActionService.StartVisible` 同范式）。🔴 **优先 Windows Terminal**（`wt.exe <命令>`，默认配置文件标签页，locale 无关），没装 WT 回退 conhost；Shell 口径与捕获路一致：**auto / pwsh = pwsh 7**（`InterpreterResolver.TryResolvePwsh`：配置路径 → `Program Files\PowerShell\7` → PATH，没有才 5.1）、cmd = `cmd /k`。
- 预设播种（NT-097）：`InitializeDeferred` → `SystemCommandSeeder.Seed`（`.seeded` 标记 + 按 Id 只加不改）。

## 4. 关键文件

| 文件 | 作用 |
|---|---|
| `SystemToolsModule.cs` | 菜单注册（组键 **`system-tools`**，Order 25；🔴 不能用 `system`——那是 Shell 隐藏宿主组，会把应用级设置 8 页渲染进本组）+ DI 接线 + `InitializeDeferred` 预设播种 |
| `Services/ISystemCommandStore.cs` / `SystemCommandStore.cs` | 命令条目（`config\system\commands.json`，`AtomicJsonFile` 落盘；`Insert` 供播种器保留固定 Id） |
| `Services/ISystemCommandLauncher.cs` / `SystemCommandLauncher.cs` | 提权运行（NT-097 补充）：优先 Windows Terminal（`wt.exe <命令>` + `runas`），没装 WT 回退 conhost；auto/pwsh = pwsh 7（`-EncodedCommand` base64），cmd = `cmd /k` |
| `Services/SystemCommandSeeder.cs` | 随包预设（NT-097）：四类 + winget，`.seeded` 只播种一次 |
| `Services/ISystemToolsSettingsService.cs` / `SystemToolsSettingsService.cs` | 本模块设置（`config\system.json`） |
| `Services/IPackageManagerService.cs` / `PackageManagerService.cs` / `AptPackageService.cs` | 包后端（winget / dpkg） |
| `Services/PackageOutputParser.cs` / `FixedWidthTable.cs` / `TsvRows.cs` | 输出解析（internal，只对测试项目开放） |
| `Services/IPackageSettingsService.cs` / `PackageSettingsService.cs` | 软件包偏好（`config\package.json`） |
| `Models/SystemCommand.cs` / `Models/PackageModels.cs` | 条目模型 |
| `Views/*` / `ViewModels/*` | 3 页 + 设置页 |

## 5. 坑点（🔴 已知）

1. **页 Key 前缀误导**：3 页里 2 页 Key 是 `script-*`（刻意保留）；文档 / 注释⛔ 不要据此认为它们属 `script` 模块。
2. **执行需要管理员的操作**：命令库 / 收藏夹的 `NeedAdmin` 条目 ▶ 运行 = **弹 UAC 提权执行**（**优先 Windows 终端**，没装才回退系统终端；UAC 取消 ⇒ 状态栏提示、不执行）⇒ 不再降级为「只给命令」。⛔ 注意：**其它路径仍无提权机制**（如网络工具的 Hosts 写入只提示「请以管理员身份运行」）；提权路 fire-and-forget **不等待进程结束**（标签页 / 窗口由用户自己关，超时设置只管非提权路）。
3. 🔴 **预设命令一律 `Shell=cmd`**（NT-097）：`ScriptRunner` 的 `auto` 走 pwsh —— pwsh 会把 `control powercfg.cpl,,3` 的逗号当数组运算符、对含双引号的命令（`netsh interface set interface "以太网" …`）转义更敏感；`cmd /c` 对 `reg` / `netsh` / `winget` / `start` / `.cpl` / `.msc` 行为最稳。
4. **Avalonia 硬规**：每个 `UserControl` 必须显式 `public Xxx(){ InitializeComponent(); }`（漏了整块 XAML 静默不加载）；跨程序集 `xmlns` 用 `using:`；`.cs` 里 `using Avalonia.Controls;` 别漏。
4. **设置页节内容**必须放 `<UserControl.Resources>` 的 `x:Key` 面板 + code-behind `Add(...)`；⛔ 在 XAML 里写 `<ui:SettingsSection>`（会不被预编译）。
5. **`PackageView` 底栏显隐**：除 `HasFooterContent` 外还要 code-behind 量 `ScrollViewer` 溢出回填 `SetContentOverflow`；右键菜单（列表 / 表头 / 表头 ☑）挂在弹出树 ⇒ `OnDataContextChanged` 里手工同步 DataContext。
6. **软件包集合已断链**：页内「集合」入口删除后，`ScriptCollectionKind.Package` 的成员只能从集合页维护；`PageKey` 映射仍指向 `script-packages`（Key 未变 ⇒ 跳转仍通）。
7. **测试引用**：`FixedWidthTable` / `TsvRows` / `PackageOutputParser` 是 internal，靠本程序集的 `InternalsVisibleTo("NewToys.Tests")`；⛔ 改回 public。
8. 🔴🔴 **组键不能叫 `system`**（2026-10-23 踩坑）：`system` 是 Shell 的 `isHome:true` **隐藏宿主组**（`ModuleLoader.HostGroupKey` / `AppSettingsRegistration.HiddenGroupKey`），应用级设置的 8 个节锚点 + 功能清单页挂它名下。`RegisterModule` 遇同名键会**复用该组**并覆盖 `Title/Order/IsHome` + 补登暂存项 ⇒ 那 8 个设置页直接渲染进「系统工具」。本模块组键 = **`system-tools`**（⛔ 页 Key 不是组键，页 Key 一律未动）。
9. 🔴 **命令库点「▶ 运行」后所有行一起灰掉 = 把「打开窗口」当成了「跑命令」**：`cmd /c diskmgmt.msc` 里 cmd 会**一直等到窗口关闭**（`start ms-settings:` 则是孙进程占住 stdout/stderr 管道等不到 EOF），捕获式执行卡在 `WaitForExit` / 读取；而「▶ 运行」绑的是同一个 `RunCommand`（`AsyncRelayCommand` 默认不并发）⇒ 窗口开着期间全表禁用，超时后还会 `Kill(entireProcessTree)` 把用户刚开的窗口杀掉。**修法 = 起窗口类命令改走 `SystemCommandLauncher.TryStartWindowCommand`（`ShellExecute` 直接打开、不等待、不占 `IsBusy`）**；`ProcessRunner.OutputDrainGrace`（退出后最多再读 500ms，见 `framework.md` §6.7）只是兜住采集路里同类的管道占用。⛔ 不要把整个「运行」改成 fire-and-forget —— ② 类命令（winget / reg / netsh）仍需退出码与失败提示。
10. **导入导出不走对话框**（NT-097 起仍是固定路径）：命令库 📤/📥 固定读写数据文件同目录的 `commands-export.json`；⛔ 别照抄脚本库的 `DialogHooks` 版（那套不在本卡范围）。
11. 🔴🔴 **View 与 VM 必须同名对**（`XxxView` ↔ `XxxViewModel`，2026-10-23 踩坑）：Shell 的 `ResolveViewModel` 按约定从 View 名反查 VM 类名，**对不上 ⇒ `view.DataContext = null` ⇒ 整页绑定静默失效**——症状 = 页面只剩静态文本（如只剩表头「操作」二字）、工具条 / 列表 / 空态全无、**无报错无日志**。NT-091 平移命令库时 VM 名没跟着 View 改（`SystemCommandListViewModel` vs `SystemCommandLibraryView`）⇒ 该页自平移起就不渲染，NT-097 改名 `SystemCommandLibraryViewModel` 修复。新增 / 改名页面后必须核对这一对。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-091 | 本模块建档与四页落地 | ✅ 已验收（2026-10-22 编译通过） |
| NT-096 | 软件包页迁入本模块（定位收紧）；「软件源」设置节**同日返工删除** | ✅ 已实施（⏳ 待 build 验收） |
| NT-097 | 页面重构：删快捷命令页 / 命令库对标脚本库 / 随包预设 / 收藏夹置首 | ✅ 已实施（⏳ 待 build 验收） |

## 附录 · 代码 WHY 蒸馏（方案1 · anchors）

<!-- WHY-DISTILL:modules-system-tools:start -->

> 由 `skill/scripts/comment-slim.py` 生成（方案1 · 白名单与锚点口径见 `tech-guard.md` §3.1）。
> 代码里只留 1 行契约 + `WHY: <ID>`；此处是 ID 对应的 WHY 正文。`lint.py --only comments` 双向对账。

<!-- ANCHOR:ST-001 -->
- **ST-001** · `src/Module.SystemTools/Models/PackageModels.cs`
  - 为什么共用：列表页的表头排序、右键菜单、右侧详情三处都只认它，各写一份必然漂移
  - 「🔎 读取详情」回填字段后必须通知 UI 才会补充显示（改了不刷新 = 详情不补充）。

<!-- ANCHOR:ST-002 -->
- **ST-002** · `src/Module.SystemTools/Models/PackageModels.cs`
  - ⚠️ 这是"设置里的数据"：它由设置页维护，但只有 config\ 下的文件
  - 才能被「数据管理」页备份 / 导入 —— 所以不要把源设置散落到模块自己的目录里
  - ⚠️ 🔴 ⛔ 不存源相关偏好（defaultSource / 源分类 / 源定义快照这类字段别再回来）。
  - 源是系统 winget 的东西，增删 / 切换一律走「命令库」里的命令条目；本文件只剩安装偏好与排序。

<!-- ANCHOR:ST-003 -->
- **ST-003** · `src/Module.SystemTools/Models/PackageModels.cs`
  - 🔴 只留「商店源」判定：商店包（msstore，Id 形如 9NBLGGH4NNS1）走 Microsoft Store 安装通道，
  - ⛔ 不吃 --silent。
  - 🔴 源的增删 / 切换已移出本应用（系统 winget 配置，全要 UAC）⇒ 分类 / 互斥 / 定义快照 / 镜像地址

<!-- ANCHOR:ST-004 -->
- **ST-004** · `src/Module.SystemTools/Models/SystemCommand.cs`
  - 🔴 与 ScriptManager 的条目模型是两套（模块禁互引 ⇒ 各持一份）：本模块条目落
  - config\system\commands.json，⛔ 不共用 script 的存储，也不带集合 / 采集器 / 保留策略那些字段。

<!-- ANCHOR:ST-005 -->
- **ST-005** · `src/Module.SystemTools/Services/AptPackageService.cs`
  - （写操作⛔ 不脚本化：提权 sudo vs UAC、交互确认、失败原因都会退化），留给命令库条目。

<!-- ANCHOR:ST-006 -->
- **ST-006** · `src/Module.SystemTools/Services/FixedWidthTable.cs`
  - 机读输出（TSV / JSON）⛔ 不走这里，见 `TsvRows`。
  - 🔴 按显示宽度切片（不是字符下标）：数据行里有中文 ⇒ 汉字占 2 列，

<!-- ANCHOR:ST-008 -->
- **ST-008** · `src/Module.SystemTools/Services/FixedWidthTable.cs`
  - ⚠️ 会自动换行折叠长名称的后端（winget）⛔ 不用本方法 —— 那种续行合并是

<!-- ANCHOR:ST-009 -->
- **ST-009** · `src/Module.SystemTools/Services/IPackageManagerService.cs`
  - 非 Windows 平台 `IsSupported` 为 false，各方法返回失败消息而不抛异常。

<!-- ANCHOR:ST-010 -->
- **ST-010** · `src/Module.SystemTools/Services/IPackageSettingsService.cs`
  - 之所以必须是 config\ 下：① 只有这里的 *.json 会被「设置 → 数据管理」的
  - 备份 / 导入覆盖；② 模块只读这里的值，不自己另存一份，避免双真相源。

<!-- ANCHOR:ST-011 -->
- **ST-011** · `src/Module.SystemTools/Services/ISystemCommandLauncher.cs`
  - 🔴 与 Core ScriptRunner 的分工：那条路 = 隐藏 + 重定向 + 捕获输出（⛔ 提权不在其契约内，
  - 见 IProcessRunner 文档「不在本契约内的两类」）；本路 = 可见窗口 + Verb=runas，

<!-- ANCHOR:ST-012 -->
- **ST-012** · `src/Module.SystemTools/Services/PackageManagerService.cs`
  - ② 一律以非交互方式调用（协议自动接受 + --disable-interactivity），否则会卡在 Y/N 提示上；
  - ③ 写操作（安装 / 卸载 / pin）不做超时静默重试，失败就把 winget 的原始错误原样抛给界面。

<!-- ANCHOR:ST-013 -->
- **ST-013** · `src/Module.SystemTools/Services/PackageManagerService.cs`
  - ⚠️ 必须允许反斜杠：MSI/ARP 安装的条目 Id 形如
  - 🔴 ：实测再修正（winget 1.29 真实 winget list 输出）：合法 Id 远不止"点分命名"——

<!-- ANCHOR:ST-014 -->
- **ST-014** · `src/Module.SystemTools/Services/PackageManagerService.cs`
  - 钩子没接上时返回 false —— 宁可什么都不做，也不能在用户没确认的情况下卸载软件。

<!-- ANCHOR:ST-015 -->
- **ST-015** · `src/Module.SystemTools/Services/PackageOutputParser.cs`
  - 本地化表头别名、续行合并、pin 兜底、show 的标签-值解析。机读 TSV 后端走 `TsvRows`。
  - ⚠️ 表头是本地化的（ 修正的认知）：中文 Windows 上 winget list 打印的是
  - ③ 分隔线（整行只有 -）；④ 表头之上的提示行（"已配置的源需要更新"之类）。

<!-- ANCHOR:ST-016 -->
- **ST-016** · `src/Module.SystemTools/Services/PackageOutputParser.cs`
  - 别名里最长的要放在前面（如 Last Updated 先于任何短词），避免被短别名截断。

<!-- ANCHOR:ST-017 -->
- **ST-017** · `src/Module.SystemTools/Services/PackageOutputParser.cs`
  - 这些行必须显式「终结上一个字段」，否则会被当成上一条多行值的续行 ——

<!-- ANCHOR:ST-020 -->
- **ST-020** · `src/Module.SystemTools/Services/PackageOutputParser.cs`
  - 🔴 老实现 `ParsePackageIds` 是"扫所有带点号的词" ⇒ 版本号（24.08、1.2.x）
  - 现在走 `ParseTable`（规范列名 Id / Version），只在表头认不出时才兜底，
  - 且兜底里按形状排除纯版本号。

<!-- ANCHOR:ST-021 -->
- **ST-021** · `src/Module.SystemTools/Services/PackageSettingsService.cs`
  - （升级前用户的源设置不丢），之后 `Save` 一律写新文件名。

<!-- ANCHOR:ST-022 -->
- **ST-022** · `src/Module.SystemTools/Services/SystemCommandLauncher.cs`
  - 🔴 wt 用法 = 官方「向默认 shell 传命令」口径：wt.exe &lt;命令行&gt;（在默认配置文件的标签页里跑）——
  - locale 无关，⛔ 不用 -p 配置文件名（中文系统里叫「命令提示符」，写死就废）。
  - ⚠️ wt 把 ; 当标签页分隔符：命令里含分号会多开标签页（预设无分号；用户自建条目注意）。
  - 🔴 Shell 口径与捕获路（ScriptRunner）一致：auto / pwsh = pwsh 7

<!-- ANCHOR:ST-023 -->
- **ST-023** · `src/Module.SystemTools/Services/SystemCommandLauncher.cs`
  - 🔴 为什么必须单独走这条路：`cmd /c diskmgmt.msc` 里 cmd 会一直等到 mmc 关闭，

<!-- ANCHOR:ST-024 -->
- **ST-024** · `src/Module.SystemTools/Services/SystemCommandSeeder.cs`
  - 🔴 只播种一次：config\system\commands.seeded 标记存在 ⇒ 整体跳过 —— 用户删掉的预设
  - 不会复活（与 ExampleSeeder 同一口径）。按 `SystemCommand.Id` 只加不改：
  - 🔴 全部预设 Shell = "cmd"：reg / netsh / winget / start / .cpl / .msc
  - 需要管理员的项 `SystemCommand.NeedAdmin` = true ⇒ App ⛔ 不代跑，只复制命令供用户在提权终端自跑。

<!-- ANCHOR:ST-025 -->
- **ST-025** · `src/Module.SystemTools/Services/TsvRows.cs`
  - —— 命令自己吐带分隔符的机读数据，⛔ 不碰固定列宽、⛔ 不关心中文表头。

<!-- ANCHOR:ST-027 -->
- **ST-027** · `src/Module.SystemTools/SystemToolsModule.cs`
  - 🔴 组键不是 `system` —— 那是 Shell 的隐藏宿主组（应用级设置锚点 + 功能清单），同名会把
  - 🔴 依赖只有 Shared / Core / UI：⛔ 不引 Shell、⛔ 不引其它模块；执行一律走 Core 的
  - ScriptRunner，⛔ 不自己起进程跑脚本。

<!-- ANCHOR:ST-028 -->
- **ST-028** · `src/Module.SystemTools/ViewModels/FavoritesViewModel.cs`
  - 🔴 数据源换了（拍板 2）：这里展示的是本模块命令条目里 `IsFavorite` 的那些，
  - 落 `config\system\commands.json`。⛔ 不是 Core 的 `IFavoritesService`（那份是首页卡片的「菜单功能 key」收藏）。

<!-- ANCHOR:ST-029 -->
- **ST-029** · `src/Module.SystemTools/ViewModels/PackageViewModel.Actions.cs`
  - 🔴 不能只写 winget：它是 App Execution Alias（WindowsApps\winget.exe 是 0 字节重解析点），
  - UseShellExecute=false 的进程创建不解析别名 ⇒ 必须自己按 PATH 找出真实路径（PackageManagerService.ResolveExe

<!-- ANCHOR:ST-030 -->
- **ST-030** · `src/Module.SystemTools/ViewModels/PackageViewModel.cs`
  - 会标「（未启用）」，勾它立刻跑 winget source enable（⛔ 工具条不再有「生效」按钮）。
  - ⚠️ 刻意不订阅 `IPackageSettingsService.Changed`：那个服务是单例，

<!-- ANCHOR:ST-031 -->
- **ST-031** · `src/Module.SystemTools/ViewModels/PackageViewModel.cs`
  - 🔴 动词层：按钮文案一律用通用动词（列出 / 搜索 / 升级 / 锁定 / 卸载 …），
  - ⛔ 不出现后端名 —— 换后端时按钮文案一个字都不用改。
  - 🔴 反转：ToolTip 一律给真实 winget 命令（用户定调）—— 用户要看的是"到底跑了什么"。

<!-- ANCHOR:ST-032 -->
- **ST-032** · `src/Module.SystemTools/ViewModels/PackageViewModel.Detail.cs`
  - 🔴 用户口径：只读信息一律只读 + 可复制；已安装的包不再给「安装」（由

<!-- ANCHOR:ST-033 -->
- **ST-033** · `src/Module.SystemTools/ViewModels/PackageViewModel.Page.cs`
  - ⛔ 本页没有「仅看 Pin」勾选框（ 去掉：过滤语义说不清，改成"把 pin 项加进列表"这个真命令）。

<!-- ANCHOR:ST-034 -->
- **ST-034** · `src/Module.SystemTools/ViewModels/SettingsSystemViewModel.cs`
  - 🔴 写入一律经 `ISystemToolsSettingsService`（Core 的 IOptionsStore，落 config\system.json）；
  - 🔴 「软件源（winget）」节删除（用户 ：「系统工具模块下有软件设置页的节，没必要」）。
  - 源 = 系统 winget 自己的配置，要改直接 winget source …；本页 ⛔ 不再陈列命令清单。

<!-- ANCHOR:ST-035 -->
- **ST-035** · `src/Module.SystemTools/ViewModels/SystemCommandLibraryViewModel.cs`
  - 🔴 类名必须与 View 成对：SystemCommandLibraryView ↔ `SystemCommandLibraryViewModel`
  - 🔴 继承 `ListPageViewModelBase`（Shared）⇒ 与脚本库同一套列显隐 / 表头排序 /
  - 🔴 执行一律走 Core 的 ScriptRunner，⛔ 不自己起进程；需要管理员的项只复制命令。

<!-- WHY-DISTILL:modules-system-tools:end -->
