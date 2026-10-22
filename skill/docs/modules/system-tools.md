# 模块 · 系统工具（System Tools）

> **程序集**：`src/Module.SystemTools`（`NewToys.SystemTools`，产物 `Module.SystemTools.dll`）
> **模块键**：`system` ｜ **一级菜单**：Order **25** · 图标 `IconSystem`（StreamGeometry，NT-091 加进 `App.axaml`）
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 已实施（NT-091 建模块 · NT-096 收编软件包）｜⏳ 待用户本地 `accept.ps1` 验收
> **最后校对**：2026-10-22

---

## 1. 职责与边界

- **定位**（v3.87 用户定调）：**当前操作系统的常用设置与快速命令** —— 系统设置入口（命令库）/ 快捷命令 / 软件包（按操作系统装软件）/ 收藏聚合。
- **归属判据**（需求 §2.3.3，机械可执行）：执行完**只产生副作用、不产出数据** ⇒ 本模块；产出**可解析数据**（能进集合 / 信息面板 / 命令链）⇒ `script` 脚本中心。
- ⛔ **不收应用级设置页的功能**（全局通用 / 启动与外观 / 路径与配置 / 网络与 DNS / 数据管理仍归框架层 `src/Shell`，需求 §4.9）。
- ⛔ 不做：采集器 / 间隔刷新 / 输出解析器 / 集合 / 命令链编排（属 `script`）。
- ⛔ 不引 Shell、⛔ 不引其它模块（模块禁互引）。执行一律走 Core 的 `ScriptRunner`（NT-088 上提）。
- 页 Key 沿用从 script 迁来的 `script-commands` / `script-favorites` / `script-packages`（**⛔ 不改 Key**：落 `columnlayout.json`，改了用户列偏好失效）。

## 2. 当前功能（4 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View / VM | 说明 |
|---|---|---|---|---|
| `script-commands` | 命令库 | 1 | `SystemCommandLibraryView` / `SystemCommandListViewModel` | 系统设置面板 / 控制面板 / 管理工具的命令行清单，点一下即执行；搜索 / 排序 / 增改删 / ▶ 运行 / 收藏 / 复制 / 导入导出 json |
| `system-quick` | 快捷命令 | 2 | `QuickCommandView` / `QuickCommandViewModel` | 十项常见开关（电源计划 / 主题明暗 / 休眠 / 网卡启禁 / 磁盘清理 / UAC / 开发者模式 / 远程桌面 / 代理开关 / 时间区域）；要管理员权限的**只给命令 + 📋 复制**，⛔ 不代跑 |
| `script-favorites` | 收藏夹 | 3 | `FavoritesView` / `FavoritesViewModel` | 按钮墙：**本模块命令条目**里 `IsFavorite` 的那些，点一下执行 |
| `script-packages` | 软件包 | 4 | `PackageView` / `PackageViewModel`（NT-096 自脚本中心迁入） | winget GUI：搜索 / 安装 / 升级 / 卸载 / 锁定 / 详情 / 导入导出已装清单；Linux 走 dpkg 只读适配器 |
| `settings-system` | 系统工具设置 | 100 | `SettingsSystemView` / `SettingsSystemViewModel` | 唯一写入点：命令超时 + 数据文件位置（🔴 2026-10-22 返工：**不再有「软件源」节**） |

## 2.5 操作逻辑（交互契约）

> 每页一张表：**操作 → 触发 → 结果 / 状态变化 → 约束**。跨模块共用语义见 `../ui.md` §10。

### 命令库 `script-commands`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| ▶ 运行 | 行右键 / 工具条 ▶ | 走 Core `ScriptRunner` 静默跑 + 捕获输出；`NeedAdmin` 的弹 UAC | 超时用设置页 `CommandTimeoutSeconds`（5~300，默认 30） |
| 增 / 改 / 删 | 工具条 ＋ / 详情卡 / 行右键 | 直接落 `config\system\commands.json`（无保存按钮） | 删除**二次确认**（`DialogHooks`） |
| 收藏 | 行右键 ★ | 置 `IsFavorite` ⇒ 收藏夹页出现 | 只影响本模块条目，⛔ 与 Core `IFavoritesService` 无关 |
| 导入 / 导出 | 工具条 📤 / 📥 | json 清单；按 `Id` 合并（已有更新、没有的加入） | 导入前先判 `..` / 绝对路径 |
| 搜索 / 排序 / 列显隐 | 关键字框 / 表头 / 表头右键 | 纯内存过滤与排序；列偏好落 `columnlayout.json` | 页 Key 仍是 `script-commands` |

