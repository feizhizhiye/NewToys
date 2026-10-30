# NewToys 框架层（Framework）

> v4.6 · 2026-10-30 · **设置页「滚动自动展开」整体移除** —— 一节开没开**只由点击决定**；`SettingsSection` 删 `UserCollapsed` / `CollapsedByAll`，三层优先级压回两层（点 / 外部寻址）。
> v4.5 · 2026-10-21 · **过时内容清理** —— §11 已完成项（NT-064/065/066/067/068/075/076/077/078/085）收拢为「已结清」一行，NT-085 状态按台账订正为已完成；版本记录按「只保留最近三次」压缩。
> v4.4 · 2026-10-19 · 新增 **§2.1 跨平台中立约束**（长期架构约束，本期不实施）—— 三层分工 / 包管理器取数分级（只有 winget 需要复杂解析） / 写操作 ⛔ 不脚本化 / 六条新增代码硬约束；同步更新 §10 B5、§11 登记 NT-081。
> **标准角色：工程规范（框架层部分）**。本文件 = **框架层（Shared / Core / Shell / NewToys.UI）的实现约定**，外加**跨模块待办**的唯一收口处。
> 原 `NewToys-工程规范.md` 已于 2026-10-18 拆分：**框架层 → 本文件**｜**UI 规范 → `ui.md`**｜**各模块 → `modules/<模块>.md`**。
> 权威源 = `src/` 源码；代码与文档冲突以源码为准并回写本文。
> 流程与术语唯一定义处 = `NewToys-AI协作手册.md`【研发流程】【术语对照】；本文件只写「怎么实现」。

## 目录

- §0 文档体系与更新纪律
- §1 编码规范（C#）
- §2 程序集与依赖边界（含 **§2.1 跨平台中立约束**）
- §3 启动顺序与模块加载
- §4 菜单注册与导航（含 **§4.6 应用级设置 / 首页 / 功能清单**）
- §5 页面解析与 VM 定位
- §6 契约（Shared 侧接口）
- §7 配置与路径（**§7.1 三层配置清单 · §7.3 单一数据总库**）
- §8 异常处理
- §9 构建与版本
- §10 技术债台账
- §11 跨模块待办
- §12 窗口外壳（Windows Win32 补丁层 · **§12.2 贴靠几何** · §12.1 补丁清单）
- 附录 A 高频编译错误速查

---

## §0 文档体系与更新纪律

### 0.1 文档地图（2026-10-18 重构）

| 文档 | 角色 | 内容 | 何时读 |
|---|---|---|---|
| `index.md` | 文档地图 | 模块清单表 + 全部文档索引 | **会话开始** |
| `NewToys-需求规格.md` | 需求规格（宪法） | 定位 / 边界 / 技术框架 / UI 框架 / 导航模型 / 功能域 | 涉及功能与边界 |
| **本文件 `framework.md`** | 工程规范 · 框架层 | 程序集边界 / 启动 / 菜单 / 加载 / 契约 / 配置 / 构建 / 技术债 / **跨模块待办** | 改 Shared/Core/Shell 或新建模块 |
| **`ui.md`** | 工程规范 · UI | 尺寸密度 / 主题键 / 图标 / CSD / 列表页 / 详情卡 / 工具条 | 改任何 `.axaml` |
| **`modules/<模块>.md`** | 模块说明 | 该模块的职责 / 当前功能 / **操作逻辑（§2.5）** / 实现逻辑 / 数据 / 坑点 / 待办 | **改哪个模块读哪个** |
| `NewToys-项目状态与路线图.md` | 工作项台账 | 基线 / 已完成 / Backlog 索引 | 会话开始读「一、当前基线」 |
| `backlog/<模块>/NT-xxx.md` | 任务卡 | 实施期工作稿 | 只有实施该卡才读 |
| `NewToys-AI协作手册.md` | 协作手册 | 研发流程 + 术语对照（🔴 唯一源） | 流程/术语有疑问 |
| `NewToys-软件说明书.md` | **使用层**（用户手册） | 用户视角：每页干什么 / 谁会用 / 典型场景 / 数据在哪（⛔ 无实现细节） | 问「怎么用」、写用法说明 |

**拆分原则**：一个功能模块一个 md；新增模块 = 新增 `modules/<模块>.md` + `backlog/<模块>/`。

### 0.2 更新节奏

| 事件 | 需求 | framework | ui | modules/<模块> | 台账 | 手册 |
|---|---|---|---|---|---|---|
| 定位 / 边界 / 框架级变化 | ✅改 | ✅改 | 视情 | 视情 | 同步 | 视情 |
| 实现方式变化 | 引用 | ✅改 | ✅改 | ✅改 | 同步 | — |
| **完成一个功能** | — | 视情 | 视情 | ✅**改**（功能/逻辑/坑） | ✅**更新** | — |
| 仅修报错 / 错别字 | — | ⛔ | ⛔ | ⛔ | ⛔ 不回写 | — |
| 协作流程变化 | — | — | — | — | — | ✅改 |

- **各文档版本号相互独立**，只有内容真变了才自增，⛔ 不做齐步递增。
- 🔒 **指导性文件不乱改**：仅为单个任务的措辞微调不擅改。
- **提交信息** `<type>: <简述>`，type ∈ {feat, fix, docs, refactor, chore, style}。
- ⛔ **评估 / 讨论结论不进文档**，只在会话里谈，用户拍板后才转为需求。

### 0.3 任务卡生命周期与滑动窗口（2026-10-18 修订）

- 卡放 `backlog/<模块>/`（跨模块卡放 `backlog/framework/`），**每目录最多 5 张**。
- **淘汰规则**：新增卡使目录超 5 张时，删除**最旧且已验收**的那张；`📝草稿 / ✅就绪 / 🔄执行中 / ✔已完成（待验收）` 的卡**不参与淘汰**。
- **验收即蒸馏**：卡验收后，把它的「结论 / 偏差 / 坑点 / 关键文件」写进 `modules/<模块>.md` 对应节，卡才可被淘汰。
- 被后续工作项整体取代 / 明确作废的卡 → 直接删除，台账行标 `🗑 已作废（卡已删）`。
- 台账行**始终保留**（只翻状态）。
- ⛔ 已废止：2026-10-14「全量保留」、2026-10-17「默认保留 + 作废删除」（被本条的滑动窗口取代）。

---

## §1 编码规范（C#）

- 文件作用域命名空间（`namespace X;`）；`using` 置顶，`System.*` 优先。
- 命名：公共成员 / 类型 / 方法 `PascalCase`；私有字段 / 局部 / 参数 `camelCase`（私有字段前缀 `_`）；异步方法 `Async` 结尾。
- 带 `[ObservableProperty]` / `[RelayCommand]` 的类**必须 `partial`**（否则 `CS0260`）。
- 属性优先 `[ObservableProperty]` 私有字段；命令 `[RelayCommand]`。
- 构造注入为主；`Ioc.Default` 仅在 code-behind / 回调等无法构造注入处使用。
- 集合 `ObservableCollection<T>`；公共 API 写 XML 注释；复杂逻辑注释「为什么」。
- ⚠️ 改 `[ObservableProperty]` 字段名 / 语义时全仓搜旧名（属性名 + `_字段名` + `nameof`）；构造里 `_xxx=…` 合法，别顺手修。
- `NewToys.Shared` / `NewToys.Core` **⛔ 不引 Avalonia**；模块 ⛔ 禁引 Shell、禁互引。

---

## §2 程序集与依赖边界

| 程序集 | 职责 | 依赖 |
|---|---|---|
| `NewToys.Shared` | 契约接口（`Services/`）/ 模型 POCO / 详情卡描述符 | 无（零 Avalonia） |
| `NewToys.Core` | 服务实现（配置 / 主题 / 菜单 / 加载 / 列布局 / winget / ffmpeg 封装） | Shared |
| `NewToys.UI` | **共享控件库**（NT-063） | Shared + Avalonia（⛔ 不引 Core / Shell） |
| `NewToys`（Shell，exe） | 启动 / DI / 主窗口 / 导航 / 应用级设置页 | Shared + Core + UI |
| `Module.<模块>` | 功能模块 | Shared + Core + UI（⛔ 不引 Shell） |

- 单向依赖：`Shared ← Core ← Shell ← Module.*`；`Shared ← UI`。
- **接口一律放 `Shared/Services/`**；POCO 放 `Shared/Models/`。
- 🔴 **`NewToys.UI` 纪律**：被所有模块引用 ⇒ 与 Shared/Core 同级 ⇒ **改一次 = 全模块重编**。只放**写完即冻结的稳定控件**；`App.axaml` 的 `*Brush` 主题刷子 ⛔ 不搬（条数以 `check.ps1` 白名单为准），UI 内 XAML 一律 `DynamicResource` 引用主题键。
- ⛔ 禁第三方 UI 库 / 图标字体。包版本只在 `src/Directory.Packages.props`。

### 2.1 跨平台中立约束（2026-10-19 · 长期约束；NT-081 步骤 1-2 已实施）

> 本期交付仍是 **Windows 10 LTSC**（需求 §3.7），Debian 13 属远期。本节**不是**「现在就做跨平台」，而是**新增代码的形状约束**：将来换平台时不用重写页面。
> 口径：**架构中立 ≠ 功能落地** —— 平台差异必须被隔离在适配层，⛔ 不允许「页面里判断平台」。

**三层分工（职责不交叉）**

| 层 | 内容 | 数量 | 约束 |
|---|---|---|---|
| 页面 / VM | 列、排序、勾选、详情卡、工具条、状态文案 | **一份**，后端无关 | ⛔ 不出现工具名（winget / apt / ffmpeg）、⛔ 不出现平台路径与分隔符 |
| 后端适配器 | 拼命令行、选解析器、探测可用性、提权方式 | 每后端一份（薄，~150 行） | 只实现 `Shared/Services` 接口；⛔ 被 VM 直接 new |
| 平台底座 | 进程启动 / 取消（进程树）/ 编码 / 路径 | 每 OS 一份 | 放 `Core`；⛔ 不进 `Shared` / `UI` |

**包管理器取数分级（🔴 只有 winget 需要复杂解析）**

| 后端 | 取数方式 | 解析成本 |
|---|---|---|
| winget（Windows） | 只能人读表格：本地化表头、列宽浮动、Id 被显示名截断 | **高** ⇒ 保留 C# 专用启发式 `PackageOutputParser`，⛔ 不重写成脚本 |
| dpkg / apt | `dpkg-query -W -f='${Package}\t${Version}\t${Status}\n'` | 极低（纯 TSV） |
| snap | `snap list` 固定列 | 低 |
| flatpak | `flatpak list --columns=name,application,version,origin` | 极低（纯分隔符） |

⇒ 结论（2026-10-19 定调）：
1. **保留** winget 的 C# 解析器（450 行启发式，验证成本远高于编写成本）；
2. 将来只需补**一份通用「TSV / JSON → 行模型」解析**（C#，约 100 行，永久不改）；
3. ⛔ **不设「脚本解析层」** —— 脚本解析换不来可移植性，反而引入编码 / locale / 协议漂移三类问题；新后端「能用机读输出」就够，不需要写解析器。

**写操作 ⛔ 不脚本化**
安装 / 升级 / 卸载必须留在 C#（后端接口 → 平台底座执行）。脚本化只省一次编译，代价是：取消（脚本子进程成孤儿，需 Job Object / 进程组）、提权（UAC vs `sudo` / `pkexec`）、交互确认卡死、失败原因只剩退出码、并发串行失控、编码、命令注入面、回滚清理。
🔴 **允许外置的只有「命令模板」**：`{exe}` / `{id}` / `{source}` 占位符 + C# 替换 —— 模板可配，执行不可配。

