# 模块 · 查看器（Viewer）

> **程序集**：`src/Module.Viewer`（`NewToys.Viewer`）；渲染器在 `src/UI/Viewers/`（`NewToys.UI.Viewers`）
> **模块键**：`viewer` ｜ **一级菜单**：Order 55 · 图标 `IconViewer`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：V1 已实施（NT-124）；V2 音视频待媒体后端拍板
> **最后校对**：2026-11-01（V1 落地：文本 / 图片 / 外壳 + 进度记忆；对照 `ViewerHost` / `TextViewer` / `ImageViewer` / `ViewerViewModel`）

---

## 1. 职责与边界

- **定位**：整个软件共用的「打开一个文件 → 用合适的渲染器显示」，**能力层不是业务层**。
- 🔴 **渲染器住在 `NewToys.UI/Viewers/`**（`ViewerHost` / `TextViewer` / `ImageViewer`），**不在本模块**。理由：模块禁互引，媒体资产 / 视频工具 / 将来的书源阅读都要复用同一套渲染器；留在业务模块里必然三处重写。
- 🔴 本模块只做三件事：**菜单入口 / 打开与分发 / 最近打开与阅读位置记忆**。
- ⛔ 不做：媒体库管理（归 `Module.MediaAssets`）、转码（归 `Module.VideoTools`）、编辑（一律只读）、图库收藏评分。

## 2. 当前功能（1 页）

| 菜单 Key | 标题 | Order | View | 说明 |
|---|---|---|---|---|
| `viewer-main` | 查看器 | 1 | `ViewerView` | 打开文本 / 源码 / HTML（取纯文本）/ 常见位图；拖入文件；最近打开条；记阅读位置 |

**V1 支持的类型**
- 文本：`.txt .md .markdown .log .json .xml .csv .ini .cfg .conf .cs .py .ps1 .js .ts .sql .bat .cmd .yaml .yml .toml`
- HTML：`.html .htm` —— 去标签取纯文本（⛔ 不执行脚本、⛔ 不加载远程资源）
- 图片：`.png .jpg .jpeg .bmp .gif .webp .ico`

**不支持（给明确提示，⛔ 不静默失败）**：音视频（V2）、PDF、EPUB、Office。

## 2.5 操作逻辑

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 打开文件 | 工具条「📂 打开」→ 系统文件选择器 | 渲染区换成对应渲染器；标题 = 文件名；记入最近打开 | 取消选择 = 什么都不做 |
| 拖入文件 | 把文件拖到窗口任意位置 | 同「打开文件」 | 只取**第一个**文件；多选忽略其余 |
| 点最近打开 | 空态时顶部最近条里点一条 | 直接打开该文件 | 列表已剔除磁盘上不存在的路径；上限 20 条 |
| 关闭 | 工具条「✕ 关闭」 | 回空态，保留最近打开 | 关闭前把阅读进度落盘 |
| 换文件 | 再打开另一个文件 | 先存上一个的进度，再装载新的 | 进度按「路径+大小+修改时间」分桶，⛔ 不串位 |
| 文本字号 | 工具条「A-」/「A+」 | 字号 ±1（9~40），立即重排 | 仅文本模式可用；写入 `config\viewer.json` |
| 图片缩放 | 「＋」/「－」；或 `Ctrl`+滚轮 | 缩放 5%~1600%，状态栏显示百分比 | 仅图片模式可用 |
| 图片适应窗口 | 工具条「⊞ 适应窗口」；或**双击图片** | 双击在「1:1」与「适应窗口」之间切换 | 适应窗口只缩小、不放大 |
| 图片平移 | 按住左键拖动 | 内容跟随鼠标滚动 | 仅在放大于窗口时才有可拖空间 |
| 切走页面 / 关软件 | 切标签或关窗口 | 文本阅读进度自动存盘 | 下次打开同一文件回到原位置 |

## 3. 实现逻辑

