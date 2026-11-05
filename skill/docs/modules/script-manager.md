# 模块 · 脚本管理器（Script Manager）

> **程序集**：`src/Module.ScriptManager`（`NewToys.ScriptManager`，产物 `Module.ScriptManager.dll`）
> **模块键**：`script` ｜ **一级菜单**：Order 20 · 图标 `IconPc`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✔ NT-068 S1~S8 已完成（⏳ 待用户本地 build / accept 验收）；✔ NT-080 首轮已完成，**v3.80 源 UI 删除返工 ⏳ 待 build 验收**；🔄 **NT-088 / NT-089 S1 已实施（⏳ 待 build 验收）**
> **最后校对**：2026-10-22（NT-088：执行器上提 Core；NT-089 S1：抓取能力整体平移入本模块 —— 新增 `web-scrape` / `web-rule-editor` / `web-crawl` 三页；对照 `ScriptManagerModule.cs`）

---

## 1. 职责与边界

- **定位**：脚本 / 命令 / 抓取规则的索引库 + 执行器（「脚本管理器」）—— 可执行单元 + 集合容器 + 命令链编排。不联网抓取数据、不做云同步。
- ⛔ 不引 Shell；⛔ 不动 `ConfigStoreService` / `SqliteDatabase`（框架层冻结）—— 模块内自建子目录 + 自建连接。
- 模块改名历史：`Module.PcTools` → `Module.ScriptManager`（NT-068 S1，commit `9eea91f`）；旧菜单 Key `pc-*` **已全部改为 `script-*`**（一次性改名，用户拍板）。

## 2. 当前功能（6 页 + 1 设置页）

> 🔴 **NT-089 S1**：原属网络工具的抓取能力 —— 规则文件（`RuleFileService`）/ 目录（`RuleCatalogService`）/
> 抓取运行（`RuleRunService`）/ 运行记录（`RunRecordService`）/ 下载器（`DownloadService`）/ 流水线（`FlowService`）
> + 三页 `web-scrape` `web-rule-editor` `web-crawl` —— **整体平移入本模块**（功能与交互零变化，仅换组与换目录）。
> 页 Key 与标题一律 ⛔ 不改。

| 菜单 Key | 标题 | Order | View | 说明 |
|---|---|---|---|---|
| `script-library` | 脚本库 | 1 | `ScriptLibraryView` | 系统设置项与常用脚本：运行 / 增删改查 / 收藏 / 扫描候选 / 「可采集」列 + 「仅可采集」筛选 |
| `script-collections` | **脚本集合** | 4 | `CollectionView` | 采集器容器：单栏成员表 + 工具条「集合 / 分组 / 类别」三个下拉 + 「📤 发送到信息面板」 |
| `web-scrape` | **抓取规则**（NT-084 改名，Key ⛔ 不改；**NT-089 自 `web` 组迁入本组**） | 5 | `RuleCatalogView` / `RuleCatalogViewModel` | **规则目录**：规则集（`.py`）的增删改 / 启用 / 重读；列表带**分类 / 说明**列 + 工具条「分类 ▾」筛选 + **「预设 ▾」从预设创建** + **「🔍 探测」站点探测**；`✏ 编辑规则` → 跳编辑器 |
| `web-rule-editor` | **调试集合**（NT-086 批 C 改名；**NT-089 自 `web` 组迁入本组**） | 6 | `RuleEditorView` / `RuleEditorViewModel` | 主显示区自绘两列：**五段槽位 + 试跑推荐**（左）/ HTML 源码（中）+ **右侧详情列**（选中段脚本的表达式检验，Shell 详情列承载 / 工具条 ▤ 开合）；装配结果存 `flows\<名>.json`（解析全在 Python 侧） |
| `web-crawl` | **运行结果**（NT-090 改名，原「爬虫面板」；NT-086 批 D + NT-087 期②；**NT-089 自 `web` 组迁入本组**） | 7 | `CrawlPanelView` / `CrawlPanelViewModel` | 跑抓取脚本看目录条目：动态列 + 双击下钻 + 复制诊断 + 试下载 / 批量下载 + ④ 运行记录 |
| `script-info` | 信息面板 | 99 | `ScriptInfoView` | 按集合分节展示采集器输出；顶部唯一 `集合 ▾` |
| `settings-script` | 脚本管理器设置 | 100 | `SettingsScriptView` | 脚本目录（唯一写入点） |

- 页 Key 一经发布 **⛔ 不改**（`columnlayout.json` / 收藏 / 标签偏好按 Key 寻址）。

## 2.5 操作逻辑（交互契约）

> **列表本体**：表格型列表页 = `<v:TableViewHost>`（原生表格）。点表头排序、拖列边界调列宽（会话内有效）；**表头右键只管列宽与列显隐，行右键只管功能；筛选统一走顶区 `FilterBar` 筛选条**。详见 `../ui.md` §6.3c。

> 每页一张表：**操作 → 触发 → 结果 / 状态变化 → 约束**。改页面必须保住这些行为；跨模块共用的语义（右键 vs 工具条 / 确认判据 / 写入时机 / 状态反馈）见 `../ui.md` §10。