**新增代码硬约束（每张卡自检）**
1. 平台判定一律用**运行时信号** `OperatingSystem.Is*()`（需求 §3.7：⛔ `#if` / OS 嗅探 / `x:OnPlatform`）。
2. P/Invoke **只准**在 Core / Shell 的平台适配处，且**必须**带内联 `OperatingSystem.IsWindows()` 守卫（CA1416 只认这种写法）。
3. ⛔ VM / UI 直接 `Process.Start` 调外部工具 —— 一律经 `Shared/Services` 接口。
4. ⛔ 硬编路径分隔符 / `C:\` / `chcp` / `pwsh` 绝对路径；读写流一律显式指定编码。
5. ⛔ 把 `Registry` / `shell32` / `user32` 等 Windows-only API 写进 `Shared`（Shared 零平台依赖，见 §2）。
6. 新增后端或新平台能力时**先问「属于三层里的哪一层」**：落不进适配层的设计 = 架构债。

**现状债（明细登记 §10 B5）**
- ✔ RID 已**条件化**（NT-081 步骤 1）：默认仍 `win-x64`，`-p:RuntimeIdentifier=` / `build.ps1 -Runtime` 可接管。
- ✔ Shell 窗口层 Win32 调用（`SetWindowPos` / `GetWindowPlacement` / `GetWindowRect` / DWM / gdi32 region / `MessageBoxW`）已全部补**内联 `OperatingSystem.IsWindows()` 守卫**（NT-081 步骤 2）。🔴 教训：X11 / Wayland 下 `TryGetPlatformHandle()?.Handle` **也是非零值** ⇒ 只判 handle 非零挡不住 Win32 调用，必须判平台。
- 未收口：P/Invoke 物理分散在 `Shell` / `Core` / `Module.MediaAssets`（MediaAssets 还留一份重复的 `SHFileOperationW`，有守卫但不该出现）；`Process.Start` 20+ 处仍在 VM。
- Windows-only 依赖：`shell32`（回收站 ×2）/ `user32`（`MessageBoxW`、贴靠）/ `gdi32` + `dwmapi`（CSD 补丁）/ `Registry`（开机启动）/ 托盘。
- ⇒ **迁 Debian 的真实门槛是发布链 + 这些 P/Invoke，不是软件包页的抽象**。跨平台立项时先做「RID 条件化 + 守卫收口 + 主界面冒烟」，再谈 apt / snap / flatpak 后端。

---

## §3 启动顺序与模块加载

### 3.1 启动顺序（`App.OnFrameworkInitializationCompleted()`，不可颠倒）

1. 建 `ServiceCollection` 注册核心服务；
2. `ModuleLoader.LoadAll(services, GetMandatoryModuleTypes())`；
3. `BuildServiceProvider()` → `AppServices.Initialize(sp)`；
4. `ModuleLoader.InitializeAll(sp)`（**必须在 3 之后**）；
5. 应用主题（订阅 `ThemeChanged`）；
6. `new ShellView{DataContext=ShellViewModel} → win.Show()`；
7. 首导由 `ShellViewModel` 构造函数末尾 `OpenTab("home")` 触发；
8. `Show()` 之后按 `DispatcherPriority.Background` 跑 `ModuleLoader.InitializeAllDeferred(sp)`：**第一件事**是
   `ConfigStoreService.InitializeDeferred()`（框架级小文件并入 `shell.json` / 模块设置归位 `config\modules\` /
   清空壳配置），随后才是总库 schema（`LibrarySchemaService.EnsureAll`）与各模块的 `InitializeDeferred`。
   ⚠️ 构造函数只算路径、不碰磁盘；唯一例外 = `ShellConfigStore` 首次读盘前兜一次 `EnsureShellMigrated()`
   （它缓存整份文档，迁移排在读之后会导致下次保存把搬来的节抹掉）。

> 契约绑定（详情面板 / 历史栈 / 状态栏 / 声明式工具条）由 **`Shell/Services/ShellPageBinder.cs`** 独立承担：
> 切标签 / 刷新页 / 设置页切节都只调 `ShellPageBinder.Bind(tab)`，`ShellViewModel` 只保留标签管理与左树逻辑。

> **`App` 按职责拆成 partial 文件**（`public partial class App : Application`，全部在 `src/Shell/`）：
> `App.axaml.cs` 只留启动装配（`Initialize` / `OnFrameworkInitializationCompleted` / 检查更新 / 必备模块表）；
> `App.Crash.cs` 全局异常捕获；`App.Profile.cs` `--profile` 多实例与启动日志；`App.Tray.cs` 托盘与窗口显隐；
> `App.Theme.cs` 主题跟随与强调色；`App.Pickers.cs` 文件夹 / 文件 / 保存 / 确认 / 输入钩子的宿主实现。
> ⛔ 别在 `App.axaml.cs` 里加新章节 —— 放进对应的 partial 文件。

> **巨型 ViewModel / Service 同样按职责拆 partial**（同一 `public partial class Xxx`，同目录、同命名空间；字段与属性集中在主文件，partial 只放方法）：
>
> | 类 | 文件分工 |
> |---|---|
> | `AssetAllViewModel`（2022 → 544） | `.cs` 字段/列定义/列偏好/刷新与分页查询；`.Preview.cs` 预览与草稿；`.FileOps.cs` 打开文件·文件夹·启动；`.Scan.cs` 扫描任务与进度；`.Selection.cs` 全选·反选·勾选同步；`.Tags.cs` 标签/分类/收藏/评分；`.Edit.cs` 保存详情与磁盘写操作；`.Detail.cs` 详情卡（`BuildCard`）与选项重建；`.Io.cs` 库合并/另存/扫描文件夹/导入导出；`.View.cs` 过滤·排序·列头 |
> | `VideoBatchViewModel`（2117 → 451） | `.cs` 字段与任务导入导出·裁剪探测；`.Selection.cs` 勾选；`.Command.cs` 类型切换·参数·命令行·校验·试跑；`.Detail.cs` 详情卡；`.Page.cs` 构造/`RefreshView`/列映射/编辑器填充；`.TaskEdit.cs` 加文件·删除·保存·清空；`.Run.cs` 运行/停止/输出目录·任务单导入导出 |
> | `VideoPreviewViewModel`（1765 → 190） | `.cs` 字段与选源·状态前缀；`.Detail.cs` 详情卡；`.Preview.cs` 预览帧与视频信息；`.Crop.cs` 黑边探测；`.Command.cs` 参数构建·校验·试跑；`.Task.cs` 转码/剪切任务构建与发送；`.Player.cs` 命令行文本·播放计时·构造 |
> | `PackageViewModel`（1676 → 353） | `.cs` 字段·源标记·列映射；`.Detail.cs` 详情卡；`.Page.cs` 构造·初始化·搜索·列表装载·视图；`.Actions.cs` 安装/升级/卸载/固定/后端调用；`.Selection.cs` 勾选与详情装载；`.Batch.cs` 批量操作·导入导出·右键动作 |
> | `QuickActionListViewModel`（1559 → 399） | `.cs` 字段与命令库（切换/导入导出）；`.Scripts.cs` 脚本目录；`.Detail.cs` 详情卡与操作栏；`.Page.cs` 构造·分类·视图；`.Run.cs` 运行与脚本导入；`.Scan.cs` 扫描候选与页面导入导出；`.Item.cs` 增删改查·收藏·打开方式 |
> | `ScriptInfoViewModel`（1142 → 240） | `.cs` 字段与构造；`.Detail.cs` 详情卡；`.Columns.cs` 列标题/显隐 chip 与面板开合；`.Collections.cs` 集合载入与恢复；`.Sections.cs` 节创建·投影·排序；`.Refresh.cs` 刷新编排·定时器·页脚·空态；`.Actions.cs` 节设置·复制·打开脚本 |
> | `CollectionViewModel`（924 → 250） | `.cs` 字段与构造；`.Detail.cs` 详情卡与编辑态装载；`.Members.cs` 集合刷新·成员表·详情编辑落库；`.Edit.cs` 新建/复制/删除/归档/送往页面；`.Export.cs` 导出与扫描入集合 |
> | `VideoListViewModel`（991 → 211） | `.cs` 字段与选文件；`.Detail.cs` 详情卡；`.Page.cs` 构造·扫描·列映射·视图；`.Selection.cs` 勾选与转码/剪切跳转；`.MediaInfo.cs` 媒体信息探测；`.Actions.cs` 导入·播放·打开·重命名·删除 |
> | `PathViewModel`（834 → 170） | `.cs` 字段与选项模型；`.State.cs` 动作可用性·构造；`.Probe.cs` 外部工具探测与 Python 行；`.Rows.cs` 行构建·列·视图；`.Save.cs` 保存/恢复默认/浏览/打开/写回；`.Profile.cs` 多实例 Profile 增删改切 |
> | `AssetService`（1513 → 247） | `.cs` 字段·构造·存储源（增删查·统计·扫描错误）；`.Asset.cs` 资产查询/插入/更新/删除/收藏评分；`.Location.cs` 文件位置读写与缺失标记；`.Query.cs` 行查询 + SQL 条件/排序构建；`.Org.cs` 名称映射与标签·分类·集合选项；`.Commit.cs` 批量行落库与显示名；`.ScanJob.cs` 扫描任务 + 数据读取映射 |
> | `FfmpegService`（932 → 306） | `.cs` 探测·抽帧·关键帧·剪切参数；`.Filters.cs` 编码器/滤镜链/裁剪探测/字幕/GIF；`.Transcode.cs` 转码参数与命令描述·输出路径；`.Run.cs` 执行与进度解析·外部播放·清理 |
> | `RuleRunService`（922 → 214） | `.cs` 构造·缓存路径·抓取与字符集嗅探·命令列表；`.Probe.cs` 探针与站点探测；`.Run.cs` 抓行与单步执行；`.Python.cs` 规则脚本调用与输出解析 |
> | `ListPageViewModelBase`（731 → 308） | `.cs` 构造·分页导航·列可见性缓存装载；`.Columns.cs` 列宽锁定/拖拽/显隐与布局持久化；`.DetailSort.cs` 详情开关与排序；`.Filters.cs` 筛选条件与快捷筛选 |
> | `CrawlPanelViewModel`（856 → 264） | `.cs` 字段·构造·抓链；`.Result.cs` 结果落表·排序·行钩子；`.DrillDown.cs` 下钻与试跑；`.Download.cs` 下载编排与事件；`.History.cs` 历史记录与诊断复制 |
> | `AssetMergeService`（850 → 233） | `.cs` `MergePreview`/`MergeResult` + 预览·读库·组织文本；`.Merge.cs` 合并入口与另存；`.MergeCore.cs` 存储源/资产/位置合并与孤儿清理；`.MergeNames.cs` 按名合并·分类父级修正·关联表·工具方法 |
> | `ScanService`（822 → 248） | `.cs` 构造·预览·设置·整库扫描；`.Storage.cs` 单个存储源扫描；`.Index.cs` 文件列表索引；`.IndexOne.cs` 单文件判定与落表·批量补全；`.Util.cs` 索引装载·枚举·哈希·类型嗅探；`StorageIndex` 独立成 `StorageIndex.cs` |
> | `ShellViewModel`（785 → 344） | `.cs` 字段·构造·标签开关与排序；`.Search.cs` 全局搜索索引与结果跳转；`.Tree.cs` 左树构建·高亮·键盘导航·模块重载；`.Resolve.cs` 标签创建与视图/VM 解析 |
> | `RuleEditorViewModel`（774 → 234） | `.cs` 字段·构造·阶段槽位·脚本列表·规则挂载；`.Run.cs` 抓网页·分段跑·推荐·命令表装载；`.Flow.cs` 流程（Flow）存取；`.Edit.cs` 保存·增删命令·过滤·诊断复制；`.Find.cs` 查找/定位/行号与规则块替换 |
> | `DownloadService`（729 → 162） | `.cs` 构造·脚本元数据·试跑；`.Download.cs` 下载主流程与事件回报；`.Plan.cs` 产物规划与 URL/类型判定；`.Save.cs` 落盘·命名去重·限速闸门 · 文件工具 |
>
> **多类型同文件**（`VideoTask.cs`、`DetailCardRender.cs`）改为按语义分组拆成独立文件，不做 partial：
> `VideoTask` → `.Cut.cs` 剪切相关类型 / `.Presets.cs` 预设与模式枚举 / `.Transcode.cs` 转码任务（主文件留 `VideoTaskBase` 与状态常量）；
> `DetailCardRender` → `.Rows.Text.cs` / `.Rows.List.cs` / `.Rows.Choice.cs` 三组字段行、`.Sections.cs` 节与图标动作、`.Renderer.cs` 卡片渲染器（主文件留 `DetailPath` 与 `DetailFieldRow` 基类）。
>
> ⛔ 新增逻辑按职责投递到对应 partial，别往主文件堆；拆分只搬方法、不改一行实现。

### 3.2 双轨加载

| | Debug | Release |
|---|---|---|
| 模块源 | `Shell.csproj` Debug 条件 `ProjectReference` | `<程序目录>\Modules\` DLL |
| 必备模块 | **无**（v3.62 起应用级设置页归 Shell） | 无 |
| 需要 `Modules\` | 否 | 是（无则兜底程序目录） |

**硬规则**：
- Debug 的功能模块必须走显式类型清单（`App.axaml.cs` 的 `#if DEBUG`），⛔ 不能只靠 `AppDomain.GetAssemblies()`。
- 跳过一个功能模块要改两处：`App.axaml.cs` 的 `typeof(XxxModule)` + `Shell.csproj` 的 `ProjectReference`。
- Release 路径用 `AppContext.BaseDirectory` 拼绝对路径；模块 DLL 命名 `Module.<模块名>.dll`。
- ⛔ **`Modules\` 是模块产物唯一去处，全程零拷贝**（`Directory.Build.props` 把模块 Release `OutDir` 直设为 `$(NewToysReleaseDir)Modules\`）。
- Release 关 pdb（`DebugType=none`）消除并发写 `MSB3026`。
- **健全性检查**：`InitializeAll` 后若**宿主分组 `system`** 不存在（Shell 自身注册失败）→ 日志 + 状态栏一行警告，⛔ 不静默（常量 `ModuleLoader.HostGroupKey = "system"`）。

### 3.3 新增模块步骤

1. 建 `src/Module.Xxx/Module.Xxx.csproj`：`RootNamespace=NewToys.<Xxx>`、`AvaloniaUseCompiledBindingsByDefault=false`，引 `Shared + Core + UI`。
2. 写 `XxxModule.cs : IModule`，命名空间 `NewToys.<Xxx>`。
3. `src/NewToys.slnx` 加 `<Project Path="Module.Xxx/Module.Xxx.csproj" Type="C#" />`。
4. **Debug 链路两处登记**：`Shell.csproj` Debug 条件 `ProjectReference` + `App.axaml.cs` 的 `#if DEBUG` 清单。
5. `Initialize` 里 `RegisterModule` + `RegisterFunction<TView>`（视图名由泛型参数决定），`icon` = Geometry 键。
6. **有设置页时**：View/VM 放**本模块** `Views/SettingsXxxView.axaml(+.cs)` + `ViewModels/SettingsXxxViewModel.cs`；`RegisterFunction<SettingsXxxView>(<本模块分组键>, "settings-<模块键>", …, Order)` 注册进**本模块的一级菜单**（⛔ 不跨组，Order 排本组末位）。设置落 `config\<模块>.json`，模块只读、唯一写入点 = 该设置页。🔴 菜单标题**保持长名**（如「脚本管理器设置」）—— 它是独立标签页标题。
7. **建 `modules/<模块>.md`**（照 7 节模板，含 §2.5 操作逻辑）+ `backlog/<模块>/` 目录。

```csharp
public interface IModule {
    void RegisterServices(IServiceCollection services);
    void Initialize(IServiceProvider services);           // 首帧前：只注册菜单 + 轻量接线
    void InitializeDeferred(IServiceProvider services) { }// 首帧后：建库 / 读配置 / 播种 / 扫描接线（默认空实现）
}
```

- 模块类命名 `XxxModule`，命名空间 `NewToys.<模块名>`（不带 `.Module` 段）。
- 🔴 **NT-070（启动提速）两段式初始化**：`Initialize` 必须跑在 `Window.Show()` 之前（左树 / 首页功能清单 / 收藏跳转都依赖菜单已注册，晚了会「左树先空后跳」）；**任何 IO 重活都不许写这里** —— 建库 / 清表 / 读配置文件 / 种子播种 / 扫描器接线 / 批量任务入队，一律放 `InitializeDeferred`。宿主在 `Show()` 之后用 `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` 驱动它（排在首帧渲染之后）。
  - ⚠️ 必须留在 **UI 线程**（不用 `Task.Run`）：`Sqlite` / 配置 store 非线程安全，换线程会和「用户已点开页面」的读取撞车。代价 = 启动瞬间 UI 卡几十毫秒。
  - ⚠️ **页面不可假设 `InitializeDeferred` 已跑完**：用户可能抢在它之前点进页面（`InitializeDeferred` 内部必须自兜 try/catch，失败按空数据渲染）。
  - 热加载（§3.4）也会紧接着跑 `InitializeDeferred`，否则新模块热加载后永远拿不到数据。
  - 启动打点：`logs\startup-*.log` 的「启动打点（累计毫秒）：…」+「模块延迟初始化 Nms（首帧之后）」两行，改完用它核对收益。
  - 🔴 **模块扫描必须过滤 `Module.*.dll`**：`ModuleLoader.LoadAll` 的 `ScanModulesDirectory` 与 `HotReload` 都要按前缀枚举（Debug 程序目录有 ~49 个 DLL，全量 `Assembly.LoadFrom` + `GetTypes()` 一次吃掉 2173ms，过滤后 9~11ms）。
  - ⚠️ **单次启动日志不能当结论**：新编译产物第一次启动必然慢（冷读 + 杀毒实时扫描 + JIT）。判断优化是否生效，要么连跑两次取第二次，要么看分项打点。基线（Debug 热启动）= 总启动 **787~826ms**，其中主窗口构造 ~450ms、窗口显示段 ~345ms 为 Avalonia 首帧固有成本，再压收益 < 150ms 且风险高 ⇒ 到此为止。

### 3.4 热加载

