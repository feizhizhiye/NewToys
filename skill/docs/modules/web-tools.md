# 模块 · 网络工具（Web Tools）

> **程序集**：`src/Module.WebTools`（`NewToys.WebTools`）
> **模块键**：`web` ｜ **一级菜单**：Order 30 · 图标 `IconWeb`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 稳定（5 页 + 1 设置页）
> **最后校对**：2026-10-18（对照 `WebToolsModule.cs` / 文件清单；§2.5 对照 6 页 VM 的 `Toolbar` 与 `Views` 右键菜单）⚠️ 本节 §5 坑点待按模块逐页精校（见 §6）

---

## 1. 职责与边界

- **定位**：一次性网络诊断与配置工具（HTTP 测试 / 书签 / Ping / 网页抓取 / hosts），不做常驻代理、不做抓包。
- 系统级写入只发生在 hosts 管理（需管理员权限）；其余均为只读探测或模块内数据。

## 2. 当前功能（5 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View/VM | 说明 |
|---|---|---|---|---|
| `web-http` | HTTP 测试 | 1 | `WebHttpView` / `WebHttpViewModel` | 构造请求并查看响应。⚠️ **工具条逃生舱**：页内自绘 `Border.toolbar`（表单式撑满布局），不套统一工具条 |
| `web-bookmark` | 书签管理 | 2 | `BookmarkView` / `BookmarkViewModel` | 收藏与整理网址、导入浏览器书签 |
| `web-ping` | 网络 Ping | 3 | `PingView` / `PingViewModel` | 测试主机连通性与延迟，支持切 DNS |
| `web-scrape` | **抓取规则**（NT-084 改名，Key ⛔ 不改） | 4 | `RuleCatalogView` / `RuleCatalogViewModel` | **规则目录**：规则集（`.py`）的增删改 / 启用 / 导入书源 / 重读；`✏ 编辑规则` → 跳编辑器 |
| `web-rule-editor` | 规则编辑器 | 5 | `RuleEditorView` / `RuleEditorViewModel` | 三段式自绘：命令列表 / HTML 源码 / 规则编辑（解析全在 Python 侧） |
| `web-hosts` | Hosts 管理 | 5 | `HostsView` / `HostsViewModel` | 切换 hosts 方案、网络导入、编辑系统 hosts |
| `settings-web-tools` | 网络工具设置 | 100 | `SettingsWebToolsView` | 超时 / 并发 / 默认解析器 |

- 页 Key 一经发布 **⛔ 不改**。

## 2.5 操作逻辑（交互契约）

> 跨模块共用语义见 `../ui.md` §10。本模块多数页是「配置 + 一键执行」型，无状态机；**长任务必须可取消**。

### HTTP 测试 `web-http`（工具条逃生舱）

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 发送请求 | 页内表单 + `▶ 发送` | 发请求并在下方显示响应 | 请求中禁用；**⛔ 不套统一工具条**（表单式撑满布局是刻意例外） |
| 清空 | `🗑 清空` | 清表单与响应区 | — |

### 书签管理 `web-bookmark`

工具条：`📥 导入书签` / `➕ 新建` / `🔗 检测全部` / `🚀 批量打开` / `🔄 重读书签`；行右键：`🔗 联通检测` / `📌 加入·移出批量打开` / `🌐 打开` / `🕷 发送到网页抓取` / `✏ 编辑` / `🗑 删除`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 导入书签 | `📥 导入书签` | 选浏览器导出的 HTML → 批量入库 | 路径须 `Path.GetFullPath` 校验 |
| 检测全部 | `🔗 检测全部` | 并发测连通，回填状态列 | 忙碌禁用；可取消 |
| 打标批量打开 | 行右键「📌 加入 / 移出批量打开」 | 切换该条标记 | — |
| 批量打开 | `🚀 批量打开` | 逐个用系统浏览器打开已打标项 | — |
| 送抓取 | 行右键「🕷 发送到网页抓取」 | 带该网址跳 `web-scrape` | — |
| 重读书签 | `🔄 重读书签` | 重读本地书签配置 | **⛔ 不联网、不导浏览器** |

