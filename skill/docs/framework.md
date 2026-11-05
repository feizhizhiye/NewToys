# NewToys 框架层（Framework）

> v4.10 · 2026-11-04 · **NT-145 结案**：启动性能议题确认「到此为止」，诊断卡标记 ✅ 已结案（不执行）。§3.5 结论维持「配置 json 不是瓶颈、Debug 热启动 775~826ms、2-3s 是冷启动环境开销、改 `src/` 省不掉」；⛔ 未动 `src/`。
> v4.8 · 2026-11-03 · **§4.2 分组表改 AUTO 块** —— 分组 / Key / Order / 图标 全部由 `docs-sync.py --fix` 按 `RegisterModule` 生成（AUTO 块内 ⛔ 手改）；宿组 `system` 行由脚本固定输出。理由：机器事实与人写的说明混在一张表 ⇒ 必然人肉同步。⛔ 未动 `src/`。
> v4.7 · 2026-11-01 · **查看器框架层新增点登记** —— §11 登记 NT-124：`NewToys.UI/Viewers/`（渲染器族）+ `Shared/Services/IViewerRegistry`（认领契约）+ 新模块 `Module.Viewer` 薄壳。🔴 三条新增点均属框架层，**须用户确认后才可动手**；V2 音视频另需媒体后端拍板。
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
- 🔴 **`NewToys.UI` 纪律**：被所有模块引用 ⇒ 与 Shared/Core 同级 ⇒ **改一次 = 全模块重编**。只放**写完即冻结的稳定控件**；`App.axaml` 的 `*Brush` 主题刷子 ⛔ 不搬（条数以 `lint.py` 白名单为准），UI 内 XAML 一律 `DynamicResource` 引用主题键。
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

### 3.5 启动性能：链路、打点、实测与结案（2026-11-04）

> 🔴 **本节是「启动慢」类问题的唯一入口**。读完即可下结论，⛔ 不要再重新摸底、不要再试 §3.5.5 的方向。任务路由见 `index.md §三`。
> **结论先行**：配置 json 不是瓶颈（<30ms）；Debug 热启动 775~826ms；用户体感 2-3s 的差额是**新编译产物首次冷启动**（冷读 DLL + 杀毒实时扫描 + JIT），改 `src/` 省不掉；热启动再压收益 <150ms 且风险高 ⇒ **结案**。
> ⚠️ **本议题 2026-11-04 当天已做过完整实测闭环（临时打点 + 多次 build，代码已还原）**：模块扫描内 `Assembly.LoadFrom` 1189ms 占 97%，`GetTypes` 28ms / `CreateInstance` 1ms / `Register` 1ms 可忽略；热启动 `LoadFrom` 仅 2ms；**并行加载无效**（1203 vs 1222ms，CLR loader lock 串行）；**R2R 试点否决**（`Module.MediaAssets.dll` 839KB → 2268KB 且带 26 个依赖，对磁盘冷读是负优化）。结论：启动慢 = CLR 加载模块 DLL 的固有磁盘 IO，**Debug 版日常用根本不慢**。⛔ 不要重做这些实验。

#### 3.5.1 当前实现的完整链路（代码位置）

| 段 | 代码 | 计时 |
|---|---|---|
| `Main` → `BuildAvaloniaApp()`（`UsePlatformDetect` / `WithInterFont` / `LogToTrace`） | `Shell/Program.cs:9-16` | ❌ **未计时** |
| CLR 加载 / Avalonia 平台后端（Win32 + Skia 原生库） | 运行时隐式 | ❌ **未计时** |
| `App.Initialize()` = `AvaloniaXamlLoader.Load(this)`；`App.axaml` 65KB / 830 行（资源字典 + FluentTheme + 2 个 `StyleInclude`） | `Shell/App.axaml.cs:40-43` | ❌ **未计时** |
| `OnFrameworkInitializationCompleted()` 进入；`swStartup` 在 `:55` 才起 | `Shell/App.axaml.cs:45` | ✅ 计时起点 |
| DI 注册 → 模块扫描 → 容器构建 → 模块菜单初始化 → 主窗口构造 → 窗口显示（8 个 `Mark()`） | `App.axaml.cs:59-251`（`:168/:170/:175/:193/:202/:257`） | ✅ |
| `Show()` 后 `DispatcherPriority.Background`：配置整理 → 库 schema → 模块 `InitializeDeferred` | `App.axaml.cs:261-267` → `ModuleLoader.cs:106-148` | ✅ 另记一行 |

**配置为何不慢**：`ConfigStoreService` 构造（`Core/Services/ConfigStoreService.cs:55-63`）只算路径、不碰磁盘；一次性迁移 / 清壳走 `:72-83` 的 `InitializeDeferred()`，排在首帧之后；`ShellConfigStore` 首次读盘前才兜一次 `EnsureShellMigrated()`。打点里 `DI注册 1 → 容器构建 +9 → 菜单初始化 +20ms` 即配置全部成本。⛔ 硬编码一份预设配置最多省 <30ms，还会破坏多配置隔离 / 备份导入 / 用户设置。

#### 3.5.2 打点与日志（唯一事实源）

- 写入：`Shell/App.Profile.cs` 的 `WriteStartupLog()`（追加当天文件、写盘失败静默）；路径 `%LOCALAPPDATA%\NewToys\profiles\<配置名>\logs\startup-yyyyMMdd.log`（`NEWTOYS_HOME` 可改根）。
- 两行格式：`启动打点（累计毫秒）：DI注册 1 → 模块扫描 14 → 容器构建 23 → 模块菜单初始化 42 → 主窗口构造 452 → 窗口显示 775` ／ `模块延迟初始化 98ms（首帧之后）`。
- ⚠️ **Debug 与 Release 共用同一个 logs 目录且日志里没有产物标识** ⇒ 两种形态的记录混在一起（2026-11-04 连续四轮误判的根因）。

#### 3.5.3 双轨加载与实测（2026-11-04，同一台机器）

