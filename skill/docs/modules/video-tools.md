# 模块 · 视频工具（Video Tools）

> **程序集**：`src/Module.VideoTools`（`NewToys.VideoTools`）
> **模块键**：`video` ｜ **一级菜单**：Order 40 · 图标 `IconVideo`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 稳定（3 页 + 1 设置页）
> **最后校对**：2026-10-18（对照 `VideoToolsModule.cs` / 文件清单 / 原工程规范 §3.9）

---

## 1. 职责与边界

- **定位**：本地视频查看器 + 单个剪辑 / 转码任务制定 + 批量任务队列。**全部处理走后端 ffmpeg 命令行**，无内置解码器。
- 视频库 / 标签 / 收藏页已在 NT-027 整体删除（收窄为「简易查看器 + 送批量任务」）。

## 2. 当前功能（3 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View/VM | 说明 |
|---|---|---|---|---|
| `video-list` | 视频列表 | 0 | `VideoListView` / `VideoListViewModel` | 本地视频查看器：分辨率 / 编码 / 码率 / 时长，可多选发送到批处理 |
| `video-cut` | 视频预览 | 1 | `VideoPreviewView` / `VideoPreviewViewModel` | 边预览边制定单个剪辑 / 转码任务，生成 ffmpeg 命令后发送批处理 |
| `video-batch` | 批量处理 | 2 | `VideoBatchView` / `VideoBatchViewModel` | 剪辑 + 转码混排任务队列：逐条改参数 / 编辑命令 / 批量运行 |
| `settings-video-tools` | 视频工具设置 | 100 | `SettingsVideoToolsView` | 播放器 / 默认预设 |

- 🔴 Key 仍是 `video-cut`（改 Key 会丢 `columnlayout.json` 用户偏好）；**类名 / 文件名已是 `VideoPreview*`**。

## 3. 实现逻辑

### 3.1 服务清单（`Services/`）