### 网络 Ping `web-ping`

工具条：`📥 导入文件` / `➕ 新建` / `📡 批量 Ping` / `🔄 重读目标` / **DNS 下拉**(200)；行右键：`📡 Ping` / `🔖 发送到书签` / `🕷 发送到网页抓取` / `✏ 编辑` / `🗑 删除`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 导入目标 | `📥 导入文件` | 一行一个主机的文本 → 批量加入 | 空态按钮也指向它 |
| 批量 Ping | `📡 批量 Ping` | 并发执行，回填 IP / 状态 / 时延 | 忙碌禁用；可取消 |
| 切 DNS | 工具条 **DNS 下拉** | 批量 Ping 与**未单独设 DNS** 的目标按此解析 | 单条可在详情卡 `DnsOverride` 覆盖；优先级 = 条目 > 页面 |
| 重读目标 | `🔄 重读目标` | 重读本地目标清单 | **⛔ 不发起 Ping** |
| 送书签 / 送抓取 | 行右键 | 带主机跳对应页 | — |

### 抓取规则 `web-scrape`（规则目录，NT-084 批 A/B/D）

> 规则载体 = `<DataDir>\web-tools\rules\<名>.py`（`SOURCE` 注释头 + `RULES` 列表 + 可选四段函数）。
> 🔴 **C# ⛔ 不解析规则**：命中数 / 预览 / 书源转换全部由 Python 算（`RuleRunService` → `IPythonRuntime`）。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建规则集 | `➕ 新建规则集` | 建一个 `.py` 骨架并进编辑器 | — |
| 启用 / 停用 | `✓ 切换启用` | 落目录清单的启用标记 | 停用只影响目录开关，⛔ 不删文件 |
| 导入书源 | `⬆ 导入书源`（批 D） | 选 Legado 书源 json → 随包 `defaults\tools\convert_legado.py` 转换 → 一条书源 = 一个规则集 `.py` | 🔴 映射 / 表达式改写全在 Python 侧（只用标准库）；转换结果按一行 JSON 回读（末尾一行） |
| 编辑规则 | 双击行 / 右键 `✏ 编辑规则` | 经 `WebTransferService` 一次性参数跳 `web-rule-editor` | 参数一次性消费（`TakeRuleSet`） |
| 重读规则 | `🔄 重读规则` | 重扫规则目录，**⛔ 不运行任何规则集、不联网** | — |
| 打开文件 / 目录 | 右键 `📄 打开文件` / `📂 打开目录` | 用系统关联 / 资源管理器打开 | — |
| 删除 | 右键 `🗑 删除` | 删规则集文件 | 二次确认 |

### 规则编辑器 `web-rule-editor`（NT-084 批 C）

三段式自绘：左**命令列表**（`Commands`）/ 中**HTML 源码**（`HtmlText`，上限 40 万字符 / 8000 行）/ 右**规则编辑**（选中命令的字段名 / 类型 / 表达式 / 预览）。

