# NewToys 文档地图（Index）

> v1.0 · 2026-10-18 · **文档体系唯一入口**（2026-10-18 按「框架层 / UI / 每模块一份」重构）
> 会话开始读本文件 + `NewToys-项目状态与路线图.md`「一、当前基线」，再按任务读对应文档。
> ⛔ 不整读文档：一律按本表定位后**定向读**。

## 一、文档清单

| 文档 | 角色 | 内容 | 何时读 |
|---|---|---|---|
| **`index.md`**（本文件） | 文档地图 | 文档索引 + 模块清单表 | 每次会话开始 |
| `NewToys-需求规格.md` | 需求规格（宪法） | 定位 / 边界 / 技术框架 / UI 框架 / 导航模型 / 功能域 | 涉及功能与边界 |
| **`framework.md`** | 工程规范 · 框架层 | 程序集边界 / 启动 / 菜单 / 加载 / UI 契约 / 配置 / 构建 / 技术债 / **跨模块待办** | 改 Shared / Core / Shell / UI，或新建模块 |
| **`ui.md`** | 工程规范 · UI | 尺寸密度 / 主题键 / 图标 / CSD / 列表页 / 详情卡 / 工具条 / 样式坑 | 改任何 `.axaml` |
| **`modules/<模块>.md`** | 模块说明 | 该模块的职责 / 当前功能 / 实现逻辑 / 数据 / 坑点 / 待办 | **改哪个模块读哪个** |
| `NewToys-项目状态与路线图.md` | 工作项台账 | 当前基线 / 已完成 / Backlog 索引 / 关键文件索引 | 每次会话开始（读「一、当前基线」） |
| `backlog/<模块>/NT-xxx.md` | 任务卡 | 实施期工作稿（≤5 张滑动窗口） | 只有实施该卡才读 |
| `NewToys-AI协作手册.md` | 协作手册 | 研发流程 + 术语对照（🔴 唯一定义处） | 流程 / 术语有疑问 |
| `NewToys-软件说明书.md` | 用户手册 | 用户视角功能说明（不含实现细节） | 写用户文档时 |

> 原 `NewToys-工程规范.md`（v3.47，892 行）已于 2026-10-18 拆入 `framework.md` + `ui.md` + 各模块 md，**原文件已删除**。

## 二、模块清单

| 模块 | 程序集 | 模块键 | 菜单 Key 前缀 | 文档 | 状态 |
|---|---|---|---|---|---|
| 脚本管理器 | `Module.ScriptManager` | `script` | `script-*` | `modules/script-manager.md` | 🔨 重构中（NT-068） |
| 网络工具 | `Module.WebTools` | `web` | `web-*` | `modules/web-tools.md` | ✅ 稳定 |
| 视频工具 | `Module.VideoTools` | `video` | `video-*` | `modules/video-tools.md` | ✅ 稳定 |
| 媒体资产 | `Module.MediaAssets` | `assets` | `assets-*` | `modules/media-assets.md` | ✅ 稳定 |
| 应用级设置 | `Shell`（非 Module） | —（宿主 `system`） | `settings-*` | `modules/settings-app.md` | ✅ 已实施（NT-062） |
| 首页 | `Shell` | — | `home` | `modules/settings-app.md` §2.3 | ✅ 稳定 |

**新增模块** = 建程序集（`framework.md` §3.3 七步）+ **新建 `modules/<模块>.md`**（照 6 节模板）+ 建 `backlog/<模块>/`。

### 模块 md 模板（6 节，固定）

```
> 程序集 / 模块键 / 一级菜单 Order 与图标 / 状态 / 最后校对 commit
1 职责与边界
2 当前功能（页面表：菜单 Key / 标题 / Order / View / 说明）
3 实现逻辑（服务清单 → 文件 / 数据流 / 关键机制）
4 数据与配置（config / data 落点与写入点）
5 坑点（🔴 实测过的，带症状）
6 待办（≤5 条；跨模块 → framework.md §11）
```

## 三、按任务选读

| 任务 | 读什么 |
|---|---|
| 改某模块的一个页面 | `modules/<模块>.md` → `ui.md`（列表页 / 详情卡 / 工具条对应节） |
| 新增页面 / 新增模块 | `framework.md` §3.3 §5 → `modules/<模块>.md` → `ui.md` §6 §7 |
| 改视觉 / 尺寸 / 配色 / 图标 | `ui.md` §2 §3 §5 |
| 改窗口 / 标题栏 / 标签栏 / 左树 | `framework.md` §4.3 → `ui.md` §2 §4 |
| 改契约（Shared 接口） | `framework.md` §6（⚠️ 框架层冻结，需用户授权） |
| 改配置 / 路径 / 多配置 | `framework.md` §7 |
| 编译报错 | `framework.md` 附录 A → `modules/<模块>.md` §5 |
| 交付 / 验收 | `framework.md` §9；`pwsh skill/scripts/accept.ps1` |

## 四、任务卡目录规则

- 一个模块一个目录：`backlog/script-manager/` · `media-assets/` · `video-tools/` · `web-tools/` · `settings-app/`；跨模块放 `backlog/framework/`。
- **每目录最多 5 张卡**；超了删**最旧且已验收**的那张；`📝草稿 / ✅就绪 / 🔄执行中 / ✔已完成（待验收）`不参与淘汰。
- **验收即蒸馏**：卡验收后把结论 / 偏差 / 坑写进 `modules/<模块>.md`，卡才可被淘汰。
- 格式与状态机见 `backlog/README.md`；流程与术语见 `NewToys-AI协作手册.md`。
