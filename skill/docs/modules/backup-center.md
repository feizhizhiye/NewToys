# 模块 · 备份中心（Backup Center）

> **程序集**：`src/Module.BackupCenter`（`NewToys.BackupCenter`，产物 `Module.BackupCenter.dll`）
> **模块键**：`backup` ｜ **一级菜单**：Order **60** · 图标 `IconBackup`（StreamGeometry）
> **上级文档**：框架层 `../framework.md` ｜ UI 规范 `../ui.md` ｜ 台账 `../NewToys-项目状态与路线图.md`
> **状态**：✅ 已实施（NT-117 + NT-119 本地闭环 + NT-118 邮件投递 + NT-120 备份邮件页）｜⏳ 待用户本地 `accept.ps1` 验收；WebDAV 通道属 v2
> **最后校对**：2026-10-28

---

## 1. 职责与边界

- **定位**：把指定目录压成**加密归档包**放在本地，并能按原样还原回去；同时用一张月历管「哪天备份过 / 哪天打算备份」。判据 = 产出**一个可搬运的包**，不产生业务数据。
- 🔴 加密后端 = **外置 7-Zip**（`7z a -t7z -mhe=on -mx=5 -p<密码>`，AES-256 + 头部加密）；⛔ 不自带压缩库、⛔ 不用 DPAPI（保持跨平台中立）。
- 🔴 **加密防的是什么**（用户 2026-10-28 定调）：主要是**包离开本机之后**被查看 —— 传到网盘 / 云端 / 别人拿到文件时，`-mhe=on` 连文件名与目录结构都看不到。它⛔ 不是防本机他人登录（那要靠系统账户）。
- ⛔ 不复用 Core 的 `BackupService`（那是「配置档备份」），本模块类名一律 `Archive* / Vault* / BackupPlan*`。
- 本期**已做本地闭环 + 邮件投递**：手动触发 + 全量归档 + 本地保留轮转 + 日历待办备忘 + 邮件分卷投递 + 邮箱侧搜索与还原。定时 / 增量 / 断点续传 / WebDAV 属 v2。
- 密码只能走命令行 `-p`（7z 无 stdin），因此**包级独立密码** + 密码库加密 + 界面只显示指纹（`sha256(密码)[0..8]`）。

## 2. 当前功能（4 页 + 1 设置页）

| 菜单 Key | 标题 | Order | View / VM | 说明 |
|---|---|---|---|---|
| `backup-plans` | 备份任务 | 1 | `BackupPlansView` / `BackupPlansViewModel` | 任务清单（名称 / 源目录 / 保留份数 / 最近运行 / 包数 / 包密码指纹）；工具条 新建 / 编辑 / 立即备份 / 取消 / 复制密码 / 删除 / 搜索 / 包目录；行右键同套命令；编辑走页内弹层 |
| `backup-archives` | 备份包 | 2 | `BackupArchivesView` / `BackupArchivesViewModel` | 本地包清单（文件 / 任务 / 大小 / 时间 / SHA256 前 12 位 / 加密=是）；重新扫描对账 / 还原（弹层填目标目录 + 冲突策略）/ 删除包 / 复制 SHA256 |
| `backup-calendar` | 备份日历 | 3 | `BackupCalendarView` / `BackupCalendarViewModel` | 6×7 月历（备份数 / 待办数 / 失败红点 / 今天）+ 右栏当天明细（待办可勾可删、当天包、当天运行记录）+ 加待办 |
| `backup-mail` | 备份邮件 | 4 | `BackupMailView` / `BackupMailViewModel` | IMAP 收信当邮箱管理器（全量列表 / 详情 / 附件下载 / 删信）+ 搜索（索引号 / 主题 / 发件人）+ 备份邮件「📥 下载并还原」 |
| `settings-backup` | 备份中心设置 | 100 | `SettingsBackupView` / `SettingsBackupViewModel` | 4 节：主密码（设置 / 解锁 / 锁库 / 更换）、保留与**默认备份目录**、**邮件投递**（SMTP/IMAP + 授权码 + 分卷 + 待发目录）、文件位置（含 SQLite 库路径） |

