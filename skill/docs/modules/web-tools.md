# 模块 · 网络工具（Web Tools）

> **程序集**：`src/Module.WebTools`（`NewToys.WebTools`）
> **模块键**：`web` ｜ **一级菜单**：Order 30 · 图标 `IconWeb`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 稳定（**5 页** + 1 设置页）
> **最后校对**：2026-10-22（🔴 **NT-089 S1**：抓取能力 —— `RuleFileService` / `RuleCatalogService` /
> `RuleRunService` / `RunRecordService` / `DownloadService` / `FlowService` + 三页 `web-scrape` /
> `web-rule-editor` / `web-crawl` —— **整体迁出至脚本中心**（`Module.ScriptManager`，见
> `modules/script-manager.md`）；本模块收窄为「网络配置与诊断」，且**不再依赖 Python 运行时**。
> 跨页传物 `WebTransferService` 上提 `NewToys.UI`（两侧共享同一实例）。

---

## 1. 职责与边界

- **定位**：一次性网络诊断与配置工具（HTTP 测试 / 网址收藏 / Ping / hosts），不做常驻代理、不做抓包。
- 🔴 NT-089 起：**网页抓取（规则集 / 调试集合 / 运行结果）已迁出本模块**（详见 §2 表与 `modules/script-manager.md`）。
- 系统级写入只发生在 hosts（方案应用 / 手动编辑 / 还原，需管理员权限）；其余均为只读探测或模块内数据。

## 2. 当前功能（5 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View/VM | 说明 |
|---|---|---|---|---|
| `web-http` | HTTP 测试 | 1 | `WebHttpView` / `WebHttpViewModel` | 构造请求并查看响应。⚠️ **工具条逃生舱**：页内自绘 `Border.toolbar`（表单式撑满布局），不套统一工具条 |
| `web-bookmark` | 网址收藏（原「书签管理」） | 2 | `BookmarkView` / `BookmarkViewModel` | 收藏与整理网址、导入浏览器书签（Key / 类名 ⛔ 不改，只改显示名） |
| `web-ping` | 网络 Ping | 3 | `PingView` / `PingViewModel` | 测试主机连通性与延迟，支持切 DNS |
| `web-hosts` | Hosts 管理 | 7 | `HostsView` / `HostsViewModel` | **双列表工作台**：左系统 hosts/备份 vs 右拉取方案，条目增删改 + 备份 / 还原 + 写入（Order 7 = 原值，⛔ 不重排） |
| `settings-web-tools` | 网络工具设置 | 100 | `SettingsWebToolsView` | 抓取默认 UA / 超时 + **Hosts** 检测超时 / 并发 / 备份份数 / 刷新 DNS + **开源项目源清单** |

- 页 Key 一经发布 **⛔ 不改**（例外：`web-hosts-system` 于 2026-10-26 经用户拍板撤销，功能并入 `web-hosts`）。

## 2.5 操作逻辑（交互契约）

> 跨模块共用语义见 `../ui.md` §10。本模块多数页是「配置 + 一键执行」型，无状态机；**长任务必须可取消**。

### HTTP 测试 `web-http`（工具条逃生舱）

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 发送请求 | 页内表单 + `▶ 发送` | 发请求并在下方显示响应 | 请求中禁用；**⛔ 不套统一工具条**（表单式撑满布局是刻意例外） |
| 清空 | `🗑 清空` | 清表单与响应区 | — |

### 网址收藏 `web-bookmark`（原「书签管理」）

**整页 = 一棵树**（NT-106）：文件夹行（带 `(N)` 条数、行首 `▾/▸` 仅作**静态指示**）与书签行同列表混排，缩进 16/级；
**折叠入口 = 双击文件夹整行**（键盘 ← / →），⛔ 无独立箭头按钮；树由扁平列表按 `Folder` 全路径派生，老数据（单层名）零迁移。
列表**多选**：`Ctrl` 点 = 增 / 减选该行，`Shift` 点 = 从基准行连选（`Ctrl+Shift` = 追加连选），`Ctrl+A` = 全选可见行 —— 多选作用于「📁 移动到」整批、`🗑 删除` 整批与状态栏计数，其余命令仍只作用于主选中行。
改名统一在**详情卡**（书签改标题、文件夹改本层目录名），⛔ 无行内重命名 / 无 `F2` / 无右键「重命名」——避免与详情卡编辑重复。
列 = 标题 / 网址 / 状态（「分组」列已删 —— 树缩进已表达层级）。

