# 模块 · 查看器（Viewer）

> **程序集**：`src/Module.Viewer`（`NewToys.Viewer`）；渲染器（短文本 / 图片 / 音频外壳）全在本模块 `Controls/`（NT-144 批 1/3 下沉，`src/UI` 已无渲染器）
> **模块键**：`viewer` ｜ **一级菜单**：Order 55 · 图标 `IconViewer`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：V1/V2 + NT-142/143 已验收；**NT-144 批 3（⏳ 待 build）：长文本阅读（ReaderShell / TxtBookService）迁入「网络阅读」模块 —— 本模块文本页改「文档查看」（源码 / 短文本 / HTML），PDF / EPUB / MOBI 另行立项**
> **最后校对**：2026-11-04（NT-144 批 3 阅读职责重划）

---

## 1. 职责与边界

- **定位**：整个软件共用的「打开一个文件 → 用合适的渲染器显示 / 播放」，**能力层不是业务层**。
- 🔴 **渲染器住本模块 `Controls/`**（`TextViewer` / `ImageViewer` / `AudioPlayer`，命名空间 `NewToys.Viewer.Controls`）。长文本阅读器壳（`ReaderShell`）住「网络阅读」模块 —— 阅读能力与渲染归一处，模块禁互引（NT-144 批 3 重划）。
- 🔴 本模块只做三件事：**入口与分派 / 压缩包遍历 / 设置与位置记忆**。
- ⛔ 不做：媒体库管理（归 `Module.MediaAssets`）、转码（归 `Module.VideoTools`）、标签写回、编辑（一律只读）、图库收藏评分。
- ⛔ **不做视频内嵌播放**（2026-11-02 用户定）：视频调起系统默认程序。

## 2. 当前功能（3 页）

| 菜单 Key | 标题 | Order | View / VM | 说明 |
|---|---|---|---|---|
| `viewer-text` | 文档查看 | 1 | `ViewerTextView` / `ViewerTextViewModel` | 源码 / 短文本 / HTML；**txt / md / log 长文本阅读已并入「网络阅读」**（打开时给指引提示）；PDF / EPUB / MOBI 规划中 |
| `viewer-image` | 图片查看 | 2 | `ViewerImageView` / `ViewerImageViewModel` | 图片与压缩包内图片；缩放 / 适应窗口 / 逐张翻页 |
| `viewer-audio` | 音乐播放 | 3 | `ViewerAudioView` / `ViewerAudioViewModel` | 本地音频播放列表；播放/暂停、歌词、顺序/单曲/随机 |

- 三页**共用**一套页面逻辑（`Views.ViewerPageBase`）与**同一份 XAML 内容**（只有 `x:Class` 不同）。
- 页面范围由 `ViewerViewModel.Scope` 决定：`Text` 收源码 / 短文本 / HTML，`Image` 收图片与压缩包，`Audio` 收音频。
- 工具条按范围裁剪：文本页 ⛔ 无缩放 / 翻张 / 播放；图片页 ⛔ 无字号；音频页 ⛔ 无字号 / 缩放。
- 最近打开按本页范围过滤显示（同一份 `viewer.json`，三页各显各的）。
- ⛔ 旧统一页 `viewer-main` 已撤销（用户定：三页取代统一页）。

**支持的类型**

| 类别 | 扩展名 | 能力 |
|---|---|---|
| ~~长文本阅读器~~ | `.txt .md .markdown .log` | **已迁出（NT-144 批 3）**：到「网络阅读 → 阅读」点「📂 本地 TXT」打开；本页拖入 / 打开这类文件时给指引提示 |
| 短文本 / 源码 | `.json .xml .csv .ini .cfg .conf .cs .py .ps1 .js .ts .sql .bat .cmd .yaml .yml .toml` | 整篇滚动 + 字号 / 行距 / 边距 |
| HTML | `.html .htm` | 去标签取纯文本（⛔ 不执行脚本、⛔ 不加载远程资源） |
| 图片 | `.png .jpg .jpeg .bmp .gif .webp .ico` | 缩放 / 平移 / **旋转 90°** / 双击切 1:1 与适应窗口；**同目录逐张翻页**（←/→）；**右键图片信息**；**分屏：单张 / 左右 / 上下 / 2×2**（翻页换整屏） |
| 压缩包内图片 | `.zip .rar .7z .cbz .cbr` | 列出包内图片 → 顶部条目条点选 → ←/→ 翻页；只列图片、⛔ 不解压到磁盘；**zip / cbz 只读中央目录**（2 GB 包也秒开）；右键图片信息显示包路径 + 包内路径；同样支持分屏网格 |
| 音乐 | `.mp3 .flac .wav .m4a .aac .wma .ogg .opus .aiff` | 播放列表（**含 m3u / m3u8 导入**）+ 播放/暂停 + 进度 + 音量 + 上一首/下一首 + 顺序/单曲/随机 + 标签封面 + 同名 `.lrc` 歌词（**支持译文双语**）+ **播放列表记忆**（关掉再开能续上，⛔ 不自动播）|
| 视频 | `.mp4 .mkv .avi …` | **不在本模块**：由系统默认播放器打开 |

