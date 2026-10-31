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

工具条：`📂 导入`(SplitButton: 文件夹 / 视频文件) + 目录框(回车=扫描) → `🔄 重扫目录` → `含子目录` → `🔎 读取全部媒体信息` → `➡ 发送到批量处理` → `🧹 清除列表`。

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 导入文件夹 | `📂 导入` 主钮 / 「📁 文件夹…」 | 扫描目录并**替换**当前列表 | 目录框回车等价 |
| 追加文件 | 「🎞 视频文件…」 | 多选后**追加**进当前列表 | ⛔ 不替换已有 |
| 重扫目录 | `🔄 重扫目录` | 重新枚举当前目录 | **选中按绝对路径保留**；媒体信息优先命中 SQLite 缓存 |
| 读取媒体信息 | `🔎 读取全部媒体信息` | 并发 ffprobe → 写 `store\video\video.db` 缓存 | 并发度 = **本模块设置页的「最大并发数」**（`SettingsVideoToolsViewModel.MaxConcurrency`；工具条 ToolTip 里写的「设置→通用」是 NT-062 拆分前的旧文案） |
| 送批处理 | `➡ 发送到批量处理` | 给**选中**视频建转码任务，**不立刻跑** | 无选中 ⇒ 禁用 + ToolTip。原「发送到剪切 / 转码」两项已合并成这一个入口（只送 `video-batch`；要进预览页仍从菜单开页） |
| 接收资产库投递 | 媒体资产页「全部资产」行右键 / 「资产浏览」右键「➡ 发送到视频列表」 | 视频列表**追加**这批视频（路径去重 + 只认视频扩展名），状态栏「已从资产库接收 N 个视频」 | 走 `IModuleBus`（主题 `video.import`）：页未开 ⇒ 构造时取信箱；页已开 ⇒ 订阅回调即时追加。**有投递时不自动扫描目录**（扫描会清空列表）；媒体信息留空，想看参数点「🔎 读取全部媒体信息」 |
| 清除列表 | `🧹 清除列表` | 清空列表显示（含选中 / 筛选）回到空态 | **⛔ 不删任何视频文件、不重扫目录** |
| 多选 | 鼠标 / 键盘：无修饰键=只选该行、`Ctrl`=增选/取消、`Shift`=从基准行连选、`Ctrl+Shift`=追加连选、`Ctrl+A`=全选 | 选中真相在 VM `SelectedRows`；View 只回填 `SelectedItems` | 与「媒体资产 → 全部资产」同口径。🔴 行首勾选框已废除（⛔ 两套状态）：Avalonia 在 `Multiple` 下单击默认「追加」⇒ code-behind 在**隧道**阶段接管，无修饰键也自己处理（替换语义） |
| 全选 / 反选 / 取消选择 | 列表右键尾部三项（= `Ctrl+A` 同一入口 `SelectAllCommand`） | 改当前列表的选中集 | 详情卡跟随最后点击行 |
| 行右键 | 列表行右键 | 播放 / 定位 / 复制路径 / 复制 ffmpeg 命令 / **➡ 发送到批量处理** / 全选·反选·取消 / 读取信息 / 重命名 / 删除（回收站） | 🔴 打行必须在 **`PointerPressed`** 阶段做（`ContextRequested` 常被菜单自己先吃掉 ⇒ 菜单项读的 `Selected` 为空，「右键删除点了没反应」）。命中行已在选中集里 ⇒ 保留多选；否则替换为该行。落空白处不改选中 |

### 视频预览 `video-cut`（当前页 Key 仍叫 `video-cut`）