| 形态 | 模块来源 | `Modules\` | 模块扫描 | 窗口显示 |
|---|---|---|---|---|
| Debug（用户日常双击的 `artifacts/bin/Shell/Debug/net10.0/NewToys.exe`） | `App.axaml.cs:335` `#if DEBUG` 显式 `typeof` 清单（**已硬编码**） | **空目录** ⇒ `ScanModulesDirectory()` 一个 DLL 都不加载 | 11~14 ms | 775~826 ms |
| Release（`artifacts/release/NewToys.exe`） | `Modules\` 下 8 个 `Module.*.dll` → `Assembly.LoadFrom` + `GetTypes()` | 8 个模块 DLL + 3 个框架 DLL | 2.5~3.4 s（冷） | 3.2~4.5 s |

- 目录事实：Debug 程序目录 85 文件 / 153MB（PDB 占 103MB）、66 个 DLL / 51MB；Release 目录 151MB。
- 历史优化（§3.3）：模块扫描必须过滤 `Module.*.dll` —— 全量扫 49 个 DLL 一次吃掉 2173ms，过滤后 9~11ms。
- ⚠️ **冷/热判据（§3.3）**：新编译产物第一次启动必然慢（冷读 + 杀毒实时扫描 + JIT）；判断优化是否生效要么连跑两次取第二次，要么看分项打点。⛔ 单次日志不能当结论。
- 用户体感 2-3s = 冷启动开销（未计时区与打点区一起变慢），与 775ms 热启动基线不矛盾。
- **历史实测闭环（2026-11-04 当天，临时打点已还原，⛔ 不要重做）**：
  - 模块扫描内部分项：`LoadFrom` 1189ms（97%）、`GetTypes` 28ms、`CreateInstance` 1ms、`Register` 1ms；热启动 `LoadFrom` 仅 2ms ⇒ 瓶颈是**磁盘冷读模块 DLL**，不是反射 / 不是清单形态。
  - **并行加载无效**：1203ms vs 1222ms（CLR loader lock 串行）。
  - **R2R 试点否决**：`Module.MediaAssets.dll` 839KB → 2268KB（2.7 倍）且带 26 个依赖，对冷读是负优化；R2R 只对「首次开页 JIT」有意义，对启动 IO 无效。
  - 附带修复：Release 模块=类库默认不复制 NuGet 依赖，已在 `Shell.csproj` 补 6 个 `PackageReference`（NAudio / SharpCompress / TagLibSharp / MailKit / AngleSharp / Jint），依赖随主程序复制到根目录。
  - 当时用户曾选「R2R 主程序 + 模块反射预筛」，但上面实测已否决其对启动的收益 ⇒ **该方向不再推进**。

#### 3.5.4 收益预估与优势（方案 NT-145）

| 动作 | 预计节省 | 置信度 |
|---|---|---|
| 补全链路打点（NT-145） | **0 ms**（纯日志） | 确定 |
| 硬编码配置预设 | <30 ms | 确定 |
| 模块清单硬编码（Debug） | 0 ms | 确定（已是） |
| 热启动继续压 Avalonia 首帧（主窗口构造 ~450ms + 窗口显示 ~345ms） | <150 ms，风险高 | 高（§3.3 结案） |
| 冷启动差额 1.2~2.2s | 改 `src/` 省不掉 | 高 |
| 并行加载 8 个模块 DLL | **0 ms**（实测 1203 vs 1222ms，CLR loader lock 串行） | 确定（已实测） |
| R2R 发布 | **负优化**（839KB→2268KB + 26 依赖，冷读更慢） | 确定（已试点否决） |
| 环境层（Defender 排除程序目录 / 预热文件缓存） | 1~2 s（**未验证**） | 低，⛔ 不在 NT-145 范围 |

优势（NT-145）：① 零行为风险；② 复用现有日志与 `WriteStartupLog`，零新依赖、零新文件；③ 区分冷/热 + Debug/Release，终结日志混读；④ 给后续优化一个可复现基线与验收口径；⑤ 把已排除项固化成禁试清单，省掉后续会话的重复摸底。

#### 3.5.5 ⛔ 不得再试清单（结案）

1. ⛔ **硬编码预设配置 json**：<30ms，且破坏多配置隔离 / 备份导入 / 用户设置。
2. ⛔ **再把模块清单硬编码**：Debug 已是显式 `typeof` 清单；Release 的慢来自冷读 DLL，不是清单形态。
3. ⛔ **凭感觉删 `WithInterFont()` / 砍 `App.axaml` 资源字典 / 延后 `StyleInclude`**：无数据支撑，可能白改一轮；真要做先跑 NT-145 拿分段数据。
4. ⛔ **为提速直接换 Release**：其 `Modules\` 8 个 DLL 冷启动更慢；要换须另立卡先解决 Release 扫描，不与本议题混。
5. ⛔ **把 `InitializeDeferred` 挪到 `Task.Run`**：Sqlite / 配置 store 非线程安全（§3.3 已定）。
6. ⛔ **用单次启动日志下结论**：必须连跑两次取第二次。
7. ⛔ **并行加载模块 DLL / R2R 发布 / 再做模块扫描内部分项打点**：当天已实测并还原，0 收益或负优化（见本节头部与 §3.5.3）。
8. ⛔ **把 Release 扫描慢归咎于「清单没硬编码」**：实测 `LoadFrom` 占 97%，根因是磁盘冷读，换静态清单 / `GetType(全名)` 都动不了它。
9. ⛔ **为「启动快」再引入 publish 流程 / 改构建产物形态**：R2R 试点已否决，且违背「一个 NT = 一次 build」的现有交付口径。

> **NT-145 已于 2026-11-04 用户确认「结案吧」后标记 ✅ 已结案**（不执行）。本节结论即「到此为止」，不达成上述任一新证据前不再新增启动优化工作项。

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

<!-- AUTO:framework.groups -->
| 分组 | Key | Order | 图标 |
|---|---|---|---|
| 脚本管理器 | `script` | 20 | `IconPc` |
| 系统工具 | `system-tools` | 25 | `IconSystem` |
| 网络工具 | `web` | 30 | `IconWeb` |
| 视频工具 | `video` | 40 | `IconVideo` |
| 媒体资产 | `assets` | 50 | `IconMediaAssets` |
| 查看器 | `viewer` | 55 | `IconViewer` |
| 网络阅读 | `reading` | 56 | `IconReading` |
| 备份中心 | `backup` | 60 | `IconBackup` |
| 宿主分组（**不渲染**） | `system` | 999 | `IconSettings` |
<!-- /AUTO:framework.groups -->

> 本表由 `python skill/scripts/docs-sync.py --fix` 按 `RegisterModule` 生成，⛔ 不要手改块内；增删模块只改 `src/` 后跑一次 `--fix`。

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
| `settings-theme` | 主题外观 | 1 | `Theme` | 明暗模式 / **配色皮肤（自制 · 原生 Fluent）** / 强调色 |
| `settings-launch` | 一键按钮 | 3 | `Launch` | 首页三个一键按钮的配置 |
| `settings-path` | 路径与配置 | 2 | `Environment` | 运行环境路径 + 多配置档（分类列区分行） |
| `settings-dns` | 加密 DNS | 4 | `Network` | 加密 DNS / 网络 |
| `settings-data` | 数据管理 | 6 | `Data` | 备份 / 导入 `config` |
| `settings-update` | **版本与更新** | 8 | `Update` | 版本号 / 版本文件地址 / 检查更新 / **组件版本** / 技术栈 / 重载模块 + 打开模块文件夹 |

- 🔴 **所有 Key ⛔ 不改**：首页跳转 / 收藏白名单 / 托盘「打开设置」都按 Key 寻址。
- **主题状态**（明暗 `Mode` / 强调色 `AccentId` / 皮肤 `Skin`）落 `shell.json` 的 `theme` 节：`Core.ThemeService` 只持有状态与落盘（**不引用 Avalonia**），配色由 `Shell/App.Theme.cs` 订阅 `ThemeChanged` 重建资源字典。皮肤切换只换同一批语义键的取值，⛔ 不影响布局。
- 🔴 **原生 Fluent 皮肤取色必须显式传变体**：`ApplyTheme` 刚设完 `RequestedThemeVariant`，`Application.ActualThemeVariant` 要等样式重估才刷新，此时无参 `TryFindResource` 会拿到上一个明暗的取值（表现：整片「只有明色」，切深色不生效）⇒ `FillFluent` 一律 `TryFindResource(key, variant, out _)`。
- 🔴 **侧栏文字按明暗分两套键**：Fluent **浅色**下 `ChromeMedium` 是浅灰底，再配 `ChromeWhite` 文字会整片糊掉；浅色改用 `ForegroundBaseHigh` / `ForegroundBaseMedium`。
- 🔴 **强调色分两个键**：`AccentBrush` = **填充 / 描边**；`AccentInkBrush` = **文字 / 图标**（自制皮肤浅色下压深 45%）。把背景强调色当文字色用，深色下就是「激活项文字糊在激活底色里」——XAML 里 `Foreground` / `Stroke` 一律用 `AccentInkBrush`。
- 🔴 **默认皮肤 = `Fluent`**（用户拍板：原生明暗层次更细）。`App.Theme.cs` 的 `FillFluent` 里，**强调色系 6 个键一律自己派生、不映射 Fluent 的 `SystemControl*Accent*`** —— 否则原生皮肤一换，用户选的强调色就整片失效（状态栏却还写着「已立即生效」）。
- 🔴 **Fluent 控件的强调色走调色板，不走 `DynamicResource`**：单选圆点 / 复选勾 / 开关滑块 / 列表选中条 / Slider / ProgressBar 都只认 `FluentTheme.Palettes[variant].Accent`（官方入口 `ColorPaletteResources.Accent`）⇒ `ApplyTheme` 里由 `ApplyFluentAccent()` 统一写入。⛔ 别再逐个控件重写模板（漏一个就是两套强调色并存，2026-11-04 实测：设置页色卡选中条 + 单选圆点都是蓝的）。
- ⚠️ `ColorPaletteResources` 还带整套调色板（`AltHigh` / `BaseHigh` / `ChromeLow` / `RegionColor` 等）⇒ 将来做「自定义配色方案」就从这里扩，比逐键覆盖稳。
- 「通用」已拆成 `settings-general` / `-update` / `-theme` **三节共用一个 VM**。
- 🔴 **v3.67：「更新」+「关于」合并 = 「版本与更新」**（8 节 → 7 节，用户拍板）—— Key 用原来的 `settings-update`；
  `settings-about` 退成**别名**（`SettingsViewModel.ResolveKey` + `ShellViewModel.SettingsSectionKeys`，旧链接仍能打开，
  ⛔ 不再注册菜单、⛔ 不再是一个节点）。该节由 `UpdateAboutView`（**拼装壳**）装 `UpdateView` + `AboutSettingsView`，
  子 View 的 DataContext 由 `Init(...)` 分别赋（⛔ 靠继承只能给到一个）；节上有**两个 VM** ⇒ `SettingsSection.ViewModels`
  是列表，状态栏 / 工具条契约按列表逐个聚合。
- 功能清单**不是独立页**（⛔ 不注册菜单 Key）：`AllFunctionsView` / `AllFunctionsViewModel` 由首页收藏卡尾部「＋ 更多」就地展开（`HomeViewModel.BuildFunctionList` 现装配）。

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
- 节点抬头有**独立底色**（`Button.sec-head` → `ContentHeaderBrush`，⛔ 无下边框）⇒ 抬头与正文一眼分开；
  磁吸浮层里的抬头另走 `Button.sec-head.stuck`（透明底，底色由浮层 Border 给）。
- 🔴 **v3.69：浮层顶部留白 = `clamp(12 - Offset.Y, 0, 12)`**，且 **`Offset.Y <= 0.5`（真·顶部）时浮层一律不挂**
  （`default(Row)` 直接喂 `ApplyStuckHeader`）。原判定只看 `Top < -0.5`，高 DPI / 边距取整下首节点会被误判成"钉住"
  ⇒ 顶部残留一条 `ContentHeaderBrush` 底色的抬头，看着像"标题栏没留空白"（用户实测）。留白跟卡片走还顺带消掉了
  "滚过 12px 时浮层咔一下跳上去"的突变。⛔ 别把顶部留白改回常量 0。
- 浮层右上「全部收起 / 全部展开」`FontSize=14` + `Padding=10,4`（放大过两轮：11 → 13 → 14，11 号字用户看不清）。
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
  顶栏底色 `PanelBrush`（卡片同档）+ 上两角不圆（`CornerRadius="0,0,8,8"`），⛔ 无下边框。
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
| 展开 / 收起 | 首页收藏卡尾部「＋ 更多」 | 就地展开全量功能**列表**（一行一项 = 图标 · 名称 · 简介），右键「📌 固定到首页」 | 只存在于首页内；⛔ 没有菜单 Key、⛔ 不进标签栏；「🔄 重载模块」在「版本与更新」节的关于部分 |
| 按模块折叠 | 点模块标题行 / 顶部「↕ 全展开 · ↕ 全收起」 | 每节独立折叠（默认**全部展开**），标题行显示 `▾ 模块名 · 功能数`，收起后整节只剩一行 | 折叠状态不落盘（每次展开清单回到全展开）；滚动条在**清单内部**（`AllFunctionsView` 自带 `ScrollViewer`） |

**首页 `home`**

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 布局 | — | `Grid RowDefinitions="Auto,Auto,*,Auto"` 四行分区：Row0 欢迎卡**固定** · Row1 收藏墙**固定** · Row2 功能清单**唯一滚动区** · Row3 一键按钮条**恒贴底** | 展开 / 收起清单只换 Row2 内容，⛔ 不推走上面两块、⛔ 不顶动底栏；⛔ 用整页 ScrollViewer |
| 一键按钮 | 点一键卡 | 按 `config\launch.json` 启动 | 配置来自「设置 → 一键按钮」；外观与收藏按钮同一套 `fav-btn accent`（透明底 + 强调色描边，悬停变实心），高 44；⛔ 硬编码底色 |
| 一键按钮设置 | 行右端小齿轮（24×24） | 跳 `settings-launch` | 与三个按钮**同一行**、贴右上角；⛔ 不单占一行标题（原「⚡ 一键操作」标题行已删） |
| 收藏 | 功能项 ★ | 写 `config\favorites.json`（即时） | — |
| ＋ 更多 | 收藏卡尾部 | 就地展开功能清单 | ⛔ 不跳页 |
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
- 🔴 **类名必须全局唯一**：同名 View 会撞车（历史两处 `DnsView`）。静态检查 = `lint.py --only comments` 的 `C-05`。
- 无占位页：兜底 = `ShellViewModel.BuildErrorView()` 现场构造的红字 + `ScrollViewer`。
- Shell 自带页面同样走 `ResolveView`（先例 `HomeView` / `HomeViewModel`）。
- 可以先注册菜单、后补页面。
- 🔴 **交付前跑 `lint.py --only view-pairs`**：`P-01` = `Views/` 下的 View 在本程序集找不到同名 VM（页面会静默空白）；
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
| `DialogCardHost.axaml(.cs)` | `Shell/Controls/` | 全窗口遮罩（`LoadingOverlayBrush`）+ 居中卡片（`PanelBrush` 底 / 圆角 8 / 投影，⛔ 无描边）；挂在 `ShellView` 最外层 Grid 顶层 |
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
| `ModuleTopics` | 同上 | 主题常量，命名 `域.动作`。已有 `VideoImport = "video.import"`（载荷 `IReadOnlyList<(string Path, string MetaJson)>` = 绝对路径 + 资产库 `metadata_json`）、`AudioPlay = "viewer.audioplay"`（载荷 `IReadOnlyList<string>` = 音乐绝对路径，**数组顺序即播放顺序**，第一首开播）、`ReaderOpenBook = "viewer.reader.openbook"`（载荷 `string` = 索引书 `index.json` 绝对路径，网络阅读 → 查看器文本页，接收方按需读章 ⛔ 全量进内存） |
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
- 跑法：`dotnet test src\NewToys.Tests`（用户本地执行；⛔ 不进 `accept.py` —— skill/ 下脚本不做构建/测试）。
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
| NT-149 | **同族页能力矩阵 + 静态验收第 10 项**：范本新增 §八 矩阵（四段齐全度，一行一页）；`lint.py --only family` + `accept.py` 第 10 项三条对账规则；不动 `src/`、零 build、标记只落文档 | ✔ 已完成（2026-11-05 静态检查 PASS，零 build；卡：`backlog/framework/NT-149.md`） |
| NT-148 | **通用筛选条 FilterBar**：`NewToys.UI/Controls/FilterBar`（内容槽 + Quick chips + 清空 + 摘要）；6 页右键筛选全删、改挂顶区筛选条；3 条自绘筛选条迁入；`AssetAll` / `AssetBrowse` 表头右键补齐；Shared 基类加 `ResetColumnWidthsCommand` | ✔ 已完成（2026-11-05 静态检查 PASS，待用户本地 build；卡：`backlog/framework/NT-148.md`） |
| NT-147 | **表头右键 / 行右键职责分离（资源管理器口径）**：`NewToys.UI/Controls/TableViewHost` 在 `ContextMenu.Opening` 按落点重建菜单内容 —— 表头两段（列宽 / 列显隐）由控件统一生成，行菜单保留页面功能项与筛选项；17 个表格页一致，自绘 `ColumnGrid` 旧页本就分菜单故不改 | ✔ 已完成（2026-11-05 静态检查 PASS，待用户本地 build；卡：`backlog/framework/NT-147.md`） |
| NT-145 | **启动链路打点补全（诊断卡）**：打点起点前移到 `Program.Main`、日志标 Debug/Release·冷/热；**本身提速 0ms**，只为终结日志混读。现状与「不得再试」清单见 **§3.5** | ✅ 已结案（2026-11-04 用户确认；框架层须用户确认故未执行；卡：`backlog/framework/NT-145.md`） |
| NT-047 | **数据驱动图标矢量化**：二级功能菜单 34 项 `icon` / 首页一键按钮 / 模块页内数据行（⛔ 静态装饰 emoji 不动） | 📝 草稿（**用户 2026-10-19：暂时不做**） |
| NT-079 | **可成长性**：模块开关 / 命令面板 / 采集器骨架生成 | 📝 草稿 |
| NT-081 | **跨平台中立化（远期）**：RID 条件化 ✔ + Windows-only P/Invoke 守卫收口 ✔（守卫层）+ 进程底座 `IProcessRunner`（Shared 契约 / Core 实现 / Shell 单例；三类进程用法分立见 §6.7）✔ + 解析分层（`FixedWidthTable` 通用机械 / `TsvRows` 机读 / winget 启发式留模块）✔ + apt 只读适配器 `AptPackageService`（按平台二选一注册，页面零改动）✔；⛳ 未做：Linux 实机验证（无环境）/ P/Invoke 物理集中 / apt 写操作（需 sudo）。约束见 **§2.1** | 🔄 执行中（步骤 1~5 已实施 2026-10-19，待本地 build；卡：`backlog/framework/NT-081.md`） |
| NT-083 | **Python 运行时底座**（跨模块共用 ⇒ **落 Core**，已批准）：`IPythonRuntime`（`ToolsRoot` / `Probe` / `RunAsync` / `Shutdown`）+ `Core/Services/PythonRuntime`（长驻 worker，行分隔 JSON / `-u -X utf8` / 超时杀进程树 / traceback 回传 / 串行 `SemaphoreSlim` / 空闲 10 分钟自动退出）+ 共享环境 **`%LOCALAPPDATA%\NewToys\tools\py`**（与 `env.json` 同级）⇒ 依赖安装交给随包脚本，**C# ⛔ 不写 pip 逻辑**（播种成集合「环境维护」，见 `modules/script-manager.md` §2.5）；状态在「设置 → 运行环境 → Python 共享环境（venv）」 | ✔ 已完成（批 1~4，2026-10-19；⏳ 待用户 build + 实机功能验收） |

| NT-088 | **执行器上提 Core**（`ScriptRunner` + `InterpreterResolver`：`Module.ScriptManager` → `NewToys.Core`）：为新增的 `system` 系统工具模块提供跨模块执行契约（模块禁互引），并为命令链（NT-092）铺路；行为零变化，纯重构 | 🔄 **已实施（⏳ 待 build 验收）** —— 2026-10-22 用户指令「执行 nt-088/089」= 批准（卡：`backlog/framework/NT-088.md`）|
| NT-094 | **重划分收尾**：设置拆分（`config\web\*` 瘦身：超时 / 并发 / 解析器 → script）+ 一级菜单显示名「脚本中心」+ `system` 组一级图标落地 + 备份段重复显示检查 | 📝 草稿（待细化；依赖 NT-088 ~ NT-093；卡：`backlog/framework/NT-094.md`） |
| NT-124 | **查看器基础能力**：`NewToys.UI/Viewers/`（`ViewerHost` 外壳 + `IViewerRenderer` + 文本分页 / 图片缩放 / 音视频三个渲染器）+ `Shared/Services/IViewerRegistry` 认领契约 + 新模块 `Module.Viewer` 薄壳（菜单 Order 60）。V1 文本 + 图片零依赖；V2 音视频待媒体后端拍板（**Avalonia 无内置解码播放**）。动力：媒体资产 / 视频工具 / 将来书源阅读都要「打开并显示」，渲染器留业务模块会三处重写 | 📝 草稿（框架层 3 处新增待用户确认；卡：`backlog/framework/NT-124.md`） |

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

**已上线机制**：`skill/scripts/lint.py --only comments`（已接入 `accept.py` 静态检查清单）
- `C-01` 版本/轮次前缀行 · `C-02` ≥6 行注释块 · `C-04` 历史叙事词 = **WARN**（不阻断）
- `C-05` **两个程序集同名 View 类 = ERROR** —— 反射导航靠类名找页，重名会静默错页，这是唯一能提前拦它的检查
- `C-07` 已治理程序集里 ≥6 行 `///` 长段 · `A-01` `WHY: <ID>` ⇄ 文档 `<!-- ANCHOR:<ID> -->` 双向对账 = **WARN**

