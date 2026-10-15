# NewToys 技术栈硬伤清单（tech-guard）

> **版本锚点**：.NET 10 / Avalonia **12.1.4** / CommunityToolkit.Mvvm 8.4.2 / MEDI 9.0.0 / Microsoft.Data.Sqlite 9.0.0
> **口径**：硬伤 = **框架 / 版本层面**的坑（Avalonia ≠ WPF、分析器行为、API 缺失、Win32 与客户区扩展的平台差异）。业务需求不在此。
> **定位**：本文件是**技术栈硬伤的唯一源**；`G-xx` 与 `scripts/check-techstack.ps1` 的规则**一一对应**（改这张表必须同步改脚本，反之亦然）。
> ⚠️ 已知重叠：`references/newtoys-spec.md` §七「分析器 / API 坑」与本文件内容重复（收敛为引用属后续动作，本轮未执行）。
> 🔴 **升级 Avalonia / .NET 后必须复核本文件**：标「版本相关 = 是」的条目会随升级失效甚至反转，不复核这里就会变成谎言。
> 🔴 本文件**不替代编译器**。三条脚本只是「编译前抓常见硬伤」，最终门禁仍是用户本地 `pwsh -File .\scripts\build.ps1`。

---

## 一、静态可检测规则（脚本 `G-01` ~ `G-14`）

级别口径：**ERROR** = 会编译失败 / 运行必错 / 破坏架构边界；**WARN** = 违反项目约定，但仍能编译。

| ID | 级别 | 硬伤现象 | 正确做法 | 版本相关 |
|---|---|---|---|---|
| **G-01** | WARN | `.axaml` 里硬编码颜色，如 `Foreground="#FFB300"` | 改用 `{DynamicResource XxxBrush}` 语义键。**仅查 `.axaml`**：`.cs` 侧的主题定义（`Shell/App.axaml.cs` 的 `ApplyTheme`）与调色板（`AccentPalette` / `AssetPalette`）本就带字面量，属合法；定义了 `x:Key="*Brush"` 的资源文件一律放行 | 否 |
| **G-02** | ERROR | `MergedDictionaries.Clear()` | `App.ApplyTheme` 必须**幂等**；清空会连带丢掉 Fluent 内置样式 | 否 |
| **G-03** | WARN | `<TextBlock MinHeight="…">` 用来撑行高 | 文本恒绘于**元素框顶部**，框被撑高则文字贴顶。行高基线放 `ListBoxItem MinHeight=28` + `Horizontal/VerticalContentAlignment=Stretch` | 否 |
| **G-04** | ERROR | `public override void OnAttachedToVisualTree / OnDetachedFromVisualTree` | 可视树回调是 `protected`；写 `public` 直接 **CS0507**。跨页投递数据就在 `OnAttachedToVisualTree` 取（⛔ 不是 `OnDataContextChanged`） | 否 |
| **G-05** | ERROR | `.axaml` 里用字符 `☑` / `☐` / `▣` 直接当勾选框（`Text="☑"`、`Content="☑"`） | 表头勾选一律**原生三态 CheckBox**（点 = 切换全选、右键 = 菜单）。⚠️ 菜单项文案 `Header="☑ 全选"` 属既有约定，**不报** | 否 |
| **G-06** | ERROR | `FontFamily` 指向图标字体（FontAwesome / FluentSystemIcons / Segoe MDL2 / Material Icons / iconfont / Octicons / Tabler …） | 全项目只用默认 Inter；图标走 **`Path` + `StreamGeometry` 资源键**（24×24 网格）。⛔ 禁图标字体、⛔ 禁 emoji / 字符当图标 | 是 |
| **G-07** | ERROR | 类名撞框架类型（`TaskStatus` / `MenuGroup` / `MenuItem` / `Color` / `Path` / `Border` / `Window` …） | 改名（`VideoTaskStatus` / `AppMenuGroup` / `AppMenuItem` …）；新增模型先 grep Avalonia ref 程序集确认不撞 | 是 |
| **G-08** | ERROR | `.csproj` 对 `defaults\` 用 `Include=` | 必须 `None Update="defaults\**" CopyToOutputDirectory="PreserveNewest"`；用 `Include` 会 **NETSDK1022** 重复项 | 否 |
| **G-09** | WARN | `.axaml` 里出现 `x:DataType` | 编译绑定**全工程关闭**（各 csproj `AvaloniaUseCompiledBindingsByDefault=false`），只写 `{Binding Xxx}` | 是 |
| **G-10** | ERROR | `Module.*` 引 `Shell`，或模块之间互引 | 分层单向：`Shared ← Core ← Shell ← Module.*`；模块只准引 Shared + Core | 否 |
| **G-11** | ERROR | `Shared` / `Core` 的 csproj 引 Avalonia 包 | 这两层**不引 Avalonia**（保持可复用、与 UI 无关） | 否 |
| **G-14** | ERROR | 插值串 `$"…"` 里出现**单个** `\`（如 `$"C:\path"`） | CS8087 + CS1009。要反斜杠写 `\\`；`\n` / `\t` / `\"` / `\\` 等合法转义不报；`$@"…"` verbatim 插值串跳过 | 否 |