🔴 **样式统一（2026-10-20 收尾，修「与其他页面不统一」）**：工具条走 `IToolbarSource` 描述符（Shell `ToolbarHost` 统一渲染，⛔ 页内不写 `Border.toolbar`）；状态文本走 `IStatusProvider`（Shell 底部状态栏）；命令列表 = `Border.list-header` + `ColumnGrid`（表头/行同规格串 `120,80,*,80`）+ `ListBox.list-page`（行高 28 / 悬停 / 选中底色，与目录页一致）；右栏抬头 = 规则集名 + 域名（`HeaderTitle`，未选时占位）。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 抓取 HTML | 工具条 `🌐 抓取 HTML` | 按 `SOURCE` 头里的 `base`/关键字抓页面 → 落 `<DataDir>\web-tools\cache\<名>\page.html` | 编码按 `SOURCE` 的 `encoding`（GBK 需 `CodePagesEncodingProvider`，net10 自带） |
| 运行全部 | 工具条 `▶ 运行` | 对缓存 HTML 跑 `RULES`，回填命中数 + 预览 | 只跑缓存，⛔ 不重复发请求；Python 未就绪 ⇒ 状态栏提示，**页面照常可用**（⛔ 不阻断） |
| 搜索命令 | 工具条搜索框（宽 88） | 按字段名 / 表达式过滤命令列表（大小写不敏感） | 主表 `_allCommands` 与视图 `Commands` 分离：**保存永远写主表**（⛔ 不把过滤藏起来的命令存丢）；`➕ 加命令` 会清关键词保证新行可见 |
| 测试选中 | 右栏 `▶ 只测这条` | 只跑这一条 | 同上 |
| 增 / 删命令 | 右栏 `➕ 加命令` / `🗑 删命令` | 改 `RULES` 列表 | — |
| 保存 / 重读 | 工具条 `💾 保存` / `🔄 重读` | 写回 `.py` / 重读磁盘 | ⛔ 无自动保存 |
| 在源码里查找 | 面板顶部查找框（回车 = 下一个）/`◀` `▶` | 命中 = **选区高亮**（系统色），显示「第 n/m 个」，到头回绕 | ⛔ 不自绘高亮层：只读 `TextBox` 无着色 API；设选区必须**先缩 End 再设 Start 再设 End**（否则 Avalonia clamp 导致跳位错） |
| 定位命中 | `🎯 定位命中` | 把选中命令的首条结果搬进查找框并跳过去 | 🔴 语法级高亮做不到（parsel 无源码偏移）⇒ 用文本查找定位替代；结果经过净化改写时可能找不到，会如实提示 |
| 行号 | 面板右上「行 N / 共 M 行」 | 随光标 / 查找跳位更新 | ⛔ 不做整列行号：8000 行自绘 + 与 TextBox 滚动同步，成本高风险大 |
| 返回目录 | 工具条 `◀ 返回目录` | 跳 `web-scrape` | — |

### Hosts 管理 `web-hosts`

工具条：`🌐 导入` / `➕ 新建方案` / `🔄 重读方案`；行右键：`✅ 应用到系统 hosts` / `📤 导出受管块` / `🗑 删除方案`。

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 应用到系统 hosts | 行右键 `✅ 应用到系统 hosts` | 把该方案写进系统 hosts 的**受管块** | 🔴 需管理员：失败必须给可读提示，**⛔ 静默失败** |
| 导出受管块 | 行右键 | 导出为文件 | — |
| 删除方案 | 行右键 | 删模块内方案 | ⛔ 不动系统 hosts |
| 重读方案 | `🔄 重读方案` | 重读本地方案清单 | **⛔ 不动系统 hosts、不导入** |

### 网络工具设置 `settings-web-tools`

> 🔴 NT-071：本页改用**共用设置页骨架**（`NewToys.UI/Controls/SettingsSectionHost`，`Sticky="False"`）—— 2 个节 = 折叠卡片
> （网页抓取 / 设置文件），点抬头开收，默认全展开。细则见 `ui.md` §11。

| 类型 | 写入时机 |
|---|---|
| 超时 / 并发 / 默认 UA / 解析器 | **失焦即落盘**（`OnXxxChanged` → `config\web-tools.json`），**⛔ 无保存按钮** |

## 3. 实现逻辑