**方案1 全量瘦身与分流（2026-11-05，用户确认破「改到哪清哪」）**：短契约（≤5 行 `///`）全留；长段（≥6 行）由 `skill/scripts/comment-slim.py --batch <批次> --apply` 压成 1 行契约 + `WHY: <ID>`，高信号 WHY 正文蒸馏进附录 A.2 / `ui.md` §9 / `modules/<模块>.md` §5，D 类叙事删；机器标记 20 条（4 `lint:view-no-vm` + 16 `inheritdoc`）必留。按 Shared → Core → Shell → UI → Module.* 分批，`lint.py` 的 `SLIMMED_ASSEMBLIES` 是已治理程序集白名单，未治理批次不报 C-07。完整判据 = `tech-guard.md` §3.1。
- `C-03` 仅出注释率 Top5 排行，**不告警**（model / 接口类的属性注释就是契约，审美判据做 lint 必然烂成噪音）

**生成侧契约 + 自动门禁（2026-11-05，用户授权）**：三分法只判「已有注释往哪去」；**写新代码该写什么** = `tech-guard.md` §3.1.1（信息优先交类型 / 命名 / 测试 / 文档，注释只留别的载体拿不到的那句；标记只认 `WHY: <ID>` / `SAFETY:` / `TODO:`）。增量门禁 `skill/scripts/comment-gate.py` 只判 `git diff` 新增行（M-01 叙事 / M-02 新增长段 / M-03 缺 ANCHOR / M-04 标记不合规），违规**非零退出**；自动触发 = `.githooks/pre-commit`（`git config core.hooksPath .githooks` 启用一次）+ `.github/workflows/comment-gate.yml`（PR / push）。职责边界：`lint.py --only comments` 全量自检恒 0 退出、只报告；门禁才阻断，存量不由门禁消化。

**存量处置原则**：⛔ 不清零、⛔ 不排一次性清洗批次（600+ 处无功能改动混进一次 build，出错无法定位）。
按「改到哪清到哪」消化；lint 只保证**新增代码不再产生**。已清理样板：`Core/Services/ModuleLoader.cs`、`Shell/ViewModels/ShellViewModel.cs`。
**唯一例外（2026-10-25 用户拍板）**：`NT-xxx` 编号引用一次性全清（1121 处 / 245 文件，纯注释），编号→结论收口到 **§11.2**；
版本号前缀（`vX.Y`）与轮次叙事仍按「改到哪清到哪」，⛔ 不再按批次清洗。

**配套测试**：`src/NewToys.Tests/ListPageBaseTests.cs`（26 个测试方法 / 33 个用例，覆盖分页溢出 / 隐藏列写 0 占位 / 锁定列不可拖 / 筛选「且」语义 / 页大小跨页收敛）。跑法：`dotnet test src\NewToys.Tests`（用户本地）。

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
| NT-123 | 列表页统一切换为原生表格（`NewToys.UI/Controls/TableViewHost`） |


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

### A.2 代码 WHY 蒸馏（方案1 · anchors）

<!-- WHY-DISTILL:shared:start -->

> 由 `skill/scripts/comment-slim.py` 生成（方案1 · 白名单与锚点口径见 `tech-guard.md` §3.1）。
> 代码里只留 1 行契约 + `WHY: <ID>`；此处是 ID 对应的 WHY 正文。`lint.py --only comments` 双向对账。

<!-- ANCHOR:SH-001 -->
- **SH-001** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 🔴 加新 Kind 是「加法」：新需求优先用「现有 Kind + 修饰位」表达；确实需要新交互时才加一个枚举值

<!-- ANCHOR:SH-003 -->
- **SH-003** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 溢出自动换行（WrapPanel）。只固定视觉样式，⛔ 不固定排列。

<!-- ANCHOR:SH-004 -->
- **SH-004** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 典型用途：信息面板改表格表头名称（每列一行）。⛔ 不做增删行（那是 Chips 的事）。

<!-- ANCHOR:SH-005 -->
- **SH-005** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 落盘由页面工具条的保存命令负责 —— 描述符不参与刷新（需求 §4.5.1「刷新契约」）。

<!-- ANCHOR:SH-006 -->
- **SH-006** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 🔴 走 C# 对象字面量声明（⛔ 不做运行时 JSON / XAML 加载）—— 享受编译期检查 + IDE 补全。
  - ⚠️ 绑定路径（ValuePath / VisibleWhen …）是字符串：属性改名不报错、只是白板，
  - 所以必须跑 skill/scripts/check-detail-paths.ps1 做静态兜底。

<!-- ANCHOR:SH-007 -->
- **SH-007** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 路径一律相对卡片 DataContext（= 页面 VM），支持 "!" 前缀取反，支持 "A.B" 多级（如 DetailRow.TypeText）。

<!-- ANCHOR:SH-008 -->
- **SH-008** · `src/Shared/Details/DetailCardDescriptor.cs`
  - 🔴 全部走路径字符串（相对卡片 DataContext = 页面 VM），由 Shell 渲染层解析成强类型属性 —— 与
  - ⚠️ 纯图标没有文字 ⇒ `ToolTip` 必填（悬停讲语义，否则用户猜不出按钮是干什么的）。

<!-- ANCHOR:SH-009 -->
- **SH-009** · `src/Shared/Models/FilterClause.cs`
  - 值留空的条件不生效（这样"先加条件后填值"不会瞬间把列表筛空），
  - 否则按文本比（忽略大小写）。这样「大小 &gt; 100」不会变成字符串比较的怪结果。

<!-- ANCHOR:SH-010 -->
- **SH-010** · `src/Shared/Models/IModule.cs`
  - 🔴 （启动提速）：重活不许写这里 —— 建库/清库、读配置文件、
  - 种子播种（Seeder）、扫描磁盘、批量更新任务入队，一律放 `InitializeDeferred`。

<!-- ANCHOR:SH-011 -->
- **SH-011** · `src/Shared/Models/IModule.cs`
  - ⚠️ 契约：实现内部必须自己 try/catch 兜住（数据层不可用不许拦启动）；

<!-- ANCHOR:SH-012 -->
- **SH-012** · `src/Shared/Models/ListColumn.cs`
  - 只描述列本身（键 / 标题 / 宽度 / 是否可排序），不参与排序逻辑、也不存可见性状态 ——

<!-- ANCHOR:SH-013 -->
- **SH-013** · `src/Shared/Models/ListColumn.cs`
  - 唯一用途 = 列宽判定的依据：示例文本宽 ≤ 表头文字宽 → 该列锁成表头宽（不可拖、不自动调）；否则不锁。
  - §4.8.1 / ⛔ 不再当作「示例行 / 水印行」显示（干净启动：列表没数据就是空态引导）。
  - ⛔ 绝不按真实行逐行测宽（虚拟化只 measure 可见行 → 滚动会让最大内容宽变化 → 列宽抖动）。

