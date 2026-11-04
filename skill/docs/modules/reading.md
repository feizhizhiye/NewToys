# 模块 · 网络阅读（Reading）

> **程序集**：`src/Module.Reading`（`NewToys.Reading`）；阅读器壳住本模块 `Controls/ReaderShell`（索引书渲染，NT-144 批 3 迁入）
> **模块键**：`reading`（菜单名 **网络阅读**）｜ **一级菜单**：Order 56 · 图标 `IconReading`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：NT-125~129、NT-133 已验收；NT-144 批 1/2/3 已实施（⏳ 批 2/3 待 build）：渲染器下沉 + 正文落盘索引书 + **阅读职责重划 —— 阅读器壳 / 本地 TXT / 听书全部收进本模块（桌面 legado），查看器文本页改「文档查看」**
> **最后校对**：2026-11-04（NT-144 批 3 阅读职责重划）

---

## 1. 职责与边界

- **定位**：**桌面 legado** —— 抓网络内容（书源）+ 本地 TXT 阅读 + 听书，阅读能力全在本模块；`Module.Viewer` 只管「文件预览」（文档查看 / 图片 / 音乐）。
- 一句话链路：**导入 legado 书源 → 输入关键词 → 出结果 → 点开看目录 → 阅读页就地读正文（或「📂 本地 TXT」直接读本地文件）**。
- **规则引擎分两层**：默认简化语法（选择器 / JSON / 插值）＋ `@js:` 片段的 JS 兼容层（Jint + `java.*` 桥）。
  真实 383 条书源里纯选择器 67.4%、加 JS 后 **96.3% 可跑**；剩下 3.7% 依赖 WebView / 唤起浏览器，桌面端不等价，书源页标「需 WebView」并拒跑。
- 🔴 **只做文本源（`bookSourceType = 0`，2026-11-02 用户定）**：图片 / 漫画 / 音频 / 视频源与「正文里的图片」都不在范围内（`StripMarkup` 会去掉 `<img>`，这是设计如此，⛔ 别当 bug 修）。
- ⛔ 不做：站点登录 / Cookie（2026-11-02 用户定）、书源编辑器 / 规则调试面板、EPUB / MOBI 导入（PDF / EPUB / MOBI 查看 = 查看器「文档查看」页，另行立项）、RSS 订阅、**WebDAV 备份（2026-11-01 用户明确不做）**。
- 🔴 **GPL 边界**：legado 是 GPL-3.0，本项目 MIT ⇒ ⛔ 不移植其 Kotlin 源码、⛔ 不翻译其实现；只按官方文档 + 真实书源行为重写。
- 🔴 依赖只加 `AngleSharp`（本就在 `Directory.Packages.props`）；⛔ 不引 XPath 库（383 条真实书源里 XPath 用法为 **0**）。

## 2. 当前功能（5 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View | 说明 |
|---|---|---|---|---|
| `reading-sources` | 书源 | 1 | `SourceListView` | 导入 / 导出 / 启停 / 删除书源（**多选批量**）；顶部按「类型 / 分组 / 校验」筛选；「🔬 校验」批量跑搜索判定失效源；每条标「可用 / 需 WebView / 不完整 / 非文本源」+「通过 / 无结果 / 失败 / 跳过」 |
| `reading-explore` | 发现 | 2 | `ExploreView` | 左「书源 → 分类」折叠树（书源名**整段显示不截断**、`Hint` 10px 小字、分类名 14px 为主角）+ 右**书单行列表**；点一行弹**书籍卡**（封面 / 作者 / 分类 / 最新章节 / 简介 + 开始阅读 / 加书架）；分类可翻页 |
| `reading-search` | 网上搜索 | 3 | `SearchView` | 关键词 + 结果行列表；书源多选收进顶栏「📚 书源」弹出的浮层；点一条结果弹同一张书籍卡 |
| `reading-shelf` | 书架 | 4 | `ShelfView` | 加进来的书；分组 / 读到哪章；**「🆕 批量更新」回源站抓目录、标出「有 N 章更新」**；双击进阅读页 |
| `reading-read` | 阅读 | 5 | `ReaderView` | **正文就地渲染**：内嵌 `ReaderShell`（索引书模式，目录 / 书签 / 全书搜索 / 排版 / 快捷键都在壳里，Ctrl+T / Ctrl+B / Ctrl+F）+ 顶栏「📂 本地 TXT / ＋ 书架 / 🔀 换源 / ⬇ 下载 / 🎧 听书」+ 页底听书控制条；翻到未缓存章自动补抓 |
| `settings-reading` | 网络阅读设置 | 100 | `SettingsReadingView` | 抓取（并发 / 超时 / UA / 缓存）+ 阅读器（字号 / 行距 / 字体 / 宽度 / 留白 / 配色 / **首行缩进 / 段间距 / 自定义色** / 进度）+ **听书**（语速 / 音量 / 音色）+ **样式方案**（预设 / 导入 / 导出） |

**导入格式**：legado 书源 JSON，兼容**顶层数组** `[{…}]` 与 `{"bookSources":[…]}` 两种形态；编码按 BOM → 严格 UTF-8 → GB18030 嗅探。