| 文件 | 职责 |
|---|---|
| `HttpTestService` | HTTP 请求构造 / 发送 / 响应解析（`web-http`） |
| `BookmarkService` | 书签 CRUD + 浏览器书签导入（`web-bookmark`） |
| `PingService` | Ping 执行与延迟统计（`web-ping`） |
| `DnsResolver` | DNS 解析（工具条切 DNS 的来源） |
| `RuleCatalogService` | 规则集目录清单（`web-scrape`）：扫 `rules\*.py` 读 `SOURCE` 头（NT-084 取代 `ScrapeService`） |
| `RuleFileService` | 规则 `.py` 的读写 / 命名清洗 / 目录（`web-scrape` / `web-rule-editor` 共用） |
| `RuleRunService` | 抓 HTML 落缓存 + **调 Python** 跑 `RULES` / 列命令 / 转书源（NT-084；⛔ C# 不做解析） |
| `HostsService` | hosts 方案读写 / 系统 hosts 落盘（需管理员，`web-hosts`） |
| `WebTransferService` | 导入 / 导出（书签 + hosts 方案） |
| `WebToolsSettingsService` | `config\web-tools.json` |

- 数据与配置：`config\web-tools.json`（设置）；书签与 hosts 方案落模块数据目录（见 `WebToolsSettingsService` 与各 Service 的路径来源）。⛔ 模块内不自己拼 `ConfigStoreService` 的根路径。
- NT-084 新增两块数据：**规则** = `<DataDir>\web-tools\rules\*.py`（可备份、可手写）；**抓取缓存** = `<DataDir>\web-tools\cache\<名>\page.html`（单文件覆盖，可重建）。
- 备份：`Services/RulesBackupSection.cs`（`IBackupSection`，Id `data.web-rules`，Order 91）—— 只备 `rules\`，⛔ 不备 `cache\`；🔴 **`DefaultSelected=true`（唯一默认勾的数据节）**，恢复落 `data\restore-<ts>\web-rules\`，⛔ 不覆盖现有规则。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `WebToolsModule.cs` |
| 工具条逃生舱样板 | `Views/WebHttpView.axaml` |
| 系统写入点 | `Services/HostsService.cs` |

## 5. 坑点（🔴 已知）

> **NT-076 第二批**：书签 / ping 目标 / 抓取任务 / hosts 方案四份数据已从 `config\*.json` 搬进总库 `web_docs`（文档名 = 旧文件名基名）。读写统一走 `Core/Services/JsonDocStore`；空列表 ⇒ 删文档（沿用「空即删」），⛔ 别在 `config\` 下再建同名文件。

1. 🔴 `web-http` 是**工具条统一改造的已知例外**（页内自绘工具条），⛔ 不要为了统一去改它的表单式撑满布局。
2. hosts 写入系统文件需管理员权限 ⇒ 失败必须给可读提示，⛔ 静默失败。
3. 抓取 / Ping 属长任务 ⇒ 必须可取消（`CancellationToken`），⛔ 阻塞 UI 线程。
4. 导入外部书签 / hosts 文件时路径须 `Path.GetFullPath` 校验（见 `../framework.md` §7 路径穿越防御）。
5. 🔴 **规则解析只在 Python 侧**（NT-084）：C# 只送路径 + 回读一行 JSON。改规则语言（css/xpath/regex/json）不用动 C#；反过来 ⛔ 别把解析逻辑搬进 C#。`json:` 用标准库自实现（⛔ 不引 jsonpath），路径支持 `$a.b[0]` / `[*]`，⛔ 不支持 `js:`（要 JS 引擎）。
6. 🔴 **Python 缺失只提示、⛔ 不阻断**：页面照常打开 / 编辑 / 保存，只有抓取后跑规则、导入书源这两处需要 Python（`IPythonRuntime.Probe()` 未就绪 ⇒ 状态栏给「怎么装」的指引）。
7. 随包 `.py`（`defaults\tools\convert_legado.py`）**只用标准库** —— 导入书源不依赖 venv 里的 requests/lxml/parsel，装了 Python 就能用。
8. 抓 HTML 要给 GBK / GB18030 注册 `CodePagesEncodingProvider`（net10 共享框架自带，⛔ 不引第三方包）；HTML 显示有上限（40 万字符 / 8000 行），⛔ 别整页塞进 TextBox。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 本模块暂无未结项；§5 坑点待与代码逐页精校（下次改本模块时顺带补全） | 📌 待精校 |