## 2.5 操作逻辑（交互契约）

> **列表本体**：`<v:TableViewHost>`（原生表格）。点表头排序、拖列边界调列宽（会话内有效）；**表头右键只管列宽与列显隐，行右键只管功能；筛选统一走顶区 `FilterBar` 筛选条**。详见 `../ui.md` §6.3c。

### 备份任务 `backup-plans`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 新建 / 编辑 | 工具条 ➕ / ✏ | 弹层（任务名 / 源目录多行 + 添加移除 / 排除规则 / **投递方式**（本地 or 发邮箱）/ 保留份数 / 备注）→ 「💾 保存」落库 | 任务名必填且自动去重；源目录至少 1 个；保留份数 1~999 |
| 添加源目录 | 弹层「➕ 添加目录」 | 走 `DialogHooks.PickFolderAsync` 选系统目录追加到清单 | 同一目录不重复加入 |
| ▶ 立即备份 | 工具条 / 行右键 | 本地通道：探测 7z → 取包密码（无则生成并入密码库）→ 7z 建包 → 算 SHA256 登记 → 按保留份数删最旧 → 写运行记录。**邮件通道**：领索引号 → 7z 分卷（`-v30m`）→ 逐卷 SHA256 → 先落库 → `7z t` 自检 → 逐封发送（间隔 3 s，重试 3 次）→ 记 3 天后清理 | 密码库**未设置 / 未解锁**一律拒跑并给方向；`IsBusy` 期间按钮置灰，顶部出现阶段条（收集 / 压缩加密 / 发送 n/m 卷） |
| 取消 | 工具条 ⏹ / 关页 | 杀 7z 进程树 + 删半成品包 | 取消不计失败，运行记录写「已取消」 |
| 复制包密码 | 工具条 📋 / 行右键 | 复制到剪贴板 | 需解锁；界面⛔ 不回显密码原文，只显示指纹 |
| 重生成包密码 | 行右键 | 确认后生成新密码写库 | 🔴 **旧包只能用旧密码打开**（确认框写明） |
| 删除任务 | 工具条 🗑 / 行右键 | 二次确认 → 删任务 + 删其密码库条目 | ⛔ 不删已生成的归档包 |
| 搜索 | 工具条关键字框 | 任务名 / 源目录 / 备注即时过滤 | 纯内存 |
| 排序 / 列显隐 | 点表头 / 表头右键 | 6 列；标识列（任务）不可隐藏 | 偏好落 `columnlayout.json`，页键 `backup-plans` |
| 详情栏 | 工具条末位详情开关 | 声明式详情卡：任务字段 + 源目录列表 + 最近 5 次运行 + 归档引擎（默认备份目录 / 密码库状态 / 加密说明） | 开合状态落 `columnlayout.json` |

### 备份包 `backup-archives`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 重新扫描 | 工具条 🔄 | 按默认备份目录对账：补新包（顺带算 SHA256）、剔掉文件已不在的记录 | 手工拷进/删掉的包都能反映出来 |
| ↩ 还原 | 工具条 / 行右键 | 弹层填目标目录 + 同名策略 → 解包到暂存目录 → 逐条目校验名 → 按策略搬到目标 | 需解锁密码库；包没登记任务（手工拷入）取不到密码 ⇒ 拒绝 |
| 同名策略 | 弹层下拉 | 跳过（默认）/ 覆盖 / 重命名（加 `(1)(2)`） | 默认「跳过」⛔ 不动目标目录已有文件 |
| 删除包 | 工具条 🗑 / 行右键 | 直接从磁盘删 + 剔清单记录 | 不可恢复，⛔ 无二次确认（与「删除任务」刻意不同：任务删了包还在，包删了就是删数据） |
| 复制 SHA256 | 工具条 📋 / 行右键 | 复制整包校验值 | 拷到别的机器后先比它 |
| 搜索 / 排序 / 列显隐 / 详情栏 | 同其它列表页 | 6 列；默认按生成时间倒序 | 页键 `backup-archives` |