**四档判定的依据（静态，不发请求）**
- `非文本源`：`bookSourceType != 0`（图片 / 音频 / 视频）—— 最先判，不进搜索 / 发现候选池，校验时标「跳过」；导入时这类源**直接不入库**（`ImportResult.Unsupported` 计数回报）。
- `可用`：三段必填字段齐全（含 `@js:` 也算可用，原因里附「含 JS：搜索、目录」）。
- `需 WebView`：三段任一规则出现 `webview` / `startBrowserAwait`（桌面端没有对应能力）。
- `不完整`：缺 `searchUrl` / `ruleSearch.bookList` / `ruleToc.chapterList` / `ruleToc.chapterUrl` / `ruleContent.content` 任一。

## 2.5 操作逻辑

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 导入书源 | 书源页工具条「📥 导入书源」 | 按 `bookSourceUrl` **幂等更新**（⛔ 不产生副本），状态栏报「新增 N / 更新 M / 跳过 K / 跳过非文本源 X」 | 只认 `.json`；单文件；**非文本源（图片 / 音频 / 视频）不入库**；**重新导入不动校验结论** |
| 搜索书源 | 工具条搜索框输入 | 列表即时过滤（名称 / 分组 / 状态 / 地址） | ⛔ 不落盘，下次进页清空 |
| 筛选类型 / 分组 / 校验 | 页内筛选条三个下拉（可叠搜索框） | 三者 AND 生效；右侧显示「筛出 N / 共 M 条」；分组下拉 = 库里实际出现过的分组 + 未分组 | 即时生效，⛔ 不落盘；当前分组被删光时退回「全部分组」 |
| 多选 | 列表 Ctrl 点 / Shift 连选，或右键「☑ 全选当前结果 / ☐ 取消选择 / ⇄ 反选」 | 选中集合 = VM 的 `BatchSelection`（绑 `TableView.SelectedItems`），工具条与右键的可用态看它 | 全选只作用于**当前筛选结果**（⛔ 不选看不见的行） |
| 启用 / 停用 | 工具条 / 右键 | 改库里的 `enabled`（作用域 = 多选集合，空则当前行）；停用的源不出现在搜索页 | 无选中时按钮置灰并给提示 |
| 导出 | 工具条「📤 导出」/ 右键 | 把作用域内书源**原样**导成 JSON 数组（含未来字段，不重新拼） | 导出的是原始 JSON 文本 |
| 改分组 | 右键「📁 改分组…」→ 输组名（留空 = 未分组） | 写 `group_name` **并回写 `rules_json.bookSourceGroup`**；分组改名 = 先筛旧分组 → 改分组新名 | 输入框取消 = 不改；组名两端空白自动裁掉 |
| 校验书源 | 工具条「🔬 校验」→ 输关键词 + 选范围（当前筛选 / 选中 / 全部已启用）→「🔬 开始校验」 | 每个源各跑**一次搜索段**，逐条实时上屏「通过 / 无结果 / 失败」+ 原因；结论随跑随落库（`check_state` / `check_note` / `checked_at`），关掉浮层不丢 | 关键词必填；只读探测（⛔ 不发详情 / 目录 / 正文，⛔ 不碰书架 / 进度 / 缓存）；关浮层或点遮罩即取消，已跑完的结论保留 |
| 只看失效 | 筛选条「只看失效」/ 校验浮层同名按钮 | 校验筛选切到「失败」；「跳过」= 没有搜索接口、跑不了搜索的源（⛔ 不算失效） | 只看不删 |
| 删除失效 | 右键「🗑 删除全部失效」/ 校验浮层同名按钮 | 删掉库内 `校验失败 + 无结果` 的源（跨分组），确认框报两类条数 | 不可撤销；没校验过 ⇒ 一条都不删（先校验） |
| 删除非文本源 | 右键「🗑 删除非文本源」 | 删掉库里遗留的图片 / 音频 / 视频源（新导入的已在导入时跳过） | 状态栏报「非文本源 N」时可顺手清掉 |
| 删除 | 工具条「🗑 删除」/ 右键 | 二次确认后删库（作用域 = 多选集合） | 不可撤销 |
| 用源搜索 | 右键「🔍 用这个源搜索」 | 跳到「网上搜索」并**预选**该源 | 只预选，不自动发起搜索 |
| 编辑书源 | 工具条「✎ 编辑」 | 弹出原始 JSON 编辑器（有原始文本就用原始文本，保住本程序不认识的字段）；「💾 保存」时先解析校验 | 解析失败 / 缺 `bookSourceUrl` 一律挡在编辑器里，⛔ 不写库；改成另一个 `bookSourceUrl` 等于**新增一条**，原条目保留；同一条源沿用原启用状态（JSON 常不写 `enabled`） |
| 试跑书源 | 工具条「▶ 试跑」 | 输一个关键词，依次跑「搜索 → 详情 → 目录 → 正文」，每段给**耗时 + 结果摘要 / 失败规则** | 只读探测：⛔ 不写库、⛔ 不碰书架 / 进度 / 正文缓存；某段失败即停，后面的段不跑 |
| 搜索 | 搜索页输关键词 + 回车 / 点「🔍 搜索」 | 勾选的书源**并发**抓（默认 3 并发），结果**逐源上屏**（哪个源先回来先显示哪一条），顶部报「已完成 N / 总数 · 命中 X 条」 | 只列「可用 + 已启用」的源；默认全选，可用「☑ 全选 / ☐ 全不选」（只作用于当前过滤出来的那些）；单个源超时**不重试**（⛔ 不把等待翻倍） |
| 选书源 | 顶栏「📚 书源 ▾」弹出浮层（左栏不常驻），里面勾哪个搜哪个 | 勾选状态只影响本次搜索，⛔ 不改库里的 `enabled`（要停用去书源页）；点「完成」或遮罩收起 | 过滤框按名称 / 分组筛；勾选数实时显示在顶栏 |
| 加入书架 | 结果右键「＋ 加入书架」，或书籍卡「＋ 书架」 | 按详情地址幂等入库（重复加入只更新书名 / 作者） | 书架在「网络阅读 → 书架」 |
| 打开一本书 | 列表里**点一行 = 弹书籍卡** → 卡里「📖 开始阅读」 | 进「阅读」页：抓目录 → 已缓存章落盘索引书（`store/reading/books/<bookId>/`）→ `ReaderShell.LoadIndex` 就地渲染并续读 | 源被删 / 停用会明确提示，⛔ 不静默空白 |
| 发现浏览 | 网络阅读 → 发现 → 左栏展开书源 → 点分类 | 右侧铺该书单**行列表**；点一行弹书籍卡；点一棵树的根节点 = 展开 / 折叠 | 列「启用 + 非 WebView + `exploreUrl` 非空」的源（⛔ 不要求搜索段完整）；规则用 `ruleExplore`，**字段空**时逐个由 `ruleSearch` 补；左树每行等宽（⛔ 不随书名长短参差） |
| 发现翻页 | 分类地址含 `{{page}}` 时出现「◀ 上一页 / 下一页 ▶」 | 抓第 N 页；失败不翻（避免停在空白页） | 地址里没有 `{{page}}` 的站点不显示翻页条 |
| 关闭书籍卡 | 点遮罩 / 卡里「关闭」 | 收起浮层并清掉列表选中（否则再点同一行不会重新弹卡） | 卡片是**居中卡片**（与「确认对话卡片」同款：遮罩 + 圆角 8 + 投影 + 按钮右对齐），遮罩点击不穿透到卡片本体 |
| 打开阅读页 | 书籍卡「📖 开始阅读」/ 书架双击 | 进「阅读」页续读 | 源被删 / 停用会明确提示，⛔ 不静默空白 |
| 翻章 | 壳顶栏「上一章 / 下一章」或 ←/→，或目录抽屉点章 | 章内容按需读落盘文件（⛔ 不联网）；文件缺失 → 占位 + 自动补抓（抓到写正文缓存 + 落盘 → 壳重载当前章） | 补抓命中正文缓存时不发网络请求 |
| **本地 TXT 导入** | 阅读页顶栏「📂 本地 TXT」 | 读文件（BOM → 严格 UTF-8 → GB18030）→ 切章（内置规则，全不中按约 3000 字切块）→ 落盘索引书（`store/reading/local/<hash>/`，hash = 路径 + 大小 + 修改时间，没变就复用）→ 壳就地渲染；目录 / 书签 / 搜索 / 排版 / 听书照常 | 进度按**绝对路径**记 `ProgressStore`（章节键 = `#序号`）；本地文件 ⛔ 换源 / 整本下载 / 加书架；⛔ 不在 UI 线程切章落盘 |
| 套用内置样式 | 设置页「样式方案」点「默认白底 / 护眼米黄 / 夜间暗色 / 大字宽松」 | 整套覆盖字号 / 行距 / 字体 / 宽度 / 留白 / 配色 / 缩进 / 段距，即时落盘 | 预设每格都填 ⇒ ⛔ 不是「挑着改」；状态栏报「已应用（N 项）」 |
| 导入样式 | 设置页「📥 导入」→ 挑一个 `.json` | 逐项写 `config\reading.json` 并即时生效；状态栏报「样式「X」已应用（N 项）」 | 🔴 缺的项 = `null` ⇒ **保持原值**（手写两三项也能用）；认不出的字段忽略；非法 JSON / 读不出来只报状态，⛔ 不弹框 |
| 导出样式 | 设置页「📤 导出」→ 选保存位置 | 把当前排版写成一份缩进 JSON（UTF-8 无 BOM），文件名默认 `reader-style.json` | 导出的就是「当前设置」快照，可带走 / 分享 |
| 调段落 | 设置页直接填 | 缩进 = 段首补 N 个全角空格（0~4）；段距 = 段间插 N 个空行（0~3）；即时落盘 | 🔴 正文控件只有一个 `SelectableTextBlock`（无段落样式）⇒ 只在文本层做，⛔ 不为此改 NewToys.UI |
| 阅读壳快捷键 | 壳内键盘 | 空格 / PgDn·PgUp 翻页；←/→ 翻章；Ctrl+T 目录、Ctrl+B 书签、Ctrl+F 全书搜索；Esc 收浮层 | 输入框聚焦时按键归输入框 |
| 加书架 | 阅读页顶栏「＋ 书架」 | 已在书架则刷新书名 / 作者，保留分组与加入时间 | 失败（库不可用）红字提示 |
| 换源 | 阅读页顶栏「🔀 换源」 | 以当前书名并发搜所有可用源 → 按「书名匹配度 → 速度」排序 → 弹换源浮层；选一个源原地重开这本书续读当前章 | ⛔ 不再带书名回搜索页；当前源也参与比对；浮层里点遮罩 / ✕ 收起 |
| 读书架的书 | 书架双击 / 工具条「📖 继续阅读」 | 进阅读页续读 | 列的「读到」列显示上次读到的章节 |
| 刷新书架 | 工具条「🔄 刷新书架」 | 只重读本地库（⛔ 不发网络请求） | 与「批量更新」区分：那个才回源站 |
| 批量更新书架 | 工具条「🆕 批量更新」（作用域 = 当前筛选结果），或右键「🆕 更新这本」 | 逐本回源站抓**详情 + 目录**，用目录章数比上次基线 ⇒ 写「有 N 章更新 / 已是最新」；状态栏实时报「已完成 N / 总数」 | 并发 = 设置里的并发数（默认 3）；首次检查只建基线（`new_count = 0`，⛔ 不算有新章节）；单本失败不中断整批，原因进「更新」列 |
| 书架筛选 | 页内筛选条：分组 / 更新状态（有更新 / 已是最新 / 未检查 / 更新失败）+ 「只看有更新」 | 与工具条搜索框 AND 叠加；右侧「筛出 N / 共 M 本」 | 即时生效，⛔ 不落盘 |
| 分组 | 书架工具条「📁 分组」/ 右键 | 输入组名（留空 = 未分组），立刻回写并刷新列表 | `shelf.group_name`，与书源分组同一套「空 ⇒ 未分组」归一 |
| 阅读设置 | 网络阅读 → 网络阅读设置 | 改并发 / 超时 / UA / 缓存 / **预下载章数** / 字号 / 行距 / 字体 / 宽度 / 留白 / 配色 / 进度 / **替换规则** | 即时落盘；⚠ 并发与超时要重开阅读页才生效（请求层在页面创建时定型） |
| 整本下载 | 阅读页顶栏「⬇ 下载」 | 先选保存位置 → 逐章抓正文（命中缓存的直接跳过）→ 合成一份 TXT（书名 / 作者 / 每章标题 + 正文，章节间用 `-----` 分隔） | 中途取消只停后续章节，**已抓的照常合并保存**；失败章节列在结算文案里，再点一次即可（已下的走缓存） |
| 预下载 | 打开 / 翻章后自动触发 | 后台串行把后面 N 章抓进正文缓存（N 来自设置，默认 5，0 = 关） | 换章即重起一轮（⛔ 不叠加）；已有缓存的不重抓；失败静默，翻到那章时再正常抓 |
| 替换规则 | 设置页「替换规则」节 | 每行 `正则##替换`（只写正则 = 删除），行首 `#` 当注释；在书源内联 `replaceRule` **之后**执行 | 只作用于新抓的正文；已缓存的章节仍是旧正文 ⇒ 改完点「🧹 清空正文缓存」；单条正则写错只跳过那一条 |
| 听书 | 阅读页顶栏「🎧 听书」 | 把**壳当前章**正文切成句子，交 Windows 自带语音（SAPI）逐句读；页底控制条：⏸ 暂停 / ▶ 继续、语速 − ＋、⏹ 停止；进度写「第 N/M 句」 | 朗读文本随壳切章自动跟上（位置变化即换文本，听书中换章 ⛔ 不自动重起）；暂停 / 停止在**句子边界**生效；语速改了从当前句重起 |
| 听书设置 | 设置页「听书」节 | 语速（−10~10，0 = 正常）/ 音量（0~100）/ 音色（本机已装语音包，空 = 系统默认） | 与阅读页控制条同一份 `reading.json`；音色下拉在后台枚举，没装中文语音包就只有「系统默认」 |
| 取消 | 搜索页 / 发现页「✖ 取消」 | 中断正在进行的抓取 | 每步都带同一取消令牌 |
| 失败提示 | 任一步失败 | 错误条显示「哪一步 · 哪条规则 · 什么原因」 | ⛔ 不只显示"失败了" |

