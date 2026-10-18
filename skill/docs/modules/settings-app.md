# 模块 · 应用级设置（App Settings）

> **程序集**：`src/Shell`（**不是** `Module.*`；NT-062 起 `Module.Settings` 已撤销并入 Shell）
> **模块键**：无（挂**不渲染宿主分组** `system`，Order 999 · `IsHome=true`）· 图标 `IconSettings`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 已实施（NT-062）
> **最后校对**：2026-10-18（对照 `Shell/Settings/AppSettingsRegistration.cs` / Shell 文件清单）

---

## 1. 职责与边界

- **定位**：应用级（跨模块）设置的唯一 UI 入口 + 首页 + 功能清单页。
- 🔴 **不是标签页菜单项**：v3.66 起左树**无「设置」一级菜单**。入口 = 左树底部**版本行齿轮** / `Ctrl+,` / 标题栏搜索 / 首页「一键操作」的 `⚙`（跳 `settings-launch`，归一成页 Key `settings`）。
- ⛔ 模块专属设置**不在这里**：它们在各自模块的一级菜单末位（如 `settings-script`）。
- 宿主分组 `system` 只为让 `ShellViewModel.OpenTab` 能靠 `IMenuService.FindItem` 解析节锚点 Key；左树 / 首页模块计数 / 功能清单页都按 `!IsHome` 过滤 ⇒ 它不出现在任何菜单里。

## 2. 当前功能

### 2.1 应用级设置页 = 一页六节（`SettingsView`，共享一个 VM 宿主）

| 节锚点 Key | 标题 | Order | 分类 | 内容 |
|---|---|---|---|---|
| `settings-general` | 启动与退出 | 0 | `General` | 开机启动 / 托盘 / 关闭按钮行为 |
| `settings-update` | 更新 | 1 | `Update` | 版本文件地址与手动检查更新 |
| `settings-theme` | 主题外观 | 1 | `Theme` | 明暗模式与强调色 |
| `settings-path` | 路径与配置 | 2 | `Environment` | 运行环境路径 + 多套配置档（分类列区分行，可新增 / 切换 / 重命名 / 删除） |
| `settings-launch` | 一键按钮 | 3 | `Launch` | 首页三个一键按钮的全部配置（名称 / 程序 / 参数 / 网址） |
| `settings-dns` | 加密 DNS | 4 | `Network` | 加密 DNS 与网络配置 |
| `settings-data` | 数据管理 | 6 | `Data` | 备份 / 导入 `config` 配置 |

- 「关于」节**无节锚点 Key**（需求 §4.9.2 表格 = "—（页内可点）"），只从设置页内进入。
- 🔴 **所有 Key ⛔ 不改**：首页跳转 / 收藏白名单 / 托盘「打开设置」都按 Key 寻址。
- 「通用」已拆成 `settings-general` / `settings-update` / `settings-theme` **三节共用一个 VM**（用户要求"拆开、删除通用"）。

### 2.2 功能清单页（`settings-all` · `AllFunctionsView`）

- 独立页，Order 7；v3.66 起**也内嵌在首页**（收藏卡尾部「显示更多」就地展开）。
- 独立页保留给标题栏 `＋` 与「关于」节按钮（当"选择器"用，不切走当前页）。
- 承载「增加功能」操作区 = 「🔄 重载模块」入口（NT-052）。

### 2.3 首页（`home`，`HomeView`）

- **默认页**，`ShowInTabStrip=false`（⛔ 不进标签栏）、`CanClose=false`、仍在 `Tabs` 里（零标签兜底回首页）。
- 收藏夹（`config\favorites.json`）+ 一键按钮 + 功能清单内嵌段。

## 3. 实现逻辑

### 3.1 设置页结构（`SettingsView.axaml`）

- **树状手风琴滚动 + 抬头磁吸浮层**：逻辑在 `SettingsView.axaml.cs`。
- 🔴 类名 `sec-head` / `sec-body` **别删**；`StuckHeader.DataContext=null` **别删**（磁吸浮层的 DataContext 隔离）。
- 每节内含工具条；节 View **⛔ 不带内层 `ScrollViewer`**（滚动由页根统一给）。
- `settings-path` 节正文用 `ListPageSkeleton` 且 **`Margin="0"`**（作设置节正文）。

### 3.2 节 / VM 归属

| 节 | View | VM |
|---|---|---|
| 启动与退出 / 更新 / 主题外观 | `GeneralView` / `UpdateView` / `ThemeView`（三节共用 VM 宿主） | `GeneralViewModel` 等 |
| 路径与配置 | `PathView` | `PathViewModel` + `Models/PathSettingItem` |
| 一键按钮 | `LaunchConfigView` | `LaunchConfigViewModel` |
| 加密 DNS | `DnsView` | `DnsViewModel` |
| 数据管理 | `DataView` | `DataViewModel` |
| 关于 | `AboutSettingsView` | `AboutSettingsViewModel` |
| 页宿主 | `SettingsView` | `SettingsViewModel` + `Models/SettingsSection` |

- 复合页工具条：节 VM 通过 **`IToolbarOwner.ToolbarOwner`** 当承载者（见 `../framework.md` §6.4），否则工具条"画得出来、点不动"。
- Shell 自带页面同样走 `ResolveView` ⇒ 🔴 **类名必须全局唯一**（历史上 Shell 与 WebTools 各有一个 `DnsView`，撞车）。

### 3.3 数据与配置

- `config\theme.json` / `app.json` / `launch.json` / `columnlayout.json`（清单见 `../framework.md` §7.1）。
- 写入点规则：**每个设置项只有本页一个写入点**；模块只读。
- 设置模型类被两模块共用时放 `Shared`（⛔ 双真相源）。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单 / 节锚点注册（Key 权威源） | `Shell/Settings/AppSettingsRegistration.cs` |
| 设置页宿主 + 手风琴 | `Views/SettingsView.axaml(+.cs)` + `ViewModels/SettingsViewModel.cs` + `Models/SettingsSection.cs` |
| 功能清单 | `Views/AllFunctionsView.axaml` + `ViewModels/AllFunctionsViewModel.cs` |
| 首页 | `Views/HomeView.axaml` + `ViewModels/HomeViewModel.cs` |
| 设置分类枚举 | `SettingsCategory`（`Shared`），`RegisterFunction` 末尾参数 |

## 5. 坑点（🔴 实测过）

1. 🔴 **内容区 = `ContentControl` + `{Binding ActiveTab.Content}`**，⛔ 不自己 `Children.Add`、⛔ 不绑独立属性。修「不换页 / 双击」落点 = `OnActiveTabChanged`：重活包 `try/catch` 不许抛 + `finally` 补发 `OnPropertyChanged(nameof(ActiveTab))`。
2. 🔴 **节 View ⛔ 带内层 `ScrollViewer`**（会出现双滚动条 + 磁吸失效）。
3. 🔴 菜单标题**保持长名**（如「脚本管理器设置」）—— 它是独立标签页的标题，⛔ 不要简化。
4. `PrimaryButton` 实心绿只给对话框；设置页按钮一律 `ghost`（见 `../ui.md` §8.2）。
5. 外部程序路径**惰性探测**：填了"看起来像路径"的值才校验存在性；缺失 → 状态栏 `⚠` 前缀可点提示，⛔ 不崩。
6. 设置页搜索只覆盖已注册项与节锚点（二级菜单项搜索 = NT-066 未做）。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| NT-066 | 标题栏搜索扩到**二级菜单项 + 设置页节点**（现只搜一级 / 已注册项）；跨 Shell ⇒ 需用户授权 | 📝 草稿（已并入 `framework.md` §11） |
