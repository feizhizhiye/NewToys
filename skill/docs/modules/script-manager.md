# 模块 · 脚本管理器（Script Manager）

> **程序集**：`src/Module.ScriptManager`（`NewToys.ScriptManager`，产物 `Module.ScriptManager.dll`）
> **模块键**：`script` ｜ **一级菜单**：Order 20 · 图标 `IconPc`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✔ NT-068 S1~S8 已完成（⏳ 待用户本地 build / accept 验收）
> **最后校对**：2026-10-18（对照 `ScriptManagerModule.cs` / 文件清单 / NT-068 执行记录；§2.5 对照各 ViewModel 的 `Toolbar` / `[RelayCommand]` 与 `Views` 右键菜单）

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

## 2.5 操作逻辑（交互契约）

> 每页一张表：**操作 → 触发 → 结果 / 状态变化 → 约束**。改页面必须保住这些行为；跨模块共用的语义（右键 vs 工具条 / 确认判据 / 写入时机 / 状态反馈）见 `../ui.md` §10。

### 收藏夹 `script-favorites`

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 运行 | 点按钮墙上的条目按钮 | 起进程执行；结论写状态栏 | 执行中全部按钮禁用（`IsNotBusy`） |
| 取消固定 | 条目右键 → ✕ 取消固定 | 从收藏夹移除（= 取消收藏） | 只动清单，⛔ 不碰磁盘文件 |
| 去脚本库 | ⚙ 打开脚本库 | 跳 `script-library` | — |

> 按钮墙右键菜单在 `FavoritesView.axaml.cs` **现构造**（弹出树取不到 DataContext），⛔ 别搬回 XAML。

### 脚本库 `script-library`

工具条顺序：`📂 导入`(SplitButton) → 脚本目录框 → `🔍 扫描目录` → 搜索框 → `含子目录` → `📥 导入` → `📤 导出` → `🧹 清空本页`；扫描出候选时追加 `➕ 加入库` / `✕ 收起`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 导入文件夹 | 📂 导入 主钮 / 菜单「📁 文件夹…」 | 把目录里的脚本建成条目，已存在的跳过 | 起始目录 = **配置的脚本目录**；「含子目录」只对这条生效 |
| 导入文件 | 📂 导入 →「📄 文件…」 | 多选 `.py/.ps1/.bat/.cmd` 建条目 | 同上 |
| 改脚本目录 | 脚本目录框回车 / 失焦 | 校验通过即落盘 `config\script-manager.json` | 非法 ⇒ 状态栏 `⚠` + ⛔ 不写盘；唯一写入点仍是设置页 |
| 脚本目录框的显示态 | 自动 | **失焦只显示水印「脚本目录」**，点进去才露出当前值（`PlaceholderWhenIdle`）；失焦先提交再清水印 | ⛔ 别改成"常显路径"或把水印当值提交（顺序反了会重置成默认目录） |
| 扫描目录 | 🔍 扫描目录 | 出候选面板（新增 / 已入库 / 不可采集三态） | **⛔ 不自动登记**；面板只在有结果时出现 |
| 加入库 | ➕ 加入库 | 勾选的新文件建条目，可同时加入某集合 | 已入库 / 不可采集不重复添加 |
| 运行 | ▶ 运行（行右键 / 详情卡） | 起进程执行 | 执行中禁用 |
| 从列表移除 | 行右键 🗑 | 只删清单行 | **不弹确认**（`ConfirmBeforeDelete=false`）—— 文件还在磁盘上 |
| 从磁盘删除 | 行右键 🔥 | 先删文件，再从清单移除 | 不可撤销 |
| 导出 / 导入 | 📤 导出 · 📥 导入（合并 / 整页替换） | 本页全条目 ↔ json | 合并按 Id 去重、同 Id 覆盖；整页替换先清空本页 |
| 清空本页 | 🧹 清空本页 | 真删除并落盘 | 二次确认 + 提示先导出 |

### 命令库 `script-commands`