## 3. 实现逻辑

```
SourceListView ──► SourceListViewModel ──► SourceStore ──► store\reading\reading.db(book_sources)
                        └────────────────► SourceImportService（legado JSON → BookSource → 幂等 upsert）

SourceCheckService ─► CrawlService.SearchManyAsync（每源跑一次搜索）──► SourceStore.SetCheck（逐条落库）

SearchView ───────► SearchViewModel ─────► CrawlService.SearchAsync（searchUrl，含 ,{…} 请求体）
ExploreView ──────► ExploreViewModel ────► CrawlService.ExploreAsync（exploreUrl 分类 + {{page}}）
                                                └─► RuleEvaluator（RuleSyntax 切分 + AngleSharp 选择器 + JsonPathLite）
```

- 🔴 **求值器返回联合体** `RuleValue`（`Texts` / `Nodes` / `JsonItems`）。压成 `List<string>` 就无法「先拿书单节点集、再在每个节点内求字段」，条目会按索引错位（旧抓取引擎的返工根因）。
- 🔴 **会话状态在 `RuleContext`**：`Root`（文档 / 条目节点）、`JsonRoot`（条目 JSON 元素）、`Vars`（`key`/`page`/`baseUrl`…）、`Store`（`@put`/`@get` 字典）、`Errors`（可读告警）。派生上下文（`ForElement` / `ForJson` / `WithDocument`）**共享 Vars / Store / Errors 引用**。
- **切分顺序**：先剥 `@js:` / `<js>`（之后整段是 JS）→ `{{}}` → `||` → `&&` → `##`。`##` 后两段是「正则 / 替换串」，⛔ 不参与规则切分。
- **JS 层**（`RuleEvaluator.Js.cs`）：`@js:` 的前半段照常求值，结果作为 JS 变量 `result`；执行完取最后表达式值（undefined 时回退取 `result`）。
  🔴 桥的边界只有 **string 进 / string 出**（元素集走 JSON 索引数组、连接走 JSON 快照），JS 侧重载与对象包装全在 prelude 里做 —— ⛔ 不直接暴露 CLR 对象。
  🔵 **Jint 4.17 支持到 ES2025**（3.x 只到 ES2021）：书源脚本可零改动用可选链 `?.`、空值合并 `??`、`Array.at(-1)`、`Object.hasOwn` 等新语法；写书源/排查「旧语法跑不通」时可优先换新语法试。