<!-- ANCHOR:SH-014 -->
- **SH-014** · `src/Shared/Models/ListPageViewModelBase.Columns.cs`
  - 🔴 必须合并写（先读回本页已有的键，再覆盖列键）：SavePage 是整页替换，

<!-- ANCHOR:SH-017 -->
- **SH-017** · `src/Shared/Models/ListPageViewModelBase.cs`
  - ⚠️ 为什么列可见性用「具名属性 + 字典」两套表示：字典用来落盘与算列宽规格，
  - 为 "Is" + key + "Visible" 发一次通知，所以列键必须与该属性名对得上。

<!-- ANCHOR:SH-018 -->
- **SH-018** · `src/Shared/Models/ListPageViewModelBase.cs`
  - ⚠️ 这是构造期的虚属性调用：派生 override PageSizeChoices 只许返回 static 只读数组。

<!-- ANCHOR:SH-020 -->
- **SH-020** · `src/Shared/Models/ListPageViewModelBase.cs`
  - 🔴 与 `IStatusProvider` 同一套路：一次在基类实现，覆盖全部列表页。
  - ⚠️ 页面 override 后必须删掉页内的 Border.toolbar，否则会出现两条工具条。

<!-- ANCHOR:SH-021 -->
- **SH-021** · `src/Shared/Models/ListPageViewModelBase.cs`
  - 🔴 修订：⛔ 不再要求「未挂起」—— 草稿态（扫描预览）列表同样会超出视口，

<!-- ANCHOR:SH-022 -->
- **SH-022** · `src/Shared/Models/ListPageViewModelBase.cs`
  - 列键与属性名不一致时返回 null，`ApplyFilters` 会把这条条件当"不生效"跳过（不会误筛掉行）。

<!-- ANCHOR:SH-023 -->
- **SH-023** · `src/Shared/Models/SettingsCategory.cs`
  - 「设置」组分区一律看本枚举。
  - 🔴 默认值 = `None`（普通功能项）⇒ 既有的 RegisterFunction 调用
  - 外观 / 顺序零变化，无需逐处补参数；只有设置项才需要显式标注。

<!-- ANCHOR:SH-025 -->
- **SH-025** · `src/Shared/Models/ToolbarItemDescriptor.cs`
  - 🔴 宿主只负责「勾 ↔ 数据项 bool」的双向绑定；点击后照常执行
  - `ToolbarItemDescriptor.CommandPath`（参数 = 数据项本身）⇒ VM 的命令里⛔ 不要再翻 bool（会翻两次）。

<!-- ANCHOR:SH-026 -->
- **SH-026** · `src/Shared/Models/ToolbarItemDescriptor.cs`
  - 🔴 加新 Kind 是加法：确实需要新交互时才加（配一个渲染分支），不影响既有页。

<!-- ANCHOR:SH-027 -->
- **SH-027** · `src/Shared/Models/ToolbarItemDescriptor.cs`
  - 临时解挂 TwoWay 绑定实现（⛔ 不能直接改 Text，会写回 VM）。用于「常态不必看值、

<!-- ANCHOR:SH-028 -->
- **SH-028** · `src/Shared/Services/IAudioPlayback.cs`
  - 🔴 引擎实现住在 Core（Windows 后端），界面控件住在 NewToys.UI 并由宿主注入本接口 ——
  - NewToys.UI ⛔ 不引 NAudio / 平台库，换后端（mpv / libvlc）只换实现。
  - 🔴 所有成员都按「可被 UI 线程随时调用」设计；实现方内部自行切线程，⛔ 不让调用方 await。

<!-- ANCHOR:SH-029 -->
- **SH-029** · `src/Shared/Services/IAudioSpectrum.cs`
  - 🔴 与 `IAudioPlayback` 分开的原因：频谱是锦上添花，⛔ 不该逼所有后端（含
  - 🔴 界面线程会按帧（≤30 fps）调用 `ReadSpectrum`：实现方内部自加锁，⛔ 阻塞音频线程。

<!-- ANCHOR:SH-030 -->
- **SH-030** · `src/Shared/Services/IAudioTone.cs`
  - 🔴 与 `IAudioPlayback` 分开：不是每个后端都做得出滤波（NullAudioPlayback 就不做），
  - 界面按 Playback as IAudioTone 判断 —— 拿不到就不显示音色键，⛔ 改 `IAudioPlayback` 逼所有后端实现。

<!-- ANCHOR:SH-032 -->
- **SH-032** · `src/Shared/Services/IBackupSection.cs`
  - 🔴 容器是 zip（System.IO.Compression，⛔ 不引第三方压缩库）：
  - 🔴 每个节自己写一个内层 zip：`ExportTo` 收到的流是外层 zip 里
  - ⚠️ 实现放哪：框架节在 Shell（ShellBackupSection / EnvBackupSection /

<!-- ANCHOR:SH-033 -->
- **SH-033** · `src/Shared/Services/IBackupSection.cs`
  - 🔴 校验顺序固定（历史坑）：先判 .. / 绝对路径（拒绝）→ 再跳过

<!-- ANCHOR:SH-034 -->
- **SH-034** · `src/Shared/Services/IColumnLayoutService.cs`
  - ⚠️ 同一个文件里存两类东西：① 列显隐（`LoadPage` / `SavePage`，键 = 列键）；
  - ② 面板开合等页面级开关（`LoadFlag` / `SaveFlag`，内部用保留前缀避免撞键）。

<!-- ANCHOR:SH-035 -->
- **SH-035** · `src/Shared/Services/IDetailCardSource.cs`
  - ⛔ 不再取 `IDetailPanelProvider.DetailContent`（那条老路保留给尚未迁移的页）；
  - 🔴 描述符走 C# 对象字面量（⛔ 不做运行时 JSON / XAML 加载）；绑定路径是字符串，
  - 必须跑 skill/scripts/check-detail-paths.ps1 做静态兜底。

<!-- ANCHOR:SH-037 -->
- **SH-037** · `src/Shared/Services/IExternalToolRegistry.cs`
  - ⛔ 不在页面里散落特判；实现放 Core（可读 IAppConfigService + PATH）。
  - 🔴 惰性探测：⛔ 不在启动时全量探测（拖慢启动）—— 只判"文件存在 + 可执行"，
  - ⛔ 不起进程跑 --version；结果缓存，`ProbeAll` 时才真正解析。

<!-- ANCHOR:SH-038 -->
- **SH-038** · `src/Shared/Services/IFavoritesService.cs`
  - 之所以必须是 config\ 下：① 只有这里的 *.json 会被「设置 → 数据管理」的备份 / 导入覆盖；
  - ② 首页与「设置 → 功能清单」都只通过本服务读写，不各自保存一份，避免双真相源。
  - 收藏项只存功能 key（如 script-info），标题 / 图标 / 简介一律到 `IMenuService` 现取，

<!-- ANCHOR:SH-039 -->
- **SH-039** · `src/Shared/Services/IFileOperationService.cs`
  - 定位：只做「磁盘上发生了什么」，⛔ 不认识任何数据库、⛔ 不弹窗、⛔ 不写业务日志。
  - 🔴 行为约定（所有实现必须一致）：
  - 只有 `PermanentDelete` 才真删（调用方负责二次确认）；③ 失败不自动回滚，
  - 把原因原样带回给调用方；④ 路径校验一律 Path.GetFullPath 后再比，⛔ 不做字符串前缀判断。

<!-- ANCHOR:SH-040 -->
- **SH-040** · `src/Shared/Services/IHistoryProvider.cs`
  - ⚠️ 本轮（）只定义契约、无页面实现 → 标题栏前进/后退按钮一律灰显。

<!-- ANCHOR:SH-041 -->
- **SH-041** · `src/Shared/Services/ILibrarySchemaContributor.cs`
  - 🔴 一个模块 = 一个库文件（store\&lt;库键&gt;\&lt;库键&gt;.db），库之间不共享连接、不跨库查询。
  - 🔴 契约：`EnsureSchema` 必须幂等（CREATE IF NOT EXISTS + 只加可空列的
  - ALTER TABLE ADD COLUMN），且可在任意启动次数下重复执行；⛔ 不许在这里做破坏性 DDL
  - （`DROP` 只允许针对本模块的可重建缓存表，并且必须带版本护栏）。
  - ⚠️ 本接口不带 SqliteConnection 参数：`Shared` 不引 `Microsoft.Data.Sqlite`。

<!-- ANCHOR:SH-042 -->
- **SH-042** · `src/Shared/Services/IMenuService.cs`
  - <paramref name="category"/> = 显式分类（需求 §4.9.2）：设置项必须标；
  - 视图类型由 <typeparamref name="TView"/> 决定（取 typeof(TView).Name）—— ⛔ 不接受字符串：

<!-- ANCHOR:SH-043 -->
- **SH-043** · `src/Shared/Services/IModuleBus.cs`
  - 投递与接收不要求同时存在，两条路径都覆盖才不会丢消息：

<!-- ANCHOR:SH-044 -->
- **SH-044** · `src/Shared/Services/IModuleBus.cs`
  - 🔴 <paramref name="handler"/> 必须是 <paramref name="owner"/> 的实例方法，⛔ 别传捕获变量的 lambda

<!-- ANCHOR:SH-046 -->
- **SH-046** · `src/Shared/Services/IModuleReloadService.cs`
  - 单个 DLL 失败被隔离；已构建的 DI 容器无法追加服务 ⇒ 热加载模块的 RegisterServices 跳过，优先页面型模块。

<!-- ANCHOR:SH-047 -->
- **SH-047** · `src/Shared/Services/IOptionsStore.cs`
  - 模块只调本接口 —— ⛔ 不碰文件、不知道底下是 json 还是 SQLite。默认实现落
  - 值一律是 string（调用方自己序列化简单类型）—— 这样换 SQLite 时表结构就是
  - module / key / value / updated_at，⛔ 不必为每种类型单独建列。

<!-- ANCHOR:SH-048 -->
- **SH-048** · `src/Shared/Services/IOptionsStore.cs`
  - ⚠️ 写失败（无权限 / 路径非法 / 磁盘满）不吞异常 —— 抛出后由调用方（模块设置服务）

<!-- ANCHOR:SH-049 -->
- **SH-049** · `src/Shared/Services/IPageCatalog.cs`
  - 🔴 存在的理由：页面由菜单项上的字符串 ViewTypeName 驱动，解析只能靠反射。
  - 本契约把扫描收成一份带缓存的索引，模块热加载后由调用方 `Invalidate`。
  - ⛔ 实现不得依赖 Avalonia：Core 层据此服务，判断「是不是控件」由调用方自行过滤。

<!-- ANCHOR:SH-050 -->
- **SH-050** · `src/Shared/Services/IProcessRunner.cs`
  - 先起读取任务再等待（避免输出超过管道缓冲把子进程卡死）；
  - 进程退出后输出读取只再等一个很短宽限期（孙进程占着管道也不许无限等，见 `ProcessRunner.OutputDrainGrace`）。
  - 🔴 不在本契约内的两类（刻意保留现状，⛔ 不一刀切合并）：① 平台能力（打开文件夹 / 文件 / URL，

<!-- ANCHOR:SH-051 -->
- **SH-051** · `src/Shared/Services/IProfileService.cs`
  - ⚠️ 切换配置 = 重启应用：配置在启动时就被大量单例服务读进内存了，热切换改动面大、且极易