工具条：`📥 导入`（json 清单，排最前）→ 只读框「数据文件」→ 搜索框 → `📤 导出` → `🧹 清空本页`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 运行 | ▶ 运行（行右键 / 详情卡） | 有输出的用可见终端跑（优先 pwsh）；GUI 启动类静默；需管理员走 UAC | 先回显命令再显示输出 |
| 复制命令 | 行右键 📋 复制命令 | 命令行文本进剪贴板 | — |
| 删除 | 行右键 🗑 | 删 `script.json` 里的记录 | **弹二次确认**（`ConfirmBeforeDelete=true`）—— 这条删了就没了 |
| 看数据文件路径 | 点只读框 | 露出真实路径并全选（好复制） | 只读；写入点在「设置 → 路径与配置」 |

### 软件包 `script-packages`

工具条：源下拉 → 关键字框（回车 = 搜索）→ `🔍 搜索` / `📋 已安装` / `⬆ 可升级` / `🔒 已锁定` → `🔄 重新加载` / `🔃 刷新索引` → `⬇ 安装选中` / `⬆ 升级选中` / `⬆⬆ 全部升级` / `🗑 卸载选中` → `静默安装` 开关。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 三种列表 | 📋 / ⬆ / 🔒 | 都刷**同一个列表**；被锁定的包标 🔒 | 源下拉**只影响 🔍 搜索**，不影响这三种 |
| 搜索 | 🔍 搜索 / 关键字回车 | 去远端源查（「全部源」= 逐个源查再合并） | 在已安装 / 可升级列表里，关键字只是**本地过滤** |
| 刷新索引 | 🔃 刷新索引 | 拉各源元数据（≈ apt update） | 「可升级」的结果依赖它 |
| 重新加载 | 🔄 重新加载 | 按**当前所在列表**重跑 | — |
| 批量安装 / 升级 | 勾选 + `⬇ 安装选中` / `⬆ 升级选中` | 逐条执行，**单个失败不影响其余** | 只在有勾选时可用（`CanRunBatch`） |
| 全部升级 | ⬆⬆ 全部升级 | 所有可升级包升到最新 | 已锁定跳过；执行前确认一次 |
| 批量卸载 | 勾选 + 🗑 卸载选中 | 逐条卸载，不可撤销 | **只确认一次**，⛔ 不逐条弹窗 |
| 锁定 / 解除 | 行右键 🔒 | Pin / Unpin（暂停更新） | — |
| 打开外部页 | 行右键 🌐 主页 / 🔗 winget.run | 系统浏览器打开 | — |
| 勾选 | 表头 ☑ 右键 → 全选 / 取消 / 反选 | 勾选状态变化 | — |

> 工具条文案一律用**通用动词**（列出 / 搜索 / 升级 / 锁定 / 卸载…），⛔ 不出现后端名（winget / scoop）。

### 集合 `script-collections`（主从两栏）

左栏 = 集合列表（搜索 + 分组「全部 / 置顶 / 最近」+ kind 筛选）；工具条 `➕ 新建集合` / `⧉ 复制` / `🗑 删除` / `📦 归档` / `🔍 扫描目录` / `📤 发送到信息面板` / `📤 导出` → 搜索成员 → `显示不可采集`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建集合 | ➕ 新建集合 | 建一个空集合，接着在右栏勾成员 | — |
| 复制 | ⧉ 复制 | 复制集合定义（含成员） | — |
| 删除 | 🗑 删除 | 删集合定义 | **内置集合不可删**；二次确认 |
| 归档 | 📦 归档 | 归档后默认不显示，排序沉底 | 仍可见（从「全部」里能取消归档） |
| 扫描目录 | 🔍 扫描目录 | 新脚本入库并**加入当前集合** | 与本页「⛔ 不自动登记」一致：用户手动触发 |
| 发送到信息面板 | 📤 发送到信息面板 | 跳 `script-info` 并选中该集合 | — |
| 成员增减 | 右栏成员行**勾选** | **立即写** `config\script\script.json` | ⛔ 无保存按钮；不可采集项灰显 `CanToggle=false`，悬停给原因 |
| 显示不可采集 | 工具条开关，默认关 | 打开后列不可采集项（灰显不可勾） | — |
| 选中集合 | 点左栏一行 | 右栏成员表 + 详情卡随之更新 | — |

