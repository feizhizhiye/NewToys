# 模块 · 阅读（Reading）

> **程序集**：`src/Module.Reading`（`NewToys.Reading`）；正文渲染复用 `src/UI/Viewers/TextViewer`
> **模块键**：`reading` ｜ **一级菜单**：Order 56 · 图标 `IconReading`
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：批1 已实施（NT-125，纯选择器书源 + 书架 + 发现）；JS 书源留 NT-126
> **最后校对**：2026-11-01（批1 落地：书源导入 / 规则求值器 / 四段管线 / 两页；对照 `SourceStore` / `RuleEvaluator` / `CrawlService`）

---

## 1. 职责与边界

- **定位**：抓网络内容（书源），与「本地浏览」的 `Module.Viewer` **是两个独立模块**。
- 一句话链路：**导入 legado 书源 → 输入关键词 → 出结果 → 点开看目录 → 点章看正文**。
- 🔴 本批次**只跑纯选择器书源**（真实数据里 67.4%）。含 `@js:` / `<js>` 的源在书源页标成「需 JS」，管线直接拒跑并给可读原因。
- ⛔ 不做：书架 / 阅读进度接界面（批3）、发现页、下载、书源编辑器、在线同步。
- 🔴 **GPL 边界**：legado 是 GPL-3.0，本项目 MIT ⇒ ⛔ 不移植其 Kotlin 源码、⛔ 不翻译其实现；只按官方文档 + 真实书源行为重写。
- 🔴 依赖只加 `AngleSharp`（本就在 `Directory.Packages.props`）；⛔ 不引 XPath 库（383 条真实书源里 XPath 用法为 **0**）。

## 2. 当前功能（2 页）

| 菜单 Key | 标题 | Order | View | 说明 |
|---|---|---|---|---|
| `reading-sources` | 书源 | 1 | `SourceListView` | 导入 / 导出 / 启停 / 删除书源；每条标「可用 / 需 JS / 不完整」 |
| `reading-explore` | 发现 | 2 | `ExploreView` | 按书源自带的 `exploreUrl` 分类表浏览书单（不用输关键词） |
| `reading-search` | 网上搜索 | 3 | `SearchView` | 一页三段：选源 + 关键词 → 结果 → 目录 + 正文；可加入书架 |
| `reading-shelf` | 书架 | 4 | `ShelfView` | 加进来的书；双击 / 「继续阅读」回到目录与正文 |

**导入格式**：legado 书源 JSON，兼容**顶层数组** `[{…}]` 与 `{"bookSources":[…]}` 两种形态；编码按 BOM → 严格 UTF-8 → GB18030 嗅探。

**三档判定的依据（静态，不发请求）**
- `可用`：搜索 / 目录 / 正文三段的必填字段齐全，且不含 JS。
- `需 JS`：三段任一规则含 `@js:` / `<js>` / `{{函数调用()}}`。
- `不完整`：缺 `searchUrl` / `ruleSearch.bookList` / `ruleToc.chapterList` / `ruleToc.chapterUrl` / `ruleContent.content` 任一。

## 2.5 操作逻辑

| 操作 | 触发 | 结果 / 状态变化 | 约束 |
|---|---|---|---|
| 导入书源 | 书源页工具条「📥 导入书源」 | 按 `bookSourceUrl` **幂等更新**（⛔ 不产生副本），状态栏报「新增 N / 更新 M / 跳过 K」 | 只认 `.json`；单文件 |
| 搜索书源 | 工具条搜索框输入 | 列表即时过滤（名称 / 分组 / 状态 / 地址） | ⛔ 不落盘，下次进页清空 |
| 启用 / 停用 | 工具条「✅ 启用」/「⏸ 停用」，或右键 | 改库里的 `enabled`；停用的源不出现在搜索页 | 无选中时按钮置灰并给提示 |
| 导出选中 | 工具条「📤 导出选中」 | 把选中书源**原样**导成 JSON 数组（含未来字段，不重新拼） | 导出的是原始 JSON 文本 |
| 删除 | 工具条「🗑 删除」/ 右键 | 二次确认后删库 | 不可撤销 |
| 用源搜索 | 右键「🔍 用这个源搜索」 | 跳到「网上搜索」并**预选**该源 | 只预选，不自动发起搜索 |
| 搜索 | 搜索页输关键词 + 回车 / 点「🔍 搜索」 | 选中的源**并发**抓（默认 3 并发），结果合并一列；顶部显示「已完成 N / 总数」 | 只列「可用 + 已启用」的源；默认全选，可用「☑ 全选 / ☐ 全不选」调整 |
| 加入书架 | 结果列表右键「＋ 加入书架」，或顶部按钮 | 按详情地址幂等入库（重复加入只更新书名 / 作者） | 书架在「阅读 → 书架」 |
| 发现浏览 | 阅读 → 发现 → 选源 → 点左列分类 | 中间列出该分类的书单；双击一本 = 去搜索页读 | 只列 `exploreUrl` 非空的源；规则用 `ruleExplore`，没给就退回 `ruleSearch` |
| 继续阅读 | 书架里双击，或工具条「📖 继续阅读」 | 跳到「网上搜索」并把这本书直接打开 | 源被删 / 停用会明确提示，⛔ 不静默空白 |
| 换章 | 正文上方「▲ 上一章 / ▼ 下一章」 | 在已加载的目录里前后翻 | 与点目录条目等价 |
| 看目录 | 点结果任意一条 | 自动读详情 → 目录，右侧列章，默认选中第 1 章 | 目录翻页最多跟 20 页（防死循环） |
| 看正文 | 点章节 | 右栏正文渲染；`nextContentUrl` 续页最多拼 10 页 | 正文 ⛔ 不执行脚本、⛔ 不加载远程资源 |
| 取消 | 搜索页「✖ 取消」 | 中断正在进行的抓取 | 每步都带同一取消令牌 |
| 失败提示 | 任一步失败 | 错误条显示「哪一步 · 哪条规则 · 什么原因」 | ⛔ 不只显示"失败了" |

