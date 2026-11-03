# 模块 · 查看器（Viewer）

> **程序集**：`src/Module.Viewer`（`NewToys.Viewer`）；渲染器 / 阅读器 / 播放器在 `src/UI/`（`NewToys.UI.Viewers` / `NewToys.UI.Readers`）
> **模块键**：`viewer` ｜ **一级菜单**：Order 55 · 图标 `IconViewer`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：V1（文本 / 图片）+ V2（文本阅读器 / 压缩包内图片 / 本地音乐播放器）已验收；NT-142（后台播放 + 界面重做）、NT-143（频谱 + 封面歌词关联 + 会话续播）已验收；视频走系统播放器
> **最后校对**：2026-11-03（播放器控制条图标化 / 播放列表改右侧浮层 / 修播完重播进度不动）

---

## 1. 职责与边界

- **定位**：整个软件共用的「打开一个文件 → 用合适的渲染器显示 / 播放」，**能力层不是业务层**。
- 🔴 **渲染器住在 `NewToys.UI`**（`Viewers/` = 短文本 / 图片 / 音频外壳，`Readers/` = 长文本阅读器壳），**不在本模块**。理由：模块禁互引，媒体资产 / 视频工具 / 书源阅读都要复用同一套渲染器。
- 🔴 本模块只做四件事：**入口与分派 / 章节切分 / 压缩包遍历 / 设置与位置记忆**。
- ⛔ 不做：媒体库管理（归 `Module.MediaAssets`）、转码（归 `Module.VideoTools`）、标签写回、编辑（一律只读）、图库收藏评分。
- ⛔ **不做视频内嵌播放**（2026-11-02 用户定）：视频调起系统默认程序。

## 2. 当前功能（3 页）

| 菜单 Key | 标题 | Order | View / VM | 说明 |
|---|---|---|---|---|
| `viewer-text` | 文本阅读 | 1 | `ViewerTextView` / `ViewerTextViewModel` | txt / md / 源码；长文本自动进阅读器；章节规则浮层 |
| `viewer-image` | 图片查看 | 2 | `ViewerImageView` / `ViewerImageViewModel` | 图片与压缩包内图片；缩放 / 适应窗口 / 逐张翻页 |
| `viewer-audio` | 音乐播放 | 3 | `ViewerAudioView` / `ViewerAudioViewModel` | 本地音频播放列表；播放/暂停、歌词、顺序/单曲/随机 |

- 三页**共用**一套页面逻辑（`Views.ViewerPageBase`）与**同一份 XAML 内容**（只有 `x:Class` 不同）。
- 页面范围由 `ViewerViewModel.Scope` 决定：`Text` 收 txt / md / 源码（含长文本），`Image` 收图片与压缩包，`Audio` 收音频。
- 工具条按范围裁剪：文本页 ⛔ 无缩放 / 翻张 / 播放；图片页 ⛔ 无字号 / 章节规则；音频页 ⛔ 无字号 / 缩放 / 章节规则。
- 最近打开按本页范围过滤显示（同一份 `viewer.json`，三页各显各的）。
- ⛔ 旧统一页 `viewer-main` 已撤销（用户定：三页取代统一页）。

**支持的类型**

| 类别 | 扩展名 | 能力 |
|---|---|---|
| 长文本阅读器 | `.txt .md .markdown .log` | 章节自动识别 + 目录抽屉 + 上一/下一章 + 章内进度 + **书签** + **全书搜索** + 排版（字号 / 行距 / 边距 / 缩进 / 段距 / 4 档配色）+ 快捷键 |
| 短文本 / 源码 | `.json .xml .csv .ini .cfg .conf .cs .py .ps1 .js .ts .sql .bat .cmd .yaml .yml .toml` | 整篇滚动 + 字号 / 行距 / 边距 |
| HTML | `.html .htm` | 去标签取纯文本（⛔ 不执行脚本、⛔ 不加载远程资源） |
| 图片 | `.png .jpg .jpeg .bmp .gif .webp .ico` | 缩放 / 平移 / **旋转 90°** / 双击切 1:1 与适应窗口；**同目录逐张翻页**（←/→）；**右键图片信息**；**分屏：单张 / 左右 / 上下 / 2×2**（翻页换整屏） |
| 压缩包内图片 | `.zip .rar .7z .cbz .cbr` | 列出包内图片 → 顶部条目条点选 → ←/→ 翻页；只列图片、⛔ 不解压到磁盘；**zip / cbz 只读中央目录**（2 GB 包也秒开）；右键图片信息显示包路径 + 包内路径；同样支持分屏网格 |
| 音乐 | `.mp3 .flac .wav .m4a .aac .wma .ogg .opus .aiff` | 播放列表（**含 m3u / m3u8 导入**）+ 播放/暂停 + 进度 + 音量 + 上一首/下一首 + 顺序/单曲/随机 + 标签封面 + 同名 `.lrc` 歌词（**支持译文双语**）+ **播放列表记忆**（关掉再开能续上，⛔ 不自动播）|
| 视频 | `.mp4 .mkv .avi …` | **不在本模块**：由系统默认播放器打开 |

