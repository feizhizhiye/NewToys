# 模块 · 系统工具（System Tools）

> **程序集**：`src/Module.SystemTools`（`NewToys.SystemTools`，产物 `Module.SystemTools.dll`）
> **模块键**：`system` ｜ **一级菜单**：Order **25** · 图标 🔴 **待新增 StreamGeometry**（现有图标里没有合适复用项）
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：📝 **规划中**（v3.86 立项 · 2026-10-22；**由 NT-091 实施**，依赖 NT-088）
> **最后校对**：2026-10-22（立项骨架）

---

## 1. 职责与边界

- **定位**：本机**快捷设置入口与快捷命令**。
- **归属判据**（需求 §2.3.3，机械可执行）：执行完**只产生副作用、不产出数据** ⇒ 本模块；产出**可解析数据**（能进集合 / 信息面板 / 命令链）⇒ `script` 脚本中心。
- ⛔ 不做：采集器 / 间隔刷新 / 输出解析器 / 集合 / 命令链编排（属 `script`）。
- ⛔ 不引 Shell；⛔ 不复制一份 Runner —— 执行一律走 Core 的 `IScriptRunner`（NT-088 上提后可用）。
- 一级菜单显示名「系统工具」；页 Key 沿用从 script 迁来的 `script-commands` / `script-favorites`（**⛔ 不改 Key**）。

## 2. 当前功能（规划 3 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View / VM | 说明 |
|---|---|---|---|---|
| `script-commands` | 命令库 | — | `CommandLibraryView` / `CommandLibraryViewModel`（由 `Module.ScriptManager` 平移） | Windows 设置面板 / 管理工具入口全集，点一下即打开 |
| `script-favorites` | 收藏夹 | — | `FavoritesView` / `FavoritesViewModel`（由 `Module.ScriptManager` 平移） | 按 `source` 分组显示全部来源收藏，点一下执行 |
| `system-quick` | 快捷命令 | — | 待 NT-091 | 电源计划 / 主题明暗 / 休眠 / 网卡启禁 / 磁盘清理 / UAC / 开发者模式 / 远程桌面 / 代理开关 / 时间区域 |
| `settings-system` | 系统工具设置 | 100 | 待 NT-091 | 本模块唯一写入点，落 `config\system\*` |

## 2.5 操作逻辑（交互契约）

> ⏳ **待 NT-091 实施时补齐**（每页一张表：操作 → 触发 → 结果 / 状态变化 → 约束），格式见 `script-manager.md` §2.5。

已确定的骨架约束：

- 收藏夹的数据本体在 Core `FavoritesService`（**⛔ 不搬数据**）；各页右键「收藏」仍是唯一写入点，本模块只做聚合展示与执行。
- 迁移必须**一次性 + 可逆**：旧条目打 `migrated=true` 保留（⛔ 不删），本模块导入副本。

## 3. 实现逻辑

- 依赖：Core `IScriptRunner`（NT-088）、`IAppConfigService`、`IOptionsStore`（`JsonOptionsStore` → `config\system\*`）。
- ⛔ 不依赖 `Module.ScriptManager`、`Module.WebTools`（**模块禁互引**，framework §3.1）。
- 页面构建：按 `ui.md` §6 列表页标准（工具条 / 列表 / 详情卡 / 底栏 `ListFooterBar`）。

## 4. 关键文件

⏳ 待 NT-091 建档后回填（`src/Module.SystemTools/{Views,ViewModels,Services,Models}/`、`SystemToolsModule.cs`）。

## 5. 坑点（🔴 已知）

1. **`QuickActionListViewModel` 双写**：脚本库与命令库分家后各持一份（VM 不能跨模块共享，也放不进 `NewToys.UI` —— 它依赖 Core 服务）。后续任一处改动必须同步两处。（用户在 v3.86 立项时已知并接受）
2. **执行需要管理员的操作**（启禁网卡 / `netsh` / `rasdial`）：应用**无提权机制** ⇒ 一律降级为「生成命令 + 📋 复制」供用户自跑，⛔ 不假装调用成功。
3. **页 Key 前缀误导**：本模块前两页的 Key 仍是 `script-*`（刻意保留以保证偏好不丢）；文档 / 代码注释里不要把它当成「属于 script 模块」的证据。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-088 | 执行器上提 Core（前置） | 🔄 已实施（⏳ 待 build 验收；framework/） |
| NT-091 | 本模块建档与三页落地 | 📝 草稿（待细化） |
