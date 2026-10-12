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
pwsh ./scripts/build.ps1 -Release

# 3. Release运行
pwsh ./scripts/run.ps1 -Release
```

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
