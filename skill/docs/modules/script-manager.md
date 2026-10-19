# 模块 · 脚本管理器（Script Manager）

> **程序集**：`src/Module.ScriptManager`（`NewToys.ScriptManager`，产物 `Module.ScriptManager.dll`）
> **模块键**：`script` ｜ **一级菜单**：Order 20 · 图标 `IconPc`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✔ NT-068 S1~S8 已完成（⏳ 待用户本地 build / accept 验收）；✔ NT-080 首轮已完成，**v3.80 源 UI 删除返工 ⏳ 待 build 验收**
> **最后校对**：2026-10-19（v3.80：源管理全套删除，改命令库；对照 `PackageViewModel` / `SettingsScriptViewModel` / `SettingsScriptView.axaml`）

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
| `script-packages` | 软件包 | 3 | `PackageView` | 搜索 / 安装 / 升级 / 卸载（**后端只支持 winget**；跨平台 apt 另开软件包页，⛔ 不在本页切后端） |
| `script-collections` | **脚本集合** | 4 | `CollectionView` | 采集器容器：主从两栏 + 搜索 / 置顶 / 最近 + 「📤 发送到信息面板」 |
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

> 🔴 NT-073：本页是**库的展示 + 收录入口**，工具条最前 = `库 ▾`（当前库 / 全部库）+ `📥 导入库` + `📤 导出库` + `📦 全量导出`。

工具条顺序：`库 ▾` → `📥 导入库` → `📤 导出库` → `📦 全量导出` → `📂 导入`(SplitButton) → 脚本目录框 → `🔍 扫描目录` → 搜索框 → `含子目录` → `📥 导入` → `📤 导出` → `🧹 清空本页`；扫描出候选时追加 `➕ 加入库` / `✕ 收起`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 切库 | `库 ▾` | 列表只显示该库成员；「全部库」= 本 kind 全部库的合集 | 记住上次选择（`lastCollectionIdByKind.script`）；标题列在「全部库」下带 `· 库名` 后缀 |
| 导入文件夹 | 📂 导入 主钮 / 菜单「📁 文件夹…」 | 把目录里的脚本建成条目，已存在的跳过 | 起始目录 = **当前库的 `rootPath`**（空则全局脚本目录）；「含子目录」只对这条生效；新条目**自动加入当前库** |
| 导入文件 | 📂 导入 →「📄 文件…」 | 多选 `.py/.ps1/.bat/.cmd` 建条目 | 同上 |
| 改脚本目录 | 脚本目录框回车 / 失焦 | 校验通过即落盘 `config\script-manager.json` | 全局默认目录；单个库的根目录在集合页详情卡里改 |
| 扫描目录 | 🔍 扫描目录 | 出候选面板（新增 / 已入库 / 不可采集三态） | **⛔ 不自动登记**；起始目录 = 当前库根 |
| 加入库 | ➕ 加入库 | 勾选的新文件建条目并加入当前库 | 已入库 / 不可采集不重复添加 |
| 导出库 | 📤 导出库 / 📦 全量导出 | 索引包 json（定义 + 成员相对路径 + 根目录）/ zip 全量包（含脚本文件） | 「全部库」时提示先选库 |
| 导入库 | 📥 导入库 | json 索引包 → 只建库 + 条目；zip 全量包 → 先解到所选目录，库根 = 该目录 | 名称冲突自动加 `(2)`；同文件按绝对路径复用条目；zip 路径穿越会被拒 |
| 运行 | ▶ 运行（行右键 / 详情卡） | 起进程执行 | 相对脚本路径按**条目所属库的根目录**解析 |
| 删除条目 | 行右键 🗑 / 详情卡 | **从当前库移出**（条目与文件保留） | 选了具体库才走这条；移出后无归属 ⇒ 收回默认脚本库 |
| 从磁盘删除 | 行右键 🔥 | 先删文件，再从清单移除 | 不可撤销 |
| 导出 / 导入 | 📤 导出 · 📥 导入（合并 / 整页替换） | 本页**当前可见条目** ↔ json | 合并按 Id 去重、同 Id 覆盖；整页替换先清空本页 |
| 清空本页 | 🧹 清空本页 | 真删除并落盘 | 二次确认 + 提示先导出 |

### 命令库 `script-commands`

> 🔴 NT-073：工具条最前同样有 `库 ▾` / `📥 导入库` / `📤 导出库`（命令库没有文件本体 ⇒ ⛔ 无「全量导出」）。

