# 模块 · 查看器（Viewer）

> **程序集**：`src/Module.Viewer`（`NewToys.Viewer`）；渲染器 / 阅读器 / 播放器在 `src/UI/`（`NewToys.UI.Viewers` / `NewToys.UI.Readers`）
> **模块键**：`viewer` ｜ **一级菜单**：Order 55 · 图标 `IconViewer`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：V1（文本 / 图片）已验收；**V2 已实施待 build** —— 文本阅读器（目录 / 书签 / 全书搜索 / 自定义章节正则）+ 压缩包内图片 + 本地音乐播放器；视频走系统播放器
> **最后校对**：2026-11-02（V2：三类能力落地）

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
| `viewer-audio` | 音频播放 | 3 | `ViewerAudioView` / `ViewerAudioViewModel` | 本地音频播放列表；播放/暂停、歌词、顺序/单曲/随机 |

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
| 图片 | `.png .jpg .jpeg .bmp .gif .webp .ico` | 缩放 / 平移 / 双击切 1:1 与适应窗口；**同目录逐张翻页**（←/→） |
| 压缩包内图片 | `.zip .rar .7z .cbz .cbr` | 列出包内图片 → 顶部条目条点选 → ←/→ 翻页；只列图片、⛔ 不解压到磁盘；**zip / cbz 只读中央目录**（2 GB 包也秒开） |
| 音频 | `.mp3 .flac .wav .m4a .aac .wma .ogg .opus .aiff` | 播放列表 + 播放/暂停 + 进度 + 音量 + 上一首/下一首 + 顺序/单曲/随机 + 标签封面 + 同名 `.lrc` 歌词 |
| 视频 | `.mp4 .mkv .avi …` | **不在本模块**：由系统默认播放器打开 |

**不支持（给明确提示，⛔ 不静默失败）**：PDF、EPUB、Office；加密 / 分卷压缩包。

## 2.5 操作逻辑

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| **跨页自动跳转** | 打开 / 拖入 / 点最近打开的文件不属于本页 | 投递给目标页并切过去，目标页当场打开该文件 | 投递走 `ModuleBus`（主题 `viewer.openfiles`，载荷 `ViewerOpenRequest`）；本页 ⛔ 不装载、⛔ 不报错 |
| 打开文件 | 工具条「📂 打开」 | 本页范围内：阅读器 / 图片 / 压缩包 / 音频 / 短文本 | 取消 = 什么都不做；可多选，**多选且全是音频**才整批进播放列表 |
| 拖入文件 | 拖到窗口任意位置 | 同上 | 多文件非全音频时只开第一个 |
| 最近打开 | 空态顶部点一条 | 直接打开（不属于本页则转去对应页） | 已剔除不存在的路径；上限 20；按本页范围过滤显示 |
| 关闭 | 工具条「✕ 关闭」 | 回空态；先存进度 | 音频同时停止 |
| **阅读器：目录** | 阅读器顶栏「目录」（Ctrl+T） | 左侧抽屉列章节；过滤框按名筛 | 点一章即跳；当前章高亮 |
| **阅读器：翻章** | 「上一章 / 下一章」或 ←/→ | 切章并回到章首 | 到头提示「已经是最后一章」，⛔ 不越界 |
| **阅读器：进度** | 滚动正文 | 每 250ms 最多回写一次位置 | 位置 = 章序号 + 章内比例 |
| **阅读器：书签** | 顶栏「书签」→「加书签」 | 记「章 + 章内比例 + 正文预览」并立即落盘 | 按文件全路径存；点书签项直接跳过去 |
| **阅读器：搜索** | 顶栏「搜索」或 Ctrl+F | 全书搜索（≤500 条），点结果跳章并加粗定位 | 面板底部显示「第 N / M 处（本章）」 |
| **阅读器：排版** | 顶栏「排版」 | 字号 12~34 / 行距 1.2~2.6 / 边距 0~160 / 首行缩进 / 段间距 / 4 档配色 | 改完立即生效并写 `viewer.json` |
| **章节规则** | 工具条「⚙ 章节规则」 | 右侧浮层：自定义正则列表 + 增删 + **试跑** | 自定义规则排在内置规则之前；试跑只读不落盘；保存后**下次打开**生效 |
| **图片翻页** | 点顶部条目条 / 工具条「◀ 上一张」「下一张 ▶」/ ←→ | 换成同一批里的那张图 | 来源两种：**同目录图片**（按文件名排序，自动带出）或**压缩包内图片**；序号显示「第 N / M 张」；只有 1 张时⛔ 不显示条带 |
| **大压缩包** | 打开 `.zip` / `.cbz` | 状态栏先报「正在读取压缩包…」，读到后列出条目并显示第一张 | zip 家族只读**中央目录**（不解压任何数据）；rar / 7z 无等价 API，仍按条目顺序扫 |
| 音频播放 | 打开音频或拖入多首 | 自动播第一首；底部条控制 | 切走页面即停播（⛔ 不留后台声音） |
| 音频模式 | 底部「顺序 / 单曲 / 随机」 | 播完按模式决定下一首 | 「顺序」到末尾停止并提示 |
| 音频歌词 | 同名 `.lrc` 存在时自动加载 | 当前行高亮 + 自动滚动 | 只认本地 `.lrc`，⛔ 不联网下载 |
| 图片缩放 | 「＋」/「－」/ Ctrl+滚轮 / 双击 | 5%~1600%，状态栏显示百分比 | 适应窗口只缩小不放大 |
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
                               ├─ ImageViewer（图片 / 压缩包内图片，支持流加载）
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

