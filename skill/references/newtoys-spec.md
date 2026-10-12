# NewToys 特定约束（skill/references）

> 与 NewToys 项目绑定；通用协作纪律见 `collab-method.md`。
> v1.0 · 2026-10-12

---

## 一、项目事实（锁死，勿擅改）

- 程序集：`NewToys.Shared/Core`、`NewToys`(=Shell→NewToys.exe)、`Module.<名>`；命名空间一律 `NewToys.*`
- 分层单向：`Shared ← Core ← Shell ← Module.*`；模块禁引 Shell、禁互引；`Shared/Core` 不引 Avalonia
- 栈：.NET 10 / Avalonia 12.1.3 / CommunityToolkit.Mvvm 8.4.2 / MEDI 9.0.0 / Microsoft.Data.Sqlite 9.0.0；版本只在 `src/Directory.Packages.props`
- 编译绑定关闭 → XAML 不写 `x:DataType`；禁第三方 UI 库 / 图标字体
- 数据落 `%LocalAppData%\NewToys\{config,data,logs}`，`NEWTOYS_HOME` 可整根切换；路径一律 `Path.Combine`
- SQLite 唯一入口 `Core/Services/SqliteDatabase.cs`：库 = `&lt;应用根&gt;\data\newtoys.db`（data 非 config）；失败不抛（IsAvailable/LastError）
- 版本号编译时生成 `1.&lt;yyyyMMdd&gt;.&lt;HH&gt;.&lt;m0&gt;`，禁手写

---

## 二、铁律（项目特定）

1. **只改 `src/`**；不读写 Downloads；不碰项目目录外的任何位置。
2. **不代跑编译**：构建由用户本地 `pwsh -File .\scripts\build.ps1`；AI 只静态自检，⛔ 不谎称通过。

---

## 三、四份权威文档（src/docs/）路由

| 文档 | 角色 | 何时读 |
|---|---|---|
| NewToys开发需求.md | WHAT：唯一权威规格、验收基线 | 涉及功能/框架/边界，至少读相关章节 |
| NewToys开发规范.md | HOW：实现细节、类名/像素/API/易错点 | 编码前按「任务型→章节」读 |
| NewToys-项目现状与进度.md | 台账：基线 + 待办 + 已完成 | 会话开始读「一、当前基线」；执行期逐条勾待办 |
| NewToys-AI协作提示词.md | 流程：两阶段工作流 + 静态自检 | 流程权威，细则以其为准 |

---

## 四、任务型 → 规范章节

| 任务 | 读《开发规范》 |
|---|---|
| 改/加页面 | §2 §3.5 §3.6 |
| 设置类页面 | §2 §3.5 §4 |
| 列表页 | §3.6 §3.8 |
| 新增模块 | §3.4 |
| 图标/样式 | §2.1 §2.2 §2.3 |
| 主题键 | §2.2 |
| 框架层（先问用户） | 需求 §3.3 + 规范 §3.3 |
| 标签栏/树/导航 | 规范 §3.8 + 需求 §4.1~4.3 §5 |
| 热加载模块 | 规范 §3.7 |
| 接口/契约 | 规范 §3.1 §3.2 §3.8 |
| 修编译报错 | 规范 附录A |
| 纯文档改动 | — |

---

## 五、密度基线（v3.12，改样式先对这张表）

| 项 | 值 |
|---|---|
| 表格行高 | 28 |
| 表头 | 32 |
| 单元格内边距 | 8 |
| 卡片内边距 | 12 |
| 工具条 Padding | `12,6` |
| 筛选条 Padding | `12,4` |
| primary 按钮 | `12,6` |
| ghost 按钮 | `10,5` |
| 树行 | 30 |
| 页面根 Margin | `12` |
| 右详情卡宽 | 340（⛔ 别动） |

列表页约定：左键排序、右键列显隐；详情开关在工具条末位（`Button.detail-toggle`）；⛔ `Columns` 不加 `Detail` 占位列。

---

## 六、高频硬约束

