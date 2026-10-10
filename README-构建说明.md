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