- **请求层**：`HttpClient`（跟随重定向 / 自动解压）、`User-Agent` 与超时来自设置、失败重试 1 次（404/403 不重试）、并发门 `SemaphoreSlim`。
- 🔴 **正文清洗**：`ruleContent.content` 常写成 `…@html`，拿回来带标签 ⇒ `StripMarkup` 去标签、块级收尾转换行、解 HTML 实体后再交给 `TextViewer`。
- **发现页**与**搜索**共用 `ParseItems`（同一套「书单 → 每条目内求值」），区别只在地址来源（`exploreUrl` 的分类 vs `searchUrl` 的模板）与规则（`ruleExplore` 逐字段退回 `ruleSearch`）。
- **搜索页与发现页都只出书单**：详情 / 目录 / 正文三段的加载全部在 `ReaderView` 一侧，页面之间靠 `ReadingSession.PendingBook` 交接 ⇒ 正文渲染实现只有一份。
- **书籍卡是两页共用的一份控件**：`Views/BookCardOverlay`（遮罩 + **居中卡片**，与 Shell 的「确认对话卡片」同款观感）。它 ⛔ 不引 VM 类型做基类，只按**同名成员**绑定
  （`CardBook` / `CardCover` / `HasCardCover` / `CardCoverInitial` / `IsCardOpen` / `CloseCard` / `ReadCardBook` / `AddCardToShelf`），
  两个页面 VM 各自提供一套 ⇒ 改一处两页同步。点遮罩关闭由 code-behind 分派到具体 VM。