---

## 二、⛔ 不可静态检测的硬伤

### 2.1 只有编译器能抓（⇒ 用户本地 `build.ps1` 是唯一可信门禁）

- **CA1416** 只认**内联** `OperatingSystem.IsWindows()`，间接属性（如 `IsWindows => OperatingSystem.IsWindows()`）不认。
- `Where(p => !string.IsNullOrWhiteSpace(p))` **不收窄** `string?` → 仍需 `.Select(p => p!)`。
- `[ObservableProperty]` 的 `partial void OnXxxChanged` **只能写在定义类**（写在派生类 → CS0759）；派生类要挂钩子就在基类加 `protected virtual NotifyXxx()` 并 override。
- **MVVMTK0034**：只在构造函数 / 字段初始化器之外报；改字段名须全仓搜（属性 + `_字段` + `nameof`）。
- `[RelayCommand]` 的 `Async` 后缀会被剥掉再拼 `Command`（`CopyFfmpegAsync` → `CopyFfmpegCommand`）。
- 🔴 `CommandParameter` 字面量是 **string**，别喂 `RelayCommand<T>`（T ≠ string）：Avalonia 在 `CanExecute` 类型不匹配时**直接抛异常且被绑定机制吞掉** —— 不崩不报，但功能静默失效。→ 参数收 `object?` 自己转。
- 手写属性 setter 别写 `private set`（View 常直接 `vm.StatusText = "…"` → **CS0272**）。
- `StringFormat` 只支持**一个**绑定。
- `SetTextAsync` / `GetTextAsync` 是 `Avalonia.Input.Platform.ClipboardExtensions` 的扩展方法 → 必须 `using Avalonia.Input.Platform;`（+ `using Avalonia;`）。
- 新增模型名先 grep Avalonia ref：`T:` 撞名是编译错，不是警告。
- `.axaml` 改动必须同步 `.axaml.cs`；`.axaml` XML 不合法 / 缺配对同属编译期。

### 2.2 只有跑起来 / 上平台才能抓

- **窗口装饰（客户区扩展 CSD）**：Win11 方角三保险（`DwmSetWindowAttribute(33,1)` + `SetWindowRgn` + 官方 `WindowCornerPreference`）；最大化 / 全屏必须 `SetWindowRgn(NULL)`；`WS_THICKFRAME|WS_CAPTION` ⛔ 别动（贴靠资格与它绑死）。
- **贴靠缝**：首次约 1px、二次约 8px（后者由 `WM_NCCALCSIZE` 零内缩消除）；残留取整缝隙**接受**，⛔ 不做外部几何纠偏。
- **DWM 阴影缺失**是客户区扩展的固有结果，与 `SetWindowRgn` 无关（开关 `UseSquareRegionHack` 对照实测结论）。
- **Win10 顶部两角圆角**是 `WS_THICKFRAME|WS_CAPTION` 的固有小圆角，非 Win11 可调 API。
- 全屏：`WindowState.FullScreen` 切换 + `F11` + 全屏态 `Esc` 退出（隧道注册 KeyDown，非全屏不吞）。
- SQLite：`ATTACH` / `DETACH` 必须在**事务外**；整数列不能用 `GetString`。
- **右键菜单（ContextMenu）在「非依附控件」上 `Open` 直接抛异常**（v3.42.1 实测崩溃）：`Control.ContextMenu` 赋值时会把宿主登记进菜单的依附列表，`ContextMenu.Open(control)` 校验 `_attachedControls.Contains(control)`，不满足即抛
  `ArgumentException: Cannot show ContentMenu on a different control to the one it is attached to.` —— 表现是"右键菜单一弹就崩"。
  ⚠️ 注意 **不是** `IsOpen` 重复打开（`Open` 内部 `if (IsOpen) return;` 是幂等的）。
  → 菜单**只挂最外层热区控件**（例：表头勾选列的整格 `Border#HeaderCheckHost`），右键由 Avalonia 原生 `ContextRequested` 弹出；⛔ 不要写 `PointerPressed` + `menu.Open(另一个控件)`，挂最外层后整格热区天然生效。