工具条：`库 ▾` → `📥 导入库` → `📤 导出库` → `📥 导入`（json 清单，排最前）→ 只读框「数据文件」→ 搜索框 → `📤 导出` → `🧹 清空本页`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 运行 | ▶ 运行（行右键 / 详情卡） | 有输出的用可见终端跑（优先 pwsh）；GUI 启动类静默；需管理员走 UAC | 先回显命令再显示输出 |
| 复制命令 | 行右键 📋 复制命令 | 命令行文本进剪贴板 | — |
| 删除 | 行右键 🗑 | 选了具体库 ⇒ **从该库移出**；「全部库」⇒ 删 `script.json` 里的记录 | 两种都弹二次确认；移出后无归属 ⇒ 收回默认命令库 |
| 看数据文件路径 | 点只读框 | 露出真实路径并全选（好复制） | 只读；写入点在「设置 → 路径与配置」 |

### 软件包 `script-packages`

工具条：`集合 ▾`(140) → 关键字框（回车 = 搜索）→ `🔍 搜索` / `📋 已安装` / `⬆ 可升级` / `🔒 已锁定` → `🔄 重新加载` / `🔃 刷新索引` → `⬇ 安装选中` / `⬆ 升级选中` / `⬆⬆ 全部升级` / `🗑 卸载选中` → `静默安装` 开关。
（🔴 v3.80：**源选择框已删除** —— 源 = 系统 winget 的配置，增删 / 切换全要 UAC，统一走「命令库 → winget 源」集合。）
（🔴 v3.81 / NT-082：原位置换成 `集合 ▾` —— 第一项「（不按集合）」，其余 = 未归档的「软件包」类集合；切集合 ⇒ 列表变成该集合的**包清单**。）

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 三种列表 | 📋 / ⬆ / 🔒 | 都刷**同一个列表**（切一次就清空重来，⛔ 不叠加）；被锁定的包标 🔒 | 源**只影响 🔍 搜索**，不影响这三种 |
| 集合清单（NT-082） | `集合 ▾` 选一个软件包集合 | 列表 = 该集合的成员（`role=package` 条目，`Target` = 包 Id）；状态按一次 `winget list` 缓存比对（已安装 / 可升级） | 切换即写 `lastCollectionIdByKind.package`；集合页右键「发送到对应页面」就是靠这个值让本页停下同一个集合；⛔ 不逐包跑 `winget show` |
| 加入集合（NT-082） | 行右键 `📥 加入集合` | 把当前选中包写成 `role=package` 条目（`Target`=Id / `Category`=源）并加进当前集合 | 没选集合 ⇒ 用第一个软件包集合，没有就建「我的软件包」；按 Id 幂等去重；写 `script.json` 后立即刷新「集合 ▾」|
| 🔒 已锁定（NT-075） | 点按钮 | `winget pin list` → 再跑一次 `winget list` 补齐名称 / 版本 / 源，只留被锁定的包（**清空原列表**） | 老行为是"把 pin 项并进上一次结果" ⇒ 看不出哪些才是锁定的 |
| 分页（NT-075） | 列表底部底栏 | 预设 **20 / 50 / 100 / 全部**（本页 override，⛔ 不动全局 25/50/100/500）；数据在内存 ⇒ 只切片不回查。**默认 = 本页预设第一档（20）**：页大小全局记忆（`shell.json` 的 `ui.list-page-size`）读到不属于本页预设的值（如 25）时，构造期收成第一档（2026-10-19 修：否则 25 会被补进下拉，点选任一预设项时下拉集合被换掉 ⇒ `ArgumentOutOfRangeException` 闪退） | `PagingEnabled=true`；底栏显隐还需 `PackageView` 的 `SetContentOverflow` 回填 |
| 搜索 | 🔍 搜索 / 关键字回车 | **一律不传 `--source`**（winget 用自己的默认源集合，自动跳过显式源）+ 把系统里的**显式源**（如 `winget-font`）逐个补查再合并去重 | 在已安装 / 可升级列表里，关键字只是**本地过滤** |
| 源管理（v3.80） | — | 本页**没有**源开关 / 源选择框；改源去「命令库 → `winget 源`」集合（设置页有「＋ 加入命令库」一键写入） | 源是系统 winget 的配置（`source add/remove/reset` 全要 UAC），App 内不再包一层 UI |
| 商店包（msstore） | 搜到的包 | Id 是 Store 的 12 位字符；**详情 / 安装都必须带 `--source`**（v3.78 修）；⛔ 不吃 `--silent`；⛔ 不进「已安装 / 可升级」列表 | 行的「源」列由 `winget search` 自带；缺了用 `StampSource` 补 |
| 刷新索引 | 🔃 刷新索引 | 拉各源元数据（≈ apt update） | 「可升级」的结果依赖它 |
| 重新加载 | 🔄 重新加载 | 按**当前所在列表**重跑 | — |
| 批量安装 / 升级 | 勾选 + `⬇ 安装选中` / `⬆ 升级选中` | 逐条执行，**单个失败不影响其余** | 只在有勾选时可用（`CanRunBatch`） |
| 全部升级 | ⬆⬆ 全部升级 | 所有可升级包升到最新 | 已锁定跳过；执行前确认一次 |
| 批量卸载 | 勾选 + 🗑 卸载选中 | 逐条卸载，不可撤销 | **只确认一次**，⛔ 不逐条弹窗 |
| 锁定 / 解除 | 行右键 🔒 | Pin / Unpin（暂停更新） | — |
| 打开外部页 | 行右键 🌐 主页 / 🔗 winget.run | 系统浏览器打开 | — |
| 勾选 | 表头 ☑ 右键 → 全选 / 取消 / 反选 | 勾选状态变化 | — |