### 收藏夹 / 命令库 —— 🔴 NT-091 已迁出本模块

> 两页随「系统工具」模块（`Module.SystemTools`，Order 25）成立而迁出：**页键不改**
> （`script-favorites` / `script-commands` 落 `columnlayout.json`，改了用户列偏好失效），
> View / VM 在 `Module.SystemTools`（精简重写，⛔ 不复刻本模块 1559 行的列表基类）。
> ⇒ NT-096 起**软件包页也迁出**（→ `Module.SystemTools`，Key `script-packages` 不改）⇒
> 本模块只剩 脚本库 / 集合 / 信息面板 / 抓取三页 / 设置。软件包的交互契约见 `system-tools.md` §2.5。
> 🔴 数据**未自动迁移**：脚本侧 `role=command` 条目仍在 `config\script\` 库里（无展示入口，⛔ 未删 ⇒ 可逆）。

### 脚本集合 `script-collections`（单栏 + 工具条筛选）

主显示区 = 成员表一块 + 右侧详情列（`ui.md` §13 单层骨架，⛔ 没有左栏）：集合选择 / 分组「全部 / 置顶 / 最近」/ 类别三个下拉全在工具条（🔴 **两个筛选框默认选中「全部」**；集合下拉每项显示「名称 ★ · 在用」）；工具条 `➕ 新建集合` / `⧉ 复制` / `🗑 删除` / `📦 归档` / `🔍 扫描目录` / `📤 发送到对应页面` / `📤 导出` → 三个下拉 → 搜索成员 → `显示不可采集`。

> 🔴 v3.81（NT-082）：集合类别**四档** —— 信息面板 / 脚本库 / 命令库 / **软件包**。
> 「软件包」不是「用集合冒充 winget 查询结果」，而是**包清单容器**：成员 = `role=package` 条目（`Target` = 包 Id），
> 发送到软件包页后按清单显示安装状态。定位是**跨包管理器的架构预设**（将来 apt / flatpak / snap 各有后端，集合按后端归类，
> 页面再把成员命令绑成按钮）。`role=package` 条目只在「软件包」类集合的候选池里出现，⛔ 不混进其他集合。
> （v3.79 曾整体删除该档，理由是「软件包是实时查询结果」；本次按用户要求恢复为清单容器，逻辑 = 实时查询走搜索、手工清单走集合。）
> 🔴 **NT-096 起**：软件包页迁到 `system`，页内「集合 ▾」与行右键「📥 加入集合」**已删除**（模块禁互引）。
> ⇒ 现有「软件包」类集合仍能在集合页看到 / 编辑 / 「📤 发送到软件包」（Key 不变，能跳过去），但没有新的加入入口。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建集合 | ➕ 新建集合 | 建一个空集合，接着在右栏选中条目右键加入 | — |
| 复制 | ⧉ 复制 | 复制集合定义（**含成员 + 视图 + 每节列显示方式**） | 视图不复制 ⇒ 副本会退回全默认，故必须一起复制 |
| 删除 | 🗑 删除 | 删集合定义 | 🔴 **只有「在用」的集合不能删**（用户 2026-10-19）；默认库恒「在用」⇒ 自然受保护，⛔ 不再另按 `Builtin` 拦；二次确认「只删集合定义，不删条目」 |
| 归档 | 📦 归档 | 归档后默认不显示，排序沉底 | 仍可见（从「全部」里能取消归档） |
| 扫描目录 | 🔍 扫描目录 | 新脚本入库并**加入当前集合** | 与本页「⛔ 不自动登记」一致：用户手动触发 |
| 发送到对应页面 | 工具条 `📤 发送到对应页面`（按钮文案按选中集合类别动态显示：信息面板 / 脚本库 / 命令库 / 软件包，未选中时落「信息面板」） | 按集合类别跳目标页并选中该集合：信息面板→`script-info` / 脚本库→`script-library` / 命令库→`script-commands` / 软件包→`script-packages` | 写 `lastCollectionIdByKind.<kind>` 后 `NavigateTo`，目标页构造时读回；`PageKey` 与 `ScriptCollectionKind` 同源（`ScriptCollectionKind.PageKey`），⛔ 别处另写 switch；按钮文案 = VM `SendToPageMenuText`，随 `SelectedCollection` 变（`OnSelectedCollectionChanged` 发通知） |
| 成员增减 | 右栏**选中行 + 右键「↩ 加入 / 移出本集合」**（「状态」列显示 ✔ 已加入 / 未加入） | **立即写** `config\script\script.json` | ⛔ 无保存按钮；不可采集项灰显 `CanToggle=false`，悬停给原因 |
| 显示不可采集 | 工具条开关，默认关 | 打开后列不可采集项（灰显不可勾） | — |
| 显示全部类型 | 工具条开关，默认关 | 候选池默认按**当前集合的 kind** 过滤（脚本库集合只列脚本、命令库只列命令、软件包集合只列 `role=package`、信息面板集合不限）；打开后跨类可见 | NT-073 D13；`role=package` 与「软件包」集合是**一对一容器关系**，不受此开关影响 |
| 改脚本根目录 | 详情卡「脚本根目录」（仅脚本库类集合） | 失焦落盘 `rootPath`；空 = 全局脚本目录 | 相对路径按它解析；命令库 ⛔ 不显示该字段 |
| 改类别 | 详情卡「类别」下拉（信息面板 / 脚本库 / 命令库 / **软件包**四选一） | 落盘 `kind`，决定集合归哪个页面 | 🔴 走**字符串值通道**（`EditKind` + `OptionValuePath=Value`），⛔ 不绑选项对象（否则永远显示第一项） | 内置库也可改（改走后下次启动自动补回该 kind 的默认库）；改 kind 后刷新 / 视图字段可用性随之变；🔴 文案与左栏徽标 / 筛选下拉三处一致 |
| 选中集合 | 工具条「集合」下拉选一项 | 右栏成员表 + 详情卡随之更新 | — |
| 使用中标记 | 集合下拉项名称后缀「· 在用」（原左栏徽标） | 各页面（信息面板 / 脚本库 / 命令库）当前选中的集合（`lastCollectionIdByKind`）+ **默认库恒为「在用」**（兜底接收无归属条目） | 「在用」= 唯一不可删条件；`kind=info` 的「在用」排最后（NT-072 防误删），默认库 / script / command 不沉底；进入本页时重算（`CollectionView.OnAttachedToVisualTree` → `RefreshUsageState`） |
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


### 抓取规则 `web-scrape`（规则目录，NT-084 批 A/B/D + NT-086 期①批 A/B/E；**NT-089 自 `web` 组迁入本组**）

> 规则载体 = `<DataDir>\script\rules\<名>.py`（NT-089 起；原 `<DataDir>\web-tools\rules\`）。
> 🔴 **C# ⛔ 不解析规则**：命中数 / 预览全部由 Python 算（`RuleRunService` → `IPythonRuntime`）。
> 🔴 **脚本头只读、⛔ 不驱动行为**（NT-086）：分类只做展示分组与筛选；按钮可用性由 `role` / 槽位决定（调试集合，期①后续批）。头键全集 / 方言 / 段间契约 = 需求规格 §6.3.1。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建规则集 | `➕ 新建规则集` | 建一个 `.py` 骨架并进编辑器 | 骨架带脚本头键说明块（`——` 体，⛔ 不用英文冒号，见 WebTools §5.10） |
| **从预设创建** | 工具条「预设 ▾」选中一个预设 | 复制预设文件成新规则集（⛔ 不覆盖同名，重名加序号），探测网址已填则写入 `# base:` | 选中即创建，创建后下拉自动复位到「（选预设创建）」；**预设只播种一次** ⇒ 用户删了不复活，此时给可读 ⚠ |
| **站点探测** | 工具条「站点网址…」框（回车）/ `🔍 探测` | 对站点根试各预设的探测路径（`/feed` `/rss.xml` `/atom.xml` `/sitemap.xml` `/wp-json/wp/v2/posts?per_page=1` `/api.php?…` `/opds`），抓下来用预设解析 | 🔴 **以「能解析出条目」为准，⛔ 不看 HTTP 200**；🔴 **只推荐不代选** —— 结果与推荐写状态栏，创建仍由「预设 ▾」显式触发；抓取并行、评估一次进程 |
| 按分类筛选 | 工具条「分类 ▾」 | 只显示该分类的规则集 | 分类值来自各文件 `# category:`，**按实际出现的值动态生成**（⛔ 不硬编码五档）；已知五档显示中文标签，未知值原样；「全部分类」= 不限 |
| 启用 / 停用 | `✓ 切换启用` | 落目录清单的启用标记 | 停用只影响目录开关，⛔ 不删文件 |
| 看运行记录 | 右键 / 详情卡 `📂 运行记录` | 打开 `runs\<名>\`（资源管理器） | 每脚本保留最近 10 次（`RunRecordService.KeepRuns`）；没跑过给「去调试集合跑一段」的指引 |
| 导出运行记录 | 右键 `📤 导出 CSV` / `📤 导出 JSON` | 另存为对话框 → 写用户选的位置 | 取**最近一次**「▶ 跑这段」的记录；CSV = 元信息 + 字段汇总 + 前 3 条预览（带 BOM，Excel 认中文）；JSON = `result.json` 原样拷贝；没跑过 ⇒ 菜单项置灰（`HasRun`） |
| 编辑规则 | 双击行 / 右键 `✏ 编辑规则` | 经 `WebTransferService` 一次性参数跳 `web-rule-editor` | 参数一次性消费（`TakeRuleSet`） |
| 重读规则 | `🔄 重读规则` | 重扫规则目录，**⛔ 不运行任何规则集、不联网** | 重读会重建「分类 ▾」数据源 |
| 打开文件 / 目录 | 右键 `📄 打开文件` / `📂 打开目录` | 用系统关联 / 资源管理器打开 | — |
| 删除 | 右键 `🕑 删除` | 删规则集文件 | 二次确认 |
| 看脚本头配置 | 详情卡 | 显示 说明 / 分类 / 角色 / 适用站点 / 最近运行 / 路径 | 空值显示「—」；`role` 缺省按 `pipeline` |
| 脏值提示 | （自动） | 状态栏 `⚠` 列出回落缺省的头键（最多 2 条 + 计数） | 数字键脏值（非数字 / 负数）回落缺省，⛔ 不阻断加载 |

### 调试集合 `web-rule-editor`（NT-086 批 C；**NT-089 自 `web` 组迁入本组**）

范式：**脚本从「一个 `.py` = 一个站点」降为「一个 `.py` = 一段」** ⇒ 本页把段装配成流水线（flow），跑通后交给面板批量下载（期②）。**本页 ⛔ 不做批量下载**。

布局 = **工具条（Shell）/ 主显示区 / 详情卡（Shell 右详情列 300px）**：主显示区两列 = 左**起始网址 + 五段槽位 + 试跑推荐**（244px，可拖分栏）/ 中**HTML 源码**（`HtmlText`，上限 40 万字符 / 8000 行）；右详情列 = **选中段脚本的表达式检验 + 错误回显 + 命令编辑**（`RuleInspectorView`，页面 VM 实现 `IDetailPanelProvider`，开合由工具条右上角 ▤ 控制、状态落 `columnlayout.json`）。🔴 详情列**整卡只有一个 `ScrollViewer`**（`Padding="0,0,14,0"` + 内容 `Margin="8,8,4,8"`，与 `DetailCardHost` 同口径）：内层再套一个滚动条会落在卡片中间、被详情列 `Border` 裁掉，看着位置错且抓不住 ⇒ 命令表用 `MaxHeight` 固定高度，滚动只走外层。

🔴 **样式统一**：工具条走 `IToolbarSource` 描述符（Shell `ToolbarHost` 统一渲染，⛔ 页内不写 `Border.toolbar`）；状态文本走 `IStatusProvider`（Shell 底部状态栏）；命令列表 = `Border.list-header` + `ColumnGrid` + `ListBox.list-page`（与目录页同规格）。

**五段**：`entry`（入口/搜索）→ `pick`（条目筛选）→ `toc`（目录）→ `content`（正文/资源）→ `save`（**内置**下载器）。槽位**一律可选、可留空**（`toc` 留空 = **链式模式**）；下拉候选按脚本头 `# role:` 过滤（无 role 的 `pipeline` 只进 `entry` 槽位）。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 抓页 | 工具条 `🌐 抓页` | 抓起始网址 → 落**共享** HTML `cache\_debug\page.html` | 🔴 试跑推荐的前提是「候选脚本对**同一份** HTML 求值」，⛔ 各抓各的 |
| 试跑推荐 | 工具条 `🔍 试跑推荐` | 当前槽位的**全部候选**对同一份 HTML 求值 → 按「条数 > 0 且字段非空率最高」排序 | **一次 Python 进程跑完所有候选**（⛔ N 个脚本启动 N 次解释器）；**只推荐不代选**，用户点 `✅ 用它` 才写进槽位 |
| 跑这段 | 工具条 `▶ 跑这段` | 跑当前槽位选中的脚本 → 命中数 / 预览 / **错误回显**；🔴 同时落一条**运行记录**（`runs\<脚本>\<时间戳>_<URL哈希>\`，NT-086 批 G） | 🔴 段间契约：入口 / 中间段输出**必须含 `url`**，缺了状态栏显式报「第 N 段输出没有 url 字段」⛔ 不静默；记录失败 ⛔ 不打断运行 |
| 选段 / 换脚本 | 左栏点槽位行 / 槽内下拉 | 右侧详情列切到该段脚本的 `RULES` 命令表 | 下拉首项「（跳过）」；`save` 是内置段 ⛔ 不可选 |
| 保存 / 载入流水线 | 工具条 `流水线 ▾` + 名字框 + `💾 保存流水线` / `🕑 删流水线` | 写 / 读 `flows\<名>.json`（五段槽位 + 起始网址） | ⛔ 不开 DB 表；载入时选中的 flow 名缺失脚本会标「（缺脚本）」 |
| 表达式检验 | 右侧详情列命令表（字段名 / 类型 / 表达式 / 命中） | 选中行看预览与**错误**（`⚠` 前缀） | 🔴 Python 的 error / traceback **必须回显**，⛔ 禁止静默显示「0 条」 |
| 改命令 | 右侧详情列「＋ 新建」/「删除」/「保存」 | 写回该脚本 `.py` 的 `RULES` 块 | 主表 `_allCommands` 与视图 `Commands` 分离：**保存永远写主表**（⛔ 不把过滤藏起来的命令存丢）；⛔ 无自动保存 |
| 复制诊断 | 右侧详情列「复制诊断」 | 段位 / 脚本 / 起始网址 / HTML 长度 / 试跑结果 / 每字段 count + error 打包进剪贴板 | 给 AI 复盘用；🔴 错误与 traceback 必含 |
| 在源码里查找 | 中栏查找框（回车 = 下一个）/`◀` `▶` | 命中 = **选区高亮**（系统色），显示「第 n/m 个」，到头回绕 | ⛔ 不自绘高亮层：只读 `TextBox` 无着色 API；设选区必须**先缩 End 再设 Start 再设 End**（否则 Avalonia clamp 导致跳位错） |
| 定位命中 | `🎯 定位命中` | 把选中命令的首条结果搬进查找框并跳过去 | 结果经过净化改写时可能找不到，会如实提示 |
| 返回目录 | 工具条 `◀ 返回目录` | 跳 `web-scrape` | — |

### 运行结果 `web-crawl`（NT-090 改名；NT-086 批 D 期① + NT-087 期②；**NT-089 自 `web` 组迁入本组**）

> 范式：**AI 写脚本 → App 只做运行 / 诊断 / 展示 / 下载**。
> 脚本只负责「**一页能取出什么**」；🔴 **下载器内置**（并发 / 限速 / 去重 / 续跑 / 取消），⛔ 不脚本化。

形态：工具条「入口脚本 ▾」+「内容脚本 ▾」+「流水线 ▾」+ 起始网址框（回车 = 抓链接）+ `🔗 抓链接` / `🧪 试下载` / `⬇ 下载` / `⏹ 取消` / `📋 复制诊断`；
主体四节（节头仿信息面板：状态点 / 标题 / 计数 / ⟳ / 折叠）：① 链接·目录（**单击行 = 勾选，行底色即状态**）② 试下载预览 ③ 下载进度 ④ 运行记录；底栏摘要 = 条数 · 字段数 · 错误数 · 耗时（下载后改写为下载汇总）。

🔴 **④ 运行记录**（NT-090）= 当前「入口脚本 ▾」选中脚本的最近 10 次 run（新→旧；时间 / 起始网址 / `列数 · 条数 · ⚠错误数`）：双击 = 打开那次运行的目录，右键 `📂 打开目录` / `📄 打开日志`（`diag.txt`）/ `📋 复制诊断`；节头 ⟳ 只重读磁盘（⛔ 不重跑脚本）。换脚本 / 每次跑完自动刷新。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 抓链接 | 工具条 `🔗 抓链接` / 网址框回车 | 抓起始网址（落 `cache\_crawl\page.html`）→ 跑选中脚本 → 目录节出行表 + 底栏摘要 + **落一条运行记录** | 🔴 行表 = 按字段独立求值后**按索引合成**（`mode=rows`）；缺字段补空串不错位；上限 1000 行 |
| 下钻 | 双击目录行 | 用这一行的 `url` 当新起始网址重跑 | 行没有 `url` 列 ⇒ 状态栏提示；用户驱动（⛔ 不自动无限钻） |
| 排序 | 点表头 | 按该列排序（再点反向；数字列按数值） | 客户端排序，箭头进表头文案 |
| 全选 | 行右键「☑ 全选 / 取消勾选」 | 勾 / 不勾全部目录行 | 行是否勾上 = 行底色（单击行切换；双击是下钻） |
| 🧪 试下载 | 工具条 / 行右键 | 对一行跑 `content`+`next` → ② 节出 形态 / 文件清单 / 正文前 600 字 / 页数 / 耗时 | ⛔ **不落最终产物**（临时副本在 `trials\`）；② 节头 ⟳ = 对同一条再跑一次 |
| ⬇ 下载 | 工具条 | 跑**点过的行** → 落 `downloads\<脚本>\<条目>\`；③ 节逐行 ✓/↷/✗ + 进度条 | 一行都不勾 = **链式模式**（见下）；并发读 `# concurrency`（钳 1..8） |
| ⏹ 取消 | 工具条 | `CancellationToken` 取消 | 已下完的文件保留 |
| 复制诊断 | 工具条 / 行右键 `📋 复制诊断` | 脚本 / 网址 / HTML 大小 / 每列命中与错误 / 前 5 行 + 试下载诊断进剪贴板 | 给 AI 复盘使用；错误列 ⛔ 不静默 |