- 🔴 **三页 XAML 必须是同一份内容**：`ViewerPageBase` 按 `x:Name`（`Host` / `RecentList` / `ArchiveList` / `RulePanel` / `RuleList` / `RuleNameBox` / `RulePatternBox` / `SampleBox` / `TestResult`）取控件；少一个就抛「ViewerPage 的 XAML 缺少名为 X 的控件」。改一页必须同步另两页。
- 🔴 **XAML 里引用的事件处理必须是 `protected`**：处理器写在基类，XAML 编译生成的调用落在子类 partial 里，`private` 编译不过（`OnAddRule` / `OnRemoveRule` / `OnCloseRules` / `OnTestRules` / `OnArchiveSelected` / `OnRecentSelected`）。
- 🔴 **`ViewerHost` 不能放在 `Module.Viewer` 里**：媒体资产详情卡要「用查看器打开」时会跨模块，模块禁互引 ⇒ 必须落 `NewToys.UI`。
- 🔴 **`Avalonia.Media.Imaging.Bitmap` 没有「限定像素尺寸」的公开重载** ⇒ 无法解码期降采样；超大图首帧会卡一下（要优化就后台解码小图再换源）。
- 🔴 文本编码：先认 BOM，再试**严格 UTF-8**，失败回落 **GB18030**。⛔ 别用 `File.ReadAllText` —— 默认静默替换非法字节，中文老文件变乱码且无从发现。
- 🔴 装载后**不能立刻设 `ScrollViewer.Offset`**：此刻 `Extent` 是 0，赋值会被吞 ⇒ 必须 `Dispatcher.Post(…, DispatcherPriority.Loaded)`。
- 🔴 **Avalonia 12 换掉了拖放 API**：`DragEventArgs.Data` / `DataFormats.Files` 不可用；用 `e.DataTransfer.Contains(DataFormat.File)` + `e.DataTransfer.TryGetFiles()`。
- 🔴 **SharpCompress 的条目流依赖归档存活**：`OpenEntryStream()` 必须在 `using archive` 内读完；本模块统一「读进 `MemoryStream` 再关归档」，⛔ 不要把流交给上层长期持有。
- 🔴 **压缩包必须「打开一次、翻页复用」**：`ArchiveImageService.OpenSession` 返回持有文件句柄的会话，翻页只按条目名取图。⛔ 别每翻一张重开归档 —— 旧的「每张重扫一遍包头」在 2 GB 包上要等几十秒。会话必须在换文件 / 关闭时 `Dispose`。
- 🔴 **zip 只读中央目录，rar / 7z 只能顺序扫**：`ZipArchive` 构造即读完中央目录（不解压），取单张 O(1)；rar / 7z 没有等价 API，⛔ 不要试图在它们身上做随机定位。读包一律丢后台线程（`Task.Run`），UI 线程只负责解码最后一帧。
- 🔴 **解码仍在 UI 线程**：`ViewerHost.OpenImageStream` 内部 `new Bitmap(stream)` 必须留在 UI 线程；后台只做解压。
- 🔴 **章节切片必须用绝对偏移**：`BuildChapters` 产出整本书坐标，`Substring` 时⛔ 不能按「章内偏移」二次计算，否则目录跳转全错位。
- 🔴 **阅读器滚动定位是近似的**：搜索命中只能按「命中位置 / 章长 × 可滚动高度」估算，无法精确到像素（Avalonia 无公开的文本命中矩形 API）。
- 🔴 **`AudioPlayer` 必须在 `DetachedFromVisualTree` 停表停播**，否则切页后音乐继续放。
- 🔴 **音频解码顺序**：先 `MediaFoundationReader`（mp3/m4a/flac/wma），失败回退 `AudioFileReader`（wav/aiff）；只用后者会让 m4a/flac 打不开。

## 6. 待办（未立项）

- PDF 查看（需 PDFium 系原生库，单独评估）。
- EPUB（结构解析可复用 `ReaderShell`；需定 `VersOne.Epub` 或自解析 zip + AngleSharp）。
- 与 `Module.MediaAssets` 详情卡接线：「用查看器打开」按钮。
- 真分页排版（当前是连续滚动）；随书源阅读器一并做。
- 视频内嵌（已明确暂不做）。