- 目录只有一个：`Path.Combine(AppContext.BaseDirectory,"Modules")`；只认 `Module.<模块名>.dll`。
- ⛔ 跳过已加载同名程序集 → 本版**只做「新增」**（B10）。
- 每个待加载 DLL 用**独立 `AssemblyLoadContext`**（`isCollectible:true`）；`Shared` / `Core` 一律由**默认 ALC 共享**（ALC 的 `Load` 覆写里把契约程序集转交默认上下文），否则 `IModule` 强转必失败。
- ⛔ 已 `BuildServiceProvider()` 的容器无法追加服务 → 热加载模块 `RegisterServices` 可跳过；优先**页面型模块**。
- 加载后调 `IMenuService.RegisterModule/RegisterFunction`，通知 `ShellViewModel` 重建左树（`BuildTree()`）。
- **失败隔离（硬要求）**：单 DLL 异常 `try-catch` 兜住，只记日志 + 一行 StatusText；完成后报「本次新增 N / 跳过 M / 失败 K」。
- ⛔ 只读本地 `Modules\`；不联网；加载前 `Path.GetFullPath` 校验路径在 `Modules\` 内。
- **入口**：「版本与更新」节的关于部分「🔄 重载模块」→ `AboutSettingsViewModel.ReloadModulesCommand` → `IModuleReloadService.HotReload()`；`ShellViewModel` 订阅 `Reloaded` 重建左树（进程级单例，无需退订），成功后该节「组件版本」自动重取。设置页 VM ⛔ 不直接调 `ModuleLoader`。
- **旁边「📁 打开模块文件夹」**：`OpenModulesFolderCommand` 在文件管理器里打开 `<程序目录>\Modules\`（不存在即建）—— 同节 VM 内一行 `Process.Start`，按 Windows/macOS/Linux 分支选 `explorer.exe` / `open` / `xdg-open`（⛔ 不为此新增服务）。
- **技术栈取数**：`TechStackProbe.Collect()`（`Shell/ViewModels/TechStackProbe.cs`）扫程序目录 + `Modules\` 的 `*.deps.json`，只保留**各项目直接引用**的包（`targets.dependencies` 反推被依赖集合，全量减被依赖者），`Avalonia.*` 家族折叠成一行「Avalonia」+ 版本 +「合并 N 个包」。同名多版本取最高；**传递依赖不展示**（用户明确：只列直接引用）。🔴 反推时只统计 **package 条目**的 dependencies —— 项目自身条目（`Shell/1.0.0`）的 dependencies 就是全部直接引用，算进来差集恒为空。⛔ 不读 `Directory.Packages.props`（发布后不存在）；⛔ 别改成白名单硬编码（新增包会漏）。
- 🔴 `Reloaded` 事件签名 = `event Action<(int Added, int Skipped, int Failed)>?` —— 裸 `Action?` 承载不了「本次新增 / 跳过 / 失败」计数（NT-052 对验收标准的细化）。

---

## §4 菜单注册与导航

### 4.1 API

```csharp
AppMenuGroup RegisterModule(string key, string title, string icon, int order,
                           bool isBottom=false, bool isHome=false);
void RegisterFunction<TView>(string moduleKey, string key, string title, string icon,
                             int order=0, string desc="", SettingsCategory category=SettingsCategory.None)
    where TView : class;
```

- `category`（追加在参数表末尾、带默认值）：`通用｜一键按钮｜运行环境｜网络｜数据｜关于｜模块设置｜None`。取代「0~99 框架 / 100+ 模块」的 Order 段位隐式约定；`Order` **只作组内排序**。
- 先 `RegisterModule` 拿分组，再 `RegisterFunction`；⛔ 菜单只能在 `Initialize` 注册。
- 🔴 `TView` 传**视图类**（如 `RegisterFunction<Views.ScriptLibraryView>(...)`）—— 菜单项视图名取 `typeof(TView).Name`，⛔ **不再接受字符串**：View 类改名时编译器强制注册处跟随，杜绝「注册名与类名不一致 ⇒ 菜单项永远打不开」。
- 菜单 Key 规范：`<模块缩写>-<页面名>`，全小写连字符。🔴 **Key 一经发布 ⛔ 不改**（改 = 用户列布局 / 收藏 / 标签偏好全丢）。
- **`icon`**：一级分组传 **Geometry 资源键**（`"IconPc"` / `"IconWeb"` / `"IconVideo"` / `"IconMediaAssets"` / `"IconSettings"`）；二级功能仍传 emoji（左树不显示；首页收藏卡 / 功能清单页用）。
- `desc`（功能简介）务必填写。
- 点击路径：`ShellViewModel.OpenTab(key)` → 新建 / 激活 `TabItemViewModel` → `ResolveView` → 写入 Tab 的 `Content`。

### 4.2 分组现状

| 分组 | Key | Order | 图标 |
|---|---|---|---|
| 脚本管理器 | `script` | 20 | `IconPc` |
| 网络工具 | `web` | 30 | `IconWeb` |
| 视频工具 | `video` | 40 | `IconVideo` |
| 媒体资产 | `assets` | 50 | `IconMediaAssets` |
| 宿主分组（**不渲染**） | `system` | 999 | `IconSettings` |

> 🔴 **`system` 是框架保留键**（`ModuleLoader.HostGroupKey` / `AppSettingsRegistration.HiddenGroupKey`）：功能模块⛔ 不得用同名组键 —— `RegisterModule` 同名会复用 + 覆盖 `Title/Order/IsHome` 并补登暂存项 ⇒ 宿主组的设置锚点会被渲染进那个模块（NT-091 踩过，系统工具改用 `system-tools`）。

- 宿主分组 `IsHome=true` ⇒ 左树 / 首页模块计数都按 `!IsHome` 过滤；只承载应用级设置的节锚点寻址。
- **v3.66 起左树无「设置」一级菜单项**；模块设置项挂回**本模块分组末位**（`assets`=99、其余=100）。
- `isBottom` 保留但当前**无分组使用**。
- 排序：`OrderBy(Order)`；分组内 `Items` 按 `Order` 重排。
- 🔴 **跨组注册 + 延迟补登**：`RegisterFunction` 发现分组未注册时**暂存**（⛔ 不抛，`InitializeAll` 的逐模块 try-catch 会吞掉 → 菜单项静默丢失），待 `RegisterModule` 建立分组时补登（去重 + 按 Order 重排）。分组始终未建立 → 启动健全性检查报一行。v3.66 起模块设置页不再跨组 ⇒ 本机制现只作「分组 key 打错」的兜底。

### 4.3 标签栏机制

- 🔴 **首页常驻标签栏**（v3.69）：`TabItemViewModel` **无可见性开关**（`ShowInTabStrip` / `IsVisibleInStrip` 已删），首页与其余标签一视同仁显示，仅 `CanClose=false`；零标签兜底照旧回首页。
- 标签栏 = `Grid ColumnDefinitions="Auto,Auto"`：`Auto` 列 `ScrollViewer(TabsScroll)` 内 `ItemsControl` **直接绑 `Tabs`**（横排 `ItemsPanel` 必须显式写，否则默认竖排 + 竖滚禁用 = 只显示第一个）；`Auto` 列 `＋`（`NewTabButton`）**紧跟最后一个标签**。
- 🔴 **宽度类浏览器行为**：标签少 ⇒ 滚动区按内容量，`＋` 贴着最后一个标签；标签溢出 ⇒ code-behind `SyncTabStripWidthCap()` 把 `TabsScroll.MaxWidth` 收到「标签栏宽 − `＋` 宽 − 左边距」⇒ 滚动区收窄、`＋` 常驻最右且始终可见（挂 `TabStrip` / `TabsControl` / `NewTabButton` 三处 `SizeChanged`）。⛔ 别再改回 `*` 列（`＋` 会被推到整条最右、脱离标签）。
- `＋` = `OpenHomeTabCommand` → `OpenTab(HomeKey)`（功能清单在首页内嵌，已无独立页）。
- 滚轮切标签 = `PrevTabCommand` / `NextTabCommand`（仅此处使用），**到头即止、不循环**（`Math.Clamp`，v3.69）。
- ⛔ 不做「固定其他标签页」（固定需求走首页收藏夹）⇒ 无通用「固定位」机制。
- 旧机制 `ScrollableTabs` / `HomeTab` / `SyncTabMirror` / `SyncScrollableTabs` **全部已删**。
- 标签项模板 = 共享资源 `TabItemTemplate`（`ShellView.axaml` 的 `Window.Resources`，⛔ 不复制两份防样式漂移）。
- View 实例由 `TabItemViewModel` 持有并缓存，切标签不重建；关标签时才销毁（VM 实现 `IDisposable` 则调用）。

### 4.4 左树行为

- 左树用 `ItemsControl` + `Button.tree-row` **自绘**，⛔ 不用 `TreeView`。
- 一级分组展开态落 `columnlayout.json` 的壳级分区（`treeGroup:<groupKey>`，默认 false）；折叠态 `treeCollapsed`。
- **展开整组 `BringIntoView`**（NT-037）：点**底部**分组的展开后，对该组**容器整块**做 `BringIntoView`（`Dispatcher.Post`，参考 `ScrollActiveTabIntoView` 模式），保证分组头 + 子项末都可见；**收起时不跳**；顶部 / 中部分组行为不变。
- 版本号 `TreeVersionText` 在左树底部版本行（`ShellView.axaml`）；设置齿轮也在此行。

### 4.5 托盘（NT-057）

- 图标 = **唯一图标工厂 `CreateAppIcon(accent)`**（窗口 + 托盘同源，随强调色）+ `Resources/app.ico`（exe 图标）⇒ ⛔ 不再代码画、⛔ 不写死强调色。
- 左键 = 切显示 / 隐藏；右键五项菜单：首页 · 设置 · 用另一个配置打开…（子菜单排除当前配置 / 无其他配置时置灰，先拉起窗口再导航）· 退出。
- ⚠️ 技术债：图标**形状两处定义**（C# 常量与 ICO 口径），改一处须同步另一处。

### 4.6 应用级设置页 / 首页 / 功能清单（Shell 页面，不是模块）

> 🔴 **定位**：应用级设置归**框架层**（`src/Shell`）。`Module.Settings` 已于 NT-062 撤销，⛔ 别再当模块写、⛔ 不建 `modules/settings-app.md`。
> 入口 = 左树底部**版本行齿轮** / `Ctrl+,` / 标题栏搜索 / 首页一键区 `⚙`（跳 `settings-launch`，归一到页 Key `settings`）。**左树无「设置」一级菜单**；模块专属设置在本模块分组末位（如 `settings-script`）。

**7 个节锚点**（`Shell/Settings/AppSettingsRegistration.cs` 注册进不渲染的 `system` 组）

| 节锚点 Key | 标题 | Order | 分类 | 内容 |
|---|---|---|---|---|
| `settings-general` | 启动与退出 | 0 | `General` | 开机启动 / 托盘 / 关闭按钮行为 |
| `settings-theme` | 主题外观 | 1 | `Theme` | 明暗模式 / 强调色 |
| `settings-launch` | 一键按钮 | 3 | `Launch` | 首页三个一键按钮的配置 |
| `settings-path` | 路径与配置 | 2 | `Environment` | 运行环境路径 + 多配置档（分类列区分行） |
| `settings-dns` | 加密 DNS | 4 | `Network` | 加密 DNS / 网络 |
| `settings-data` | 数据管理 | 6 | `Data` | 备份 / 导入 `config` |
| `settings-update` | **版本与更新** | 8 | `Update` | 版本号 / 版本文件地址 / 检查更新 / **组件版本** / 技术栈 / 重载模块 + 打开模块文件夹 |

- 🔴 **所有 Key ⛔ 不改**：首页跳转 / 收藏白名单 / 托盘「打开设置」都按 Key 寻址。
- 「通用」已拆成 `settings-general` / `-update` / `-theme` **三节共用一个 VM**。
- 🔴 **v3.67：「更新」+「关于」合并 = 「版本与更新」**（8 节 → 7 节，用户拍板）—— Key 用原来的 `settings-update`；
  `settings-about` 退成**别名**（`SettingsViewModel.ResolveKey` + `ShellViewModel.SettingsSectionKeys`，旧链接仍能打开，
  ⛔ 不再注册菜单、⛔ 不再是一个节点）。该节由 `UpdateAboutView`（**拼装壳**）装 `UpdateView` + `AboutSettingsView`，
  子 View 的 DataContext 由 `Init(...)` 分别赋（⛔ 靠继承只能给到一个）；节上有**两个 VM** ⇒ `SettingsSection.ViewModels`
  是列表，状态栏 / 工具条契约按列表逐个聚合。
- 功能清单**不是独立页**（⛔ 不注册菜单 Key）：`AllFunctionsView` / `AllFunctionsViewModel` 由首页收藏卡尾部「显示更多」就地展开（`HomeViewModel.BuildFunctionList` 现装配）。

**页面结构机制**（`SettingsView.axaml(.cs)`）

> 🔴 **NT-071（2026-10-18）：整页骨架已搬到 UI 库** `NewToys.UI/Controls/SettingsSectionHost`（节模型 = `NewToys.UI.Settings.SettingsSection`），
> 应用级设置页与 **4 个模块设置页共用同一份实现**。`SettingsView` 现在只剩两件事：VM `ScrollRequested` → `Host.ScrollTo`、
> `Host.SectionToggled` → VM `NotifySectionsToggled`（刷新状态栏聚合）。⛔ 别再往 `SettingsView` 里加视口/折叠逻辑，
> 细则见 `ui.md` §11。同时：`SettingsViewModel` 的 `ToggleSection` / `ToggleAllSections` / `ToggleAllText` / `ExpandSection` **已删**。

- **树状手风琴滚动 + 抬头磁吸浮层**：逻辑在 code-behind；🔴 类名 `sec-head` / `sec-body` 别删，`StuckHeader.DataContext=null` 别删（磁吸浮层 DataContext 隔离）。
- 🔴 **v3.68：磁吸浮层是 `ScrollViewer` 的兄弟覆盖层**（`PageRoot` 的第二个孩子），⛔ **不许放回滚动内容里**——
  放里面只能靠 `RenderTransform` 反补 `Offset` 把位置"推算"出来，实测出「滚到顶残留空白浮层 / 顶部抬头被裁一半 /
  抬头与正文错位」。覆盖层由布局自己定位（`VerticalAlignment=Top` + code-behind 按卡片算左右 Margin），零补偿数学；
  代价 = 浮层上的滚轮要转发给 `SectionScroll`（`PointerWheelChanged`，漏了就"鼠标停顶部滚不动"）。
- 节点抬头有**独立底色**（`Button.sec-head` → `ContentHeaderBrush` + 展开时下边框 `DividerBrush`）⇒ 抬头与正文一眼分开；
  磁吸浮层里的抬头另走 `Button.sec-head.stuck`（透明底，底色由浮层 Border 给）。
- 🔴 **v3.69：浮层顶部留白 = `clamp(12 - Offset.Y, 0, 12)`**，且 **`Offset.Y <= 0.5`（真·顶部）时浮层一律不挂**
  （`default(Row)` 直接喂 `ApplyStuckHeader`）。原判定只看 `Top < -0.5`，高 DPI / 边距取整下首节点会被误判成"钉住"
  ⇒ 顶部残留一条 `ContentHeaderBrush` 底色的抬头，看着像"标题栏没留空白"（用户实测）。留白跟卡片走还顺带消掉了
  "滚过 12px 时浮层咔一下跳上去"的突变。⛔ 别把顶部留白改回常量 0。
- 浮层右上「全部收起 / 全部展开」`FontSize=13` + `Padding=10,4`（v3.69 放大；11 号字用户看不清）。
- 每节内含工具条（`HasToolbar` 门控，`ToolbarHost` 挂在节抬头下方）；⛔ **工具条不上抛页顶**；节 View **⛔ 带内层 `ScrollViewer`**（会出现双滚动条 + 磁吸失效）。
- 切节 ⇒ `ActiveSection` 变 ⇒ `SectionChanged` ⇒ Shell 重绑工具条 / 状态栏 / 详情卡（漏了就"画得出来、点不动"）。
- 🔴 **滚动 ⛔ 不改任何节的展开状态**：一节开没开**只由点击决定**（点抬头 / 「全部收起」）。早先做过"滚到哪节自动展开哪节"，实测与用户意图冲突（想看最下面一节，滚一下上面几节全被弹开）⇒ 已整体移除，⛔ 别再恢复。
- `settings-path` 节正文用 `ListPageSkeleton` 且 `Margin="0"`。

**三层优先级（v3.67，改这块前必须遵守）**

| 层 | 触发 | 行为 |
|---|---|---|
| 1 | 用户点抬头 / 「全部收起·全部展开」 | 立即生效；点抬头 = 只切这一节（其余不动）、展开时滚到它贴视口顶；「全部收起」= 全部收起（一屏看全抬头） |
| 2 | 外部寻址（首页 ⚙ / 收藏 / 搜索） | 目标展开 + 其余折叠 + **滚到目标贴视口顶**（`ScrollRequested`） |

- 🔴 无第三层：**滚动不参与折叠状态**（`SettingsSection` 只剩 `IsExpanded`，`UserCollapsed` / `CollapsedByAll` 已删）。

- 🔴 **v3.69：激活色整套已删**（用户："激活色始终不可见"）——`SettingsSection.IsActive` 属性、`Button.sec-head.active`、
  `Button.settings-nav.active`、"被钉住的那节上激活色"的 View 逻辑全部撤销。**"当前在哪一节"只由顶栏表达**：
  顶栏底色 `PanelBrush`（卡片同档）+ 下边框 `DividerBrush` + 上两角不圆（`CornerRadius="0,0,8,8"`）。
  ⛔ 别再引入 active 态；`SettingsViewModel.ActiveSection` 仍归左树 / 状态栏用，与视觉无关。
- 🔴 定位必须**两阶段**：先展开/折叠 → 等 `LayoutUpdated` → 再算 offset（`TranslatePoint(→SectionScroll).Y + Offset.Y`），
  最多重试 8 次收敛；⛔ 同步一次算完（高度还在变，算出来必错）。

**节 / VM 归属**

| 节 | View | VM |
|---|---|---|
| 启动与退出 / 主题外观 | `GeneralView` / `ThemeView` | `GeneralViewModel`（无自有 VM，共用） |
| 一键按钮 | `LaunchConfigView` | `LaunchConfigViewModel` |
| 路径与配置 | `PathView` | `PathViewModel` + `Models/PathSettingItem` |
| 加密 DNS | `DnsView` | `DnsViewModel` |
| 数据管理 | `DataView` | `DataViewModel` |
| 版本与更新 | **`UpdateAboutView`**（拼装 `UpdateView` + `AboutSettingsView`） | `GeneralViewModel` + `AboutSettingsViewModel`（**两个**） |
| 页宿主 | `SettingsView` | `SettingsViewModel` + `Models/SettingsSection` |

- 复合页工具条：节 VM 靠 **`IToolbarOwner.ToolbarOwner`** 当承载者（见 §6.4）。
- 设置模型类被两模块共用时放 `Shared`（⛔ 双真相源）；配置落 `config\theme.json` / `app.json` / `launch.json` / `columnlayout.json`（见 §7.1）。

**操作逻辑**

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 展开 / 折叠节 | 点节抬头 | **只切这一节**（其余节保持原状），展开时滚到本节点贴视口顶 | 滚动 ⛔ 不改展开状态 |
| 切节 | 点另一节抬头 / 页内搜索命中 | 工具条 / 状态栏 / 详情卡重绑到该节 VM | 见上「SectionChanged」 |
| 滚动 | 滚轮 / 滚动条 | 只滚内容：当前节点抬头磁吸顶部（含 Push）；**滚到最顶部（`Offset≈0`）时浮层自动消失**，页顶只留卡片自己的 12px 留白 | 不改任何节的展开状态；想换节就点抬头 |
| 标题栏搜索（NT-066） | 标题栏搜索按钮 → 输入框 | 统一索引 = **全部二级菜单项** + **本页 7 个节**；输入即出列表，↑↓ / 回车 / 双击打开 | 设置节 Key 由 `OpenTab` 归一成 `settings` + 展开滚动；⛔ 不搜模块私有内容、⛔ 拼音 |

**各节写入方式**（每项只有本页一个写入点）

| 节 | 时机 | 备注 |
|---|---|---|
| 启动与退出 / 更新 / 主题外观 / 一键按钮 / 加密 DNS | 即时落盘 | 主题即时生效 |
| 路径与配置 | 编辑即落盘 | **切换配置二次确认 + 本窗重启**；`🚀 新开` 同时开两套；外部程序路径**惰性探测**，缺失 ⇒ 状态栏 `⚠` 可点提示，⛔ 不崩 |
| 数据管理 | 显式动作 | 备份 = **zip 分节包**（`manifest.json` + 每节一个内层 zip）；默认只勾设置节（`shell` + `module.*`），`env` / `data.*` 默认不勾；恢复先归档 `.bak-<时间戳>`；旧的 v1 单 JSON 只读导入 | 见 §7.4 |

**功能清单（首页内嵌，非独立页）**

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 展开 / 收起 | 首页收藏卡尾部「显示更多」 | 就地展开全量功能卡片墙，右键「📌 固定到首页」 | 只存在于首页内；⛔ 没有菜单 Key、⛔ 不进标签栏；「🔄 重载模块」在「版本与更新」节的关于部分 |

**首页 `home`**

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 一键按钮 | 点一键卡 | 按 `config\launch.json` 启动 | 配置来自「设置 → 一键按钮」 |
| 收藏 | 功能项 ★ | 写 `config\favorites.json`（即时） | — |
| 显示更多 | 收藏卡尾部 | 就地展开功能清单 | ⛔ 不跳页 |
| 标签行为 | — | `CanClose=false`（其余同普通标签） | 零标签兜底回首页（见 §4.3） |

**坑点**

1. 🔴 内容区 = `ContentControl` + `{Binding ActiveTab.Content}`，⛔ 不自己 `Children.Add`、⛔ 不绑独立属性。修「不换页 / 双击」落点 = `OnActiveTabChanged`（重活包 try/catch + `finally` 补发 `OnPropertyChanged(nameof(ActiveTab))`）。
2. 菜单标题**保持长名**（如「脚本管理器设置」）—— 它是独立标签页标题。
3. Shell 自带页面也走 `ResolveView` ⇒ 🔴 **类名必须全局唯一**（历史两处 `DnsView` 撞车）。

---

## §5 页面解析与 VM 定位

- View：`UserControl`，放模块 `Views/`，`.axaml` + `.axaml.cs`。
- VM：`NewToys.<模块>.Views.XxxView` ↔ `NewToys.<模块>.ViewModels.XxxViewModel`，**必须成对**（设置页的节子视图除外，
  在类声明上方写 `// lint:view-no-vm` 声明豁免）。
