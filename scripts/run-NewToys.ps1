#Requires -Version 7.0
<#
.SYNOPSIS
    直接运行已构建的 NewToys（不自动编译）；产物不存在时提醒先构建。

.DESCRIPTION
    与 run.ps1（构建并启动）不同，本脚本只「直接运行」，不做任何编译：
    - 默认 Debug：artifacts\bin\Shell\Debug\net10.0\NewToys.exe
    - -Configuration Release：artifacts\release\NewToys.exe
    若对应 exe 不存在，提示先用 build.ps1 / run.ps1 构建，并以退出码 1 结束（不抛异常、不自动编译）。

.PARAMETER Configuration
    Debug（默认）/ Release。
.PARAMETER Env
    test / prod：仅本次进程把 NEWTOYS_HOME 指向测试根 / 清掉（同 run.ps1）。

.EXAMPLE
    pwsh ./scripts/run-NewToys.ps1
    直接运行 Debug 产物。

.EXAMPLE
    pwsh ./scripts/run-NewToys.ps1 -Configuration Release
    直接运行 Release 产物。

.EXAMPLE
    pwsh ./scripts/run-NewToys.ps1 -Env test
    用测试应用根运行 Debug 产物。
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("", "test", "prod")]
    [string]$Env = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

# -Env 只作用于本进程（测试不需要污染用户级环境变量）
if ($Env -eq "test") {
    $testRoot = Join-Path $RepoRoot "artifacts\env\test"
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $env:NEWTOYS_HOME = $testRoot
    Write-Host "应用根隔离到: $testRoot" -ForegroundColor DarkGray
}
elseif ($Env -eq "prod") {
    Remove-Item Env:NEWTOYS_HOME -ErrorAction SilentlyContinue
}

$exe = if ($Configuration -eq "Release") {
    Join-Path $RepoRoot "artifacts\release\NewToys.exe"
}
else {
    Join-Path $RepoRoot "artifacts\bin\Shell\Debug\net10.0\NewToys.exe"
}

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host ""
    Write-Host "✋ 未找到可执行文件: $exe" -ForegroundColor Yellow
    Write-Host "   请先构建 $Configuration 版本：" -ForegroundColor Gray
    Write-Host ("     pwsh ./scripts/build.ps1 -Configuration " + $Configuration) -ForegroundColor Cyan
    Write-Host "   或  pwsh ./scripts/run.ps1 -Configuration $Configuration  （构建并启动）" -ForegroundColor Cyan
    Write-Host ""
    exit 1
}

Write-Host "==> 启动 $exe" -ForegroundColor Cyan
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