**不支持（给明确提示，⛔ 不静默失败）**：PDF / EPUB / MOBI（「文档查看」页规划中，见 §6）、Office；加密 / 分卷压缩包。

## 2.5 操作逻辑

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| **跨页自动跳转** | 打开 / 拖入 / 点最近打开的文件不属于本页 | 投递给目标页并切过去，目标页当场打开该文件 | 投递走 `ModuleBus`（主题 `viewer.openfiles`，载荷 `ViewerOpenRequest`）；本页 ⛔ 不装载、⛔ 不报错 |
| 打开文件 | 工具条「📂 打开」 | 本页范围内：阅读器 / 图片 / 压缩包 / 音频 / 短文本 | 取消 = 什么都不做；可多选，**多选且全是音频**才整批进播放列表 |
| 拖入文件 | 拖到窗口任意位置 | 同上 | 多文件非全音频时只开第一个 |
| 最近打开 | 工具条「🕘 历史」→ 浮层点一条 | 直接打开（不属于本页则转去对应页） | 已剔除不存在的路径；上限 20；按本页范围过滤显示；浮层里可「清空」；打开任一项后浮层自动收起；**点浮层以外任意处 / 按 Esc 也自动收起**（浮层是页内 `Border`，自己挂顶层监听做 light-dismiss）；浮层**左边缘对齐工具条「历史」按钮**（按钮被折进「⋯」时退回右上角） |
| 关闭 | 工具条「✕ 关闭」 | 回空态；先存进度 | 音频同时停止（显式关闭 = 不出声） |
| **分屏** | 工具条「分屏」按钮 → 点一下**展开四档菜单**：`▭ 单张` / `◫ 左右` / `⊟ 上下` / `▦ 2×2` | 选一档即切；按钮文字常驻显示**当前档**（如「◫ 左右」），选「单张」回单图 | 只有这四种布局，⛔ 无「三列」；网格按屏对齐：**翻页一次换一整屏**；**一屏的激活项 = 本屏第一张**（开分屏 / 翻屏 / 换布局都这样：第一格带强调色描边、目录条选中并居中到该条），点某格改选它（强调色描边 + 目录条跳到该条），**双击某格 = 退出分屏并单图看这张**；分屏下「＋ / － / 适应窗口 / ↻ 旋转」置灰（各格按格自适应）；末屏不足一张屏照常显示 |
| **条目条滚动** | 换图 / 换屏 / 点条目 | 当前条目**滚到条目条正中**；整条放得下时不滚动、按顺序从头排 | 横向溢出时不出滚动条（`HorizontalScrollBarVisibility=Hidden`）；条高 20px 项高；在条上滚轮 = 翻图 / 换屏（⛔ 不横向滚动） |
| **条目条分节** | 同目录图片 / 压缩包条目 ≥ 20 张时自动出现 | 节大小按总数自适应（≈ 10 节，10 的倍数，最小 10 张）：条目条上每隔一节画竖线 + 范围（`1–10`）；条头「节」下拉选一节 = 直接跳到该节第一张 | 同目录与压缩包**都分节**；节下拉随当前图自动指到所在节；分屏翻屏时节下拉跟着屏走 |
| **长文本指引** | 打开 / 拖入 / 最近点 `.txt .md .markdown .log` | 不装载，状态栏报「长文本阅读已并入『网络阅读』—— 到那边点『📂 本地 TXT』打开」 | 🔴 阅读器壳 / 目录 / 书签 / 听书都在「网络阅读」模块（NT-144 批 3） |
| **图片翻页** | 点顶部条目条 / 工具条「←」「→」/ ←→ | 换成同一批里的那张图 | 工具条只放箭头图标（`←` `→`，⛔ 不用播放键那种实心三角，⛔ 不带文字，说明靠悬停提示）；来源三种：**同目录图片**（按文件名排序，自动带出）、**压缩包内图片**、**外部投递的一批图**（`ModuleTopics.ImageOpen` ← 媒体资产「标签详情卡 → 🖼 图片」：翻页集**就是**这批图，⛔ 不再扫同目录混进没被选中的图）；序号显示「N/M」（分屏时「a–b/M」，⛔ 不加「第…张」前缀 —— 位置本身已说明是图片序号）；只有 1 张时⛔ 不显示条带 |
| **大压缩包** | 打开 `.zip` / `.cbz` | 状态栏先报「正在读取压缩包…」，读到后列出条目并显示第一张 | zip 家族只读**中央目录**（不解压任何数据）；rar / 7z 无等价 API，仍按条目顺序扫 |
| 音乐播放 | 打开音乐 / 拖入多首 / 拖入 m3u·m3u8 | 自动播第一首；底部条控制 | 🔴 **打开单首时会先问一句「把同一文件夹里另外 N 首也加进播放列表？」**（`DialogHooks.ConfirmAsync`，同目录音频按文件名排序并入，当前曲目定为起始项；选取消就只放这一首；⛔ 拖入多首 / 走「媒体资产 → ▶ 播放」/ m3u 这条路不问）；🔴 **切走页面 ⛔ 不停播**（`AudioPlayer` 卸载时只停轮询表、不 Stop），切回来界面照旧刷新、播完照常续播；列表重建与「续播」互不打架：`RestoreAudioPlaylist` 见到 `Host.Audio` 有待播内容且后端非 `Stopped` 就直接返回，⛔ 不重建列表；真正换文件（`Open` 打开非音频）前才 `Host.Audio.Stop()`；播放列表文件先展开成音频列表，路径失效的条目跳过；**关闭标签页即停播**（`ViewerViewModel.Dispose` → 先存续播快照再 `Stop`；文本 / 图片页的 VM 被关掉时⛔ 不掐音乐），重开音频页回到同一首 + 同一位置 + 同一模式与音量（**暂停态**，⛔ 自动播） |
| 播放器界面 | — | **悬浮曲目条**（曲名 / 艺术家·专辑 / 技术信息，浮在主显示区左上角；**透明背景、无边框无阴影**，与主显示区不区分，⛔ 不再占顶部一整行） + 大封面 + **居中歌词**（当前行加粗高亮，走到中线后跟随滚动）+ 底部控制条（上一首 / 播放 / 下一首 / 进度 / 时间 / 模式 / 音色 / 音量）；**布局三档**（工具条「◫ 左右 / ▭ 封面 / ⊟ 上下」下拉，与图片分屏同一套交互）：左右分屏（封面 ｜ 歌词，默认）/ **只显示封面**（歌词收起，封面放到最大）/ **上下分屏**（封面在上、歌词在下）；**主显示区动效**（工具条「✦ 动效」）= 盖一层动态几何图案（三层旋转多边形 + 顶点连线 + 中心呼吸圆 + 向外扩散脉冲环，30 fps）；**播放列表 = 右侧悬浮层**（工具条「☰ 列表」开合，默认收起，点条目切歌，层内 ✕ 收起，每行右侧显示该曲**时长**）；**音色 = 底部音色键弹出的菜单**（点键即展开，点外即收） | **控制条容器透明**（整页共用内容底色：歌词区与底栏都 ⛔ 不刷 `ContentHeaderBrush`，控制条看起来是嵌在主显示区里的一条，不是独立底栏）；底栏按钮 **22px**（`Padding="10,0"`，⛔ 再撑高）；按钮**一律纯图标**（`◄◄` `►`/`❚❚` `►►`）并显式 `Horizontal/VerticalContentAlignment=Center`，含义进 `ToolTip` + `AutomationProperties.Name`；🔴 **图标只用非 emoji 码位** —— `⏮ ⏭ ▶ ⏸ 🔁` 会被 Segoe UI Emoji 接管渲染成彩色方块（与主题冲突）；🔴 **两个 Slider ⛔ 设 Height / MinHeight**（模板被压扁 ⇒ 轨道与按钮错位，见 `ui.md` §9-15），只写 `VerticalAlignment=Center`；进度/时长文字 `VerticalAlignment=Center`；技术信息 = `格式 · 采样率 · 码率 · 声道`（TagLibSharp 读 `TagLib.Properties`，读不到只留扩展名）；歌词行字号 14 / 译文 12，居中排版；🔴 **封面边长按布局与可用空间算**（`UpdateCoverSize`：左右 130~320、只封面 150~460、上下 110~260），⛔ 写死 260（上下分屏时会把歌词挤没）；封面下方分频柱（32 段竖条，对数分频 60 Hz~16 kHz，33 ms 刷新；宽高随封面缩放）：只在播放时出现，暂停 / 停止 / 后端不支持频谱即整块收起（⛔ 留空框）；封面取不到时显示音符占位（⛔ 空白边框） |
| 播放列表 | 工具条「☰ 列表」（仅音频页可用） | 右侧悬浮层：曲名·艺术家 + 右侧时长（`mm:ss`，超 1 小时 `h:mm:ss`，未知留空） | 行模板是两列 Grid（`*,Auto`）；开关按钮在**工具条**而不是播放器里（曲目信息是浮在主显示区上的曲目条）；音色是底部键弹出的菜单，与列表互不干扰（点菜单外即收） |
| 音量 / 静音 | 点底部「音量」键 | 一下静音（音量滑到 0），再一下回到**静音前**的音量 | 静音态键面变「静音」、提示变「恢复上次音量」；拖滑杆离开 0 即视为取消静音 |
| 音色 | 点底部音色键 | 键下方弹出菜单，列出 5 档（关闭 / 低音增强 / 流行 / 人声 / 古典），点一项立即生效并收起菜单，当前项带单选勾 | 每次展开重建菜单项（以 `IAudioTone.CurrentPresetKey` 为准，⛔ 缓存项防漂移）；键面写**当前预设名**，直通态写「音色」（写「关闭」看不出这键在选音色）；能力 != 契约：后端实现 `IAudioTone` 才出现该键（`NullAudioPlayback` 不实现 ⇒ 自动隐藏）；播放中即时生效（滤波系数整体替换），⛔ 需重开文件；曲线 = 级联 biquad（RBJ：LowShelf / Peaking / HighShelf），⛔ 依赖 `NAudio.Dsp` |
| 歌词滚动 | 随播放自动；滚轮 / 拖滚动条可手动查看 | **当前行落在视口垂直中线**，行与行之间按播放时间线性插值滚动（40 ms 一帧） | 🔴 偏移夹在 `≥0`：开头几行没排满一屏时列表不动，高亮行从上往下逐行走，走到中线后列表才开始滚；⛔ 用 `BringIntoView()`（一到行首就跳居中，顿挫得像卡死）；暂停 / 停止即停表；**手动查看**：滚轮或在滚动条上按下即暂停跟随，停手 **3 秒**（`_lyricResumeTimer`）自动拉回当前行；只在命中 `ScrollBar` 时算手动（点歌词正文不算），换曲 / 切页即恢复跟随；贴边滚动条**不留白缝**（Shell 的 `WM_NCHITTEST` 钩子让控件优先，见 `ui.md` §9-16） |
| 音频模式 | 底部模式键（图标循环切换） | 播完按模式决定下一首 | 「顺序」到末尾停止并提示；键面 = `↓` 顺序 / `↻1` 单曲 / `⇄` 随机（三个都是非 emoji 码位），当前模式进 `ToolTip` 与状态栏 |
| 歌词 | ① 同名 `.lrc`（+ 译文）② 内嵌标签 `Tag.Lyrics` ③「标题 - 艺术家.lrc」/「艺术家 - 标题.lrc」 | ①③ 带时间轴 ⇒ 当前行高亮 + 自动滚动；② 无时间轴 ⇒ **静态整段**平铺（⛔ 不高亮不滚动）；都没有 ⇒ 歌词区显示「这首歌没有歌词」 | 🔴 **编码回退**：BOM → UTF-8（严格）→ **GB18030**（中文老 LRC 多为 GBK，按 UTF-8 直读会花屏）→ Latin1；⛔ 联网下载歌词，⛔ 逐字卡拉 OK |
| 封面 | ① 内嵌标签图（优先 `FrontCover`）② 同目录同名图 → ②b **宽松同名**（词集合互相包含，命中歌名末词优先）→ `cover` / `folder` / `front` / `album` 等固定名 ③ 上一级目录（分碟 `CD1/` 场景）④ 占位音符块 | 全部读不到时显示占位（⛔ 空白边框） | 🔴 只读、⛔ 联网、⛔ 写回文件（在线补封面归媒体资产的标签服务）；同目录图片**按目录缓存一次**；宽松匹配把文件名切成词（去括号内容 / 轨道号 / 分隔符）再比，`周杰伦 - 稻香.mp3` ⇄ `稻香.jpg` 能配上，⛔ 单字符词（至少 2 字符）防乱配 |
| 双语歌词 | ① 双行 LRC（同一时间戳重复写原文 + 译文）② 外置译文 `<歌名>.zh.lrc` / `.trans.lrc` / `.translation.lrc` / `.cn.lrc` | 原文一行、译文小一号显示在同一块 | 译文按**时间戳**对齐原文；两种都给时**文件内双行优先**；⛔ 不做逐字卡拉OK（LRC 没有逐字时间轴） |
| 接「媒体资产 → ▶ 播放」 | 资产勾选音乐 → 工具条「▶ 播放」 | 整批换列表并立即开播（`IModuleBus` 主题 `viewer.audioplay`） | 载荷 = 路径数组，顺序即播放顺序；页面还没建时先攒着，挂树后开播 |
| 图片缩放 | 「＋」/「－」/ Ctrl+滚轮 / 双击 | 5%~1600%，状态栏显示百分比 | 适应窗口只缩小不放大 |
| **图片旋转** | 工具条「↻」或图片右键「旋转 90°」 | 顺时针转 90°，四次一圈；转完还按「适应窗口」显示 | 只对单图生效（分屏网格置灰）；旋转后缩放 / 平移 / 滚轮翻页照旧；信息卡里多一行「旋转 90°」 |
| **图片信息** | 在图片上右键 →「图片信息」 | 浮层卡片：名称 / 像素尺寸 / 当前显示比例与显示尺寸 / 文件大小 / 修改时间 / 类型 / 位置（压缩包内图 → 包路径 + 包内路径）；可「复制路径」 | 点卡片外、`Esc`、「关闭」都收起；卡片开着时滚轮与 ←→ **不翻页**（否则数字会换成别的图） |
| **图片滚轮翻页** | 图片上滚动滚轮 | 上一张 / 下一张（与 ←→ 等效） | 仅在**未放大**（Zoom ≤ 1）时生效；放大后滚轮回归滚动查看；`Ctrl`+滚轮仍是缩放；180 ms 节流防触控板连翻 |