工具条：`📂 选择视频` / `▶ 播放预览`(↔ `⏸ 停止`) / **`🧪 试跑 5 秒`** / **`▶ 播放小样`** / `↻ 90°` / `↺ 90°` / `➡ 发送到批处理` / **`✔ 保存回任务`**（仅在编辑批量任务时出现）/ `↺ 区间重置` / `❮❯ 详情`（末位，通知 Shell 开合详情列）。

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 选择视频 | `📂 选择视频` | 载入并**自动预览**（探测时长 + 抽当前帧） | 从视频列表送来时也自动触发一次 |
| 播放预览 | `▶ 播放预览` / `⏸ 停止` | 抽帧模拟播放，再点停止 | 拖滑条时「最新值优先」节流 |
| 旋转 | `↻ 90°` / `↺ 90°` | 预览画面旋转（**纯视觉**） | 命令卡「旋转」字段同步 |
| **时间轴区间标记**（NT-105） | 「预览位置」行 = `TimelineTrack` 自绘轨道（点一下 / 按住拖动定位） | 轨道上高亮块 = 当前剪辑区间：单段整块、**多段 ≥2 段逐段色块**；未勾选「剪辑」时 35% 淡显；刻度尺按宽度自动选档 | 区间数据 = `SegmentList`（与 ffmpeg 命令**同源**）；颜色全走主题键 |
| 拖时间条 | 时间轴轨道（定位）/ 开始 / 结束两条滑条 | 时间码文本 + 大画面 + 命令预览**同步刷新** | ⛔ 滑条不直接绑模型属性；`Maximum` 绑 `TimelineMax`（未知给占位 1，防 0/0=NaN 崩布局） |
| 勾剪辑 / 勾转码 | 详情卡 `Expander` 抬头的勾选框 | 决定命令带 `-ss/-to` / 编码参数 | **取消勾选自动折叠**；折叠只影响界面，是否执行由勾选决定；两个可同勾 = 一条命令 |
| 改参数 | 卡内字段 | 命令区实时重算 | 手改过命令后**不再覆盖** `EditArgs` |
| **编辑批量任务**（2026-10-30） | 批量页 `🎬 预览编辑` → 本页 `ConsumePending` 取 `TakePreviewTarget()` | 整条任务参数灌进编辑缓冲（`FillFromTask`），状态栏提示「正在编辑批量任务 X」；工具条出现 `✔ 保存回任务` → `VideoTaskStore.Replace(原, 新)` **保队列位置、不新增**（点 `➡ 发送到批处理` 则另存为新任务） | 编辑属性名与批量页**完全一致**（同一张卡 `Details/VideoTaskCard.cs`）；`FillFromTask` 与批量页 `FillEditors` 是同一套映射，改一处要同步 |
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

工具条：`📂 添加文件` → `▶▶ 批量运行` / `▶ 只跑选中` / `⏹ 停止` → `💾 保存修改` / `🗑 删除` / `📂 输出目录` / `📋 复制命令` / `↺ 重置为自动` → `📤 导出任务` / `📥 导入任务` / `🧹 清理已结束` / `🧹 清空队列` → `🎬 预览编辑`。

