# 模块 · 脚本管理器（Script Manager）

> **程序集**：`src/Module.ScriptManager`（`NewToys.ScriptManager`，产物 `Module.ScriptManager.dll`）
> **模块键**：`script` ｜ **一级菜单**：Order 20 · 图标 `IconPc`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：🔨 重构中（NT-068 S1~S8 分步，S7 待拍板）
> **最后校对**：2026-10-18（对照 `ScriptManagerModule.cs` / 文件清单 / NT-068 执行记录）

---

## 1. 职责与边界

- **定位**：脚本 / 命令 / 采集器的索引库 + 执行器 + 软件包管理器。不联网抓取数据（除 winget 查询）、不做云同步。
- ⛔ 不引 Shell；⛔ 不动 `ConfigStoreService` / `SqliteDatabase`（框架层冻结）—— 模块内自建子目录 + 自建连接。
- 模块改名历史：`Module.PcTools` → `Module.ScriptManager`（NT-068 S1，commit `9eea91f`）；旧菜单 Key `pc-*` **已全部改为 `script-*`**（一次性改名，用户拍板）。

## 2. 当前功能（6 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View | 说明 |
|---|---|---|---|---|
| `script-favorites` | 收藏夹 | 0 | `FavoritesView` | 已收藏的脚本与命令，点一下即运行 |
| `script-library` | 脚本库 | 1 | `ScriptLibraryView` | 系统设置项与常用脚本：运行 / 增删改查 / 收藏 / 扫描候选 / 「可采集」列 + 「仅可采集」筛选 |
| `script-commands` | 命令库 | 2 | `CommandLibraryView` | 系统设置入口（设置面板 / 管理工具），点一下即打开 |
| `script-packages` | 软件包 | 3 | `PackageView` | 搜索 / 安装 / 升级 / 卸载（winget 后端，命令模板抽象） |
| `script-collections` | 集合 | 4 | `CollectionView` | 采集器容器：主从两栏 + 搜索 / 置顶 / 最近 + 「📤 发送到信息面板」 |
| `script-info` | 信息面板 | 99 | `ScriptInfoView` | 按集合分节展示采集器输出；顶部唯一 `集合 ▾` |
| `settings-script` | 脚本管理器设置 | 100 | `SettingsScriptView` | 脚本目录（唯一写入点） |

- 页 Key 一经发布 **⛔ 不改**（`columnlayout.json` / 收藏 / 标签偏好按 Key 寻址）。

## 3. 实现逻辑

### 3.1 四层模型

```
采集器（collector） → 集合（collection） → 视图（view） → 页面（页面 = 集合选择框 + 按采集器分节）
```

- 取代旧的四份扁平 JSON 模型（`pc-scripts.json` / `pc-quick.json` / `quickactions.json` / `sysinfo.manifest.json` 均已废弃）。
- **命令即采集器**：命令条目带 `capture` + 黑名单 + 强制超时 + 解析器即可当采集器用。

### 3.2 服务清单（`Services/`）

| 文件 | 职责 |
|---|---|
| `ScriptStoreService` | `config\script\script.json` 读写，**原子写**（`.tmp` + rename），手工数据唯一入口 |
| `ScriptDatabase` | `data\script\script.db`（`pc_observations` + `pc_runs` 两表 + 索引 + 保留策略），**可重建、不进备份** |
| `CollectorService` | 采集执行（并发上限、按采集器归舱、只刷未折叠节） |
| `InfoRowFactory` | 只读呈现内核（列 schema 自动生成） |
| `ScriptRunner` | 进程执行：`UseShellExecute=false` + 重定向 + **stdout 严格 UTF-8 解码**（非法字节 ⇒ 判该脚本失败）+ 超时 / 取消 / 退出码 |
| `InterpreterResolver` | 解释器 → exe + args：配置路径 / 探安装位 / PATH 回退；Auto 按扩展名 |
| `SysInfoOutputParser` | 三种外壳的宽松解析（`{rows:[{key,status,values}]}` 等） |
| `CollectableEvaluator` | `collectable` 可采集判定（自动规则 + 用户覆盖） |
| `ScriptScanner` | 目录扫描三态（新增 / 已存在 / 忽略） |
| `BuiltinCollectorSeeder` | 内置采集脚本播种（**只加不覆盖** + `.seeded` 标记） |
| `QuickActionService` | `script.json` 适配器 + `ConfirmHandler` 复用（⛔ 不再是四份 JSON 的门面） |
| `ScriptManagerSettingsService` | `config\script-manager.json`（`IOptionsStore` 分区名 `script-manager`） |