## 3. 实现逻辑

```
ViewerPageBase（Module.Viewer/Views，文本 / 图片 / 音频三页共用）
  ├─ ViewerTextView / ViewerImageView / ViewerAudioView：只 `InitializeComponent()` + `InitPage()`
  ├─ 工具条 / 状态栏        → VM 实现 IToolbarSource / IStatusProvider
  ├─ 文件选择 / 拖放 / 键盘  → 基类 code-behind（VM ⛔ 不碰 StorageProvider）
  └─ 持有 ViewerHost ──────→ Module.Viewer/Controls（命名空间 NewToys.Viewer.Controls）
                               ├─ KindOf(path) 扩展名分派
                               ├─ TextViewer（短文本 / 源码 / HTML）
                               ├─ ImageViewer（图片 / 压缩包内图片，支持流加载；自带右键菜单 + 信息浮层）
                               ├─ AudioPlayer（播放列表 + 控制条 + 歌词；主显示区布局三档）
                               └─ GeometryVisual（动态几何图案，自绘 + 30 fps 表；AudioPlayer 的动效层）
ArchiveImageService   打开压缩包会话（zip 走 ZipArchive 中央目录，rar/7z 走 SharpCompress），条目读进内存流
AudioLibraryService   TagLib 读标签 / 封面 / 同名 lrc
NAudioPlayback        IAudioPlayback 的 Windows 实现（MF 解码 + WaveOut 输出）
ViewerSettingsService config\viewer.json 唯一写入点
```

