#Requires -Version 7.0
<#
    NewToys 统一构建脚本（对齐 WPF 版 scripts\build.ps1 的约定）
    ------------------------------------------------------------------
    · 只做「还原 + 编译 + 输出结果核对」，不做任何 DLL 拷贝。
    · Release 的模块 DLL 由 src\Directory.Build.props 直接产出到
      artifacts\release\Modules\ ——⛔ 脚本里禁止再加任何拷贝步骤
      （那正是「页面一直是旧的」这类事故的根因）。
      NT-062：Module.Settings 已撤销（应用级设置归 Shell）⇒ 所有 Module.* 一律进 Modules\。
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    # 目标 RID；省略则用 Directory.Build.props 的默认 win-x64。
    # 例：-Runtime linux-x64 / osx-x64 / win-arm64
    [string]$Runtime = "",
    # 自包含：带上 .NET 运行时，目标机无需预装 .NET 10。默认关（框架依赖）。
    [switch]$SelfContained,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
trap { Write-Host ("❌ 执行出错: " + $_) -ForegroundColor Red; Read-Host "按 Enter 关闭窗口"; exit 1 }

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Sln      = Join-Path $RepoRoot "src\NewToys.slnx"
$SrcDir   = Join-Path $RepoRoot "src"

Write-Host "==> 配置: $Configuration" -ForegroundColor Cyan
Write-Host "==> 解决方案: $Sln"

if ($Restore) {
    Write-Host "`n==> 还原 NuGet 包..." -ForegroundColor Cyan
    dotnet restore $Sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore 失败" }
}

Write-Host "`n==> 编译..." -ForegroundColor Cyan
$buildArgs = @($Sln, "-c", $Configuration, "--nologo", "-v", "q")
if ($Runtime) {
    Write-Host "    目标平台: $Runtime" -ForegroundColor Cyan
    $buildArgs += "-r"
    $buildArgs += $Runtime
    if (-not $SelfContained) {
        $buildArgs += "-p:SelfContained=false"
    }
}
& dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

Write-Host "`n编译完成（以 build 输出的 error/warning 计数为准）。" -ForegroundColor Green

if ($Configuration -eq "Release") {
    $release = Join-Path $RepoRoot "artifacts\release"
    $modules = Join-Path $release "Modules"
    $exe     = Join-Path $release "NewToys.exe"
    Write-Host "`nRelease 产物: $release" -ForegroundColor Green
    if (Test-Path $exe) {
        Write-Host "  主程序: NewToys.exe" -ForegroundColor Gray
    }

    if (Test-Path $modules) {
        $dlls = @(Get-ChildItem $modules -Filter "Module.*.dll" -File)
        Write-Host "  模块 DLL: $($dlls.Count) 个 → Modules\" -ForegroundColor Gray
        # NT-062：Module.Settings 已撤销 ⇒ 所有 Module.* 都进 Modules\，期望数 = 全部模块目录数。
        $expected = @(Get-ChildItem $SrcDir -Directory -Filter "Module.*").Count
        if ($expected -gt 0 -and $dlls.Count -ne $expected) {
            Write-Host "  [警告] 期望 $expected 个功能模块 DLL，实际 $($dlls.Count) 个" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  [警告] 未生成 Modules\ —— 请确认是从解决方案（而非单个项目）构建的" -ForegroundColor Yellow
    }
}
else {
    Write-Host "`nDebug 产物: $(Join-Path $RepoRoot 'artifacts\bin\Shell\Debug\net10.0')" -ForegroundColor Green
}

exit 0
