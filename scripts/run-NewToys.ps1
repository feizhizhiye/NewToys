#Requires -Version 7.0
<#
.SYNOPSIS
    直接运行已构建的 NewToys（不自动编译）；产物不存在时提醒先构建。

.DESCRIPTION
    与 run.ps1（构建并启动）不同，本脚本只「直接运行」，不做任何编译：
    - 默认 Debug：artifacts\bin\Shell\Debug\net10.0\NewToys.exe
    - -Configuration Release：artifacts\release\NewToys.exe
    静默启动优先：exe 存在则直接拉起并退出，不残留提示。仅当 exe 不存在或启动失败时，
    才打印原因并暂停（Read-Host）等待人工确认，随后以退出码 1 结束（不抛异常、不自动编译）。

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
trap { Write-Host ("❌ 执行出错: " + $_) -ForegroundColor Red; Read-Host "按 Enter 关闭窗口"; exit 1 }
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
    Read-Host "按 Enter 键退出"   # 暂停以便阅读，随后窗口随启动方式关闭
    exit 1
}

Write-Host "==> 启动 $exe" -ForegroundColor Cyan
try {
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -ErrorAction Stop
}
catch {
    Write-Host "✋ 启动失败：$_" -ForegroundColor Red
    Read-Host "按 Enter 键退出"
    exit 1
}
# 成功：直接返回，窗口随启动方式自动关闭
exit 0