工具条：`📥 导入书签` / 搜索框 / `➕ 新建` / `🔗 检测全部` / `🚀 批量打开` / `🔄 重读收藏` / **打开方式下拉**(190)；
行右键：`🔗 联通检测` / `📌 加入·移出批量打开` / `🌐 打开` / `📋 复制网址` / `🕷 发送到网页抓取` / **`📁 移动到`**（子菜单）/ `✏ 编辑` / `🗑 删除`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 导入书签 | `📥 导入书签` | 选浏览器导出的 HTML → 批量入库，**保留文件夹层级** | 路径须 `Path.GetFullPath` 校验；按 URL 去重；空文件夹不保留 |
| 书签编辑 | 行右键 `✏ 编辑` 或工具条详情开关 → 详情卡改标题 / 网址 / 分组 / 备注 → `💾 保存` | 右键 `✏ 编辑` = 展开右侧详情栏并给状态提示；写回收藏库并按新分组重挂树 | 字段失焦即写进内存，落盘靠「💾 保存」；网址为空拒绝；⛔ 右键直接绑基类 `ShowDetailCommand`（只改内存标志、Shell 不刷新 ⇒ 表现为「点了没反应」） |
| 文件夹改名 | 选中文件夹行 → 详情卡「名称」输入框 → `💾 保存改名` | 改**本层**目录名：整棵子树（子文件夹 + 网址）跟着换路径前缀，折叠记忆一并搬、选中跟到新行 | 名含 `/` 拒绝（层级只能由「📁 移动到」表达）；空名 / 未改动 = 不动；一次落盘 |
| 移动到其他节点 | 行右键 `📁 移动到` → 选目标 | 该条 `Folder` 改成目标文件夹全路径 ⇒ 树里换挂载点；祖先自动展开、选中跟到新行 | 子菜单 = 根级「（根级 · 未分组）」+ 全部文件夹，**排除当前所在文件夹**（避免同义动作）；只作用于**书签行**（选中文件夹行时子菜单置灰）；⛔ 不做行拖拽 |
| 批量移动 | `Ctrl` / `Shift` 多选书签行 → 右键 `📁 移动到` → 选目标 | 选中的书签**一次**全改到目标文件夹（**只落盘一次**），状态栏报「已把 N 条网址移到「X」」 | 多选里混入的文件夹行**被忽略**（⛔ 不含子树）；多选时目标列表不再排除任何文件夹；右键**未被选中**的行 = 改为单选它，右键**已选中**的行 = 保持整批 |
| 删除 / 批量删除 | 行右键 `🗑 删除`（或详情卡 `🗑 删除`） | 单选 = 删这一条；多选（≥2）= **整批删**，一次落盘，状态栏报「已删除 N 条网址」 | 多选里混入的文件夹行**被忽略**（文件夹行本身不可删，选中文件夹行时命令置灰）；删完清空多选快照，选中落到最近的可见祖先文件夹 |
| 展开 / 折叠 | **双击**文件夹行（或选中后 ← / →） | 收放该子树（只记内存，⛔ 不持久化） | 行首 `▾/▸` 只是静态指示符、不接收点击 |
| 检测全部 | `🔗 检测全部` | 并发测连通，回填状态列 | 忙碌禁用；可取消 |
| 全部打开（文件夹行） | 详情卡 `🌐 全部打开` | 子树全部网址交给**所选浏览器** | 纯临时调用，**⛔ 不写** `LaunchConfig.Urls` |
| 全部检测（文件夹行） | 详情卡 `🔗 全部检测` | 子树逐条回写状态 | 并发 6，复用单条检测 |
| 打开方式 | 工具条下拉 | 决定「打开 / 批量打开 / 全部打开」用哪个浏览器 | 复用「设置 → 启动与行为」的**浏览器1 / 浏览器2**（⛔ 不新建配置）；未配程序路径标「未配置」且相关动作置灰 |
| 写入浏览器配置 | 详情卡 `📌 写入浏览器配置` | 该书签 URL 追加进所选浏览器的网址列表 | **追加 + 去重**（幂等）；经 `ILaunchConfigService.Save()` 落盘 —— `app.json` 的 `Launch` **第二写入点**（用户 2026-10-24 授权） |
| 复制网址 | 详情卡 `📋 复制网址` / 右键「📋 复制网址」 | 书签行 = 该条网址进剪贴板；文件夹行（详情卡 `📋 复制全部网址`）= 子树网址一行一个 | 只读操作，**⛔ 不动任何配置 / 存储**；右键项按选中类型自动分流 |
| 打标批量打开 | 行右键「📌 加入 / 移出批量打开」 | 切换该条标记 | — |
| 批量打开 | `🚀 批量打开` | 优先打开选中项，否则打标项 | — |
| 送抓取 | 行右键「🕷 发送到网页抓取」 | 带该网址跳 `web-scrape` | — |
| 新建 | `➕ 新建` | 落在当前选中文件夹（选中书签行取其所属文件夹；无选中 = 未分组） | — |
| 重读书签 | `🔄 重读书签` | 重读本地书签库 | **⛔ 不联网、不导浏览器** |