### 快捷命令 `system-quick`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 复制命令 | 卡片 📋 | 复制到剪贴板 | 十项都只给命令，⛔ App 不执行（无提权机制） |
| 打开设置页 | 卡片 ↗ | `Process.Start("ms-settings:…")` | 打不开 ⇒ 状态栏红字，⛔ 不抛 |

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
| 批量安装 / 升级 / 卸载 | 勾选 + 对应按钮 | 逐条执行，**单个失败不影响其余**；卸载不可撤销 | 只在有勾选时可用；卸载**只确认一次** |
| 全部升级 | ⬆⬆ 全部升级 | 所有可升级包升到最新 | 已锁定跳过；执行前确认一次 |
| 锁定 / 解除 | 行右键 🔒 | Pin / Unpin（暂停更新） | — |
| 详情 / 主页 / 在线索引 | 行右键 🔎 / 🌐 / 🔗 | 读 `winget show` / 开主页 / 开 winget.run | 详情联网失败 ⇒ 状态栏提示，⛔ 不抛 |
| 导出 / 导入已装清单 | 工具条 📤 / 📥 | json（`PackageExportItem`：Id / Name / Version） | 默认目录 = 当前配置档 `data\` |
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
- 迁移（脚本侧 `role=command` 条目 → 本模块）**未自动做**：脚本侧条目原样保留（⛔ 未删 ⇒ 可逆）；
  要迁就用「脚本库 📤 导出 → 命令库 📥 导入」。

## 3. 实现逻辑

- 依赖：Core `ScriptRunner`（NT-088）、`ConfigStoreService`、`IOptionsStore`、`DialogHooks`；UI 控件库（骨架 / 列 / 详情卡 / 底栏）。
- 数据两处：`config\system.json`（设置，`ISystemToolsSettingsService` 唯一写入点）+ `config\system\commands.json`（命令条目，`ISystemCommandStore`）。
  软件包偏好仍落 `config\package.json`（`IPackageSettingsService`，NT-074 时的路径，⛔ 不改 —— 老用户配置不丢）。
- ⛔ 不开 DB：本模块没有观测数据（需求 §6.7）。
- 包后端按平台二选一（NT-081 口径）：Windows = `PackageManagerService`（winget）；Linux = `AptPackageService`（dpkg/apt 只读）。页面 / VM 零改动。
- 页面构建：按 `ui.md` §6 列表页标准（工具条 / 列表 / 详情卡 / 底栏 `ListFooterBar`）；设置页用 `SettingsSectionHost`（节内容放 `UserControl.Resources` + code-behind 建节）。

## 4. 关键文件

| 文件 | 作用 |
|---|---|
| `SystemToolsModule.cs` | 菜单注册（`system` 组 Order 25）+ DI 接线 |
| `Services/ISystemCommandStore.cs` / `SystemCommandStore.cs` | 命令条目（`config\system\commands.json`，`AtomicJsonFile` 落盘） |
| `Services/ISystemToolsSettingsService.cs` / `SystemToolsSettingsService.cs` | 本模块设置（`config\system.json`） |
| `Services/QuickCommandCatalog.cs` | 十项快捷命令清单 |
| `Services/IPackageManagerService.cs` / `PackageManagerService.cs` / `AptPackageService.cs` | 包后端（winget / dpkg） |
| `Services/PackageOutputParser.cs` / `FixedWidthTable.cs` / `TsvRows.cs` | 输出解析（internal，只对测试项目开放） |
| `Services/IPackageSettingsService.cs` / `PackageSettingsService.cs` | 软件包偏好（`config\package.json`） |
| `Models/SystemCommand.cs` / `Models/PackageModels.cs` | 条目模型 |
| `Views/*` / `ViewModels/*` | 4 页 + 设置页 |

## 5. 坑点（🔴 已知）

1. **页 Key 前缀误导**：4 页里 3 页 Key 是 `script-*`（刻意保留）；文档 / 注释⛔ 不要据此认为它们属 `script` 模块。
2. **执行需要管理员的操作**（启禁网卡 / `netsh` / `rasdial` / 改源）：应用**无提权机制** ⇒ 一律降级为「生成命令 + 📋 复制」供用户自跑，⛔ 不假装调用成功。
3. **Avalonia 硬规**：每个 `UserControl` 必须显式 `public Xxx(){ InitializeComponent(); }`（漏了整块 XAML 静默不加载）；跨程序集 `xmlns` 用 `using:`；`.cs` 里 `using Avalonia.Controls;` 别漏。
4. **设置页节内容**必须放 `<UserControl.Resources>` 的 `x:Key` 面板 + code-behind `Add(...)`；⛔ 在 XAML 里写 `<ui:SettingsSection>`（会不被预编译）。
5. **`PackageView` 底栏显隐**：除 `HasFooterContent` 外还要 code-behind 量 `ScrollViewer` 溢出回填 `SetContentOverflow`；右键菜单（列表 / 表头 / 表头 ☑）挂在弹出树 ⇒ `OnDataContextChanged` 里手工同步 DataContext。
6. **软件包集合已断链**：页内「集合」入口删除后，`ScriptCollectionKind.Package` 的成员只能从集合页维护；`PageKey` 映射仍指向 `script-packages`（Key 未变 ⇒ 跳转仍通）。
7. **测试引用**：`FixedWidthTable` / `TsvRows` / `PackageOutputParser` 是 internal，靠本程序集的 `InternalsVisibleTo("NewToys.Tests")`；⛔ 改回 public。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-091 | 本模块建档与四页落地 | ✅ 已验收（2026-10-22 编译通过） |
| NT-096 | 软件包页迁入本模块（定位收紧）；「软件源」设置节**同日返工删除** | ✅ 已实施（⏳ 待 build 验收） |
