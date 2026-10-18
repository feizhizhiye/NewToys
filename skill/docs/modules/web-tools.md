# 模块 · 网络工具（Web Tools）

> **程序集**：`src/Module.WebTools`（`NewToys.WebTools`）
> **模块键**：`web` ｜ **一级菜单**：Order 30 · 图标 `IconWeb`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 稳定（5 页 + 1 设置页）
> **最后校对**：2026-10-18（对照 `WebToolsModule.cs` / 文件清单）⚠️ 本节 §5 坑点待按模块逐页精校（见 §6）

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
