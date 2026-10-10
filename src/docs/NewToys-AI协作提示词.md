# NewToys · AI 协作提示词

> v1.30 · 2026-10-10 · 会话启动包（面向**本地执行型** AI 客户端：可直接读写 `src/`、跑命令）
> 用法：新会话开头贴「正文」，末尾写本次需求。
> 本文件只管**流程 + 索引 + 自检**；细则一律引用另三份，⛔ 不在此复述（单一权威 → 防漂移、省 token）。
> 流程设计目标：**分级读档省 token、中段闸门提成功率**。

---

## 正文

```text
你是 NewToys 的开发助手，工作区根=能看到 src/ 的那一级。按下述流程执行，不跳步。

[0] 分型 + 分级读档（token 第一关：只读用得上的）
① 先读小文件：本文件 + src/docs/NewToys-项目现状与进度.md 的「一、当前基线」。
② 判任务型，按下表【只读规范相关章节】，⛔ 不整篇读规范：
   改/加页面      → 规范 §3.3 §4.2 §5.2 §6.5
   设置类页面     → 规范 §3.4 §3.5 §9
   列表页         → 规范 §3.6
   新增模块       → 规范 §4.4 §6.1-§6.4
   主题/样式      → 规范 §3.3 §3.4 §5.2
   框架层(先问)   → 规范 §4.3 §8
   热加载模块     → 规范 §6.6
   接口/契约      → 规范 §4.1 §4.4 §6.1 §6.2
   修报错         → 规范 附录A + §5.5
   纯文档         → 规范 §0
③ 冲突以 src/ 源码为准并回写文档。规范 〔A〕=硬约束必守；〔B〕=可动但需理由+用户同意+登记 §12 台账。
④ 宪法（开发需求）极少读：仅在判「做不做/是否越界」时读。

[1] 摸底（定向检索，不整读）
grep/read 目标文件 + 相邻同型文件（照抄其风格）；只读相关行区间；⛔ 不读 artifacts/ bin/ obj/。

[2] 变更声明（动手前先给，防跑偏/防超范围）
列出：① 要改的文件清单（增/改/删，逐条一句话）② 验收标准（怎么算做完）③ 已知风险/需用户拍板点。
范围清楚再编码；小改动（单文件、纯文案/配色）可口头一句带过。

[3] 编码速记（跨章节高频易错；细则见 [0] 表指向的章节）
· 栈锁 .NET10/Avalonia12.1.3/CT.Mvvm8.4.2/MEDI9.0.0；禁第三方 UI 库；包版本只在 Directory.Packages.props，csproj 不写 <PackageVersion>。
· 分层单向 Shared←Core←Shell←Module.*；模块禁引 Shell、禁互引；Shared/Core 不引 Avalonia。
· 框架层(Shared/Core/Shell)冻结，新功能只在 Module.*；改框架层先问。
· 命名空间 NewToys.<模块>.Views/.ViewModels（无 .Module 段）；View(.axaml+.axaml.cs) 与 VM 成对、跟同模块风格。
· IModule{RegisterServices,Initialize}；菜单只在 Initialize 用 RegisterModule/RegisterFunction 注册；viewName=View 类名；Key=小写连字符；分组必显式 Order，settings=999 isBottom。
· .axaml 扩展名；编译绑定关→写 {Binding}、不写 x:DataType；改 .axaml 必同步 .axaml.cs；⛔ WPF 写法(IsCheckable/ItemContainerStyle/Border.HorizontalContentAlignment/TargetType)。
· 颜色一律 DynamicResource；主题键只用 *Brush 语义键(32个)，禁旧键 AppBg/TextPrimary 等；code-behind 不写死 #RRGGBB。
· 设置：立竿见影的即时生效(禁保存按钮)，多字段表单显式保存；每页≥1 行 StatusText；设置数据落 config\*.json、模块只读、唯一写入点在设置页。
· 用户数据 %LocalAppData%\NewToys\{config,data,logs}，NEWTOYS_HOME 整根切换；路径一律 Path.Combine。
· Modules\ 是模块产物唯一去处(OutDir 直出，禁拷贝步骤)；热加载只读本地 Modules\、逐 DLL try-catch 隔离、不联网。

[4] 分型静态预检（交付前必跑；不能本地编译 → 把编译错消灭在这）
· 通用：.axaml ↔ .axaml.cs 同步？using/类型/命名空间对得上？引用的主题键都在 32 键内？改动的 VM 字段名全仓搜过旧名(含 nameof)？
· 加模块：slnx + Shell.csproj(Debug 条件) + App.axaml.cs #if DEBUG 三处都登了？
· 改列表页：表头与行同套星号列定义？内边距一致？ContextMenu.DataContext 行菜单+表头菜单都在 code-behind 同步了？隐藏列用宽度 0？

[5] 交付
① 变更清单（增/改/删逐条一句话）+ 前置基线 + 用户需本地执行的验证命令（build.ps1 等）。
② 直接写工作区，用 read/ls 确认落盘；不列下载链接、不打 zip、不做 sources md 快照。
③ 改完一个功能同步台账「当前基线/已完成/待办」并 `git commit`（`<type>: <简述>`）；**纯修报错不回写台账**。⛔ 不写 md 日志——版本记录一律走 git。
④ ⛔ **不逐次改文档**：需求/规范/提示词是指导性文件，只在其内容真的过时/与代码冲突时才改；各文档**独立升版本**（不齐步），**《开发需求》冻结**。逐次变更历史一律看 git。
⑤ 停手规则——**必须问**：要改框架层 / 要删用户已确认的实现 / 需求自相矛盾 / 文档与代码冲突且影响本次 / 缺关键标识符。
   其余一律**按最合理假设推进**，并在交付里标注假设；⛔ 不反复确认细节（省往返 token）。

[6] 自检（全过再发）
☐ 改动真实落盘？             ☐ 框架层动过已先问？
☐ .axaml 同步 .cs？           ☐ 新增模块三处登记？
☐ 主题键只 *Brush、无旧键？   ☐ 列表页合 §3.6、ContextMenu 已同步？
☐ 设置落 config\、模块只读？  ☐ 模块 DLL 无拷贝、OutDir 直出？
☐ 未谎报编译、写清用户命令？ ☐ 台账按功能级更新、版本号齐步？

本次需求：
（在此粘贴需求/参考源码/报错信息）
```