- 🔴 **命名约定的唯一实现 = `Shared/Services/ViewNaming.cs`**（`ViewModelName` / `ViewModelNamespace`）。
  解析与静态检查都调它，⛔ 不要在别处再写一份字符串处理 —— 两处规则漂移的后果是「页面打得开、整页空白、无异常」。
- 类型检索：`IPageCatalog`（实现在 `Core/Services/PageCatalog.cs`）—— 首次访问把已加载程序集的类型
  按**简单类名**建索引并缓存，`FindByName` / `ViewModelCandidates` 走字典，⛔ 不再每次导航全量扫程序集。
  索引顺序 = `AppDomain.GetAssemblies()` 顺序 + 程序集内声明顺序 ⇒ 「同名取首个」的结果与逐次扫描一致。
  🔴 **模块热加载后必须 `Invalidate()`**（`ModuleReloadService.HotReload` 已调，且在 `Reloaded` 之前），
  否则新程序集里的页面类型永远查不到，点开全是「找不到视图类型」。
- 解析（`ShellViewModel.ResolveView`）：① `IPageCatalog.FindByName` 取首个 `Control` 子类 → ② `ActivatorUtilities.CreateInstance`
  → ③ 推导 VM 优先限定在 View 自己的程序集（`.Views`→`.ViewModels`），找不到才降级全 `AppDomain` 扫。
- 🔴 **类名必须全局唯一**：同名 View 会撞车（历史两处 `DnsView`）。静态检查 = `check-comments.ps1` 的 `C-05`。
- 无占位页：兜底 = `ShellViewModel.BuildErrorView()` 现场构造的红字 + `ScrollViewer`。
- Shell 自带页面同样走 `ResolveView`（先例 `HomeView` / `HomeViewModel`）。
- 可以先注册菜单、后补页面。
- 🔴 **交付前跑 `check-view-pairs.ps1`**：`P-01` = `Views/` 下的 View 在本程序集找不到同名 VM（页面会静默空白）；
  `P-02` = `RegisterFunction<TView>` 的泛型实参不是 `*View` 命名或 `src/` 里没有该 View 类（类型存在性已由编译器保证）。

---

## §6 契约（Shared 侧接口，全部放 `Shared/Services/`）

> 实现与渲染口径见 `ui.md`；本节约只写**契约定义与 Shell 侧职责**。

### 6.1 `IDetailPanelProvider`

```csharp
public interface IDetailPanelProvider
{
    bool HasDetailPanel { get; }
    bool IsDetailOpen   { get; }
    object? DetailContent { get; }
    event EventHandler? DetailChanged;
    void ToggleDetail();
    void OpenDetail();
}
```

- Shell：`ActiveTab` 变化时检测 `ActiveTab.ViewModel is IDetailPanelProvider p`；是 → 显示右列 + 订阅 `p.DetailChanged`，`IsDetailOpen` 初值从 `ColumnLayoutService.LoadFlag(<pageKey>, FlagDetailPanel)` 读；否 → 隐藏右列。订阅前**先解绑旧 Tab**。
- ⚠️ 基类 `ToggleDetailCommand` **不发 `DetailChanged`** ⇒ 迁移页必须自己的开合命令。

### 6.2 `IHistoryProvider`

```csharp
public interface IHistoryProvider
{
    object? CaptureState();
    void RestoreState(object state);
    event EventHandler? StatePushed;
}
```

- Shell：`ActiveTab` 变化时检测并订阅；`TabItemViewModel.CanGoBack/CanGoForward` 控制前进后退按钮。
- 现状仅 `video-list`（目录切换）有实际语义。

### 6.3 `IDetailCardSource`

```csharp
public interface IDetailCardSource
{
    DetailCardDescriptor? Card { get; }   // 非 null → 声明式；null → 退回自绘 DetailContent
}
```

**件与归属（⛔ 别放错层）**

| 件 | 归属 |
|---|---|
| `DetailCardDescriptor` / `DetailSection` / `DetailField` / `DetailAction` / `FieldKind` / `EditCommitMode` | `Shared/Details/DetailCardDescriptor.cs`（纯 POCO，命令用 BCL `ICommand`） |
| `IDetailCardSource` | `Shared/Services/` |
| `DetailCardHost.axaml(.cs)` + `DetailCardRender.cs` | `NewToys.UI/Controls/`（NT-063 P0-a 从 Shell 搬入） |
| 16 张卡的 `Card` 声明 | 各页 VM（C# 对象字面量） |

- 渲染器确认弹窗经 `DetailCardHooks` 由 Shell 启动时转接 `DialogHooks.ConfirmHandler`。
- **Shell 两轨择一**（`ShellViewModel.RefreshDetail`）：① 是实现 `IDetailCardSource` 且 `Card != null` → `IsDetailCardActive = true`，⛔ **此时不读 `p.DetailContent`**；② 否则走老路 `DetailPanelContent = p.DetailContent`。`ShellView` 右列 = `Panel{ ContentControl(老路) + DetailCardHost(声明式) }`，按 `IsDetailCardActive` 互斥显隐。

### 6.4 `IToolbarSource` + `IToolbarOwner`

| 件 | 位置 | 说明 |
|---|---|---|
| `IToolbarSource` | `Shared/Services/` | `Toolbar`（`IReadOnlyList<ToolbarItemDescriptor>?`，空 / null = 无工具条）+ `ToolbarChanged` |
| `IToolbarOwner` | `Shared/Services/` | 🔴 `object? ToolbarOwner` —— **复合页**把工具条承载者让给"有工具条的那一节 VM"（描述符的 `CommandPath` 相对节 VM 解析；复合页自己当承载者 ⇒ 工具条"画得出来、点不动"）。Shell 规则：页面 VM 实现本接口 ⇒ 用 `ToolbarOwner` 作承载者，否则用页面 VM 自己 |
| `ToolbarItemDescriptor` / `ToolbarMenuItemDescriptor` | `Shared/Models/` | 纯 POCO；`*Path` 一律相对页面 VM。`TextField` 的四种特殊字段：`Placeholder` / `IsReadOnly`（只读假水印）/ `PlaceholderWhenIdle`（**可编辑**框失焦只留水印）/ `EnterCommandPath`（唯一提交入口 = 回车 + 失焦）；三态对照表见 `ui.md` §8.4 |
| `ToolbarHost` | `Shell/Controls/` | 渲染 + 自动折叠 + 详情开关浮层 |
| `ListPageViewModelBase` | `Shared/Models/` | 已实现 `IToolbarSource`（`public virtual Toolbar => null`） |

### 6.5 选择器钩子（VM ⛔ 不引 Avalonia）

统一走 `Core.DialogHooks` 的 3 个钩子：`PickFolderAsync` / `PickFilesAsync`（多模式用 `;` 分隔，如 `"*.html;*.htm"`）/ `SaveFileAsync`；由 `Shell/App.axaml.cs` 注入实现（未注入 = 按「用户取消」处理）。

### 6.5b 弹出卡片层（`DialogCardHost`，NT-084）

| 件 | 位置 | 说明 |
|---|---|---|
| `DialogCardHost.axaml(.cs)` | `Shell/Controls/` | 全窗口遮罩（`LoadingOverlayBrush`）+ 居中卡片（`PanelBrush` 底 / `DividerBrush` 1px / 圆角 8 / 投影）；挂在 `ShellView` 最外层 Grid 顶层 |
| 宿主注入 | `Shell/App.axaml.cs` | `DialogHooks.ConfirmHandler` → `ConfirmAsync(msg)`、`InputHandler` → `InputAsync(title, initial)`，内部取 `(_mainWindow as ShellView)?.DialogCards`；`DetailCardHooks.ConfirmHandler` 转接不变 |
| 调用方 | 全部页面 | `DialogHooks.ConfirmAsync` / `InputAsync`（Core 静态钩子）+ `DataView`（导入/备份确认走 `MessageAsync` / `NotifyAsync`）+ 检查更新提示 |

- NT-084 起 **Shell 内不再有 `new Window` 弹窗**（Confirm / Input / Notify / DataView 四套 Window 实现全收编）；`PickFolder` / `PickFiles` / `SaveFile` 仍是系统选择器（本来就走 OS UI，与主题无关）。
- 卡片 API：`ConfirmAsync(message)` / `NotifyAsync(title, message)` / `InputAsync(title, initial)` / `MessageAsync(title, message, okText, showCancel)`；样式与交互口径见 `ui.md` §10.7。