> 🔴 **详情卡是唯一一份**：本页与「视频预览」页共用 `Details/VideoTaskCard.cs` 的定义（节 / 字段 / 文案 / 显隐全写在那里），两页只在 `Build(...)` 时传差异 —— 批量 = 显式提交（`💾 保存修改` 才落库）+ 多一节 `📄 任务状态`；预览 = 失焦即生效 + 无任务状态节。
> 卡里引用的属性名两页必须都有，由 `skill/scripts/check-video-card.ps1`（已进 `accept.ps1`）静态校验：**别处再 `new DetailCardDescriptor` 会直接报 ERROR**。

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 添加文件 | `📂 添加文件` | 按左侧选定类型**建任务**；转码任务预设取**详情卡当前预设** | **只建任务，不立刻跑**；工具条无预设框（预设只在详情卡） |
| 批量运行 | `▶▶ 批量运行` | 按列表顺序依次处理，跑完自动探测输出；**已完成的任务直接跳过**（状态就是记录，⛔ 不反复执行） | 运行中该命令禁用 |
| 只跑选中 | `▶ 只跑选中` | 跑**选中的全部行**（原生多选：`Ctrl` 增选 / `Shift` 连选 / `Ctrl+A` 全选）；已完成的同样跳过 | 无选中 / 运行中禁用；「✅ 行首勾选框」已于 2026-10-30 撤除 |
| **重新排队** | 右键 `↺ 重新排队（清状态再跑）` | 把选中任务的状态清回「等待中」（含已完成）+ 清掉上次运行记录 | 明确表示「这条要再跑一次」，⛔ 不删任务、不动磁盘输出 |
| **跑批不睡眠** | 批量运行 / 只跑选中期间自动生效 | `KeepAwakeScope` 起一条后台线程挂 `SetThreadExecutionState(ES_SYSTEM_REQUIRED)`，跑完 / 停止后自动清 | 只拦睡眠，不拦熄屏；非 Windows 平台是空实现 |
| **运行记录** | 列表「完成时间」列 | 每跑完一个任务（成功 / 失败 / 取消）写 `FinishedAt` + `ElapsedSeconds`，显示成 `10-30 15:32 · 1m20s`；随队列落盘，导入后还在 | `Reset()`（改参数保存 / 重新排队）会清掉这条记录 |
| 停止 | `⏹ 停止` | 中止当前任务 | **仅运行中可用**（`IsRunning`） |
| 改参数 | 详情卡改字段 | **不自动写回任务** | 必须点 `💾 保存修改` 才写回（有选中才可用） |
| **分区按能力拆开 + 自动折叠**（2026-10-30） | 详情卡 = 9 节：台头 / `✂ 剪辑` / `🔁 转码` / `🎨 画面（裁剪·字幕）` / `🔊 音轨` / `🎚 码率与体积` / 输出 / `⌨ ffmpeg 参数` / `📄 任务状态` | 切换选中任务时按内容自动定展开态：剪辑 / 转码两节跟勾选框；画面 = 有裁剪或字幕才展开；音轨 = 音频≠保持；码率与体积 = 有目标体积 / 目标码率 / GIF 预设 | 转码专属三节用 `DetailSection.VisiblePath=EditIsTranscode` —— 剪辑任务**整节不渲染**（不是仅收起）；手动点抬头仍可随时展开 / 收起 |
| **保存命令**（2026-10-30） | 卡底动作 `💾 保存命令` | 只把「⌨ ffmpeg 参数」框内容写进 `RawArgs` + 落盘 + 刷列表「命令摘要」列 | ⛔ 不动其它编辑缓冲（与工具条 `💾 保存修改` 的全量提交分工）；内容等于自动生成 ⇒ 清空 `RawArgs` |
| 保存后保持选中（2026-10-30） | `💾 保存修改` / `💾 保存命令` | `RefreshView()` 会 `Items.Clear()` ⇒ ListBox 回写 `Selected=null`，刷新末尾按原引用补回选中 | 补回只在「原选中项还在列表里」时做；⛔ 不补已删除 / 被筛掉的项（否则卡显示幽灵任务） |
| **字幕 / 多段**（NT-100 / NT-101） | 同上（与预览页同一套字段） | 字段只是编辑缓冲，`💾 保存修改` 后写回任务并重算命令 | 软字幕保存时输出后缀自动改 `.mkv` |
| **裁剪 / 音频 / 目标体积**（NT-102 ~ NT-104） | 同上（与预览页同一套字段） | 同样是编辑缓冲；`🖼 探测黑边` 只填缓冲，`💾 保存修改` 才写回任务 | 命令与预检都基于**任务上的值**（保存前改框不会改命令），卡内有提示 |
| **收益预估**（NT-122） | 建任务时自动算（`📂 添加文件` / 预览页发送 / 编辑互转）；详情卡「🔁 转码」节顶部一行 `收益预估：…` | 用源属性（总码率 / 分辨率 / 帧率 / 编码族 / 音轨码率）算 `bpp`，对照目标编码族的**透明线**（x264 0.10 / x265 0.06 / AV1 0.05）判「值得 / 收益有限 / 不建议 / 无法预估」；音轨按码率差单独算 `±N MB` | 只判方向、不预测体积点值（CRF 体积取决于画面复杂度）；**源信息不足一律放行**（`Unknown`，⛔ 不拦）。「只换容器」预设判据独立：音轨原样 ⇒ 「体积几乎不变」 |
| **省体积闸门**（NT-122 · 严格档） | `▶▶ 批量运行` / `▶ 只跑选中` 逐条过闸门 | 策略 = 「严格」时，预估 `不值得` 的转码任务**标 `已跳过`**：不跑 ffmpeg、`Message` 写原因、底部摘要计「已跳过 N」 | 跳过 ≠ 失败；`▶▶` 不会再跑它们（想试就右键 `↺ 重新排队`）；失败 / 取消仍可重跑。目标体积模式不看内容，直接按体积比给确定结论 |
| 删除任务 | `🗑 删除` | 删**选中的全部行**（可多选） | ⛔ 不删源文件；运行中禁用 |
| 导入任务 | `📥 导入任务` | 从导出 JSON **追加**进队列 | ⛔ 不覆盖已有；默认目录 = `store\video\` |
| 导出任务 | `📤 导出任务` | 队列 → JSON（可外部加工再导入） | 默认目录 = `store\video\` |
| 清理已结束 | `🧹 清理已结束` | 从列表移除已完成 / 失败 / 取消的条目（一次清掉，无二次确认） | ⛔ 不动磁盘输出 |
| 清空队列 | `🧹 清空队列` | 清掉整条队列（剪辑 + 转码）**并写进库**（`video_docs` 的 `tasks` 文档一并删除） | 二次确认；⛔ 不动源文件 / 已生成输出。必须落盘 —— 启动会自动恢复，只清内存等于没清 |
| 预览编辑 | `🎬 预览编辑` | 选中任务**连全部参数**送进 `video-cut`，在预览页边看画面边改；那边「✔ 保存回任务」写回本条（`Replace`，保队列位置、⛔ 不新增） | 一次只编一个；走 `VideoTaskStore.AttachPreviewTarget/TakePreviewTarget`（⛔ 不是只传路径的 `EnqueueForCut`） |
| **启动自动恢复** | 打开软件 / 新建 `VideoTaskStore` | 构造时自动 `LoadFromFile()` 读回上次的队列；上次没跑完的「运行中」标成失败（`上次没有跑完（程序退出）`） | 队列 = 工作现场，不是缓存；想清空用 `🧹 清空队列`（落盘） |
| 手改命令 | 命令区参数框 | `RawArgs` 非空时批量运行**优先用它** | 与预览页同一套 `IsArgsEdited` 机制 |
| **试跑选中**（NT-098） | 详情卡动作 `🧪 试跑选中` | 有区间只跑区间（否则前 5 秒）到临时目录，成功自动交给播放器 | ⛔ 不改任务状态 / 输出路径；批处理运行中禁用 |
| **已完成分区** | 列表下区（`FinishedItems`） | 状态 = 完成的任务从上面那块沉到下面独立一块，分区标题写「✔ 已完成（N）」；下区只显示 文件 / 处理后 / 状态 / 完成时间 四列，列位与上区共用 `VisibleColumnSpecs`（宽度天然对齐） | 上区 = 待处理 / 运行中 / 失败 / 取消。下区 `MaxHeight=220` 自带滚动；没有已完成项时整块收起。`RefreshView` 一次分流，⛔ 不另算一遍查询 |
| 看已完成的结果 | 下区行右键 | `▶ 播放输出文件` / `📂 在资源管理器中定位` / `📂 打开输出目录` / `🗑 从队列删除（不动文件）` | 右键先选命中行（按压阶段补选中）；`SelectedFinished` 只服务这组命令，⛔ 不驱动上区详情卡 |

### 视频工具设置 `settings-video-tools`

> 🔴 NT-071：本页改用**共用设置页骨架**（`NewToys.UI/Controls/SettingsSectionHost`，`Sticky="False"`）—— 3 个节 = 折叠卡片
> （性能 / 全局项只读 / 设置文件），点抬头开收，默认全展开。细则见 `ui.md` §11。

| 类型 | 写入时机 |
|---|---|
| 播放器 / 默认预设 / 最大并发数 | **即时落盘**（`OnXxxChanged` → 写 `config\video-tools.json`），⛔ 无保存按钮 |
| 转码前收益判定策略 / 省幅门槛 | 同上即时落盘（`adviceMode` / `adviceMinSavingPercent`）。策略三档：**建议**（默认，只标黄）/ **严格**（不值得就跳过）/ **关闭**（不判定）；门槛 0~90%，默认 10% |

## 3. 实现逻辑

### 3.1 服务清单（`Services/`）

| 文件 | 职责 |
|---|---|
| `FfmpegService` | 命令拼装 + 执行 + `ExtractFrameAsync`（抽帧）/ 探测时长 / `Play`（外部播放器 → ffplay） |
| **`CommandLint`**（NT-098） | 参数**静态预检**：copy 配滤镜/CRF、`.gif` 配 x264、NVENC 用 `-crf`、多个 `-vf`、`-vf`+`-filter_complex`、CRF 越界、GIF 超 30 秒… 只做已知冲突白名单，⛔ 不解析 ffmpeg 语法 |
| `VideoScanner` | 目录扫描入库 |
| `VideoMediaIndex` | 本地索引（`store\video\` 侧，轻量） |
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
| 3 | 「📁 输出文件」 | 路径（留空自动命名，绝不覆盖已有文件）；**输出命名规则将来挂这一节**（如按编码器定后缀：CPU→`H265` / GPU→`HEVC`） | — |
| 4 | 「⌨ 命令」 | 完整命令行（`SelectableTextBlock`）+ 可编辑参数框 + `↺ 重置为自动` + `📋 复制命令` | — |

> 🔴 **2026-10-30 起「输出文件」独立成节**（原来是「输出 + 命令」无标题合成一节）：两页都有「📁 输出文件」抬头，
> 字段标签缩短为「路径（留空 = …）」（⛔ 别再和节标题重复写「输出文件」）；预览页因此变为 5 节（剪切 / 转码 / 输出文件 / 命令 / 收尾说明）。

> 🔴 **批量页 2026-10-30 起结构更细**（预览页仍是上表 4 节）：转码那节拆成 `🔁 转码`（预设/编码器/容器/分辨率/上限/旋转）+
> `🎨 画面（裁剪·字幕）` + `🔊 音轨` + `🎚 码率与体积`，后三节 `VisiblePath=EditIsTranscode`（剪辑任务不渲染）；
> **删掉只读命令行预览**（与可编辑参数框重复，`CommandPreview` 属性仍在，只服务工具条 `📋 复制命令`）与
> **「处理前 → 处理后」对比节**（数据改看列表列的 处理前 / 处理后）；末尾多一节 `📄 任务状态`。
> 折叠语义也升级：切换选中任务时按内容自动定展开态（见 §2.5「分区按能力拆开 + 自动折叠」）。

- **勾选框放在 `Expander` 的 `Header` 里**（`StackPanel Horizontal` 装 `CheckBox` + 文字）；取消勾选 → 自动折叠（`OnIsCutChanged` / `OnIsTranscodeChanged` 改 `IsXxxSectionExpanded`）。折叠只影响界面，**是否执行由勾选决定**。
- **两页 VM 属性刻意对齐**（同名同义）：`EditStart/EditEnd`、`EditExtraArgs`、`EditOutput`、`EditArgs`、`IsArgsEdited`、`CommandPreview`、`CopyCommandLineCommand`、`ResetCommandCommand`。
  - 批量页的转码属性名用 `EditPresetId/EditCrf/EditMaxHeight`（它是列表页、属性名已定型），⛔ 不要为对齐改名（改名要全仓搜旧名）。
- **命令可编辑**：`CommandPreview`（只读合成）与 `EditArgs`（可写）分离 + `IsArgsEdited` 标记。手改后参数变化**不得覆盖** `EditArgs`；`↺ 重置为自动` = 清标记并重新赋自动值。⛔ 手改后的命令必须**原样执行**（存 `VideoTaskBase.RawArgs`，批量运行时优先用它）。
  - ⛔ 别直接给 `EditArgs` 赋自动串再靠比较反推"是否手改"（赋值即相等 → 永远判不出）；`IsArgsEdited` 由「用户输入 vs 当前自动串」比较得出（`OnEditArgsChanged`）。
- **批量页命令区**：选中任务变了要重算 `CommandPreview` / `EditArgs`；`RawArgs` 非空时 `CommandPreview` 直接显示 `RawArgs` 拼出的那条。
- **两页差异**：① 区间来源（预览页 = 时间条，批量页 = 选中任务）；② 类型决定方式（预览页 = 两个勾选框可同时勾 → 剪辑+转码一条命令；批量页 = 任务类型决定）；③ **批量页把转码拆成 4 节并做分区级显隐 + 无前后对比 + 无只读命令行**（见上注）。其余（折叠联动、命令可编辑、提交语义）两页必须一致。

### 3.4 跨页投递

- **媒体资产 → 视频列表**（`video.import`）：载荷 = `IReadOnlyList<(string Path, string MetaJson)>`（`MetaJson` = 资产库的 `metadata_json`）。
  `FlattenPaths` 同时兼容老的纯 `string` / `IEnumerable<string>`；`ImportVideoFiles` 按「载荷元数据 → `video.db` 缓存 → 留空」取值，
  带上的同时 `Upsert` 回缓存。
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

### 3.7.1 转码前收益判定（NT-122）

- **判据是 bpp，不是码率**：`bpp = 视频码率 ÷ (宽 × 高 × 帧率)`，把码率 / 分辨率 / 帧率折到同一尺度，跨分辨率可比。
- **每个编码族一条「视觉透明线」**：`x264 0.10` / `x265 0.06` / `AV1 0.05`（1080p30 等效 6 / 3.7 / 3.1 Mbps）。
  源 bpp **低于**目标族线 ⇒ 源已被压到该族极限，再转只重新分配比特，体积不会降（还会叠加音轨抬码 + 代际损失）⇒ 判「不值得」。
- **速度档折算**：预设是 `medium`，达到同视觉质量比 `slow` 多约 25% 码率 ⇒ 目标线 ×1.25，避免高估省幅。
- **音轨单独精算**：`(目标kbps − 源kbps) × 时长 ÷ 8` = `±N MB`。这是「只省 50MB 的片子反而更大」最常见成因 ——
  预设默认把音轨重编到 192k，源只要 ≤128k 就必然增重。
- **目标体积模式不做预测**：`TargetSizeMb > 0` 时体积是码率封顶的硬约束，直接按 `目标 ÷ 源` 给确定结论。
- **信息不足一律放行**（`Unknown`）：缺帧率 / 分辨率 / 码率时不判死，宁可多转一个也不误杀；帧率缺失按 25fps 估、总码率可由「文件大小 ÷ 时长」反算（剪辑↔转码互转的任务靠这条）。
- ⚠️ 判定是**方向 + 区间**，不是体积预测：CRF 的实际体积取决于画面复杂度，静态算不出来。

### 3.8 列表页扫描与默认预设（旧卡蒸馏：NT-011 / NT-012 / NT-013 / NT-027）

- **无独立「扫描」按钮**：入口只有 `📂 导入`（SplitButton：文件夹 = 扫描并**替换**列表 / 视频文件 = 追加）与目录框回车；`🔄 重扫目录` 走同一份 `VideoScanner.Scan`。
- `VideoScanner.Scan` 只按扩展名白名单（14 种，`.mp4 .mkv .avi .mov .wmv .flv .webm .m4v .ts .mpg .mpeg .rmvb .3gp .vob .m2ts`）过滤；**目录不存在或无权限返回空列表、不抛异常**。数千文件的场景靠「分页 + 媒体信息**显式**触发」兜住：`🔎 读取全部媒体信息` 才并发 ffprobe 并写 `store\video\video.db`，⛔ 不在导入时逐条探测。
- **预设清单的权威源是代码** `Models/VideoTask.Presets.cs`（无配置文件、⛔ 别去 `config\*.json` 找）：`h265` = `libx265` / CRF 23 / `medium` / 输出后缀 `_H265`；GIF 预设自带无声 + 循环。设置页只存「**选中的默认预设**」与「最大并发数」（`config\video-tools.json`，即时落盘）。
- **旋转是纯视觉**：`↻ 90°` / `↺ 90°` 只改预览画面朝向 + 命令卡「旋转」字段，⛔ 不写回源文件、⛔ 不进 `VideoItem` 模型。
- **本模块只做查看器**：视频库（DB 检索）/ 标签 / 收藏三块已整体删除（NT-027），选片 → 预览定参 → 批量跑，跨页只经 `VideoTaskStore`。

## 4. 关键文件

| 关注点 | 文件 |
|---|---|
| 菜单注册（Key 权威源） | `VideoToolsModule.cs` |
| 预览页 | `Views/VideoPreviewView.axaml(+.cs)` + `ViewModels/VideoPreviewViewModel.cs` |
| 时间轴轨道（自绘控件，NT-105） | `Controls/TimelineTrack.cs` |
| 批量页 | `Views/VideoBatchView.axaml(+.cs)` + `ViewModels/VideoBatchViewModel.cs` + `Models/VideoTask.cs` |
| ffmpeg | `Services/FfmpegService.cs` |

## 5. 坑点（🔴 实测过）

> **NT-076 第二批**：任务队列已从 `config\video-tasks.json` 搬进总库 `video_docs`（文档名 `tasks`）。**构造时自动载入**（需求 §6.4.4）—— 关掉软件再打开，队列还在；清空走 `🧹 清空队列`（落盘删文档）。

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
20. 🔴 **`StatusDisplay` 是拼出来的串，必须显式通知**：批量页「状态」列显示 `运行中 42%`，但 `[ObservableProperty] _progress` 默认只通知 `Progress` 本身 ⇒ 百分比只在状态跳变时才刷新（看着像进度不动）。`_progress` / `_status` 两个字段都挂 `[NotifyPropertyChangedFor(nameof(StatusDisplay))]`，⛔ 别删。
21. 🔴 **资产库投递的媒体信息优先于本地缓存**（见 §3.4）：`ImportVideoFiles` 先吃载荷里的 `metadata_json`，再退 `video.db` 缓存，都没有才留空等用户点「🔎 读取信息」——⛔ 导入时不跑 ffprobe（几十个文件会把界面卡住）。
22. 🔴 **`ffprobe` 一旦带 `-select_streams v:0` 就永远拿不到音轨信息**（NT-122 已修）：`-show_streams` 只输出被选中的流，音频流的 `codec_name` / `bit_rate` 全部为空 —— 这也是历史上「音频」列一直显示 `—` 的根因。现在不传 `-select_streams`，解析时按 `codec_type == "video"` 找第一路视频（⛔ 别再用 `streams[0]`：封面图 / 字幕流可能排在前面）。
23. 🔴 **判定不能据 `Unknown` 拦任务**（NT-122）：缺帧率 / 分辨率 / 码率时是「不知道」，不是「不值得」。反过来的错更贵 —— 该转的片子被拦下来，用户以为软件坏了。
24. 🔴 **跳过 ≠ 失败**（NT-122）：`VideoTaskStatus.Skipped` 不进 `Done`，但`▶▶ 批量运行`的候选过滤只排除 `Done` / `Skipped`（失败 / 取消仍要能重跑，⛔ 别顺手改成 `IsFinished`，那会把「重试失败任务」这个既有能力一起砍掉）。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

| NT | 内容 | 状态 |
|---|---|---|
| — | 本模块暂无未结项 | — |