<!-- ANCHOR:SH-052 -->
- **SH-052** · `src/Shared/Services/IPythonRuntime.cs`
  - 🔴 跨模块共用（网络工具的规则集、脚本管理器的 .py 命令）⇒ 接口在 Shared、实现在 Core。
  - 🔴 ⛔ 不引 pythonnet：子进程 + 行分隔 JSON（stdin/stdout），保持平台中立（framework.md §2.1）。

<!-- ANCHOR:SH-053 -->
- **SH-053** · `src/Shared/Services/ISelectionStatusProvider.cs`
  - 与 `IStatusProvider` 分开的原因：状态文本会被业务消息（「⚠ 查询失败…」/「已复制 3 项」）
  - 🔴 只有多选列表页需要实现（当前 = 全部资产 / 资产浏览 / 视频列表 / 批量处理）；
  - ⚠️ 继承 `INotifyPropertyChanged` 是为了少写代码：页面 VM 都派生自

<!-- ANCHOR:SH-054 -->
- **SH-054** · `src/Shared/Services/IStatusActionProvider.cs`
  - （典型 = 外部程序缺失时 `⚠ 未找到 ffmpeg` → 一点跳到「运行环境」节）。
  - ⚠️ 只在前缀是 `⚠`（= 警示态）时才应该给 `StatusActionKey`：
  - 普通成功文案不给跳转，避免状态栏到处可点、语义变糊。

<!-- ANCHOR:SH-055 -->
- **SH-055** · `src/Shared/Services/IStatusProvider.cs`
  - ⚠️ 列表页由 ListPageViewModelBase 统一实现（一次覆盖全部列表页）；

<!-- ANCHOR:SH-057 -->
- **SH-057** · `src/Shared/Services/IThemeService.cs`
  - ⚠️ 本接口位于 Core（不引用 Avalonia），所以它只保存状态、不碰界面：

<!-- ANCHOR:SH-058 -->
- **SH-058** · `src/Shared/Services/IToolbarSource.cs`
  - 🔴 与 IStatusProvider（底部状态栏）、IDetailCardSource（右详情卡）同一套路：
  - 没有实现就是"本页没有工具条"（Shell ⛔ 不留空行；页面继续用自己的页内工具条）。
  - 🔴 页面⛔ 不得再自写 Border.toolbar：两处同时渲染会出现两条工具条（§4.8.5 逐页 opt-in）。

<!-- ANCHOR:SH-059 -->
- **SH-059** · `src/Shared/Services/IToolbarSource.cs`
  - 🔴 为什么必须有这一层：`ToolbarItemDescriptor` 里的 CommandPath / IsEnabledPath
  - 否则沿用当前页 VM 自己（普通页零改动）。

<!-- ANCHOR:SH-060 -->
- **SH-060** · `src/Shared/Services/ViewNaming.cs`
  - 解析与静态检查必须共用本类，⛔ 不要各自写一份字符串处理 —— 两处规则一旦漂移，

<!-- WHY-DISTILL:shared:end -->

### A.3 代码 WHY 蒸馏 · Core 批（方案1 · anchors）

<!-- WHY-DISTILL:core:start -->

> 由 `skill/scripts/comment-slim.py` 生成（方案1 · 白名单与锚点口径见 `tech-guard.md` §3.1）。
> 代码里只留 1 行契约 + `WHY: <ID>`；此处是 ID 对应的 WHY 正文。`lint.py --only comments` 双向对账。

<!-- ANCHOR:CR-001 -->
- **CR-001** · `src/Core/Services/AppConfigService.cs`
  - 只填空字段（env.json 已有值 ⇒ 用户数据优先，⛔ 不覆盖）；

<!-- ANCHOR:CR-002 -->
- **CR-002** · `src/Core/Services/AppConfigService.cs`
  - ⛔ 模块 json 还没有这个键时不动 —— 那是迁移还没发生，清了就真丢路径。

<!-- ANCHOR:CR-003 -->
- **CR-003** · `src/Core/Services/AppConfigService.cs`
  - ⛔ 只在新位置还没有该文件时搬一次（已有 ⇒ 一律不覆盖，用户数据优先）；
  - ⛔ 复制不移除老文件（保守，留作回退），老文件不再被读写；

<!-- ANCHOR:CR-004 -->
- **CR-004** · `src/Core/Services/AppConfigService.cs`
  - ⛔ 只在 app.json 里还没有 Launch 时才搬（已有 ⇒ 用户数据优先）；

<!-- ANCHOR:CR-005 -->
- **CR-005** · `src/Core/Services/AtomicJsonFile.cs`
  - 进程崩溃 / 断电只会留下临时文件，⛔ 不会得到半截正式文件（旧的 File.WriteAllText 会）。
  - 删除目标文件（读方一律把「文件不存在」当默认值）。这样配置目录只留下用户真正改过的东西，
  - 不会攒出一堆 2 字节的 []。
  - 🔴 只给「配置 / 小型状态」用，⛔ 不要用它写导出文件（用户选了路径，删空文件会让人以为导出失败）。

<!-- ANCHOR:CR-006 -->
- **CR-006** · `src/Core/Services/BackupService.cs`
  - 🔴 三条铁律：
  - 只 zip 不 JSON —— db / 缩略图无法进 JSON，且 zip 才支持「只恢复其中一节」；
  - 恢复不覆盖数据 —— 设置节覆盖原文件（先归档 .bak-&lt;时间戳&gt;），数据节一律恢复到新文件（批次 B）。
  - ⚠️ 旧格式（v1 单 JSON 的 Files[]）只保留只读导入，⛔ 本服务不再产出该格式。

<!-- ANCHOR:CR-007 -->
- **CR-007** · `src/Core/Services/ColumnLayoutService.cs`
  - ⚠️ 这里不走规范 3.9 的「模块只读设置」约束 —— 列显隐是纯 UI 偏好（不是应用设置），

<!-- ANCHOR:CR-008 -->
- **CR-008** · `src/Core/Services/ConfigStoreService.cs`
  - 否则切换配置时会把自己也换掉）。
  - ⚠️ 全工程的服务都只向本类要路径（`ConfigDir` / `StoreDir` /
  - 环境变量 NEWTOYS_HOME 可把整个应用根换掉（测试 / 便携版），且不做迁移。

<!-- ANCHOR:CR-009 -->
- **CR-009** · `src/Core/Services/ConfigStoreService.cs`
  - ⚠️ 必须早于第一次读 shell.json：`ShellConfigStore` 缓存整份文档，
  - <returns>本次是否真的合并了东西（调用方据此决定要不要丢弃缓存）。</returns>

<!-- ANCHOR:CR-010 -->
- **CR-010** · `src/Core/Services/ConfigStoreService.cs`
  - ⛔ 不覆盖：目标已有同名文件 ⇒ 旧文件改名 .legacy 留档（用户数据优先）；

<!-- ANCHOR:CR-011 -->
- **CR-011** · `src/Core/Services/ConfigStoreService.cs`
  - ⚠️ 跳过以 . 开头的目录 / 文件（.bak-&lt;时间戳&gt; 归档、临时文件）；单文件失败不阻断启动。

<!-- ANCHOR:CR-012 -->
- **CR-012** · `src/Core/Services/ConfigStoreService.cs`
  - 只在 shell.json 不存在时执行（幂等）；搬完把旧文件改名 .legacy（⛔ 不删）。
  - ⚠️ 曾把 shell.json 改名成 ui.json；本迁移正好把 ui.json 收回

<!-- ANCHOR:CR-014 -->
- **CR-014** · `src/Core/Services/ConfigStoreService.cs`
  - ⚠️ 迁移失败绝不删数据：退回旧结构继续跑，并在「设置 → 配置」提示；

<!-- ANCHOR:CR-015 -->
- **CR-015** · `src/Core/Services/ConfigStoreService.cs`
  - files[{path,content}]（x DataView 自己拼的）—— 用户手里的历史备份不该因为改名而导不进。

<!-- ANCHOR:CR-016 -->
- **CR-016** · `src/Core/Services/DataMigrations/JsonDocMigration.cs`
  - 搬成功后把源文件改名 .json.legacy（⛔ 不删 —— 用户数据永远留一份可手工考回的原文）；
  - ⚠️ 「源文件不存在」完全正常（用户从没用过那页），静默跳过。

<!-- ANCHOR:CR-017 -->
- **CR-017** · `src/Core/Services/DataMigrations/StorageLayoutV2.cs`
  - 🔴 目录改名用 `Directory.Move(string, string)`：同一配置档内换名 = 秒级 rename，⛔ 不复制数据。
  - 🔴 老库只改名不删：搬完落到 store\legacy\newtoys-v1.db，装回旧版本 / 手工核对都靠它。
  - ⚠️ 这里内置 4 个库的表名归属：迁移是一次性的历史动作，需要静态清单才能在不依赖模块加载顺序的前提下搬数据。

<!-- ANCHOR:CR-018 -->
- **CR-018** · `src/Core/Services/DataMigrations/StorageLayoutV2.cs`
  - 目录整体改名不会顺手改用户录入的绝对路径（scriptsFolder / thumbsDirectory 等），

<!-- ANCHOR:CR-019 -->
- **CR-019** · `src/Core/Services/DataMigrations/StorageLayoutV2.cs`
  - 🔴 以新库为主连接、ATTACH 旧库来读：这样 DDL 与索引天然建在新库里，

<!-- ANCHOR:CR-020 -->
- **CR-020** · `src/Core/Services/DialogHooks.cs`
  - ⛔ 钩子没接上时必须按"用户没同意"处理（`ConfirmAsync` 返回 false、
  - `InputAsync` 返回 null）：宁可什么都不做，也不能在没有确认的情况下动用户的东西。

<!-- ANCHOR:CR-021 -->
- **CR-021** · `src/Core/Services/EncryptedDnsService.cs`
  - - 解析 DoH 端点自身的域名必须用系统 DNS（bootstrap），否则递归。
  - 并且一律 `ConfigureAwait(false)`；绝不在这里碰界面。

<!-- ANCHOR:CR-022 -->
- **CR-022** · `src/Core/Services/EnvConfigService.cs`
  - 语义：这里是「本机怎么跑外部程序」，跟随机器、⛔ 不随配置档切换
  - ⛔ 这些键不放进 app.json：那是配置档内参数，混在一起会被切配置带走。
  - 🔴 覆盖链（读）：命令行 --python / --pwsh / --ffmpeg ＞ 环境变量
  - 写只写 env.json（覆盖值不进文件 —— 否则"临时覆盖"会变成永久设置）。
  - ⚠️ 只有 Core 读写本文件；模块一律经 IAppConfigService，⛔ 模块不该知道文件名。

<!-- ANCHOR:CR-023 -->
- **CR-023** · `src/Core/Services/ExternalToolRegistry.cs`
  - ⛔ 不起进程跑 --version（规范：探测只判"文件存在 + 可执行"，避免拖启动 / 弹窗）。

<!-- ANCHOR:CR-025 -->
- **CR-025** · `src/Core/Services/InterpreterResolver.cs`
  - 为什么单独抽出来：QuickActionService 只开可见窗口跑脚本（不捕获输出），本页要的是
  - （QuickActionService 本期不改，留给后续复用 —— 见卡 §四「不做」）。

<!-- ANCHOR:CR-027 -->
- **CR-027** · `src/Core/Services/InterpreterResolver.cs`
  - 随 `InterpreterResolver` 从 NewToys.ScriptManager.Models 上提 Core（Core ⛔ 反向依赖模块）。