**产物形态优先级链**（🔴 下载行为由 `content` 返回形态决定，⛔ 不由分类直接决定）：
`content` 返回 JSON（`text` / `files` / `rows`）＞ `# artifact:` ＞ 分类缺省猜测（images/media/video/audio）＞ 缺省文本。
`rows` = 容器页 ⇒ 子条目入队递归跟进，**容器本身 ⛔ 不下盘**。

**分页原语 `next`**：挂在各段上的参数（文本锚 / URL 模式）；三保险 = URL 去重 + `# max-pages`（缺省 50）+ 连续 2 页空即停。
**链式模式**（流水线 `toc` 槽位留空 / 一行都不勾）：从起始网址起 `content`+`next` 逐页跟进，目录**边跑边累积**（① 节空时才灌，⛔ 不冲掉抓链接的结果）。
**失败策略**：单条失败不停批；连续失败 3 次停批并报原因；目标目录已有文件 ⇒ 跳过（可续跑）。
**备份**：`downloads\` / `trials\` / `cache\` 都是可再生产物 ⇒ ⛔ 不进备份（规则目录仍是唯一默认勾）。

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
| 展开 / 收起 | 节体「⌄ 展开（N 行）/ ⌃ 收起」 | 预览 20 行 → 显示全量 | 节数上限 40 |
| 列排序 | 点列头 | 该节内排序 | 节与节**列宽独立** |
| 分页 | — | 分节模式**关分页**，底栏改摘要（耗时 · 节数 · 总行数） | — |
| 空态 | — | 两种：没有集合 / 集合里没采集器 | 主动作按钮给去处 |

### 脚本管理器设置 `settings-script`

> 🔴 NT-071：本页改用**共用设置页骨架**（`NewToys.UI/Controls/SettingsSectionHost`，`Sticky="False"`）—— 2 个节 = 折叠卡片
> （脚本目录 / 设置文件），点抬头开收，默认全展开。节内 ⛔ 再写 `Border.card`。细则见 `ui.md` §11。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 改脚本目录 | 输入框失焦 | 校验通过即落盘 | **⛔ 无保存按钮**（即时生效）；非法 ⇒ 底栏红字 + ⛔ 不写盘 |
| 选择目录 | 📂 选择目录 | 弹文件夹选择器 → 写入 | — |
| 恢复默认 | 恢复默认 | 清空用户值 ⇒ `<应用根>\scripts` | — |
| ~~软件源（v3.80）~~ | 🔴 **已迁出**：v3.80 起只在 v3.62~NT-096 期间挂着只读命令清单；NT-096 随软件包页整体移到系统工具，**同日用户返工把该节彻底删除**（系统工具设置页也不再列） | 现在改源 = 直接敲 `winget source …`；本模块 ⛔ 无源相关 UI | — |
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
| **`RuleFileService`**（NT-089 自 WebTools 迁入） | 规则 `.py` 的读写 / 命名清洗 / 目录：`<DataDir>\script\rules`（NT-089 前为 `<DataDir>\web-tools\rules`）；脚本头解析（标量键 + `ua`/`header` 多行键 + 数字键脏值回落与警告）+ 预设（`PresetPath` / `CreateFromPreset` 复制预设 + 写 `# base:`） |
| **`RuleCatalogService`** 〃 | 规则集目录清单：扫 `rules\*.py` 读 `SOURCE` 头；`MarkRun` 回写「最近运行」元数据（落 `web_docs` 的 `rule-meta`，分区键沿用 `web`） |
| **`RuleRunService`** 〃 | 抓 HTML 落缓存 + 调 Python 跑 `RULES` / 列命令；支持指定 HTML 跑指定脚本 + `mode=probe` 试跑推荐 + `ProbeSiteAsync` 站点探测 + `mode=rows` 行表合成 + `mode=step` 单段求值 |
| **`RunRecordService`** 〃 | 运行记录 `runs\<脚本>\<ts>_<URL哈希8>\` = `result.json` + `page.html` + `diag.txt`；每脚本留最近 10 条；`ToCsv` 导出；`History(script)` = 最近 N 条（NT-090 ④ 节用） |
| **`DownloadService`** 〃 | **内置下载器**（⛔ 下载行为不脚本化）：并发 / 间隔 / 去重 / 续跑 / 连续失败停批 / 可取消；`ResolveArtifacts` = 产物形态优先级链 |
| **`FlowService`** 〃 | 流水线（flow）读写：`<DataDir>\script\flows\<名>.json`（⛔ 不开 DB 表；NT-090 自 `web-tools\flows` 归位 + 一次性搬迁） |
| **`RulesBackupSection`** 〃 | `IBackupSection`（Id `data.web-rules`，Order 91）—— 只备 `rules\`，⛔ 不备 `cache\`；🔴 **唯一默认勾的数据节** |
| **`WebTransferService`**（NT-089 上提 `NewToys.UI`） | 跨页一次性传参（书签 / Ping（网络工具）↔ 抓取（本模块））；两侧各注册一次单例（同一容器 ⇒ 同一实例） |

- VM 侧：`QuickActionListViewModel` 现**只服务 `script-library`**（`script-commands` 已随 NT-091 迁出；系统工具那边是精简重写版）。
  - NT-089 S1：新增 `RuleCatalogViewModel` / `RuleEditorViewModel` / `CrawlPanelViewModel` 三页 VM（抓取能力随迁，功能零变化）。
  - 🔴 抓取默认项（UA / 超时）**仍由「网络工具设置」页写入 `config\web-tools.json`**（菜单 Key ⛔ 不改）；本模块经 Core 的 `IOptionsStore` **只读**同一份文件（NT-094 设置拆分后归位）。
- 🔴 NT-085（2026-10-20）：动态列控件**已不在本模块** —— `Controls/SysInfoGrid` 连同单元格模型上提 `src/UI/Controls/DynamicColumnGrid.cs`（类改名 `DynamicColumnGrid`，模型 `SysInfoCell` / `SysInfoHeaderCell` 随迁，命名空间 `NewToys.UI.Controls`）。本模块 `ScriptInfoView.axaml` 用 `xmlns:v="using:NewToys.UI.Controls"` + `v:DynamicColumnGrid`；`InfoSectionViewModel` / `InfoRowFactory` 加 `using NewToys.UI.Controls`。⛔ 别再把控件搬回来。

### 3.3 数据与配置

| 位置 | 内容 | 备份 |
|---|---|---|
| `config\modules\script-manager.json` | 脚本目录（`IOptionsStore` 分区，NT-077 第二批归位） | ✅ |
| `config\modules\package.json` | 软件包查询偏好 | ✅ |
| `store\script\script.db` 的 `script_docs` | 手工数据：集合 / 条目（脚本·命令·采集器·软件包）/ 标签 / 视图 / `lastCollectionId`（整份 JSON 一条） | ✅ 随库 |
| `store\script\script.db` 的 `pc_observations` / `pc_runs` | 采集观测 + 运行日志 | ✅ 随库 |
| `<脚本目录>\sysinfo\*.ps1` | 内置采集脚本（播种） | — |
| `<脚本目录>\examples\*` | 随包示例脚本（`.ps1` / `.py` / `.bat` + 采集器模板）+ `.seeded` 一次性标记 | — |
| `<脚本目录>\env\*` | 随包环境脚本（`setup-python-env.ps1` + `python-requirements.txt`）+ `.seeded` 一次性标记（NT-083） | — |
| `%LOCALAPPDATA%\NewToys\tools\py\` | Python 共享环境：`venv\` + `py_worker.py`（NT-083，与 `env.json` 同级；⛔ 不进 `profiles\`） | — |
| `<DataDir>\script\rules\*.py` | **抓取规则集**（NT-089 自 `web-tools\rules` 迁入） | ✅ |
| `<DataDir>\script\cache\` | 抓取 HTML 缓存（含 `_debug` / `_crawl` / `_probe` / `_dl`） | ⛔ 可重建 |
| `<DataDir>\script\downloads\` / `trials\` | 下载产物 / 试下载临时件 | ⛔ 可重建 |
| `<DataDir>\script\runs\` / `flows\` | 运行记录 / 流水线定义（NT-090 自 `web-tools\` 归位） | ⛔ 可重建 / — |

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
| 采集呈现 | `Services/CollectorService.cs` / `InfoRowFactory.cs` / `ViewModels/InfoSectionViewModel.cs`；动态列表格已上提控件库 `src/UI/Controls/DynamicColumnGrid.cs`（XAML 用 `v:DynamicColumnGrid`，⛔ 本模块无 `Controls/`） |
| 软件包 | ⛔ **不在本模块**：NT-096 起整套在 `Module.SystemTools`（`PackageManagerService` / `PackageOutputParser` / `PackageSettingsService` / `PackageView`）；本模块只剩 `ScriptLibraryPackageService.cs`（脚本库导出 / 导入 zip） |
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
8. `winget` 表头**本地化**（中文 Windows 打「名称/ID/版本/可用」）⇒ 列标签按「英文 + 中文别名」映射到规范名再切片。
9. `winget show` 的「发布服务器 URL:」会被当成上一条多行值的续行 ⇒ 不认识的 `标签:` 必须显式**终结**上一个字段并丢弃。
10. 已安装列表少一大截 = `IdLike` 正则不认 `\` ⇒ 加 `\\`（`^[A-Za-z0-9][A-Za-z0-9._+\\-]*$`）。
11. 🔴🔴 **VM 构造函数里「先设下拉选中项、后建节字段」= 整页空白**（2026-11-01 实测，`CrawlPanelViewModel`）：`SelectedScript` / `SelectedFlow` 这类 setter 会立刻链到 `RefreshHistory()`，而 `_links/_preview/_progress/_history` 是 `null!` 字段、要到后面才赋值 ⇒ `NullReferenceException` ⇒ `ShellViewModel.ResolveViewModel` **吞掉异常返 null** ⇒ `DataContext = null`，页面只剩空壳、**零报错**。凡 `null!` 字段会被 setter 链间接读到，赋值顺序就必须排在所有 setter 触发之前；排查入口 = 先确认页面 DataContext 是否为 null（该处异常目前不落任何提示）。
11. 列宽拖拽**未接**（`DynamicColumnGrid` 不支持，NT-085 起控件住 `src/UI`）；改宽走「⚙ 列设置」写回 manifest。
12. 分节模式**关分页**（底栏改摘要），因为一次只显示一个集合的多个采集器节。
13. 🔴 **示例 vs 内置采集器是两套语义**：`ExampleSeeder` 只播种一次（`examples\.seeded` 存在即整体跳过，删掉的示例**不复活**）；`BuiltinCollectorSeeder` 缺了就补回。改 `defaults\scripts\*` 后老配置目录不会自动更新（要用户删 `examples\` 目录再启动）。
14. 🔴 **解析分层纪律（NT-081）**：固定列宽表格的通用机械只在 `FixedWidthTable`（🔴 按**显示宽度**切，汉字 2 列）；winget 的续行合并 / 别名表 / pin 兜底是**固有启发式**⛔ 不进通用层；机读后端（dpkg-query）⛔ 不走表格解析，直接 `TsvRows`。新增后端 = 新适配器 + 命令模板，页面 / VM 零改动。
15. 🔴 **进程执行只走 `IProcessRunner`**（NT-081）：散落的 `Process.Start` 分三类，⛔ 不一刀切合并 —— ① 捕获式外部工具（脚本 / 命令 / winget）⇒ `IProcessRunner`；② 平台能力（打开文件夹 / 文件 / URL，`UseShellExecute=true`）⇒ VM 直接用 BCL；③ 可见控制台 / 提权（`Verb=runas`）⇒ `QuickActionService.StartVisible`（winget 源命令走命令库 + UAC，NT-080 定调）。
16. `.bat` 示例**只写 ASCII** + 首行 `chcp 65001` —— 带中文的 .bat 在 936 代码页下必乱码，且 BOM 会被 cmd 当命令字符。
17. 🔴 v3.80：**源在 UI 里没有入口了**。源选择框 / 源开关 / 分类 / 互斥 / 定义快照全删
    （`PackageSourceViewModel`、`PackageSourceRow`、`PackageSettings.DefaultSource|SourceCategories|SourceDefinitions`、
    `PackageManagerService.RunElevatedAsync` 一并删除）—— 源操作 = 命令库里的命令条目；设置页只剩「命令清单 + ＋ 加入命令库」
    （写入 `script.json` 的集合「winget 源」，按标题幂等去重）。
18. NT-074：软件包服务下放到模块后，**Shell 不能再给它接确认钩子**（Release 下 Shell 不引模块）⇒
    二次确认改走 `Core.DialogHooks.ConfirmAsync`（原 `PackageManagerService.ConfirmHandler` 已删）。
19. 🔴🔴 v3.80 实测 + 用户定调：**winget 1.29 没有 `source enable` / `source disable`**
    （`winget source --help` 只有 add/list/update/remove/edit/reset/export）⇒ 「停用源」只能 `source remove`，
    要能加回来必须先 `source export` 记定义 —— **这就是源 UI 被砍掉的原因**：与其在 App 里模拟一套开关状态，
    不如把 `source add/remove/reset` 交给命令库（条目的 `NeedAdmin=true` ⇒ `runas` 提权 + 可见控制台）。
    ⛔ 装 / 卸 / pin 包不提权（用户级安装不需要）。
20. 🔴 v3.81（NT-082）：`role=package` 条目 ↔ 「软件包」类集合是**一对一容器** ——
    候选池判定写成 `isPackageItem != isPackageCol ⇒ continue`；⛔ 别只把 v3.79 那句无条件 `continue` 删掉
    （那样包条目会污染脚本库 / 命令库候选池）。软件包页的清单状态只取自**一次 `winget list` 缓存**，
    ⛔ 不逐包跑 `winget show`（慢、且会触发源元数据更新）。
21. 🔴 NT-075：`winget pin list` 的**版本号长得跟包 Id 一样**（都有点号）⇒ 老实现"扫所有带点号的词"会把
    `24.08` 当成一个包 Id 混进列表。现在走 `ParsePins`（表头切片取 `Id` + `Version`），兜底正则按形状排除纯数字版本。
22. 🔴 NT-059：`ApplyView()` 里 `Items.Clear()` 会让 ListBox 的 `SelectedItem` 双向绑定**回写成 null** ⇒ 点收藏 / 切分类后详情卡掉回示例态。
    修法：刷新统一走 `ApplyViewKeepSelection()`（刷新后把选中项放回去），收藏 / 分类切换都改走它。
23. 🔴 NT-059：`QuickActionService.RemoveAsync` 内部会弹确认 ⇒ 详情卡 🗑 与「🔥 从磁盘删除」两条路会**问两遍**，
    且第二遍取消时文件已删。修法：`IQuickActionService.RemoveSilent`（⛔ 不弹），确认框由调用方自己弹。
24. NT-059：导入只有一个 `SplitButton`「📂 导入」（主钮 = 选文件夹，箭头 = 文件夹 / 文件两路）；落盘口径 = 目录内**相对文件名**、目录外**绝对路径**（`ToStoredScriptPath`）；导入后必须清搜索框 + 分类归「全部类型」+ 清表头筛选 + 选中第一条，否则"导入了看不到"。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 跨平台包管理（Debian/Ubuntu 的 apt 等）：**另开独立软件包页**（`IPackageManagerService` 保持中立，新增实现 + 新页面 + 新菜单 Key），⛔ 不在现有「软件包」页里切后端 | 📝 未立项 |

> 已验收淘汰的卡见 `../backlog/script-manager/`（≤5 张滑动窗口）。
