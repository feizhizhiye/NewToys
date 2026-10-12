# NewToys v0.1.7 构建说明

## Debug / Release 模式区别

| 模式 | 模块加载方式 | 编译速度 | 特点 |
|------|-------------|---------|------|
| **Debug** (默认) | 硬编码项目引用 | 慢一点（全部编译） | ✅ 支持断点、热重载<br>✅ 注释不需要的模块即可跳过编译 |
| **Release** | 扫描 `Modules/` 目录DLL | 快（Shell不引用模块项目） | ✅ 适合分发发布<br>✅ 新增模块无需改Shell代码 |

## 快速开始（Windows）

```powershell
# 1. Debug调试运行（默认）
pwsh ./scripts/run.ps1

# 2. Release构建（自动复制模块DLL到Modules/）
pwsh ./scripts/build.ps1 -Configuration Release

# 3. Release运行（构建并启动）
pwsh ./scripts/run.ps1 -Configuration Release
```

> 若只想**直接运行已构建**的产物（不触发编译，缺失时提醒先构建），用 `run-NewToys.ps1`：
>
> ```powershell
> # 直接运行 Debug 产物（默认）
> pwsh ./scripts/run-NewToys.ps1
>
> # 直接运行 Release 产物
> pwsh ./scripts/run-NewToys.ps1 -Configuration Release
> ```
>
> 它与 `run.ps1`（构建并启动）的区别：永远不编译，仅当 `NewToys.exe` 不存在时提示先构建并以退出码 1 结束。

## 目标平台（默认 win-x64）

`src/Directory.Build.props` 已锁定默认只产出 **Windows x64** 且为**框架依赖**（`RuntimeIdentifier=win-x64` + `SelfContained=false`）：

- 原生资产（Skia / HarfBuzz / SQLite 等）只拷 `runtimes/win-x64`，不再拷贝 linux / osx / 其他架构。
- 运行依赖本机已装的 **.NET 10 桌面运行时**（系统默认已具备）。

跨平台 / 其他架构构建用 `-Runtime`（仍是框架依赖，目标机也需装 .NET 10）：

```powershell
# Debug 构建 linux-x64
pwsh ./scripts/build.ps1 -Runtime linux-x64

# Release 构建 osx-x64（自包含，目标机无需预装 .NET）
pwsh ./scripts/build.ps1 -Configuration Release -Runtime osx-x64 -SelfContained
```

> `-Runtime` 只覆盖本次构建的 RID，不动 `Directory.Build.props` 的默认 win-x64。
> 跨平台产物需在对应系统运行；本机 Windows 调试用不带 `-Runtime` 的默认命令即可。

## Release 构建（全量 / 指定模块 两份）

与 Debug / clean 一致，Release 也分两种入口：

| 入口 | 作用 | 命令 |
|------|------|------|
| `release.ps1` | **全量** Release（所有模块） | `pwsh ./scripts/release.ps1` |
| `release-module.ps1` | **指定模块** Release（直出 `artifacts\release\Modules\`） | `pwsh ./scripts/release-module.ps1 -Module VideoTools` |

- 二者都是对 `build.ps1` / `build-module.ps1 -Configuration Release` 的薄封装，不复制构建逻辑。
- 当前平台限定 win-x64（框架依赖，运行机需 .NET 10）；跨平台 Release 以后再说。
- `release-module.ps1` 支持 `-All`（全部模块）、`-Restore`、`-Module A,B` 多选，参数与 `build-module.ps1` 对齐。

## 减少Debug编译时间

打开 `src/Shell/Shell.csproj`，注释掉不需要调试的模块：

```xml
<!--
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <ProjectReference Include="..\Module.PcTools\Module.PcTools.csproj" />
    <ProjectReference Include="..\Module.WebTools\Module.WebTools.csproj" />
    <ProjectReference Include="..\Module.VideoTools\Module.VideoTools.csproj" />
    <ProjectReference Include="..\Module.Settings\Module.Settings.csproj" />
</ItemGroup>
-->
```
注释后Debug编译就会跳过这些模块，速度大幅提升。

## Git使用

```powershell
# 在工程根目录初始化git
git init
git add .
git commit -m "init: NewToys v0.1.7"
```

所有生成物（artifacts/ production/ Modules/*.dll var/ 等）都已在 `.gitignore` 中忽略，提交只会包含纯净源码。