### 备份日历 `backup-calendar`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 选某天 | 点格子 | 右栏换成那天的待办 / 归档包 / 运行记录 | 前后补齐的淡字格子也能选（看得到那天的记录） |
| 翻月 / 回今天 | 顶部 `‹` `›` / 「今天」 | 重画 42 格并选中该月 1 号（回今天则选中今天） | 周一为一周开头 |
| ＋ 加待办 | 右栏底部输入框 + 按钮 | 写 `todos` 表（关联选中日期，可留空任务） | ⛔ 不触发自动备份；标题为空拒绝 |
| 勾完成 / 取消 | 待办前的 `☐` / `☑` | 写 / 清 `done_at` | 完成后待办仍显示（不自动消失） |
| 删待办 | 待办后的 🗑 | 直接从库里删 | 不可恢复，无二次确认（一条备忘） |
| 🔄 刷新 | 工具条 | 重读库 + 重画月历 | 在别的页备份完回来点一下 |
| 📂 默认备份目录 | 工具条 | 打开归档包所在目录 | 目录不存在则先建 |

### 备份中心设置 `settings-backup`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 设置主密码 | 首次「🔐 设置主密码」 | PBKDF2-SHA256（30 万次）派生 → 建库并解锁 | 至少 8 位、两次一致；🔴 丢了主密码，包密码取不回来 |
| 解锁 / 锁库 | 「🔓 解锁」/「🔒 锁库」 | 解锁仅在**本次会话**有效 | 连续失败 5 次 ⇒ 本会话锁库（重启才可重试） |
| 更换主密码 | 已解锁时的「💾 更换主密码」 | 用旧密码解出全部条目 → 新密码重新加密 | 已生成的包不受影响（各用自己的包密码） |
| 保留份数 | 「💾 保存」 | 只影响新建任务的初值 | 1~999 |
| 默认备份目录 | 输入框 +「📂 选择」/「💾 保存」/「恢复默认」/「↗ 打开」 | 落选项；留空 = `%USERPROFILE%\NewToys\Backup` | 改目录**不会**搬已有的包（文案写明） |
| 保存邮件配置 | 「💾 存入密码库」 | 主机 / 端口 / 账号 / 收件人 / 分卷大小 / 待发目录写 `config\backup-center.json` 的 `mail` 键；授权码写**密码库**（条目 `mail.auth`） | 端口 1~65535；授权码输入框留空 = 保留库里原来的 |
| 清除授权码 | 「🗑 清除授权码」 | 删密码库条目 `mail.auth` | 需解锁；清掉后投递 / 收信都不可用 |
| 🔌 测试连接 | 按钮 | 依次试 SMTP 登录 + IMAP 登录（只连不发信） | 结果两段拼一行显示；失败按「授权码错 / 网络 / 端口」分级提示 |
| 分卷大小 | 下拉 10 / 20 / 30 MB | 决定 7z `-v` 的切分大小 | 🔴 上限 30：邮件走 base64 编码，体积涨约 1/3 |
| 待发目录 | 输入框 +「📂 选择」/「↗ 打开」 | 分卷先落这里再发信；留空 = `<默认备份目录>\outbox` | 与默认备份目录**两套规则**（这里发完即清，那里按份数轮转） |
| 🧹 清理已发送分卷 | 按钮 | 立即删掉所有 `status=sent` 的本地分卷（不等 3 天缓冲） | 服务器上的邮件不受影响 |

### 备份邮件 `backup-mail`