### 6.6 `IFileOperationService`（v3.66 / NT-069，跨模块磁盘动作）

| 件 | 位置 | 说明 |
|---|---|---|
| `IFileOperationService` | `Shared/Services/` | `Rename` / `Move` / `Copy` / `DeleteToRecycle` / `PermanentDelete` / `EnsureDirectory` / `IsInside`；返回 `FileOpResult(bool Ok, string Message, string? Path)`（`Shared/Services/` 内，与枚举 `FileConflictPolicy` 同文件） |
| `FileConflictPolicy` | 同上 | `AppendNumber`（默认）/ `ShortHash` / `Skip` / `Overwrite` |
| 实现 | `Core/Services/FileOperationService.cs` | 纯磁盘动作：⛔ 不碰数据库、⛔ 不弹窗、⛔ 不写业务日志 |
| 注册 | `Shell/App.axaml.cs` | 单例 |

🔴 **分层纪律**：确认框与库表记账留在各模块自己那层——媒体资产用 `Module.MediaAssets/Services/FileOperationService`（带 `operation_logs` 日志 + 存储源防穿越），视频工具用 `VideoFileService`（带 `DialogHooks` 确认）。两者都只把**磁盘那一步**交给本接口。

🔴 **命名坑**：`NewToys.Core.Services.FileOperationService` 与 `NewToys.MediaAssets.Services.FileOperationService` 同名 ⇒ 模块内同时 `using` 两个命名空间会 CS0104；消费方显式取别名（`AssetAllViewModel` / `AssetTrashViewModel` / `SettingsMediaAssetsViewModel` 已加）。

⚠️ 未完成（待办）：媒体资产那层的磁盘动作仍是自己的实现（含 `ConflictPolicy` 私有枚举），尚未改为委托本接口——只有视频工具接上了。

### 6.7 `IProcessRunner`（NT-081 步骤 3，外部工具执行底座）

| 件 | 位置 | 说明 |
|---|---|---|
| `IProcessRunner` + `ProcessRunRequest` / `ProcessRunResult` | `Shared/Services/IProcessRunner.cs` | `RunAsync(request, timeout, ct)`：隐藏 + 重定向 + **显式 UTF-8**（stdout 严格 / stderr 宽容，`StrictOutput` 开关）+ 先读后等 + 超时 / 取消**杀进程树**；结果带 `Started` / `ExitCode` / `TimedOut` / `Canceled` / `StdOut` / `StdErr` / `OutputDecodeFailed` / `Error` |
| 🔴 输出读取宽限 | `Core/Services/ProcessRunner.cs::OutputDrainGrace` | 进程退出后，输出读取最多再等 **500ms**，到点用**已读到的部分**返回。原因：`ms-settings:` / `.cpl` / `.msc` / `start xxx` 起的 GUI 孙进程会**继承并长期占住** stdout/stderr 管道 ⇒ 等 EOF 会永久挂住 ⇒ 调用方的 `await`（以及 `AsyncRelayCommand.IsRunning` / `IsBusy`）再也不返回，同类按钮一起变灰。⛔ 不许改回 `ReadToEndAsync` 无限等 |
| 实现 | `Core/Services/ProcessRunner.cs` | 无状态；Shell 注册单例（`App.axaml.cs`，与 `IFileOperationService` 同处） |

🔴 **三类进程用法分立，⛔ 不一刀切收口**：① **捕获式外部工具**（脚本 / 命令 / winget，输出当数据解析）⇒ 走本接口（已迁 `ScriptRunner` / `PackageManagerService`）；② **平台能力**（打开文件夹 / 文件 / URL，`UseShellExecute=true`）⇒ VM 直接用 BCL，换平台只改"explorer → xdg-open"这一处；③ **可见控制台 / 提权**（`Verb=runas`）⇒ `QuickActionService.StartVisible`（NT-080 定调：winget 源命令走命令库 + UAC）。`PythonRuntime` 长驻 worker 自有双向协议，不迁。

⚠️ 契约**不含提权参数**：当前无捕获式提权消费方（YAGNI，不预留）；真需要时在可见控制台路径上扩展。

### 6.8 `IModuleBus`（跨模块消息通道，NT-121）

需求规格 §3.6 定的是「模块禁互引，跨模块协作走 `Shared` 契约」。此前该「契约」是空的，于是各模块各自造私货（`WebTransferService` / `VideoTaskStore` / `AssetNavigationStore`）。本契约是**今后唯一的跨模块投递通道**。

| 件 | 位置 | 说明 |
|---|---|---|
| `IModuleBus` | `Shared/Services/IModuleBus.cs` | `Post(topic, payload)` / `object? Take(topic)` / `IReadOnlyList<object?> TakeAll(topic)` / `int PendingCount(topic)` / `IDisposable Subscribe(topic, owner, handler)` |
| `ModuleTopics` | 同上 | 主题常量，命名 `域.动作`。已有 `VideoImport = "video.import"`（载荷 `IReadOnlyList<string>` = 绝对路径） |
| 实现 | `Core/Services/ModuleBus.cs` | 单例；一主题一条 **FIFO 信箱** + 一张弱引用订阅表；单把锁，**派发在锁外**（订阅者顺手再 `Post` 不自锁） |

🔴 **信箱 + 订阅双路，缺一即丢消息**：`NavigationService.NavigateTo` 每次都新建 View，而 `ShellViewModel.OpenTab` 遇到已存在的标签只切 `ActiveTab`（VM 不重建）⇒ 接收方「可能不存在」也「可能常驻」。所以：

| 时序 | 走哪条 | 落点 |
|---|---|---|
| 目标页**还没创建** | 消息入信箱 | 接收方构造时 `TakeAll(topic)` 取存量 |
| 目标页**已经打开** | 订阅当场回调 | 接收方构造时 `Subscribe(...)` |
| 订阅者全被回收 | 退回信箱 | 等页面重建时取（`Post` 内兜底） |

🔴 **订阅按弱引用持有 owner**，调用方不需要注销：
```csharp
_bus.Subscribe(ModuleTopics.VideoImport, this, OnVideoImportPosted);   // ✅ 实例方法
_bus.Subscribe(ModuleTopics.VideoImport, this, p => Handle(p));        // ⛔ 闭包，绑不回 owner
```
`ModuleBus` 存 `WeakReference(owner)` + `handler.Method`，派发时 `MethodInfo.Invoke(owner, payload)`；owner 被 GC ⇒ 该订阅自动作废并从表里剔除（VM 是 `AddTransient`，标签关掉就该被回收，⛔ 不能被订阅表钉住）。

⛔ 不做跨进程（Redis / MQ 对单进程桌面应用过重）；⛔ 不做消息持久化（进程内语义，重启即清）。
现有三处模块内投递保持原样，迁移另开卡（卡：`backlog/framework/NT-121.md`）。

---

## §7 配置与路径