- 搜索 / 快速筛选 = **重建子树**：只留命中书签及其全部祖先文件夹，并强制展开（否则搜到看不见）；无命中 ⇒ 空态。
- 表头排序**只在同一父节点内**生效（⛔ 不跨层重排）；「分组」列已删（树本身就是分组顺序，那列排序永远置灰）。
- 选中文件夹行 ⇒ 6 个书签动作 `CanExecute=false`；详情卡换「路径 / 书签数」摘要。


### 网络 Ping `web-ping`

工具条：`📥 导入文件` / `➕ 新建` / `📡 批量 Ping` / `🔄 重读目标` / **DNS 下拉**(200)；行右键：`📡 Ping` / `🔖 发送到书签` / `🕷 发送到网页抓取` / `✏ 编辑` / `🗑 删除`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 导入目标 | `📥 导入文件` | 一行一个主机的文本 → 批量加入 | 空态按钮也指向它 |
| 批量 Ping | `📡 批量 Ping` | 并发执行，回填 IP / 状态 / 时延 | 忙碌禁用；可取消 |
| 切 DNS | 工具条 **DNS 下拉** | 批量 Ping 与**未单独设 DNS** 的目标按此解析 | 单条可在详情卡 `DnsOverride` 覆盖；优先级 = 条目 > 页面 |
| 重读目标 | `🔄 重读目标` | 重读本地目标清单 | **⛔ 不发起 Ping** |
| 送书签 / 送抓取 | 行右键 | 带主机跳对应页 | — |

### Hosts 管理 `web-hosts`（双列表工作台）

> 🔴 2026-10-26（NT-111）：原 `web-hosts-system`「系统 Hosts」页已撤 —— 展示 / 文本编辑 / 备份 / 还原 / 清除受管块
> **全部并入本页**，⛔ 别再单独开「系统 hosts」页。

布局：① 信息条（路径 / 字节·行数·修改时间 / 受管块区间 / 权限 / 差异汇总）② 左「目标」| 右「来源」双列表 ③ 底栏（应用 + 写入）。
左栏数据源 = **当前系统 hosts 或某份备份快照**（下拉切换；备份只读，可直接还原）；右栏数据源 = **方案**（拉取结果也是方案）。
工具条：`📦 备份当前` / `♻ 还原` / `🗑 删备份` / `📄 文本`（↔ `📋 条目`）/ `🧹 清除受管块` / `📂 备份目录` / `🔍 检测` / `➕ 新建方案` / `🔄 重读`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 编辑条目 | 点任一行 → 详情卡改 IP / 主机名 / 注释 / 启用 → `💾 保存条目` | 左栏写**草稿**，右栏写方案内存 | 左栏需再点「✅ 写入系统 hosts」才落盘；备份快照只读 |
| 新增 / 删除条目 | 详情卡 `➕ 新增` / `🗑 删除` | 左栏追加到草稿末尾 / 右栏加到方案末尾 | 左栏删除二次确认 |
| 应用到左侧 | `⬅ 应用到左侧` / 右栏右键 | 右栏**启用**条目 → 替换左栏草稿的受管块 | ⛔ 不落盘；备份快照下不可用 |
| 写入系统 hosts | `✅ 写入系统 hosts` | 二次确认 → 自动备份 → 整份写入草稿 | 🔴 需管理员；备份失败 ⇒ 放弃写入；**⛔ 静默失败** |
| 文本视图 | `📄 文本` | 左栏变整份可编辑 `TextBox`（`AcceptsReturn` + `NoWrap`，自带双向滚动条） | 备份快照只读；改动同样只进草稿 |
| 备份当前 | `📦 备份当前` | `data\web\hosts-backup\hosts-<时间戳>.bak`（**完整原文**） | 只读也能备份；超出保留份数删最旧 |
| 还原 | 左栏下拉选备份 → `♻ 还原` | 整份回写成该备份 | 二次确认；还原前先把当前文件再备份一份（可反悔） |
| 删除备份 | `🗑 删备份` | 只删备份文件 | 二次确认；⛔ 不动系统 hosts |
| 清除受管块 | `🧹 清除受管块` | 从草稿删掉 `start/end` 之间 | 用户自有行保留；仍需「✅ 写入」才落盘 |
| 拉取源 | 右栏源下拉 + `⬇ 拉取` | 按地址逐个试，第一个拉到 ≥N 条的胜出 → 覆盖同名源方案 / 新建方案 | ⛔ 不自动写系统 hosts；预设源要求 ≥20 条（防错误页），自定义 ≥1；比上次少一半 ⇒ 标注疑似失效 |
| 检测可用性 | `🔍 检测` / 右键 / 详情卡 `🧪 检测此条` | 本机 DNS vs 映射 IP + TCP 443（失败回退 80）+ 时延 + 静态安全 | 只测**启用**条目；并发 / 超时读设置页；可取消；**结论不落库** |
| 导入 / 导出 | 详情卡 `📥 导入文件` / `📤 导出方案` | 本地 hosts 文本 → 当前方案 / 方案 → `.hosts` | 路径 `Path.GetFullPath` 校验；无选中不执行 |
| 保存方案 | `💾 保存方案` | 右栏条目落库（`web_docs/hosts-profiles`） | 左栏草稿不受影响 |
| 删除方案 | `🗑 删除方案` | 删模块内方案 | ⛔ 不动系统 hosts |
| 差异标记 | 两栏行首列 + 「差异」勾选 | `＋` 仅右栏有 / `－` 仅左栏有 / `≠` IP 不同；勾选后只显示不一致行 | 按 **host** 比对（与系统实际生效一致，重复域名以最后一条为准） |