- **封面**：`CoverLoader`（单例）下载 + 内存缓存（同一地址只下一次，失败缓存 `null`，上限 120 张整清）。
  VM 拿到 `Bitmap` 只用于绑定，⛔ 不 Dispose（所有权在缓存）。

```
ShelfView ─────► ShelfViewModel ──► ShelfStore.SetGroup / LoadAll / SetUpdate
                      ├──────────► ProgressStore.LoadLatest（一次取全部书的最新进度）
                      └──────────► ShelfUpdateService（批量更新：详情 + 目录 → 比章数）

ReaderView ────► ReaderViewModel ──► CrawlService（详情 → 目录；缺章补抓）
                      ├────────────► ProgressStore（壳位置变化即存；本地 TXT 按路径记）
                      ├────────────► ReadingSettingsService（壳排版唯一落点）
                      ├────────────► BookIndexWriter（索引书落盘：books/<bookId> 与 local/<hash> 两族）
                      └────────────► Controls.ReaderShell（渲染 + 目录 / 书签 / 搜索 / 排版；⛔ 壳不碰配置，事件外转）

SettingsReadingView ► SettingsReadingViewModel ► ReadingSettingsService（唯一写入点 config\reading.json）
```

- 🔴 **壳 ⇄ VM 只走事件**（`ReaderView` code-behind 互转）：壳发 `PositionChanged / ChapterMissing / TypographyChanged / 书签三事件 / StatusChanged`，
  VM 发 `BookReady / ChapterFetched / ShellTypographyReady`。VM ⛔ 碰控件、壳 ⛔ 碰配置 —— 双向都不许越界。
- 🔴 **壳排版 = 服务即真相**：壳排版浮层改动 → VM 写 `ReadingSettingsService`（`reader-*` 既有键 + 新键 `reader-shell-theme`）
  → `Changed` 回灌 → `ShellTypographyReady` → 壳 `ApplyTypography`；`_syncing` 闸门防回环。
- 🔴 **续读 = `LoadIndex(path, position)` 一次到位**：`OpenAsync` 算好（章序号，章内比例）随 `BookReady` 发给视图；
  网络书按 `ProgressStore` 的 chapter_url 对齐章，本地 TXT 按 `#序号` 解析。

## 4. 数据与配置