## 3. 实现逻辑

```
SourceListView ──► SourceListViewModel ──► SourceStore ──► store\reading\reading.db(book_sources)
                        └────────────────► SourceImportService（legado JSON → BookSource → 幂等 upsert）
SearchView ───────► SearchViewModel ─────► CrawlService
                                             ├─ SearchAsync   searchUrl（含 ,{…} 请求体）
                                             ├─ DetailAsync   ruleBookInfo.tocUrl
                                             ├─ ChaptersAsync ruleToc.chapterList + nextTocUrl
                                             └─ ContentAsync  ruleContent.content + nextContentUrl + replaceRule
                                                   └─► RuleEvaluator（RuleSyntax 切分 + AngleSharp 选择器 + JsonPathLite）
```

- 🔴 **求值器返回联合体** `RuleValue`（`Texts` / `Nodes` / `JsonItems`）。压成 `List<string>` 就无法「先拿书单节点集、再在每个节点内求字段」，条目会按索引错位（旧抓取引擎的返工根因）。
- 🔴 **会话状态在 `RuleContext`**：`Root`（文档 / 条目节点）、`JsonRoot`（条目 JSON 元素）、`Vars`（`key`/`page`/`baseUrl`…）、`Store`（`@put`/`@get` 字典）、`Errors`（可读告警）。派生上下文（`ForElement` / `ForJson` / `WithDocument`）**共享 Vars / Store / Errors 引用**。
- **切分顺序**：`{{}}` → `||` → `&&` → `##`。`##` 后两段是「正则 / 替换串」，⛔ 不参与规则切分；`@js:` 片段整条放弃。
- **请求层**：`HttpClient`（跟随重定向 / 自动解压）、`User-Agent` 与超时来自设置、失败重试 1 次（404/403 不重试）、并发门 `SemaphoreSlim`。
- 🔴 **正文清洗**：`ruleContent.content` 常写成 `…@html`，拿回来带标签 ⇒ `StripMarkup` 去标签、块级收尾转换行、解 HTML 实体后再交给 `TextViewer`。
- **发现页**与**搜索**共用 `ParseItems`（同一套「书单 → 每条目内求值」），区别只在地址来源（`exploreUrl` 的分类 vs `searchUrl` 的模板）与规则（`ruleExplore` 退回 `ruleSearch`）。
- **书架**只做「存 + 列 + 跳」，阅读器仍在搜索页（`TextViewer` 只有一份实现）。

## 4. 数据与配置

| 项 | 落点 | 写入点 |
|---|---|---|
| 书源（`book_sources`） | `store\reading\reading.db` | `SourceStore`（upsert / delete / setEnabled / touchUsed） |
| 书架（`shelf`） | 同上（**批3 才接界面**） | — |
| 阅读进度（`reading_progress`） | 同上（**批3 才接界面**） | — |
| 并发 / 超时 / UA / 缓存开关 | `config\reading.json` | 本模块设置页（批1 只读默认值） |

- 🔴 **一个模块一个库**：三张表只在 `reading.db`；`ReadingSchemaContributor` 在首帧后幂等建表（`DatabaseKey = "reading"`）。
- 表结构：`book_sources(url PK / name / group_name / source_type / enabled / rules_json / last_used_at)`；`rules_json` 存**导入原文**，后续补 JS 兼容层时不必重新导入。

## 5. 坑点（实测）

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
- ⚠️ **GBK 站点要按 GBK 编码搜索词**：`searchUrl` 里出现 `ie=gbk` / `gb2312` / `gb18030` 时，`{{key}}` 必须用 GB18030 逐字节百分号编码（UTF-8 编码的 key 到 GBK 站点等于乱码 ⇒ 0 结果）。

## 6. 待办（未立项）

- **批2（NT-126）JS 兼容层**：`@js:` 片段 + 20 个 `java.*` API（审计报告 `skill/reports/legado-audit-2026-11-01.md`）。
- **阅读进度**（`reading_progress`）：表已建，界面未接（书架只记「加了什么书」，不记读到第几章）。
- **发现页分页**：当前只抓分类第 1 页；翻页需按 `{{page}}` 递进。
- **正文缓存**：`config\reading.json` 的 `cache-enabled` 已读，尚未落地缓存层。