**不支持（给明确提示，⛔ 不静默失败）**：PDF、EPUB、Office；加密 / 分卷压缩包。

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
| **阅读器：目录** | 阅读器顶栏「目录」（Ctrl+T） | 左侧抽屉列章节；过滤框按名筛 | 点一章即跳；当前章高亮 |
| **阅读器：翻章** | 「上一章 / 下一章」或 ←/→ | 切章并回到章首 | 到头提示「已经是最后一章」，⛔ 不越界 |
| **阅读器：进度** | 滚动正文 | 每 250ms 最多回写一次位置 | 位置 = 章序号 + 章内比例 |
| **阅读器：书签** | 顶栏「书签」→「加书签」 | 记「章 + 章内比例 + 正文预览」并立即落盘 | 按文件全路径存；点书签项直接跳过去 |
| **阅读器：搜索** | 顶栏「搜索」或 Ctrl+F | 全书搜索（≤500 条），点结果跳章并加粗定位 | 面板底部显示「第 N / M 处（本章）」 |
| **阅读器：排版** | 顶栏「排版」 | 字号 12~34 / 行距 1.2~2.6 / 边距 0~160 / 首行缩进 / 段间距 / 4 档配色 | 改完立即生效并写 `viewer.json` |
| **章节规则** | 工具条「⚙ 章节规则」 | 右侧浮层：自定义正则列表 + 增删 + **试跑** | 自定义规则排在内置规则之前；试跑只读不落盘；保存后**下次打开**生效 |
| **图片翻页** | 点顶部条目条 / 工具条「←」「→」/ ←→ | 换成同一批里的那张图 | 工具条只放箭头图标（`←` `→`，⛔ 不用播放键那种实心三角，⛔ 不带文字，说明靠悬停提示）；来源三种：**同目录图片**（按文件名排序，自动带出）、**压缩包内图片**、**外部投递的一批图**（`ModuleTopics.ImageOpen` ← 媒体资产「标签详情卡 → 🖼 图片」：翻页集**就是**这批图，⛔ 不再扫同目录混进没被选中的图）；序号显示「第 N / M 张」（分屏时「第 a–b / M 张」）；只有 1 张时⛔ 不显示条带 |
| **大压缩包** | 打开 `.zip` / `.cbz` | 状态栏先报「正在读取压缩包…」，读到后列出条目并显示第一张 | zip 家族只读**中央目录**（不解压任何数据）；rar / 7z 无等价 API，仍按条目顺序扫 |
| 音乐播放 | 打开音乐 / 拖入多首 / 拖入 m3u·m3u8 | 自动播第一首；底部条控制 | 🔴 **打开单首时会先问一句「把同一文件夹里另外 N 首也加进播放列表？」**（`DialogHooks.ConfirmAsync`，同目录音频按文件名排序并入，当前曲目定为起始项；选取消就只放这一首；⛔ 拖入多首 / 走「媒体资产 → ▶ 播放」/ m3u 这条路不问）；🔴 **切走页面 ⛔ 不停播**（`AudioPlayer` 卸载时只停轮询表、不 Stop），切回来界面照旧刷新、播完照常续播；列表重建与「续播」互不打架：`RestoreAudioPlaylist` 见到 `Host.Audio` 有待播内容且后端非 `Stopped` 就直接返回，⛔ 不重建列表；真正换文件（`Open` 打开非音频）前才 `Host.Audio.Stop()`；播放列表文件先展开成音频列表，路径失效的条目跳过；**关闭标签页即停播**（`ViewerViewModel.Dispose` → 先存续播快照再 `Stop`；文本 / 图片页的 VM 被关掉时⛔ 不掐音乐），重开音频页回到同一首 + 同一位置 + 同一模式与音量（**暂停态**，⛔ 自动播） |
| 播放器界面 | — | 顶栏「曲名 / 艺术家·专辑 / 技术信息」+ 左侧大封面（260×260）+ 右侧**居中歌词**（当前行加粗高亮，走到中线后跟随滚动）+ 底部控制条（上一首 / 播放 / 下一首 / 进度 / 时间 / 模式 / 音色 / 音量）；**播放列表 = 右侧悬浮层**（顶栏「☰ 列表」开合，默认收起，点条目切歌，层内 ✕ 收起） | 底栏压到 **22px 按钮 + 3px 内边距**（⛔ 再撑高）；按钮**一律纯图标**（`◄◄` `►`/`❚❚` `►►`，⛔ 带文字）并显式 `Horizontal/VerticalContentAlignment=Center`，含义进 `ToolTip` + `AutomationProperties.Name`；🔴 **图标只用非 emoji 码位** —— `⏮ ⏭ ▶ ⏸ 🔁` 会被 Segoe UI Emoji 接管渲染成彩色方块（与主题冲突）；模式 / 音色 / 音量键用两字文本（顺序 / 单曲 / 随机、音色名、音量 / 静音）；技术信息 = `格式 · 采样率 · 码率 · 声道`（TagLibSharp 读 `TagLib.Properties`，读不到只留扩展名）；歌词行字号 14 / 译文 12，居中排版；封面下方 **260×48 分频柱**（32 段竖条，对数分频 60 Hz~16 kHz，33 ms 刷新）：只在播放时出现，暂停 / 停止 / 后端不支持频谱即整块收起（⛔ 留空框）；封面取不到时显示音符占位（⛔ 空白边框） |
| 音量 / 静音 | 点底部「音量」键 | 一下静音（音量滑到 0），再一下回到**静音前**的音量 | 静音态键面变「静音」、提示变「恢复上次音量」；拖滑杆离开 0 即视为取消静音 |
| 音色 | 点底部音色键循环切换 | 关闭 → 低音增强 → 流行 → 人声 → 古典 → 关闭 | 能力 != 契约：后端实现 `IAudioTone` 才出现该键（`NullAudioPlayback` 不实现 ⇒ 自动隐藏）；播放中即时生效（滤波系数整体替换），⛔ 需重开文件；曲线 = 级联 biquad（RBJ：LowShelf / Peaking / HighShelf），⛔ 依赖 `NAudio.Dsp` |
| 歌词滚动 | 随播放自动 | **当前行落在视口垂直中线**，行与行之间按播放时间线性插值滚动（40 ms 一帧） | 🔴 偏移夹在 `≥0`：开头几行没排满一屏时列表不动，高亮行从上往下逐行走，走到中线后列表才开始滚；⛔ 用 `BringIntoView()`（一到行首就跳居中，顿挫得像卡死）；暂停 / 停止即停表 |
| 音频模式 | 底部模式键（文本循环切换） | 播完按模式决定下一首 | 「顺序」到末尾停止并提示；键面 = 顺序 / 单曲 / 随机，当前模式进 `ToolTip` 与状态栏 |
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
  └─ 持有 ViewerHost ──────→ NewToys.UI.Viewers
                               ├─ KindOf(path) 扩展名分派
                               ├─ TextViewer（短文本）
                               ├─ ImageViewer（图片 / 压缩包内图片，支持流加载；自带右键菜单 + 信息浮层）
                               ├─ AudioPlayer（播放列表 + 控制条 + 歌词）
                               └─ ReaderShell（长文本，来自 NewToys.UI.Readers）