- **菜单项「还有下一级」的折叠标识**：Fluent 的 `MenuItem` 模板**自带** `Path#PART_ChevronPath`（Grid 第 5 列），显隐由 `:empty` 伪类控制 = `ItemCount == 0`（无子项）时隐藏 ⇒ **有子项就自动出现**，⛔ 别往 `Header` 里拼 `▸`/自绘 Path。
  项目只在 `Shell/Styles/Controls.axaml` ⑱ 统一它的 `Fill`（`PrimaryTextBrush`；全局 Style 优先级高于 ControlTheme ⇒ 该色对所有状态一律生效）。
- 两条死诊断：无边框自绘窗口 `Window.Title` 界面上**不显示**；`DwmGetWindowAttribute(33)` **不支持读**（永远 -1）。诊断写文件或状态栏。

---

## 三、已由其它脚本覆盖（⛔ 别在本表重复实现）

| 脚本 | 已覆盖的范围 |
|---|---|
| `scripts/check.ps1` | `.axaml` XML 合法性 / `.axaml`↔`.axaml.cs` 配对 / `*Brush` 主题键白名单（白名单外的键 = 旧键或拼错） |
| `scripts/check-axaml-props.ps1` | XAML **属性名存在性**（逐个到 Avalonia ref 程序集核对 + 本仓自定义属性豁免），专治 Avalonia ≠ WPF 名字差异：`SelectedValuePath` / `SelectedValue` / `StrokeLineJoin`（Avalonia 叫 `StrokeJoin`）/ `Watermark`（→ `PlaceholderText`）/ `DisplayMemberPath` / `SharedSizeGroup` / `ValidatesOnExceptions` / `IsCheckable`。另：勾选按钮 ⛔ 无 `IsCheckable`（用 `ToggleType=CheckBox`），`IsChecked` 本项目约定须 `Mode=OneWay` |

---

## 四、用法（仓库根执行，交付前跑）

```powershell
pwsh skill/scripts/check.ps1
pwsh skill/scripts/check-axaml-props.ps1
pwsh skill/scripts/check-techstack.ps1          # 加 -All 看 WARN 明细
```

三条都**只报告、不改文件、不以非零码退出**（自检是参考，不是门禁）；最终以用户本地 `pwsh -File .\scripts\build.ps1` 为准。

---

## 五、编译错误速查（按错误码索引）

🔴 **回填纪律**：本轮只要出现本地编译失败，修复后**必须把「错误码 + 根因 + 一行修法」追加进本表** —— AI 不代跑编译，编译期知识只能从用户侧回流；不回填 = 同一个坑反复踩。（《AI协作手册》[4]/[6] 已写入这条）

