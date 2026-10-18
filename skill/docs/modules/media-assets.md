# 模块 · 媒体资产（Media Assets）

> **程序集**：`src/Module.MediaAssets`（`NewToys.MediaAssets`）
> **模块键**：`assets` ｜ **一级菜单**：Order 50 · 图标 `IconMediaAssets`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 稳定（7 页 + 1 设置页）
> **最后校对**：2026-10-18（对照 `MediaAssetsModule.cs` / 文件清单 / NT-063 P2 执行记录）

---

## 1. 职责与边界

- **定位**：本地媒体资产的索引 + 整理（标签 / 分类 / 集合）+ 物理操作（移动 / 复制 / 软删 / 永久删）+ 任务队列。
- 🔴 **`FileOperationService` = 唯一动磁盘处**；其它服务只改索引。
- 物理删除唯一入口 = 列表右键「⛔ 从磁盘删除」（二次确认 + 任务队列 + 删索引）；详情卡 IconBar 的 🗑 只做**软删除**（移入回收站）。

## 2. 当前功能（7 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View | 说明 |
|---|---|---|---|---|
| `assets-all` | 全部资产 | 0 | `AssetAllView` | **唯一资产浏览页**：文件名 / 类型 / 标签 / 分类 / 集合 / 大小 / 修改时间 / 存储源；筛选栏内**类型切换**驱动列集合 |
| `assets-tags` | 标签管理 | 6 | `AssetTagsView` | 名称 / 命名空间 / 颜色 / 使用数；合并与清理未使用 |
| `assets-categories` | 分类管理 | 7 | `AssetCategoriesView` | 分类树 + 成员列表（本模块唯一非列表页） |
| `assets-collections` | 集合管理 | 8 | `AssetCollectionsView` | 手动 / 智能集合；成员排序 |
| `assets-storages` | 存储源 | 9 | `AssetStoragesView` | local / removable / nas；在线状态；移除只移索引不删磁盘文件 |
| `assets-tasks` | 任务中心 | 10 | `AssetTasksView` | 扫描 / 重命名 / 移动 / 复制 / 删除 / 元数据 / 缩略图：取消 / 重试 / 日志 |
| `assets-trash` | 回收站 | 11 | `AssetTrashView` | 软删资产：恢复 / 永久删除（二次确认）/ 清空 |
| `settings-media-assets` | 媒体资产设置 | 99 | `SettingsMediaAssetsView` | 元数据 / 缩略图 / 扫描偏好 |

- 🔴 **5 个类型页已删除**（`assets-video` / `-music` / `-book` / `-image` / `-document`，NT-063 P2-a，2026-10-16）：类型切换改由 `assets-all` **筛选栏的既有类型下拉**驱动列集合 / 详情卡视频字段 / 状态栏前缀。
  - 类型切换是**即时设定，⛔ 不落盘**；`CaptureUserColumnPrefs` 保住用户列偏好。
  - `AssetAllViewModel` 的 `SetPresetType` / `PageKey` / `StatusPrefix` 三个虚成员已收窄，不再有派生子类。

## 3. 实现逻辑

### 3.1 服务分层（`Services/`）

| 文件 | 职责 |
|---|---|
| `AssetDatabase` | `data\newtoys.db` 13 表 + 索引（⚠️ B14 债：标签/分类/集合/关联与需求 §3.5 的定位冲突） |
| `AssetService` | 资产查询（`AssetQuery` / `CountAssets` / `QueryPage`，**WHERE 只写一份**）—— 分页样板 |
| `ScanService` | 扫描入库（识别键 = 大小 + 修改时间 + 路径） |
| `FileOperationService` | **唯一磁盘写入点**：日志先行 + 防穿越 + 冲突策略 + 回收站 |
| `TaskQueue` | 任务队列（扫描 / 重命名 / 移动 / 复制 / 删除 / 元数据 / 缩略图） |
| `CategoryService` / `TagService` / `CollectionService` / `RelationService` | 组织维度 |
| `StorageService` | 存储源在线 / 离线 + 外部变更校准 |
| `AssetMergeService` | 多库融合（`ATTACH DATABASE`；工作副本模型 = 打开库 A → 打开库 B → 💾 保存） |
| `MetadataService` / `ThumbnailService` / `MediaTools` | 元数据读取 / 缩略图 / ffmpeg 探测 |
| `AssetImportExport` / `UserDataExport` | 列表 JSON 导入导出 / 用户数据导出（B14 缓解） |
| `AssetNavigationStore` | 跨页导航状态（详情 → 关联页） |
| `OperationLogService` | `operation_logs` |

### 3.2 数据与配置

| 位置 | 内容 | 备份 |
|---|---|---|
| `data\newtoys.db` | 资产索引 13 表 + 组织维度 + `operation_logs` | ⛔（B14 待裁决） |
| `config\media-assets.json` | 设置（元数据 / 缩略图 / 扫描） | ✅ |

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `MediaAssetsModule.cs` |
| 样板列表页（列宽 / 工具条 / 筛选 / 类型切换 / 骨架） | `Views/AssetAllView.axaml(+.cs)` + `ViewModels/AssetAllViewModel.cs` |
| 磁盘操作 | `Services/FileOperationService.cs` + `Models/OperationLog.cs` |
| 详情卡样板顺序 | 文件名（只读）→ 固定只读项 → 标签(分组)/分类/集合 chips → **IconBar** → 评分 chips → 描述 → 关联/提示 → 底部改名框 |

## 5. 坑点（🔴 实测过）

1. 🔴 **扫描「大小 + 修改时间」当识别键会碰撞**（误判为复制 / 改名）⇒ 必须叠加路径，见 NT-032。
2. 🔴 **多库融合重复 / 重叠并入会产生孤儿资产**（NT-033 已修）⇒ 合并后按主键去重 + 关系重挂。
3. 集合排序当前用 **⬆⬇ 按钮**（⛔ 不是拖拽）；**智能集合规则未实装**（NT-018 偏差，仍开放）。
4. 回收站曾缺「删除时间」列（NT-029 已修）；移动 / 复制无 UI 入口（NT-030 已补）。
5. 收藏 / 评分 **即时落盘**（⛔ 不走「保存」）；「移动到 / 复制到（目标存储源）」整块已删（低频重操作不进卡）。
6. 资产详情卡 chips 的 `OptionsPath` 必须是常驻集合实例，否则 ComboBox 回写会抹掉页面值（见 `../ui.md` §7.2）。
7. `AssetTasksView` **不是标准列表页**（三块堆叠 + `ScrollViewer`）⇒ ⛔ 不套 `ListPageSkeleton`。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| B14 | 用户手动数据（标签/分类/集合/关联）落 `data\` 与需求 §3.5「手动数据进备份」冲突 | ⏳ 待用户拍板（技术债） |
| NT-018 遗留 | 智能集合规则未实装 | 📝 未立项（沿用卡内偏差记录） |