- VM 侧：`QuickActionListViewModel` 为 `script-library` / `script-commands` 共用基类（`ToolbarExtras` / `ToolbarTailExtras` 拼装工具条）。
- 控件：`Controls/SysInfoGrid`（`Grid` 子类，规格串 → `ColumnDefinitions`；**星号列宽必需**，`ItemsControl` + 横向 `StackPanel` 表达不了）。

### 3.3 数据与配置

| 位置 | 内容 | 备份 |
|---|---|---|
| `config\script-manager.json` | 脚本目录（`IOptionsStore` 分区） | ✅ |
| `config\script\script.json` | 手工数据：集合 / 条目（脚本·命令·采集器·软件包）/ 标签 / 视图 / `lastCollectionId` | ✅ |
| `config\package.json` | 软件包查询偏好 | ✅ |
| `data\script\script.db` | 采集观测 + 运行日志 | ⛔ 不进备份 |
| `<脚本目录>\sysinfo\*.ps1` | 内置采集脚本（播种） | — |

- ⛔ `config\script\settings.json` 不可行：`IOptionsStore.Validate` 禁路径分隔符 / `.` / `..`。
- ⛔ **不做旧数据迁移**（用户拍板「无所谓老用户」）：旧四份 JSON 静默忽略（不读不写不删）。

### 3.4 执行频率三层 + 保留策略

- 频率：**视图 > 集合 > 条目**（后者覆盖前者）；只读项才许 `auto`；范围 2~3600s。
- 保留策略：`latest` / `count` / `days`；只读项强制 `latest`。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `ScriptManagerModule.cs` |
| 数据模型 | `Models/ScriptStore.cs` / `Models/InfoOptions.cs` / `Models/SysInfoRow.cs` |
| 执行底座 | `Services/ScriptRunner.cs` / `InterpreterResolver.cs` / `SysInfoOutputParser.cs` |
| 采集呈现 | `Services/CollectorService.cs` / `InfoRowFactory.cs` / `Controls/SysInfoGrid.cs` / `ViewModels/InfoSectionViewModel.cs` |
| 软件包 | `ViewModels/PackageViewModel.cs` / `PackageSourceViewModel.cs` + Core 的 `PackageManagerService` / `PackageOutputParser` |

## 5. 坑点（🔴 实测过）

1. 🔴🔴 **随包 `.ps1` 必须 UTF-8 带 BOM** —— PowerShell 5.1 读无 BOM 的 UTF-8 按 ANSI 解码 ⇒ 中文 literal 全乱码。
2. 脚本输出契约：`{rows:[{key,status,values}]}`，用 `[Console]::Out.Write` 输出，⛔ 不打日志到 stdout（会污染解析）。
3. `ScriptRunner` 的 `PYTHONIOENCODING` / `PYTHONUTF8` / `NEWTOYS_SYSINFO_DIR` 必须显式给，否则子进程编码漂移。
4. 🔴 导出 zip 会带 `.seeded` ⇒ 导入白名单**须先判 `..` / 绝对路径**再跳过它，否则 `../evil.ps1` 被静默放过。
5. 行级右键命令用 `CommandParameter="{Binding Selected}"`（ContextMenu 在弹出树）+ code-behind `ContextRequested` 选行。
6. 🔴 `winget.exe` 是 App Execution Alias ⇒ **必须解析绝对路径**再起进程。
7. `winget` 表头**本地化**（中文 Windows 打「名称/ID/版本/可用」）⇒ 列标签按「英文 + 中文别名」映射到规范名再切片。
8. `winget show` 的「发布服务器 URL:」会被当成上一条多行值的续行 ⇒ 不认识的 `标签:` 必须显式**终结**上一个字段并丢弃。
9. 已安装列表少一大截 = `IdLike` 正则不认 `\` ⇒ 加 `\\`（`^[A-Za-z0-9][A-Za-z0-9._+\\-]*$`）。
10. 列宽拖拽**未接**（`SysInfoGrid` 不支持）；改宽走「⚙ 列设置」写回 manifest。
11. 分节模式**关分页**（底栏改摘要），因为一次只显示一个集合的多个采集器节。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-068 | S7 软件包页换只读内核（列 / 筛选 / 批量勾选）——**待拍板**；其余 S1~S6 / S7' / S7'' / S8 已完成，⏳ 待本地 build 逐条验收 | 🔨 待拍板 |
| — | （本模块暂无其它未结项） | — |

> 已验收淘汰的卡见 `../backlog/script-manager/`（≤5 张滑动窗口）。