| 操作 | 触发 | 结果 | 约束 |
|---|---|---|---|
| 列表 | 进页 | 先读本地缓存 `mail_index`（离线也能翻历史） | 显示主题 / 发件人 / 日期 / 附件数 / 「备份」标记 |
| ⟳ 同步 | 工具条 | 按 UID 增量拉收件箱新邮件头 → 写缓存 → 刷新列表 + 服务器封数 | 首次只拉最近 300 封（`SyncBatch`）；`IsBusy` 期间给阶段文案 |
| 🔍 搜索 | 工具条 | 本地 SQL：主题 / 发件人 / 索引号 模糊匹配 | 纯本地，秒出 |
| 🔍 搜索服务器 | 工具条 | 先同步再在本地过滤 | 关键词为空直接拒绝 |
| 看详情 | 选中一行 | 读正文（纯文本；HTML 兜底剥标签）+ 附件清单 | 🔴 用 `BODY.PEEK`，⛔ 不改已读状态，⛔ 不渲染 HTML |
| ⬇ 保存附件到… | 附件列表下按钮 | 弹层选目录 → 下载同名附件 | 只取 `Path.GetFileName`，⛔ 不信任邮件里的路径 |
| 📥 下载并还原 | 备份邮件详情 | 按索引号 IMAP `SUBJECT` 搜索 → 下载全部分卷到暂存 → 逐个比对 db 里的 SHA256 → `7z x .001` → 复用还原（冲突策略下拉） | 缺卷 / 校验不过 / 密码库没解锁 ⇒ 明确中止并说明原因；暂存目录 `finally` 删 |
| 🗑 删除这封邮件 | 详情底部 | 二次确认 → 打 `Deleted` 标记 + `EXPUNGE`，同时移除本地缓存行 | 🔴 只删服务器那一封；⛔ 不做自动批量清理 |

## 3. 实现逻辑

- **存储 = 一个 SQLite 库** `store\backup\backup.db`（`BackupDb : SqliteDatabase`，表：`plans` / `archives` / `runs` / `todos` / `vault_meta` / `vault_items`）。
  - 为什么不继续用四个 JSON：日历要「按天聚合备份次数」，那是 SQL 的 `GROUP BY`；JSON 只能每次全量读进内存再 LINQ。
  - 四个存储类（`BackupPlanStore` / `ArchiveCatalog` / `BackupRunLog` / `VaultService`）**公开 API 不变**，内部换实现 ⇒ 页面 VM 零改动。
  - 旧 `config\backup\*.json` 首次启动一次性导入，写 `meta.migrated.<名字>` 标记；⛔ 旧文件不删（迁移出错还能翻查）。
- **一个任务 = 一个包密码**：密码存在密码库里，条目 Id = 任务 Id（改名不影响）；包密码指纹用 `sha256(密码)` 前 8 位展示。
- **建包**：`ArchiveService.CreateAsync` → `IProcessRunner`（隐藏窗口 + 杀进程树 + 取消）跑 7z，超时上限 6 小时；退出码 1 当「完成（有跳过）」而非失败（源目录遇独占文件不改判整个任务）。
- **还原**：解到 `cache\backup\staging\restore-<时间戳>` → 枚举文件校验（拒绝 `..` / 绝对路径 / 盘符 / `ReparsePoint`）→ 逐文件按策略搬运 → 记录本次创建的文件，异常/取消时**回滚这些文件** → `finally` 删暂存目录。
- **对账**：`ArchiveCatalog.SyncWithDisk` 用「文件名在不在磁盘」双向对齐；SHA256 只给新出现的文件算（已有记录不重算，避免每次进页都读整包）。
- **保留轮转**：任务跑成功后按 `KeepCount` 取最新的 N 条记录，多出来的先删文件再删记录；删不掉（被占用）就留着下轮再删。
- **日历聚合**：格子备份数取 `archives.created_at`（有包才算备份过，历史比 200 条运行记录长），失败红点取 `runs` 里状态含「失败」的条数，待办数取 `todos.due_date` 分组。
- **状态反馈**：阶段文案 + 不确定进度条（7z 不流式回报百分比）；失败按 7z 退出码分级（2 致命 / 7 参数 / 8 内存 / 255 中断）。
- **邮件投递**（`Services/Mail/`）：
  - `BackupMailService.DeliverAsync` = 领索引号（`MailJobStore.NewIndexNo`，`mail_seq` 按日事务自增）→ `ArchiveService.CreateAsync(volumeBytes)` 分卷建包 → 逐卷 SHA256 + 合并 SHA256（流式增量，不整包读内存）→ **先落库再发信** → `TestAsync` 自检 → 逐封 `SendOnceAsync`（重试 3 次，3/6/12 s 退避，封间 3 s）→ `MarkJobSent(purgeAfter = now + 3 天)`。
  - `ImapMailService` = 收信侧：增量同步邮件头、`BODY.PEEK` 读详情与附件、按 `SUBJECT` 搜索引号下载全部分卷、删信。**一次操作一条连接**（IMAP 空闲连接会被服务器掐，长连接要额外处理重连 / 心跳）。
  - `OutboxService` = 待发目录清理：`PurgeDue()` 只清 `status=sent` 且过了 `purge_after` 的；`failed` / `pending` 一律留着；删不掉的记日志且**不标已清理**（否则文件永远留盘）。
  - 索引号 = `NTB-yyMMdd-NNNN`；主题 `[NewToys备份] NTB-… · n/m`；附件名 = 分卷名 = `NTB-….7z.001`。🔴 主题与附件名里⛔ 出现任务名 / 源目录（邮件在服务器上是明文，索引号是唯一对外标识，映射只在本地 `backup.db`）。
  - `mail_index` 缓存让「打开页面就能搜」成立：搜索先查本地 SQL，需要更新时才连服务器。