- 源清单管理**只在本模块设置页**（「Hosts 开源项目源」节）：预设可删（删了不复活）、可新建 / 改地址 / 测试拉取；Hosts 页只拿只读下拉。
- 受管块仍然是「应用」的落点：`⬅ 应用到左侧` 先 `StripManagedBlock` 再追加新块 ⇒ 用户手写行不受影响。

### 网络工具设置 `settings-web-tools`

> 🔴 NT-071：本页改用**共用设置页骨架**（`NewToys.UI/Controls/SettingsSectionHost`，`Sticky="False"`）—— 2 个节 = 折叠卡片
> （网页抓取 / 设置文件），点抬头开收，默认全展开。细则见 `ui.md` §11。

| 类型 | 写入时机 |
|---|---|
| 超时 / 并发 / 默认 UA / 解析器 | **失焦即落盘**（`OnXxxChanged` → `config\web-tools.json`），**⛔ 无保存按钮** |
| Hosts 开源项目源清单 | 新建 / 编辑 / 删除 / 测试拉取 → 即时落 `web_docs/hosts-sources`；🔴 **本页是源清单的唯一写入点**（Hosts 管理页只读下拉，⛔ 不再有「➕ 源」） |

> 🔴 NT-089：抓取页虽已迁入脚本中心，但**抓取默认 UA / 超时仍由本页写入** `config\web-tools.json`
> （菜单 Key `settings-web-tools` ⛔ 不改；脚本中心经 Core 的 `IOptionsStore` 只读同一份文件）。
> 归位到 `config\modules\script-manager.json` 属 NT-094。

## 3. 实现逻辑

| 文件 | 职责 |
|---|---|
| `HttpTestService` | HTTP 请求构造 / 发送 / 响应解析（`web-http`） |
| `BookmarkService` | 网址收藏 CRUD + 浏览器书签导入（`web-bookmark`） |
| `PingService` | Ping 执行与延迟统计（`web-ping`） |
| `DnsResolver` | DNS 解析（工具条切 DNS 的来源） |
| `HostsService` | hosts 方案读写 / 系统 hosts 读·写·备份·还原·清除 / 受管块 diff / `ParseLines`·`RenderLines`（行模型 ↔ 全文）/ `StripManagedBlock`（纯函数，草稿用） |
| `HostsSourceService` | hosts 源订阅（预设 + 自定义，落 `web_docs/hosts-sources`）+ 拉取解析（镜像 fallback、跌幅判失效） |
| `HostsCheckService` | 条目可用性（DNS 对比 + TCP）+ 静态安全规则 |
| `IHostsPlatform` / `WindowsHostsPlatform` | 平台底座：系统 hosts 路径 + `ipconfig /flushdns`（其它平台返回不支持） |
| `WebTransferService`（已上提 `NewToys.UI`） | 跨页一次性传参：书签 / Ping ↔ 抓取（脚本中心）；两侧共用同一单例 |
| `WebToolsSettingsService` | `config\web-tools.json` |