- 一律 `Path.Combine`，⛔ 硬编码 `\` 或 `/`。
- **用户数据**由 `ConfigStoreService` 唯一决定（不在仓库内）：应用根 `%LocalAppData%\NewToys`（环境变量 **`NEWTOYS_HOME`** 整根切换）。
  - 应用根之下分「配置」：`<应用根>\profiles\<配置名>\` → 其下才是 `config / data / logs`；当前配置名记在 **`<应用根>\active-profile.txt`**（在 `profiles\` 之外，否则切换会把自己也换掉）。
  - `ConfigStoreService` 构造时：① 确保 `profiles\<active>\` 存在（首次跑旧结构迁移）；② 暴露 `ConfigDir` / `ModulesConfigDir`（`config\modules`）/ `DataDir` / `LogsDirectory`；③ `ProfilesRoot` / `ActiveProfileName` 供设置页用。
  - ⛔ 模块不自己拼路径，向 `IAppConfigService.DataDirectory` / `ConfigStoreService` 取值。
- **配置切换**：`IProfileService`（Core）负责列出 / 新增 / 重命名 / 删除 / 切换 / 设为默认；**切换 = 重启进程并显式传 `--profile <名>`**（`Process.Start(Environment.ProcessPath + " --profile " + name)` + `Environment.Exit(0)`），⛔ 不写 `active-profile.txt`；**设为默认**才写指针。⛔ 不做运行时热切换。
- **启动参数**：`NewToys.exe [--profile <配置名>]`；解析顺序 = **参数 > `active-profile.txt` > `default`**；非法 / 不存在 → 用默认 + 状态栏一行提示（⛔ 不崩、不静默）。
- **每配置单例**：命名互斥 `Local\NewToys_Profile_<配置名小写>`（`new Mutex(false, name)` + `WaitOne(0)`）；已占用 → 提示「该配置已在运行」后 `Environment.Exit(0)`，⛔ 不静默。⛔ 别用 `Global\`；配置名大小写不敏感 → mutex 名统一小写。
- **新开实例**：每配置行「🚀 新开」= `Process.Start(Environment.ProcessPath, $"--profile {name}")`；窗口标题 = `NewToys · <配置名>`。
- **窗口 bounds 记忆**：`shell.json` 的 `window` 节（`X/Y/Width/Height/State`），按配置各存各的；关闭时存，最小化态不存位置；还原时夹进当前屏幕工作区。
- 平台相关能力（winget / `ms-settings:` / ffmpeg）集中封装；路径穿越防御：外部相对路径 `Path.GetFullPath` 后校验落在目标目录内。
- **配置写盘只有一个实现：`AtomicJsonFile.Write`**（NT-075）。`Core/Services/AtomicJsonFile.cs` = 同目录 `.<名>.<guid>.tmp` → `File.Move(overwrite)`；
  内容为 `""` / `{}` / `[]` / `null` ⇒ **删除目标文件**（懒创建：默认值 = 无文件）。
  ⛔ 新代码不许再用 `File.WriteAllText` 落配置；⛔ 也不要用它写**用户导出文件**（删空文件会让人以为导出失败）。
- **启动清理（NT-075）**：`ConfigStoreService` 构造时 `PruneEmptyConfigFiles()` 递归删 `config\**\*.json` 里的空壳，
  跳过 `.` 开头的目录 / 文件（`.bak-*` 归档不动）。
  🔴 前提约定：**「配置文件不存在」= 用默认值**，所以删空壳语义等价。
  ⇒ 任何 store ⛔ 不许用 `File.Exists(config)` 判「是否首次运行 / 要不要播种」，要标记就用 `.seeded` 这类独立标记文件。

### 7.1 配置文件清单（NT-077 第二批定稿：三层 + 一个总库）

**L0 机器级**（跟随机器，⛔ 不随配置档）

| 文件 | 归属 | 内容 |
|---|---|---|
| `<应用根>\env.json`（默认 `%LOCALAPPDATA%\NewToys\env.json`） | 框架层 | 唯一写入点 = `Core/Services/EnvConfigService`：ffmpeg / 播放器 / Python / pwsh / PS5.1 / **7z** 路径、更新地址、加密 DNS 三项。**覆盖链**（读）：命令行 `--ffmpeg/--player/--python/--pwsh/--pwsh51/--7z/--update-url` ＞ 环境变量 `NEWTOYS_FFMPEG` 等 ＞ `env.json` ＞ 内置默认。覆盖值⛔ 不落盘 |

**L2 框架偏好**（跟随配置档）

| 文件 | 归属 | 内容 |
|---|---|---|
| `config\shell.json` | 框架层 | 五节：`theme` / `window` / `favorites` / `columnLayout` / `ui`。唯一写入点 = `Core/Services/ShellConfigStore`（整节读写 + 原子写 + 空即删）。⚠️ NT-077 前是 5 个独立文件，迁移后旧文件改名 `.legacy` |

**L3 模块设置**（跟随配置档，统一在 `config\modules\`）

| 文件 | 归属 | 内容 |
|---|---|---|
| `config\modules\script-manager.json` | 脚本管理器 | 分区 `script-manager`：脚本目录等 |
| `config\modules\web-tools.json` | 网络工具 | 分区 `web-tools`：默认 UA / 抓取超时 |
| `config\modules\video-tools.json` | 视频工具 | 分区 `video-tools`：最大并发数 |
| `config\modules\media-assets.json` | 媒体资产 | 扫描 / 缩略图 / 命名模板 / 回收站 / 物理操作 / 元数据 |
| `config\modules\package.json` | 系统工具（NT-096 起） | 软件包偏好（静默安装 / 排序列）；🔴 v3.80：源相关字段全部删除（源归命令库命令）；路径⛔ 不改（老用户配置不丢） |

- `IOptionsStore` 的分区名仍**不带路径**（`Validate` 禁分隔符），目录由 `ConfigStoreService.ModulesConfigDir` 决定 ⇒ 分区 `x` → `config\modules\x.json`。
- 应用级行为（**不是机器环境**）留 `app.json`：`MaxConcurrency` + `Launch`（三按钮 + 通用开关）。
- 🔴 **`app.json.VideoDbFolder` 已删除**（NT-077 第二批）：它是跨配置共享却写着某个 profile 绝对路径的缺陷字段。导入/导出默认目录一律 = `IAppConfigService.StoreDirectory`（当前配置档 `store\`），设置页不再有这一行。
- ⛔ 模块不自建 `config\` 子目录：手工数据 / 功能数据一律进本模块库（见 7.3）。

### 7.3 模块自有库（NT-113：一个模块 = 一个 `store\<库键>\<库键>.db`）

- 🔴 **目录分层（NT-113，2026-10-26）**：`<配置根>\store\` = **用户数据**（模块库 / 抓取规则 / 流程 / 运行记录 / 脚本实体，删了不可恢复）；`<配置根>\cache\` = **可重建**（缩略图 / 页面缓存 / 下载中间产物 / 升级前快照 / 恢复副本）。旧 `data\` 目录已撤销，由 `Core/Services/DataMigrations/StorageLayoutV2` 一次性迁移（整目录 rename + 拆库 + 分家，可重入，完成标记 `store\.layout-v2.done`）。
- 🔴 **配置里的旧绝对路径**（`scriptsFolder` / `thumbsDirectory` 这类用户录入值）不会被目录改名带走 ⇒ 同文件内的 `RewriteLegacyConfigPaths` 扫 `config\**\*.json`，把 `<配置根>\data\<rest>` 改成 `store\<rest>` / `cache\<rest>`（**映射目标存在才改**，别的机器 / 离线盘原样留着）。它用**独立标记** `store\.layout-v2-paths.done` —— 布局早已完成的用户也会补跑这一次。模块侧另有运行时兜底（`EffectiveScriptsFolder` / `ThumbsDirectory` 读到失效的 data 路径就地改回）。
- 🔴 **库文件**：`store\assets\assets.db`（13 张资产表）/ `store\video\video.db`（`video_media` + `video_docs`）/ `store\web\web.db`（`web_docs`）/ `store\script\script.db`（`script_docs` + `pc_observations` / `pc_runs`）。
- 🔴 **模块与库一一固定绑定**：每个模块在自己的程序集里派生一个 `XxxDb : SqliteDatabase`（`AssetsDb` / `VideoDb` / `WebDb` / `ScriptDb`）并注册为单例，模块内服务只注入**自己的派生类型** —— 同一个 DI 容器里 4 个库互不干扰，也不会误拿别人的库。⛔ 没有库指针、没有 UI 选库：**NT-076 否决的是「多库切换」，不是「一模块一库」**。
- **入口唯一**：`SqliteDatabase`（连接 + `meta` 表 + WAL + `ApplySchema`）；建表统一入口 = `LibrarySchemaService.EnsureAll()`，跑在 `InitializeAllDeferred` 第一步。
- **契约**：`Shared/Services/ILibrarySchemaContributor`（`ModuleKey` / `DatabaseKey` / `SchemaVersion` / `EnsureSchema(): SchemaOutcome`），每个模块一个，在 `RegisterServices` 注册。版本写各库的 `meta['schema.<模块键>']`，同时写 `PRAGMA user_version`；库里版本更高 ⇒ 返回 `Ahead`，只告警不降级。
- **不可逆操作先快照**：`SqliteDatabase.ApplySchema` 在「库里有旧版本」时先 `VACUUM INTO cache\db-backups\<库名>-pre-v<n>-<时间戳>.db`（⛔ 不许 `File.Copy`，WAL 下会漏事务）；首次建库不快照（空文件没意义）。
- **失败留证据**：写 `logs\migrate-<时间戳>.log`；「设置 → 数据管理」页顶部（`DataView`）红字显示。
- **表前缀纪律**：模块只许 `<模块前缀>_*`（`asset_` / `video_` / `script_` / `web_`），外键挂 `asset_id`，⛔ 不建第二份文件索引。历史例外：脚本观测表叫 `pc_*`（NT-068 前的 PcTools），改名要迁移数据。
- **内容型 JSON → 文档表**：`script_docs` / `web_docs` / `video_docs`，结构 `(name, payload, updated_at)`，`payload` = 整份 JSON 原文。读写统一走 `Core/Services/JsonDocStore`，旧文件由各模块贡献者用 `JsonDocMigration.ImportOnce` 一次性搬入并改名 `.legacy`（⛔ 不删）。要按字段查时用 `json_extract(payload,'$.x')`。
- **`store\` 进不进备份由用户勾**：`store\*\*.db` 是独立备份节（`data.db`，逐个库 `VACUUM INTO`）、`cache\assets\thumbs` 是 `data.thumbs`、`store\scripts` 是 `data.scripts`，默认**不勾**；勾上才导出。见 §7.4。

### 7.4 备份路由（NT-078：zip 分节）

- **容器 = 一个 zip**（`System.IO.Compression`，⛔ 不引第三方压缩库）：`manifest.json` + 每节一个 `<节ID>.zip`。理由：db / 缩略图放不进 JSON，zip 还能「只恢复其中一节」。
- **唯一实现** = `Core/Services/BackupService`；节的契约 = `Shared/Services/IBackupSection`（`Id` / `Title` / `Description` / `Scope` / `Order` / `DefaultSelected` / `IsMachineBound` / `SchemaVersion` / `EstimateBytes()` / `ExportTo(Stream)` / `RestoreFrom(ZipArchiveEntry)`）。
- **节的落点**：框架节在 `src/Shell/Services/`（`shell` = `config\**\*.json`、`env` = 应用根 `env.json`、`data.*`）；模块节放各 `Module.*`，`RegisterServices` 里 `services.AddSingleton<IBackupSection, XxxBackupSection>()`（⛔ 模块不引 Shell）。
- **节 ID / 顺序**（发布后⛔ 不改）：`env`(Env,10) / `shell`(Shell,20) / `module.<模块键>`(Module,30+) / `data.db` `data.thumbs` `data.scripts`(Data,90/91/92)。
- **默认勾选** = 设置备份：`shell` + 全部 `module.*`；`env` 与 `data.*` **默认不勾**（前者含绝对路径、后者可能几百 MB）。
- **恢复落点**：设置节 = 覆盖原文件，**覆盖前整目录归档到 `config\.bak-<时间戳>\`**；数据节 = **写成新副本**（`store\restore-<时间戳>\`，一次恢复里所有数据节共用同一个目录，由 `BackupPaths.BeginRestore` 会话决定），⛔ 不覆盖当前库。恢复完成会弹框给出副本路径。
- **已实现的数据节**（NT-113 修订）：`data.db` = `store\*\*.db` 全部模块库的 `VACUUM INTO` 快照（扫文件、跳过 `store\legacy\` 归档与 `store\restore-*` 副本；内层不压缩）；`data.thumbs` = 缩略图目录（从 `config\modules\media-assets.json` 的 `thumbsDirectory` 解析，空 ⇒ `cache\assets\thumbs`，Shell **只读字段、⛔ 不引模块**）；`data.scripts` = 脚本实体文件（由 **`Module.ScriptManager`** 注册：默认脚本目录 + 各库 `RootPath`，`<别名>/<相对路径>` + `_roots.txt` 记录来源）。
- 🔴 **`VACUUM INTO` 只有一处实现**：`SqliteDatabase.VacuumIntoFile(targetPath, out error)` —— 数据节与资产页「💿 另存库文件」共用，⛔ 别再写第二份 SQL。
- **批次 C 的决定**：4 个模块**不**各注册一个「模块设置节」—— `shell` 节已递归打包 `config\**\*.json`（含 `modules\*.json`），再做模块节只会让同一份文件在包里出现两次。模块自主注册的节只用于**模块自己的数据**（当前只有 `data.scripts`）。
- **体积闸**：勾了数据节且估算合计 > 100 MB ⇒ 导出前弹确认（⛔ 不做进度条 / 取消，卡内拍板）；节大小在**后台线程**估算（缩略图可能几万个文件）。
- **恢复前校验**：`manifest.format` > 当前 ⇒ 拒绝；`manifest.app` 主版本不一致 ⇒ 拒绝；节的 `schema` 高于当前 ⇒ 跳过该节并提示升级。
- **`VACUUM INTO` 取快照**（⛔ `File.Copy`：WAL 下会漏未落盘事务）；目标文件必须不存在 ⇒ 先落临时文件名再打包。
- **路径防御顺序固定**：先判 `..` / 绝对路径（拒绝）→ 再跳过 `.seeded` / `.bak` / `.legacy`（顺序反了会放过 `../evil.ps1`）。实现 = `Shared/Services/BackupPaths`。
- **旧格式只读**：v1 单 JSON 的 `Files[]`（以及早期 DataView 的 `files[{path,content}]`）仍可导入，⛔ 不再产出。

### 7.2 三配置档（debug / default / release）

- 三档**永久驻留**开发机，随时切换、**不丢数据**（⛔ 任何自动清理）。
- **Debug 构建首启播种**：`default`（既有；缺失则按现状播种）、`debug`（= 复制 `default` 内容）、`release`（= 干净初始态）。
- 动作是 **「重置为初始态」**（按播种源填回），⛔ 不提供「删除」。
- ⛔ 商店版配置（条件播种 / 排除 dev 文件 / 仅 `default`）归需求 §2.1 远期，**未实现**。

---

## §8 异常处理

- 导航 / 模块加载 / 主题应用等边界处 `try-catch`，失败不崩溃；至少写日志或兜底 UI，**不得吞异常无反馈**。
- 全局兜底：`AppDomain.UnhandledException` / 启动异常捕获，崩溃给可读错误窗。

---

## §9 构建与版本

- 构建 / 发布 / 环境切换**由用户本地执行**（`pwsh -File .\scripts\build.ps1 -Configuration Debug|Release`）；AI 改完 `src/` 后列出需用户验证的点，**不代跑构建**。
- ⛔ 禁止声称「已编译通过」；没跑过写「未编译，请用户本地执行 `build.ps1` 验证」。纯文档变更注明「不涉及编译」。
- ⛔ **脚本只用 PowerShell 7（`.ps1`），禁止 `.cmd` / `.bat`**；fail-loud（成功 `exit 0` 自关窗口、失败 `trap` 暂停）。

**单元测试**（`src/NewToys.Tests`，2026-10-21 新增）：
- 只测**纯函数**（`FixedWidthTable` / `TsvRows` / `PackageOutputParser` 这类「外部输出 → 行模型」的解析器）——⛔ 不 new VM、⛔ 不碰 Avalonia / 文件系统 / 进程。
- 现有覆盖面（`src/NewToys.Tests`）：包管理器输出解析（`ParserTests`）/ 系统信息输出（`SysInfoOutputParserTests`）/ 列表页基类（`ListPageBaseTests`）/ 页面契约（`PageCatalogTests` + `ViewNamingTests` + `MenuServiceTests`）/ **备份路径防御（`BackupPathsTests`）** / **ffmpeg 命令拼装（`FfmpegArgsTests`：裁剪取偶、编码器映射、目标体积反算、copy→libx264 升级、软字幕强制 mkv、GIF 调色板链、多段剪切的 filter_complex）**。
- 测试工程按需求引被测模块（当前 = `Module.ScriptManager` / `Module.SystemTools` / **`Module.VideoTools`** / `Shared` / `Core`）；⛔ 别为了测试把整个 `Shell` 拉进来。
- 跑法：`pwsh skill/scripts/accept.ps1 -Test`（构建后追加，秒级）或直接 `dotnet test src\NewToys.Tests`。
- `internal` 的解析类**不为测试改 public**，用被测类型所在模块的 `<InternalsVisibleTo Include="NewToys.Tests" />` 打开（当前 = `Module.SystemTools.csproj`，NT-096 迁入软件包解析器）。
- 产物落 `artifacts\tests\`（Release 的 `OutDir` 被项目级覆盖）——⛔ 不许进 `artifacts\release\`，那里是交付产物区，多出 TestHost 会污染产物核对。

**版本号**（`src/Directory.Build.props` 编译时自动生成，⛔ 禁止手写）：
- `InformationalVersion` = `1.<yyMMdd>.<HH>.<mm取整到十位>`（显示用，例 `1.261030.01.40`）
- `FileVersion` = `1.<yyyy>.<MMdd>.<HHmm>`；`AssemblyVersion` 固定 `1.0.0.0`
- 「设置·关于」读 `InformationalVersion`；左树底部 `TreeVersionText` 显示同一值。

**组件版本**（关于节，`LoadedComponentProbe`）：
- 只读**当前进程已加载**的程序集（⛔ 不扫盘、⛔ 不额外加载 DLL）：外壳 = 入口 `NewToys` + `NewToys.*`，模块 = `Module.*`；热加载的模块在 `AppDomain.GetAssemblies()` 里，天然被统计。
- 每项显示 名称 / 版本（`InformationalVersion`，去 `+sha`）/ 编译时间（从版本串解析）/ 模块与外壳的差异标记（`⚠ 比外壳新/旧`，比较前把日期段归一成 8 位，避免历史 `1.yyyyMMdd.*` 旧 DLL 被误判更新）。
- 刷新时机：构造 VM 时 + 「🔄 刷新」+ 「🔄 重载模块」成功后。

**产物路径**：

| 产物 | 路径 |
|---|---|
| Debug 输出 | `artifacts/bin/<项目名>/Debug/net10.0/` |
| Debug 主程序 | `artifacts/bin/Shell/Debug/net10.0/NewToys.exe` |
| Release 输出根 | `artifacts/release/` |
| Release 模块 DLL | `artifacts/release/Modules/Module.*.dll`（直出，零拷贝） |
| 发布产物 | `production/NewToys_<运行时>/` |
| 测试环境根 | `artifacts/env/test/` |
| **单元测试产物** | `artifacts/tests/`（`NewToys.Tests`，⛔ 不进 release） |
| 用户数据 | ⚠️ 不在仓库内：`%LocalAppData%\NewToys\{config,data,logs}` |

AI 只读 `src/`，⛔ 不读 `artifacts/ bin/ obj/ .vs/`。

---

## §10 技术债台账（B 类）

> 本表 = **技术债（Tech Debt）**：当前做法是「需求 vs 工程现实」的妥协，非永久铁律。**变更条件满足 + 用户同意 + 本表登记**后方可调；未登记不得擅改。

| 项 | 理想 | 当前折衷 | 变更条件 | 影响面 |
|---|---|---|---|---|
| **B1 编译绑定** | 开启编译绑定 | 全工程关闭，不写 `x:DataType` | **保持关闭**（用户拍板） | 全部 View |
| **B2 VM 基类** | 统一继承 `Shared/ViewModelBase` | 模块内多直接继承 `ObservableObject`，仅 Shell/Home 用 `ViewModelBase` | 某模块改造时逐模块统一 | 各模块 VM |
| **B3 全局样式** | 显式 `x:Key` 样式键 | 用 `Classes` 隐式样式 | 收益足够大时整体改造 | `App.axaml` |
| **B4 导航服务** | 命名与实际职责一致 | `NavigationService` **不是** `ShellViewModel` 的替代/重复实现，而是**出站**通道（模块 → Shell，经 `Navigated` 事件）；入站解析（Key → View → VM）归 `ShellViewModel.Resolve*` | 无需合并；若要清理，只把该服务定位写清（出站事件总线） | Shell/Core |
| **B5 平台后端** | 完全跨平台 | 后端仍调 winget / ffmpeg 命令行；`Package*` 类型已中立；**解析器未分层**（winget 启发式解析与 VM 字段映射混在模块内） | 迁 Debian 时替换：先发布链冒烟（RID 条件化 + 守卫收口），再按 §2.1 抽适配器与通用 TSV 解析 | Core 服务 / Module.ScriptManager |
| ~~**B6 仓库根 slnx**~~ | 唯一入口 `src/NewToys.slnx` | ✅ **已结清（2026-10-25 核查：仓库根已无 `NewToys.slnx` 副本）** | — | 仓库 |
| ~~**B7 Modules 冗余 DLL**~~ | `Modules\` 仅含模块 DLL | ✅ **已定性为非缺陷（2026-10-25）**：实测 `Private="false"` / `ExcludeAssets=runtime` 均压不住 ProjectReference 复制（见 `src/Directory.Build.props` 末尾注释）；`ModuleLoader` 又跳过「已加载的同名程序集」⇒ 永远不会加载成第二份，⛔ 别再为此改 csproj | — | Release 产物 |
| **B10 运行期热加载** | 任意模块随时加载 / 卸载 / 替换 | 仅支持「新增未加载过的模块」；容器已 build，模块不能注册新服务 | 引入可卸载 ALC + 模块级容器 | ModuleLoader + Shell |
| **B11 反射使用** | 最小魔法 | 模块装配 / 热加载 / 视图解析三处用反射 | 机制必需暂不改；⛔ 仅限这三处 | ModuleLoader / ShellViewModel |
| **B12 前进/后退** | 所有页面丰富页内导航 | 仅 `video-list` 有目录语义；多数页灰显 | 各页逐步实现 `IHistoryProvider` | 各模块页面 |
| ~~**B14 媒体资产用户数据落库**~~ | 手动数据进备份 | ✅ **已结清（NT-113）**：标签 / 分类 / 集合 / 关联随 `store\assets\assets.db` 落 `store\`，已是用户数据区，`data.db` 备份节覆盖 | — | — |
| **B15 数据驱动图标** | 全矢量 | 二级菜单 `icon` / 首页一键按钮 / 模块页内数据行仍 emoji | 收敛工作项 **NT-047** | 各模块注册 + 首页 |
| **B16 `SetWindowRgn` 方角硬手段** | 官方 corner preference 稳定生效 | 保留 `ApplySquareRegion()` + `SetCornerPreferenceNoRound()` 双保险 | NT-042 已裁决保留（阴影代价已证伪） | `ShellView.Win32.cs` |
| **B17 窗口装饰 Win32 补丁残留** | 零补丁 | 方角两处 + `WM_NCCALCSIZE` 零内缩 + 贴靠矩形纠偏 `FixTiledGeometry`（Windows 独有例外）；样式位补位已删 | 见 B16 + Avalonia 提供等价开关；迁非 Windows 前须补外框门控与窗口记忆回退 | `ShellView.axaml(.cs)` + `ShellView.Win32.cs`（**Windows 专属代码全在此 partial**）+ `App.axaml` |

**已结清并移出本表**：B8（闲置转换器，已删）/ B9（骨架页面，零骨架）/ B13（详情面板上提，16/16 声明式）—— 明细见 `git log`。

---

## §11 跨模块待办（唯一收口处）

> 涉及 2 个以上模块、或改 Shared / Core / Shell / UI 的待办一律登记在此，**不在模块 md 里重复**。
> 任务卡放 `backlog/framework/`（同样 5 张滑动窗口）。台账状态以《项目状态与路线图》三、为准。

| NT | 内容 | 状态 |
|---|---|---|
| NT-047 | **数据驱动图标矢量化**：二级功能菜单 34 项 `icon` / 首页一键按钮 / 模块页内数据行（⛔ 静态装饰 emoji 不动） | 📝 草稿（**用户 2026-10-19：暂时不做**） |
| NT-079 | **可成长性**：模块开关 / 命令面板 / 采集器骨架生成 | 📝 草稿 |
| NT-081 | **跨平台中立化（远期）**：RID 条件化 ✔ + Windows-only P/Invoke 守卫收口 ✔（守卫层）+ 进程底座 `IProcessRunner`（Shared 契约 / Core 实现 / Shell 单例；三类进程用法分立见 §6.7）✔ + 解析分层（`FixedWidthTable` 通用机械 / `TsvRows` 机读 / winget 启发式留模块）✔ + apt 只读适配器 `AptPackageService`（按平台二选一注册，页面零改动）✔；⛳ 未做：Linux 实机验证（无环境）/ P/Invoke 物理集中 / apt 写操作（需 sudo）。约束见 **§2.1** | 🔄 执行中（步骤 1~5 已实施 2026-10-19，待本地 build；卡：`backlog/framework/NT-081.md`） |
| NT-083 | **Python 运行时底座**（跨模块共用 ⇒ **落 Core**，已批准）：`IPythonRuntime`（`ToolsRoot` / `Probe` / `RunAsync` / `Shutdown`）+ `Core/Services/PythonRuntime`（长驻 worker，行分隔 JSON / `-u -X utf8` / 超时杀进程树 / traceback 回传 / 串行 `SemaphoreSlim` / 空闲 10 分钟自动退出）+ 共享环境 **`%LOCALAPPDATA%\NewToys\tools\py`**（与 `env.json` 同级）⇒ 依赖安装交给随包脚本，**C# ⛔ 不写 pip 逻辑**（播种成集合「环境维护」，见 `modules/script-manager.md` §2.5）；状态在「设置 → 运行环境 → Python 共享环境（venv）」 | ✔ 已完成（批 1~4，2026-10-19；⏳ 待用户 build + 实机功能验收） |

| NT-088 | **执行器上提 Core**（`ScriptRunner` + `InterpreterResolver`：`Module.ScriptManager` → `NewToys.Core`）：为新增的 `system` 系统工具模块提供跨模块执行契约（模块禁互引），并为命令链（NT-092）铺路；行为零变化，纯重构 | 🔄 **已实施（⏳ 待 build 验收）** —— 2026-10-22 用户指令「执行 nt-088/089」= 批准（卡：`backlog/framework/NT-088.md`）|
| NT-094 | **重划分收尾**：设置拆分（`config\web\*` 瘦身：超时 / 并发 / 解析器 → script）+ 一级菜单显示名「脚本中心」+ `system` 组一级图标落地 + 备份段重复显示检查 | 📝 草稿（待细化；依赖 NT-088 ~ NT-093；卡：`backlog/framework/NT-094.md`） |

**已结清并移出本表**（结论已蒸馏进正文，状态与日期以台账为准）：NT-064 分页数据层 / NT-066 标题栏搜索扩范围 / NT-067 信息展示脚本化底座 / NT-068 脚本管理器重构 / NT-069 跨模块文件操作契约 / NT-075 配置写入基建 / NT-076 单库收编 / NT-077 配置分层 / NT-078 备份路由 / NT-085 `DynamicColumnGrid` 上提 UI 库。

### 11.1 技术债：注释治理（2026-10-25 落地，机制化而非一次性清洗）

> 背景：`src/` 65k 行里曾有 **11.4k 行注释（17.4%）**，其中 618 行是版本/轮次叙事、1060 行带 `NT-xxx` 引用。
> 注释腐烂的根因不是「写得多」，而是**缺少生命周期规则** —— 每轮追加「vX.Y 第 N 轮：…」，旧结论又不删。
> ⛔ 「搬进文档」不是解法：文档离代码更远，没有规则只会烂得更快。**能机器判定的规则必须变成 lint。**

**注释三分法**（判据 → 归宿；完整表在 `skill/references/tech-guard.md` §3.1）：

| 类型 | 判据 | 归宿 |
|---|---|---|
| A 契约 | 调用方必须知道（参数语义 / 单位 / 异常 / 生命周期） | 留 `/// <summary>`，必须有 |
| B 约束 | 「不许这么写」且可机器判定 | **变静态检查规则**，代码里 ≤1 行提示 |
| C 意图 | 为什么这样设计、踩过什么坑、代价是什么 | 文档一处（本节 / `modules/*.md` §5），代码留稳定 ID |
| D 叙事 | 哪一版、第几轮、原来是 A 后来改 B | **删**（git log / git blame 的职责） |