<!-- ANCHOR:CR-029 -->
- **CR-029** · `src/Core/Services/JsonDocStore.cs`
  - 🔴 为什么不拆成关系表：这些结构都是「整份读、整份写」的文档（脚本库还带三层嵌套：集合 → 视图 → 逐节列覆盖），
  - 拆表要把读方的模型全改成 SQL 投影，收益只有「人能用 sqlite 查一查」，风险远大于收益。
  - 本类保证的是卡片要的两件事：文件不再散在 config\ 里、同一份数据只有一个写入点。
  - DDL 不在这里执行 —— 由各模块的 ILibrarySchemaContributor 统一建（建表只有一个入口）。

<!-- ANCHOR:CR-030 -->
- **CR-030** · `src/Core/Services/JsonDocStore.cs`
  - 防的是「服务在 schema 阶段之前就被解析」的时序问题（只会多跑一次幂等 DDL，不会盖数据）。

<!-- ANCHOR:CR-031 -->
- **CR-031** · `src/Core/Services/JsonOptionsStore.cs`
  - 换配置天然读到另一份，⛔ 不加 profile 列 / 不加 profile 字段。
  - 手改友好：文件用缩进 + 不转义中文（路径里的中文不会变成 \uXXXX）；
  - 文件解析失败先另存 &lt;module&gt;.json.bak-&lt;时间戳&gt; 再用空表接手（⛔ 不静默丢内容）。

<!-- ANCHOR:CR-032 -->
- **CR-032** · `src/Core/Services/LaunchConfigService.cs`
  - 🔴 为什么不再自己写文件：同一个文件两个写入方 = 读-改-写互相覆盖（谁后保存谁赢）。
  - app.json 的唯一写入方必须是 `AppConfigService`。

<!-- ANCHOR:CR-033 -->
- **CR-033** · `src/Core/Services/LibrarySchemaService.cs`
  - 语义：单模块失败只记日志、⛔ 不阻断其它模块；库里版本高于代码版本（= 用旧版软件打开新版库）

<!-- ANCHOR:CR-035 -->
- **CR-035** · `src/Core/Services/ModuleLoader.cs`
  - 🔴 这里只应剩菜单注册与轻量接线。重活（建库 / 读配置 / 播种 / 扫描）走

<!-- ANCHOR:CR-036 -->
- **CR-036** · `src/Core/Services/ModuleLoader.cs`
  - —— 首帧已经画完，窗口不会被这些 IO 挡住。
  - ⚠️ 必须跑在 UI 线程：各模块的服务（Sqlite / 配置 store）都不是线程安全的，

<!-- ANCHOR:CR-037 -->
- **CR-037** · `src/Core/Services/ModuleLoader.cs`
  - ⚠️ 路径必须用 `AppContext.BaseDirectory` 拼绝对路径——

<!-- ANCHOR:CR-038 -->
- **CR-038** · `src/Core/Services/ModuleLoader.cs`
  - 🔴 四条硬约束：
  - ① 只认 Module.&lt;名&gt;.dll，⛔ 跳过已加载同名 ⇒ 本版只做「新增」，移除模块要重启；
  - ② 每个 DLL 用独立 collectible ALC，依赖一律转交默认 ALC —— 否则 IModule
  - ③ 容器已 BuildServiceProvider() ⇒ 无法再追加服务，RegisterServices 跳过
  - ④ 单个 DLL 失败被 try-catch 隔离（⛔ 不拖垮宿主）；⛔ 只读本地目录、不联网；

<!-- ANCHOR:CR-039 -->
- **CR-039** · `src/Core/Services/PageCatalog.cs`
  - 因此「同名类型取第一个」的结果与逐次扫描完全一致，⛔ 不改变既有页面解析结果。

<!-- ANCHOR:CR-041 -->
- **CR-041** · `src/Core/Services/ProcessRunner.cs`
  - 🔴 孙进程（`ms-settings:` / `.cpl` / `.msc` / `start xxx` 起的 GUI 程序）会继承并长期占住 stdout/stderr 管道，
  - 超过这个宽限一律收尾，用已经读到的内容返回。

<!-- ANCHOR:CR-042 -->
- **CR-042** · `src/Core/Services/ProfileService.cs`
  - 本类在 Core，⛔ 不引用 Avalonia —— 重启走 `Process` + `Environment.Exit(int)`。

<!-- ANCHOR:CR-043 -->
- **CR-043** · `src/Core/Services/PythonRuntime.cs`
  - <br/>环境：优先共享 venv %LOCALAPPDATA%\NewToys\tools\py\venv，否则用「运行环境 → Python 解释器」或 PATH 里的 python/python3。
  - 🔴 三个必踩的坑（已处理，改代码前先读）：① -u 不加则 Python 块缓冲，实时输出全废；
  - ② Windows 管道默认 GBK ⇒ 两侧都显式 UTF-8；③ worker 里响应必须写 sys.__stdout__（脚本的 stdout 被重定向了）。

<!-- ANCHOR:CR-044 -->
- **CR-044** · `src/Core/Services/PythonRuntime.cs`
  - 🔴 刻意不用 AppRoot：程序可能装在只读目录（Program Files），运行期写

<!-- ANCHOR:CR-045 -->
- **CR-045** · `src/Core/Services/ScriptRunner.cs`
  - 🔴 本类从 Module.ScriptManager 上提 NewToys.Core —— system 系统工具模块
  - （收藏夹 / 快捷命令）要执行条目，而模块禁互引（§3.1），执行契约只能放框架层。
  - QuickActionShell 同批上提（否则 Core 会反向依赖模块）。
  - 🔴 与 QuickActionService 的根本差别：这里 UseShellExecute=false + 重定向 stdout/stderr，
  - 🔴 命令（role=command 的采集器）走 `RunCommandAsync`：复用同一套捕获逻辑，
  - ⛔ 不复用 QuickActionService.BuildRunPlan（那个是可见窗口给用户看的）。

<!-- ANCHOR:CR-046 -->
- **CR-046** · `src/Core/Services/ShellConfigStore.cs`
  - 坏文件先归档：解析失败改名 shell.json.bak-&lt;时间戳&gt; 再用空文档接手（⛔ 不静默丢）。
  - ⚠️ 只服务框架层（主题 / 窗口 / 收藏 / 列宽 / 页码大小）。模块设置仍走 `IOptionsStore`
  - 的独立文件（config\&lt;模块&gt;.json），⛔ 不要往这里塞模块的东西。

<!-- ANCHOR:CR-047 -->
- **CR-047** · `src/Core/Services/SqliteDatabase.cs`
  - 为什么放 store\ 而不是 config\：config\ 是「人手能看懂能改的 json」，
  - ⚠️ 取连接用 `Open`，用完即 Dispose（连接池由 Microsoft.Data.Sqlite 自己管）。
  - 单库写入很轻（只在 UI 线程上按批落库），不做跨线程互斥；busy_timeout 兜住偶发争用。

<!-- ANCHOR:CR-048 -->
- **CR-048** · `src/Core/Services/SqliteDatabase.cs`
  - 🔴 快照只在「版本要变」时做：每次启动都 VACUUM 会白白写几十 MB。
  - 库版本高于代码版本（旧软件打开新库）⇒ ⛔ 不执行 DDL、⛔ 不改版本号。
  - ⛔ 不抛：任何失败都归成 `SchemaOutcome.Failed`，由调用方汇总。

<!-- ANCHOR:CR-049 -->
- **CR-049** · `src/Core/Services/SqliteDatabase.cs`
  - 🔴 用 VACUUM INTO 而不是 `System.IO.File` 复制：WAL 模式下未 checkpoint 的事务
  - <returns>快照全路径（失败 ⇒ null，⛔ 不抛）。</returns>

<!-- ANCHOR:CR-050 -->
- **CR-050** · `src/Core/Services/SqliteDatabase.cs`
  - 🔴 全工程唯一的「把库写成另一个 .db」实现：备份数据节与资产页「另存库文件」都走它。
  - ⚠️ 目标文件必须不存在（SQLite 限制）—— 调用方自己决定要不要先删 / 改名。
  - <returns>成功 true；失败 false 且 <paramref name="error"/> 给出原因（⛔ 不抛）。</returns>

<!-- ANCHOR:CR-051 -->
- **CR-051** · `src/Core/Services/StartupService.cs`
  - 为什么用注册表而不是往「启动」文件夹里放快捷方式：建 .lnk 要走 COM（WScript.Shell），
  - ⛔ 非 Windows 平台一律返回「不支持」，并如实告知 —— 绝不假装生效（规范 0.3：界面不得虚报状态）。

<!-- ANCHOR:CR-052 -->
- **CR-052** · `src/Core/Services/ThemeService.cs`
  - ⚠️ 本类在 Core，不引用 Avalonia：所以它只管状态与 theme.json，

<!-- ANCHOR:CR-053 -->
- **CR-053** · `src/Core/Services/ThemeService.cs`
  - - 旧格式（ 之前只有 { "IsDark": true }）→ 映射成 Dark / Light，老用户的偏好不丢。

<!-- ANCHOR:CR-054 -->
- **CR-054** · `src/Core/Services/UpdateService.cs`
  - ⛔ 诚实边界：
  - 本程序不会自己下载 / 覆盖自己 —— 只提示有新版本，更新仍由用户手动完成；

<!-- WHY-DISTILL:core:end -->

### A.4 代码 WHY 蒸馏 · Shell 批（方案1 · anchors）

<!-- WHY-DISTILL:shell:start -->

> 由 `skill/scripts/comment-slim.py` 生成（方案1 · 白名单与锚点口径见 `tech-guard.md` §3.1）。
> 代码里只留 1 行契约 + `WHY: <ID>`；此处是 ID 对应的 WHY 正文。`lint.py --only comments` 双向对账。

<!-- ANCHOR:SHL-001 -->
- **SHL-001** · `src/Shell/App.Crash.cs`
  - ⚠️ 只记录、不改行为：Handled 保持原样（该崩还是崩），不把程序留在半坏状态里假装没事。

<!-- ANCHOR:SHL-002 -->
- **SHL-002** · `src/Shell/App.Theme.cs`
  - 窗口里只有两块是「卡」（主显示区 + 详情列）：卡内工具条 / 表头 / 底栏 / 内容底共用 2 档，
  - 🔴 唯一例外 = 列表行区（3 档）：行区与表头同档时亮色下整片浅灰糊成一块，表格看不出边界
  - 🔴 格式被 lint.py --only regions 解析：一行一条 ("键", 档),，⛔ 别换写法、⛔ 别加注释在同一条里。

<!-- ANCHOR:SHL-003 -->
- **SHL-003** · `src/Shell/App.Theme.cs`
  - 布局变了（新增一条横条 / 挪走一个面板）就补一条边，否则新接缝不会被检查到。

<!-- ANCHOR:SHL-004 -->
- **SHL-004** · `src/Shell/App.Theme.cs`
  - 为什么放在 Shell：IPlatformSettings 是 Avalonia 类型，而 Core 不引用 Avalonia，

<!-- ANCHOR:SHL-006 -->
- **SHL-006** · `src/Shell/App.Theme.cs`
  - 为什么必须走这里：Fluent 的控件模板（RadioButton 圆点、CheckBox 勾、ToggleSwitch 滑块、

<!-- ANCHOR:SHL-007 -->
- **SHL-007** · `src/Shell/App.Tray.cs`
  - ⚠️ 托盘是 Avalonia 的原生控件，必须落在 Shell 这一层（Core 不引用 Avalonia）。

<!-- ANCHOR:SHL-008 -->
- **SHL-008** · `src/Shell/App.Tray.cs`
  - 底色取当前强调色（⛔ 不写死），换主题 / 换强调色图标跟着变。
  - ⛔ 改这里的圆角 / N 笔画比例，必须同步 Resources/app.ico 的形状口径，否则三处会走形。

