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
| `web-scrape` | 网页抓取 | 4 | `ScrapeView` / `ScrapeViewModel` | 多配置批量抓取并整合 |
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

### 网页抓取 `web-scrape`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建任务 | `➕ 新建任务` | 加一条抓取配置 | — |
| 运行全部 | `▶ 运行全部` | 批量抓取并按配置整合 | 忙碌禁用；可取消；⛔ 阻塞 UI 线程 |
| 重读任务 | `🔄 重读任务` | 重读本地任务配置 | **⛔ 不开始抓取** |

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
| `ScrapeService` | 多配置批量抓取 + 结果整合（`web-scrape`） |
| `HostsService` | hosts 方案读写 / 系统 hosts 落盘（需管理员，`web-hosts`） |
| `WebTransferService` | 导入 / 导出（书签 + hosts 方案） |
| `WebToolsSettingsService` | `config\web-tools.json` |

- 数据与配置：`config\web-tools.json`（设置）；书签与 hosts 方案落模块数据目录（见 `WebToolsSettingsService` 与各 Service 的路径来源）。⛔ 模块内不自己拼 `ConfigStoreService` 的根路径。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `WebToolsModule.cs` |
| 工具条逃生舱样板 | `Views/WebHttpView.axaml` |
| 系统写入点 | `Services/HostsService.cs` |

## 5. 坑点（🔴 已知）

1. 🔴 `web-http` 是**工具条统一改造的已知例外**（页内自绘工具条），⛔ 不要为了统一去改它的表单式撑满布局。
2. hosts 写入系统文件需管理员权限 ⇒ 失败必须给可读提示，⛔ 静默失败。
3. 抓取 / Ping 属长任务 ⇒ 必须可取消（`CancellationToken`），⛔ 阻塞 UI 线程。
4. 导入外部书签 / hosts 文件时路径须 `Path.GetFullPath` 校验（见 `../framework.md` §7 路径穿越防御）。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 本模块暂无未结项；§5 坑点待与代码逐页精校（下次改本模块时顺带补全） | 📌 待精校 |