TxtBookService        编码嗅探 + 章节切分 + 规则试跑
ArchiveImageService   打开压缩包会话（zip 走 ZipArchive 中央目录，rar/7z 走 SharpCompress），条目读进内存流
AudioLibraryService   TagLib 读标签 / 封面 / 同名 lrc
NAudioPlayback        IAudioPlayback 的 Windows 实现（MF 解码 + WaveOut 输出）
ViewerSettingsService config\viewer.json 唯一写入点
```

- **连线方式**：VM 发事件（`OpenRequested`（载荷 `IReadOnlyList<string>?`，`null` = 弹选择器）/ `ArchiveNavigateRequested` / `PlayToggleRequested` / `RulesEditRequested` / `ReaderOptionsChanged`），View 订阅后操作控件；VM ⛔ 不 new 控件。
- 🔴 **跨页跳转 = `ModuleBus` 投递 + `NavigateTo`**：`ViewerOpenRequest` 带目标 `Scope`，收到请求的页先比对范围，不匹配直接忽略（三页共用一个主题，靠载荷分流）。投递前无订阅者则进信箱，目标页构造时 `TakeAll` 取走 ⇒ 两条路径都不丢。
- 🔴 **长文本按「章」渲染，不整本塞进视觉树**：章节是整本书字符串上的区间（`Start/Length/BodyStart`），切章只重建当前章的行内元素。
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
| 长文本排版（`readerFontSize` / `readerLineSpacing` / `readerPadding` / `readerIndent` / `readerSpacing` / `readerTheme`） | 同上 | 同上 |
| 长文本位置（`readerPositions`：章序号 + 章内比例，≤500） | 同上 | 同上 |
| 书签（`bookmarks`：按文件全路径分组） | 同上 | 同上 |
| 自定义章节正则（`txtRules`） | 同上 | 同上 |
| 播放音量（`audioVolume`，0~100） | 同上 | 同上 |
| 打开的源文件 | **只读**，⛔ 不写回 | — |

- 短文本进度键 = `完整路径|字节数|最后修改时间(ticks)`（文件变了就重置）。
- 书签键 = **文件全路径**（文件被替换仍属同本书，⛔ 不用时间戳）。

## 5. 坑点（实测）

- 🔴 **三页 XAML 必须是同一份内容**：`ViewerPageBase` 按 `x:Name`（`Host` / `RecentList` / `ArchiveList` / `SectionBox` / `SplitGrid` / `HistoryPanel` / `RulePanel` / `RuleList` / `RuleNameBox` / `RulePatternBox` / `SampleBox` / `TestResult`）取控件；少一个就抛「ViewerPage 的 XAML 缺少名为 X 的控件」。改一页必须同步另两页。
- 🔴 **XAML 里引用的事件处理必须是 `protected`**：处理器写在基类，XAML 编译生成的调用落在子类 partial 里，`private` 编译不过（`OnAddRule` / `OnRemoveRule` / `OnCloseRules` / `OnTestRules` / `OnArchiveSelected` / `OnSectionSelected` / `OnRecentSelected`）。
- 🔴 **条目条里的 `ComboBox` 必须显式 `Height="22" MinHeight="0"`**：隐式样式 `MinHeight=28` 会把 22px 的条撑高，条带跟着变胖。
- 🔴 **`ViewerHost` 不能放在 `Module.Viewer` 里**：媒体资产详情卡要「用查看器打开」时会跨模块，模块禁互引 ⇒ 必须落 `NewToys.UI`。
- 🔴 **降采样解码必须先探测原始尺寸**：`Bitmap.DecodeToWidth/Height(stream, n)` 是解码期缩放（省内存的唯一手段），但**目标尺寸大于原图时会放大** ⇒ 小图反而多吃内存。所以先走 `ImageHeader.TryReadSize`（只读文件头，png / jpeg / gif / bmp / webp）拿原始宽高，超限才降采样；探测不出来就整图解码。
- 🔴 **解码边长上限 = 视口物理像素宽 × 2**（夹 `1200 ~ 4096`；分屏每格 `480 ~ 2048`）。超过屏幕精度的像素只是白占内存（漫画包四格全尺寸能吃掉几百 MB）。信息卡里「尺寸」仍报**原始**像素，「解码尺寸」另起一行说明降过采样。
- 🔴 **换图那一帧必须先把滚动条置 `Disabled`**：新图 `Extent` 未稳定时 `Auto` 会让竖直滚动条闪一下；铺好（`DispatcherPriority.Loaded`）再恢复 `Auto`。回调自带 `_loadToken`，连按翻页时旧回调直接丢弃。

- 🔴 文本编码：先认 BOM，再试**严格 UTF-8**，失败回落 **GB18030**。⛔ 别用 `File.ReadAllText` —— 默认静默替换非法字节，中文老文件变乱码且无从发现。
- 🔴 装载后**不能立刻设 `ScrollViewer.Offset`**：此刻 `Extent` 是 0，赋值会被吞 ⇒ 必须 `Dispatcher.Post(…, DispatcherPriority.Loaded)`。
- 🔴 **Avalonia 12 换掉了拖放 API**：`DragEventArgs.Data` / `DataFormats.Files` 不可用；用 `e.DataTransfer.Contains(DataFormat.File)` + `e.DataTransfer.TryGetFiles()`。
- 🔴 **SharpCompress 的条目流依赖归档存活**：`OpenEntryStream()` 必须在 `using archive` 内读完；本模块统一「读进 `MemoryStream` 再关归档」，⛔ 不要把流交给上层长期持有。
- 🔴 **压缩包必须「打开一次、翻页复用」**：`ArchiveImageService.OpenSession` 返回持有文件句柄的会话，翻页只按条目名取图。⛔ 别每翻一张重开归档 —— 旧的「每张重扫一遍包头」在 2 GB 包上要等几十秒。会话必须在换文件 / 关闭时 `Dispose`。
- 🔴 **zip 只读中央目录，rar / 7z 只能顺序扫**：`ZipArchive` 构造即读完中央目录（不解压），取单张 O(1)；rar / 7z 没有等价 API，⛔ 不要试图在它们身上做随机定位。读包一律丢后台线程（`Task.Run`），UI 线程只负责解码最后一帧。
- 🔴 **解码仍在 UI 线程**：`ViewerHost.OpenImageStream` 内部 `new Bitmap(stream)` 必须留在 UI 线程；后台只做解压。
- 🔴 **章节切片必须用绝对偏移**：`BuildChapters` 产出整本书坐标，`Substring` 时⛔ 不能按「章内偏移」二次计算，否则目录跳转全错位。
- 🔴 **阅读器滚动定位是近似的**：搜索命中只能按「命中位置 / 章长 × 可滚动高度」估算，无法精确到像素（Avalonia 无公开的文本命中矩形 API）。
- 🔴 **`AudioPlayer` 卸载只停轮询表、⛔ 不停播**（NT-142 起改为后台播放）：切页后音乐要继续放，关标签页由 `ViewerViewModel` 释放时 `Stop`（NT-143 补）。⛔ 别在 `DetachedFromVisualTree` 里 `Stop` —— 那样切页就会断歌。
- 🔴 **轮询表必须跟着播放状态重新起**：`Stop()`（播完最后一首、关页）会 `_timer.Stop()`，重播若只调 `Playback.Play()` 而不起表，声音出来了但**进度条与歌词原地不动**。所以 `Playback` 注入时接管 `StateChanged`（切回 UI 线程）→ 变 `Playing` 就 `EnsureTimer()`；`TogglePlay` 在同一处补「位置已在末尾 ⇒ 先 `Seek(0)`」。
- 🔴 **控制条别用 emoji 码位**：`⏮ ⏭ ▶ 🔁 🔊` 在 Win10 会被 Segoe UI Emoji 接管，渲染成**彩色方块**（截图实测），主题色全失效。用几何字符（`◄◄` `►` `❚❚` `►►`）或两字文本，⛔ 靠 `FontFamily` 硬怼（跨平台不中立）。
- 🔴 **歌词滚动不能用 `BringIntoView()`**：那是「一换行就把它怼到可视区」，表现是一到行首整块跳一下。正解 = 每 40 ms 算目标偏移（当前行中心 − 视口一半，行间按时间插值），且**夹在 ≥0** —— 开头几行自动变成「高亮行从上往下走，走到中线才开始滚」。**行中心必须补 `LyricPanel.Margin.Top`**：`Bounds` 是相对面板的，滚动量是相对整块内容的，漏补就整段偏上（截图实测：偏 32px）。
- 🔴 **音频解码顺序**：先 `MediaFoundationReader`（mp3/m4a/flac/wma），失败回退 `AudioFileReader`（wav/aiff）；只用后者会让 m4a/flac 打不开。
- 🔴 **NAudio 3.x 的 `IWaveProvider` 只有 `Read(Span<byte>)`**：自定义 `IWaveProvider`（频谱旁路 `SpectrumTap`）必须实现 Span 版，照 2.x 写 `Read(byte[], int, int)` 会 `CS0535`；同理⛔ 假设 `NAudio.Dsp.FastFourierTransform` 还在（FFT 已自写）。
- 🔴 **压缩包来源由宿主标注**：`ImageViewer` 只认「本地路径」与「流」，包路径 / 条目大小靠 `ViewerHost.OpenImageStream(..., size, archivePath)` → `SetArchiveSource` 传入。⛔ 别在控件里按 `!` 拆 `包!条目` —— 包路径与条目名都可能含 `!`。

## 6. 待办（未立项）

- PDF 查看（需 PDFium 系原生库，单独评估）。
- EPUB（结构解析可复用 `ReaderShell`；需定 `VersOne.Epub` 或自解析 zip + AngleSharp）。
- 与 `Module.MediaAssets` 详情卡接线：「用查看器打开」按钮。
- 真分页排版（当前是连续滚动）；随书源阅读器一并做。
- 视频内嵌（已明确暂不做）。