- 数据与配置：`config\web-tools.json`（设置：UA / 抓取超时 / Hosts 检测超时·并发·备份份数·刷新 DNS）；网址收藏、ping 目标、hosts 方案、hosts 源全部落总库 `data\newtoys.db` 的 `web_docs` 文档；系统 hosts 备份落 `data\web\hosts-backup\`。⛔ 模块内不自己拼 `ConfigStoreService` 的根路径。
- 🔴 **NT-089 S1：抓取能力已整体迁出本模块** —— `RuleFileService` / `RuleCatalogService` / `RuleRunService` /
  `RunRecordService` / `DownloadService` / `FlowService` / `RulesBackupSection` + 三页 `web-scrape` /
  `web-rule-editor` / `web-crawl` 与规则备份节，现归 **`Module.ScriptManager`**（见 `modules/script-manager.md`）。
  本模块保留 4 页（HTTP 测试 / 书签 / Ping / Hosts）+ 设置页，**不再依赖 Python 运行时**。
  - 路径随之搬家：规则 `<DataDir>\script\rules\`、缓存 `<DataDir>\script\cache\`、
    下载 `<DataDir>\script\downloads\` / `trials\`；`runs` / `flows` 亦已归位 `<DataDir>\script\`（NT-090）。
  - `WebTransferService`（跨页传物）上提 `NewToys.UI` —— 书签 / Ping 与抓取**跨模块**共用同一实例。
  - ⚠️ 抓取相关踩坑（§5 的 5~27）随代码迁出，内容保留备查；新改动见 `script-manager.md`。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `WebToolsModule.cs` |
| 工具条逃生舱样板 | `Views/WebHttpView.axaml` |
| 系统写入点 | `Services/HostsService.cs`（任何写系统 hosts 的路都由它落地） |
| 备份与受管块 | `Services/HostsService.cs`（`BackupNow` / `Restore` / `ManagedRange` / `DiffAgainstManaged`） |
| 源订阅 | `Services/HostsSourceService.cs`（预设清单在文件头，改预设 = 改这里） |
| 检测 | `Services/HostsCheckService.cs`（可用性 + 静态安全规则） |
| Hosts 工作台页 | `Views/HostsView.axaml` + `ViewModels/HostsViewModel.cs`（列表 / 备份 / 源）+ `HostsViewModel.Detail.cs`（详情卡条目编辑） |

## 5. 坑点（🔴 已知）

> **NT-076 第二批**：书签 / ping 目标 / 抓取任务 / hosts 方案四份数据已从 `config\*.json` 搬进总库 `web_docs`（文档名 = 旧文件名基名）。读写统一走 `Core/Services/JsonDocStore`；空列表 ⇒ 删文档（沿用「空即删」），⛔ 别在 `config\` 下再建同名文件。

1. 🔴 `web-http` 是**工具条统一改造的已知例外**（页内自绘工具条），⛔ 不要为了统一去改它的表单式撑满布局。
2. hosts 写入系统文件需管理员权限 ⇒ 失败必须给可读提示，⛔ 静默失败。
3. 抓取 / Ping 属长任务 ⇒ 必须可取消（`CancellationToken`），⛔ 阻塞 UI 线程。
4. 导入外部书签 / hosts 文件时路径须 `Path.GetFullPath` 校验（见 `../framework.md` §7 路径穿越防御）。
5. 🔴 **规则解析只在 Python 侧**（NT-084）：C# 只送路径 + 回读一行 JSON。改规则语言（css/xpath/regex/json）不用动 C#；反过来 ⛔ 别把解析逻辑搬进 C#。`json:` 用标准库自实现（⛔ 不引 jsonpath），路径支持 `$a.b[0]` / `[*]`，⛔ 不支持 `js:`（要 JS 引擎）。
6. 🔴 **Python 缺失只提示、⛔ 不阻断**：页面照常打开 / 编辑 / 保存，只有抓取后跑规则需要 Python（`IPythonRuntime.Probe()` 未就绪 ⇒ 状态栏给「怎么装」的指引）。
7. 抓 HTML 要给 GBK / GB18030 注册 `CodePagesEncodingProvider`（net10 共享框架自带，⛔ 不引第三方包）；HTML 显示有上限（40 万字符 / 8000 行），⛔ 别整页塞进 TextBox。
8. 🔴 **规则集 `.py` 是 `exec_module` 整文件执行的** ⇒ 文件里任何一行语法错误 = 整个规则集连 `RULES` 都读不出来（2026-10-20 踩坑：得奇小说.py）。写脚本时**注释必须逐行加 `#`**（只注首行 ⇒ 续行裸奔 ⇒ SyntaxError）。
9. 🔴 **表达式方言（2026-10-20 去 Legado 后）**：`css:` / `xpath:` / `regex:` / `json:` / **`text:`** / **`link:`** / **`url:`** / `=常量`；**不写前缀 = 按 CSS 解析**；⛔ 不再支持 legado 默认规则（`class.x@tag.a@text`）。多候选 `||`、净化 `##正则##替换` 不变。
   - 三个语义操作符（NT-086 批 F，引擎里转 xpath，⛔ 不是新语法）：`text:下一页` = 按可见文本找元素（**优先 `<a>`**，找不到再退回 `//*`）；`link:下一章` = 只找 `<a>` 按文本；`url:/chapter/` = 按 href 片段找 `<a>`，**不写 `@` 时默认取 `href`**（翻页 / 下钻要的是地址）。
   - 🔴 **契约硬禁令**：⛔ 绝对位置路径（`/html/body/…`、`div[3]` 这类连续索引）。稳度分级（写在契约里给 AI 看）：功能文本 / URL 模式 ＞ 语义标签层级 ＞ 主题类名 ＞ 绝对位置。