| 项 | 落点 | 写入点 |
|---|---|---|
| 书源（`book_sources`） | `store\reading\reading.db` | `SourceStore`（upsert / delete / setEnabled / setGroup / setCheck / touchUsed） |
| 书架（`shelf`） | 同上（含 `group_name` 分组 + `last_chapter` / `chapter_count` / `new_count` / `checked_at` / `update_note`） | `ShelfStore`（Add / SetGroup / Remove / SetUpdate）；批量更新写后者 |
| 阅读进度（`reading_progress`） | 同上（每章一行，取最新一行为续读落点） | `ProgressStore`（Save / Get / LoadLatest / Remove） |
| 正文缓存（`content_cache`） | 同上（每章一行，`chapter_url` PK） | `ContentCacheStore`（Get / Put；`cache-enabled` 开关控制，关时⛔ 不读不写） |
| 并发 / 超时 / UA / 缓存 + 阅读器排版 | `config\reading.json` | 本模块设置页 + 阅读页顶栏 + 样式卡导入（同一个 `ReadingSettingsService`） |

阅读器那组的键：`reader-font-size` / `reader-line-spacing` / `reader-font-family` / `reader-content-width` /
`reader-padding` / `reader-theme` / `reader-indent` / `reader-paragraph-spacing` / `reader-bg` / `reader-fg` /
`reader-remember-progress`（后两个自定义色留空 = 跟配色档走）；**壳配色档** `reader-shell-theme`（0 跟随主题 / 1 米黄 / 2 淡绿 / 3 夜间）。
🔴 索引书落盘：`store/reading/books/<bookId>/`（网文）与 `store/reading/local/<hash>/`（本地 TXT），各含
`index.json` + `chapters/NNNN.txt` + `bookmarks.json`（书签，`BookmarksStore`）；索引自愈：重开书 / 整本下载后重导。

听书那组的键：`tts-rate`（−10~10）/ `tts-volume`（0~100）/ `tts-voice`（系统音色的完整描述，空 = 默认音色）。

- 听书不落任何库表：句子切分与朗读状态全在内存，关页即丢。

- 阅读进度在「记住阅读进度」关掉后不再写库（已写的旧进度保留，不清）。

- 🔴 **一个模块一个库**：三张表只在 `reading.db`；`ReadingSchemaContributor` 在首帧后幂等建表（`DatabaseKey = "reading"`）。
- 表结构：`book_sources(url PK / name / group_name / source_type / enabled / rules_json / last_used_at / check_state / check_note / checked_at)`；`rules_json` 存**导入原文**，后续补 JS 兼容层时不必重新导入。

## 5. 坑点（实测）

- 🔴🔴 **`exploreUrl` 的分隔符是两种**（`&&` 与**换行**，真机书源里换行占多数）。只按 `&&` 切 ⇒ 整张分类表并成一条，
  URL 里混进「分类名::」⇒ 请求必然 404 ⇒ 发现页永远是空的（历史故障就是这条）。
- 🔴 **每项分类自带请求配置**：`分类::地址, {"method":"POST","body":"…"}` —— `SplitRequest` 拆完必须把 `method` / `body`
  一起带进 `ExploreCategory`，⛔ 丢掉后 POST 站点一律拿不到书单。
- 🔴 **`ruleExplore` 是逐字段回退**，不是整对象回退。只写 `bookList` 的源很常见；整对象回退会让 `name` / `author`
  为空 ⇒ 条目全被判「书名与链接都为空」⇒ 看起来像"选择器没命中"。
- 🔴 **`ResolveUrl` 必须按字符串拼**：分类解析阶段地址里还带 `{{page}}`，`Uri.TryCreate` 会失败或改写原串
  （花括号被转义），插值就白做了。
