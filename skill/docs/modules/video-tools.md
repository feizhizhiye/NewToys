# 模块 · 视频工具（Video Tools）

> **程序集**：`src/Module.VideoTools`（`NewToys.VideoTools`）
> **模块键**：`video` ｜ **一级菜单**：Order 40 · 图标 `IconVideo`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 稳定（3 页 + 1 设置页）
> **最后校对**：2026-10-24（NT-100 字幕 / NT-101 多段剪切 + 详情卡 `Pair` 成对输入；NT-105 时间轴区间标记）

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

## 2.5 操作逻辑（交互契约）

> 跨模块共用语义见 `../ui.md` §10。三页分工：**列表选片 → 预览定参数 → 批量跑**；跨页投递只经 `VideoTaskStore`（取一次即清空）。

### 视频列表 `video-list`

工具条：`📂 导入`(SplitButton: 文件夹 / 视频文件) + 目录框(回车=扫描) → `🔄 重扫目录` → `含子目录` → `🔎 读取全部媒体信息` → `➡ 批量处理` → `➡ 视频预览` → `🧹 清除列表`。

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 导入文件夹 | `📂 导入` 主钮 / 「📁 文件夹…」 | 扫描目录并**替换**当前列表 | 目录框回车等价 |
| 追加文件 | 「🎞 视频文件…」 | 多选后**追加**进当前列表 | ⛔ 不替换已有 |
| 重扫目录 | `🔄 重扫目录` | 重新枚举当前目录 | **勾选状态保留**；媒体信息优先命中 SQLite 缓存 |
| 读取媒体信息 | `🔎 读取全部媒体信息` | 并发 ffprobe → 写 `data\newtoys.db` 缓存 | 并发度 = **本模块设置页的「最大并发数」**（`SettingsVideoToolsViewModel.MaxConcurrency`；工具条 ToolTip 里写的「设置→通用」是 NT-062 拆分前的旧文案） |
| 送批处理 | `➡ 批量处理` | 给勾选视频建转码任务，**不立刻跑** | 无勾选 ⇒ 提示先勾 |
| 送预览 | `➡ 视频预览` | 送 `video-cut`，一次只编一个 | 无勾选 ⇒ 禁用 + ToolTip |
| 清除列表 | `🧹 清除列表` | 清空列表显示（含勾选 / 筛选）回到空态 | **⛔ 不删任何视频文件、不重扫目录** |
| 勾选 | 表头 `☑` 右键 | 全选 / 取消 / 反选 | — |

### 视频预览 `video-cut`（当前页 Key 仍叫 `video-cut`）