- `.axaml` 改动必同步 `.axaml.cs`；编译绑定关闭 → 只写 `{Binding}`，⛔ 不写 `x:DataType`、⛔ WPF 写法。
- 颜色一律 DynamicResource；主题键只用 32 个 `*Brush` 语义键（不新增、不用旧键 `AppBg/AppBackground/TextPrimary`）；code-behind ⛔ 写死 `#RRGGBB`。
- `App.ApplyTheme` 幂等，⛔ 禁 `MergedDictionaries.Clear()`。
- 菜单：`icon` 传几何资源键（24 网格描边型，`&lt;Path Stroke=… Stretch="Uniform"/&gt;`），不是 emoji；Key = 小写连字符；viewName = View 类名。
- 跨页投递数据在 `OnAttachedToVisualTree` 取（⛔ 不是 `OnDataContextChanged`；必须 `protected override`，public 报 CS0507）。
- 设置类 `config\*.json` 模块只读，唯一写入点 = 设置页；功能数据由功能页读写。

---

## 七、分析器 / API 坑（写新代码先对照）

- `TaskStatus` 撞 `System.Threading.Tasks.TaskStatus` → 改名 `VideoTaskStatus`；`MenuGroup/MenuItem` → `AppMenuGroup/AppMenuItem`。新增模型先 grep avalonia ref 的 `T:`。
- CA1416 只认**内联** `OperatingSystem.IsWindows()`；间接属性不认。
- `Where(p=&gt;!string.IsNullOrWhiteSpace(p))` 不收窄 `string?` → 仍需 `.Select(p=&gt;p!)`。
- 插值串禁单个 `\`（CS8087+CS1009）；要反斜杠写 `\\`。
- `[ObservableProperty]` 的 `partial void OnXxxChanged` 只能写在定义类（派生→CS0759）；派生类靠基类 `protected virtual NotifyXxx()` override。
- `MVVMTK0034` 只构造/字段初始化器外报；改字段名须全仓搜（属性 + `_字段` + nameof）。
- `[RelayCommand]` 的 `Async` 后缀被剥再拼 `Command`（CopyFfmpegAsync → CopyFfmpegCommand）。
- 🔴 `CommandParameter` 字面量是 string，别喂 `RelayCommand&lt;T&gt;`（T≠string）：Avalonia 在 `CanExecute` 类型不匹配**直接抛 ArgumentException**，被绑定机制吞掉（不崩不报，但切页半途而废）。→ 参数收 `object?` 自己转。
- `SetTextAsync/GetTextAsync` 是 `Avalonia.Input.Platform.ClipboardExtensions` → 须 `using Avalonia.Input.Platform;` + `using Avalonia;` + `Avalonia.Controls.ApplicationLifetimes`。
- Avalonia≠WPF：无 IsCheckable（用 ToggleType=CheckBox）；IsChecked 须 Mode=OneWay；Watermark 废弃 → PlaceholderText。
- 🔴 **可视树回调是 `protected`**：`OnAttachedToVisualTree`/`OnDetachedFromVisualTree` 写 `public override` → CS0507。
- 🔴 **⛔ 别用 MinHeight 给 TextBlock 撑行高**：文本恒绘于元素框顶部，框被撑高文字贴顶。行高基线放 `ListBox.list-page ListBoxItem MinHeight=28`，`ListBoxItem` 还要 `Horizontal/VerticalContentAlignment=Stretch`（Fluent 默认垂直 Top）。
- **手写属性 setter 别写 `private set`**：View 常直接 `vm.StatusText = "…"` 报状态 → 写 private 即 CS0272。
- 🔴 **表头勾选一律用原生 CheckBox 三态**（点=切换全选、右键=菜单），⛔ 别用字符 "☑" 画（字体渲染成深色块，与行内框不搭）。
- 给列表插首列 = `Columns` 加 `Pick("34")` + 表头/行模板**所有 `Grid.Column` 索引 +1**。

---

## 八、触发词

NewToys、src/、视频工具、PcTools、WebTools、开发需求、开发规范、台账、build.ps1