> 工具条**按钮文案**一律用通用动词（列出 / 搜索 / 升级 / 锁定 / 卸载…），⛔ 不出现后端名。
> 🔴 NT-074 反转：**ToolTip 一律给真实 winget 命令**（用户定调："要看它到底跑了什么"）—— 命令模板常量集中在
> `PackageViewModel` 顶部（`CmdSearch` / `CmdListInstalled` / `CmdUpgradeAll` …），改命令时提示词跟着变，⛔ 不各写一份。

### 脚本集合 `script-collections`（主从两栏）

左栏 = 集合列表（搜索 + 分组「全部 / 置顶 / 最近」+ 类别筛选，🔴 **两个筛选框默认选中「全部」**）；工具条 `➕ 新建集合` / `⧉ 复制` / `🗑 删除` / `📦 归档` / `🔍 扫描目录` / `📤 发送到对应页面` / `📤 导出` → 搜索成员 → `显示不可采集`。

> 🔴 v3.81（NT-082）：集合类别**四档** —— 信息面板 / 脚本库 / 命令库 / **软件包**。
> 「软件包」不是「用集合冒充 winget 查询结果」，而是**包清单容器**：成员 = `role=package` 条目（`Target` = 包 Id），
> 发送到软件包页后按清单显示安装状态。定位是**跨包管理器的架构预设**（将来 apt / flatpak / snap 各有后端，集合按后端归类，
> 页面再把成员命令绑成按钮）。`role=package` 条目只在「软件包」类集合的候选池里出现，⛔ 不混进其他集合。
> （v3.79 曾整体删除该档，理由是「软件包是实时查询结果」；本次按用户要求恢复为清单容器，逻辑 = 实时查询走搜索、手工清单走集合。）

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建集合 | ➕ 新建集合 | 建一个空集合，接着在右栏勾成员 | — |
| 复制 | ⧉ 复制 | 复制集合定义（**含成员 + 视图 + 每节列显示方式**） | 视图不复制 ⇒ 副本会退回全默认，故必须一起复制 |
| 删除 | 🗑 删除 | 删集合定义 | 🔴 **只有「在用」的集合不能删**（用户 2026-10-19）；默认库恒「在用」⇒ 自然受保护，⛔ 不再另按 `Builtin` 拦；二次确认「只删集合定义，不删条目」 |
| 归档 | 📦 归档 | 归档后默认不显示，排序沉底 | 仍可见（从「全部」里能取消归档） |
| 扫描目录 | 🔍 扫描目录 | 新脚本入库并**加入当前集合** | 与本页「⛔ 不自动登记」一致：用户手动触发 |
| 发送到对应页面 | 工具条 `📤 发送到对应页面` / 左栏集合**右键**同名项 | 按集合类别跳目标页并选中该集合：信息面板→`script-info` / 脚本库→`script-library` / 命令库→`script-commands` / 软件包→`script-packages` | 写 `lastCollectionIdByKind.<kind>` 后 `NavigateTo`，目标页构造时读回；`PageKey` 与 `ScriptCollectionKind` 同源（`ScriptCollectionKind.PageKey`），⛔ 别处另写 switch；右键命中的行由 code-behind 先选中（Avalonia 右键不自动选行） |
| 成员增减 | 右栏成员行**勾选** | **立即写** `config\script\script.json` | ⛔ 无保存按钮；不可采集项灰显 `CanToggle=false`，悬停给原因 |
| 显示不可采集 | 工具条开关，默认关 | 打开后列不可采集项（灰显不可勾） | — |
| 显示全部类型 | 工具条开关，默认关 | 候选池默认按**当前集合的 kind** 过滤（脚本库集合只列脚本、命令库只列命令、软件包集合只列 `role=package`、信息面板集合不限）；打开后跨类可见 | NT-073 D13；`role=package` 与「软件包」集合是**一对一容器关系**，不受此开关影响 |
| 改脚本根目录 | 详情卡「脚本根目录」（仅脚本库类集合） | 失焦落盘 `rootPath`；空 = 全局脚本目录 | 相对路径按它解析；命令库 ⛔ 不显示该字段 |
| 改类别 | 详情卡「类别」下拉（信息面板 / 脚本库 / 命令库 / **软件包**四选一） | 落盘 `kind`，决定集合归哪个页面 | 🔴 走**字符串值通道**（`EditKind` + `OptionValuePath=Value`），⛔ 不绑选项对象（否则永远显示第一项） | 内置库也可改（改走后下次启动自动补回该 kind 的默认库）；改 kind 后刷新 / 视图字段可用性随之变；🔴 文案与左栏徽标 / 筛选下拉三处一致 |
| 选中集合 | 点左栏一行 | 右栏成员表 + 详情卡随之更新 | — |
| 使用中标记 | 左栏行副标题右侧「在用」 | 各页面（信息面板 / 脚本库 / 命令库）当前选中的集合（`lastCollectionIdByKind`）+ **默认库恒为「在用」**（兜底接收无归属条目） | 「在用」= 唯一不可删条件；`kind=info` 的「在用」排最后（NT-072 防误删），默认库 / script / command 不沉底；进入本页时重算（`CollectionView.OnAttachedToVisualTree` → `RefreshUsageState`） |
| 置顶 ★ | 详情卡「置顶」勾选框 / 左栏行尾 ★ | 落盘 `pinned` | 分组选「置顶」可单独筛出；信息面板集合下拉里排最前 |
| 重名 | 详情卡「名称」（失焦提交） | 撞名 ⇒ ⛔ 不落盘，输入框退回原名 + 状态栏提示 | 新建 / 复制自动取不重名的名字（`新集合 2` / `X 副本 2`） |