<!-- ANCHOR:SHL-009 -->
- **SHL-009** · `src/Shell/Behaviors/TextBoxContextMenu.cs`
  - ⚠️ 本类不能是 static class：附加属性的 owner 要作为 RegisterAttached 的类型参数，

<!-- ANCHOR:SHL-010 -->
- **SHL-010** · `src/Shell/Controls/DialogCardHost.axaml.cs`
  - 调用方（⛔ 页面不得自造弹窗）：① App.axaml.cs 给 Core.DialogHooks 的
  - 🔴 未展示时整层 IsVisible=False ⇒ 不吃命中测试，缩放热区 / 标题栏按钮照常。
  - 🔴 钩子没接上时调用方一律按「用户取消」处理（与旧 Window 实现同口径）。

<!-- ANCHOR:SHL-011 -->
- **SHL-011** · `src/Shell/Controls/ToolbarHost.axaml.cs`
  - ② 详情开关浮层（右上角、⛔ 不占独立列，但容量计算给它留安全区）。
  - 🔴 折叠算法由「全部资产」页（AssetAllView.axaml.cs）原样搬迁，两条防抖必须保留：
  - 项宽首帧缓存一次、容量幂等短路 —— 否则切标签 / 拉窗口时工具条会抖。

<!-- ANCHOR:SHL-013 -->
- **SHL-013** · `src/Shell/Controls/ToolbarHost.axaml.cs`
  - ⛔ 不用 ComboBox：Avalonia 的 ComboBox 选完就自动收起，做不了「勾多个」。
  - 这里用普通 Button + Flyout：浮层里的按钮⛔ 不会自动关闭浮层 ⇒ 可以连着勾。

<!-- ANCHOR:SHL-014 -->
- **SHL-014** · `src/Shell/Controls/ToolbarHost.axaml.cs`
  - 🔴 为什么不会布局循环 / 抖动：项宽只在首次布局后测一次并缓存，之后只按"容器宽 + 缓存宽"纯计算

<!-- ANCHOR:SHL-015 -->
- **SHL-015** · `src/Shell/Converters/ScrollThumbRatioConverter.cs`
  - ⚠️ 为什么不用 ScaleTransform 缩 Thumb：那只改渲染不改布局，滑块会「居中缩」——

<!-- ANCHOR:SHL-016 -->
- **SHL-016** · `src/Shell/Converters/VerticalDirectionConverter.cs`
  - 🔴 Track 的垂直方向默认是「值增大 ⇒ 滑块向上」，与滚动条「值增大 ⇒ 内容下滚 / 滑块下移」相反，
  - 必须反转；水平方向默认已是从左往右，不能反转。

<!-- ANCHOR:SHL-017 -->
- **SHL-017** · `src/Shell/Diagnostics/UiStyleDiag.cs`
  - 这类问题只看截图无法区分三种成因：
  - ⚠️ 定位完就可以整文件删除（调用点只有 ShellView.axaml.cs 的两处）。

<!-- ANCHOR:SHL-018 -->
- **SHL-018** · `src/Shell/Models/PathSettingItem.cs`
  - 🔴 分类是开放集合：以后若要「指定某个功能默认使用哪个 json / 配置」，只加一个枚举值
  - （如 `功能绑定`）+ 该分类的行与工具即可，⛔ 不再新开页面 —— 这是合并两页的根本理由。

<!-- ANCHOR:SHL-019 -->
- **SHL-019** · `src/Shell/Models/PathSettingItem.cs`
  - 「配置」页并入本表：配置行也用它，靠 `Category` 区分，⛔ 不搞两套行类型。

<!-- ANCHOR:SHL-020 -->
- **SHL-020** · `src/Shell/Models/SearchTarget.cs`
  - ⛔ 不搜模块私有内容（资产名 / 脚本名）。
  - 设置节来自 `SettingsViewModel.Catalog`）⇒ 热加载模块后自动生效，⛔ 无静态缓存。

<!-- ANCHOR:SHL-021 -->
- **SHL-021** · `src/Shell/Models/TreeMenuItem.cs`
  - ⚠️ 当前不参与渲染：删掉了视图里的 emoji 回退分支，又去掉了行首展开箭头，
  - 一/二级行的图标位置都只认 `VectorIcon`。保留字段是因为模块注册仍带 icon 值

<!-- ANCHOR:SHL-022 -->
- **SHL-022** · `src/Shell/Services/DataBackupSection.cs`
  - 🔴 导出必须走 VACUUM INTO：库开了 WAL，直接 File.Copy 会漏掉还没落盘的 -wal 事务，
  - 🔴 恢复落到新文件：store\restore-&lt;时间戳&gt;\&lt;库键&gt;\&lt;库键&gt;.db —— ⛔ 不覆盖当前库

<!-- ANCHOR:SHL-023 -->
- **SHL-023** · `src/Shell/Services/DataBackupSection.cs`
  - Shell 只读这个字段，⛔ 不引 Module.MediaAssets（Release 下 Shell 不引任何模块）。
  - 恢复同样落到 store\restore-&lt;时间戳&gt;\thumbs\ —— ⛔ 不覆盖当前缩略图目录。

<!-- ANCHOR:SHL-024 -->
- **SHL-024** · `src/Shell/Services/EnvBackupSection.cs`
  - 🔴 语义上是机器级（含绝对路径），所以 `DefaultSelected` = false
  - ⚠️ 覆盖值（命令行 / NEWTOYS_* 环境变量）⛔ 不进备份 —— 只备文件本身。

<!-- ANCHOR:SHL-025 -->
- **SHL-025** · `src/Shell/Services/ShellBackupSection.cs`
  - 🔴 store\（模块库）与 cache\（缩略图）不在本节里 —— 它们属 DataBackupSection，默认不勾。

<!-- ANCHOR:SHL-026 -->
- **SHL-026** · `src/Shell/Services/ShellPageBinder.cs`
  - 独立成类的理由：契约探测与订阅生命周期（切标签、设置页切节、页面自报状态变化）

<!-- ANCHOR:SHL-027 -->
- **SHL-027** · `src/Shell/Services/WindowBoundsService.cs`
  - ⚠️ 按配置隔离（路径取 `ConfigStoreService.ConfigDir`）——多实例各记各的，
  - 本服务只做 JSON 读写；「夹进屏幕工作区」这类判定留在窗口侧（Screens 是 Avalonia API，只有 Shell 引）。

<!-- ANCHOR:SHL-029 -->
- **SHL-029** · `src/Shell/Settings/AppSettingsRegistration.cs`
  - 🔴 应用级设置页的入口 = 左树底部版本行的设置齿轮 + `Ctrl+,` + 首页「一键操作」的 `⚙`
  - 但各节锚点 Key 仍必须注册：ShellViewModel.OpenTab 靠
  - ⛔ 不注册这些入口会静默失效。故把它们挂在一个 IsHome 的不渲染宿主分组上：

<!-- ANCHOR:SHL-030 -->
- **SHL-030** · `src/Shell/ViewModels/AboutSettingsViewModel.cs`
  - 🔴 本节没有节锚点菜单 Key（需求表格里是"—（页内可点）"）⇒ 只能从设置页的节导航进入，
  - ⛔ 不进 settings 分组、⛔ 不可被 OpenTab 外部寻址。

<!-- ANCHOR:SHL-031 -->
- **SHL-031** · `src/Shell/ViewModels/AllFunctionsViewModel.cs`
  - 🔴 入口 = 首页收藏卡尾部「显示更多」就地展开（HomeViewModel 用 GetRequiredService 现行装配）；

<!-- ANCHOR:SHL-032 -->
- **SHL-032** · `src/Shell/ViewModels/DataViewModel.cs`
  - 🔴 真正的打包 / 恢复逻辑在 Core.Services.BackupService，本 VM 不碰磁盘 ——

<!-- ANCHOR:SHL-033 -->
- **SHL-033** · `src/Shell/ViewModels/DnsViewModel.cs`
  - 自定义 URL 是文本框，但只有一项、且改完立刻能"测试解析"验证，因此同样即时写盘（不为它单独设保存按钮）。

<!-- ANCHOR:SHL-034 -->
- **SHL-034** · `src/Shell/ViewModels/GeneralViewModel.cs`
  - ⚠️ 诚实边界（规范 0.3）：没配更新地址就是「未配置」，页面照实写，不假装检查过；

<!-- ANCHOR:SHL-035 -->
- **SHL-035** · `src/Shell/ViewModels/HomeViewModel.cs`
  - （⛔ 不再跳独立页）。故命令与命令参数下沉到卡片自身（收藏卡 → 导航；更多卡 → 折叠切换）。

<!-- ANCHOR:SHL-036 -->
- **SHL-036** · `src/Shell/ViewModels/LaunchConfigViewModel.cs`
  - 因为"三个按钮的配置本来就是一件事，拆到三处只会互相打架"。
  - ⚠️ 保存时在原对象上改（_launch.Config），不新建 `LaunchConfig` ——
  - 否则会把「通用设置」页维护的四个开关（开机启动 / 最小化 / 关闭到托盘 / 更新检查）冲掉。

<!-- ANCHOR:SHL-038 -->
- **SHL-038** · `src/Shell/ViewModels/PathViewModel.cs`
  - 🔴 分类决定"哪些工具可用"：路径专属工具在选中配置行时置灰并给出原因提示，反之亦然；
  - 🔴 ：细节调整：本页详情卡已删除（⛔ 不再实现 IDetailPanelProvider /
  - ⚠️ 曾去掉"视频库默认目录"； 重新引入为「导入导出默认目录」（全局，覆盖视频库 .db 与

<!-- ANCHOR:SHL-039 -->
- **SHL-039** · `src/Shell/ViewModels/PathViewModel.Probe.cs`
  - 🔴 未装 Python ⛔ 不阻断任何功能 —— 只有"跑 .py 脚本 / 抓取规则"才需要它，本行只是如实告知。

<!-- ANCHOR:SHL-040 -->
- **SHL-040** · `src/Shell/ViewModels/SettingsViewModel.cs`
  - 一节开不开只由点击决定（滚动 ⛔ 不改展开状态，见 SettingsView.axaml.cs）。
  - 🔴 ：细节调整（用户 验收前反馈，多轮）：
  - 树状滚动 + 节点抬头磁吸：⛔ 无标题栏、⛔ 无分栏、⛔ 无节导航栏；
  - 详情卡契约取消（⛔ 不再实现 IDetailPanelProvider / IDetailCardSource）——
  - 状态栏跨节聚合：`⚠` 优先且跨全部节，否则取第一个已展开节的非空；
  - 🔴 第五轮起工具条不再上抛（原走 IToolbarOwner ⇒ 顶到页面顶部）——

<!-- ANCHOR:SHL-041 -->
- **SHL-041** · `src/Shell/ViewModels/SettingsViewModel.cs`
  - ⚠️ 必须先给 View 设 DataContext 再挂进内容区（DataContext 是继承属性）—— 与 Shell 的
  - 🔴 第五轮：原「通用」节的三组设置拆成三个节，三节共用一个 GeneralViewModel
  - （数据同源、只是显示位置分开）⇒ 拆的是 View，⛔ 不是 VM。

<!-- ANCHOR:SHL-042 -->
- **SHL-042** · `src/Shell/ViewModels/SettingsViewModel.cs`
  - ⚠️ 折叠动作本身（"全部收起"文案、滚到视口顶部）全在
  - NewToys.UI.Controls.SettingsSectionHost，⛔ 本 VM 不再重复实现。

<!-- ANCHOR:SHL-043 -->
- **SHL-043** · `src/Shell/ViewModels/ShellViewModel.cs`
  - UI 契约：标签 VM 可选实现 `IDetailPanelProvider`（右详情面板）/