| 文件 | 职责 |
|---|---|
| `FfmpegService` | 命令拼装 + 执行 + `ExtractFrameAsync`（抽帧）/ 探测时长 |
| `VideoScanner` | 目录扫描入库 |
| `VideoMediaIndex` | 本地索引（`data\` 侧，轻量） |
| `VideoTaskStore` | 跨页投递（`TakePendingCut` / `TakePendingTranscode`，**取一次即清空**） |
| `VideoFileService` | 文件选择 / 打开 |
| `VideoToolsSettingsService` | `config\video-tools.json` |

### 3.2 视频预览页（`video-cut`）

- **布局（迁移后）**：页根主体 Grid **单列**（预览列）；详情卡 = Shell 右详情列（340px，`DetailCardHost` 渲染，可开合）。
- 顶部工具条（页面级动作都归这里）：`📂 选择视频` / `➡ 发送到批处理` / `↺ 区间重置` / `❮❯ 详情`（末位绑 `ToggleDetailPanelCommand`，**会通知 Shell**）；卡内动作（如 `📂 选择输出文件…`）走描述符 `Actions` 区。
- VM **不继承 `ListPageViewModelBase`**（不是列表页）→ `ObservableObject, IStatusProvider`；已实现 `IDetailPanelProvider` + `IDetailCardSource`（`HasDetailPanel => true`、`DetailContent => null`）。

**三条时间条（预览位置 / 开始 / 结束）**
- ⛔ **滑条别直接绑模型属性** —— 一律走 VM 手动属性（`PreviewTime` / `StartSeconds` / `EndSeconds`），setter 里改一处 → 时间码文本 + 大画面 + 命令预览一起刷新（避免"拖了滑条、时间码没跟上 → 保存被旧值覆盖"）。
- `Maximum` 绑 `TimelineMax`（时长未知时给**占位 1**）：⛔ 别在 `Maximum==Minimum==0` 时直绑 `Duration` → `(Value-Min)/(Max-Min)=0/0=NaN` → **整页布局算不过去**。
- 大画面 `Image Source="{Binding PreviewImage}"`（`Bitmap`）；拖滑条用**"最新值优先"节流**（`_extracting` + `_pendingPreview`）。⛔ **不铺 `ItemsControl` 缩略图墙**，只保留"当前帧"一张大图。

### 3.3 两页详情卡 = 同一套结构（`video-cut` / `video-batch`）

分区**严格按 ffmpeg 命令的书写顺序**排，⛔ 不另起"任务类型"块：

| 顺序 | 分区 | 内容 | 勾选框语义 |
|---|---|---|---|
| 1 | 「✂ 剪辑」 | 开始 / 结束、剪切预设、重新编码 CRF、自定义参数 | `IsCut`：勾上 → 命令带 `-ss/-to` |
| 2 | 「🔁 转码」 | 预设、编码器(CPU·GPU)、容器、分辨率宽高、码率(CRF/目标) | `IsTranscode`：勾上 → 命令带编码参数 |
| 3 | 输出 | 输出文件（留空自动命名，绝不覆盖已有文件） | — |
| 4 | 命令 | 完整命令行（`SelectableTextBlock`）+ 可编辑参数框 + `↺ 重置为自动` + `📋 复制命令` | — |

- **勾选框放在 `Expander` 的 `Header` 里**（`StackPanel Horizontal` 装 `CheckBox` + 文字）；取消勾选 → 自动折叠（`OnIsCutChanged` / `OnIsTranscodeChanged` 改 `IsXxxSectionExpanded`）。折叠只影响界面，**是否执行由勾选决定**。
- **两页 VM 属性刻意对齐**（同名同义）：`EditStart/EditEnd`、`EditExtraArgs`、`EditOutput`、`EditArgs`、`IsArgsEdited`、`CommandPreview`、`CopyCommandLineCommand`、`ResetCommandCommand`。
  - 批量页的转码属性名用 `EditPresetId/EditCrf/EditMaxHeight`（它是列表页、属性名已定型），⛔ 不要为对齐改名（改名要全仓搜旧名）。
- **命令可编辑**：`CommandPreview`（只读合成）与 `EditArgs`（可写）分离 + `IsArgsEdited` 标记。手改后参数变化**不得覆盖** `EditArgs`；`↺ 重置为自动` = 清标记并重新赋自动值。⛔ 手改后的命令必须**原样执行**（存 `VideoTaskBase.RawArgs`，批量运行时优先用它）。
  - ⛔ 别直接给 `EditArgs` 赋自动串再靠比较反推"是否手改"（赋值即相等 → 永远判不出）；`IsArgsEdited` 由「用户输入 vs 当前自动串」比较得出（`OnEditArgsChanged`）。
- **批量页命令区**：选中任务变了要重算 `CommandPreview` / `EditArgs`；`RawArgs` 非空时 `CommandPreview` 直接显示 `RawArgs` 拼出的那条。
- **两页差异**只在两点：区间来源（预览页 = 时间条，批量页 = 选中任务）与类型决定方式（预览页 = 两个勾选框可同时勾 → 剪辑+转码一条命令；批量页 = 任务类型决定）。分区顺序、折叠、命令区形态**必须一致**。

### 3.4 跨页投递

- 从「视频列表」送来 → 载入后**自动触发一次预览**（探测时长 + 抽当前帧）。
- 取件必须在 `OnAttachedToVisualTree`（不是 `OnDataContextChanged`），见 `../ui.md` §6.2 第 8 条。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `VideoToolsModule.cs` |
| 预览页 | `Views/VideoPreviewView.axaml(+.cs)` + `ViewModels/VideoPreviewViewModel.cs` |
| 批量页 | `Views/VideoBatchView.axaml(+.cs)` + `ViewModels/VideoBatchViewModel.cs` + `Models/VideoTask.cs` |
| ffmpeg | `Services/FfmpegService.cs` |

## 5. 坑点（🔴 实测过）

1. 🔴 **覆写可视树回调要写 `protected override`**（`OnAttachedToVisualTree` / `OnDetachedFromVisualTree` 是 protected，写 `public override` → `CS0507`）。
2. 🔴 滑条 `Maximum==Minimum` → `NaN` 布局崩（见 §3.2）。
3. 🔴 输出路径**绝不覆盖已有文件**（自动命名要避让）。
4. 拖滑条抽帧必须节流（最新值优先），否则卡住 UI 线程。
5. 批量页属性名与预览页**不完全同名是刻意的**（见 §3.3），⛔ 别"统一命名"。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 本模块暂无未结项 | — |