## 4. 关键文件

| 路径 | 作用 |
|---|---|
| `Services/BackupDb.cs` | SQLite 库 + 六张表 + schema 版本 + 迁移标记 |
| `Services/BackupSettingsService.cs` | 唯一的选项写入点 + 全部路径的决定者（7z 路径转发到应用级 `env.json`） |
| `Services/VaultService.cs` | 主密码密码库（PBKDF2 → AES-GCM，条目级 nonce，密文进库） |
| `Services/PasswordGenerator.cs` | 24 位密码生成（剔除 `0OoIl1\|`）+ 指纹 |
| `Services/SevenZipLocator.cs` | 7z 定位与版本探测（带缓存，可 `Reset`） |
| `Services/ArchiveService.cs` | 建包 / 还原（路径穿越校验 + 回滚） |
| `Services/BackupPlanStore.cs` | `plans` 表读写 |
| `Services/ArchiveCatalog.cs` | `archives` 表 + 磁盘对账 + SHA256 |
| `Services/BackupRunLog.cs` | `runs` 表（最近 200 条）+ 按天查询 |
| `Services/BackupTodoStore.cs` | `todos` 表（日历待办） |
| `ViewModels/BackupPlansViewModel.cs` | 任务页（工具条 / 详情卡 / 编辑弹层 / 备份链路） |
| `ViewModels/BackupArchivesViewModel.cs` | 包页（对账 / 还原弹层 / 删除） |
| `ViewModels/BackupCalendarViewModel.cs` | 日历页（42 格重建 / 右栏三段 / 待办增删勾） |
| `ViewModels/SettingsBackupViewModel.cs` | 设置页（4 节） |
| `Models/MailModels.cs` | 邮件通道模型（配置 / 投递记录 / 分卷 / 列表行 / 附件 / 结果） |
| `Services/Mail/MailJobStore.cs` | `mail_seq` / `mail_job` / `mail_part` 读写 + 索引号分配 |
| `Services/Mail/MailIndexStore.cs` | `mail_index` 邮件头缓存（增量 UID + 本地搜索） |
| `Services/Mail/BackupMailService.cs` | 建包分卷 → 落库 → 自检 → SMTP 逐封发送 |
| `Services/Mail/ImapMailService.cs` | IMAP 收信（同步 / 详情 / 附件 / 引号下载 / 删信 / 登录测试） |
| `Services/Mail/OutboxService.cs` | 待发目录缓冲清理（3 天）+ 手工清理 |
| `Views/BackupMailView.axaml` | 备份邮件页（左列表 + 右详情两栏） |

## 5. 坑点（🔴 实测/必守）