### 信息面板 `script-info`

工具条：`集合 ▾`(240) → `视图 ▾`(140) → `🔄 全部刷新` / `⚙ 节设置` / `📂 脚本目录`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 切集合 | 集合 ▾ | 整屏换；记住上次选择，重启恢复 | 排序 = ★置顶 → 最近使用 → 名称；⛔ 不做按钮条 / 标签条 |
| 全部刷新 | 🔄 全部刷新 | 只刷**未折叠**的节 | 前一次未完成 ⇒ 跳过本次 |
| 刷新本节 | 节头 ⟳ | 只刷这一节 | 单节可独立刷新 |
| 折叠 / 展开 | 节头 ⌄ / › | 折叠后不参与自动刷新 | 折叠态落视图定义并持久化 |
| 节设置 | ⚙（工具条或节头） | 打开设置区：解析器 / 超时 / 保留策略 / 自动刷新 → `💾 保存设置` | 针对**当前选中节** |
| 展开全部 | 节体「展开全部」 | 预览 20 行 → 显示全量 | 节数上限 40 |
| 列排序 | 点列头 | 该节内排序 | 节与节**列宽独立** |
| 分页 | — | 分节模式**关分页**，底栏改摘要（耗时 · 节数 · 总行数） | — |
| 空态 | — | 两种：没有集合 / 集合里没采集器 | 主动作按钮给去处 |

### 脚本管理器设置 `settings-script`

> 🔴 NT-071：本页改用**共用设置页骨架**（`NewToys.UI/Controls/SettingsSectionHost`，`Sticky="False"`）—— 3 个节 = 折叠卡片
> （脚本目录 / 软件源 / 设置文件），点抬头开收，默认全展开。节内 ⛔ 再写 `Border.card`。细则见 `ui.md` §11。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 改脚本目录 | 输入框失焦 | 校验通过即落盘 | **⛔ 无保存按钮**（即时生效）；非法 ⇒ 底栏红字 + ⛔ 不写盘 |
| 选择目录 | 📂 选择目录 | 弹文件夹选择器 → 写入 | — |
| 恢复默认 | 恢复默认 | 清空用户值 ⇒ `<应用根>\scripts` | — |
| 软件源 | 内嵌 `PackageSourceView` 节 | 原独立设置页并入本页 | 其状态转发到本页状态栏 |


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
| `ExampleSeeder` | 随包**示例**播种：`defaults\scripts\*` → `<脚本目录>\examples\` + 3 个示例脚本条目 + 1 个采集器模板 + 8 条示例命令（🔴 **只播种一次**，删了不复活） |
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
| `<脚本目录>\examples\*` | 随包示例脚本（`.ps1` / `.py` / `.bat` + 采集器模板）+ `.seeded` 一次性标记 | — |

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
12. 🔴 **示例 vs 内置采集器是两套语义**：`ExampleSeeder` 只播种一次（`examples\.seeded` 存在即整体跳过，删掉的示例**不复活**）；`BuiltinCollectorSeeder` 缺了就补回。改 `defaults\scripts\*` 后老配置目录不会自动更新（要用户删 `examples\` 目录再启动）。
13. `.bat` 示例**只写 ASCII** + 首行 `chcp 65001` —— 带中文的 .bat 在 936 代码页下必乱码，且 BOM 会被 cmd 当命令字符。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-068 | S7 软件包页换只读内核（列 / 筛选 / 批量勾选）——**待拍板**；其余 S1~S6 / S7' / S7'' / S8 已完成，⏳ 待本地 build 逐条验收 | 🔨 待拍板 |
| — | （本模块暂无其它未结项） | — |

> 已验收淘汰的卡见 `../backlog/script-manager/`（≤5 张滑动窗口）。