```
ViewerView（Module.Viewer）
  ├─ 工具条 / 状态栏      → VM 实现 IToolbarSource / IStatusProvider，Shell 统一渲染
  ├─ 文件选择 / 拖放      → View code-behind（VM ⛔ 不碰 StorageProvider）
  └─ 持有 ViewerHost ─────→ NewToys.UI.Viewers
                             ├─ KindOf(path) 按扩展名判定
                             ├─ TextViewer（编码嗅探 + 滚动 + 进度）
                             └─ ImageViewer（缩放 + 拖拽平移 + 双击切换）
ViewerSettingsService
  └─ IOptionsStore → config\viewer.json（fontSize / lineSpacing / pagePadding / recent / positions）
```

- **连线方式**：VM 发事件（`OpenRequested` / `CloseRequested` / `ZoomIn…` / `TypographyChanged`），View 订阅后操作控件；VM ⛔ 不 new 控件。
- 🔴 **文本是连续滚动，不是分页切页**：V1 用 `ScrollViewer` + 进度比例（0~1）。真分页要按 `TextLayout` 切行切页 + 窗口 resize 重算，风险与代码量都高，随「书源阅读器」一起做。
- 🔴 **图片缩放改 `Image.Width/Height`，不用 `RenderTransform`**：只有占位尺寸变了 `ScrollViewer.Extent` 才跟上，否则放大后拖不到边缘。
- 🔴 **阅读进度用属性变更监听（`ScrollViewer.OffsetProperty`），⛔ 不用 `ScrollChanged` 事件**：项目里踩过该事件不触发 / 时序不稳的坑（同 `SettingsSectionHost`）。

## 4. 数据与配置

| 项 | 落点 | 写入点 |
|---|---|---|
| 排版参数（`fontSize` / `lineSpacing` / `pagePadding`） | `config\viewer.json` | `ViewerSettingsService`（唯一） |
| 最近打开（`recent`，≤20） | 同上 | 同上 |
| 阅读位置（`positions`，≤500 条） | 同上 | 同上 |
| 打开的源文件 | **只读**，⛔ 不写回 | — |

- 进度键 = `完整路径|字节数|最后修改时间(ticks)`；文件不存在 ⇒ 不记（避免死记录堆积）。

## 5. 坑点（实测）

- 🔴 **`ViewerHost` 不能放在 `Module.Viewer` 里**：媒体资产详情卡要「用查看器打开」时会跨模块，模块禁互引 ⇒ 必须落 `NewToys.UI`，否则只能经 `IModuleBus` 跳页，体验割裂。
- 🔴 **`Avalonia.Media.Imaging.Bitmap` 没有「限定像素尺寸」的公开重载** ⇒ 无法解码期降采样；超大图首帧会卡一下，V1 接受（要优化就后台线程解码小图再换源）。
- 🔴 文本编码：先认 BOM，再试**严格 UTF-8**，失败回落 **GB18030**（中文老 TXT 常见）。⛔ 别直接用 `File.ReadAllText` —— 默认会静默替换非法字节，中文老文件变乱码且无从发现。
- 🔴 装载后**不能立刻设 `ScrollViewer.Offset`**：此刻 `Extent` 还是 0，赋值会被吞掉 ⇒ 必须 `Dispatcher.Post(…, DispatcherPriority.Loaded)`。
- 🔴 **Avalonia 12 换掉了拖放 API**：`DragEventArgs.Data` / `DataFormats.Files` / `e.Data.GetFiles()` **全部不可用**（`DataFormats` 已标 `[Obsolete]`）。新写法：`e.DataTransfer.Contains(DataFormat.File)` 判类型、`e.DataTransfer.TryGetFiles()` 取文件。

## 6. 待办（未立项）

- **V2 音视频**：待媒体后端拍板（LibVLCSharp / WMF / 抽帧降级）—— 需求 §6.9.3。
- **真分页排版**：随书源阅读器一并做；届时 `TextViewer` 按「章节文本 + 排版参数 → 页」接口改造，⛔ 不要另写一套。
- PDF / EPUB / 字幕 / 幻灯片：各自需第三方库，单独评估。
- 与 `Module.MediaAssets` 详情卡接线：「用查看器打开」按钮（V1 已具备复用条件，未接）。