**已上线机制**：`skill/scripts/check-comments.ps1`（已接入 `accept.ps1` 静态检查第 5 项）
- `C-01` 版本/轮次前缀行 · `C-02` ≥6 行注释块 · `C-04` 历史叙事词 = **WARN**（不阻断）
- `C-05` **两个程序集同名 View 类 = ERROR** —— 反射导航靠类名找页，重名会静默错页，这是唯一能提前拦它的检查
- `C-03` 仅出注释率 Top5 排行，**不告警**（model / 接口类的属性注释就是契约，审美判据做 lint 必然烂成噪音）

**存量处置原则**：⛔ 不清零、⛔ 不排一次性清洗批次（600+ 处无功能改动混进一次 build，出错无法定位）。
按「改到哪清到哪」消化；lint 只保证**新增代码不再产生**。已清理样板：`Core/Services/ModuleLoader.cs`、`Shell/ViewModels/ShellViewModel.cs`。
**唯一例外（2026-10-25 用户拍板）**：`NT-xxx` 编号引用一次性全清（1121 处 / 245 文件，纯注释），编号→结论收口到 **§11.2**；
版本号前缀（`vX.Y`）与轮次叙事仍按「改到哪清到哪」，⛔ 不再按批次清洗。

**配套测试**：`src/NewToys.Tests/ListPageBaseTests.cs`（26 个测试方法 / 33 个用例，覆盖分页溢出 / 隐藏列写 0 占位 / 锁定列不可拖 / 筛选「且」语义 / 页大小跨页收敛）。跑法：`pwsh skill/scripts/accept.ps1 -Test`。

### 11.2 NT 编号索引（代码注释里不再写 NT 号，追溯查这里）

> 2026-10-25：`src/` 注释里的 1121 处 `NT-xxx` 引用已全部清除（纯注释改动，零功能变化）。
> 理由 = 注释三分法的 D 类：**编号属于叙事，不是契约**。但编号→结论的对应关系要留一份，
> 因为任务卡会随滑动窗口淘汰（每目录 ≤5 张），全靠 git log 找回成本太高。
> 下表覆盖**代码里出现过的全部编号**（一行结论）；细节与验收状态以《项目状态与路线图》三、和 git log 为准。

| NT | 一行结论 |
|---|---|
| NT-001 | web-http 页面实装（最后一个骨架页） |
| NT-002 | 软件包页收尾：解析器实机 + 批量队列 |
| NT-005 | 模块热加载（`IModuleReloadService`） |
| NT-006 | 快捷设置：可见终端运行 / 隐藏运行 / 切 shell + 导入导出 |
| NT-007 | 设置与脚本：导入导出 + 右键编辑 / 运行如双击 |
| NT-008 | 软件包：可升级对齐 / Pin 显示 / 详情修正 / 导入导出 |
| NT-009 | 首页：收藏配色 + 一键操作固定底部 |
| NT-010 | 通用样式统一（按钮 / 输入框水印 / 弹层） |
| NT-012 | 视频预览：旋转播放按钮 + 命令卡 |
| NT-013 | ffmpeg 卡：剪辑转码输入对齐 + 默认 H.265 / CRF23 |
| NT-014 | 媒体资产模块骨架（项目 + 菜单 + 空页面） |
| NT-015 | 媒体资产数据层（13 表）+ 扫描 + 任务队列 |
| NT-016 | `assets-all` 列表页（详情 + 工具条 + 筛选） |
| NT-018 | 标签 / 分类 / 集合三页 + 操作日志表 |
| NT-019 | 物理操作（日志先行 / 防穿越 / 回收站）+ 任务中心 |
| NT-020 | 存储源页 + 移动硬盘离线 / 上线校准 |
| NT-021 | 媒体资产设置页（元数据 + 缩略图） |
| NT-022 | 列表 JSON 导入导出 + 用户数据导出 |
| NT-026 | 媒体资产多库融合（工作副本模型） |
| NT-030 | 移动 / 复制 UI 入口 |
| NT-031 | 视频类型专属列 |
| NT-032 | 修扫描「大小 + 修改时间」识别键碰撞 |
| NT-033 | 修多库融合重复并入产生的孤儿资产 |
| NT-034 | 修最大化 / 分屏圆角 |
| NT-035 | 左树一级图标矢量化 |
| NT-037 | 左树一级展开整组滚入视野 |
| NT-038 | 三配置档（debug / default / release） |
| NT-039 | 窗口外框 1px + 激活 / 失焦两态 |
| NT-041 | 清理 Win32 窗口补丁（逐处删 + 逐处回归） |
| NT-042 | DWM 阴影与方角方案复核（`SetWindowRgn` 去留） |
| NT-044 | 标题栏全屏按钮 + `F11` / `Esc` |
| NT-045 | 图标实现统一：全框架矢量 `Geometry` |
| NT-046 | 图标收尾（最小化居中 / 拼图 / 全屏改型） |
| NT-048 | 分屏贴靠几何不收敛（**已作废**：根因是零内缩窗口，改 `FixTiledGeometry`） |
| NT-051 | 设置页合并：主题 + 关于 → 通用 |
| NT-052 | 「重载模块」移入功能清单页 |
| NT-054 | 详情卡声明式三区模型 + 字段 Kind |
| NT-055 | 工具条 Shell 声明式承载（`IToolbarSource`） |
| NT-056 | 设置总表：路径 + 配置合并一表 |
| NT-057 | 系统托盘增强 + 托盘图标用应用图标 |
| NT-058 | 详情卡 `IconBar` 图标动作组 |
| NT-059 | 「设置与脚本」改脚本索引库 |
| NT-060 | 干净启动：空态引导，删示例数据 |
| NT-061 | 配置下沉 `IOptionsStore` + 各模块设置页 |
| NT-062 | 应用级设置归位 Shell（撤 `Module.Settings`） |
| NT-063 | 控件库 `NewToys.UI` + 列表页控件化 |
| NT-064 | 列表分页数据层（LIMIT/OFFSET + 排序下推） |
| NT-066 | 标题栏搜索扩到全部菜单项 + 设置节 |
| NT-067 | 信息展示页脚本化（`ScriptRunner` + manifest） |
| NT-068 | 脚本管理器：改名 + 数据库化 + 集合 |
| NT-069 | 跨模块文件操作契约 `IFileOperationService` |
| NT-070 | 启动提速：模块初始化两段式（`InitializeDeferred`） |
| NT-072 | 集合页防误删（使用中沉底 + 二次确认） |
| NT-073 | 脚本 / 命令多库索引 + `kind` 真正生效 |
| NT-074 | 软件包管理整套下放模块 |
| NT-075 | 配置写入基建（`AtomicJsonFile` / 默认值不落盘） |
| NT-076 | 单库收编（`script.db` → `newtoys.db`） |
| NT-077 | 配置分层（env / shell / modules 三层） |
| NT-078 | 备份路由（`IBackupSection` + zip 分节） |
| NT-080 | 软件包定位收敛 + winget 源模型重写 |
| NT-081 | 跨平台中立化（守卫层 / `IProcessRunner` / 解析分层） |
| NT-082 | 软件包集合 + 集合右键发送 |
| NT-083 | Python 运行时底座（共享 venv） |
| NT-084 | 抓取规则两页（目录 + 规则编辑器） |
| NT-085 | `DynamicColumnGrid` 上提 UI 库 |
| NT-086 | 爬虫脚本面板（期①） |
| NT-087 | 爬虫脚本下载（期②：试下载 / 批量 / next 分页） |
| NT-088 | 执行器上提 Core（`ScriptRunner` / `InterpreterResolver`） |
| NT-089 | 脚本中心：规则侧搬家 |
| NT-090 | 脚本中心：运行台搬家 |
| NT-091 | 新建 `Module.SystemTools`（命令库 + 收藏夹） |
| NT-094 | 设置拆分与文案收尾 |
| NT-096 | 软件包页迁入系统工具 |
| NT-097 | 系统工具页面重构（命令库对标脚本库） |
| NT-098 | 命令编辑增强（参数预检 / 试跑 / 播放小样） |
| NT-099 | 转码 GIF 输出（单遍调色板） |
| NT-100 | 字幕：软字幕封装 / 硬字幕烧入 |
| NT-101 | 多段剪切 + 拼接（`trim`/`atrim` → `concat`） |
| NT-102 | 画面裁剪 + 黑边探测（`cropdetect`） |
| NT-103 | 音轨：静音 / 音量增益 / 替换 |
| NT-104 | 按目标体积压缩（ABR 码率反算） |
| NT-105 | 时间轴区间标记（`TimelineTrack`） |
| NT-106 | 书签树状展开（整页单树） |
| NT-107 | 页面注册编译期化（`RegisterFunction<TView>`）+ 菜单注册回归测试 |
| NT-121 | 跨模块消息通道 `IModuleBus`（信箱 + 弱引用订阅）+ 资产库 → 视频工具交接 |


---

## §12 窗口外壳（Windows Win32 补丁层）

> 唯一实现处：**`src/Shell/Views/ShellView.Win32.cs`**（`partial class ShellView`，644 行；业务逻辑仍在 `ShellView.axaml.cs`）。平台门控：钩子只在 `Win32Properties` 可用（= Windows）时装，P/Invoke 全部走 `OperatingSystem.IsWindows()` 内联守卫（见 §2.1）。
> 关联：需求 §四（窗口 CSD）、`ui.md` §4（CSD 现役配置）、`NewToys-软件说明书.md` §四（贴靠分屏的用户语言）。