- 🔴 **`-mhe=on` 必开**：只加密码不加密头部，7z 包里的文件名与目录结构仍可 `7z l` 列出，「加密」不成立。
- 🔴 **密码走命令行**：同机其他用户用进程查看器能看到 `-p...`；缓解只有「一任务一密码 + 密码库加密 + ⛔ 日志/错误串里不回显命令原文」。清空密码后**旧包永久打不开**。
- 🔴 **还原必须防路径穿越**：7z 解包本身不保证拒绝 `..`；先解到独立暂存目录再校验搬运（同时避免污染目标目录）。
- 🔴 **`Module.BackupCenter` 设置页的节内容必须放 `<UserControl.Resources>`**：在 XAML 里直接写 `ui:SettingsSection` 会让该页不被预编译（运行时报 `No precompiled XAML found`）；节内 ⛔ 再包 `ScrollViewer`（与 `SettingsSectionHost` 抢滚动）。
- 🔴 **类名与 View 配对**：`SettingsBackupView` ↔ `SettingsBackupViewModel`、`BackupCalendarView` ↔ `BackupCalendarViewModel`（Shell 按名字反查，对不上整页静默空白）。
- 🔴 **迁移只做一次**：导入旧 JSON 后必须写 `meta.migrated.*`，否则每次启动都把旧数据重新灌一遍（用户删掉的包会"复活"）。
- 🔴 **改默认备份目录不会搬包**：旧目录里的包留在原地，`archives` 表仍记着文件名 ⇒ 对账时会被判成"文件不在"而剔记录。换目录前先把旧包搬过去（设置页文案已提示）。
- 🔴 **日历格子用 `ListBox + UniformGrid(7×6)`**：选中态交给 `ListBox`，格子只是 ItemTemplate；⛔ 不要自己用 42 个 Button 手搓选中态（重画和键盘导航都要自己写）。
- ⚠️ 页内弹层用 `LoadingOverlayBrush` 做遮罩、`PanelBrush` 做卡面（⛔ 不写死色值）；弹层里的「移除选中源目录」不能靠 `$parent[ListBox]`（按钮在 ListBox 外），改由 VM 的 `EditorSourceSelected` 取。
- ⚠️ 7z 退出码 1 = 警告（多半是文件被独占跳过）；按 `Can not open` 行数估「跳过 N 个文件」只是近似。
- 🔴 **分卷只能在压缩时决定**（`7z a -v30m` 产出 `.7z.001/.002`），事后无法切分 ⇒ 邮件通道必须一开始就给 `ArchiveService.CreateAsync` 传 `volumeBytes`。
- 🔴 **48 MB 会超限，别信直觉**：邮件附件走 base64，体积膨胀约 1/3（48 MB → ~64 MB），而 QQ 单封普通附件上限 50 MB ⇒ 默认 30 MB（~40 MB）。超大附件是中转站链接（30 天过期、IMAP 拉不到本体），⛔ 不能用来做备份。
- 🔴 **主题 / 附件名里⛔ 放任务名或源目录**：邮件在服务器上是明文存储，包本身加密但主题没加密 —— 泄露的是"你备份了什么目录"。
- 🔴 **先落库后发信**：发到一半进程崩溃时，`mail_job` / `mail_part` 是唯一能说清「哪几卷已发」的记录；反过来（先发后记）会造出"邮箱有一半、本地不知道"的孤儿分卷。
- 🔴 **IMAP 一律 `BODY.PEEK`**：用会标记已读的取信重载，用户一进页面就把未读清空了。MailKit 里用 `GetMessageAsync(uid, ct)` —— 它内部走 `BODY.PEEK[]`（4.18 的 `IMailFolder` **没有** `(uid, items, headers, ct)` 四参重载，编译会报 CS1501）。
- 🔴 **下载附件的文件名是外部输入**：只取 `Path.GetFileName`，并且要求以索引号开头；⛔ 直接 `Path.Combine(dir, part.FileName)`。
- ⚠️ 首次同步只拉最近 300 封（`ImapMailService.SyncBatch`）：几万封的老邮箱全量拉会卡几十分钟，翻更早的邮件靠「搜索服务器」。

## 6. 待办（≤5，跨模块见 `framework.md` §11）

- v2 候选：WebDAV 通道（`IBackupTarget` 抽象位留着，⛔ 本期不做）、定时任务（计划触发器，可直接复用 `todos` 的到期数据）、增量 / 断点续传（7z `-u` 语义有限，需另设计）、VSS 快照（现在遇独占文件只跳过）、邮箱服务端容量自动清理（现由用户手动删信）。