10. 🔴 **脚本头模板的说明块⛔ 不用英文冒号**（NT-086）：解析器只认 `# 键名: 值`（英文冒号）⇒ 模板里的键说明写成 `# desc —— …`（em dash 体）；否则说明文字会被当成真值读进去（症状：新规则集的 UA 池里混进文档行、分类值变成一长串说明）。

11. 🔴 **试跑推荐必须一次进程跑完**（NT-086 批 C）：`mode=probe` 在 Python 侧循环所有候选脚本，⛔ 不要在 C# 里逐脚本 `RunAsync`（N 次解释器启动 = 慢到不能用）。共享 `Selector` 与 JSON 缓存也只对**同一份 HTML** 有效 ⇒ 换页必须重新抓页。
12. 🔴 **撤出左树菜单靠 `system` 组**（NT-086 批 C）：`menu.RegisterFunction("system", …)` —— 该组 `IsHome=true` ⇒ 左树 / 首页计数 / 功能清单都跳过它，但 `FindItem` 仍能解析 ⇒ `NavigateTo("web-rule-editor")` 照常可用。⛔ 不要直接删注册（导航会静默失效：拿不到 ViewTypeName）。
13. 🔴 **槽位候选的 role 过滤有假设**（NT-086 批 C）：无 `# role:` 的脚本按 `pipeline`（整站）处理，**只允许进 `entry` 槽位**；其余槽位严格按 role 匹配 ⇒ 「整站脚本想当 content 段」目前做不到（要先给它加 `# role: content`）。可回退。
14. 🔴 **错误回显是硬要求**：Python 侧返回 `error` 字段，C# 必须搬运到界面（命中列显示「⚠ 出错」+ 右栏 `⚠ <traceback>`），⛔ 禁止只显示「0 条」—— 0 条会让人以为是选择器写错，实际可能是脚本语法错误（§5.8 的 `exec_module` 陷阱）。
15. 🔴 **XML 源必须 `local-name()` 匹配**（NT-086 批 E，2026-10-20 实测）：RSS / Atom / sitemap / OPDS 都带默认命名空间（`xmlns="http://www.w3.org/2005/Atom"`），parsel 的 `//entry` / `css:entry` **匹配不到**（lxml.html 尊重命名空间）⇒ 一律写 `xpath://*[local-name()='entry']/*[local-name()='title']`。症状：探测 / 试跑对真 feed 返回 0 条但无报错。
16. 🔴 **`||` 后续候选省写前缀时继承前一个的**（NT-086 批 E 修）：`xpath://a||//b` 的 `//b` 以前会掉进 CSS 分支报「Expected selector, got <DELIM '/' at 0>」；现在引擎用 `_norm_cands` 补前缀（`css:a||b` 的 `b` 也按 css）。写多候选表达式两种都对，推荐省写。
17. 🔴 **探测的「能解析出条目」要滤掉错误占位符**（NT-086 批 E）：`json:` 找不到可解析 JSON 时返回的是 `"(json: …)"` **占位串（非空 list）** ⇒ 探测循环必须把 `(...)` 形态的值滤掉再计数，否则 404 页也会被算成 1 条（⛔ 假命中）。
18. 🔴 **`mode=rows` 行表按索引合成**（NT-086 批 D）：各字段独立求值后按位置合成行，缺字段补空串（⛔ 不按索引硬取 ⇒ 不错位）；错误列 count=0 + error 回显，**不污染其他列**；Python 侧截到 1000 行。实测：rss 打 atom.xml 三列同源对齐、只有 title 的脚本不出错、坏表达式列单独报错。
19. 🔴 **面板行右键 / 节头命令挂数据对象**（NT-086 批 D）：ContextMenu 在弹出树里取不到页面 VM ⇒ 行命令（试下载 / 复制诊断）挂 `CrawlEntry`、节命令（重跑 / 折叠）挂 `CrawlSection`（与 `SysInfoHeaderCell.SortCommand` 同一套解法）。
20. 🔴 **面板「脚本 ▾」列全部规则集**（NT-086 批 D）：段脚本 / 整站脚本 / 预设都能跑；`# role:` 只影响调试集合槽位过滤，⛔ 不过滤面板下拉。
21. 🔴 **XAML 的 `IsVisible` 只认 bool**（NT-087）：⛔ 不能绑 `string.Length` / `Collection.Count`（Avalonia 默认转换器不收）⇒ 一律在 VM / 模型上加 `HasXxx` bool 属性（集合还要在 `CollectionChanged` 里补发通知）。
22. 🔴 **`ProgressBar.Maximum` ⛔ 不能喂负数**（NT-087）：链式模式总数是 -1 ⇒ 另给 `DlMax`（≤0 时给 1）+ `IsIndeterminate` 走不确定态。
23. 🔴 **链式队列不能在「队列空」时直接 break**（NT-087）：并发任务还会往队列里加 `rows` 子条目 ⇒ 必须「队列空 **且** 在跑任务数为 0」才退出，否则 `Task.WhenAny` 等一个跑完再看队列。
24. 🔴 **`mode=step` 只求值指定段、回全量值**（NT-087）：`RunStepAsync(..., keys: "content,next")` ⛔ 不截断、⛔ 不预览；翻页 / 并发 / 停止条件全在 C# 侧（Python 只答「这一页有什么、下一页在哪」）。
25. 🔴 **下载器内置 ≠ 写操作脚本化**（NT-087）：安装/升级/卸载类写操作仍 ⛔ 不脚本化；这里脚本化的只有「解析出什么」（读），落盘 / 并发 / 取消是 C# 的事。
26. 🔴 **`||` 多候选必须写在 `##` 净化之前**（2026-10-20 得奇实战）：`eval_expr` 先按 `##` 切分 ⇒ 写成 `A@text##^前缀##||B` 会把整个 `||B` 吞进**过滤器数组**（候选 B 永远进不去）。正确顺序：`A@text||B##^前缀##`（净化对胜出的那个候选生效）。症状：该列在多形态页面（详情 / 列表）上只有一半场景有值。
27. 🔴 **JS 取正文类站点抓不到内容**（2026-10-20 得奇小说）：章节页 `#chapter-content` 恒空，真正文靠 JS 二次请求 `ajax2.php`（`token` + `timestamp` + `nonce` 一次性，缺任一回「请求已过期」）⇒ 单章静态抓取**必然空**。出路是找站点自带的**免 token 打包接口**（该站 `packdown.php?aid=&start=&end=`，一次 ≤501 章、GBK 纯文本），写法见实测段脚本。同理：改 UA 伪装（百度 / Google spider）对这类站无效。
28. 🔴 **书签树⛔ 用 `TreeView`**（NT-106）：`TreeViewItem` 的层级缩进会把**整行**（含右侧网址 / 状态列）一起右移 ⇒ 与表头必然错位。实现 = 树先序拍平成 `VisibleNodes` + 缩进只作用于第一列的 `Width=Depth×16` 占位 Border（vm `BookmarkViewModel.ApplyView/Materialize`）。要加列 / 改列宽只动列规格串，别碰缩进。
29. 🔴 **`BookmarkItem.Folder` 是「全路径」**（NT-106）：`/` 分隔（`工作/项目A`），老数据单层名等价单层路径。派生树只能从「有书签的路径」长出中间节点 ⇒ 纯空文件夹导入后消失（接受）。文件夹名自身含 `/` 会被拆级（罕见，接受）。
30. ⚠️ **工具条 `Combo` 不支持逐项禁用**（NT-106）：「未配置」的浏览器项只能靠**文案标注 + 相关命令 `CanExecute` 置灰**表达，⛔ 不能真的把下拉项灰掉（`ToolbarMenuItemDescriptor` 没有 `IsEnabledPath`）。
31. ⚠️ **`ListFooterBar` 的「每页」组恒显示**（NT-106）：它不在 `HasPaging` 的显隐控制里 ⇒ 树形态（无分页）**不显示底栏**，计数改由 Shell 底部状态栏承载（VM `StatusText` =「共 N 条网址 · M 个文件夹」）。
32. 🔴 **备份存的是「完整系统 hosts 原文」，不是受管块**：还原 = 整份回写（用户手改的行也一起回来）。⛔ 别图省事只存受管段 —— 那样「还原」和「应用方案」就没区别了。
33. 🔴 **写系统 hosts 一律「先备份、备份失败就放弃写入」**：`WriteWithBackup` 是唯一落点（应用 / 手动保存 / 清除 / 还原都走它）。⛔ 别在任何路径上先写后备份。
34. 🔴 **权限判定用「能不能打开文件写」而不是「是否管理员」**：`CanWrite()` 探测 `FileStream(ReadWrite)` —— 有 ACL 授权的普通用户也能写，反过来只看 `IsElevated` 会误判。
35. ⚠️ **`DetailAction.ConfirmText` 是静态字符串**（渲染层不绑 VM）：动态 diff 文案进不了确认框 ⇒ diff 由「🧪 预检」显示在字段里，确认框只给固定提示。要动态文案得改 `Shared`（框架层，另立项）。
36. 🔴 **检测结果必须 `[JsonIgnore]`**：`HostsEntry` 的 `Status` / `LatencyMs` / `ResolvedIp` / `Port` 与 `HostsProfile.HealthText` 都只存内存 —— 落库就会拿上次的陈旧结论冒充本次检测。
37. 🔴 **预设源地址必须实测过才准加**（HTTP 200 + 可解析条目 ≥ 20）：GitHub 加速类项目地址常变，`raw.githubusercontent.com` 在国内多数不可达。当前两个预设（GitHub520 / ineo6-hosts）走 `raw.hellogithub.com` 与 `fastly.jsdelivr.net`，均实测 200。
38. ⚠️ **文本视图的 `TextBox` 用自带滚动**（`AcceptsReturn` + `NoWrap` + 双向 scrollbar），⛔ 别外面再套 `ScrollViewer`（抢滚动 + 行宽失控）。
39. 🔴 **左栏一切都是草稿**：条目编辑 / 文本视图 / 清除受管块 / 应用到左侧 都只改 `_draftText`，⛔ 别在这些路径里直接写盘 —— 唯一落盘点是「✅ 写入系统 hosts」（`WriteRaw`，内部自动备份）。
40. 🔴 **行模型是全文的投影**：`HostsService.ParseLines` 保留非条目行原文，`RenderLines` 条目行重新对齐渲染 ⇒ 编辑一行不会丢注释行 / 受管块标记行；行号会随增删变化，删除 / 保存按 `No` 重新查，⛔ 别缓存行对象当索引。
41. 🔴 **差异标记按 host 比对**（不是行），与系统实际生效一致（重复域名以最后一条为准）；两栏标记在 `RefreshRows` 里一次算完，⛔ 别在行模板里各算各的。
42. 🔴 **选中联动要抑制**：两栏 `SelectedItem` 互斥，`RefreshRows` 恢复选中时必须置 `_suppressSelectionSync`，否则重建会把编辑目标来回清空（表现为详情卡空白）。
43. 🔴 **书签页的 Ctrl / Shift 多选是自己算的**：`SelectionMode=Multiple` 只管「能选多个」，修饰键点击由 `BookmarkView` 在**隧道**阶段截（`e.Handled=true` + 手工 `SelectedItems` 增删 + 手工 `Focus()` 容器），Shift 连选基准 `_anchor` 在这里维护。⛔ 别指望改 `SelectionMode` 就自动有 Shift 连选；也别在隧道之外补 —— 会和默认「点谁选谁」打架。
44. 🔴 **批量动作的候选集只有一个来源**：`ActionCandidates()`（多选 ≥2 → 多选快照；否则主选中；再退回快照）。移动 / 删除都走它，⛔ 别在命令里各自读 `Selected` —— 多选时会退化成只处理一条。多选快照 `_multiSelected` 只由 View 的 `SelectionChanged` 回填（`_rebuilding` 期间忽略），删除后必须 `Clear()`。
45. ⚠️ **文件夹卡「名称」是草稿属性**：`BookmarkNode.Name` 是 `init` 快照 ⇒ 输入框绑 `FolderNameDraft`（`OnSelectedNodeChanged` 里按当前文件夹重置），「💾 保存改名」才把整棵子树换前缀。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-086 | 期①批次：**A** 目录页分类/说明 ✅ → **B** 脚本头 ✅ → **C** 调试集合重构 ✅ → **E** 预设库 + 探测 ✅ → **F** 三操作符 ✅ → **G** runs 落盘/导出 ✅ → **D** 面板页 `web-crawl` ✅（**期①全部已实施**） | ✅ **已验收 2026-10-20**（build 通过） |
| NT-087 | 期②：试下载（单条预览）+ 批量下载（`downloads\`）+ `next` 分页 + 链式模式 + 并发/间隔 + 失败策略 + 可取消 | ✅ **已验收 2026-10-20**（build 通过） |
| — | `flows\` 是否进备份（当前 ⛔ 不在 `RulesBackupSection` 里） | 📌 待拍板 |