- **连线方式**：VM 发事件（`OpenRequested`（载荷 `IReadOnlyList<string>?`，`null` = 弹选择器）/ `ArchiveNavigateRequested`），View 订阅后操作控件；VM ⛔ 不 new 控件。
- 🔴 **跨页跳转 = `ModuleBus` 投递 + `NavigateTo`**：`ViewerOpenRequest` 带目标 `Scope`，收到请求的页先比对范围，不匹配直接忽略（三页共用一个主题，靠载荷分流）。投递前无订阅者则进信箱，目标页构造时 `TakeAll` 取走 ⇒ 两条路径都不丢。
- 🔴 **音频主显示区布局 = 固定「两行两列」+ 只改两块的 `Row`/`Column`/`Span`**（`AudioPlayer.ApplyLayout`，三档 `AudioStageLayout`；XAML 里 `MainGrid` 就是 `RowDefinitions="*,*"` + `ColumnDefinitions="*,1.1*"`）：⛔ **运行时替换 `RowDefinitions` / `ColumnDefinitions`** —— 换完旧索引还在（如 `LyricPane` 的 `Column=1` 撞上只剩 1 列）会让 Grid 取定义越界、**整个软件直接崩**（2026-11-04 实测）；⛔ 不建第二套 XAML、⛔ 不换控件（换了要重挂歌词 / 封面）；封面边长随布局与可用空间 `UpdateCoverSize` 重算（内部 `_sizing` 防重入），分频柱宽高跟着封面走。
- 🔴 **动效层开关只切 `IsVisible` + 起停表**：`GeometryVisual` 不可见 / 离开视觉树即停表（切页回来由 `AudioPlayer.AttachedToVisualTree` 重新起），⛔ 后台空转；配色取 `Background`/`Foreground` 主题键，代码里⛔ 写死色值。
- 🔴 **音频走 `Shared/Services/IAudioPlayback` 契约**：`NewToys.UI` ⛔ 不引 NAudio；控件靠宿主注入，换后端（libvlc / mpv）只换 `NAudioPlayback`。
- 🔴 **图片缩放改 `Image.Width/Height`，不用 `RenderTransform`**：占位尺寸不变则 `ScrollViewer.Extent` 不跟，放大后拖不到边缘。
- 🔴 **旋转用「外层 `Panel` 按旋转后外框占位 + 内层 `Image` 自转」**：90°/270° 时外框宽高互换、内层保持未旋转尺寸绕中心转 ⇒ 布局尺寸与 `Extent` 都对（Avalonia 12 没有 `LayoutTransformControl`，只给 `Image` 加 `RenderTransform` 会裁切 / 拖不到边）。
- 🔴 **分屏 = 页面内动态建的网格控件（`SplitGrid`），不经过 `ViewerHost`**：每格 = `Border` + `Image(Stretch=Uniform)` + 序号角标；布局由 VM 的 `SplitRows × SplitColumns` 决定（只有 1×2 左右 / 2×1 上下 / 2×2 三种，`N` = 两者乘积）。位图在 `Task.Run` 里解码（`ReadBitmap`，⛔ 不碰控件），回 UI 线程才设 `Source`；重铺时递增 `_splitToken` 丢弃过期结果。**各格解码并行**（`Task.WhenAll`，逐格 `await` 会让右边那张等左边解码完才开工）；压缩包会话 `Open` 内部加锁串行（`ZipArchive` 条目流共享底层流，并发读会错位）。
- 🔴 **分屏的翻页是「按屏」的**：`_entryIndex` 永远存屏起点，`GoToScreen(delta)` 用 `index / N` 算屏号；滚轮 / ←→ / 工具条上下张在分屏下都走它。分屏时 ⛔ 不能再用 `Host.Kind` / `Host.Zoom` 判断（宿主此刻隐藏，值还是上一张的）。
- 🔴 **位图释放顺序**：先把 `Image.Source = null` 再 `Dispose()`，否则 Skia 可能持有已释放的表面。
- 🔴 **条目条滚动用 `container.BringIntoView()`（与标签栏同一套），⛔ 别自己算 `ScrollViewer.Offset`**：自行算偏移在容器还没生成时静默不滚；`BringIntoView` 走布局系统，天然处理横向溢出。要延后到 `DispatcherPriority.Background` 再取 `ContainerFromIndex`，取不到（还没建容器）就下一轮重试，最多 2 次。
- 🔴 **分节 = 条目项自带节标，⛔ 不往条目集合里插额外条目**：`ArchiveEntries` 是 `ViewerEntryItem { Name, IsSectionStart, SectionLabel }`，视图索引必须与图片序号一一对应（插头会打乱 `SelectedIndex` 与 `_entryIndex` 的等价关系）；节大小按总数自适应（`PickSectionSize`：≈ 10 节、10 的倍数、夹在 10~100），< 20 张不分节；同目录与压缩包都分节。
- 🔴 **历史浮层对齐工具条按钮 = 按命令对象反查**：浮层在页面里、按钮在 Shell 的工具条里 ⇒ 从 `TopLevel` 视觉树找 `Command == ToggleHistoryCommand` 且可见的 `Button`，`TranslatePoint` 到浮层父级算左边距；⛔ 别按文案匹配（文案是本地化的），查不到（被折进「⋯」）就退回右上角贴边。
- 🔴 **进度监听用 `ScrollViewer.OffsetProperty` 属性变更，⛔ 不用 `ScrollChanged` 事件**（该项目踩过不触发 / 时序不稳）。

