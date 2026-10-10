# NewToys · scripts 脚本说明

仓库根目录 `scripts/` 下的构建 / 运行 / 发布 / 打包 / 源码交换脚本，**统一用 PowerShell 7（`pwsh`）执行**。

> 所有脚本都把「仓库根」判定为**它们所在目录的上一级**（即能看到 `src/` 的那一级），
> 因此你在哪个目录敲 `pwsh` 都能跑，但建议在仓库根执行，示例统一写 `pwsh ./scripts/xxx.ps1`。

---

## 一、通用约定

| 项 | 约定 |
|---|---|
| 唯一解决方案 | `src\NewToys.slnx`（根目录那份旧 `NewToys.slnx` 是历史副本，忽略/可删） |
| 模块名 | `Module.PcTools` / `Module.WebTools` / `Module.VideoTools` / `Module.Settings`；脚本里传**不带 `Module.` 前缀**的名字：`PcTools` |
| 产物区 | 全部落在 `artifacts\`（已 `.gitignore`），**源码区 `src\` 保持纯净** |
| 版本号 | 编译时由 `src\Directory.Build.props` 自动生成，**不要手写** |
| 零拷贝铁律 | `Modules\` 是模块产物的唯一去处，由 MSBuild `OutDir` **直出**；⛔ 任何 csproj / 脚本都不许再加「把 DLL 拷进 `Modules\`」的步骤 |
| 用户数据 | `%LocalAppData%\NewToys\{config,data,logs}`，可用环境变量 `NEWTOYS_HOME` 整根切换 |

---

## 二、脚本速查

| 脚本 | 用途 | 典型命令 |
|---|---|---|
| `build.ps1` | **全量**还原 + 编译（Debug/Release） | `pwsh ./scripts/build.ps1 -Configuration Release` |
| `build-module.ps1` | **只编指定模块**（可多选） | `pwsh ./scripts/build-module.ps1 -Module VideoTools` |
| `clean.ps1` | 全量清理（`dotnet clean`，可选 `-Deep`/`-Data`） | `pwsh ./scripts/clean.ps1 -All` |
| `clean-module.ps1` | **只清指定模块**的产物（可多选） | `pwsh ./scripts/clean-module.ps1 -Module VideoTools` |
| `run.ps1` | 构建并启动（可 `-NoBuild` 直接跑） | `pwsh ./scripts/run.ps1 -Configuration Debug -Env test` |
| `env.ps1` | 查看 / 切换 测试·生产应用根 | `pwsh ./scripts/env.ps1 test` |
| `publish.ps1` | 发布到目标目录（RID / 自包含可选） | `pwsh ./scripts/publish.ps1 -Target D:\NewToys` |
| `pack.ps1` | 打源码快照 md（喂 AI / 转发） | `pwsh ./scripts/pack.ps1` |
| `export-framework.ps1` | 导出框架层源码 → md | `pwsh ./scripts/export-framework.ps1` |
| `export-module.ps1` | 导出单个模块源码 → md（交互多选） | `pwsh ./scripts/export-module.ps1` |
| `import-sources.ps1` | 把快照 md 反向还原到 `src\` | `pwsh ./scripts/import-sources.ps1 -DryRun` |

> 仓库根还有一个 `apply-patch.ps1`（补丁应用，不在本目录）；`NewToys.exe - 快捷方式.lnk` 是启动快捷方式，不是脚本。

---

## 三、两种工作流

### A. 密集更新期（改动大）—— 全量

```powershell
pwsh ./scripts/clean.ps1 -All           # 全清（artifacts/ production/ 所有 bin obj + 用户数据）
pwsh ./scripts/build.ps1                # 全量 Debug
pwsh ./scripts/build.ps1 -Configuration Release
```

### B. 稳定期（只改个别模块）—— 单模块

工程稳定后，通常只有一两个模块在动，不必每次全清全编：

```powershell
# 交互选择要编的模块（可多选）
pwsh ./scripts/build-module.ps1
# 或直接指定；Release 直接产出到 Modules\
pwsh ./scripts/build-module.ps1 -Module VideoTools -Configuration Release
# 只想清某个模块
pwsh ./scripts/clean-module.ps1 -Module VideoTools
```

`build-module.ps1` / `clean-module.ps1` 都支持**交互选择**（同 `export-module.ps1`：输入 `1` / `1,3` / `1-3` / `A`=全部 / 回车=取消），也支持 `-Module A,B`、`-All` 脚本化调用。

---

## 四、全量脚本（`build.ps1` / `clean.ps1`）

### `build.ps1`

```powershell
pwsh ./scripts/build.ps1 [-Configuration Debug|Release] [-Restore]
```

- 只做「还原（`-Restore`）+ 编译 + 结果核对」，**不做任何 DLL 拷贝**。
- `-Restore` 仅在「清过 NuGet 缓存 / 改过包版本」时需要；平时省略。
- Release 构建后会自动核对 `artifacts\release\Modules\` 里模块 DLL 数量是否与 `src\Module.*` 目录数一致，不符会告警。

### `clean.ps1`

```powershell
pwsh ./scripts/clean.ps1 [-Deep] [-Data] [-All] [-WhatIf]
```

| 开关 | 作用 |
|---|---|
| （无） | 轻度：`dotnet clean` + 清仓库根残留的 `Modules\` |
| `-Deep` | 深度：`artifacts\`、`production\` 及仓库内所有 `bin\`/`obj\` |
| `-Data` | 清用户数据 `var\logs`、`var\cache`、`var\temp`（**保留 `var\config`**） |
| `-All` | = `-Deep` + `-Data` |
| `-WhatIf` | 只预览要删的路径，不实际删除 |

---

## 五、单模块脚本（`build-module.ps1` / `clean-module.ps1`）

### `build-module.ps1`

```powershell
pwsh ./scripts/build-module.ps1 [[-Module] <名[,名...]>] [-All] [-Configuration Debug|Release] [-Restore] [-NoShellSync]
```

- 只 `dotnet build` 所选**模块项目**；其依赖的 `Shared` / `Core` 会随 ProjectReference 自动带上一起编译。
- 不带 `-Module` 也不带 `-All` → 弹出交互菜单多选。
- **产物落点**（由 `src\Directory.Build.props` 决定，脚本不拷贝进 `Modules\`）：

  | 配置 | 产物位置 |
  |---|---|
  | `Debug` | `artifacts\bin\Module.<名>\Debug\net10.0\Module.<名>.dll` |
  | `Release` | `artifacts\release\Modules\Module.<名>.dll`（MSBuild 直出） |

- ⚠️ **Debug 的补充动作**：Debug 下宿主 `NewToys.exe` 是用「项目引用」静态加载模块的，模块 DLL 必须在宿主目录 `artifacts\bin\Shell\Debug\net10.0\` 里才会生效。所以脚本在 Debug 编译成功后，会把新产物（本模块 DLL + `Shared`/`Core`）**同步进宿主目录**，否则会出现「改了没生效 / 页面一直是旧的」。用 `-NoShellSync` 可关掉；宿主目录不存在时会提示先跑一次 `build.ps1`。
  （Release 不需要这步 —— 宿主运行时从 `Modules\` 目录扫描加载。）

### `clean-module.ps1`

```powershell
pwsh ./scripts/clean-module.ps1 [[-Module] <名[,名...]>] [-All] [-Configuration Debug|Release] [-KeepHostCopy] [-WhatIf]
```

- 只清所选模块：`dotnet clean` + 删 `artifacts\obj\Module.<名>\`、对应输出目录/文件，以及 Debug 下宿主目录里的模块副本。
- `-KeepHostCopy`：Debug 下保留宿主目录副本（默认一并删除）。
- `-WhatIf`：只预览，不删除。
- ⛔ 不动其他模块、不碰 `artifacts\release\NewToys.exe` 等宿主产物。

---

## 六、运行 / 环境 / 发布

### `run.ps1`

```powershell
pwsh ./scripts/run.ps1 [-Configuration Debug|Release] [-Env test|prod] [-NoBuild]
```

- 默认 `Release`，会先 `build.ps1` 再启动；`-NoBuild` 跳过编译直接跑。
- `-Env test` **只影响本次进程**：把 `NEWTOYS_HOME` 指到 `artifacts\env\test`（不污染用户级环境变量）。

### `env.ps1`

```powershell
pwsh ./scripts/env.ps1          # 查看当前 NEWTOYS_HOME
pwsh ./scripts/env.ps1 test     # 切到测试根 artifacts\env\test（用户级持久）
pwsh ./scripts/env.ps1 prod     # 清掉 NEWTOYS_HOME，回到 %LocalAppData%\NewToys
```

整根切换 `config\ / data\ / logs\`，**不做数据迁移**；新开的进程才读到新值。

### `publish.ps1`

```powershell
pwsh ./scripts/publish.ps1 [-Runtime win-x64|linux-x64|osx-x64] [-Target <目录>] [-SelfContained]
```

- 不依赖 `dotnet publish`：直接把 Release 输出重定向到目标目录（`/p:NewToysReleaseDir=<目标>\`，**必须带结尾分隔符**），模块 DLL 由 MSBuild 自己落进 `<目标>\Modules\`。
- 默认目标 `production\NewToys_<Runtime>`。

---

## 七、源码交换（喂 AI / 转发）

| 脚本 | 说明 |
|---|---|
| `pack.ps1` | 默认导出**框架层**快照到 `artifacts\context\NewToys-<时间戳>.md`（内部转调 `export-framework.ps1`）；`-Module <名>` 可只导某模块 |
| `export-framework.ps1` | 导出 `Shared`+`Core`+`Shell`+根配置 → md，默认 `sources\framework.md`；`-List` 只列不写 |
| `export-module.ps1` | 导出单个/多个模块源码 → md，默认 `sources\module-<小写名>.md`；`-List` 只列，交互多选同前 |
| `import-sources.ps1` | 把快照 md（`FILE: <相对路径>` 段）反向还原到 `src\`；`-DryRun` 预览，`-NoBackup` 跳过备份（默认备份到 `src\.bak-<时间戳>\`），内置路径穿越校验 |

```powershell
pwsh ./scripts/pack.ps1                          # 框架快照
pwsh ./scripts/export-module.ps1 -ModuleName Settings
pwsh ./scripts/import-sources.ps1 -File sources\module-settings.md -DryRun
```

---

## 八、产物路径

| 产物 | 路径 |
|---|---|
| Debug 主程序 | `artifacts\bin\Shell\Debug\net10.0\NewToys.exe` |
| Debug 单模块 DLL | `artifacts\bin\Module.<名>\Debug\net10.0\Module.<名>.dll` |
| Release 输出根 | `artifacts\release\`（`/p:NewToysReleaseDir=<dir>\` 可覆盖） |
| Release 模块 DLL | `artifacts\release\Modules\Module.*.dll`（**直出，零拷贝**） |
| 中间产物 | `artifacts\obj\<项目名>\` |
| 发布产物 | `production\NewToys_<运行时>\` |
| 测试环境根 | `artifacts\env\test\` |
| 用户数据 | ⚠️ 不在仓库内：`%LocalAppData%\NewToys\{config,data,logs}` |

---

## 九、常见坑

- **Debug 下用 build-module 编完仍看到旧界面**：说明宿主目录里的模块 DLL 没更新。脚本默认已自动同步；若你用了 `-NoShellSync`，改回默认或跑一次 `build.ps1`。
- **只编了模块但改了 `Shared`/`Core`**：`build-module.ps1` 会带上依赖一起编，并在 Debug 下同步进宿主目录；Release 下如需宿主也刷新，跑一次 `build.ps1 -Configuration Release`。
- **`Modules\` 里多出 `NewToys.Shared.dll` / `NewToys.Core.dll`**：正常现象，加载器会跳过同名程序集（见 `Directory.Build.props` 注释）。
- **`dotnet clean` 退出码非 0**：多半是该项目还没构建过，可忽略（脚本已按警告处理）。
- **本机文档差异提醒**：仓库根的 `README-构建说明.md` 是早期版本，里面的 `-Release` 写法与「自动复制模块 DLL 到 Modules」描述已过时——`build.ps1` 当前参数是 `-Configuration Release`，且模块 DLL 为 MSBuild 直出、无拷贝步骤。
