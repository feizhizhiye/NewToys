# NewToys 机器事实（AUTO）

> 本文件全部内容由 `python skill/scripts/docs-sync.py --fix` 从 `src/` 与脚本自身生成。
> ⛔ 块内不手改；需要人工判断 / 状态 / 原因的内容写进对应的权威文档，不写在这里。
> 事实源唯一：`src/` 源码 + `skill/scripts/{lint,accept,docs-sync}.py`。冲突以源码为准。

## 一、包版本

<!-- AUTO:facts.versions -->
| 组件 | 版本 |
|---|---|
| `.NET` | `net10.0` |
| `AngleSharp` | `1.9.1` |
| `Avalonia` | `12.1.5` |
| `Avalonia.Desktop` | `12.1.5` |
| `Avalonia.Fonts.Inter` | `12.1.5` |
| `Avalonia.Themes.Fluent` | `12.1.5` |
| `CommunityToolkit.Mvvm` | `8.4.2` |
| `Jint` | `4.17.0` |
| `MailKit` | `4.18.2` |
| `Microsoft.Data.Sqlite` | `10.0.14` |
| `Microsoft.Extensions.Configuration` | `10.0.14` |
| `Microsoft.Extensions.Configuration.Json` | `10.0.14` |
| `Microsoft.Extensions.DependencyInjection` | `10.0.14` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.14` |
| `Microsoft.Extensions.Logging` | `10.0.14` |
| `Microsoft.Extensions.Logging.Console` | `10.0.14` |
| `Microsoft.Extensions.Options` | `10.0.14` |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | `10.0.14` |
| `Microsoft.NET.Test.Sdk` | `18.11.0` |
| `NAudio` | `3.3.0` |
| `SharpCompress` | `1.0.0` |
| `TagLibSharp` | `2.3.0` |
| `xunit` | `2.9.3` |
| `xunit.runner.visualstudio` | `3.1.5` |
<!-- /AUTO:facts.versions -->

## 二、区域档位与相邻对

`RegionLayers` / `RegionAdjacency` 的唯一源是 `src/Shell/App.Theme.cs`。档位分配与色差检查规则见 `ui.md` §3 / §13。

<!-- AUTO:facts.regions -->
| 区域键 | 档位 |
|---|---|
| `SiderBrush` | 1 |
| `SiderFootBrush` | 1 |
| `StatusBarBrush` | 1 |
| `TitleBarBrush` | 1 |
| `WindowBackdropBrush` | 1 |
| `ContentBareBrush` | 2 |
| `ContentBrush` | 2 |
| `DetailCardBrush` | 2 |
| `FooterBarBrush` | 2 |
| `ListHeaderBrush` | 2 |
| `ToolbarBrush` | 2 |
| `ListBodyBrush` | 3 |

| 相邻 A | 相邻 B |
|---|---|
| `TitleBarBrush` | `SiderBrush` |
| `TitleBarBrush` | `ToolbarBrush` |
| `TitleBarBrush` | `ContentBareBrush` |
| `TitleBarBrush` | `ListHeaderBrush` |
| `SiderBrush` | `SiderFootBrush` |
| `SiderBrush` | `ContentBrush` |
| `SiderBrush` | `ListBodyBrush` |
| `SiderBrush` | `ListHeaderBrush` |
| `SiderFootBrush` | `StatusBarBrush` |
| `SiderFootBrush` | `FooterBarBrush` |
| `ToolbarBrush` | `ListHeaderBrush` |
| `ToolbarBrush` | `ListBodyBrush` |
| `ToolbarBrush` | `ContentBrush` |
| `ListHeaderBrush` | `ListBodyBrush` |
| `ListHeaderBrush` | `ContentBrush` |
| `ListHeaderBrush` | `StatusBarBrush` |
| `ListBodyBrush` | `FooterBarBrush` |
| `ListBodyBrush` | `StatusBarBrush` |
| `ListBodyBrush` | `DetailCardBrush` |
| `ContentBrush` | `FooterBarBrush` |
| `ContentBrush` | `StatusBarBrush` |
| `ContentBrush` | `DetailCardBrush` |
| `ContentBareBrush` | `StatusBarBrush` |
| `ContentBareBrush` | `DetailCardBrush` |
| `FooterBarBrush` | `StatusBarBrush` |
| `ToolbarBrush` | `DetailCardBrush` |
| `ListHeaderBrush` | `DetailCardBrush` |
| `FooterBarBrush` | `DetailCardBrush` |
| `DetailCardBrush` | `StatusBarBrush` |
| `WindowBackdropBrush` | `ContentBrush` |
<!-- /AUTO:facts.regions -->

## 三、主题刷子键

<!-- AUTO:facts.brushes -->
`AccentBrush`、`AccentHoverBrush`、`AccentInkBrush`、`AccentTextBrush`、`ButtonBorderBrush`、`CardHoverBrush`、`CardSelectedBrush`、`CardSelectedTextBrush`、`ComboBoxBorderBrush`、`ContentBareBrush`、`ContentBrush`、`ContentHeaderBrush`、`DangerBrush`、`DetailCardBrush`、`FooterBarBrush`、`HairlineBrush`、`InputBorderBrush`、`ListBodyBrush`、`ListHeaderBrush`、`ListRowHoverBrush`、`LoadingOverlayBrush`、`OnColorSecondaryTextBrush`、`PanelBrush`、`PlaceholderTextBrush`、`PrimaryTextBrush`、`ScrollThumbBrush`、`ScrollThumbHoverBrush`、`SecondaryTextBrush`、`SiderBrush`、`SiderFootBrush`、`SiderHoverBrush`、`SiderIconActiveBrush`、`SiderIconBrush`、`SiderTextBrush`、`StatusBarBrush`、`TextControlBorderBrush`、`TitleBarBrush`、`TitleBarCloseHoverBrush`、`TitleBarCloseHoverTextBrush`、`ToolbarBrush`、`WindowBackdropBrush`、`WindowBorderBrush`、`WindowBorderInactiveBrush`
<!-- /AUTO:facts.brushes -->

## 四、Shared 服务契约

实现列只登记首个实现类；一个接口多个实现属正常，明细见 `framework.md` §6。

<!-- AUTO:facts.interfaces -->
| 接口 | 文件 | 实现 |
|---|---|---|
| `IAppConfigService` | `Shared/Services/IAppConfigService.cs` | `AppConfigService` |
| `IAudioPlayback` | `Shared/Services/IAudioPlayback.cs` | `NAudioPlayback` |
| `IAudioSpectrum` | `Shared/Services/IAudioSpectrum.cs` | `—` |
| `IAudioTone` | `Shared/Services/IAudioTone.cs` | `—` |
| `IBackupSection` | `Shared/Services/IBackupSection.cs` | `DataDbBackupSection` |
| `IColumnLayoutService` | `Shared/Services/IColumnLayoutService.cs` | `FakeLayout` |
| `IDetailCardSource` | `Shared/Services/IDetailCardSource.cs` | `—` |
| `IDetailPanelProvider` | `Shared/Services/IDetailPanelProvider.cs` | `BookmarkViewModel` |
| `IEncryptedDnsService` | `Shared/Services/IEncryptedDnsService.cs` | `EncryptedDnsService` |
| `IExternalToolRegistry` | `Shared/Services/IExternalToolRegistry.cs` | `ExternalToolRegistry` |
| `IFavoritesService` | `Shared/Services/IFavoritesService.cs` | `FavoritesService` |
| `IFileOperationService` | `Shared/Services/IFileOperationService.cs` | `FileOperationService` |
| `IHistoryProvider` | `Shared/Services/IHistoryProvider.cs` | `—` |
| `ILaunchConfigService` | `Shared/Services/ILaunchConfigService.cs` | `LaunchConfigService` |
| `ILibrarySchemaContributor` | `Shared/Services/ILibrarySchemaContributor.cs` | `WebToolsSchemaContributor` |
| `IMenuService` | `Shared/Services/IMenuService.cs` | `MenuService` |
| `IModuleBus` | `Shared/Services/IModuleBus.cs` | `ModuleBus` |
| `IModuleReloadService` | `Shared/Services/IModuleReloadService.cs` | `ModuleReloadService` |
| `INavigationService` | `Shared/Services/INavigationService.cs` | `NavigationService` |
| `IOptionsStore` | `Shared/Services/IOptionsStore.cs` | `FakeOptions` |
| `IPageCatalog` | `Shared/Services/IPageCatalog.cs` | `PageCatalog` |
| `IProcessRunner` | `Shared/Services/IProcessRunner.cs` | `ProcessRunner` |
| `IProfileService` | `Shared/Services/IProfileService.cs` | `ProfileService` |
| `IPythonRuntime` | `Shared/Services/IPythonRuntime.cs` | `PythonRuntime` |
| `ISelectionStatusProvider` | `Shared/Services/ISelectionStatusProvider.cs` | `—` |
| `IStatusActionProvider` | `Shared/Services/IStatusActionProvider.cs` | `PathViewModel` |
| `IStatusProvider` | `Shared/Services/IStatusProvider.cs` | `AboutSettingsViewModel` |
| `IThemeService` | `Shared/Services/IThemeService.cs` | `ThemeService` |
| `IToolbarSource` | `Shared/Services/IToolbarSource.cs` | `—` |
<!-- /AUTO:facts.interfaces -->

## 五、跨模块消息主题

`IModuleBus` 主题常量。⛔ 调用处不得写字面量主题串，一律引用常量。

<!-- AUTO:facts.topics -->
| 常量 | 主题字符串 | 定义处 |
|---|---|---|
| `OpenFiles` | `viewer.openfiles` | `Module.Viewer/Services/ViewerTopics.cs` |
| `VideoImport` | `video.import` | `Shared/Services/IModuleBus.cs` |
| `AudioPlay` | `viewer.audioplay` | `Shared/Services/IModuleBus.cs` |
| `ImageOpen` | `viewer.imageopen` | `Shared/Services/IModuleBus.cs` |
<!-- /AUTO:facts.topics -->

## 六、需求规格章节索引

由 `NewToys-需求规格.md` 的编号标题树生成，供 `NewToys-需求摘要.md` §九 定位原文。

<!-- AUTO:facts.outline -->
| 节号 | 标题 |
|---|---|
| `2.1` | 远期规划（暂不实现；**勿立项、勿重复评估** —— 省 token） |
| `2.2` | 编排能力上限（2026-10-19 用户定调 · **L3/L4 封顶**） |
| `2.3` | 模块重划分 v1.1（2026-10-22 用户定案） |
| `2.3.1` | 三模块终态 |
| `2.3.2` | 页归属变更（⛔ Key 一律不改） |
| `2.3.3` | 迁移判据（可机械执行） |
| `2.3.4` | 命令链（用户口头「流水线」的本体） |
| `2.3.5` | 数据与文件归属 |
| `2.3.6` | 分期工作项 |
| `2.3.7` | 🔴 待批准 / 已知成本 |
| `3.1` | 分层与依赖 |
| `3.2` | 技术栈（锁定，禁止擅升擅降） |
| `3.3` | 框架层冻结 |
| `3.4` | 模块化与热加载 |
| `3.5` | 配置与数据 |
| `3.6` | 默认数据（不许硬编码；v3.60 收窄为「只给默认路径」） |
| `3.7` | 跨平台预留 |
| `4.0` | 布局总览 |
| `4.1` | 标题栏（40px，窗口级） |
| `4.1.1` | 窗口装饰与窗框（**走 Avalonia 12 官方 CSD 路线**） |
| `4.2` | 标签栏（并入标题栏中段） |
| `4.3` | 左侧菜单栏（150px / 折叠 48px，窗口级） |
| `4.4` | 主内容区（中间） |
| `4.5` | 详情卡（右侧 300px，可开合） |
| `4.5.1` | 详情卡声明式规范（v3.45；工作项 NT-054） |
| `4.6` | 底部状态栏（26px，窗口级） |
| `4.6.1` | 系统托盘（v3.60 新增，工作项 NT-057） |
| `4.7` | 主题 |
| `4.8` | 列表页 |
| `4.8.1` | 列宽（v3.21 收口为通用规则；视频两页首发，能力对所有列表页可用） |
| `4.8.2` | 筛选条快捷项的分类项（v3.21） |
| `4.8.3` | 空态引导（v3.60 改写；原「示例数据」整体作废，工作项 NT-060） |
| `4.8.4` | 主显示区（工具条 / 筛选条）基本范例（v3.41 收口 · 所有列表页照此） |
| `4.8.5` | 工具条由 Shell 统一承载（v3.53 用户立项；工作项 NT-055） |
| `4.8.9` | 列表页范本后续（NT-149 / NT-150） |
| `4.9` | 设置页 |
| `4.9.1` | 设置总表（v3.53 用户立项；工作项 NT-056） |
| `4.9.2` | 应用级设置页（v3.62；Shell 承载） |
| `4.10` | 视觉规范（硬约束） |
| `4.10.1` | 图标 |
| `4.10.2` | 密度基线 |
| `4.10.3` | 字号层级 |
| `4.10.4` | 视觉一致性 |
| `4.11` | 未实装 / 预留（本轮范围外） |
| `5.1` | 核心概念 |
| `5.2` | 标签页模型 |
| `5.3` | 历史栈 |
| `5.4` | 启动时序 |
| `5.5` | 防闪退铁律 |
| `6.1` | 首页 |
| `6.2` | 脚本管理器（`Module.ScriptManager`；原「脚本管理器 / 电脑工具 / `Module.PcTools`」。显示名 2026-11-03 定为不改，v3.86 的「脚本中心」作废） |
| `6.2.1` | 快捷设置（命令行清单） |
| `6.2.2` | 设置与脚本（脚本文件清单） |
| `6.2.3` | 软件包（winget GUI） |
| `6.2.4` | 信息展示（脚本输出呈现器，原系统信息 `pc-info`） |
| `6.2.5` | 脚本集合（`script-collections`，v3.71 / NT-068 新增） |
| `6.2.6` | 信息面板（`script-info`：集合选择框 + 分节） |
| `6.3` | 网络工具（`Module.WebTools`） |
| `6.3.3` | Hosts 管理（v3.97 · NT-111 单页双列表工作台；v3.98 · NT-112 多选 + 批量校验） |
| `6.3.1` | 爬虫脚本面板（AI 写脚本范式 · 期① NT-086 / 期② NT-087） |
| `6.3.2` | 书源运行时兼容（L1 · 远期，⛔ 本期不做） |
| `6.4` | 视频工具（`Module.VideoTools`） |
| `6.4.1` | 两页详情卡（同一套规格） |
| `6.4.2` | 「视频预览」页（单任务编辑器） |
| `6.4.3` | 视频列表·导入入口（「📂 导入」拆分按钮） |
| `6.4.4` | 批量处理·任务的导出 / 导入 |
| `6.4.5` | 导入 / 导出默认目录（NT-077 第二批改口径：不再可配） |
| `6.4.6` | 视频标签（tags）—— ⛔ 已移除（v3.32） |
| `6.4.7` | 详情卡内直接改名 + 同步磁盘 |
| `6.4.8` | 列表表头扩充（更多可隐藏列） |
| `6.4.9` | 视频收藏页 —— ⛔ 已移除（v3.32） |
| `6.4.10` | 表头 ☑ 的勾选菜单（全选 / 取消选择 / 反选） |
| `6.4.11` | 清除列表（清空显示，⛔ 不是「重载」） |
| `6.4.12` | 视频列表优化 + 清理 |
| `6.4.13` | 视频预览旋转 + 按钮标题 |
| `6.4.14` | ffmpeg 命令卡布局 + 转码默认 |
| `6.4.15` | 表头列宽：手动拖拽（v3.20；⛔ 删除「自动调整列宽」） |
| `6.4.16` | 本模块"资产库"能力整体移除（v3.32；用户定调） |
| `6.4.17` | 字幕：软字幕封装 / 硬字幕烧入（v3.92；工作项 NT-100） |
| `6.4.18` | 多段剪切 + 拼接（v3.92；工作项 NT-101） |
| `6.4.19` | 画面裁剪 + 黑边探测（v3.93；工作项 NT-102） |
| `6.4.20` | 音轨：静音 / 音量增益 / 替换（v3.93；工作项 NT-103） |
| `6.4.21` | 按目标体积压缩（v3.93；工作项 NT-104） |
| `6.4.22` | 时间轴区间标记（v3.94；工作项 NT-105） |
| `6.4.23` | 转码前收益判定（省体积闸门）（v3.97；工作项 NT-122） |
| `6.5` | 应用级设置（v3.62 归 **Shell**；原 `Module.Settings` 撤销） |
| `6.6` | 媒体资产（`Module.MediaAssets`） |
| `6.6.1` | 资产浏览页（`assets-browse` · v3.99 新增 · NT-115） |
| `6.7` | 系统工具（`Module.SystemTools` · v3.86 新增） |
| `6.8` | 备份中心（`Module.BackupCenter` · v3.92 立项） |
| `6.8.1` | v3.93 增补（2026-10-28 · NT-119） |
| `6.8.2` | v3.94 增补（2026-10-28 · NT-118 邮件投递 / NT-120 邮箱管理器） |
| `6.9` | 查看器（`Module.Viewer` · 立项 NT-124） |
| `6.9.1` | 定位（🔴 决定一切） |
| `6.9.2` | 三种渲染器 + 一种外壳 |
| `6.9.3` | 媒体后端（🔴 待拍板，不拍板则 V2 不启动） |
| `6.9.4` | 分期 |
| `6.9.5` | 范围边界（⛔ 首版一律不做） |
| `6.9.6` | 音乐播放器增强：频谱 · 封面与歌词关联 · 会话续播（NT-143） |
| `6.10` | 网络阅读（`Module.Reading` · 立项 NT-125） |
| `6.10.1` | 与查看器的分工（🔴 别再混） |
| `6.10.2` | 能力范围 |
| `6.10.3` | 方言支持矩阵（🔴 依据实测，不是猜） |
| `6.10.4` | 数据落点 |
| `6.10.5` | 分期 |
| `6.10.6` | 风险与硬约束 |
<!-- /AUTO:facts.outline -->

## 七、任务卡索引

由 `backlog/` 下的卡文件生成；状态与是否验收以《项目状态与路线图》台账为准。

<!-- AUTO:facts.cards -->
| 卡 | 模块 | 标题 |
|---|---|---|
| `NT-117` | backup | NT-117 · 备份中心模块（骨架 + 7z 加密归档 + 本地包管理 + 还原 + 密码库） |
| `NT-118` | backup | NT-118 · 备份中心 · 邮件投递（分卷 + 索引号 + db 记录 + 缓冲清理） |
| `NT-119` | backup | NT-119 · 备份中心 v1.1：SQLite 存档 + 备份日历 + 7z 路径上提 + 默认备份目录 |
| `NT-120` | backup | NT-120 · 备份中心 · 备份邮件页（邮箱管理器 + 搜索 + 下载还原） |
| `NT-047` | framework | NT-047 · 数据驱动图标矢量化（二级菜单 / 首页一键按钮 / 模块页内数据行） |
| `NT-079` | framework | NT-079 · 可成长性三件套：模块开关 / 命令面板 / 采集器骨架生成 |
| `NT-081` | framework | NT-081 · 跨平台中立化（步骤 1-5 已实施 · 待 Linux 实机验证） |
| `NT-121` | framework | NT-121 · 跨模块消息通道 `IModuleBus` + 「资产库 → 视频工具」交接 |
| `NT-123` | framework | NT-123 列表页统一切换为原生表格（`TableViewHost`） |
| `NT-124` | framework | NT-124 · 查看器：文档 / 图片 / 音视频基础查看能力 |
| `NT-145` | framework | NT-145 · 启动链路打点补全（诊断卡 · 本身不提速） |
| `NT-146` | framework | NT-146 · 图标体系精简 + 通用动作矢量化 |
| `NT-147` | framework | NT-147 · 表头右键 / 行右键职责分离（资源管理器口径） |
| `NT-148` | framework | NT-148 · 通用筛选条 FilterBar + 右键筛选全清 |
| `NT-149` | framework | NT-149 · 同族页能力矩阵 + 静态验收第 10 项 |
| `NT-138` | media-assets | NT-138 · 媒体资产：扫描提速（索引与增强解耦）+ 存储源页并入资产浏览页 |
| `NT-139` | media-assets | NT-139 · 媒体资产：批量重命名（多选 + 规则对话框） |
| `NT-140` | media-assets | NT-140 · 媒体资产：加标签 / 加集合入口重做 + 标签口径统一为「类型」+ 筛选联动 + 详情卡保存 |
| `NT-141` | media-assets | NT-141 · 媒体资产：标签详情卡去掉资产清单 + 「按此标签浏览」按类型分流（视频 / 图片 / 音乐） |
| `NT-150` | media-assets | NT-150 · 全部资产：详情卡字段补齐 |
| `NT-129` | reading | NT-129 · 网络阅读 批6：正文缓存 + 换源增强 + 发现页 @js: 动态分类 + t2s 繁转简 |
| `NT-130` | reading | NT-130 · 网络阅读 批7：书源管理工具（编辑 + 试跑）+ 全局替换规则 + 预下载 5 章 + 整本下载整合 |
| `NT-131` | reading | NT-131 · 网络阅读 批8：书源分组管理（类型 / 分组 / 校验筛选 + 多选批量）+ 书源校验 |
| `NT-132` | reading | NT-132 · 网络阅读 批9：书架批量更新（追更） |
| `NT-133` | reading | NT-133 · 网络阅读 批10：听书（TTS 朗读） |
| `NT-080` | script-manager | NT-080 · 软件包定位收敛 + winget 源模型重写 |
| `NT-082` | script-manager | NT-082 · 软件包集合（跨后端包清单预设）+ 集合右键发送到对应页面 |
| `NT-089` | script-manager | NT-089 · 脚本中心：抓取能力整体平移 + 规则侧落脚本库 |
| `NT-090` | script-manager | NT-090 · 脚本中心：运行台搬家 + `web-crawl` 泛化为「运行结果」 |
| `NT-095` | script-manager | NT-095 · `web-scrape` 抓取规则页终态（S2 拆出，待拍板） |
| `NT-091` | system-tools | NT-091 · 新建 `Module.SystemTools`（系统工具，Order 25） |
| `NT-096` | system-tools | NT-096 · 软件包页迁入系统工具 + 系统工具定位收紧 |
| `NT-097` | system-tools | NT-097 · 系统工具页面重构（命令库对标脚本库 + 删快捷命令 + 预设播种 + 收藏夹置首） |
| `NT-102` | video-tools | NT-102 · 画面裁剪 + 黑边探测 |
| `NT-103` | video-tools | NT-103 · 音轨：静音 / 音量增益 / 替换 |
| `NT-104` | video-tools | NT-104 · 按目标体积压缩 |
| `NT-105` | video-tools | NT-105 · 时间轴区间标记（视频预览） |
| `NT-122` | video-tools | NT-122 · 转码前收益判定（省体积闸门） |
| `NT-135` | viewer | NT-135 · 查看器拆成三页（文本阅读 / 图片查看 / 音频播放） |
| `NT-136` | viewer | NT-136 · 图片查看：分屏网格 + 条目条滚动/降高 + 历史收进工具条 |
| `NT-142` | viewer | NT-142 · 查看器音乐播放：后台播放 + 播放器界面重做（封面 / 居中歌词 / 技术信息） |
| `NT-143` | viewer | NT-143 · 音乐播放器第二批：频谱条 + 封面与歌词关联 + 会话续播 |
| `NT-144` | viewer | NT-144 · 查看器渲染器全下沉 + 阅读／查看器职责重划 |
| `NT-108` | web-tools | NT-108 · 书签「移动到其他节点」 |
| `NT-109` | web-tools | NT-109 · 「书签管理」显示名改「网址收藏」 |
| `NT-110` | web-tools | NT-110 · Hosts 管理扩展（系统 Hosts 页 + 备份 / 源订阅 / 三项检测） |
| `NT-111` | web-tools | NT-111 · Hosts 管理合单页（双列表工作台）+ 源清单进设置 |
| `NT-112` | web-tools | NT-112 · Hosts 管理页多选（Ctrl / Shift）+ 批量校验 |
<!-- /AUTO:facts.cards -->