工具条：`📂 选择视频` / `▶ 播放预览`(↔ `⏸ 停止`) / **`🧪 试跑 5 秒`** / **`▶ 播放小样`** / `↻ 90°` / `↺ 90°` / `➡ 发送到批处理` / `↺ 区间重置` / `❮❯ 详情`（末位，通知 Shell 开合详情列）。

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 选择视频 | `📂 选择视频` | 载入并**自动预览**（探测时长 + 抽当前帧） | 从视频列表送来时也自动触发一次 |
| 播放预览 | `▶ 播放预览` / `⏸ 停止` | 抽帧模拟播放，再点停止 | 拖滑条时「最新值优先」节流 |
| 旋转 | `↻ 90°` / `↺ 90°` | 预览画面旋转（**纯视觉**） | 命令卡「旋转」字段同步 |
| **时间轴区间标记**（NT-105） | 「预览位置」行 = `TimelineTrack` 自绘轨道（点一下 / 按住拖动定位） | 轨道上高亮块 = 当前剪辑区间：单段整块、**多段 ≥2 段逐段色块**；未勾选「剪辑」时 35% 淡显；刻度尺按宽度自动选档 | 区间数据 = `SegmentList`（与 ffmpeg 命令**同源**）；颜色全走主题键 |
| 拖时间条 | 时间轴轨道（定位）/ 开始 / 结束两条滑条 | 时间码文本 + 大画面 + 命令预览**同步刷新** | ⛔ 滑条不直接绑模型属性；`Maximum` 绑 `TimelineMax`（未知给占位 1，防 0/0=NaN 崩布局） |
| 勾剪辑 / 勾转码 | 详情卡 `Expander` 抬头的勾选框 | 决定命令带 `-ss/-to` / 编码参数 | **取消勾选自动折叠**；折叠只影响界面，是否执行由勾选决定；两个可同勾 = 一条命令 |
| 改参数 | 卡内字段 | 命令区实时重算 | 手改过命令后**不再覆盖** `EditArgs` |
| **字幕**（NT-100） | 卡内「字幕文件」+「字幕方式」/ Actions `📝 选择字幕文件…` | 软字幕 = 只封装（`-c copy`，输出 `.mkv`）；硬字幕 = 追加 `subtitles=…` 滤镜 | 软字幕下编码 / 分辨率设置**不生效**（卡内有提示行）；硬字幕必须重编码（copy 预设自动升级 libx264） |
| **多段剪切**（NT-101） | 卡内「多段剪切」多行框（每行 `开始-结束`） | 解析 ≥2 段 ⇒ 命令改走 `-filter_complex`（trim + concat），框下提示「已识别 N 段」 | 多段时忽略开始 / 结束，强制重新编码；<2 段按原单段走 |
| **裁剪**（NT-102） | 卡内「裁剪（宽 × 高）」「裁剪起点（X × Y）」/ Actions `🖼 探测黑边` | 填了就出 `crop=w:h:x:y`；探测从**当前预览位置**取 2 秒跑 `cropdetect`，把结果填进四个框 | 宽高要同时 > 0；奇数边自动取偶；探测失败**不改**已有值（状态栏照实说） |
| **音频**（NT-103） | 卡内「音频」下拉（保持 / 静音 / 音量增益 / 替换音轨）/ Actions `🎵 选择音频文件…` | 静音 → `-an`；音量 → `-af volume=±NdB`；替换 → 第二条输入 + `-map`；挑完音频文件自动切到「替换音轨」 | 替换 + 软字幕**不能同时用**（两条 `-map` 打架，预检红字） |
| **目标体积**（NT-104） | 卡内「目标体积（MB）」 | 填了就按源时长反算码率（`-b:v / -maxrate / -bufsize`），框下显示「≈ 视频 xxx kbps」 | 优先于 CRF / 目标码率；读不到时长则算不出来（预检红字） |
| 手改命令 | 命令区参数框 | 置 `IsArgsEdited`，参数变化不再覆盖 | 手改后**原样执行**（存 `RawArgs`） |
| 重置为自动 | `↺ 重置为自动` | 清标记并重算自动参数 | 仅手改后可见 |
| 复制命令 | `📋 复制命令` | 整条 ffmpeg 命令进剪贴板 | — |
| **试跑 5 秒**（NT-098） | 工具条 `🧪 试跑 5 秒` | 用**当前全部参数**（手改过就用手改的）从「预览位置」起真转 5 秒 → `%TEMP%\NewToys\trial\`；成功后**只回一个信号**：状态栏 `✔ 试跑通过（体积 / 秒数）` + 工具条出现 `▶ 播放小样` | 失败时状态栏显示 ffmpeg 原始错误尾行；不写正式输出、不改任务；⛔ 不在界面并排画小样（NT-098b 去掉对比栏） |
| **留空 = 用默认的字段**（NT-098b） | 卡内「分辨率」「裁剪」「CRF」「GIF 宽度」「输出文件」 | 留空 / 0 时输入框**空着**，水印直接写**当前实际生效的值**（原片宽高 / 预设 CRF / 自动命名后的真实输出路径）—— 一眼分清「手填的」和「自动的」；手填即覆盖，清空回到水印 | 只对「0 = 不改」这类字段生效；`0` 本身有意义的字段⛔ 不开 |
| **播放小样**（NT-098） | 工具条 `▶ 播放小样` | 外部播放器（设置 → 路径配置）优先，其次 `ffplay -autoexit` | 两者都没有 ⇒ 明确提示去配置；没有小样时播源片 |
| 发送到批处理 | `➡ 发送到批处理` | 加入队列，**不立刻跑** | 视频详情卡工具条 |

### 批量处理 `video-batch`

工具条：`📂 添加文件` → `▶▶ 批量运行` / `▶ 只跑选中` / `⏹ 停止` → `💾 保存修改` / `🗑 删除` / `📂 输出目录` / `📋 复制命令` / `↺ 重置为自动` → `📤 导出任务` / `📥 导入任务` / `🧹 清理已结束` / `🧹 清除缓存` → `➡ 送预览`。

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 添加文件 | `📂 添加文件` | 按左侧选定类型**建任务** | **只建任务，不立刻跑** |
| 批量运行 | `▶▶ 批量运行` | 按列表顺序依次处理，跑完自动探测输出 | 运行中该命令禁用 |
| 只跑选中 | `▶ 只跑选中` | 只跑选中行 | — |
| 停止 | `⏹ 停止` | 中止当前任务 | **仅运行中可用**（`IsRunning`） |
| 改参数 | 详情卡改字段 | **不自动写回任务** | 必须点 `💾 保存修改` 才写回（有选中才可用） |
| **字幕 / 多段**（NT-100 / NT-101） | 同上（与预览页同一套字段） | 字段只是编辑缓冲，`💾 保存修改` 后写回任务并重算命令 | 软字幕保存时输出后缀自动改 `.mkv` |
| **裁剪 / 音频 / 目标体积**（NT-102 ~ NT-104） | 同上（与预览页同一套字段） | 同样是编辑缓冲；`🖼 探测黑边` 只填缓冲，`💾 保存修改` 才写回任务 | 命令与预检都基于**任务上的值**（保存前改框不会改命令），卡内有提示 |
| 删除任务 | `🗑 删除` | 只删队列条目 | ⛔ 不删源文件 |
| 导入任务 | `📥 导入任务` | 从导出 JSON **追加**进队列 | ⛔ 不覆盖已有 |
| 导出任务 | `📤 导出任务` | 队列 → JSON（可外部加工再导入） | — |
| 清理已结束 | `🧹 清理已结束` | 从列表移除已完成条目 | — |
| 清除缓存 | `🧹 清除缓存` | 只清页面列表显示 | `config\video-tasks.json` **不动**，可「📥 导入任务」读回 |
| 送预览 | `➡ 送预览` | 选中任务的源文件送进 `video-cut` | 一次只编一个 |
| 手改命令 | 命令区参数框 | `RawArgs` 非空时批量运行**优先用它** | 与预览页同一套 `IsArgsEdited` 机制 |
| **试跑选中**（NT-098） | 详情卡动作 `🧪 试跑选中` | 有区间只跑区间（否则前 5 秒）到临时目录，成功自动交给播放器 | ⛔ 不改任务状态 / 输出路径；批处理运行中禁用 |

### 视频工具设置 `settings-video-tools`

> 🔴 NT-071：本页改用**共用设置页骨架**（`NewToys.UI/Controls/SettingsSectionHost`，`Sticky="False"`）—— 3 个节 = 折叠卡片
> （性能 / 全局项只读 / 设置文件），点抬头开收，默认全展开。细则见 `ui.md` §11。

| 类型 | 写入时机 |
|---|---|
| 播放器 / 默认预设 / 最大并发数 | **即时落盘**（`OnXxxChanged` → 写 `config\video-tools.json`），⛔ 无保存按钮 |

## 3. 实现逻辑

### 3.1 服务清单（`Services/`）

| 文件 | 职责 |
|---|---|
| `FfmpegService` | 命令拼装 + 执行 + `ExtractFrameAsync`（抽帧）/ 探测时长 / `Play`（外部播放器 → ffplay） |
| **`CommandLint`**（NT-098） | 参数**静态预检**：copy 配滤镜/CRF、`.gif` 配 x264、NVENC 用 `-crf`、多个 `-vf`、`-vf`+`-filter_complex`、CRF 越界、GIF 超 30 秒… 只做已知冲突白名单，⛔ 不解析 ffmpeg 语法 |
| `VideoScanner` | 目录扫描入库 |
| `VideoMediaIndex` | 本地索引（`data\` 侧，轻量） |
| `VideoTaskStore` | 跨页投递（`TakePendingCut` / `TakePendingTranscode`，**取一次即清空**） |
| `VideoFileService` | 文件选择 / 打开 + 改名 / 删除的**弹窗确认层**（磁盘动作 v3.66 起委托 `IFileOperationService`，见 framework.md §6.6） |
| `VideoToolsSettingsService` | `config\video-tools.json` |

### 3.2 视频预览页（`video-cut`）

- **布局（迁移后）**：页根主体 Grid **单列**（预览列）；详情卡 = Shell 右详情列（340px，`DetailCardHost` 渲染，可开合）。
- 顶部工具条（页面级动作都归这里）：`📂 选择视频` / `➡ 发送到批处理` / `↺ 区间重置` / `❮❯ 详情`（末位绑 `ToggleDetailPanelCommand`，**会通知 Shell**）；卡内动作（如 `📂 选择输出文件…`）走描述符 `Actions` 区。
- VM **不继承 `ListPageViewModelBase`**（不是列表页）→ `ObservableObject, IStatusProvider`；已实现 `IDetailPanelProvider` + `IDetailCardSource`（`HasDetailPanel => true`、`DetailContent => null`）。

**时间轴轨道 + 两条时间条（NT-105）**
- 「预览位置」行 = `Controls/TimelineTrack`（自绘 `Control`，高 44）：底轨 + 刻度尺 + 播放头 + 区间块。**开始 / 结束两条滑条保留**，负责微调。
- 区间块判定与命令同源：`Segments.Count >= 2` 逐段画，否则画 `Start → End`（`End=0` 按片尾处理）。未勾选「剪辑」时 `PushOpacity(0.35)` 淡显。
- 控件属性：`Duration` / `Position`（TwoWay）/ `Start` / `End` / `Segments` / `IsRangeEnabled` + 5 个 Brush。XAML 里 `Duration` 绑 `TimelineMax`（未知给占位 1，见下条）。
- 刻度：间隔在 `0.5s ~ 4h` 的候选表里选第一个「标签数 ≤ 宽度/76」的档；标签 `mm:ss` / `h:mm:ss`。
- ⛔ **滑条别直接绑模型属性** —— 一律走 VM 手动属性（`PreviewTime` / `StartSeconds` / `EndSeconds`），setter 里改一处 → 时间码文本 + 大画面 + 命令预览一起刷新（避免"拖了滑条、时间码没跟上 → 保存被旧值覆盖"）。
- `Maximum` 绑 `TimelineMax`（时长未知时给**占位 1**）：⛔ 别在 `Maximum==Minimum==0` 时直绑 `Duration` → `(Value-Min)/(Max-Min)=0/0=NaN` → **整页布局算不过去**。
- 大画面 `Image Source="{Binding PreviewImage}"`（`Bitmap`）；拖滑条用**"最新值优先"节流**（`_extracting` + `_pendingPreview`）。⛔ **不铺 `ItemsControl` 缩略图墙**，只保留"当前帧"一张大图。

### 3.3 两页详情卡 = 同一套结构（`video-cut` / `video-batch`）

分区**严格按 ffmpeg 命令的书写顺序**排，⛔ 不另起"任务类型"块：

| 顺序 | 分区 | 内容 | 勾选框语义 |
|---|---|---|---|
| 1 | 「✂ 剪辑」 | 开始 / 结束 / **多段剪切（多行）**、剪切预设、重新编码 CRF、自定义参数 | `IsCut`：勾上 → 命令带 `-ss/-to` |
| 2 | 「🔁 转码」 | 预设、编码器(CPU·GPU)、容器、**分辨率（宽 × 高 一行）**、默认宽高上限（一行）、**裁剪（宽×高 / X×Y 两行）**、**字幕（文件 / 方式 / 字号 / 边距）**、**音频（方式 / dB / 替换文件）**、码率(CRF/目标码率/**目标体积**)、GIF 两项 | `IsTranscode`：勾上 → 命令带编码参数 |
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

### 3.5 参数预检 / 试跑 / GIF（NT-098 · NT-099）

- **预检**：`CommandLint.Check(args, output, source)` 吃**含输出路径的完整参数列表**（两页都从各自的 `Build*Args` 拿，手改过就是手改那条），返回 `LintIssue(IsError, Text)` 列表；`Describe()` 拼成多行（最多 4 条）。详情卡里是一条 `FieldKind.Hint` + `ValuePath=LintText` + `VisibleWhen=HasLint`。
- **试跑**：预览页 `RunTrialAsync` 直接改任务的区间字段后调同一个 `Build*Args`（⛔ 不另写拼参逻辑），产物落 `%TEMP%\NewToys\trial\trial_HHmmss.<ext>`；**NT-098b 起不再抽帧做对比栏**（`TrialImage` / `HasTrialImage` / `LoadTrialFrameAsync` 已删），成功只置 `TrialFile`（工具条 `▶ 播放小样` 靠它显隐）+ 状态栏 `✔ 试跑通过`。批量页 `TrialSelectedAsync` 走 `BuildArgs(t, includeRaw: true, overrideOutput: 临时路径)`，没区间时在输出路径**前**插 `-t 5`。
- **留空字段的水印**（NT-098b）：`DetailField.PlaceholderPath`（动态水印路径）+ `ZeroAsPlaceholder`（0/空按未填处理，提交空写回 `0`）—— 预览页 `TargetWidthPlaceholder` / `TargetHeightPlaceholder` / `CropWidthPlaceholder` / `CropHeightPlaceholder` / `GifWidthPlaceholder`（源宽高，未探测给「原宽 / 原高」）、`TranscodeCrfPlaceholder`（预设自带 CRF）、`OutputPlaceholder`（`ResolveOutput()` = 自动命名的真实路径）。水印随 `SourceWidth/Height`、预设、任意参数变化由 `RefreshCommand()` / `NotifyPlaceholders()` 发通知，⛔ 不靠重建卡片。
- **GIF**：`TranscodePreset.Id="gif"` + `FfmpegService.IsGif()` 判定 → `BuildTranscodeArgs` **独立分支**（`-an -c:v gif -loop 0` + `GifFilterChain` 单遍调色板）；`TranscodeTask.ContainerDisplay` 对 GIF 强制 `.gif`（容器下拉无效）。**单遍**而不是两遍：两遍要在预览里显示两条命令，「看到的 ≠ 跑的」；单遍代价是整段帧过内存 ⇒ 预检对 >30 秒给提示。
- **进度**：`-progress pipe:1 -nostats` 必须放在**业务参数之前**；只认 `out_time_us` 与 `out_time=`（时间码），⛔ 不读 `out_time_ms`（多版本里实际是微秒）。

### 3.6 字幕 / 多段剪切（NT-100 · NT-101）

- **字幕落模型**：`TranscodeTask.SubtitlePath / SubtitleMode / SubtitleFontSize / SubtitleMarginV`；`SubtitleModes`（`None` / `Soft` / `Hard`）+ 下拉项在 `Models/VideoTask.cs`，⛔ 不写死在 XAML。
- **软字幕**走 `BuildTranscodeArgs` 的**独立分支**（`-i 字幕` + `-map 0 -map 1` + `-c copy -c:s srt` + `language=chi` + `default`），**先于** GIF / 编码分支返回；`FfmpegService.OutputExtension(task)` 与 `TranscodeTask.ContainerDisplay` 都对软字幕强制 `.mkv`，批量页保存时还会把旧 `.mp4` 输出名一起改后缀。
- **硬字幕**挂在通用编码分支的 `filters` **最后**（旋转 → 缩放 → 字幕），`SubtitlesFilter()` 负责 filtergraph 转义：`\`→`/`、`:`→`\:`、整值单引号包裹，逗号才不会被当成滤镜分隔符。预设为 `copy` 时 `baseCodec` 自动升级 `libx264`。
- **多段剪切**：`CutTask.SegmentsText` + `CutSegments.Parse`（一行一段，分隔符 `-` `~` `→` 逗号 空白，解析失败的行直接丢）；`BuildCutArgs` 见到 `Segments.Count > 1` 直接转 `BuildMultiCutArgs`（`trim/atrim` + `setpts/asetpts` → `concat=n=N:v=1:a=1`，libx264 + aac）。`Segments` 在 `OnSegmentsTextChanged` 里重算缓存在模型上，⛔ 不在 getter 里反复解析。
- **详情卡成对输入**：分辨率 / 默认宽高上限用 `FieldKind.Pair`（`ValuePath` + `SecondValuePath`），渲染在 `src/UI/Controls/DetailCardRender.cs::PairFieldRow` + `DetailCardHost.axaml`；`Pair` 的提交语义与 `Text` 一致（`OnBlur` 走 `OnPairLostFocus`）。

### 3.7 裁剪 / 音轨 / 目标体积（NT-102 ~ NT-104）

- **命令顺序固定**：旋转 → **裁剪** → 缩放 → 字幕 → 音频 → 码率控制。
  - 裁剪必须在缩放**之前**（用户填的是源画面像素坐标）；`crop` 宽高向下取偶（yuv420p 2×2 采样）。
  - 裁剪 / 音量 / 替换音轨 / 目标体积都会让 `-c copy` 预设自动升级 `libx264`（`needsReEncode` 一处判定）。
- **黑边探测**：`FfmpegService.DetectCropAsync`（独立 `Process`，不共用 `RunAsync`）跑
  `-ss 位置 -t 2 -i 源 -vf cropdetect=24:2:0 -f null -`，从 **stderr** 正则取最后一条 `crop=W:H:X:Y`。
  预览页从「预览位置」取，批量页从区间起点取；结果只进编辑缓冲（批量页需 `💾 保存修改`）。
- **音轨**：`AudioModes` 四档单选取值（`""` / `mute` / `volume` / `replace`），⛔ 不用三个独立开关。
  - 替换 ⇒ 源输入之后立刻追加 `-i 音频`，输出侧 `-map 0:v:0 -map 1:a:0` + `-c:a aac`（源音频不再参与）。
  - 音量 ⇒ `-af volume=±NdB`；预设音频是 `copy` 时自动改 `aac`（`-af` 对 copy 无效）。
  - 静音 ⇒ `-an`，且不再输出任何 `-c:a`。
- **目标体积**：`AutoBitrateKbps` = `MB × 8192 ÷ 秒 − 音频kbps − 8`（下限 50k），走单遍 ABR
  `-b:v Nk -maxrate 1.5Nk -bufsize 2Nk`；优先级 **目标体积 > 目标码率 > CRF**。
- **预检上下文**：`CommandLint.Check(..., LintExtra)`——裁剪尺寸 / 音频方式 / 替换文件 / 目标体积 / 时长
  无法从命令行文本反推，由调用方显式传入；批量页取**任务上的值**（与生成命令的那份一致）。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `VideoToolsModule.cs` |
| 预览页 | `Views/VideoPreviewView.axaml(+.cs)` + `ViewModels/VideoPreviewViewModel.cs` |
| 时间轴轨道（自绘控件，NT-105） | `Controls/TimelineTrack.cs` |
| 批量页 | `Views/VideoBatchView.axaml(+.cs)` + `ViewModels/VideoBatchViewModel.cs` + `Models/VideoTask.cs` |
| ffmpeg | `Services/FfmpegService.cs` |

## 5. 坑点（🔴 实测过）

> **NT-076 第二批**：任务缓存已从 `config\video-tasks.json` 搬进总库 `video_docs`（文档名 `tasks`）。⛔ 构造时不自动载入（需求 §6.4.4），只有用户点「📥 导入任务」才读。

1. 🔴 **覆写可视树回调要写 `protected override`**（`OnAttachedToVisualTree` / `OnDetachedFromVisualTree` 是 protected，写 `public override` → `CS0507`）。
2. 🔴 滑条 `Maximum==Minimum` → `NaN` 布局崩（见 §3.2）。
3. 🔴 输出路径**绝不覆盖已有文件**（自动命名要避让）。
4. 拖滑条抽帧必须节流（最新值优先），否则卡住 UI 线程。
5. 批量页属性名与预览页**不完全同名是刻意的**（见 §3.3），⛔ 别"统一命名"。
6. 🔴 **`-progress` 不能追加在输出路径之后**（NT-098 已修）：ffmpeg 会告警 “Trailing option(s)…may be ignored”，进度可能整个不掉。
7. 🔴 **`out_time_ms` 的数值在多版本里是微秒**（历史 bug）⇒ 按毫秒除 1000 会算成 1000 倍，进度条瞬间满。只读 `out_time_us` / `out_time`。
8. 🔴 **`-ss` 前置 + `-c copy` 会带负 DTS**（开头黑屏/卡顿）⇒ copy 分支固定补 `-avoid_negative_ts make_zero`。
9. 🔴 **GIF 不吃 `-crf` / `-preset` / 音频参数**，且必须自带调色板滤镜；把 GIF 塞进通用转码分支会得到「灰扑扑 + 体积大」或直接报错。
10. 🔴 **抽帧文件名要跟产物同名**（`xxx.shot.jpg`）：`Bitmap` 加载后会占着文件，固定名复用会被上一张锁住导致新图抽不出来。
11. 🔴 **`subtitles=` 滤镜里 Windows 路径必须转义**：`\`→`/`、`:`→`\:`，整值用单引号包住；否则 `C:\a\b.srt` 会被 filtergraph 按 `:` 切成两段，报 `No option name near ...`。
12. 🔴 **软字幕只能进 `.mkv`**：mp4 / mov 不支持文本字幕轨，`-c:s srt` 会直接失败 ⇒ 容器对软字幕强制 `.mkv`，预检也拦。
13. 🔴 **多段剪切靠 `[0:a]`**：源无音轨时 `atrim` 引用不存在的流会报错；当前如实显示 ffmpeg 报错（⛔ 不静默降级成无声版）。多段也无法 `-c copy`（copy 拼不了时间轴）。
14. 🔴 **裁剪坐标必须偶数对齐**（NT-102）：`crop` 宽高取偶是硬要求，x / y 交给 ffmpeg 自己对齐；只填一个边等于没填（`CropFilter` 直接返回空串，预检会红字）。
15. 🔴 **`-af` 只能作用于重编码音频**（NT-103）：`-c:a copy` + `volume` 会让 ffmpeg 报 "Filtergraph…not supported"，所以音量增益遇到 copy 预设要把音频改 `aac`。
16. 🔴 **替换音轨与软字幕的 `-map` 会打架**（NT-103）：两处都要 `-map`，同时开一定出错 ⇒ 预检拦死，不做"自动二选一"。
17. 🔴 **目标体积依赖源时长**（NT-104）：`SourceDuration` 为 0 时算不出码率，预检红字；换源文件要清零（时长换了）。
18. 🔴 **自绘控件写回 TwoWay 属性必须用 `SetCurrentValue`**（NT-105）：`SetValue` 会把 VM 那条绑定**整条掐断**（拖一次之后控件与数据脱钩）。另外自绘 `Control` 要在 `Render` 里先 `FillRectangle(Transparent, bounds)` 一次，否则命中测试没有绘制内容 ⇒ **收不到指针事件**。
19. 🔴 **`TimelineTrack` 的轨道左右各内缩 10px**（`RailPad`）：和下面两条 Slider 的 thumb 内缩对齐；改滑条模板高度/内边距时这条要跟着核。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 本模块暂无未结项 | — |