<!-- ANCHOR:SHL-044 -->
- **SHL-044** · `src/Shell/ViewModels/ShellViewModel.cs`
  - 再在已打开（或新建）的设置页上激活对应节 —— 切节⛔ 不新开标签页（需求 §4.9.2 第 1 条）。

<!-- ANCHOR:SHL-045 -->
- **SHL-045** · `src/Shell/ViewModels/ShellViewModel.Search.cs`
  - ⛔ 不取 MenuService 的 `_pending` 暂存区，那些还没落进任何分组、导航过去也解析不到）；

<!-- ANCHOR:SHL-046 -->
- **SHL-046** · `src/Shell/ViewModels/ShellViewModel.Tree.cs`
  - 规范 §3.7：只做「新增」（移除模块要重启）；单个 DLL 失败被隔离（⛔ 不拖垮宿主）；
  - 已构建的 DI 容器无法追加服务 → 热加载模块的 RegisterServices 跳过，优先页面型模块。

<!-- ANCHOR:SHL-047 -->
- **SHL-047** · `src/Shell/ViewModels/ShellViewModel.Tree.cs`
  - ⛔ 但不收起其它已经展开的一级 —— 用户手动展开的分组要保留。
  - 折叠态下什么都不做（二级本来就不显示），一级高亮交给 `UpdateTreeActive`；

<!-- ANCHOR:SHL-048 -->
- **SHL-048** · `src/Shell/ViewModels/ShellViewModel.Tree.cs`
  - 取不到（旧的 emoji / 未知键）返回 null → UI 回退成文本图标，不会崩。
  - ⚠️ Avalonia 12 的 ResourceNodeExtensions.TryFindResource 是 Try 模式（out 参数），

<!-- ANCHOR:SHL-050 -->
- **SHL-050** · `src/Shell/ViewModels/TechStackProbe.cs`
  - 为什么读 deps.json 而不是 Directory.Packages.props：props 是构建期文件、发布后不随包；
  - ⇒ 必须合并根目录与 Modules\ 的清单。

<!-- ANCHOR:SHL-052 -->
- **SHL-052** · `src/Shell/Views/DataView.axaml.cs`
  - 🔴 本页只做三件事：把节清单灌进 VM / 弹文件选择器 / 转交 `BackupService`。
  - 打包、版本校验、路径防御全在服务里 —— ⛔ 别在这里再写一份读盘逻辑（历史上

<!-- ANCHOR:SHL-053 -->
- **SHL-053** · `src/Shell/Views/HomeView.axaml.cs`
  - 为什么在 code-behind 里现构造菜单：ContextMenu 挂在独立的弹出树上，
  - （裸写 MenuItem 不会再撞名：应用侧菜单模型已改名 AppMenuItem。）

<!-- ANCHOR:SHL-054 -->
- **SHL-054** · `src/Shell/Views/SettingsView.axaml.cs`
  - 🔴 （）：折叠 / 磁吸 / 滚动定位整体搬进 UI 库 `NewToys.UI.Controls.SettingsSectionHost`
  - 状态栏聚合与当前节（状态栏按"哪几节展开"算，⛔ 不能只靠节自己）。
  - ⚠️ 视口相关的数学全在宿主里，⛔ 本页不再有任何 Offset 补偿。

<!-- ANCHOR:SHL-055 -->
- **SHL-055** · `src/Shell/Views/ShellView.axaml.cs`
  - —— 🔴 定案：官方 CSD 路线 + 这一处零内缩（跨平台只有官方 CSD 能对接，需求 §4.1.1）。
  - 🔴 ⑦ 的全部实现（DllImport / Win32 结构 / 钩子 / 贴靠纠偏）在 `ShellView.Win32.cs`（partial， 收敛）：
  - 本文件只留业务逻辑与它们的调用点，⛔ 别把 Win32 那段搬回来。

<!-- ANCHOR:SHL-056 -->
- **SHL-056** · `src/Shell/Views/ShellView.axaml.cs`
  - 🔴 ⛔ 不在 XAML 里声明：XAML 引用该 UserControl 会连带整个窗口的 x:Name 回填失败

<!-- ANCHOR:SHL-057 -->
- **SHL-057** · `src/Shell/Views/ShellView.axaml.cs`
  - 🔴 这里只切 Classes，厚度与颜色一律由 `App.axaml` 的 `Border.window-frame` 样式给 ——
  - 在代码里直接写 `BorderBrush` 会变成本地值，压死样式里的状态切换，且主题切换后不会跟着变。
  - ⚠️ `IsActive` 走 `OnPropertyChanged`（与 `WindowState` 同一套路，见 `OnPropertyChanged`）；

<!-- ANCHOR:SHL-058 -->
- **SHL-058** · `src/Shell/Views/ShellView.axaml.cs`
  - ⚠️ 退出一律回 `Normal`（⛔ 不回 `Maximized`）：进全屏前的旧状态Avalonia 自己会记，

<!-- ANCHOR:SHL-059 -->
- **SHL-059** · `src/Shell/Views/ShellView.axaml.cs`
  - 🔴 折叠左树后版本行的齿轮仍在，`Ctrl+,` 是它的备用入口（约定俗成的设置快捷键）。
  - ⚠️ 走隧道路由（Tunnel）⇒ 即使焦点在页内输入框也照样命中；判定用 `KeyModifiers` 而不是 `KeyGesture`。

<!-- ANCHOR:SHL-060 -->
- **SHL-060** · `src/Shell/Views/ShellView.axaml.cs`
  - ⚠️ 弹窗内容在弹出树里、不在本窗口的 NameScope ⇒ 不给它 `x:Name`，一律从 `sender` 取。
  - 🔴 弹出内容在弹出树里 ⇒ 绑定的 `DataContext` 未必继承到本窗口；这里兜底补一次

<!-- ANCHOR:SHL-061 -->
- **SHL-061** · `src/Shell/Views/ShellView.Win32.cs`
  - DWM 圆角压制 + 矩形窗口区域兜底、官方 WndProc 钩子（零内缩 + 方角同步 + 贴靠几何纠偏）。
  - 🔴 平台纪律（framework.md §2.1）：每个入口首行都是
  - ⛔ 别把这里的 DllImport 或 Win32 结构搬回主文件，也别在别处新开第二份。

<!-- ANCHOR:SHL-062 -->
- **SHL-062** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 版本事实（ 用本地 avalonia.win32\12.1.x 二进制核实，⛔ 别再照抄旧源码结论）：
  - 12.1.x 只有 UpdateWindowCornerPreference + SetWindowCornerPreference，
  - 🔴 两个调用纪律（来自 Win32Properties.cs 静态构造里的 Changed.AddClassHandler）：
  - ① 只在 属性真的变化 时下发 → ⛔ 别在构造期先调一次「占位」，否则 OnOpened 再设同值

<!-- ANCHOR:SHL-063 -->
- **SHL-063** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 为什么需要这个标记：`WINDOWPLACEMENT.showCmd` 在全屏时未必是 `SW_SHOWMAXIMIZED`
  - （12.1.x 的全屏是「无边框 + 铺满屏」），但同样必须清掉矩形窗口区域 ——
  - 否则 `SetWindowRgn` 会沿用进全屏前的旧尺寸 → 内容被裁掉一大块。

<!-- ANCHOR:SHL-064 -->
- **SHL-064** · `src/Shell/Views/ShellView.Win32.cs`
  - ⚠️ 想再复核时把它改 false 即可（等价于把 `ApplySquareRegion` 两处调用都注掉）。

<!-- ANCHOR:SHL-065 -->
- **SHL-065** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 为什么需要它：corner preference 在本窗口上试了 5 轮都没稳定生效（普通窗 / 分屏仍圆角；
  - 而「最大化没圆角」是 Win11 的固有行为，⛔ 不能算我们的功劳）。
  - MS 文档《应用圆角》明确：使用了「窗口区域」的应用，系统不会对其应用圆角 →
  - ⚠️ 最大化 / 全屏必须清掉 region（传 NULL）—— 否则矩形区域会跟最大化几何打架（露边 / 裁内容）。

<!-- ANCHOR:SHL-066 -->
- **SHL-066** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 为什么单独拆出来：Avalonia 12.1.x 的 ShowWindow → ExtendClientArea() →
  - （窗口状态变化 / 全屏 / 贴靠都会触发）→ 必须在它处理完之后再压一次（见 `PostChromeReassert`）。
  - ⛔ 从窗口过程里调的这个版本绝不能带 SWP_FRAMECHANGED：消息处理中再发消息会递归。

<!-- ANCHOR:SHL-067 -->
- **SHL-067** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 为什么必须留 ①（ 实测规律）：贴靠第 1 次缝小(~1px)、第 2 次变大(~8px) —— 8px 正是
  - 回写了几何、赢过系统给的贴靠矩形 ⇒ 只有让客户区与外框同尺寸，两边的记账才会一致。
  - 🔴 为什么改走官方钩子（12.1.x 的 Win32Properties.AddWndProcHookCallback，取代自写子类化）：
  - 🔴 历史留档（⛔ 别再加回来）：
  - ⚠️ 的 `FixTiledGeometry` 形似但判据明确（只认 tile 矩形），不是它的复活。
  - ⚠️ 装不上就什么都不做 —— 这类代码出错的代价是"窗口完全不响应消息"，失败路径必须保守。

<!-- ANCHOR:SHL-068 -->
- **SHL-068** · `src/Shell/Views/ShellView.Win32.cs`
  - 根因：系统算贴靠矩形时按「外框 = 客户区 + 边框（本机 8px/边）」换算 —— 对普通窗口成立
  - 为什么不是「去掉零内缩」：去掉就等于恢复那圈 ≈8px 非客户区，贴靠态系统不画 → 露缝（ 的原始问题）。
  - 为什么不是「事后按屏幕自检纠偏」（已删）：那是无判据的强行 SetWindowPos；

<!-- ANCHOR:SHL-069 -->
- **SHL-069** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 零内缩的实现随之简化：手搓版要「抄下系统外框 → 转发给 Avalonia → 再把 rgrc[0] 写回原值」，
  - 🔴 定案：「贴靠偏宽」不是 DWM 记账延迟，而是系统给贴靠矩形做「外框 = 客户区 + 边框」
  - 修法 = `FixTiledGeometry`（只认 tile 矩形，其余一律不碰）。两条死路已实测排除：
  - ⚠️ 最大化不拦截：最大化时外框本来就比屏幕大一圈（边框被甩到屏幕外），

<!-- ANCHOR:SHL-070 -->
- **SHL-070** · `src/Shell/Views/ShellView.Win32.cs`
  - 🔴 为什么要 Post：官方钩子在框架之前调用，而 `ReassertWindowChrome` 必须在框架之后
  - ⚠️ 标志执行完才复位：重置过程（SetWindowRgn）会同步再触发同类消息，提前复位会自激成环。

<!-- ANCHOR:SHL-071 -->
- **SHL-071** · `src/Shell/Views/UpdateAboutView.axaml.cs`
  - 🔴 两个子 View 的 DataContext 必须分别赋（`Init`）—— 外层 DataContext 只有一个，

<!-- WHY-DISTILL:shell:end -->


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

## 附录 · 淘汰卡结论留档

- NT-085 / 088 · `DynamicColumnGrid` 上提 UI 库（跨程序集 xmlns 写 `using:`，UI 库只引 Shared + Avalonia）；`ScriptRunner` / `InterpreterResolver` 归 `Core/Services`，脚本语义层 ⛔ 不落 Shell / Module。