### 12.1 补丁清单（2026-10-21 现状）

| # | 补丁 | 干什么 | 开关 / 门控 | 什么时候可以删 |
|---|---|---|---|---|
| 1 | `WM_NCCALCSIZE` 零内缩（吞 `wParam==1`） | 客户区 = 外框，消掉 ≈8px 非客户区边框（贴靠态系统不画那圈 → 露缝） | 无开关，无条件（最大化时放行） | 不再自绘标题栏 / 换官方完整装饰 |
| 2 | `FixTiledGeometry` 贴靠几何纠偏 | 把系统给的 tile 矩形纠回精确半屏 / 四分屏（#1 的必然副作用），并**记账贴靠前的自由外框**供取消贴靠还原（见 §12.2 第 5、6 条） | 只认 tile（容差按 DPI 缩放，见下条），其余不动 | 不再零内缩（即 #1 删掉） |
| 2b | 贴靠容差按 DPI 缩放（`SnapToleranceFor`） | 8px/边 是 96 DPI 的量；固定 32px 在 200% 屏失效 ⇒ 按 `GetDpiForWindow()`/96 放大，`Clamp(32, 128)` | 缺 `GetDpiForWindow` 导出 → 退回 96 DPI 基准 | 始终保留 |
| 3 | DWM 圆角 `DWMWCP_DONOTROUND` + `PostChromeReassert` 保序重压 | Win11 顶部圆角压制；框架 `ShowWindow/ExtendClientArea` 会回写 → 必须在其**之后**再压一次 | `Dispatcher.Post(Send)` 保序，重入闸门 `_inChromeSync` | 只在 Win11 有意义；Win10 无效果无害 |
| 4 | `SetWindowRgn` 矩形窗口区域（方角硬手段） | 圆角偏好"不听话"时的兜底（MS 文档：用了窗口区域系统不再套圆角） | `UseSquareRegionHack` 开关（NT-042 复核用） | **与 #3 语义重复**，确认 #3 稳定后可整段删除（−60 行 + 一个开关） |
| 5 | 最大化 / 全屏时清 region | #4 的配套（否则区域与最大化几何打架 → 露边 / 裁内容） | 跟着 #4 | 跟着 #4 |
| 6 | 窗口尺寸 / 位置记忆走**外框物理像素** | 客户区口径进出会被每轮再加一圈边框（实测每次变大 16px） | `WindowBoundsService` | 不再零内缩后，改用客户区口径 |

⛔ **历史死路（已删，别再抄回来）**：NT-048「按屏幕自检强行 SetWindowPos」、手搓 `SetWindowLongPtr(GWLP_WNDPROC)` 子类化（已由官方钩子取代）、手工补 `WS_CAPTION/WS_THICKFRAME` 样式位、`DwmExtendFrameIntoClientArea(0)` 清零扩展帧（见 §12.2）。

### 12.2 贴靠几何：根因与修法（2026-10-21 定案）

**现象**：贴靠半屏后可见区比半屏宽 16px（左贴靠向右溢出、右贴靠向左溢出）。用户感知的"第一次宽、第二次正常"是错觉 —— 右贴靠的溢出被左侧窗口盖住，看着像齐平。

**根因**：系统算 tile 矩形时按「外框 = 客户区 + 边框（本机 8px/边）」换算。普通窗口那圈 NC 边框正好把这笔换算吃掉，本窗口零内缩（#1）没有边框 ⇒ 多算的 16px 全变成可见内容。

**实测对照**（1920×1080 / 96 DPI / rcWork = 1920×1040，半屏 = 960）：

| 窗口 | 贴靠后 window rect | 可见区（DWM `EXTENDED_FRAME_BOUNDS`） | 判定 |
|---|---|---|---|
| 记事本（标准边框 7px） | (−7,0)–(967,1047) 974×1047 | 960×1040 | ✅ 系统算对 |
| Edge（标准边框 8px） | (−7,0)–(967,1047) 974×1047 | 960×1040 | ✅ 系统算对 |
| 本窗口（零内缩）修前 | (0,0)–(976,1040) 976×1040 | 976×1040 | ❌ 宽 16 |
| 本窗口 · `SetWindowRgn` 生效时 | (−7,0)–(983,1047) 990×1047 | 990×1047 | ❌ 宽 30 / 高 7（多叠一层边框） |

**修法**（`FixTiledGeometry`）：

1. **两个时机各打一次**：`WM_WINDOWPOSCHANGING` 改写 `lParam` 指向的 `WINDOWPOS`（无闪烁、多数情况一次到位）；`WM_WINDOWPOSCHANGED` 兜住"改写后又被补一层边框"的路径（实测开着窗口区域时必现）。
2. **`SWP_NOSENDCHANGING` 是必需品**：不带的 `SetWindowPos` 会被框架按「客户区 + 边框」重算回来（960 → 976），改不动。
3. **判据 = 几何指纹，不依赖 flags**：系统贴靠的 `flags=0x300204` 未文档化 ⇒ 改认「尺寸 ≈ 半屏 / 四分屏 ±32px 且贴工作区边缘」。
4. **`MonitorFromRect` 而非 `MonitorFromWindow`**：跨屏贴靠那一刻窗口还在旧屏上。
5. **幂等闸门 + 最大化排除 + 全包 `try/catch`**：已精确则不动（防 `POSCHANGED` 内 `SetWindowPos` 自激）；最大化外框 `(−8,−8,1936,1056)` 离 tile 只有 16px，必须排除；纠不了就维持原样。
6. **同一笔换算会反向生效**：取消贴靠（`Win+↓` / 拖离边缘）时系统会把窗口还原成"大一圈"的外框 ⇒ `FixTiledGeometry` 自己记账 `_freeFrame`（自由态持续更新），首次进入 tile 时冻结成 `_preTileFrame`，退出贴靠的第一个非 tile 请求改写回该外框。⚠️ 必须排除「铺满显示器」的请求 —— 最大化与取消贴靠是同一条路径，误判会让最大化再也进不去。
7. **容差按 DPI 缩放**：`SnapToleranceAt96Dpi = 32` × `GetDpiForWindow()/96`，`Clamp(32,128)`。固定 32 在 200% 屏进入临界区。

**修后实测**（Release 产物）：左贴靠 `(0,0)-(960,1040)`、右贴靠 `(960,0)-(1920,1040)`、最大化 `(-8,-8)-(1928,1048)` 且可见区 = 工作区（未受影响）。

**⛔ 三条死路（都实测过，⛔ 别再走）**：
1. 吞消息前 `DwmExtendFrameIntoClientArea(0)` 清零扩展帧 → 无效：tile 矩形在 `SetWindowPos` 之前就算完了，与 DWM 何时记账无关。
2. 删掉零内缩、交回官方零内缩 → 首次分屏仍偏宽（同因），且露缝复发。
3. 只吞 `WM_NCCALCSIZE` 的 `wParam==0`（想让系统"学到"无边框）→ 系统在贴靠路径上根本不发这条消息（实测日志里一条没有）。

### 12.3 排查方法论（换项目也能用）

- **能量矩形的就别看截图**：`GetWindowRect` / `GetClientRect` / `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` / `GetMonitorInfoW(rcWork)` 四个数一比，误差到底是 7、8 还是 16 一目了然。
- **对照实验**：先量一个标准窗口（记事本）和一个自绘外壳窗口（Edge），确定"系统本来怎么做"，再判断是本项目算错还是系统算错。
- **临时诊断开关**：环境变量开一档，只往 `%TEMP%` 追加 `WM_WINDOWPOSCHANGING / POSCHANGED / NCCALCSIZE` 序列（含 `WINDOWPOS` 的 x/y/cx/cy/flags 与当时矩形）；**交付前删干净**。
- **合成按键要解前台限制**：`keybd_event` 发 `Win+方向` 前先按一下 `ALT` + `SetForegroundWindow` + `BringWindowToTop`，否则按键根本不到目标窗口（会被误判成"贴靠不可复现"）。

### 12.4 已知风险 / 待办

| 项 | 说明 | 处理方向 |
|---|---|---|
| 四分屏未实机回归 | Win10 LTSC 无"同向贴靠两次变四分之一"，代码已覆盖但无实测证据 | 有 Win11 环境时补一次 |
| 跨屏贴靠未实机验证 | 已改用 `MonitorFromRect`，开发机只有单屏 | 双屏环境回归一次 |
| 取消贴靠还原只按「上一帧自由外框」记账 | 显示器拔插 / 分辨率切换后 `_freeFrame` 会滞后一帧 | 观察是否复现；必要时在 `WM_DISPLAYCHANGE` 时清空记账 |

---

## 附录 A 高频编译错误速查

| 错误 | 原因 | 解法 |
|---|---|---|
| NU1010 | CPM 下 csproj 写了 Version 或 props 未声明 | props 补 `<PackageVersion>`；csproj 不写版本 |
| CS0260 | 带 `[ObservableProperty]` 的类未 `partial` | 类声明加 `partial` |
| AVLN2000 | 用了 WPF 专有写法 / `IsCheckable` | 改 Avalonia 语法（`Selector` 代 `TargetType`；列显隐用 `ToggleType=CheckBox`） |
| AVLN3000 | `ColumnDefinitions` 绑 `{Binding}` | 改列宽规格串 + `ColumnGrid` |
| CS1061 ItemsControl 未含 Children | `.axaml` 是 ItemsControl，code-behind 按 Panel 用 `.Children` | 两文件同步 |
| CS0103 当前上下文不存在 Xxx | XAML 元素没 `x:Name`，code-behind 引用 | 补 `x:Name` |
| CS0104「Xxx」不明确引用 | 应用侧类型与所用库同名 | 给应用侧类型换名（`AppMenuItem`）；⛔ 别用别名掩盖 |
| CS0507 | `Visual.OnAttachedToVisualTree` 写成 `public override` | 基类是 protected ⇒ 写 `protected override` |
| No precompiled XAML found | 用 `AvaloniaXamlLoader.Load` 加载独立资源字典 | 主题改纯代码构建 ResourceDictionary 注入 |
| NETSDK1022 重复项 | `defaults\**` 用了 `None Include` | 改 `None Update` |
| 菜单为空（一个模块都没） | 程序集延迟加载，扫 AppDomain 扫不到 | Debug 用显式类型清单；Release 先 `LoadFrom` |
| 侧栏菜单重复 N 次 | 模块被重复注册 | ModuleLoader 跳过已加载同名程序集 |
| 切主题后树 / 标签栏仍旧配色 | code-behind new 出的行是本地值 | 订阅 `ThemeChanged` 后重建；优先 Classes + 样式 |
| 点刷新没反应 | 只扫目录没重建左树；或容器已 build 无法注册服务 | 加载后 `BuildTree()`；页面型模块可不注册服务 |
| 菜单项静默丢失 | 跨组注册时目标分组未建 → 异常被逐模块 try-catch 吞 | `MenuService` 延迟补登已兜住；分组始终未建 → 健全性检查报一行 |
| `PathIcon` 不显示 | `Data` 绑的 `Geometry` 键不存在或拼错 | 全仓搜键名，确认 `App.axaml` 已定义 `Icon*` |
| `Application` 无 `TryFindResource` | Avalonia 12 里是 `IResourceHost` 扩展方法（命名空间 `Avalonia.Controls`），Try 模式 | 加 `using Avalonia.Controls;`；写 `if (app.TryFindResource(key, out var v) && v is Geometry g)` |
| `VisualRoot as Window` 恒为 null | 视觉根是 `TopLevelHost` | 走逻辑树 `FindLogicalAncestorOfType<Window>(includeSelf: true)` |
| 标签页重复打开 | `OpenTab` 未做单例判定 | `Tabs` 中按 `Key` 查重，存在则激活 |
| 关闭首页标签失败 | 未设 `CanClose=false` | 首页构造时 `CanClose=false`，`CloseTab` 检查 |
| 窗口不能拖边框缩放 | — | 走官方 CSD（见 `ui.md` §4） |

> 模块专属编译坑见各 `modules/<模块>.md` §5 坑点。

### 11.3 横展审计：静默失败 / UI 线程阻塞（2026-10-27）

> 触发：修「资产库搜索恒 0 行」后做的全仓横展。本节只记**结论与处置策略**，细则在各模块 md §5 与本文件 §6。
> 🔴 **用户 2026-10-27 定：② ③ 不单独开工作项** —— 一律按「改到哪清哪」执行，⛔ 不批量排期、⛔ 不为此新建 NT。

**① SQL 聚合表达式写法（已全部修完）**
- 病灶：`group_concat(n.name, …)` 外层引用的是内层子查询的**表别名**，而内层 `SELECT n.name` 的输出列名是 `name` ⇒ SQLite 抛 `no such column: n.name`。
- 全仓只有 `Module.MediaAssets/Services/AssetService.Query.cs` 三处（`TagsExpr` / `CategoriesExpr` / `CollectionsExpr`），已改内层 `SELECT n.name AS name`；其他有 SQL 的地方（`ScriptDatabase` / `VideoMediaIndex` / `JsonDocStore` / `DataBackupSection`）**没有 `group_concat`**，不受影响。
- 为什么难发现：这三个表达式只出现在 **WHERE（搜索）/ ORDER BY（那三列排序）/ 表头筛选**，不在 `QueryPage` 的 SELECT 里 ⇒ 列表正常显示、只有搜索与三列排序恒空，极易误判成「搜索功能坏了」。坑点记 `modules/media-assets.md` §5-10。

**② 「查询失败 → 返回空集合」缺错误出口（规则已立，改造未做）**
- 规则进 `references/newtoys-spec.md` §六；现有样板 = `AssetService.LastQueryError` → assets-all 状态栏 `⚠ 查询失败：…`。
- 尚未接同类出口：`Module.ScriptManager/Services/ScriptDatabase`、`Module.VideoTools/Services/VideoMediaIndex`、`Core/Services/JsonDocStore`、`Shell/Services/DataBackupSection`。
- 处置：**改到哪清哪**，⛔ 不排一次性批次（无功能改动的批量改动混进 build，出错无法定位）。

**③ UI 线程直接做磁盘 / shell 动作（同类风险，未改）**
- 规则进 `references/newtoys-spec.md` §六；样板 = `AssetAllViewModel.FileOps.cs::RunOffUiThread`（线程池 + `Dispatcher.UIThread.Post` 回投文案）。
- 存量位置（`File.Exists` / `Directory.Exists` / `File.ReadAllText` / `Process.Start` 在 VM 里同步调用）：`Module.ScriptManager/ViewModels/` 的 `CrawlPanelViewModel.History.cs`、`QuickActionListViewModel.Item.cs`、`QuickActionListViewModel.Run.cs`、`QuickActionListViewModel.Scan.cs`、`QuickActionListViewModel.Scripts.cs`、`RuleCatalogViewModel.cs`、`ScriptInfoViewModel.Actions.cs`；`Module.VideoTools/ViewModels/VideoListViewModel.Actions.cs`。
- 影响面：目标在休眠机械硬盘 / 网络盘时才明显，本地 SSD 基本无感 ⇒ 不紧急，碰这些文件时顺手改。