- 🔴 **条目字段的作用域**：`bookList` 求出节点集后，`name` / `author` 必须在**每个节点内**求值（`ctx.ForElement(node)`）；对整页求值再按索引拼 = 条目错位。
- 🔴 **`@put:{k:rule}` 里的 `@` 不是步骤分隔符**：`SplitSteps` 遇到 `@put:` / `@get:` / `@var:` 必须**按花括号配平整段收下**，否则 `{k:tag.a@href}` 被腰斩成两段。
- 🔴 **`{{key}}` 是已 URL 编码的值**：`CreateVariables` 里 `key` 放 `Uri.EscapeDataString(key)`，原文另存 `keyUnescaped`；POST body 里要原文就用后者。
- 🔴 **JSON 源的字段规则常直接写 `$.xxx`**（不带 `@json:`）：求值器对「以 `$` 开头 + 上下文有 `JsonRoot`」直接走 JSONPath。`JsonPathLite` 的**属性取值会自动展开数组**（`$.data.list.name` 在 list 是数组时逐元素取）。
- 🔴 **正文/目录的解析要看懂「反序索引」**：`class.x!0` 是**最后一个**（`!N` ⇒ 索引 `-(N+1)`），`class.x.0` 是第一个；两者都在 `ParseSelector` 里剥离，再交给 CSS。
- 🔴 **`IText` 判定要 `is IText`**：`ownText` 只取直接文本子节点；用 `TextContent` 会把子元素文字也算进来。
- ⚠️ 目录 / 正文翻页必须**记已访问 URL**：站点 404 页会把 `nextContentUrl` 指向自己，不去重就无限循环（另加 20 / 10 页硬上限）。
- 🔴🔴 **`searchUrl` 大量是相对路径**（`/search?q={{key}}`）—— 实测 40 个纯选择器源里过半如此。必须用 `ResolveUrl(书源地址, 规则地址)` 拼绝对地址再发请求，否则 HttpClient 直接抛「无效 URI」⇒ 一个源都搜不出来。
- 🔴 **`!` 是「排除」不是「倒数」**：`class.x!0` = 去掉第 1 个（跳过表头），`class.x!0:1:2` 去掉前三个，`class.x.-1` 才是倒数第一个。
- 🔴 **规则以 `$` 开头就是 JSON 规则**（legado 不要求写 `@json:`）：`$..list[*]` 这类要按 JSONPath 走，⛔ 不能当 CSS 选择器。
- 🔴 **单花括号 `{}` 是 JSON 字段插值**：`http://x.com/b/{$.wapBookId}.html` —— 只在 JSON 条目上下文生效，⛔ 与 `{{变量}}` 混为一谈。
- 🔴 **`@js:` 之后整段都是 JS**：那里的 `||` 是逻辑或、`##` 是注释，⛔ 不能再按规则切分（`RuleSyntax.SplitJs` 在 `Evaluate` 最外层先剥）。
- 🔴 **JS 桥必须 string 进 / string 出**：Jint 对 `object` / `params` / `JsValue[]` 的参数推断各版本不一致，直传 CLR 对象会静默取到错值；元素用「池 + 索引」、连接用 JSON 快照最稳。
- 🔴 **`java.ajax` 是同步语义**：必须在 `Task.Run` 里等 `FetchAsync`，⛔ 直接在 UI 线程上等 async（死锁）。
- ⚠️ **JS 异常只废当前字段**：`EvaluateJs` 整体 try/catch → 一行 `ctx.Errors`，⛔ 不冒到界面、不影响同一条源的其它字段。
- ⚠️ **GBK 站点要按 GBK 编码搜索词**：`searchUrl` 里出现 `ie=gbk` / `gb2312` / `gb18030` 时，`{{key}}` 必须用 GB18030 逐字节百分号编码（UTF-8 编码的 key 到 GBK 站点等于乱码 ⇒ 0 结果）。
- ⚠️ **关书籍卡必须同时清 `SelectedResult`**：`ListBox` 选中没变就不会再触发「选中变化」⇒ 卡片关掉后再点同一行打不开第二次。
- ⚠️ **左树要显式 `HorizontalContentAlignment=Stretch`**：`Expander` 的 header 与 `ListBox` 项默认靠左，底色只包住文字 ⇒ 每行宽窄不一；
  两个都设 `Stretch`（页面局部 `Style`）才等宽。
- 🔴🔴 **壳排版下发要等书装好**：`BookReady` 里先 `LoadIndex` 再 `ApplyTypography` —— 排版重排保滚动位置的逻辑只在有章时生效，顺序反了首屏会用默认排版。
- 🔴🔴 **排版回环**：壳 `TypographyChanged` → VM 写盘 → `settings.Changed` → `ShellTypographyReady` → 壳 `ApplyTypography`；靠 `_syncing` 闸门拦住「回灌再触发写盘」，⛔ 不要在回灌路径里再调服务写入。
- 🔴🔴 **缺章补抓要防重**：`ChapterMissing` 在 `ShowChapter` 与搜索跳章都会触发 ⇒ VM 用 `_fetchingIndex` 防同章重复抓；补抓完成发 `ChapterFetched` → 视图调 `ReloadChapter()`（保滚动位置），⛔ 不要整本重新 LoadIndex。
- 🔴 **壳标签页复用**：阅读页 VM 与壳都不随书重建 ⇒ 每次 `BookReady` 都要重新 `LoadIndex` + 重挂书签，书签按书目录隔离。
- 🔴 **正文缓存的「读」与「写」是两件事**：`ContentAsync` **总是**先查缓存（预下载写进来的也算），
  是否**写**缓存在关「缓存正文」时由预下载自己 `Put`。⛔ 别把它再绑回同一个开关——那样预下载就白下载了。
- 🔴 **书架「更新」不是「刷新」**：`🔄 刷新书架` 只重读本地库；`🆕 批量更新` 才回源站抓详情 + 目录（每本 2 个请求）。两者别混在一个命令里。
- 🔴 **首次检查只建基线**：没有上次的章数就没法算「多了几章」⇒ 第一次 `new_count` 必须是 0（否则一导入就满屏"有 999 章更新"）。
- 🔴 **检查失败要保住章数基线**：`ShelfStore.SetUpdate` 传了 `note` 时只写 `checked_at` / `update_note`，⛔ 不动 `chapter_count` —— 否则站点抽风一次，「有新章节」的对比基准就被清零了。
- 🔴 **改分组必须同时回写 `rules_json`**：导出走的是导入原文（`RawJson`），只改 `group_name` 列的话，导出再导入分组就退回去了（`SourceStore.PatchGroup`）。
- 🔴 **批量作用域只有一个来源**：`BatchSelection`（绑 `TableView.SelectedItems`）非空就用它，否则退回当前行；⛔ 别再加一套「勾选列」的选中状态，两处会打架。
- 🔴 **非文本源要从三处一起挡**：判定（`SourceAnalyzer` 先判 `Type != 0`）、候选池（搜索只收 `Ready`；发现页显式排除 `Unsupported`）、校验（直接标「跳过」）。少挡一处就会出现「搜得到、点开是空正文」。
- 🔴 **校验只写三列、不碰别的**：`check_state` / `check_note` / `checked_at` 由 `SourceStore.SetCheck` 单条更新；重新导入走 `ON CONFLICT` 更新时**故意不覆盖这三列**（重导入不该把校验结论清成"未校验"）。
- 🔴 **替换规则有两套语法，⛔ 混用**：书源内联 `ruleContent.replaceRule` 是 legado 三段 `规则##正则##替换`
  （`RuleSyntax.SplitReplace`）；设置页的**全局规则**是本机两段 `正则##替换`（`ReplaceRuleService`，行首 `#` 为注释）。