## 4. 数据与配置

| 项 | 落点 | 写入点 |
|---|---|---|
| 短文本排版（`fontSize` / `lineSpacing` / `pagePadding`） | `config\viewer.json` | `ViewerSettingsService` |
| 最近打开（`recent`，≤20） | 同上 | 同上 |
| 短文本位置（`positions`，≤500） | 同上 | 同上 |
| ~~长文本排版 / 位置 / 书签 / 章节正则~~ | 同上（键已下线，旧数据残留无害，⛔ 不迁移） | — |
| 播放音量（`audioVolume`，0~100） | 同上 | 同上 |
| 打开的源文件 | **只读**，⛔ 不写回 | — |

- 短文本进度键 = `完整路径|字节数|最后修改时间(ticks)`（文件变了就重置）。
- 书签键 = **文件全路径**（文件被替换仍属同本书，⛔ 不用时间戳）。

## 5. 坑点（实测）

- 🔴 **三页 XAML 必须是同一份内容**：`ViewerPageBase` 按 `x:Name`（`Host` / `RecentList` / `ArchiveList` / `SectionBox` / `SplitGrid` / `HistoryPanel`）取控件；少一个就抛「ViewerPage 的 XAML 缺少名为 X 的控件」。改一页必须同步另两页。
- 🔴 **XAML 里引用的事件处理必须是 `protected`**：处理器写在基类，XAML 编译生成的调用落在子类 partial 里，`private` 编译不过（`OnArchiveSelected` / `OnSectionSelected` / `OnRecentSelected`）。
- 🔴 **条目条里的 `ComboBox` 必须显式 `Height="22" MinHeight="0"`**：隐式样式 `MinHeight=28` 会把 22px 的条撑高，条带跟着变胖。
- 🔴 **条目条（`ArchiveList`）必须用横向 `VirtualizingStackPanel`，⛔ 别退回 `StackPanel`**：横向 `StackPanel` 不虚拟化，几千张的漫画包会一次性实例化几千个 `ListBoxItem` 容器，切标签页再切回时 Avalonia 摘除→重挂视觉树触发全量重新 Measure/Arrange，实测卡顿 >1 秒。虚拟化后只实例化可见条目，切回重排成本从 O(几千) 降到 O(可见数)。
- ~~🔴 `ViewerHost` 不能放在 `Module.Viewer` 里~~ **已作废（2026-11-04）**：渲染器族已整体下沉到 `Module.Viewer/Controls/`；批 3 后 `ReaderShell` / `TxtBookService` 迁入「网络阅读」，`src/UI` 渲染器清零。
- 🔴 **降采样解码必须先探测原始尺寸**：`Bitmap.DecodeToWidth/Height(stream, n)` 是解码期缩放（省内存的唯一手段），但**目标尺寸大于原图时会放大** ⇒ 小图反而多吃内存。所以先走 `ImageHeader.TryReadSize`（只读文件头，png / jpeg / gif / bmp / webp）拿原始宽高，超限才降采样；探测不出来就整图解码。
- 🔴 **解码边长上限 = 视口物理像素宽 × 2**（夹 `1200 ~ 4096`；分屏每格 `480 ~ 2048`）。超过屏幕精度的像素只是白占内存（漫画包四格全尺寸能吃掉几百 MB）。信息卡里「尺寸」仍报**原始**像素，「解码尺寸」另起一行说明降过采样。
- 🔴 **SharpCompress 1.0 把 `ReaderFactory.OpenReader(stream)` 改名成 `Open(stream)`**（2026-11-04 实测编译报 CS0117）：`IReader` 的 `Entry` / `MoveToNextEntry` / `OpenEntryStream` 与 `IEntry` 的 `Key` / `IsDirectory` / `Size` 都没变 ⇒ 0.50 → 1.0 只需改这一处方法名，⛔ 别整体重写 `ArchiveImageService`。
- 🔴 **换图那一帧必须先把滚动条置 `Disabled`**：新图 `Extent` 未稳定时 `Auto` 会让竖直滚动条闪一下；铺好（`DispatcherPriority.Loaded`）再恢复 `Auto`。回调自带 `_loadToken`，连按翻页时旧回调直接丢弃。