| 错误码 | 含义 | 根因 → 修法 |
|---|---|---|
| `CS0507` | 重写成员的访问修饰符不匹配 | 可视树回调必须 `protected override`（**G-04**） |
| `CS0272` | setter 不可访问 | 手写属性写了 `private set`，而 View 直接 `vm.Xxx = "…"` 报状态 → 改 public set 或收进方法 |
| `CS0759` | 找不到分部方法的定义声明 | `partial void OnXxxChanged` 写在了派生类 → 移回定义类；派生类挂钩子在基类加 `protected virtual NotifyXxx()` 并 override |
| `CS8087` / `CS1009` | 插值串转义非法 / 无法识别的转义序列 | 插值串里单个 `\` → 写 `\\`（**G-14**）；非插值串 `"C:\path"` 同样报 CS1009 |
| `CA1416` | 平台兼容性分析器 | 只认**内联** `OperatingSystem.IsWindows()`；间接属性（`IsWindows => …`）不认 |
| `MVVMTK0034` | 直接访问 backing field | 只在构造函数 / 字段初始化器内允许；改字段名须全仓搜（属性 + `_字段` + `nameof`） |
| `AVLN2000` | Avalonia XAML 编译失败 | 九成是 WPF 专有属性名（`SelectedValuePath` / `StrokeLineJoin` / `Watermark` / `DisplayMemberPath` / `SharedSizeGroup`）→ 先跑 `check-axaml-props.ps1`（**§三**） |
| `NETSDK1022` | 重复的编译项 | `defaults\` 用了 `Include=` → 改 `None Update="defaults\**"`（**G-08**） |
| `CS0103` | 找不到名称 | 删了带 `x:Name` 的 XAML 块（如迁走的页内工具条）但 code-behind 仍引用那个字段 ⇒ 全仓搜该 `x:Name`，把 code-behind 里的接线一并删掉。🔴 实例（NT-055）：`BtnImport` 随工具条一起删了，`BookmarkView` / `PingView` 的构造器还在 `BtnImport.Click += …` |
| `MVVMTK0023` | 命令方法在类型内不唯一 | 新写的 `[RelayCommand] XxxAsync()` 与**同类型既有**方法**同名**（`Xxx` 与 `XxxAsync` 都生成 `XxxCommand`）⇒ 给新方法换名（如加 `Pick` 前缀）并同步描述符 / XAML 里的 `CommandPath`。🔴 实例（NT-055）：`AssetAllViewModel` 已有 `SaveLibraryAsAsync(string?)`，新加的选路径版同名 ⇒ 改成 `PickSaveLibraryAsAsync`（内部再调既有的 `SaveLibraryAsCommand.Execute(path)`） |
| `CS0067` | 事件从未使用 | 不继承 `ListPageViewModelBase` 的自定义 VM（如 `VideoPreviewViewModel`）自己声明了 `ToolbarChanged` 却没有 `?.Invoke` 点 ⇒ 补一个 `private void NotifyToolbarChanged() => ToolbarChanged?.Invoke(this, EventArgs.Empty);`（继承基类的页不用管，基类已自带该辅助方法） |
| `CS8619` | 空性与目标类型不匹配 | 🔴 实例（NT-055）：`items.ItemsPanel = new FuncTemplate<Panel>(…)` —— `ItemsControl.ItemsPanel` 的目标类型是 **`ITemplate<Panel?>`** ⇒ 类型参数必须写 **`FuncTemplate<Panel?>`**（写 `Panel` 只警告不报错，但污染输出）。同类坑：凡目标是 `ITemplate<X?>` / `Func<X?>`，泛型实参都要带 `?` |
| `CS0246` / `CS1061` | 找不到类型 / 找不到成员 | 先跑 `check-axaml-props.ps1`；再核命名空间与引用方向（**G-10** / **G-11**）。🔴 实例（v3.43 编译轮修）：`Core/Services/ModuleReloadService.cs` 实现 Shared 契约 `IModuleReloadService` 但漏 `using NewToys.Shared.Services;` —— `NewToys.Core.Services` 与 `NewToys.Shared.Services` 是 `NewToys` 下的**兄弟命名空间，不互达** ⇒ 报在类声明行（CS0246@`: IXxx`）。修法 = 补一行 using；「新建 Core 服务类实现 Shared 接口」时**先抄同目录现役服务的 using 头** |

**无错误码、但会静默失效（编译器抓不到 —— 优先查这里）**：

- `CommandParameter` 传了字符串字面量，而对应命令是 `RelayCommand<T>`（T ≠ string）→ Avalonia 在 `CanExecute` 抛 `ArgumentException`，被绑定机制**吞掉**；表现是"点了没反应 / 切页半途而废"。参数一律收 `object?` 自己转。
  ⚠️ 本项目列表页有 **150+ 处** `CommandParameter="列名"` 属**合法标准模式**（`SortCommand` / `ToggleColumnCommand` 都是 `object?`）⇒ **无法静态判定**，只在出新命令时留意。
- 列键 ≠ 属性名且没显式写进 `FieldText` → 筛选**静默失效**。
- 🔴 `ItemsControl` 改绑 `ItemsSource` / 抽模板到资源时**丢了原 `ItemsPanel`** → 回落默认**竖排** `StackPanel`；配 `VerticalScrollBarVisibility="Disabled"` 的横向滚动区 = **只显示第一项**（v3.44 标签栏踩过：新增标签只显示第一个，编译不报）⇒ 横排列表必须显式写 `<ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>`。
- 绑定路径写错（编译绑定全工程关闭）→ 运行期才报，且只在输出窗口里。

**已被实测否决的「疑似静态规则」（别再尝试静态化）**：

| 设想规则 | 否决理由 |
|---|---|
| `CommandParameter="字面量"` → 报错 | 全仓 150+ 处合法用法（列表页标准模式），误报率 100% |
| `private set` 手写属性 → 报错 | ~14 处命中，Core 服务 / Model 的合法封装与 VM 违规混杂，静态不可区分 ⇒ 交给编译器（CS0272） |