- 🔴 **听书不能引 `System.Speech`**：本模块 TFM 是 `net10.0`，那个包只支持 `net10.0-windows`（引了直接 NU1202）。
  走 `Type.GetTypeFromProgID("SAPI.SpVoice")` + `InvokeMember` 后期绑定；非 Windows 时 `Type.GetTypeFromProgID`
  直接抛 ⇒ 先 `OperatingSystem.IsWindows()` 守卫，再把失败原因写成可见文案（页面提示装语音包）。
- 🔴 **暂停 / 停止只能落在句子边界**：`SpVoice.Speak` 是同步调用，中途没有回调可以打断 ⇒ 切句必须够细
  （按行 + 句末标点切，超长句再按逗号切到 90 字内），否则按一次暂停要等十几秒。⛔ 别为了"精确暂停"去用 SAPI 的
  `Pause/Resume`：那是跨线程调 COM 对象，且 RunState 在暂停时不可靠。
- 🔴 **朗读对象同一时刻只能一个线程用**：改语速 = Stop 后立刻 Start（从当前句重起），两个任务会抢同一个 `SpVoice`
  ⇒ `RunAsync` 里用 `_speakGate` 串行；旧任务靠 token 取消快速退出，新任务等它一下即可。
- 🔴 **朗读线程 ⇒ 一律 `Dispatcher.UIThread.Post` 改属性**：`SegmentStarted` 在后台线程触发，直接改
  `ObservableProperty` 会在 Avalonia 里抛「不在 UI 线程」。

## 6. 待办（未立项）
- ~~书源编辑器 / 规则调试面板~~ ✅ 已实施：工具条「✎ 编辑」（原始 JSON + 解析校验）与「▶ 试跑」（四段管线各跑一遍，
  逐段报耗时与失败规则，`SourceDebugService`）。
- ~~正文全局替换净化~~ ✅ 已实施：`ReplaceRuleService` + 设置页「替换规则」节，在 `ContentAsync` 末尾、书源内联规则之后执行。
- ~~阅读时预下载~~ ✅ 已实施：`PrefetchService`（默认 5 章，0 = 关），写正文缓存。
- ~~整本下载合并~~ ✅ 已实施：`BookDownloadService` + 阅读页「⬇ 下载」，合成单份 TXT。
- ~~书源分组管理与批量操作~~ ✅ 已实施（批8）：页内筛选条（类型 / 分组 / 校验）+ 列表多选 + 批量启停 / 导出 / 删除 + 「📁 改分组」。
- ~~书源校验~~ ✅ 已实施（批8）：`SourceCheckService` 逐源跑一次搜索，结论落库三列，支持「只看失效」与「删除全部失效」。
- ~~书架批量更新~~ ✅ 已实施（批9）：`ShelfUpdateService`（详情 + 目录 → 比章数）+ 「更新」列 + 筛选 + 「只看有更新」；`shelf` 表补 5 列（schema v5）。
- ~~TTS 听书~~ ✅ 已实施（NT-133）：`TtsService` 走 Windows 自带 SAPI（COM 后期绑定）逐句朗读 + 暂停 / 语速 / 停止，设置页「听书」节管语速 / 音量 / 音色。~~本地 TXT 导入~~ ✅ 已实施（NT-144 批 3）：阅读页「📂 本地 TXT」→ `TxtBookService` 切章 → `ExportLocalText` 落盘索引书。仍未立项：**EPUB 导入**、**RSS 订阅**、**书架收编本地书**。
- ~~正文缓存~~ ✅ 已实施（NT-129）：`content_cache` 表 + `ContentCacheStore`，`ContentAsync` 首查尾写、`cache-enabled` 开关。
- ~~换源~~ ✅ 已实施（NT-129）：多源并发比对 → 按「书名匹配度 → 速度」排序 → 换源浮层选源原地续读（⛔ 不再带书名回搜索页）。
- **`java.*` 未覆盖的少数派**：~~`t2s` 繁转简~~ ✅ 已实施（NT-129，`TraditionalToSimplified` 内置映射）；`java.webview` / `startBrowserAwait` 桌面端不实现（这类源已判「需 WebView」）。
- ~~发现页 `@js:` 动态分类~~ ✅ 已实施（NT-129）：`ParseExploreCategories` 改实例方法，`@js:` 段执行 JS 生成分类表。