- 🔴 文本编码：先认 BOM，再试**严格 UTF-8**，失败回落 **GB18030**。⛔ 别用 `File.ReadAllText` —— 默认静默替换非法字节，中文老文件变乱码且无从发现。
- 🔴 装载后**不能立刻设 `ScrollViewer.Offset`**：此刻 `Extent` 是 0，赋值会被吞 ⇒ 必须 `Dispatcher.Post(…, DispatcherPriority.Loaded)`。
- 🔴 **Avalonia 12 换掉了拖放 API**：`DragEventArgs.Data` / `DataFormats.Files` 不可用；用 `e.DataTransfer.Contains(DataFormat.File)` + `e.DataTransfer.TryGetFiles()`。
- 🔴 **SharpCompress 的条目流依赖归档存活**：`OpenEntryStream()` 必须在 `using archive` 内读完；本模块统一「读进 `MemoryStream` 再关归档」，⛔ 不要把流交给上层长期持有。
- 🔴 **压缩包必须「打开一次、翻页复用」**：`ArchiveImageService.OpenSession` 返回持有文件句柄的会话，翻页只按条目名取图。⛔ 别每翻一张重开归档 —— 旧的「每张重扫一遍包头」在 2 GB 包上要等几十秒。会话必须在换文件 / 关闭时 `Dispose`。
- 🔴 **zip 只读中央目录，rar / 7z 只能顺序扫**：`ZipArchive` 构造即读完中央目录（不解压），取单张 O(1)；rar / 7z 没有等价 API，⛔ 不要试图在它们身上做随机定位。读包一律丢后台线程（`Task.Run`），UI 线程只负责解码最后一帧。
- 🔴 **解码仍在 UI 线程**：`ViewerHost.OpenImageStream` 内部 `new Bitmap(stream)` 必须留在 UI 线程；后台只做解压。
- 🔴 **`AudioPlayer` 卸载只停轮询表、⛔ 不停播**（NT-142 起改为后台播放）：切页后音乐要继续放，关标签页由 `ViewerViewModel` 释放时 `Stop`（NT-143 补）。⛔ 别在 `DetachedFromVisualTree` 里 `Stop` —— 那样切页就会断歌。
- 🔴 **轮询表必须跟着播放状态重新起**：`Stop()`（播完最后一首、关页）会 `_timer.Stop()`，重播若只调 `Playback.Play()` 而不起表，声音出来了但**进度条与歌词原地不动**。所以 `Playback` 注入时接管 `StateChanged`（切回 UI 线程）→ 变 `Playing` 就 `EnsureTimer()`；`TogglePlay` 在同一处补「位置已在末尾 ⇒ 先 `Seek(0)`」。
- 🔴 **控制条别用 emoji 码位**：`⏮ ⏭ ▶ 🔁 🔊` 在 Win10 会被 Segoe UI Emoji 接管，渲染成**彩色方块**（截图实测），主题色全失效。用几何字符（`◄◄` `►` `❚❚` `►►`）或两字文本，⛔ 靠 `FontFamily` 硬怼（跨平台不中立）。**模式键图标**（顺序 = 三个并列箭头 / 随机 = 两条交叉箭头 / 单曲 = `↻1`）用 `Path` 描边画，字符集里没有对应字形（`⇶` `⇄` 既缺字形也不是交叉），切换只切三块 `IsVisible`、⛔ 换 `Content`。
- 🔴 **歌词滚动不能用 `BringIntoView()`**：那是「一换行就把它怼到可视区」，表现是一到行首整块跳一下。正解 = 每 40 ms 算目标偏移（当前行中心 − 视口一半，行间按时间插值），且**夹在 ≥0** —— 开头几行自动变成「高亮行从上往下走，走到中线才开始滚」。**行中心必须补 `LyricPanel.Margin.Top`**：`Bounds` 是相对面板的，滚动量是相对整块内容的，漏补就整段偏上（截图实测：偏 32px）。
- 🔴 **音频解码顺序**：先 `MediaFoundationReader`（mp3/m4a/flac/wma），失败回退 `AudioFileReader`（wav/aiff）；只用后者会让 m4a/flac 打不开。
- 🔴 **NAudio 3.x 的 `IWaveProvider` 只有 `Read(Span<byte>)`**：自定义 `IWaveProvider`（频谱旁路 `SpectrumTap`）必须实现 Span 版，照 2.x 写 `Read(byte[], int, int)` 会 `CS0535`；同理⛔ 假设 `NAudio.Dsp.FastFourierTransform` 还在（FFT 已自写）。
- 🔴 **压缩包来源由宿主标注**：`ImageViewer` 只认「本地路径」与「流」，包路径 / 条目大小靠 `ViewerHost.OpenImageStream(..., size, archivePath)` → `SetArchiveSource` 传入。⛔ 别在控件里按 `!` 拆 `包!条目` —— 包路径与条目名都可能含 `!`。
- NT-134 · 播放能力走 `Shared/Services/IAudioPlayback` 注入（UI 库 ⛔ 不引 NAudio / SharpCompress / TagLib）；压缩包与标签解析在模块侧（`ArchiveImageService` / `AudioLibraryService`）；视频继续外放系统程序，⛔ 不引 libvlc。

## 6. 待办（未立项）

- **文档查看**（`viewer-text` 页的既定方向，NT-144 批 3 改名占位）：PDF（需 PDFium 系原生库，先拍板「解析/渲染引擎算后端依赖」的口径）→ EPUB → MOBI；EPUB 建议走「网络阅读」书架路线（解析成章节文本，复用排版 / 听书）。
- 与 `Module.MediaAssets` 详情卡接线：「用查看器打开」按钮。
- 视频内嵌（已明确暂不做）。