#### 内置集合「环境维护」（NT-083，只播种一次）

软件**自己要用**的环境准备脚本（不是教学示例）单独放一个内置脚本库集合，复用本模块的运行 / 输出捕获 / 超时，⛔ 不另做"工具页"。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 建集合 + 成员 | 首次启动（`EnvScriptsSeeder.Seed`） | 随包 `defaults\env-scripts\*` 复制到 `<脚本目录>\env\`；登记条目 `env-python-setup`「环境：准备 Python」；建集合 `builtin-env`「环境维护」并装入该成员 | 🔴 **只播种一次**（`.seeded` 标记）：用户删集合 / 改脚本 / 移出成员都不复活、不覆盖、不抢回；建集合**只在刚建时**装成员 |
| 运行 | 脚本库页点「▶ 运行」（或收藏夹） | 建 `%LOCALAPPDATA%\NewToys\tools\py\venv` 并装 `requests` / `lxml` / `parsel`；输出进现有输出窗 | 条目 `Timeout=900`、`Capture=true`、`ParseMode=Line`；幂等（有 venv 只补装），`-Force` 重建、`-IndexUrl` 换镜像 |
| 当采集器 | — | ⛔ 不允许 | `Collectable=false` + `IneligibleReason`：有副作用（写磁盘 / 装包），不进采集器候选池，集合页「显示不可采集」下也灰显不可勾 |
| 被扫描 | 脚本库「🔍 扫描目录」 | ⛔ 该目录（`env\`）**不出现在扫描候选里** | `ScriptScanner` 按第一段目录名跳过它：扫进去会被当成普通采集脚本，勾进集合 + 设定时 ⇒ 反复建环境 / 装包（`examples\` / `sysinfo\` 无副作用，仍可扫） |
| 环境状态 | 设置 → 运行环境 → 「Python 共享环境（venv）」行 | 只读显示 `Probe()` 结论（venv 就绪 / 用系统解释器 / 未找到 Python）+ `↗` 打开 `tools\py` | 进本节时随外部程序一起惰性探测（`ProbeExternalTools`）；`Value` 恒为工具根目录（供 `↗` 用） |


### 信息面板 `script-info`

工具条：`集合 ▾`(240) → `视图 ▾`(140) → `🔄 全部刷新` / `📂 脚本目录`。🔴 v3.77：**工具条里⛔ 没有只读文字标签**（原「集合 / 视图」两个文字已删）—— 两个下拉各自带 `ToolTip` 说明自己是什么。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 切集合 | 集合 ▾ | 整屏换；记住上次选择，重启恢复 | 排序 = ★置顶 → 最近使用 → 名称；⛔ 不做按钮条 / 标签条 |
| 切视图 | 视图 ▾ | 换一套显示方式：**每节显示哪些列 / 列宽 / 节顺序 / 折叠态** | 差异全部记在视图（`views[].sectionColumns`），⛔ 不写脚本本体 ⇒ 同一脚本在不同集合可显示不同列 |
| 全部刷新 | 🔄 全部刷新 | 只刷**未折叠**的节 | 前一次未完成 ⇒ 跳过本次 |
| 刷新本节 | 节头 ⟳ | 只刷这一节 | 单节可独立刷新 |
| 折叠 / 展开 | 节头 ⌄ / › | 折叠后不参与自动刷新 | 折叠态落视图定义并持久化 |
| 选中节（切详情卡） | **点节任意处**（节头 / 节点都行） | 右侧详情卡切到这一节 | **不主动打开面板**（和列表页点行一个心智）；节头按钮（⟳ / 折叠）点击不冒泡成选中 |
| 打开节详情卡 | **右键任意一节** → 「⚙ 显示这一节的详情 / 设置」 | 打开面板并切到这一节 | 这是删掉节头 `⚙` 与工具条 `⚙ 节设置` 后的**唯一显式打开入口** |
| 节详情卡内容 | — | ① 节（采集器 / 目标 / 状态）② 采集参数（解析器 / 超时 / 保留 / 保留数量 / 自动刷新 / 间隔）→ `💾 保存参数` ③ 本视图显示（列 chips 点一下切显隐 + **「表头名称」逐列改名，失焦即生效**）+ `⬆ 上移 / ⬇ 下移` | ⛔ 卡里**不显示行的属性值**（行数据只在节体表格里）；采集参数所有集合共用，显示 / 表头名只影响当前视图 |
| 改表头名称 | 详情卡「表头名称」每列一个输入框 | 失焦提交 → 写 `views[].sectionColumns.titles` → 本节表头重投影 | 与默认标题相同则不存覆盖；⛔ 不改脚本本体 |
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
| 软件源（v3.80） | 只读**命令清单**（6 条）+ `＋ 加入命令库（winget 源）` | 点按钮把它们写进命令库的「winget 源」集合（幂等：按标题去重，已有就跳过），之后去命令库点运行 | 🔴 本页**不执行 winget、不弹 UAC**；源是系统配置（`source add/remove/reset` 全要 UAC）⇒ 执行归命令库（`NeedAdmin=true` ⇒ `runas` + 可见控制台）；这批命令写进 `script.json`（随「设置 → 数据」备份） |
| 〃为什么不做开关（v3.80 用户定调） | — | — | winget 1.29 **没有 `source enable/disable`**（只有 add/list/update/remove/edit/reset/export）⇒ 「停用」= remove；App 里做开关等于把「终端一条命令」包成一层 UI + 自己记定义快照 + 管分类互斥，纯增加复杂度。**源与系统共用一套数据，用命令操作最直接** |


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
| `ScriptStoreService` | 总库 **`script_docs`** 文档表读写（整份 `ScriptStoreRoot` 一条，`payload` = JSON 原文），手工数据唯一入口。NT-076 第二批前是 `config\script\script.json` |
| `ScriptDatabase` | 总库的 `pc_observations` + `pc_runs` 两表 + 索引 + 保留策略（NT-076 起不再有独立 `script.db`），**可重建、不进备份** |
| `CollectorService` | 采集执行（并发上限、按采集器归舱、只刷未折叠节） |
| `InfoRowFactory` | 只读呈现内核（列 schema 自动生成） |
| `ScriptRunner` | 进程执行（NT-081 步骤3 起本体下沉 `IProcessRunner`：显式 UTF-8 / 超时 / 取消杀进程树，与 `PackageManagerService` 共用）；本类只留脚本特有部分：解释器解析、`PYTHONIOENCODING` 等环境约定、结果语义（严格 stdout，非法字节 ⇒ 判失败） |
| `InterpreterResolver` | 解释器 → exe + args：配置路径 / 探安装位 / PATH 回退；Auto 按扩展名 |
| `SysInfoOutputParser` | 三种外壳的宽松解析（`{rows:[{key,status,values}]}` 等） |
| `CollectableEvaluator` | `collectable` 可采集判定（自动规则 + 用户覆盖） |
| `ScriptScanner` | 目录扫描三态（新增 / 已存在 / 忽略） |
| `BuiltinCollectorSeeder` | 内置采集脚本播种（**只加不覆盖** + `.seeded` 标记） |
| `ExampleSeeder` | 随包**示例**播种：`defaults\scripts\*` → `<脚本目录>\examples\` + 3 个示例脚本条目 + 1 个采集器模板 + 8 条示例命令（🔴 **只播种一次**，删了不复活） |
| `EnvScriptsSeeder` | 随包**环境脚本**播种（NT-083）：`defaults\env-scripts\*` → `<脚本目录>\env\` + 条目 `env-python-setup` + 内置集合「环境维护」（`builtin-env`；`Collectable=false`，🔴 **只播种一次**，不覆盖用户改动） |
| `QuickActionService` | `script_docs` 适配器 + `ConfirmHandler` 复用（⛔ 不再是四份 JSON 的门面） |
| `ScriptManagerSettingsService` | `config\modules\script-manager.json`（`IOptionsStore` 分区名 `script-manager`） |

- VM 侧：`QuickActionListViewModel` 为 `script-library` / `script-commands` 共用基类（`ToolbarExtras` / `ToolbarTailExtras` 拼装工具条）。
- 控件：`Controls/SysInfoGrid`（`Grid` 子类，规格串 → `ColumnDefinitions`；**星号列宽必需**，`ItemsControl` + 横向 `StackPanel` 表达不了）。

### 3.3 数据与配置

| 位置 | 内容 | 备份 |
|---|---|---|
| `config\modules\script-manager.json` | 脚本目录（`IOptionsStore` 分区，NT-077 第二批归位） | ✅ |
| `config\modules\package.json` | 软件包查询偏好 | ✅ |
| `data\newtoys.db` 的 `script_docs` | 手工数据：集合 / 条目（脚本·命令·采集器·软件包）/ 标签 / 视图 / `lastCollectionId`（整份 JSON 一条） | ⛔ 随库 |
| `data\newtoys.db` 的 `pc_observations` / `pc_runs` | 采集观测 + 运行日志 | ⛔ 随库 |
| `<脚本目录>\sysinfo\*.ps1` | 内置采集脚本（播种） | — |
| `<脚本目录>\examples\*` | 随包示例脚本（`.ps1` / `.py` / `.bat` + 采集器模板）+ `.seeded` 一次性标记 | — |
| `<脚本目录>\env\*` | 随包环境脚本（`setup-python-env.ps1` + `python-requirements.txt`）+ `.seeded` 一次性标记（NT-083） | — |
| `%LOCALAPPDATA%\NewToys\tools\py\` | Python 共享环境：`venv\` + `py_worker.py`（NT-083，与 `env.json` 同级；⛔ 不进 `profiles\`） | — |

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
| 执行底座 | `Services/ScriptRunner.cs` / `InterpreterResolver.cs` / `SysInfoOutputParser.cs`；进程本体在框架层 `IProcessRunner`（Shared 契约 / Core 实现，Shell 注册单例） |
| 采集呈现 | `Services/CollectorService.cs` / `InfoRowFactory.cs` / `Controls/SysInfoGrid.cs` / `ViewModels/InfoSectionViewModel.cs` |
| 软件包 | `ViewModels/PackageViewModel.cs` / `PackageSourceViewModel.cs` + **本模块** `Services/PackageManagerService.cs` / `PackageOutputParser.cs` / `PackageSettingsService.cs` / `Models/PackageModels.cs`（NT-074：整套从 Core / Shared 下放，只有本模块用） |
| 解析分层（NT-081 步骤4） | 通用机械 `Services/FixedWidthTable.cs`（固定列宽：清洗 / 显示宽度 / 切片 / 溢出回填 / 噪行）+ 机读 `Services/TsvRows.cs`（TSV → 行模型）；winget 固有启发式（别名表 / 续行合并 / pin 兜底 / 标签-值）留在 `PackageOutputParser.cs` |
| 后端适配器（NT-081 步骤5） | `Services/AptPackageService.cs`（Linux 只读：dpkg-query TSV / `apt list --upgradable` / sources.list / `apt-mark showhold`；写操作未实装）；`ScriptManagerModule` 按平台二选一注册 |

## 5. 坑点（🔴 实测过）

> **NT-076 第二批**：手工数据已从 `config\script\script.json` 搬进总库 `script_docs`（整份 JSON 一条）。`ScriptStoreService.Reload()` 在表里没有文档时会**先试搬旧文件**再播种子 —— 顺序反了就会先落一份空种子、再把真实数据当“已有文档”跳过。旧文件搬完改名 `.json.legacy`，⛔ 不删。

1. 🔴🔴 **随包 `.ps1` 必须 UTF-8 带 BOM** —— PowerShell 5.1 读无 BOM 的 UTF-8 按 ANSI 解码 ⇒ 中文 literal 全乱码。
2. 脚本输出契约：`{rows:[{key,status,values}]}`，用 `[Console]::Out.Write` 输出，⛔ 不打日志到 stdout（会污染解析）。
3. `ScriptRunner` 的 `PYTHONIOENCODING` / `PYTHONUTF8` / `NEWTOYS_SYSINFO_DIR` 必须显式给，否则子进程编码漂移。
4. 🔴 导出 zip 会带 `.seeded` ⇒ 导入白名单**须先判 `..` / 绝对路径**再跳过它，否则 `../evil.ps1` 被静默放过。
5. 行级右键命令用 `CommandParameter="{Binding Selected}"`（ContextMenu 在弹出树）+ code-behind `ContextRequested` 选行。
6. 🔴 `winget.exe` 是 App Execution Alias ⇒ **必须解析绝对路径**再起进程。
7. 🔴 **内置「系统信息」曾被播种成两份**（用户 2026-10-19 发现）：`EnsureInfoCollection` 用 `c.Kind == Info` **精确比较**、固定 id `builtin-info` 被占时又改随机 id 另建 ⇒ 两个 `builtin=true` + 同名 + 同样的 6 个采集器。已修：比较一律走 `ScriptCollectionKind.Normalize`、id 被占就**接管**那个 info 集合、`DedupeBuiltinInfo` 每次播种把多余的内置 info 集合合并（成员并集 / 丢副本视图 / 在用 id 改指保留的那个）。
7. `winget` 表头**本地化**（中文 Windows 打「名称/ID/版本/可用」）⇒ 列标签按「英文 + 中文别名」映射到规范名再切片。
8. `winget show` 的「发布服务器 URL:」会被当成上一条多行值的续行 ⇒ 不认识的 `标签:` 必须显式**终结**上一个字段并丢弃。
9. 已安装列表少一大截 = `IdLike` 正则不认 `\` ⇒ 加 `\\`（`^[A-Za-z0-9][A-Za-z0-9._+\\-]*$`）。
10. 列宽拖拽**未接**（`SysInfoGrid` 不支持）；改宽走「⚙ 列设置」写回 manifest。
11. 分节模式**关分页**（底栏改摘要），因为一次只显示一个集合的多个采集器节。
12. 🔴 **示例 vs 内置采集器是两套语义**：`ExampleSeeder` 只播种一次（`examples\.seeded` 存在即整体跳过，删掉的示例**不复活**）；`BuiltinCollectorSeeder` 缺了就补回。改 `defaults\scripts\*` 后老配置目录不会自动更新（要用户删 `examples\` 目录再启动）。
13. 🔴 **解析分层纪律（NT-081）**：固定列宽表格的通用机械只在 `FixedWidthTable`（🔴 按**显示宽度**切，汉字 2 列）；winget 的续行合并 / 别名表 / pin 兜底是**固有启发式**⛔ 不进通用层；机读后端（dpkg-query）⛔ 不走表格解析，直接 `TsvRows`。新增后端 = 新适配器 + 命令模板，页面 / VM 零改动。
14. 🔴 **进程执行只走 `IProcessRunner`**（NT-081）：散落的 `Process.Start` 分三类，⛔ 不一刀切合并 —— ① 捕获式外部工具（脚本 / 命令 / winget）⇒ `IProcessRunner`；② 平台能力（打开文件夹 / 文件 / URL，`UseShellExecute=true`）⇒ VM 直接用 BCL；③ 可见控制台 / 提权（`Verb=runas`）⇒ `QuickActionService.StartVisible`（winget 源命令走命令库 + UAC，NT-080 定调）。
13. `.bat` 示例**只写 ASCII** + 首行 `chcp 65001` —— 带中文的 .bat 在 936 代码页下必乱码，且 BOM 会被 cmd 当命令字符。
14. 🔴 v3.80：**源在 UI 里没有入口了**。源选择框 / 源开关 / 分类 / 互斥 / 定义快照全删
    （`PackageSourceViewModel`、`PackageSourceRow`、`PackageSettings.DefaultSource|SourceCategories|SourceDefinitions`、
    `PackageManagerService.RunElevatedAsync` 一并删除）—— 源操作 = 命令库里的命令条目；设置页只剩「命令清单 + ＋ 加入命令库」
    （写入 `script.json` 的集合「winget 源」，按标题幂等去重）。
15. NT-074：软件包服务下放到模块后，**Shell 不能再给它接确认钩子**（Release 下 Shell 不引模块）⇒
    二次确认改走 `Core.DialogHooks.ConfirmAsync`（原 `PackageManagerService.ConfirmHandler` 已删）。
16. 🔴🔴 v3.80 实测 + 用户定调：**winget 1.29 没有 `source enable` / `source disable`**
    （`winget source --help` 只有 add/list/update/remove/edit/reset/export）⇒ 「停用源」只能 `source remove`，
    要能加回来必须先 `source export` 记定义 —— **这就是源 UI 被砍掉的原因**：与其在 App 里模拟一套开关状态，
    不如把 `source add/remove/reset` 交给命令库（条目的 `NeedAdmin=true` ⇒ `runas` 提权 + 可见控制台）。
    ⛔ 装 / 卸 / pin 包不提权（用户级安装不需要）。
17. 🔴 v3.81（NT-082）：`role=package` 条目 ↔ 「软件包」类集合是**一对一容器** ——
    候选池判定写成 `isPackageItem != isPackageCol ⇒ continue`；⛔ 别只把 v3.79 那句无条件 `continue` 删掉
    （那样包条目会污染脚本库 / 命令库候选池）。软件包页的清单状态只取自**一次 `winget list` 缓存**，
    ⛔ 不逐包跑 `winget show`（慢、且会触发源元数据更新）。
17. 🔴 NT-075：`winget pin list` 的**版本号长得跟包 Id 一样**（都有点号）⇒ 老实现"扫所有带点号的词"会把
    `24.08` 当成一个包 Id 混进列表。现在走 `ParsePins`（表头切片取 `Id` + `Version`），兜底正则按形状排除纯数字版本。
18. 🔴 NT-059：`ApplyView()` 里 `Items.Clear()` 会让 ListBox 的 `SelectedItem` 双向绑定**回写成 null** ⇒ 点收藏 / 切分类后详情卡掉回示例态。
    修法：刷新统一走 `ApplyViewKeepSelection()`（刷新后把选中项放回去），收藏 / 分类切换都改走它。
19. 🔴 NT-059：`QuickActionService.RemoveAsync` 内部会弹确认 ⇒ 详情卡 🗑 与「🔥 从磁盘删除」两条路会**问两遍**，
    且第二遍取消时文件已删。修法：`IQuickActionService.RemoveSilent`（⛔ 不弹），确认框由调用方自己弹。
20. NT-059：导入只有一个 `SplitButton`「📂 导入」（主钮 = 选文件夹，箭头 = 文件夹 / 文件两路）；落盘口径 = 目录内**相对文件名**、目录外**绝对路径**（`ToStoredScriptPath`）；导入后必须清搜索框 + 分类归「全部类型」+ 清表头筛选 + 选中第一条，否则"导入了看不到"。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 跨平台包管理（Debian/Ubuntu 的 apt 等）：**另开独立软件包页**（`IPackageManagerService` 保持中立，新增实现 + 新页面 + 新菜单 Key），⛔ 不在现有「软件包」页里切后端 | 📝 未立项 |

> 已验收淘汰的卡见 `../backlog/script-manager/`（≤5 张滑动窗口）。
